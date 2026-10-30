namespace Marginalia.Web.Application;

/// <summary>
/// Reports one feed refresh outcome without exposing response content or source URLs.
/// </summary>
public sealed record FeedRefreshSummary(
    Guid FeedId,
    bool Succeeded,
    bool NotModified,
    int NewInboxArticles,
    int NewSourceOccurrences,
    int SuppressedPurgedIdentities,
    int RepeatedOccurrences,
    string? Error);
