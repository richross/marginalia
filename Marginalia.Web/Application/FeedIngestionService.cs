using Marginalia.Web.Domain;
using Marginalia.Web.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Marginalia.Web.Application;

/// <summary>
/// Idempotently records feed entries while preserving reviewed article state and metadata.
/// </summary>
public sealed class FeedIngestionService(
    LibraryDbContext dbContext,
    TimeProvider timeProvider)
{
    /// <summary>
    /// Ingests feed entries as Inbox items without resurfacing retained or purged articles.
    /// </summary>
    /// <param name="feedId">The feed subscription identifier.</param>
    /// <param name="items">The bounded parsed feed entries.</param>
    /// <param name="cancellationToken">A token that cancels ingestion.</param>
    /// <returns>Counts of inserted, suppressed, and repeated outcomes.</returns>
    public async Task<FeedIngestionSummary> IngestAsync(
        Guid feedId,
        IReadOnlyList<FeedItemMetadata> items,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(items);
        var sourceKey = $"feed-{feedId:N}";
        var newArticles = 0;
        var newOccurrences = 0;
        var suppressed = 0;
        var repeated = 0;

        foreach (var item in items)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var identity = UrlIdentity.Normalize(item.Url);
            if (await HasOccurrenceAsync(sourceKey, item.OccurrenceKey, cancellationToken))
            {
                repeated++;
                continue;
            }

            var existing = await dbContext.Articles
                .AsNoTracking()
                .SingleOrDefaultAsync(article => article.IdentityKey == identity, cancellationToken);
            if (existing is not null)
            {
                var inserted = await InsertOccurrenceAsync(
                    existing.Id,
                    sourceKey,
                    item.OccurrenceKey,
                    cancellationToken);
                newOccurrences += inserted;
                repeated += inserted == 0 ? 1 : 0;
                continue;
            }

            var fingerprint = IdentityFingerprint.Create(identity);
            if (await dbContext.IdentityTombstones
                    .AnyAsync(tombstone => tombstone.IdentityFingerprint == fingerprint, cancellationToken))
            {
                suppressed++;
                continue;
            }

            var now = timeProvider.GetUtcNow();
            var article = Article.CreateInbox(
                identity,
                fingerprint,
                item.Url,
                item.Title,
                item.Description,
                now,
                item.PublishedUtc);
            dbContext.Articles.Add(article);
            dbContext.SourceOccurrences.Add(SourceOccurrence.Create(
                article.Id,
                SourceOccurrenceType.Feed,
                sourceKey,
                item.OccurrenceKey,
                now));

            try
            {
                await dbContext.SaveChangesAsync(cancellationToken);
                newArticles++;
                newOccurrences++;
            }
            catch (DbUpdateException exception) when (IsIngestionUniqueConflict(exception))
            {
                dbContext.ChangeTracker.Clear();
                if (await HasOccurrenceAsync(sourceKey, item.OccurrenceKey, cancellationToken))
                {
                    repeated++;
                    continue;
                }

                var concurrentArticle = await dbContext.Articles
                    .AsNoTracking()
                    .SingleOrDefaultAsync(candidate => candidate.IdentityKey == identity, cancellationToken);
                if (concurrentArticle is null)
                {
                    if (await dbContext.IdentityTombstones
                            .AnyAsync(tombstone => tombstone.IdentityFingerprint == fingerprint, cancellationToken))
                    {
                        suppressed++;
                        continue;
                    }

                    throw;
                }

                var inserted = await InsertOccurrenceAsync(
                    concurrentArticle.Id,
                    sourceKey,
                    item.OccurrenceKey,
                    cancellationToken);
                newOccurrences += inserted;
                repeated += inserted == 0 ? 1 : 0;
            }
        }

        return new FeedIngestionSummary(newArticles, newOccurrences, suppressed, repeated);
    }

    private Task<bool> HasOccurrenceAsync(
        string sourceKey,
        string occurrenceKey,
        CancellationToken cancellationToken) =>
        dbContext.SourceOccurrences.AnyAsync(
            occurrence => occurrence.SourceKey == sourceKey && occurrence.OccurrenceKey == occurrenceKey,
            cancellationToken);

    private Task<int> InsertOccurrenceAsync(
        Guid articleId,
        string sourceKey,
        string occurrenceKey,
        CancellationToken cancellationToken) =>
        dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"INSERT INTO \"SourceOccurrences\" (\"Id\", \"ArticleId\", \"Type\", \"SourceKey\", \"OccurrenceKey\", \"OccurredUtc\") VALUES ({Guid.NewGuid()}, {articleId}, {(int)SourceOccurrenceType.Feed}, {sourceKey}, {occurrenceKey}, {timeProvider.GetUtcNow()}) ON CONFLICT (\"SourceKey\", \"OccurrenceKey\") DO NOTHING",
            cancellationToken);

    private static bool IsIngestionUniqueConflict(DbUpdateException exception) =>
        exception.InnerException is PostgresException
        {
            SqlState: PostgresErrorCodes.UniqueViolation,
            ConstraintName: "IX_Articles_IdentityKey" or "IX_SourceOccurrences_SourceKey_OccurrenceKey"
        };
}
