using Microsoft.EntityFrameworkCore;
using PantmigService.Data;
using PantmigService.Entities;
using PantmigShared;

namespace PantmigService.Services;

public class StatisticsService(PantmigDbContext db) : IStatisticsService
{
    private readonly PantmigDbContext _db = db;
    private const decimal AverageDeposit = 2.33m;

    public async Task<DonorStatisticsResult> GetDonorStatisticsAsync(string donatorUserId, CancellationToken ct = default)
    {
        var baseQuery = _db.RecycleListings
            .AsNoTracking()
            .Where(l => l.CreatedByUserId == donatorUserId && l.Status == ListingStatus.Completed);

        var listingCount = await baseQuery.CountAsync(ct);

        var totalItems = await (from i in _db.RecycleListingItems.AsNoTracking()
                                join l in baseQuery on i.ListingId equals l.Id
                                select (int?)i.Quantity).SumAsync(ct) ?? 0;

        var totalApproximateWorth = Math.Round(totalItems * AverageDeposit, 2, MidpointRounding.AwayFromZero);

        return new DonorStatisticsResult(listingCount, totalItems, totalApproximateWorth);
    }

    public async Task<RecyclerStatisticsResult> GetRecyclerStatisticsAsync(string recyclerUserId, CancellationToken ct = default)
    {
        var baseQuery = _db.RecycleListings
            .AsNoTracking()
            .Where(l => l.AssignedRecyclerUserId == recyclerUserId && l.Status == ListingStatus.Completed);

        var listingCount = await baseQuery.CountAsync(ct);

        var totalItems = await (from i in _db.RecycleListingItems.AsNoTracking()
                                join l in baseQuery on i.ListingId equals l.Id
                                select (int?)i.Quantity).SumAsync(ct) ?? 0;

        var breakdown = await (from i in _db.RecycleListingItems.AsNoTracking()
                               join l in baseQuery on i.ListingId equals l.Id
                               group i by i.MaterialType into g
                               select new RecyclerMaterialBreakdown(g.Key, g.Sum(x => x.Quantity)))
                               .ToListAsync(ct);

        var totalApproximateWorth = Math.Round(totalItems * AverageDeposit, 2, MidpointRounding.AwayFromZero);

        var totalReportedAmount = await baseQuery
            .Select(l => (decimal?)(l.ReportedAmount ?? 0m))
            .SumAsync(ct) ?? 0m;

        return new RecyclerStatisticsResult(listingCount, totalItems, totalApproximateWorth, totalReportedAmount, breakdown);
    }

    public async Task<CityStatisticsResult?> GetCityStatisticsAsync(string city, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(city)) return null;

        var slug = SlugHelper.ToSlug(city);

        var cityRow = await _db.Cities
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Slug == slug || EF.Functions.Like(c.Name, city), ct);

        if (cityRow is null)
        {
            return null;
        }

        var baseQuery = _db.RecycleListings
            .AsNoTracking()
            .Where(l => l.CityId == cityRow.Id && l.Status == ListingStatus.Completed);

        var totalItems = await (from i in _db.RecycleListingItems.AsNoTracking()
                                join l in baseQuery on i.ListingId equals l.Id
                                select (int?)i.Quantity).SumAsync(ct) ?? 0;

        var breakdown = await (from i in _db.RecycleListingItems.AsNoTracking()
                               join l in baseQuery on i.ListingId equals l.Id
                               group i by i.MaterialType into g
                               select new RecyclerMaterialBreakdown(g.Key, g.Sum(x => x.Quantity)))
                               .ToListAsync(ct);

        var totalApproximateWorth = Math.Round(totalItems * AverageDeposit, 2, MidpointRounding.AwayFromZero);

        return new CityStatisticsResult(cityRow.Name, totalItems, totalApproximateWorth, breakdown);
    }
}
