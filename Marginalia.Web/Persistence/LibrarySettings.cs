namespace Marginalia.Web.Persistence;

/// <summary>
/// Stores the persistent creation timestamp for the single-user library.
/// </summary>
public sealed class LibrarySettings
{
    /// <summary>
    /// The fixed key used for the single library settings row.
    /// </summary>
    public const int SingletonId = 1;

    private LibrarySettings()
    {
    }

    /// <summary>
    /// Gets the singleton row identifier.
    /// </summary>
    public int Id { get; private set; }

    /// <summary>
    /// Gets the time the local library was first initialized.
    /// </summary>
    public DateTimeOffset CreatedUtc { get; private set; }

    /// <summary>
    /// Creates the singleton settings row.
    /// </summary>
    /// <param name="createdUtc">The current UTC time.</param>
    /// <returns>The new library settings row.</returns>
    public static LibrarySettings Create(DateTimeOffset createdUtc) =>
        new() { Id = SingletonId, CreatedUtc = createdUtc };
}
