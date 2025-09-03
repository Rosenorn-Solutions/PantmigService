using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Infrastructure;
using PantmigService.Data;

#nullable disable

namespace PantmigService.Migrations
{
    [DbContext(typeof(PantmigDbContext))]
    [Migration("20250831120000_AddPostalCodeToCities")]
    public partial class AddPostalCodeToCities : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Add column if it doesn't exist
            migrationBuilder.Sql(@"
IF COL_LENGTH('dbo.Cities', 'PostalCode') IS NULL
BEGIN
    ALTER TABLE [dbo].[Cities] ADD [PostalCode] nvarchar(16) NULL;
END");

            // Create index if it doesn't exist
            migrationBuilder.Sql(@"
IF NOT EXISTS (
    SELECT 1 FROM sys.indexes i
    WHERE i.name = 'IX_Cities_PostalCode' AND i.object_id = OBJECT_ID('dbo.Cities')
)
BEGIN
    CREATE INDEX [IX_Cities_PostalCode] ON [dbo].[Cities] ([PostalCode]);
END");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Drop index if exists
            migrationBuilder.Sql(@"
IF EXISTS (
    SELECT 1 FROM sys.indexes i
    WHERE i.name = 'IX_Cities_PostalCode' AND i.object_id = OBJECT_ID('dbo.Cities')
)
BEGIN
    DROP INDEX [IX_Cities_PostalCode] ON [dbo].[Cities];
END");

            // Drop column if exists
            migrationBuilder.Sql(@"
IF COL_LENGTH('dbo.Cities', 'PostalCode') IS NOT NULL
BEGIN
    ALTER TABLE [dbo].[Cities] DROP COLUMN [PostalCode];
END");
        }
    }
}
