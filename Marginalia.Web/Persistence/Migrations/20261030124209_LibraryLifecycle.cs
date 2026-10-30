using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Marginalia.Web.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class LibraryLifecycle : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Articles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    IdentityKey = table.Column<string>(type: "text", nullable: false),
                    IdentityFingerprint = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    OriginalUrl = table.Column<string>(type: "text", nullable: false),
                    Title = table.Column<string>(type: "text", nullable: true),
                    Description = table.Column<string>(type: "text", nullable: true),
                    Notes = table.Column<string>(type: "text", nullable: true),
                    State = table.Column<int>(type: "integer", nullable: false),
                    IsRead = table.Column<bool>(type: "boolean", nullable: false),
                    IsFavorite = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    DismissedUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    TrashedUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    PurgeAfterUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    PreTrashIsRead = table.Column<bool>(type: "boolean", nullable: true),
                    PreTrashIsFavorite = table.Column<bool>(type: "boolean", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Articles", x => x.Id);
                    table.CheckConstraint("CK_Articles_RecoveryState", "(\"State\" = 2 AND \"DismissedUtc\" IS NOT NULL AND \"TrashedUtc\" IS NULL AND \"PurgeAfterUtc\" IS NOT NULL) OR (\"State\" = 3 AND \"TrashedUtc\" IS NOT NULL AND \"DismissedUtc\" IS NULL AND \"PurgeAfterUtc\" IS NOT NULL) OR (\"State\" IN (0, 1) AND \"DismissedUtc\" IS NULL AND \"TrashedUtc\" IS NULL AND \"PurgeAfterUtc\" IS NULL)");
                    table.CheckConstraint("CK_Articles_State", "\"State\" BETWEEN 0 AND 3");
                    table.CheckConstraint("CK_Articles_TitleRequiredOutsideInbox", "\"State\" = 0 OR (\"Title\" IS NOT NULL AND length(trim(\"Title\")) > 0)");
                });

            migrationBuilder.CreateTable(
                name: "Collections",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    CreatedUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Collections", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "IdentityTombstones",
                columns: table => new
                {
                    IdentityFingerprint = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    PurgedUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IdentityTombstones", x => x.IdentityFingerprint);
                });

            migrationBuilder.CreateTable(
                name: "Tags",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    DisplayName = table.Column<string>(type: "text", nullable: false),
                    NormalizedName = table.Column<string>(type: "text", nullable: false),
                    CreatedUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Tags", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SourceOccurrences",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ArticleId = table.Column<Guid>(type: "uuid", nullable: false),
                    Type = table.Column<int>(type: "integer", nullable: false),
                    SourceKey = table.Column<string>(type: "text", nullable: false),
                    OccurrenceKey = table.Column<string>(type: "text", nullable: false),
                    OccurredUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SourceOccurrences", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SourceOccurrences_Articles_ArticleId",
                        column: x => x.ArticleId,
                        principalTable: "Articles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ArticleCollections",
                columns: table => new
                {
                    ArticleId = table.Column<Guid>(type: "uuid", nullable: false),
                    CollectionId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ArticleCollections", x => new { x.ArticleId, x.CollectionId });
                    table.ForeignKey(
                        name: "FK_ArticleCollections_Articles_ArticleId",
                        column: x => x.ArticleId,
                        principalTable: "Articles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ArticleCollections_Collections_CollectionId",
                        column: x => x.CollectionId,
                        principalTable: "Collections",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ArticleTags",
                columns: table => new
                {
                    ArticleId = table.Column<Guid>(type: "uuid", nullable: false),
                    TagId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ArticleTags", x => new { x.ArticleId, x.TagId });
                    table.ForeignKey(
                        name: "FK_ArticleTags_Articles_ArticleId",
                        column: x => x.ArticleId,
                        principalTable: "Articles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ArticleTags_Tags_TagId",
                        column: x => x.TagId,
                        principalTable: "Tags",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ArticleCollections_CollectionId",
                table: "ArticleCollections",
                column: "CollectionId");

            migrationBuilder.CreateIndex(
                name: "IX_Articles_IdentityKey",
                table: "Articles",
                column: "IdentityKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Articles_PurgeAfterUtc",
                table: "Articles",
                column: "PurgeAfterUtc",
                filter: "\"PurgeAfterUtc\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Articles_State_UpdatedUtc_Id",
                table: "Articles",
                columns: new[] { "State", "UpdatedUtc", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_ArticleTags_TagId",
                table: "ArticleTags",
                column: "TagId");

            migrationBuilder.CreateIndex(
                name: "IX_Collections_Name",
                table: "Collections",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SourceOccurrences_ArticleId_OccurredUtc",
                table: "SourceOccurrences",
                columns: new[] { "ArticleId", "OccurredUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_SourceOccurrences_SourceKey_OccurrenceKey",
                table: "SourceOccurrences",
                columns: new[] { "SourceKey", "OccurrenceKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Tags_NormalizedName",
                table: "Tags",
                column: "NormalizedName",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ArticleCollections");

            migrationBuilder.DropTable(
                name: "ArticleTags");

            migrationBuilder.DropTable(
                name: "IdentityTombstones");

            migrationBuilder.DropTable(
                name: "SourceOccurrences");

            migrationBuilder.DropTable(
                name: "Collections");

            migrationBuilder.DropTable(
                name: "Tags");

            migrationBuilder.DropTable(
                name: "Articles");
        }
    }
}
