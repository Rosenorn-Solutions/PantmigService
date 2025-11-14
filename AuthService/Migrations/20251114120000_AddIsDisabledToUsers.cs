using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AuthService.Migrations
{
    public partial class AddIsDisabledToUsers : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
            name: "IsDisabled",
            table: "AspNetUsers",
            type: "bit",
            nullable: false,
            defaultValue: false);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
            name: "IsDisabled",
            table: "AspNetUsers");
        }
    }
}
