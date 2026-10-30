using Marginalia.Web.Domain;

namespace Marginalia.Web.Application;

/// <summary>
/// A bounded article projection for library lists.
/// </summary>
/// <param name="Id">The article identifier.</param>
/// <param name="OriginalUrl">The original article URL.</param>
/// <param name="Title">The article title, if known.</param>
/// <param name="Description">The optional description.</param>
/// <param name="Notes">The owner's optional notes.</param>
/// <param name="State">The article state.</param>
/// <param name="IsRead">The explicit read state.</param>
/// <param name="IsFavorite">The favorite state.</param>
/// <param name="UpdatedUtc">The last metadata change time.</param>
/// <param name="CreatedUtc">When the article was first captured.</param>
/// <param name="PublishedUtc">The source-reported publication time, if available.</param>
/// <param name="PurgeAfterUtc">The recovery deadline, if applicable.</param>
/// <param name="Collections">The collection names.</param>
/// <param name="Tags">The preserved tag display names.</param>
/// <param name="SourceOccurrences">The retained source history.</param>
public sealed record ArticleListItem(
    Guid Id,
    string OriginalUrl,
    string? Title,
    string? Description,
    string? Notes,
    ArticleState State,
    bool IsRead,
    bool IsFavorite,
    DateTimeOffset UpdatedUtc,
    DateTimeOffset CreatedUtc,
    DateTimeOffset? PublishedUtc,
    DateTimeOffset? PurgeAfterUtc,
    IReadOnlyList<string> Collections,
    IReadOnlyList<string> Tags,
    IReadOnlyList<SourceOccurrenceSummary> SourceOccurrences);
