using Microsoft.EntityFrameworkCore;
using AuthService.Data;
using AuthService.Entities;
using AuthService.Utils;

namespace AuthService.Services
{
    public class CityResolver : ICityResolver
    {
        private readonly ApplicationDbContext _db;
        public CityResolver(ApplicationDbContext db)
        {
            _db = db;
        }

        public async Task<int> ResolveOrCreateAsync(string input, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(input)) throw new ArgumentException("City is required", nameof(input));

            var slug = SlugHelper.ToSlug(input);
            var city = await _db.Cities.FirstOrDefaultAsync(c => c.Slug == slug, ct);
            if (city is null)
            {
                city = new City { Name = input.Trim(), Slug = slug };
                _db.Cities.Add(city);
                await _db.SaveChangesAsync(ct);
            }
            return city.Id;
        }
    }
}
