using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Infrastructure;
using PantmigService.Utils;
using PantmigService.Data;

#nullable disable

namespace PantmigService.Migrations
{
    [DbContext(typeof(PantmigDbContext))]
    [Migration("20250831132000_SeedCityPostalCodes")]
    public partial class SeedCityPostalCodes : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // NOTE: This seed contains a minimal set. For full coverage, prefer importing the official PostDanmark dataset
            // and inserting to CityPostalCodes, or implement a provider that reads from a bundled CSV.
            var map = new (string Name, string[] Postals)[]
            {
                ("Aarhus", new[]{"8000","8200","8210","8240","8220"}),
                ("Odense", new[]{"5000","5200","5210","5220"}),
                ("Aalborg", new[]{"9000","9200","9210","9220"}),
                ("Esbjerg", new[]{"6700","6710","6715","6720"}),
                ("Randers", new[]{"8900","8920","8930","8940"}),
                ("København", new[]{"1000","1050","1100","1200","1300","1400","1500","1600","1700","1800","2100","2200","2300","2400","2450","2500","2600","2610","2620","2630","2640","2650","2700","2720","2730","2740","2750","2760","2770"}),
                ("Ølstykke-Stenløse", new[]{"3650","3660"}),
                ("Hedehusene-Fløng", new[]{"2640","2645"}),
                ("Hellebæk-Ålsgårde", new[]{"3140","3150"}),
                ("Hornbæk-Dronningmølle", new[]{"3100","3120"}),
                ("Lyngby-Taarbæk", new[]{"2800","2830"}),
                ("Greve Strand", new[]{"2670"}),
                ("Frederiksberg", new[]{"1800","1810","1820","1900","1910","1920","1950","1960","1970","1999"}),
                ("Bornholm", new[]{"3700","3720","3730","3740","3751","3760","3770","3782","3790"}),
            };

            foreach (var entry in map)
            {
                var slug = PantmigService.Utils.SlugHelper.ToSlug(entry.Name);
                foreach (var pc in entry.Postals)
                {
                    migrationBuilder.Sql($"INSERT INTO CityPostalCodes (CityId, PostalCode) SELECT Id, '{pc}' FROM Cities WHERE Slug = '{slug}' AND NOT EXISTS (SELECT 1 FROM CityPostalCodes WHERE PostalCode = '{pc}' AND CityId = Cities.Id)");
                }
            }
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DELETE FROM CityPostalCodes");
        }
    }
}
