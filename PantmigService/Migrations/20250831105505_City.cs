using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PantmigService.Migrations
{
    /// <inheritdoc />
    public partial class City : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<decimal>(
                name: "EstimatedAmount",
                table: "RecycleListings",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)");

            migrationBuilder.AddColumn<int>(
                name: "CityId",
                table: "RecycleListings",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "ChatMessages",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ListingId = table.Column<int>(type: "int", nullable: false),
                    ChatSessionId = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    SenderUserId = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Text = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    SentAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ChatMessages", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Cities",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Slug = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    CountryCode = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Cities", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_RecycleListings_CityId_AvailableFrom_AvailableTo",
                table: "RecycleListings",
                columns: new[] { "CityId", "AvailableFrom", "AvailableTo" });

            migrationBuilder.CreateIndex(
                name: "IX_RecycleListings_EstimatedAmount",
                table: "RecycleListings",
                column: "EstimatedAmount");

            migrationBuilder.CreateIndex(
                name: "IX_RecycleListings_IsActive_Status_CityId_AvailableFrom",
                table: "RecycleListings",
                columns: new[] { "IsActive", "Status", "CityId", "AvailableFrom" });

            migrationBuilder.CreateIndex(
                name: "IX_ChatMessages_ListingId_SentAt",
                table: "ChatMessages",
                columns: new[] { "ListingId", "SentAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Cities_Slug",
                table: "Cities",
                column: "Slug",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_RecycleListings_Cities_CityId",
                table: "RecycleListings",
                column: "CityId",
                principalTable: "Cities",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_RecycleListings_Cities_CityId",
                table: "RecycleListings");

            migrationBuilder.DropTable(
                name: "ChatMessages");

            migrationBuilder.DropTable(
                name: "Cities");

            migrationBuilder.DropIndex(
                name: "IX_RecycleListings_CityId_AvailableFrom_AvailableTo",
                table: "RecycleListings");

            migrationBuilder.DropIndex(
                name: "IX_RecycleListings_EstimatedAmount",
                table: "RecycleListings");

            migrationBuilder.DropIndex(
                name: "IX_RecycleListings_IsActive_Status_CityId_AvailableFrom",
                table: "RecycleListings");

            migrationBuilder.DropColumn(
                name: "CityId",
                table: "RecycleListings");

            migrationBuilder.AlterColumn<string>(
                name: "EstimatedAmount",
                table: "RecycleListings",
                type: "nvarchar(max)",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "decimal(18,2)",
                oldPrecision: 18,
                oldScale: 2);
        }
    }
}
