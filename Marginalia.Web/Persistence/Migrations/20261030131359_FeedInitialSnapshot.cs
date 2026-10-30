using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Marginalia.Web.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class FeedInitialSnapshot : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "InitialBoundaryOccurrenceKey",
                table: "FeedSubscriptions",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "InitialCutoffUtc",
                table: "FeedSubscriptions",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "InitialSnapshotUtc",
                table: "FeedSubscriptions",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "InitialBoundaryOccurrenceKey",
                table: "FeedSubscriptions");

            migrationBuilder.DropColumn(
                name: "InitialCutoffUtc",
                table: "FeedSubscriptions");

            migrationBuilder.DropColumn(
                name: "InitialSnapshotUtc",
                table: "FeedSubscriptions");
        }
    }
}
