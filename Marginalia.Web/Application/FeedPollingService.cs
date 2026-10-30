using Marginalia.Web.Domain;
using Marginalia.Web.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Marginalia.Web.Application;

/// <summary>
/// Fetches, parses, and idempotently ingests one feed using persisted validators and due times.
/// </summary>
public sealed class FeedPollingService(
    LibraryDbContext dbContext,
    ISafePublicHttpFetcher httpFetcher,
    FeedIngestionService ingestionService,
    FeedRefreshGate refreshGate,
    TimeProvider timeProvider)
{
    private static readonly string[] SupportedXmlTypes =
    [
        "application/atom+xml",
        "application/rss+xml",
        "application/xml",
        "text/xml"
    ];

    /// <summary>
    /// Refreshes one enabled feed, serializing against scheduled and manual refresh operations.
    /// </summary>
    /// <param name="feedId">The feed subscription identifier.</param>
    /// <param name="cancellationToken">A token that cancels the operation.</param>
    /// <returns>Safe refresh health and ingestion counts.</returns>
    public async Task<FeedRefreshSummary> RefreshAsync(
        Guid feedId,
        CancellationToken cancellationToken = default)
    {
        await refreshGate.WaitAsync(cancellationToken);
        try
        {
            var feed = await dbContext.FeedSubscriptions
                .SingleOrDefaultAsync(candidate => candidate.Id == feedId, cancellationToken)
                ?? throw new KeyNotFoundException("The feed subscription no longer exists.");
            if (!feed.IsEnabled)
            {
                return Failure(feedId, "This feed is disabled.");
            }

            feed.MarkAttempt(timeProvider.GetUtcNow());
            try
            {
                await dbContext.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateConcurrencyException)
            {
                dbContext.ChangeTracker.Clear();
                return Failure(feedId, "The subscription changed before refresh could start.");
            }

            try
            {
                var response = await httpFetcher.FetchAsync(
                    new Uri(feed.FeedUrl),
                    feed.EntityTag,
                    feed.LastModifiedUtc,
                    cancellationToken);
                if (response.StatusCode == System.Net.HttpStatusCode.NotModified)
                {
                    feed.MarkSuccess(
                        timeProvider.GetUtcNow(),
                        feed.FeedTitle,
                        response.EntityTag,
                        response.LastModified,
                        notModified: true);
                    await dbContext.SaveChangesAsync(cancellationToken);
                    return Success(feedId, notModified: true, new FeedIngestionSummary(0, 0, 0, 0));
                }

                if (response.ContentType is not null
                    && !SupportedXmlTypes.Contains(response.ContentType, StringComparer.OrdinalIgnoreCase))
                {
                    throw new FeedParseException();
                }

                var document = RssFeedParser.Parse(response.Body, response.FinalUri);
                var isInitialSnapshot = !feed.InitialSnapshotUtc.HasValue
                    || (!feed.InitialCutoffUtc.HasValue && feed.InitialBoundaryOccurrenceKey is null);
                var selectedItems = isInitialSnapshot
                    ? SelectInitialItems(document.Items)
                    : SelectNewItems(feed, document.Items);
                var ingestion = await ingestionService.IngestAsync(feed.Id, selectedItems, cancellationToken);
                var refreshedAt = timeProvider.GetUtcNow();
                DateTimeOffset? initialCutoff = null;
                string? initialBoundary = null;
                if (isInitialSnapshot)
                {
                    var publishedDates = selectedItems
                        .Where(item => item.PublishedUtc.HasValue)
                        .Select(item => item.PublishedUtc!.Value)
                        .ToArray();
                    initialCutoff = publishedDates.Length == 0 ? null : publishedDates.Min();
                    initialBoundary = selectedItems.LastOrDefault()?.OccurrenceKey;
                }

                feed.MarkSuccess(
                    refreshedAt,
                    document.Title,
                    response.EntityTag,
                    response.LastModified,
                    notModified: false,
                    initialSnapshotUtc: isInitialSnapshot ? refreshedAt : null,
                    initialCutoffUtc: initialCutoff,
                    initialBoundaryOccurrenceKey: initialBoundary);
                await dbContext.SaveChangesAsync(cancellationToken);
                return Success(feedId, notModified: false, ingestion);
            }
            catch (SafeHttpFetchException exception)
            {
                return await RecordFailureAsync(feed, exception.Message, cancellationToken);
            }
            catch (FeedParseException exception)
            {
                return await RecordFailureAsync(feed, exception.Message, cancellationToken);
            }
            catch (DbUpdateConcurrencyException)
            {
                dbContext.ChangeTracker.Clear();
                return Failure(feedId, "The subscription changed during refresh.");
            }
        }
        finally
        {
            refreshGate.Release();
        }
    }

    private async Task<FeedRefreshSummary> RecordFailureAsync(
        FeedSubscription feed,
        string safeError,
        CancellationToken cancellationToken)
    {
        feed.MarkFailure(timeProvider.GetUtcNow(), safeError);
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            dbContext.ChangeTracker.Clear();
            return Failure(feed.Id, "The subscription changed during refresh.");
        }

        return Failure(feed.Id, safeError);
    }

    private static FeedRefreshSummary Success(
        Guid feedId,
        bool notModified,
        FeedIngestionSummary ingestion) =>
        new(
            feedId,
            Succeeded: true,
            notModified,
            ingestion.NewInboxArticles,
            ingestion.NewSourceOccurrences,
            ingestion.SuppressedPurgedIdentities,
            ingestion.RepeatedOccurrences,
            Error: null);

    private static FeedRefreshSummary Failure(Guid feedId, string safeError) =>
        new(feedId, false, false, 0, 0, 0, 0, safeError);

    private static FeedItemMetadata[] SelectInitialItems(IReadOnlyList<FeedItemMetadata> items) =>
        items
            .OrderByDescending(item => item.PublishedUtc ?? DateTimeOffset.MinValue)
            .Take(20)
            .ToArray();

    private static FeedItemMetadata[] SelectNewItems(
        FeedSubscription feed,
        IReadOnlyList<FeedItemMetadata> items)
    {
        var boundaryIndex = feed.InitialBoundaryOccurrenceKey is null
            ? -1
            : items.ToList().FindIndex(item =>
                item.OccurrenceKey == feed.InitialBoundaryOccurrenceKey);
        var undatedBeforeBoundary = boundaryIndex > 0
            ? items.Take(boundaryIndex)
                .Where(item => !item.PublishedUtc.HasValue)
                .Select(item => item.OccurrenceKey)
                .ToHashSet(StringComparer.Ordinal)
            : new HashSet<string>(StringComparer.Ordinal);

        if (feed.InitialCutoffUtc is { } cutoff)
        {
            return items
                .Where(item => item.PublishedUtc > cutoff
                    || (!item.PublishedUtc.HasValue && undatedBeforeBoundary.Contains(item.OccurrenceKey)))
                .ToArray();
        }

        return boundaryIndex > 0
            ? items.Take(boundaryIndex).ToArray()
            : [];
    }
}
