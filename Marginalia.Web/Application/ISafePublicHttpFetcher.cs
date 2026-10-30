namespace Marginalia.Web.Application;

/// <summary>
/// Fetches bounded public HTTP(S) resources with destination validation.
/// </summary>
public interface ISafePublicHttpFetcher
{
    /// <summary>
    /// Fetches a public resource, optionally using conditional request validators.
    /// </summary>
    /// <param name="target">The HTTP(S) resource to fetch.</param>
    /// <param name="entityTag">An optional previously stored ETag.</param>
    /// <param name="lastModified">An optional previously stored Last-Modified value.</param>
    /// <param name="cancellationToken">A token that cancels the request.</param>
    /// <returns>The bounded response status, validators, and body bytes.</returns>
    Task<SafeHttpFetchResponse> FetchAsync(
        Uri target,
        string? entityTag = null,
        DateTimeOffset? lastModified = null,
        CancellationToken cancellationToken = default);
}
