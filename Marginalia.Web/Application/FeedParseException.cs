namespace Marginalia.Web.Application;

/// <summary>
/// Indicates that a bounded response is not a supported well-formed RSS or Atom feed.
/// </summary>
public sealed class FeedParseException : Exception
{
    /// <summary>
    /// Initializes a new instance of the <see cref="FeedParseException"/> class.
    /// </summary>
    public FeedParseException()
        : base("The feed response was malformed or used an unsupported format.")
    {
    }
}
