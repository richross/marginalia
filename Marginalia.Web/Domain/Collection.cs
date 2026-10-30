namespace Marginalia.Web.Domain;

/// <summary>
/// An optional named group for saved articles.
/// </summary>
public sealed class Collection
{
    private Collection()
    {
    }

    /// <summary>
    /// Gets the collection identifier.
    /// </summary>
    public Guid Id { get; private set; }

    /// <summary>
    /// Gets the collection's user-visible name.
    /// </summary>
    public string Name { get; private set; } = string.Empty;

    /// <summary>
    /// Gets when the collection was created.
    /// </summary>
    public DateTimeOffset CreatedUtc { get; private set; }

    /// <summary>
    /// Gets the article memberships.
    /// </summary>
    public ICollection<ArticleCollection> Articles { get; private set; } = new List<ArticleCollection>();

    /// <summary>
    /// Creates a collection.
    /// </summary>
    /// <param name="name">The user-supplied collection name.</param>
    /// <param name="now">The current time.</param>
    /// <returns>A new collection.</returns>
    public static Collection Create(string name, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        return new Collection { Id = Guid.NewGuid(), Name = name.Trim(), CreatedUtc = now };
    }
}
