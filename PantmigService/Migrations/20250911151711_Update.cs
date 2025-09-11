using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PantmigService.Migrations
{
    /// <inheritdoc />
    public partial class Update : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_RecycleListings_EstimatedAmount",
                table: "RecycleListings");

            migrationBuilder.AlterColumn<decimal>(
                name: "EstimatedValue",
                table: "RecycleListings",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "EstimatedAmount",
                table: "RecycleListings",
                type: "nvarchar(max)",
                nullable: true,
                oldClrType: typeof(decimal),
                oldType: "decimal(18,2)",
                oldPrecision: 18,
                oldScale: 2);

            migrationBuilder.CreateIndex(
                name: "IX_RecycleListings_EstimatedValue",
                table: "RecycleListings",
                column: "EstimatedValue");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_RecycleListings_EstimatedValue",
                table: "RecycleListings");

            migrationBuilder.AlterColumn<string>(
                name: "EstimatedValue",
                table: "RecycleListings",
                type: "nvarchar(max)",
                nullable: true,
                oldClrType: typeof(decimal),
                oldType: "decimal(18,2)",
                oldPrecision: 18,
                oldScale: 2,
                oldNullable: true);

            migrationBuilder.AlterColumn<decimal>(
                name: "EstimatedAmount",
                table: "RecycleListings",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)",
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_RecycleListings_EstimatedAmount",
                table: "RecycleListings",
                column: "EstimatedAmount");
        }
    }
}
