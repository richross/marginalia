namespace Marginalia.Web.Application;

/// <summary>
/// Describes a safe, non-sensitive failure while fetching public metadata.
/// </summary>
public sealed class SafeHttpFetchException : Exception
{
    /// <summary>
    /// Initializes a new instance of the <see cref="SafeHttpFetchException"/> class.
    /// </summary>
    /// <param name="failure">The safe failure category.</param>
    public SafeHttpFetchException(SafeHttpFetchFailure failure)
        : base(GetSafeMessage(failure))
    {
        Failure = failure;
    }

    /// <summary>
    /// Gets the safe failure category.
    /// </summary>
    public SafeHttpFetchFailure Failure { get; }

    private static string GetSafeMessage(SafeHttpFetchFailure failure) => failure switch
    {
        SafeHttpFetchFailure.UnsafeTarget => "The target is not an allowed public HTTP(S) destination.",
        SafeHttpFetchFailure.DnsRejected => "The target did not resolve exclusively to public addresses.",
        SafeHttpFetchFailure.RedirectRejected => "The response contained a disallowed redirect.",
        SafeHttpFetchFailure.TooManyRedirects => "The response exceeded the redirect limit.",
        SafeHttpFetchFailure.ResponseTooLarge => "The response exceeded the maximum allowed size.",
        SafeHttpFetchFailure.Timeout => "The public metadata request timed out.",
        SafeHttpFetchFailure.RemoteFailure => "The public metadata source returned an unsuccessful response.",
        SafeHttpFetchFailure.NetworkFailure => "The public metadata source could not be reached.",
        SafeHttpFetchFailure.UnsupportedContentType => "The public source did not return supported HTML metadata.",
        SafeHttpFetchFailure.MetadataUnavailable => "The public source did not provide a usable title.",
        _ => "The public metadata request failed."
    };
}

/// <summary>
/// Lists safe failure categories for public metadata requests.
/// </summary>
public enum SafeHttpFetchFailure
{
    /// <summary>The request target is not an allowed public HTTP(S) destination.</summary>
    UnsafeTarget,

    /// <summary>DNS resolution returned an unapproved address or no address.</summary>
    DnsRejected,

    /// <summary>A redirect target is invalid or downgrades HTTPS to HTTP.</summary>
    RedirectRejected,

    /// <summary>The response exceeded the redirect limit.</summary>
    TooManyRedirects,

    /// <summary>The response body exceeded the configured size limit.</summary>
    ResponseTooLarge,

    /// <summary>The request exceeded the bounded time limit.</summary>
    Timeout,

    /// <summary>The remote server returned a non-success status.</summary>
    RemoteFailure,

    /// <summary>A network error prevented the request from completing.</summary>
    NetworkFailure,

    /// <summary>The response did not contain supported HTML metadata.</summary>
    UnsupportedContentType,

    /// <summary>The response contained no usable title.</summary>
    MetadataUnavailable
}
