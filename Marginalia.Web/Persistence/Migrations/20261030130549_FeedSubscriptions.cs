using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Marginalia.Web.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class FeedSubscriptions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "FeedSubscriptions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    FeedUrl = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    FeedTitle = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    IsEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    EntityTag = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    LastModifiedUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LastAttemptUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LastSuccessUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    NextDueUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LastError = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    Revision = table.Column<int>(type: "integer", nullable: false),
                    ConsecutiveFailures = table.Column<int>(type: "integer", nullable: false),
                    CreatedUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FeedSubscriptions", x => x.Id);
                    table.CheckConstraint("CK_FeedSubscriptions_ConsecutiveFailures", "\"ConsecutiveFailures\" BETWEEN 0 AND 6");
                });

            migrationBuilder.CreateIndex(
                name: "IX_FeedSubscriptions_FeedUrl",
                table: "FeedSubscriptions",
                column: "FeedUrl",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FeedSubscriptions_IsEnabled_NextDueUtc",
                table: "FeedSubscriptions",
                columns: new[] { "IsEnabled", "NextDueUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "FeedSubscriptions");
        }
    }
}
