namespace Marginalia.Web.Application;

/// <summary>
/// Holds bounded link metadata and a privacy-minimized feed occurrence identity.
/// </summary>
public sealed record FeedItemMetadata(
    string Url,
    string? Title,
    string? Description,
    DateTimeOffset? PublishedUtc,
    string OccurrenceKey);
