namespace Marginalia.Web.Domain;

/// <summary>
/// Associates an article with an optional collection.
/// </summary>
public sealed class ArticleCollection
{
    private ArticleCollection()
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
    /// Gets the collection identifier.
    /// </summary>
    public Guid CollectionId { get; private set; }

    /// <summary>
    /// Gets the collection.
    /// </summary>
    public Collection Collection { get; private set; } = null!;

    /// <summary>
    /// Creates a membership.
    /// </summary>
    /// <param name="articleId">The article identifier.</param>
    /// <param name="collectionId">The collection identifier.</param>
    /// <returns>A collection membership.</returns>
    public static ArticleCollection Create(Guid articleId, Guid collectionId) =>
        new() { ArticleId = articleId, CollectionId = collectionId };
}
