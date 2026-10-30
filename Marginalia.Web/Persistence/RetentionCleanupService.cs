namespace Marginalia.Web.Persistence;

/// <summary>
/// Periodically removes only dismissed or trashed article data whose recovery deadline elapsed.
/// </summary>
public sealed class RetentionCleanupService(
    IServiceScopeFactory scopeFactory,
    TimeProvider timeProvider,
    ILogger<RetentionCleanupService> logger) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromHours(24);

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var retentionService = scope.ServiceProvider.GetRequiredService<ArticleRetentionService>();
                var purgedCount = await retentionService.PurgeExpiredAsync(stoppingToken);
                if (purgedCount > 0)
                {
                    logger.LogInformation("Purged {ArticleCount} expired article records.", purgedCount);
                }

                await Task.Delay(Interval, timeProvider, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Article retention cleanup failed.");
                await Task.Delay(Interval, timeProvider, stoppingToken);
            }
        }
    }
}
