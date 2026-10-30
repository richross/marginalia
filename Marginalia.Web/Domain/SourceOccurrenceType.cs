namespace Marginalia.Web.Domain;

/// <summary>
/// Identifies the kind of source through which an article was encountered.
/// </summary>
public enum SourceOccurrenceType
{
    /// <summary>
    /// The article was delivered by a subscribed feed.
    /// </summary>
    Feed,

    /// <summary>
    /// The article was explicitly captured by pasting its URL.
    /// </summary>
    ManualCapture
}
