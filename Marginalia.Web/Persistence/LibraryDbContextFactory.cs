using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Marginalia.Web.Persistence;

/// <summary>
/// Creates the EF Core context for migration tooling without requiring credentials.
/// </summary>
public sealed class LibraryDbContextFactory : IDesignTimeDbContextFactory<LibraryDbContext>
{
    /// <inheritdoc />
    public LibraryDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<LibraryDbContext>()
            .UseNpgsql("Host=localhost;Database=marginalia")
            .Options;

        return new LibraryDbContext(options);
    }
}
