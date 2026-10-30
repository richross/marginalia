using Marginalia.Web.Domain;
using Microsoft.EntityFrameworkCore;

namespace Marginalia.Web.Persistence;

/// <summary>
/// Purges expired dismissed and trashed metadata while retaining identity-only suppression.
/// </summary>
public sealed class ArticleRetentionService(
    LibraryDbContext dbContext,
    TimeProvider timeProvider)
{
    /// <summary>
    /// Purges expired recoverable articles and their cascading source/organization records.
    /// </summary>
    /// <param name="cancellationToken">A token that cancels the operation.</param>
    /// <returns>The number of article records purged.</returns>
    public async Task<int> PurgeExpiredAsync(CancellationToken cancellationToken = default)
    {
        var now = timeProvider.GetUtcNow();
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"""
            INSERT INTO "IdentityTombstones" ("IdentityFingerprint", "PurgedUtc")
            SELECT "IdentityFingerprint", {now}
            FROM "Articles"
            WHERE "State" IN ({(int)ArticleState.Dismissed}, {(int)ArticleState.Trashed})
              AND "PurgeAfterUtc" <= {now}
            ON CONFLICT ("IdentityFingerprint") DO NOTHING
            """,
            cancellationToken);

        var purgedCount = await dbContext.Articles
            .Where(article =>
                (article.State == ArticleState.Dismissed || article.State == ArticleState.Trashed)
                && article.PurgeAfterUtc <= now)
            .ExecuteDeleteAsync(cancellationToken);

        await transaction.CommitAsync(cancellationToken);
        return purgedCount;
    }
}
