using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Infrastructure;
using PantmigService.Utils;
using PantmigService.Data;

#nullable disable

namespace PantmigService.Migrations
{
    [DbContext(typeof(PantmigDbContext))]
    [Migration("20250831111000_SeedCitiesFromList")]
    public partial class SeedCitiesFromList : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Raw list supplied by user; insert Name + Slug
            string[] names = new []
            {
                "Aarhus","Odense","Aalborg","Esbjerg","Randers","Horsens","Kolding","Vejle","Roskilde","Herning",
                "Silkeborg","Hørsholm","Helsingør","Næstved","Viborg","Fredericia","Køge","Holstebro","Taastrup","Hillerød",
                "Slagelse","Holbæk","Sønderborg","Svendborg","Hjørring","Nørresundby","Ringsted","Ølstykke-Stenløse","Frederikshavn","Haderslev",
                "Smørumnedre","Birkerød","Farum","Skive","Skanderborg","Nyborg","Solrød Strand","Frederikssund","Nykøbing F","Lillerød",
                "Aabenraa","Middelfart","Kalundborg","Ikast","Hedehusene-Fløng","Korsør","Grenaa","Varde","Rønne","Thisted",
                "Værløse","Odder","Brønderslev","Frederiksværk","Hedensted-Løsning","Nakskov","Dragør","Haslev","Hobro","Vordingborg",
                "Jyllinge","Lystrup","Vejen","Struer","Ringkøbing","Humlebæk","Grindsted","Beder-Malling","Støvring","Galten-Skovby",
                "Nykøbing M","Fredensborg","Helsinge","Sæby","Aars","Løgten-Skødstrup","Hundested","Hørning","Ribe","Hinnerup",
                "Hadsten","Nivå","Sorø","Skjern","Svenstrup","Tønder","Skagen","Bjerringbro","Brande","Vojens",
                "Ry","Ebeltoft","Bramming","Billund","Hammel","Faaborg","Lemvig","Slangerup","Gilleleje","Ringe",
                "Aabybro","Skælskør","Børkop","Hornslet","Assens","Kerteminde","Rødekro","Bellinge","Hellebæk-Ålsgårde","Nordborg",
                "Maribo","Nibe","Hirtshals","Munkebo","Hornbæk-Dronningmølle","Tune","Otterup","Kjellerup","Fensmark","Mårslet",
                "Klarup","Nykøbing S","Strøby Egede","Hadsund","Strib","Vamdrup","Borup","Vodskov","Viby S","Solbjerg",
                "Give","Rudkøbing","Brørup","Sejs-Svejbæk","Sakskøbing","Havdrup","Hjallerup","Gråsten","Høng","Kirke Hvalsø",
                "Padborg","Jyderup","Svogerslev","Sunds","Langeskov","Videbæk","Årslev","Faxe","Lynge-Uggeløse","Tarm",
                "Jægerspris","Bogense","Dianalund","Vildbjerg","Juelsminde","Brædstrup","Harlev","Løgstør","Frederiksberg","Tølløse",
                "Præstø","Assentoft","Ølgod","Jelling","Stege","Virklund","Nexø","Store Heddinge","Gistrup","Hjortshøj",
                "Græsted","Dronninglund","Taulov","Storvorde","Løgumkloster","Farsø","Glamsbjerg","Thurø By","Fjerritslev","Sabro",
                "Vissenbjerg","Rønde","Søndersø","Broager","Toftlund","Aulum","Trige","Nørre Aaby","Skærbæk","Aarup",
                "Lund","Sundby","Frejlev","Augustenborg","Auning","Bjæverskov","Søften","Asnæs","Lejre","Skibby",
                "Ejby","Vindinge","Sindal","Brejning","Sankt Klemens","Vinderup","Holsted","Skørping","Egebjerg","Blovstrød",
                "Christiansfeld","Lunderskov","Tørring","Pandrup","Tjæreborg","Hårlev","Ganløse","Vestbjerg","Langå","Skævinge",
                "Højslev Stationsby","Oksbøl","Hvide Sande","Svinninge","Aalestrup","Rødding","Rønnede","Nordby","Tinglev","Gundsømagle",
                "Hurup","Høruphav","Ryomgård","Brovst","Kibæk","Skibet","Ullerslev","Vrå","Østbirk","Mariager",
                "Forlev","Stoholm","Vester Hassing","Vadum","Arden","Stevnstrup","Haarby","Hals","Liseleje","Starup",
                "Nyråd","Faxe Ladeplads","Gram","Guderup","Hørve","Kås","København","Albertslund","Brøndby","Gentofte",
                "Gladsaxe","Glostrup","Herlev","Hvidovre","Lyngby-Taarbæk","Rødovre","Tårnby","Vallensbæk","Ishøj","Greve Strand",
                "Ballerup","Søllerød","Gribskov","Halsnæs","Rudersdal","Egedal","Allerød","Furesø","Høje Taastrup","Bornholm"
            };

            foreach (var name in names)
            {
                var slug = SlugHelper.ToSlug(name);
                var safeName = name.Replace("'", "''");
                migrationBuilder.Sql($"IF NOT EXISTS (SELECT 1 FROM Cities WHERE Slug = '{slug}') INSERT INTO Cities (Name, Slug) VALUES ('{safeName}', '{slug}')");
            }
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Optionally remove seeded cities - keeping data on Down is usually fine; no-op
        }
    }
}
