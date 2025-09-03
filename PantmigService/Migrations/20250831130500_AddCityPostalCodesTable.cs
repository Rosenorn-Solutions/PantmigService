using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Infrastructure;
using PantmigService.Data;

#nullable disable

namespace PantmigService.Migrations
{
    [DbContext(typeof(PantmigDbContext))]
    [Migration("20250831130500_AddCityPostalCodesTable")]
    public partial class AddCityPostalCodesTable : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
IF OBJECT_ID('dbo.CityPostalCodes', 'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[CityPostalCodes] (
        [Id] int NOT NULL IDENTITY(1,1),
        [CityId] int NOT NULL,
        [PostalCode] nvarchar(32) NOT NULL,
        CONSTRAINT [PK_CityPostalCodes] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_CityPostalCodes_Cities_CityId] FOREIGN KEY ([CityId]) REFERENCES [dbo].[Cities] ([Id]) ON DELETE CASCADE
    );
END");

            migrationBuilder.Sql(@"
IF NOT EXISTS (
    SELECT 1 FROM sys.indexes i WHERE i.name = 'IX_CityPostalCodes_CityId_PostalCode' AND i.object_id = OBJECT_ID('dbo.CityPostalCodes')
)
BEGIN
    CREATE UNIQUE INDEX [IX_CityPostalCodes_CityId_PostalCode]
    ON [dbo].[CityPostalCodes]([CityId], [PostalCode]);
END");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
IF OBJECT_ID('dbo.CityPostalCodes', 'U') IS NOT NULL
BEGIN
    DROP TABLE [dbo].[CityPostalCodes];
END");
        }
    }
}
