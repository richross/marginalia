using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using Marginalia.Web.Security;
using OpenTelemetry;

namespace Marginalia.Web.Application;

/// <summary>
/// Performs bounded public HTTP requests and validates every redirect target.
/// </summary>
public sealed class SafePublicHttpFetcher(
    HttpClient httpClient,
    IHostAddressResolver addressResolver) : ISafePublicHttpFetcher, IDisposable
{
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(15);
    private const int MaximumResponseBytes = 4 * 1024 * 1024;
    private const int MaximumRedirects = 5;

    /// <inheritdoc />
    public async Task<SafeHttpFetchResponse> FetchAsync(
        Uri target,
        string? entityTag = null,
        DateTimeOffset? lastModified = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(target);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(RequestTimeout);

        try
        {
            var current = target;
            var originalScheme = target.Scheme;
            for (var redirectCount = 0; ; redirectCount++)
            {
                await ValidateTargetAsync(current, timeout.Token);

                using var request = new HttpRequestMessage(HttpMethod.Get, current);
                request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/rss+xml"));
                request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/atom+xml"));
                request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/xml"));
                request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/xml"));
                request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/html"));
                request.Headers.UserAgent.ParseAdd("Marginalia/1.0 (+public metadata fetch)");
                if (redirectCount == 0 && !string.IsNullOrWhiteSpace(entityTag))
                {
                    if (!EntityTagHeaderValue.TryParse(entityTag, out var parsedEntityTag))
                    {
                        throw new ArgumentException("The stored ETag is invalid.", nameof(entityTag));
                    }

                    request.Headers.IfNoneMatch.Add(parsedEntityTag);
                }

                if (redirectCount == 0 && lastModified.HasValue)
                {
                    request.Headers.IfModifiedSince = lastModified.Value;
                }

                using var instrumentationSuppression = SuppressInstrumentationScope.Begin();
                using var response = await httpClient.SendAsync(
                    request,
                    HttpCompletionOption.ResponseHeadersRead,
                    timeout.Token);

                if (IsRedirect(response.StatusCode))
                {
                    if (redirectCount >= MaximumRedirects
                        || response.Headers.Location is not { } location
                        || !Uri.TryCreate(current, location, out var next))
                    {
                        throw new SafeHttpFetchException(
                            redirectCount >= MaximumRedirects
                                ? SafeHttpFetchFailure.TooManyRedirects
                                : SafeHttpFetchFailure.RedirectRejected);
                    }

                    if (originalScheme == Uri.UriSchemeHttps && next.Scheme != Uri.UriSchemeHttps)
                    {
                        throw new SafeHttpFetchException(SafeHttpFetchFailure.RedirectRejected);
                    }

                    current = next;
                    continue;
                }

                if (response.StatusCode != HttpStatusCode.NotModified && !response.IsSuccessStatusCode)
                {
                    throw new SafeHttpFetchException(SafeHttpFetchFailure.RemoteFailure);
                }

                var body = response.StatusCode == HttpStatusCode.NotModified
                    ? []
                    : await ReadBoundedBodyAsync(response, timeout.Token);
                var responseTag = response.Headers.ETag?.ToString();
                return new SafeHttpFetchResponse(
                    response.StatusCode,
                    current,
                    response.Content.Headers.ContentType?.MediaType,
                    body,
                    responseTag is { Length: <= 512 } ? responseTag : null,
                    response.Content.Headers.LastModified);
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new SafeHttpFetchException(SafeHttpFetchFailure.Timeout);
        }
        catch (HttpRequestException)
        {
            throw new SafeHttpFetchException(SafeHttpFetchFailure.NetworkFailure);
        }
        catch (SocketException)
        {
            throw new SafeHttpFetchException(SafeHttpFetchFailure.NetworkFailure);
        }
    }

    private async Task ValidateTargetAsync(Uri target, CancellationToken cancellationToken)
    {
        if (!target.IsAbsoluteUri
            || (target.Scheme != Uri.UriSchemeHttp && target.Scheme != Uri.UriSchemeHttps)
            || !string.IsNullOrEmpty(target.UserInfo)
            || (target.Scheme == Uri.UriSchemeHttp && target.Port != 80)
            || (target.Scheme == Uri.UriSchemeHttps && target.Port != 443)
            || string.IsNullOrWhiteSpace(target.Host))
        {
            throw new SafeHttpFetchException(SafeHttpFetchFailure.UnsafeTarget);
        }

        IPAddress[] addresses;
        try
        {
            addresses = await addressResolver.ResolveAsync(target.Host, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (SocketException)
        {
            throw new SafeHttpFetchException(SafeHttpFetchFailure.NetworkFailure);
        }
        catch (HttpRequestException)
        {
            throw new SafeHttpFetchException(SafeHttpFetchFailure.NetworkFailure);
        }

        if (addresses.Length == 0 || addresses.Any(address => !PublicAddressPolicy.IsPublic(address)))
        {
            throw new SafeHttpFetchException(SafeHttpFetchFailure.DnsRejected);
        }
    }

    private static async Task<byte[]> ReadBoundedBodyAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        if (response.Content.Headers.ContentLength > MaximumResponseBytes)
        {
            throw new SafeHttpFetchException(SafeHttpFetchFailure.ResponseTooLarge);
        }

        await using var source = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var destination = new MemoryStream();
        var buffer = new byte[16 * 1024];
        while (true)
        {
            var bytesRead = await source.ReadAsync(buffer, cancellationToken);
            if (bytesRead == 0)
            {
                break;
            }

            if (destination.Length + bytesRead > MaximumResponseBytes)
            {
                throw new SafeHttpFetchException(SafeHttpFetchFailure.ResponseTooLarge);
            }

            await destination.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken);
        }

        return destination.ToArray();
    }

    private static bool IsRedirect(HttpStatusCode statusCode) =>
        statusCode is HttpStatusCode.MovedPermanently
            or HttpStatusCode.Redirect
            or HttpStatusCode.RedirectMethod
            or HttpStatusCode.TemporaryRedirect
            or HttpStatusCode.PermanentRedirect;

    /// <inheritdoc />
    public void Dispose() => httpClient.Dispose();
}
