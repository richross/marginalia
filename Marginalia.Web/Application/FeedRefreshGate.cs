namespace Marginalia.Web.Application;

/// <summary>
/// Serializes scheduled and manually requested feed refresh operations in one app instance.
/// </summary>
public sealed class FeedRefreshGate
{
    private readonly SemaphoreSlim _semaphore = new(1, 1);

    /// <summary>
    /// Waits until a refresh can begin.
    /// </summary>
    /// <param name="cancellationToken">A token that cancels the wait.</param>
    internal Task WaitAsync(CancellationToken cancellationToken) =>
        _semaphore.WaitAsync(cancellationToken);

    /// <summary>
    /// Releases the refresh gate.
    /// </summary>
    internal void Release() => _semaphore.Release();
}
