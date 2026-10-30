namespace Marginalia.Web.Domain;

/// <summary>
/// Records one idempotent encounter of a retained article through a source.
/// </summary>
public sealed class SourceOccurrence
{
    private SourceOccurrence()
    {
    }

    /// <summary>
    /// Gets the occurrence identifier.
    /// </summary>
    public Guid Id { get; private set; }

    /// <summary>
    /// Gets the associated article identifier.
    /// </summary>
    public Guid ArticleId { get; private set; }

    /// <summary>
    /// Gets the associated article.
    /// </summary>
    public Article Article { get; private set; } = null!;

    /// <summary>
    /// Gets whether the source is a feed or a manual capture.
    /// </summary>
    public SourceOccurrenceType Type { get; private set; }

    /// <summary>
    /// Gets a non-sensitive stable source identifier.
    /// </summary>
    public string SourceKey { get; private set; } = string.Empty;

    /// <summary>
    /// Gets the source-specific idempotency key.
    /// </summary>
    public string OccurrenceKey { get; private set; } = string.Empty;

    /// <summary>
    /// Gets when the source occurrence was recorded.
    /// </summary>
    public DateTimeOffset OccurredUtc { get; private set; }

    /// <summary>
    /// Creates a source occurrence.
    /// </summary>
    /// <param name="articleId">The retained article identifier.</param>
    /// <param name="type">The source type.</param>
    /// <param name="sourceKey">A non-sensitive stable source identifier.</param>
    /// <param name="occurrenceKey">A stable idempotency key from the source.</param>
    /// <param name="occurredUtc">When the article was encountered.</param>
    /// <returns>A new source occurrence.</returns>
    public static SourceOccurrence Create(
        Guid articleId,
        SourceOccurrenceType type,
        string sourceKey,
        string occurrenceKey,
        DateTimeOffset occurredUtc)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(occurrenceKey);

        return new SourceOccurrence
        {
            Id = Guid.NewGuid(),
            ArticleId = articleId,
            Type = type,
            SourceKey = sourceKey.Trim(),
            OccurrenceKey = occurrenceKey.Trim(),
            OccurredUtc = occurredUtc
        };
    }
}
