using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PantmigService.Migrations
{
    /// <inheritdoc />
    public partial class RecycleMeetingPoint : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "MeetingLatitude",
                table: "RecycleListings",
                type: "decimal(9,6)",
                precision: 9,
                scale: 6,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "MeetingLongitude",
                table: "RecycleListings",
                type: "decimal(9,6)",
                precision: 9,
                scale: 6,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "MeetingSetAt",
                table: "RecycleListings",
                type: "datetime2",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "MeetingLatitude",
                table: "RecycleListings");

            migrationBuilder.DropColumn(
                name: "MeetingLongitude",
                table: "RecycleListings");

            migrationBuilder.DropColumn(
                name: "MeetingSetAt",
                table: "RecycleListings");
        }
    }
}
