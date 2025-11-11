using Microsoft.EntityFrameworkCore;
using PantmigService.Data;
using PantmigService.Entities;
using PantmigShared;
using System.Text;

namespace PantmigService.Seed
{
    public static class PostalCodeCsvSeeder
    {
        // CSV format supported:
        // 1) CityName,PostalCode
        // 2) PostalCode,CityName
        // Header line optional; lines starting with # are ignored.
        public static async Task SeedAsync(PantmigDbContext db, string csvPath, CancellationToken ct = default)
        {
            if (!File.Exists(csvPath)) return;

            // Fast short-circuit: if we already have data and not forced, skip
            var force = string.Equals(Environment.GetEnvironmentVariable("PANTMIG_FORCE_POSTAL_SEED"), "true", StringComparison.OrdinalIgnoreCase);
            if (!force)
            {
                var any = await db.CityPostalCodes.AsNoTracking().Take(1).AnyAsync(ct);
                if (any) return;
            }

            var lines = await File.ReadAllLinesAsync(csvPath, Encoding.UTF8, ct);
            if (lines.Length == 0) return;

            // Track pairs we add during this run to avoid duplicate inserts within one SaveChanges batch
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var raw in lines)
            {
                var line = raw.Trim();
                if (string.IsNullOrWhiteSpace(line)) continue;
                if (line.StartsWith("#")) continue; // comment

                var parts = line.Split(',', StringSplitOptions.TrimEntries);
                if (parts.Length < 2) continue;

                string cityName;
                string postal;

                // Detect order: if first token looks like a postal (all digits, 3-5 length), treat it as postal first
                if (IsLikelyPostal(parts[0]))
                {
                    postal = parts[0];
                    cityName = parts[1];
                }
                else
                {
                    cityName = parts[0];
                    postal = parts[1];
                }

                if (string.IsNullOrWhiteSpace(cityName) || string.IsNullOrWhiteSpace(postal)) continue;

                // Normalize postal (strip spaces)
                postal = new string(postal.Where(char.IsDigit).ToArray());
                if (postal.Length == 0) continue;

                var slug = SlugHelper.ToSlug(cityName);
                var dedupKey = $"{slug}|{postal}";
                if (!seen.Add(dedupKey))
                {
                    continue; // skip duplicates within batch
                }

                var city = await db.Cities.FirstOrDefaultAsync(c => c.Slug == slug, ct);
                if (city is null)
                {
                    city = new City { Name = cityName.Trim(), Slug = slug, ExternalId = CityKey.FromSlug(slug) };
                    db.Cities.Add(city);
                    await db.SaveChangesAsync(ct);
                }

                var exists = await db.CityPostalCodes
                    .AsNoTracking()
                    .AnyAsync(cp => cp.CityId == city.Id && cp.PostalCode == postal, ct);
                if (!exists)
                {
                    db.CityPostalCodes.Add(new CityPostalCode { CityId = city.Id, PostalCode = postal });
                }
            }

            await db.SaveChangesAsync(ct);
        }

        private static bool IsLikelyPostal(string token)
        {
            if (string.IsNullOrWhiteSpace(token)) return false;
            // Accept tokens with digits and optional spaces, like "1055" or "1 055"
            var digits = token.Where(char.IsDigit).Count();
            if (digits < 3 || digits > 5) return false;
            return token.All(ch => char.IsDigit(ch) || char.IsWhiteSpace(ch));
        }
    }
}
