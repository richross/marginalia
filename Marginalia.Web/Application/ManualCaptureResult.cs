namespace Marginalia.Web.Application;

/// <summary>
/// The result of resolving a manually captured URL against retained and purged identities.
/// </summary>
/// <param name="Outcome">The next action or completed outcome.</param>
/// <param name="Url">The manually supplied URL.</param>
/// <param name="ExistingArticle">Prior metadata when it is still retained.</param>
public sealed record ManualCaptureResult(
    ManualCaptureOutcome Outcome,
    string Url,
    ArticleRecoveryDetails? ExistingArticle);

/// <summary>
/// Retained information shown when manual capture matches a dismissed or trashed article.
/// </summary>
/// <param name="Article">The bounded article details.</param>
/// <param name="SourceOccurrences">The retained non-sensitive source history.</param>
public sealed record ArticleRecoveryDetails(
    ArticleListItem Article,
    IReadOnlyList<SourceOccurrenceSummary> SourceOccurrences);

/// <summary>
/// A bounded, non-sensitive projection of an article's source history.
/// </summary>
/// <param name="Type">The source occurrence kind.</param>
/// <param name="SourceKey">The opaque source identifier.</param>
/// <param name="OccurredUtc">When the occurrence was recorded.</param>
public sealed record SourceOccurrenceSummary(
    string Type,
    string SourceKey,
    DateTimeOffset OccurredUtc);
