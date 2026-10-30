namespace Marginalia.Web.Application;

/// <summary>
/// Versioned export of the owner's Saved links and organization.
/// </summary>
public sealed record LibraryExportDocument(
    int Version,
    DateTimeOffset ExportedUtc,
    IReadOnlyList<LibraryExportArticle> Articles);

/// <summary>
/// A metadata-only Saved link in a library export.
/// </summary>
public sealed record LibraryExportArticle(
    string OriginalUrl,
    string? Title,
    string? Description,
    string? Notes,
    bool IsRead,
    bool IsFavorite,
    DateTimeOffset CreatedUtc,
    DateTimeOffset? PublishedUtc,
    IReadOnlyList<string> Collections,
    IReadOnlyList<string> Tags,
    IReadOnlyList<LibraryExportSource> Sources);

/// <summary>
/// Permitted provenance for one saved link, excluding source occurrence keys.
/// </summary>
public sealed record LibraryExportSource(
    string Type,
    string SourceKey,
    DateTimeOffset OccurredUtc);
