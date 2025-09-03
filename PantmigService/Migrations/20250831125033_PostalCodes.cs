using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PantmigService.Migrations
{
    /// <inheritdoc />
    public partial class PostalCodes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "PostalCode",
                table: "Cities",
                type: "nvarchar(450)",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Cities_PostalCode",
                table: "Cities",
                column: "PostalCode");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Cities_PostalCode",
                table: "Cities");

            migrationBuilder.DropColumn(
                name: "PostalCode",
                table: "Cities");
        }
    }
}
