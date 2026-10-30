namespace Marginalia.Web.Domain;

/// <summary>
/// Builds a conservative identity for HTTP(S) article URLs.
/// </summary>
public static class UrlIdentity
{
    /// <summary>
    /// Normalizes scheme/host casing, default ports, and fragments while preserving path and query.
    /// </summary>
    /// <param name="value">The supplied absolute URL.</param>
    /// <returns>The canonical identity used for deduplication.</returns>
    public static string Normalize(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        if (!Uri.TryCreate(value.Trim(), UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
            || string.IsNullOrWhiteSpace(uri.Host)
            || !string.IsNullOrEmpty(uri.UserInfo))
        {
            throw new ArgumentException("A valid HTTP or HTTPS URL without embedded credentials is required.", nameof(value));
        }

        var builder = new UriBuilder(uri)
        {
            Fragment = string.Empty
        };
        return builder.Uri.AbsoluteUri;
    }
}
