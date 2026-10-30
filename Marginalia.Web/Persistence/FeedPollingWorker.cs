using Marginalia.Web.Application;
using Microsoft.EntityFrameworkCore;

namespace Marginalia.Web.Persistence;

/// <summary>
/// Runs persisted-due feed refreshes while the local web application is running.
/// </summary>
public sealed class FeedPollingWorker(
    IServiceScopeFactory scopeFactory,
    TimeProvider timeProvider,
    ILogger<FeedPollingWorker> logger) : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromMinutes(1);

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await RefreshDueFeedsAsync(stoppingToken);
        using var timer = new PeriodicTimer(PollInterval, timeProvider);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            await RefreshDueFeedsAsync(stoppingToken);
        }
    }

    private async Task RefreshDueFeedsAsync(CancellationToken cancellationToken)
    {
        Guid[] dueFeedIds;
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<LibraryDbContext>();
            var now = timeProvider.GetUtcNow();
            dueFeedIds = await dbContext.FeedSubscriptions
                .AsNoTracking()
                .Where(feed => feed.IsEnabled && feed.NextDueUtc <= now)
                .OrderBy(feed => feed.NextDueUtc)
                .Select(feed => feed.Id)
                .ToArrayAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogError(
                "Could not load due feeds; polling will retry on the next interval ({FailureType}).",
                exception.GetType().Name);
            return;
        }

        foreach (var feedId in dueFeedIds)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await using var scope = scopeFactory.CreateAsyncScope();
            try
            {
                var pollingService = scope.ServiceProvider.GetRequiredService<FeedPollingService>();
                var result = await pollingService.RefreshAsync(feedId, cancellationToken);
                if (!result.Succeeded)
                {
                    logger.LogWarning(
                        "Feed refresh did not succeed for subscription {FeedId}: {Failure}",
                        feedId,
                        result.Error);
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                logger.LogError(
                    "Unexpected feed refresh failure for subscription {FeedId} ({FailureType}).",
                    feedId,
                    exception.GetType().Name);
            }
        }
    }
}
