using System.Text.Json;
using System.Text.Json.Serialization;
using Marginalia.Web.Domain;
using Marginalia.Web.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Marginalia.Web.Application;

/// <summary>
/// Imports a versioned Saved-library document without modifying existing identities.
/// </summary>
public sealed class LibraryImportService(
    LibraryDbContext dbContext,
    TimeProvider timeProvider)
{
    public const long MaximumImportFileBytes = 128L * 1024 * 1024;

    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        RespectRequiredConstructorParameters = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };

    /// <summary>
    /// Validates the complete document, then atomically adds new Saved identities.
    /// </summary>
    public async Task<LibraryImportResult> ImportJsonAsync(
        Stream jsonStream,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(jsonStream);

        LibraryExportDocument document;
        try
        {
            document = await JsonSerializer.DeserializeAsync<LibraryExportDocument>(
                    jsonStream,
                    SerializerOptions,
                    cancellationToken)
                ?? throw new LibraryImportValidationException("The selected file is empty or is not a Marginalia export.");
        }
        catch (JsonException)
        {
            throw new LibraryImportValidationException("The selected file is not valid versioned Marginalia JSON.");
        }

        var entries = ValidateDocument(document);
        var conflicts = new List<LibraryImportConflict>();
        var candidates = new List<PreparedArticle>();
        var seenIdentities = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entry in entries)
        {
            if (seenIdentities.Add(entry.IdentityKey))
            {
                candidates.Add(entry);
            }
            else
            {
                conflicts.Add(new LibraryImportConflict(
                    entry.ArticleNumber,
                    entry.OriginalUrl,
                    "Duplicate identity in this import file."));
            }
        }

        try
        {
            await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
            var identityKeys = candidates.Select(candidate => candidate.IdentityKey).ToArray();
            var fingerprints = candidates.Select(candidate => candidate.IdentityFingerprint).ToArray();
            var existingIdentities = identityKeys.Length == 0
                ? new HashSet<string>(StringComparer.Ordinal)
                : await dbContext.Articles
                    .AsNoTracking()
                    .Where(article => identityKeys.Contains(article.IdentityKey))
                    .Select(article => article.IdentityKey)
                    .ToHashSetAsync(cancellationToken);
            var purgedIdentities = fingerprints.Length == 0
                ? new HashSet<string>(StringComparer.Ordinal)
                : await dbContext.IdentityTombstones
                    .AsNoTracking()
                    .Where(tombstone => fingerprints.Contains(tombstone.IdentityFingerprint))
                    .Select(tombstone => tombstone.IdentityFingerprint)
                    .ToHashSetAsync(cancellationToken);

            var newArticles = new List<PreparedArticle>(candidates.Count);
            foreach (var candidate in candidates)
            {
                if (existingIdentities.Contains(candidate.IdentityKey))
                {
                    conflicts.Add(new LibraryImportConflict(
                        candidate.ArticleNumber,
                        candidate.OriginalUrl,
                        "This identity already exists and was left unchanged."));
                }
                else if (purgedIdentities.Contains(candidate.IdentityFingerprint))
                {
                    conflicts.Add(new LibraryImportConflict(
                        candidate.ArticleNumber,
                        candidate.OriginalUrl,
                        "This identity was previously purged and was not restored."));
                }
                else
                {
                    newArticles.Add(candidate);
                }
            }

            if (newArticles.Count == 0)
            {
                await transaction.CommitAsync(cancellationToken);
                return new LibraryImportResult(0, conflicts);
            }

            var importedUtc = timeProvider.GetUtcNow();
            var collectionNames = newArticles
                .SelectMany(article => article.Collections)
                .Distinct(StringComparer.Ordinal)
                .ToArray();
            var collectionByName = collectionNames.Length == 0
                ? new Dictionary<string, Collection>(StringComparer.Ordinal)
                : await dbContext.Collections
                    .AsNoTracking()
                    .Where(collection => collectionNames.Contains(collection.Name))
                    .ToDictionaryAsync(collection => collection.Name, StringComparer.Ordinal, cancellationToken);
            var newCollections = new List<Collection>();
            foreach (var name in collectionNames)
            {
                if (!collectionByName.ContainsKey(name))
                {
                    var collection = Collection.Create(name, importedUtc);
                    collectionByName.Add(name, collection);
                    newCollections.Add(collection);
                }
            }

            var desiredTags = newArticles
                .SelectMany(article => article.Tags)
                .GroupBy(Tag.Normalize, StringComparer.Ordinal)
                .Select(group => group.First())
                .ToArray();
            var normalizedTagNames = desiredTags.Select(Tag.Normalize).ToArray();
            var tagByNormalizedName = normalizedTagNames.Length == 0
                ? new Dictionary<string, Tag>(StringComparer.Ordinal)
                : await dbContext.Tags
                    .AsNoTracking()
                    .Where(tag => normalizedTagNames.Contains(tag.NormalizedName))
                    .ToDictionaryAsync(tag => tag.NormalizedName, StringComparer.Ordinal, cancellationToken);
            var newTags = new List<Tag>();
            foreach (var name in desiredTags)
            {
                var normalizedName = Tag.Normalize(name);
                if (!tagByNormalizedName.ContainsKey(normalizedName))
                {
                    var tag = Tag.Create(name, importedUtc);
                    tagByNormalizedName.Add(tag.NormalizedName, tag);
                    newTags.Add(tag);
                }
            }

            var articles = new List<Article>(newArticles.Count);
            var occurrences = new List<SourceOccurrence>();
            foreach (var entry in newArticles)
            {
                var article = Article.CreateImportedSaved(
                    entry.IdentityKey,
                    entry.IdentityFingerprint,
                    entry.OriginalUrl,
                    entry.Title,
                    entry.Description,
                    entry.Notes,
                    entry.IsRead,
                    entry.IsFavorite,
                    entry.CreatedUtc,
                    entry.PublishedUtc,
                    importedUtc);
                foreach (var name in entry.Collections)
                {
                    article.Collections.Add(ArticleCollection.Create(article.Id, collectionByName[name].Id));
                }

                foreach (var name in entry.Tags)
                {
                    article.Tags.Add(ArticleTag.Create(article.Id, tagByNormalizedName[Tag.Normalize(name)].Id));
                }

                foreach (var source in entry.Sources)
                {
                    occurrences.Add(SourceOccurrence.Create(
                        article.Id,
                        source.Type,
                        source.SourceKey,
                        Guid.NewGuid().ToString("N"),
                        source.OccurredUtc));
                }

                articles.Add(article);
            }

            dbContext.Collections.AddRange(newCollections);
            dbContext.Tags.AddRange(newTags);
            dbContext.Articles.AddRange(articles);
            dbContext.SourceOccurrences.AddRange(occurrences);
            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return new LibraryImportResult(articles.Count, conflicts);
        }
        catch (DbUpdateException exception) when (
            exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            dbContext.ChangeTracker.Clear();
            throw new LibraryImportConcurrencyException(
                "The library changed while the import was being saved. No import transaction was committed; retry to recalculate conflicts.",
                exception);
        }
    }

    private static IReadOnlyList<PreparedArticle> ValidateDocument(LibraryExportDocument document)
    {
        if (document.Version != LibraryExportService.CurrentVersion)
        {
            throw new LibraryImportValidationException("This library export version is not supported.");
        }

        if (document.ExportedUtc == default || document.Articles is null)
        {
            throw new LibraryImportValidationException("The library export is missing required document data.");
        }

        var entries = new List<PreparedArticle>(document.Articles.Count);
        for (var index = 0; index < document.Articles.Count; index++)
        {
            var article = document.Articles[index];
            var articleNumber = index + 1;
            if (article is null)
            {
                throw InvalidArticle(articleNumber, "record");
            }

            if (article.CreatedUtc == default
                || (article.PublishedUtc.HasValue && article.PublishedUtc.Value == default)
                || article.Collections is null
                || article.Tags is null
                || article.Sources is null)
            {
                throw InvalidArticle(articleNumber, "required fields");
            }

            var originalUrl = RequireText(article.OriginalUrl, articleNumber, "URL");
            if (!string.Equals(article.OriginalUrl, article.OriginalUrl.Trim(), StringComparison.Ordinal))
            {
                throw InvalidArticle(articleNumber, "URL");
            }

            string identityKey;
            try
            {
                identityKey = UrlIdentity.Normalize(originalUrl);
            }
            catch (ArgumentException)
            {
                throw InvalidArticle(articleNumber, "HTTP(S) URL");
            }

            var title = RequireText(article.Title, articleNumber, "title");
            var collections = article.Collections
                .Select(name => RequireText(name, articleNumber, "collection name"))
                .Distinct(StringComparer.Ordinal)
                .ToArray();
            var tags = article.Tags
                .Select(name => RequireText(name, articleNumber, "tag"))
                .GroupBy(Tag.Normalize, StringComparer.Ordinal)
                .Select(group => group.First())
                .ToArray();
            var sources = article.Sources
                .Select(source => ValidateSource(source, articleNumber))
                .ToArray();

            entries.Add(new PreparedArticle(
                articleNumber,
                originalUrl,
                identityKey,
                IdentityFingerprint.Create(identityKey),
                title,
                NormalizeOptionalText(article.Description),
                NormalizeOptionalText(article.Notes),
                article.IsRead,
                article.IsFavorite,
                article.CreatedUtc,
                article.PublishedUtc,
                collections,
                tags,
                sources));
        }

        return entries;
    }

    private static PreparedSource ValidateSource(LibraryExportSource? source, int articleNumber)
    {
        if (source is null || source.OccurredUtc == default)
        {
            throw InvalidArticle(articleNumber, "source history");
        }

        var sourceKey = RequireText(source.SourceKey, articleNumber, "source identifier");
        if (source.Type == "manual-capture" && sourceKey == "manual")
        {
            return new PreparedSource(SourceOccurrenceType.ManualCapture, sourceKey, source.OccurredUtc);
        }

        const string FeedPrefix = "feed-";
        if (source.Type == "feed"
            && sourceKey.StartsWith(FeedPrefix, StringComparison.Ordinal)
            && Guid.TryParseExact(sourceKey[FeedPrefix.Length..], "N", out _))
        {
            return new PreparedSource(SourceOccurrenceType.Feed, sourceKey, source.OccurredUtc);
        }

        throw InvalidArticle(articleNumber, "source history");
    }

    private static string RequireText(string? value, int articleNumber, string field)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw InvalidArticle(articleNumber, field);
        }

        return value.Trim();
    }

    private static string? NormalizeOptionalText(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static LibraryImportValidationException InvalidArticle(int articleNumber, string field) =>
        new($"Article {articleNumber} has an invalid {field}; no records were imported.");

    private sealed record PreparedArticle(
        int ArticleNumber,
        string OriginalUrl,
        string IdentityKey,
        string IdentityFingerprint,
        string Title,
        string? Description,
        string? Notes,
        bool IsRead,
        bool IsFavorite,
        DateTimeOffset CreatedUtc,
        DateTimeOffset? PublishedUtc,
        IReadOnlyList<string> Collections,
        IReadOnlyList<string> Tags,
        IReadOnlyList<PreparedSource> Sources);

    private sealed record PreparedSource(
        SourceOccurrenceType Type,
        string SourceKey,
        DateTimeOffset OccurredUtc);
}

public sealed record LibraryImportConflict(
    int ArticleNumber,
    string OriginalUrl,
    string Reason);

public sealed record LibraryImportResult(
    int ImportedCount,
    IReadOnlyList<LibraryImportConflict> Conflicts);

public sealed class LibraryImportValidationException(string message) : Exception(message);

public sealed class LibraryImportConcurrencyException(string message, Exception innerException)
    : Exception(message, innerException);
