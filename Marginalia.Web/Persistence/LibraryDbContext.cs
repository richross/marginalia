using Microsoft.EntityFrameworkCore;
using Marginalia.Web.Domain;

namespace Marginalia.Web.Persistence;

/// <summary>
/// Provides persistence for the private Marginalia library.
/// </summary>
public sealed class LibraryDbContext(DbContextOptions<LibraryDbContext> options) : DbContext(options)
{
    /// <summary>
    /// Gets the singleton settings record for this library.
    /// </summary>
    public DbSet<LibrarySettings> LibrarySettings => Set<LibrarySettings>();

    /// <summary>
    /// Gets the metadata-only articles in the library.
    /// </summary>
    public DbSet<Article> Articles => Set<Article>();

    /// <summary>
    /// Gets the recorded source occurrences for retained articles.
    /// </summary>
    public DbSet<SourceOccurrence> SourceOccurrences => Set<SourceOccurrence>();

    /// <summary>
    /// Gets the collections in the library.
    /// </summary>
    public DbSet<Collection> Collections => Set<Collection>();

    /// <summary>
    /// Gets the reusable tags in the library.
    /// </summary>
    public DbSet<Tag> Tags => Set<Tag>();

    /// <summary>
    /// Gets the identity-only records for expired articles.
    /// </summary>
    public DbSet<IdentityTombstone> IdentityTombstones => Set<IdentityTombstone>();

    /// <summary>
    /// Gets the individual RSS and Atom feed subscriptions.
    /// </summary>
    public DbSet<FeedSubscription> FeedSubscriptions => Set<FeedSubscription>();

    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<LibrarySettings>(entity =>
        {
            entity.ToTable("LibrarySettings");
            entity.HasKey(settings => settings.Id);
            entity.Property(settings => settings.Id).ValueGeneratedNever();
            entity.Property(settings => settings.CreatedUtc).IsRequired();
        });

        modelBuilder.Entity<Article>(entity =>
        {
            entity.ToTable("Articles", table =>
            {
                table.HasCheckConstraint("CK_Articles_State", "\"State\" BETWEEN 0 AND 3");
                table.HasCheckConstraint(
                    "CK_Articles_TitleRequiredOutsideInbox",
                    "\"State\" = 0 OR (\"Title\" IS NOT NULL AND length(trim(\"Title\")) > 0)");
                table.HasCheckConstraint(
                    "CK_Articles_RecoveryState",
                    "(\"State\" = 2 AND \"DismissedUtc\" IS NOT NULL AND \"TrashedUtc\" IS NULL AND \"PurgeAfterUtc\" IS NOT NULL) OR " +
                    "(\"State\" = 3 AND \"TrashedUtc\" IS NOT NULL AND \"DismissedUtc\" IS NULL AND \"PurgeAfterUtc\" IS NOT NULL) OR " +
                    "(\"State\" IN (0, 1) AND \"DismissedUtc\" IS NULL AND \"TrashedUtc\" IS NULL AND \"PurgeAfterUtc\" IS NULL)");
            });
            entity.HasKey(article => article.Id);
            entity.Property(article => article.IdentityKey).IsRequired();
            entity.Property(article => article.IdentityFingerprint).IsRequired().HasMaxLength(64);
            entity.Property(article => article.OriginalUrl).IsRequired();
            entity.Property(article => article.State).HasConversion<int>();
            entity.HasIndex(article => article.IdentityKey).IsUnique();
            entity.HasIndex(article => new { article.State, article.UpdatedUtc, article.Id });
            entity.HasIndex(article => article.PurgeAfterUtc)
                .HasFilter("\"PurgeAfterUtc\" IS NOT NULL");
            entity.HasMany(article => article.SourceOccurrences)
                .WithOne(occurrence => occurrence.Article)
                .HasForeignKey(occurrence => occurrence.ArticleId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasMany(article => article.Collections)
                .WithOne(membership => membership.Article)
                .HasForeignKey(membership => membership.ArticleId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasMany(article => article.Tags)
                .WithOne(membership => membership.Article)
                .HasForeignKey(membership => membership.ArticleId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<SourceOccurrence>(entity =>
        {
            entity.ToTable("SourceOccurrences");
            entity.HasKey(occurrence => occurrence.Id);
            entity.Property(occurrence => occurrence.Type).HasConversion<int>();
            entity.Property(occurrence => occurrence.SourceKey).IsRequired();
            entity.Property(occurrence => occurrence.OccurrenceKey).IsRequired();
            entity.HasIndex(occurrence => new { occurrence.SourceKey, occurrence.OccurrenceKey }).IsUnique();
            entity.HasIndex(occurrence => new { occurrence.ArticleId, occurrence.OccurredUtc });
        });

        modelBuilder.Entity<Collection>(entity =>
        {
            entity.ToTable("Collections");
            entity.HasKey(collection => collection.Id);
            entity.Property(collection => collection.Name).IsRequired();
            entity.HasIndex(collection => collection.Name).IsUnique();
            entity.HasMany(collection => collection.Articles)
                .WithOne(membership => membership.Collection)
                .HasForeignKey(membership => membership.CollectionId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<ArticleCollection>(entity =>
        {
            entity.ToTable("ArticleCollections");
            entity.HasKey(membership => new { membership.ArticleId, membership.CollectionId });
            entity.HasIndex(membership => membership.CollectionId);
        });

        modelBuilder.Entity<Tag>(entity =>
        {
            entity.ToTable("Tags");
            entity.HasKey(tag => tag.Id);
            entity.Property(tag => tag.DisplayName).IsRequired();
            entity.Property(tag => tag.NormalizedName).IsRequired();
            entity.HasIndex(tag => tag.NormalizedName).IsUnique();
            entity.HasMany(tag => tag.Articles)
                .WithOne(membership => membership.Tag)
                .HasForeignKey(membership => membership.TagId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<ArticleTag>(entity =>
        {
            entity.ToTable("ArticleTags");
            entity.HasKey(membership => new { membership.ArticleId, membership.TagId });
            entity.HasIndex(membership => membership.TagId);
        });

        modelBuilder.Entity<IdentityTombstone>(entity =>
        {
            entity.ToTable("IdentityTombstones");
            entity.HasKey(tombstone => tombstone.IdentityFingerprint);
            entity.Property(tombstone => tombstone.IdentityFingerprint).HasMaxLength(64);
            entity.Property(tombstone => tombstone.PurgedUtc).IsRequired();
        });

        modelBuilder.Entity<FeedSubscription>(entity =>
        {
            entity.ToTable("FeedSubscriptions", table =>
                table.HasCheckConstraint(
                    "CK_FeedSubscriptions_ConsecutiveFailures",
                    "\"ConsecutiveFailures\" BETWEEN 0 AND 6"));
            entity.HasKey(feed => feed.Id);
            entity.Property(feed => feed.FeedUrl).IsRequired().HasMaxLength(2_048);
            entity.Property(feed => feed.FeedTitle).HasMaxLength(500);
            entity.Property(feed => feed.InitialBoundaryOccurrenceKey).HasMaxLength(64);
            entity.Property(feed => feed.EntityTag).HasMaxLength(512);
            entity.Property(feed => feed.LastError).HasMaxLength(256);
            entity.Property(feed => feed.Revision).IsConcurrencyToken();
            entity.HasIndex(feed => feed.FeedUrl).IsUnique();
            entity.HasIndex(feed => new { feed.IsEnabled, feed.NextDueUtc });
        });
    }
}
