using Microsoft.EntityFrameworkCore;
using PantmigService.Data;
using PantmigService.Entities;
using PantmigShared;

namespace PantmigService.Services
{
    public class CityResolver : ICityResolver
    {
        private readonly PantmigDbContext _db;
        public CityResolver(PantmigDbContext db)
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
                // Creation disabled in listing create flow; throw instead so caller returns validation error.
                throw new InvalidOperationException("Unknown city: creation disabled");
            }
            return city.Id;
        }

        public async Task<int> ResolveByExternalIdAsync(Guid externalId, CancellationToken ct = default)
        {
            var city = await _db.Cities.FirstOrDefaultAsync(c => c.ExternalId == externalId, ct);
            if (city is null) throw new InvalidOperationException("Unknown city external id");
            return city.Id;
        }
    }
}
