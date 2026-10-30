namespace Marginalia.Web.Application;

/// <summary>
/// A server-paged result set.
/// </summary>
/// <param name="Items">The current page.</param>
/// <param name="TotalCount">The total number of matching articles.</param>
/// <param name="PageNumber">The one-based page number returned.</param>
/// <param name="PageSize">The maximum number of items on this page.</param>
public sealed record ArticlePage(
    IReadOnlyList<ArticleListItem> Items,
    long TotalCount,
    int PageNumber,
    int PageSize);
