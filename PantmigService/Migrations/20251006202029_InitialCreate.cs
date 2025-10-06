using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PantmigService.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
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
                    Slug = table.Column<string>(type: "nvarchar(450)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Cities", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "CityPostalCodes",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CityId = table.Column<int>(type: "int", nullable: false),
                    PostalCode = table.Column<string>(type: "nvarchar(450)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CityPostalCodes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CityPostalCodes_Cities_CityId",
                        column: x => x.CityId,
                        principalTable: "Cities",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "RecycleListings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Title = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Description = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Location = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    EstimatedValue = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                    AvailableFrom = table.Column<DateTime>(type: "datetime2", nullable: false),
                    AvailableTo = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedByUserId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CityId = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    AssignedRecyclerUserId = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    AcceptedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    PickupConfirmedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ChatSessionId = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ReceiptImageUrl = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ReceiptImageBytes = table.Column<byte[]>(type: "varbinary(max)", nullable: true),
                    ReceiptImageContentType = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ReceiptImageFileName = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ReportedAmount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                    VerifiedAmount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                    CompletedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    MeetingLatitude = table.Column<decimal>(type: "decimal(9,6)", precision: 9, scale: 6, nullable: true),
                    MeetingLongitude = table.Column<decimal>(type: "decimal(9,6)", precision: 9, scale: 6, nullable: true),
                    MeetingSetAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RecycleListings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RecycleListings_Cities_CityId",
                        column: x => x.CityId,
                        principalTable: "Cities",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

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

            migrationBuilder.CreateTable(
                name: "RecycleListingImages",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ListingId = table.Column<int>(type: "int", nullable: false),
                    Data = table.Column<byte[]>(type: "varbinary(max)", nullable: false),
                    ContentType = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    FileName = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Order = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RecycleListingImages", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RecycleListingImages_RecycleListings_ListingId",
                        column: x => x.ListingId,
                        principalTable: "RecycleListings",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "RecycleListingItems",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ListingId = table.Column<int>(type: "int", nullable: false),
                    MaterialType = table.Column<int>(type: "int", nullable: false),
                    Quantity = table.Column<int>(type: "int", nullable: false),
                    DepositClass = table.Column<string>(type: "nvarchar(450)", nullable: true),
                    EstimatedDepositPerUnit = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RecycleListingItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RecycleListingItems_RecycleListings_ListingId",
                        column: x => x.ListingId,
                        principalTable: "RecycleListings",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ChatMessages_ListingId_SentAt",
                table: "ChatMessages",
                columns: new[] { "ListingId", "SentAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Cities_Slug",
                table: "Cities",
                column: "Slug",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CityPostalCodes_CityId_PostalCode",
                table: "CityPostalCodes",
                columns: new[] { "CityId", "PostalCode" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RecycleListingApplicants_ListingId_RecyclerUserId",
                table: "RecycleListingApplicants",
                columns: new[] { "ListingId", "RecyclerUserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RecycleListingImages_ListingId_Order",
                table: "RecycleListingImages",
                columns: new[] { "ListingId", "Order" });

            migrationBuilder.CreateIndex(
                name: "IX_RecycleListingItems_ListingId_MaterialType_DepositClass",
                table: "RecycleListingItems",
                columns: new[] { "ListingId", "MaterialType", "DepositClass" });

            migrationBuilder.CreateIndex(
                name: "IX_RecycleListings_CityId_AvailableFrom_AvailableTo",
                table: "RecycleListings",
                columns: new[] { "CityId", "AvailableFrom", "AvailableTo" });

            migrationBuilder.CreateIndex(
                name: "IX_RecycleListings_CreatedByUserId",
                table: "RecycleListings",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_RecycleListings_EstimatedValue",
                table: "RecycleListings",
                column: "EstimatedValue");

            migrationBuilder.CreateIndex(
                name: "IX_RecycleListings_IsActive_Status_CityId_AvailableFrom",
                table: "RecycleListings",
                columns: new[] { "IsActive", "Status", "CityId", "AvailableFrom" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ChatMessages");

            migrationBuilder.DropTable(
                name: "CityPostalCodes");

            migrationBuilder.DropTable(
                name: "RecycleListingApplicants");

            migrationBuilder.DropTable(
                name: "RecycleListingImages");

            migrationBuilder.DropTable(
                name: "RecycleListingItems");

            migrationBuilder.DropTable(
                name: "RecycleListings");

            migrationBuilder.DropTable(
                name: "Cities");
        }
    }
}
