using System.Text.Json;
using System.Text.Json.Serialization;
using Marginalia.Web.Domain;
using Marginalia.Web.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Marginalia.Web.Application;

/// <summary>
/// Produces a bounded, versioned JSON export of Saved library data.
/// </summary>
public sealed class LibraryExportService(
    LibraryDbContext dbContext,
    TimeProvider timeProvider)
{
    public const int CurrentVersion = 1;

    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        WriteIndented = true
    };

    /// <summary>
    /// Serializes Saved link metadata, organization, explicit state, and permitted provenance.
    /// </summary>
    /// <param name="cancellationToken">A token that cancels the query.</param>
    /// <returns>UTF-8 JSON bytes ready for a download.</returns>
    public async Task<byte[]> ExportJsonAsync(CancellationToken cancellationToken = default)
    {
        var articles = await dbContext.Articles
            .AsNoTracking()
            .Where(article => article.State == ArticleState.Saved)
            .OrderBy(article => article.IdentityKey)
            .Select(article => new LibraryExportArticle(
                article.OriginalUrl,
                article.Title,
                article.Description,
                article.Notes,
                article.IsRead,
                article.IsFavorite,
                article.CreatedUtc,
                article.PublishedUtc,
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
                    .Select(occurrence => new LibraryExportSource(
                        occurrence.Type == SourceOccurrenceType.Feed ? "feed" : "manual-capture",
                        occurrence.SourceKey,
                        occurrence.OccurredUtc))
                    .ToArray()))
            .ToArrayAsync(cancellationToken);

        var document = new LibraryExportDocument(
            CurrentVersion,
            timeProvider.GetUtcNow(),
            articles);
        return JsonSerializer.SerializeToUtf8Bytes(document, SerializerOptions);
    }
}
