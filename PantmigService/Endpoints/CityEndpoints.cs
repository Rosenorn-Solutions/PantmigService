using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.EntityFrameworkCore;
using PantmigService.Data;

namespace PantmigService.Endpoints
{
    public static class CityEndpoints
    {
        public record CitySearchResult(int Id, string Name, string[] PostalCodes);

        public static IEndpointRouteBuilder MapCityEndpoints(this IEndpointRouteBuilder app)
        {
            var group = app.MapGroup("/cities").WithTags("Cities");

            // Open endpoint to search cities by name or postal code (typeahead)
            group.MapGet("/search", async (string? q, PantmigDbContext db, int take = 15) =>
            {
                if (string.IsNullOrWhiteSpace(q)) return Results.Ok(Array.Empty<CitySearchResult>());
                q = q.Trim();
                if (take is < 1 or > 50) take = 15;

                // Find cities by postal startswith or name contains (case-insensitive)
                var postalQuery = db.CityPostalCodes
                    .Where(cp => cp.PostalCode.StartsWith(q))
                    .Select(cp => cp.CityId);

                var nameQuery = db.Cities
                    .Where(c => EF.Functions.Like(c.Name, "%" + q + "%"))
                    .Select(c => c.Id);

                var cityIds = await postalQuery
                    .Union(nameQuery)
                    .Distinct()
                    .Take(take)
                    .ToListAsync();

                if (cityIds.Count == 0) return Results.Ok(Array.Empty<CitySearchResult>());

                // Load names
                var cities = await db.Cities
                    .Where(c => cityIds.Contains(c.Id))
                    .Select(c => new { c.Id, c.Name })
                    .ToListAsync();

                // Load postal codes for these cities
                var postals = await db.CityPostalCodes
                    .Where(cp => cityIds.Contains(cp.CityId))
                    .GroupBy(cp => cp.CityId)
                    .Select(g => new { CityId = g.Key, PostalCodes = g.Select(x => x.PostalCode).OrderBy(x => x).ToArray() })
                    .ToListAsync();

                var postalDict = postals.ToDictionary(x => x.CityId, x => x.PostalCodes);

                var results = cities
                    .Select(c => new CitySearchResult(c.Id, c.Name, postalDict.TryGetValue(c.Id, out var arr) ? arr : Array.Empty<string>()))
                    .OrderBy(c => c.Name)
                    .ToList();

                return Results.Ok(results);
            })
            .WithOpenApi(op =>
            {
                op.OperationId = "Cities_Search";
                op.Summary = "Search cities for typeahead";
                op.Description = "Search by city name or postal code. Open to all callers.";
                return op;
            })
            .Produces<IEnumerable<CitySearchResult>>(StatusCodes.Status200OK, contentType: "application/json");

            return app;
        }
    }
}
