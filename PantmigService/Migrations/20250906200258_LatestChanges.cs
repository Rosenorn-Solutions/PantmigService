using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PantmigService.Migrations
{
    /// <inheritdoc />
    public partial class LatestChanges : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "RecycleListingApplicants",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ListingId = table.Column<int>(type: "int", nullable: false),
                    RecyclerUserId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    AppliedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RecycleListingApplicants", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RecycleListingApplicants_RecycleListings_ListingId",
                        column: x => x.ListingId,
                        principalTable: "RecycleListings",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_RecycleListingApplicants_ListingId_RecyclerUserId",
                table: "RecycleListingApplicants",
                columns: new[] { "ListingId", "RecyclerUserId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RecycleListingApplicants");
        }
    }
}
