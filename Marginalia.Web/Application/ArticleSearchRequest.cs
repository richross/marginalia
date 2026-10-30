using Marginalia.Web.Domain;

namespace Marginalia.Web.Application;

/// <summary>
/// Describes server-side filters and paging for library articles.
/// </summary>
/// <param name="Query">Optional keyword query.</param>
/// <param name="State">Optional article state.</param>
/// <param name="CollectionId">Optional collection filter.</param>
/// <param name="TagName">Optional tag-name filter.</param>
/// <param name="IsRead">Optional explicit read-state filter.</param>
/// <param name="IsFavorite">Optional favorite-state filter.</param>
/// <param name="PageNumber">The one-based page number.</param>
/// <param name="PageSize">The requested number of rows per page.</param>
public sealed record ArticleSearchRequest(
    string? Query = null,
    ArticleState? State = null,
    Guid? CollectionId = null,
    string? TagName = null,
    bool? IsRead = null,
    bool? IsFavorite = null,
    int PageNumber = 1,
    int PageSize = 50);
