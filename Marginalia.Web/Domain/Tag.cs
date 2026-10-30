namespace Marginalia.Web.Domain;

/// <summary>
/// A reusable, case-insensitively matched label for library articles.
/// </summary>
public sealed class Tag
{
    private Tag()
    {
    }

    /// <summary>
    /// Gets the tag identifier.
    /// </summary>
    public Guid Id { get; private set; }

    /// <summary>
    /// Gets the first preserved display spelling of the tag.
    /// </summary>
    public string DisplayName { get; private set; } = string.Empty;

    /// <summary>
    /// Gets the trimmed case-insensitive identity used for uniqueness.
    /// </summary>
    public string NormalizedName { get; private set; } = string.Empty;

    /// <summary>
    /// Gets when the tag was created.
    /// </summary>
    public DateTimeOffset CreatedUtc { get; private set; }

    /// <summary>
    /// Gets the article memberships.
    /// </summary>
    public ICollection<ArticleTag> Articles { get; private set; } = new List<ArticleTag>();

    /// <summary>
    /// Creates a tag while preserving its initial display spelling.
    /// </summary>
    /// <param name="name">The user-supplied tag name.</param>
    /// <param name="now">The current time.</param>
    /// <returns>A new tag.</returns>
    public static Tag Create(string name, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        var displayName = name.Trim();
        return new Tag
        {
            Id = Guid.NewGuid(),
            DisplayName = displayName,
            NormalizedName = Normalize(displayName),
            CreatedUtc = now
        };
    }

    /// <summary>
    /// Creates the unique identity for a tag without changing its display spelling.
    /// </summary>
    /// <param name="name">A tag name.</param>
    /// <returns>The trimmed, invariant upper-case tag identity.</returns>
    public static string Normalize(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        return name.Trim().ToUpperInvariant();
    }
}
