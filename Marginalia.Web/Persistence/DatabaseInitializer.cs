using Microsoft.EntityFrameworkCore;

namespace Marginalia.Web.Persistence;

/// <summary>
/// Initializes PostgreSQL-backed library persistence before serving requests.
/// </summary>
public sealed class DatabaseInitializer(
    IServiceScopeFactory scopeFactory,
    TimeProvider timeProvider) : IDatabaseInitializer
{
    /// <inheritdoc />
    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<LibraryDbContext>();

        await dbContext.Database.MigrateAsync(cancellationToken);

        await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"INSERT INTO \"LibrarySettings\" (\"Id\", \"CreatedUtc\") VALUES ({LibrarySettings.SingletonId}, {timeProvider.GetUtcNow()}) ON CONFLICT (\"Id\") DO NOTHING",
            cancellationToken);
    }
}
