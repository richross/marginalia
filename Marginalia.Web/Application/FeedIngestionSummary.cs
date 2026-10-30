namespace Marginalia.Web.Application;

/// <summary>
/// Counts safe feed ingestion outcomes without retaining response content.
/// </summary>
public sealed record FeedIngestionSummary(
    int NewInboxArticles,
    int NewSourceOccurrences,
    int SuppressedPurgedIdentities,
    int RepeatedOccurrences);
