using Marginalia.Web.Domain;
using Marginalia.Web.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Marginalia.Web.Application;

/// <summary>
/// Applies explicit single-article library and organization commands.
/// </summary>
public sealed class LibraryCommandService(
    LibraryDbContext dbContext,
    TimeProvider timeProvider)
{
    private const string ManualCaptureSourceKey = "manual";

    /// <summary>
    /// Resolves a pasted URL without silently resurfacing reviewed or purged articles.
    /// </summary>
    /// <param name="url">The manually supplied URL.</param>
    /// <param name="title">The metadata or user-entered title.</param>
    /// <param name="description">Optional description metadata.</param>
    /// <param name="captureRequestId">A stable idempotency key for this capture action.</param>
    /// <param name="confirmPurgedIdentity">Whether the user confirmed creating a fresh entry after a purged match.</param>
    /// <param name="cancellationToken">A token that cancels the command.</param>
    /// <returns>The created result or the explicit decision the user must make.</returns>
    public async Task<ManualCaptureResult> CaptureAsync(
        string url,
        string? title,
        string? description,
        Guid captureRequestId,
        bool confirmPurgedIdentity = false,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(url);
        if (captureRequestId == Guid.Empty)
        {
            throw new ArgumentException("A non-empty idempotency key is required.", nameof(captureRequestId));
        }

        var identityKey = UrlIdentity.Normalize(url);
        var identityFingerprint = IdentityFingerprint.Create(identityKey);
        var occurrenceKey = captureRequestId.ToString("N");

        var previousRequest = await dbContext.SourceOccurrences
            .AsNoTracking()
            .Where(occurrence =>
                occurrence.SourceKey == ManualCaptureSourceKey
                && occurrence.OccurrenceKey == occurrenceKey)
            .Select(occurrence => new { occurrence.Article.IdentityKey, occurrence.ArticleId })
            .SingleOrDefaultAsync(cancellationToken);

        if (previousRequest is not null)
        {
            if (!string.Equals(previousRequest.IdentityKey, identityKey, StringComparison.Ordinal))
            {
                throw new InvalidOperationException("The capture idempotency key was already used for a different URL.");
            }

            var priorArticle = await dbContext.Articles
                .AsNoTracking()
                .SingleAsync(article => article.Id == previousRequest.ArticleId, cancellationToken);
            return await ResultForExistingAsync(priorArticle, cancellationToken);
        }

        var existing = await dbContext.Articles
            .SingleOrDefaultAsync(article => article.IdentityKey == identityKey, cancellationToken);
        if (existing is not null)
        {
            return await ResultForExistingAsync(existing, cancellationToken);
        }

        var hasPurgedIdentity = await dbContext.IdentityTombstones
            .AnyAsync(tombstone => tombstone.IdentityFingerprint == identityFingerprint, cancellationToken);
        if (hasPurgedIdentity && !confirmPurgedIdentity)
        {
            return new ManualCaptureResult(
                ManualCaptureOutcome.PurgedIdentityConfirmationRequired,
                url,
                ExistingArticle: null);
        }

        if (string.IsNullOrWhiteSpace(title))
        {
            return new ManualCaptureResult(ManualCaptureOutcome.TitleRequired, url, ExistingArticle: null);
        }

        var now = timeProvider.GetUtcNow();
        var article = Article.CreateSaved(identityKey, identityFingerprint, url, title, description, now);
        dbContext.Articles.Add(article);
        dbContext.SourceOccurrences.Add(SourceOccurrence.Create(
            article.Id,
            SourceOccurrenceType.ManualCapture,
            ManualCaptureSourceKey,
            occurrenceKey,
            now));

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            return new ManualCaptureResult(ManualCaptureOutcome.Created, url, ExistingArticle: null);
        }
        catch (DbUpdateException exception) when (IsManualCaptureConflict(exception))
        {
            dbContext.ChangeTracker.Clear();
            var concurrentRequest = await dbContext.SourceOccurrences
                .AsNoTracking()
                .Where(occurrence =>
                    occurrence.SourceKey == ManualCaptureSourceKey
                    && occurrence.OccurrenceKey == occurrenceKey)
                .Select(occurrence => new { occurrence.Article.IdentityKey, occurrence.ArticleId })
                .SingleOrDefaultAsync(cancellationToken);
            if (concurrentRequest is not null)
            {
                if (!string.Equals(concurrentRequest.IdentityKey, identityKey, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException("The capture idempotency key was already used for a different URL.");
                }

                var requestedArticle = await dbContext.Articles
                    .AsNoTracking()
                    .SingleAsync(article => article.Id == concurrentRequest.ArticleId, cancellationToken);
                return await ResultForExistingAsync(requestedArticle, cancellationToken);
            }

            var concurrentArticle = await dbContext.Articles
                .AsNoTracking()
                .SingleOrDefaultAsync(candidate => candidate.IdentityKey == identityKey, cancellationToken);
            if (concurrentArticle is null)
            {
                throw;
            }

            return await ResultForExistingAsync(concurrentArticle, cancellationToken);
        }
    }

    /// <summary>
    /// Restores a retained dismissed or trashed duplicate after the owner chooses Restore.
    /// </summary>
    /// <param name="articleId">The retained article identifier.</param>
    /// <param name="captureRequestId">A stable idempotency key for the restoration choice.</param>
    /// <param name="cancellationToken">A token that cancels the command.</param>
    /// <returns>The restored or already-active article outcome.</returns>
    public async Task<ManualCaptureResult> RestoreManualCaptureMatchAsync(
        Guid articleId,
        Guid captureRequestId,
        CancellationToken cancellationToken = default)
    {
        if (captureRequestId == Guid.Empty)
        {
            throw new ArgumentException("A non-empty idempotency key is required.", nameof(captureRequestId));
        }

        var occurrenceKey = captureRequestId.ToString("N");
        var existingRequestArticleId = await dbContext.SourceOccurrences
            .AsNoTracking()
            .Where(occurrence =>
                occurrence.SourceKey == ManualCaptureSourceKey
                && occurrence.OccurrenceKey == occurrenceKey)
            .Select(occurrence => (Guid?)occurrence.ArticleId)
            .SingleOrDefaultAsync(cancellationToken);
        if (existingRequestArticleId.HasValue)
        {
            if (existingRequestArticleId.Value != articleId)
            {
                throw new InvalidOperationException("The capture idempotency key was already used for a different article.");
            }

            var alreadyRestored = await dbContext.Articles
                .AsNoTracking()
                .SingleAsync(candidate => candidate.Id == articleId, cancellationToken);
            return await ResultForExistingAsync(alreadyRestored, cancellationToken);
        }

        var article = await dbContext.Articles
            .SingleOrDefaultAsync(candidate => candidate.Id == articleId, cancellationToken)
            ?? throw new KeyNotFoundException("The article no longer exists.");

        if (article.State is ArticleState.Dismissed or ArticleState.Trashed)
        {
            article.RestoreDuplicateAsUnreadSaved(timeProvider.GetUtcNow());
            dbContext.SourceOccurrences.Add(SourceOccurrence.Create(
                article.Id,
                SourceOccurrenceType.ManualCapture,
                ManualCaptureSourceKey,
                occurrenceKey,
                timeProvider.GetUtcNow()));
            try
            {
                await dbContext.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateException exception) when (IsManualOccurrenceConflict(exception))
            {
                dbContext.ChangeTracker.Clear();
                var concurrentArticleId = await dbContext.SourceOccurrences
                    .AsNoTracking()
                    .Where(occurrence =>
                        occurrence.SourceKey == ManualCaptureSourceKey
                        && occurrence.OccurrenceKey == occurrenceKey)
                    .Select(occurrence => (Guid?)occurrence.ArticleId)
                    .SingleOrDefaultAsync(cancellationToken);
                if (concurrentArticleId != articleId)
                {
                    throw new InvalidOperationException(
                        "The capture idempotency key was already used for a different article.",
                        exception);
                }

                var concurrentlyRestored = await dbContext.Articles
                    .AsNoTracking()
                    .SingleAsync(candidate => candidate.Id == articleId, cancellationToken);
                return await ResultForExistingAsync(concurrentlyRestored, cancellationToken);
            }
        }

        return await ResultForExistingAsync(article, cancellationToken);
    }

    /// <summary>
    /// Saves an Inbox article after one individual keep decision.
    /// </summary>
    /// <param name="articleId">The Inbox article identifier.</param>
    /// <param name="title">The required title.</param>
    /// <param name="cancellationToken">A token that cancels the command.</param>
    public async Task SaveFromInboxAsync(
        Guid articleId,
        string title,
        CancellationToken cancellationToken = default)
    {
        var article = await GetArticleAsync(articleId, cancellationToken);
        article.SaveFromInbox(title, timeProvider.GetUtcNow());
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Dismisses one Inbox article for recoverable retention.
    /// </summary>
    /// <param name="articleId">The Inbox article identifier.</param>
    /// <param name="cancellationToken">A token that cancels the command.</param>
    public async Task DismissAsync(Guid articleId, CancellationToken cancellationToken = default)
    {
        var article = await GetArticleAsync(articleId, cancellationToken);
        article.Dismiss(timeProvider.GetUtcNow());
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Restores one dismissed article to Inbox during its recovery period.
    /// </summary>
    /// <param name="articleId">The dismissed article identifier.</param>
    /// <param name="cancellationToken">A token that cancels the command.</param>
    public async Task RestoreDismissedAsync(Guid articleId, CancellationToken cancellationToken = default)
    {
        var article = await GetArticleAsync(articleId, cancellationToken);
        article.RestoreDismissedToInbox(timeProvider.GetUtcNow());
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Moves one Saved article to recoverable Trash.
    /// </summary>
    /// <param name="articleId">The saved article identifier.</param>
    /// <param name="cancellationToken">A token that cancels the command.</param>
    public async Task MoveToTrashAsync(Guid articleId, CancellationToken cancellationToken = default)
    {
        var article = await GetArticleAsync(articleId, cancellationToken);
        article.MoveToTrash(timeProvider.GetUtcNow());
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Restores one trashed article during its recovery period.
    /// </summary>
    /// <param name="articleId">The trashed article identifier.</param>
    /// <param name="cancellationToken">A token that cancels the command.</param>
    public async Task RestoreTrashAsync(Guid articleId, CancellationToken cancellationToken = default)
    {
        var article = await GetArticleAsync(articleId, cancellationToken);
        article.RestoreFromTrash(timeProvider.GetUtcNow());
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Changes explicit read state without coupling it to opening the original URL.
    /// </summary>
    /// <param name="articleId">The Saved article identifier.</param>
    /// <param name="isRead">The explicit new read state.</param>
    /// <param name="cancellationToken">A token that cancels the command.</param>
    public async Task SetReadAsync(Guid articleId, bool isRead, CancellationToken cancellationToken = default)
    {
        var article = await GetArticleAsync(articleId, cancellationToken);
        article.SetRead(isRead, timeProvider.GetUtcNow());
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Changes favorite state independently of read state and retention.
    /// </summary>
    /// <param name="articleId">The Saved article identifier.</param>
    /// <param name="isFavorite">The explicit new favorite state.</param>
    /// <param name="cancellationToken">A token that cancels the command.</param>
    public async Task SetFavoriteAsync(Guid articleId, bool isFavorite, CancellationToken cancellationToken = default)
    {
        var article = await GetArticleAsync(articleId, cancellationToken);
        article.SetFavorite(isFavorite, timeProvider.GetUtcNow());
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Changes user-authored metadata without replacing it from imported source data.
    /// </summary>
    /// <param name="articleId">The article identifier.</param>
    /// <param name="title">The required title.</param>
    /// <param name="description">The optional description.</param>
    /// <param name="notes">The optional personal notes.</param>
    /// <param name="cancellationToken">A token that cancels the command.</param>
    public async Task UpdateMetadataAsync(
        Guid articleId,
        string title,
        string? description,
        string? notes,
        CancellationToken cancellationToken = default)
    {
        var article = await GetArticleAsync(articleId, cancellationToken);
        article.UpdateMetadata(title, description, notes, timeProvider.GetUtcNow());
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Replaces collection and tag memberships without changing article metadata or state.
    /// </summary>
    /// <param name="articleId">The Saved article identifier.</param>
    /// <param name="collectionNames">The desired exact collection names.</param>
    /// <param name="tagNames">The desired case-insensitive tag names.</param>
    /// <param name="cancellationToken">A token that cancels the command.</param>
    public async Task SetOrganizationAsync(
        Guid articleId,
        IEnumerable<string> collectionNames,
        IEnumerable<string> tagNames,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(collectionNames);
        ArgumentNullException.ThrowIfNull(tagNames);

        var namesOfCollections = collectionNames
            .Select(RequireName)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var desiredTags = tagNames
            .Select(RequireName)
            .GroupBy(Tag.Normalize, StringComparer.Ordinal)
            .Select(group => group.First())
            .ToArray();
        var normalizedTags = desiredTags.Select(Tag.Normalize).ToArray();

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        var article = await dbContext.Articles
            .Include(candidate => candidate.Collections)
                .ThenInclude(membership => membership.Collection)
            .Include(candidate => candidate.Tags)
                .ThenInclude(membership => membership.Tag)
            .SingleOrDefaultAsync(candidate => candidate.Id == articleId, cancellationToken)
            ?? throw new KeyNotFoundException("The article no longer exists.");
        if (article.State != ArticleState.Saved)
        {
            throw new InvalidOperationException("Only a Saved article can be organized.");
        }

        foreach (var name in namesOfCollections)
        {
            var id = Guid.NewGuid();
            await dbContext.Database.ExecuteSqlInterpolatedAsync(
                $"INSERT INTO \"Collections\" (\"Id\", \"Name\", \"CreatedUtc\") VALUES ({id}, {name}, {timeProvider.GetUtcNow()}) ON CONFLICT (\"Name\") DO NOTHING",
                cancellationToken);
        }

        foreach (var tagName in desiredTags)
        {
            var id = Guid.NewGuid();
            var normalizedName = Tag.Normalize(tagName);
            await dbContext.Database.ExecuteSqlInterpolatedAsync(
                $"INSERT INTO \"Tags\" (\"Id\", \"DisplayName\", \"NormalizedName\", \"CreatedUtc\") VALUES ({id}, {tagName}, {normalizedName}, {timeProvider.GetUtcNow()}) ON CONFLICT (\"NormalizedName\") DO NOTHING",
                cancellationToken);
        }

        var collections = await dbContext.Collections
            .Where(collection => namesOfCollections.Contains(collection.Name))
            .ToDictionaryAsync(collection => collection.Name, cancellationToken);
        var tags = await dbContext.Tags
            .Where(tag => normalizedTags.Contains(tag.NormalizedName))
            .ToDictionaryAsync(tag => tag.NormalizedName, cancellationToken);
        var desiredCollectionIds = collections.Values.Select(collection => collection.Id).ToHashSet();
        var desiredTagIds = tags.Values.Select(tag => tag.Id).ToHashSet();

        foreach (var membership in article.Collections
                     .Where(membership => !desiredCollectionIds.Contains(membership.CollectionId))
                     .ToArray())
        {
            article.Collections.Remove(membership);
        }

        foreach (var membership in article.Tags
                     .Where(membership => !desiredTagIds.Contains(membership.TagId))
                     .ToArray())
        {
            article.Tags.Remove(membership);
        }

        var currentCollectionIds = article.Collections.Select(membership => membership.CollectionId).ToHashSet();
        foreach (var collectionId in desiredCollectionIds.Except(currentCollectionIds))
        {
            article.Collections.Add(ArticleCollection.Create(article.Id, collectionId));
        }

        var currentTagIds = article.Tags.Select(membership => membership.TagId).ToHashSet();
        foreach (var tagId in desiredTagIds.Except(currentTagIds))
        {
            article.Tags.Add(ArticleTag.Create(article.Id, tagId));
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    private async Task<Article> GetArticleAsync(Guid articleId, CancellationToken cancellationToken) =>
        await dbContext.Articles.SingleOrDefaultAsync(article => article.Id == articleId, cancellationToken)
        ?? throw new KeyNotFoundException("The article no longer exists.");

    private async Task<ManualCaptureResult> ResultForExistingAsync(
        Article article,
        CancellationToken cancellationToken)
    {
        if (article.State is ArticleState.Dismissed or ArticleState.Trashed)
        {
            var details = await BuildRecoveryDetailsAsync(article, cancellationToken);
            return new ManualCaptureResult(
                ManualCaptureOutcome.RecoveryDecisionRequired,
                article.OriginalUrl,
                details);
        }

        return new ManualCaptureResult(
            ManualCaptureOutcome.AlreadyPresent,
            article.OriginalUrl,
            ExistingArticle: null);
    }

    private async Task<ArticleRecoveryDetails> BuildRecoveryDetailsAsync(
        Article article,
        CancellationToken cancellationToken)
    {
        var collections = await dbContext.Entry(article)
            .Collection(candidate => candidate.Collections)
            .Query()
            .Select(membership => membership.Collection.Name)
            .ToArrayAsync(cancellationToken);
        var tags = await dbContext.Entry(article)
            .Collection(candidate => candidate.Tags)
            .Query()
            .Select(membership => membership.Tag.DisplayName)
            .ToArrayAsync(cancellationToken);
        var occurrences = await dbContext.SourceOccurrences
            .AsNoTracking()
            .Where(occurrence => occurrence.ArticleId == article.Id)
            .OrderBy(occurrence => occurrence.OccurredUtc)
            .Select(occurrence => new SourceOccurrenceSummary(
                occurrence.Type.ToString(),
                occurrence.SourceKey,
                occurrence.OccurredUtc))
            .ToArrayAsync(cancellationToken);

        var articleItem = new ArticleListItem(
            article.Id,
            article.OriginalUrl,
            article.Title,
            article.Description,
            article.Notes,
            article.State,
            article.IsRead,
            article.IsFavorite,
            article.UpdatedUtc,
            article.CreatedUtc,
            article.PublishedUtc,
            article.PurgeAfterUtc,
            collections,
            tags,
            occurrences);

        return new ArticleRecoveryDetails(articleItem, occurrences);
    }

    private static string RequireName(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        return value.Trim();
    }

    private static bool IsManualCaptureConflict(DbUpdateException exception) =>
        exception.InnerException is PostgresException
        {
            SqlState: PostgresErrorCodes.UniqueViolation,
            ConstraintName: "IX_Articles_IdentityKey" or "IX_SourceOccurrences_SourceKey_OccurrenceKey"
        };

    private static bool IsManualOccurrenceConflict(DbUpdateException exception) =>
        exception.InnerException is PostgresException
        {
            SqlState: PostgresErrorCodes.UniqueViolation,
            ConstraintName: "IX_SourceOccurrences_SourceKey_OccurrenceKey"
        };
}
