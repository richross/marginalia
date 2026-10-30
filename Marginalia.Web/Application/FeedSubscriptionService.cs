using Marginalia.Web.Domain;
using Marginalia.Web.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Marginalia.Web.Application;

/// <summary>
/// Adds, lists, and disables individual feed subscriptions without deleting history.
/// </summary>
public sealed class FeedSubscriptionService(
    LibraryDbContext dbContext,
    TimeProvider timeProvider)
{
    /// <summary>
    /// Adds an individual RSS or Atom URL, due immediately for its initial refresh.
    /// </summary>
    /// <param name="feedUrl">The HTTP(S) feed endpoint.</param>
    /// <param name="cancellationToken">A token that cancels the command.</param>
    /// <returns>The new subscription identifier.</returns>
    public async Task<Guid> AddAsync(string feedUrl, CancellationToken cancellationToken = default)
    {
        var feed = FeedSubscription.Create(feedUrl, timeProvider.GetUtcNow());
        var exists = await dbContext.FeedSubscriptions
            .AnyAsync(candidate => candidate.FeedUrl == feed.FeedUrl, cancellationToken);
        if (exists)
        {
            throw new InvalidOperationException("That feed URL is already subscribed.");
        }

        dbContext.FeedSubscriptions.Add(feed);
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (IsFeedUrlConflict(exception))
        {
            dbContext.ChangeTracker.Clear();
            throw new InvalidOperationException("That feed URL is already subscribed.", exception);
        }

        return feed.Id;
    }

    /// <summary>
    /// Lists subscriptions and their safe refresh health details.
    /// </summary>
    /// <param name="cancellationToken">A token that cancels the query.</param>
    /// <returns>Subscriptions ordered by their most recent update.</returns>
    public Task<FeedSubscriptionSummary[]> ListAsync(CancellationToken cancellationToken = default) =>
        dbContext.FeedSubscriptions
            .AsNoTracking()
            .OrderBy(feed => feed.FeedTitle ?? feed.FeedUrl)
            .ThenBy(feed => feed.Id)
            .Select(feed => new FeedSubscriptionSummary(
                feed.Id,
                feed.FeedUrl,
                feed.FeedTitle,
                feed.IsEnabled,
                feed.LastAttemptUtc,
                feed.LastSuccessUtc,
                feed.NextDueUtc,
                feed.LastError,
                feed.ConsecutiveFailures))
            .ToArrayAsync(cancellationToken);

    /// <summary>
    /// Disables a subscription while retaining its validators and historical occurrences.
    /// </summary>
    /// <param name="feedId">The feed subscription identifier.</param>
    /// <param name="cancellationToken">A token that cancels the command.</param>
    public async Task DisableAsync(Guid feedId, CancellationToken cancellationToken = default)
    {
        var feed = await GetFeedAsync(feedId, cancellationToken);
        feed.Disable(timeProvider.GetUtcNow());
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Re-enables a feed and makes it eligible for an immediate refresh.
    /// </summary>
    /// <param name="feedId">The feed subscription identifier.</param>
    /// <param name="cancellationToken">A token that cancels the command.</param>
    public async Task EnableAsync(Guid feedId, CancellationToken cancellationToken = default)
    {
        var feed = await GetFeedAsync(feedId, cancellationToken);
        feed.Enable(timeProvider.GetUtcNow());
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task<FeedSubscription> GetFeedAsync(Guid feedId, CancellationToken cancellationToken) =>
        await dbContext.FeedSubscriptions.SingleOrDefaultAsync(feed => feed.Id == feedId, cancellationToken)
        ?? throw new KeyNotFoundException("The feed subscription no longer exists.");

    private static bool IsFeedUrlConflict(DbUpdateException exception) =>
        exception.InnerException is Npgsql.PostgresException
        {
            SqlState: Npgsql.PostgresErrorCodes.UniqueViolation,
            ConstraintName: "IX_FeedSubscriptions_FeedUrl"
        };
}
