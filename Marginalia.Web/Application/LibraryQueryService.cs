using Marginalia.Web.Domain;
using Marginalia.Web.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Marginalia.Web.Application;

/// <summary>
/// Performs bounded, server-side library search and paging.
/// </summary>
public sealed class LibraryQueryService(LibraryDbContext dbContext)
{
    private const int MaximumPageSize = 100;

    /// <summary>
    /// Lists the names and identifiers needed to filter library organization.
    /// </summary>
    /// <param name="cancellationToken">A token that cancels the query.</param>
    /// <returns>Distinct collection and tag options.</returns>
    public async Task<OrganizationOptions> GetOrganizationOptionsAsync(
        CancellationToken cancellationToken = default)
    {
        var collections = await dbContext.Collections
            .AsNoTracking()
            .OrderBy(collection => collection.Name)
            .Select(collection => new CollectionOption(collection.Id, collection.Name))
            .ToArrayAsync(cancellationToken);
        var tags = await dbContext.Tags
            .AsNoTracking()
            .OrderBy(tag => tag.NormalizedName)
            .Select(tag => tag.DisplayName)
            .ToArrayAsync(cancellationToken);

        return new OrganizationOptions(collections, tags);
    }

    /// <summary>
    /// Searches article metadata and organization without loading the complete library.
    /// </summary>
    /// <param name="request">The requested filters and page.</param>
    /// <param name="cancellationToken">A token that cancels the query.</param>
    /// <returns>A stable page and total match count.</returns>
    public async Task<ArticlePage> SearchAsync(
        ArticleSearchRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var pageNumber = Math.Max(1, request.PageNumber);
        var pageSize = Math.Clamp(request.PageSize, 1, MaximumPageSize);
        IQueryable<Article> query = dbContext.Articles.AsNoTracking();

        if (request.State is not null)
        {
            query = query.Where(article => article.State == request.State.Value);
        }

        if (request.CollectionId is not null)
        {
            var collectionId = request.CollectionId.Value;
            query = query.Where(article =>
                article.Collections.Any(membership => membership.CollectionId == collectionId));
        }

        if (!string.IsNullOrWhiteSpace(request.TagName))
        {
            var normalizedTag = Tag.Normalize(request.TagName);
            query = query.Where(article =>
                article.Tags.Any(membership => membership.Tag.NormalizedName == normalizedTag));
        }

        if (request.IsRead is not null)
        {
            query = query.Where(article => article.IsRead == request.IsRead.Value);
        }

        if (request.IsFavorite is not null)
        {
            query = query.Where(article => article.IsFavorite == request.IsFavorite.Value);
        }

        if (!string.IsNullOrWhiteSpace(request.Query))
        {
            var pattern = $"%{EscapeLikePattern(request.Query.Trim())}%";
            query = query.Where(article =>
                EF.Functions.ILike(article.Title ?? string.Empty, pattern, "\\")
                || EF.Functions.ILike(article.Description ?? string.Empty, pattern, "\\")
                || EF.Functions.ILike(article.OriginalUrl, pattern, "\\")
                || EF.Functions.ILike(article.Notes ?? string.Empty, pattern, "\\")
                || article.Tags.Any(membership =>
                    EF.Functions.ILike(membership.Tag.DisplayName, pattern, "\\"))
                || article.Collections.Any(membership =>
                    EF.Functions.ILike(membership.Collection.Name, pattern, "\\")));
        }

        var totalCount = await query.LongCountAsync(cancellationToken);
        var skip = ((long)pageNumber - 1) * pageSize;
        if (skip > int.MaxValue)
        {
            return new ArticlePage([], totalCount, pageNumber, pageSize);
        }

        var items = await query
            .OrderByDescending(article => article.UpdatedUtc)
            .ThenBy(article => article.Id)
            .Skip((int)skip)
            .Take(pageSize)
            .Select(article => new ArticleListItem(
                article.Id,
                article.OriginalUrl,
                article.Title,
                article.Description,
                article.Notes,
                article.State,
                article.IsRead,
                article.IsFavorite,
                article.UpdatedUtc,
                article.CreatedUtc,
                article.PublishedUtc,
                article.PurgeAfterUtc,
                article.Collections
                    .OrderBy(membership => membership.Collection.Name)
                    .Select(membership => membership.Collection.Name)
                    .ToArray(),
                article.Tags
                    .OrderBy(membership => membership.Tag.NormalizedName)
                    .Select(membership => membership.Tag.DisplayName)
                    .ToArray(),
                article.SourceOccurrences
                    .OrderBy(occurrence => occurrence.OccurredUtc)
                    .Select(occurrence => new SourceOccurrenceSummary(
                        occurrence.Type == SourceOccurrenceType.Feed ? "Feed" : "ManualCapture",
                        occurrence.SourceKey,
                        occurrence.OccurredUtc))
                    .ToArray()))
            .ToListAsync(cancellationToken);

        return new ArticlePage(items, totalCount, pageNumber, pageSize);
    }

    private static string EscapeLikePattern(string value) =>
        value.Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("%", "\\%", StringComparison.Ordinal)
            .Replace("_", "\\_", StringComparison.Ordinal);
}
