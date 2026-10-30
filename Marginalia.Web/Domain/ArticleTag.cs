namespace Marginalia.Web.Domain;

/// <summary>
/// Associates an article with a reusable tag.
/// </summary>
public sealed class ArticleTag
{
    private ArticleTag()
    {
    }

    /// <summary>
    /// Gets the article identifier.
    /// </summary>
    public Guid ArticleId { get; private set; }

    /// <summary>
    /// Gets the article.
    /// </summary>
    public Article Article { get; private set; } = null!;

    /// <summary>
    /// Gets the tag identifier.
    /// </summary>
    public Guid TagId { get; private set; }

    /// <summary>
    /// Gets the tag.
    /// </summary>
    public Tag Tag { get; private set; } = null!;

    /// <summary>
    /// Creates a membership.
    /// </summary>
    /// <param name="articleId">The article identifier.</param>
    /// <param name="tagId">The tag identifier.</param>
    /// <returns>A tag membership.</returns>
    public static ArticleTag Create(Guid articleId, Guid tagId) =>
        new() { ArticleId = articleId, TagId = tagId };
}
