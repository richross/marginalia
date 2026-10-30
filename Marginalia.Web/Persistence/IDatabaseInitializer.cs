namespace Marginalia.Web.Persistence;

/// <summary>
/// Applies the database schema and initializes the library's persistent settings.
/// </summary>
public interface IDatabaseInitializer
{
    /// <summary>
    /// Applies migrations and creates the singleton settings row when absent.
    /// </summary>
    /// <param name="cancellationToken">A token that cancels initialization.</param>
    /// <returns>A task representing the initialization.</returns>
    Task InitializeAsync(CancellationToken cancellationToken = default);
}
