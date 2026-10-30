namespace Marginalia.Web.Application;

/// <summary>
/// Holds parsed feed metadata without retaining feed body content.
/// </summary>
public sealed record FeedDocument(string? Title, IReadOnlyList<FeedItemMetadata> Items);
