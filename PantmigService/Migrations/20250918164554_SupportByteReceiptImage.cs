using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PantmigService.Migrations
{
    /// <inheritdoc />
    public partial class SupportByteReceiptImage : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<byte[]>(
                name: "ReceiptImageBytes",
                table: "RecycleListings",
                type: "varbinary(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ReceiptImageContentType",
                table: "RecycleListings",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ReceiptImageFileName",
                table: "RecycleListings",
                type: "nvarchar(max)",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ReceiptImageBytes",
                table: "RecycleListings");

            migrationBuilder.DropColumn(
                name: "ReceiptImageContentType",
                table: "RecycleListings");

            migrationBuilder.DropColumn(
                name: "ReceiptImageFileName",
                table: "RecycleListings");
        }
    }
}
