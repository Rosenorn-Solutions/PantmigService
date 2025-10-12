using Microsoft.EntityFrameworkCore;
using PantmigService.Data;
using PantmigService.Entities;
using PantmigService.Services;

namespace PantMigTesting.Services;

public class StatisticsServiceTests
{
    private static PantmigDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<PantmigDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new PantmigDbContext(options);
    }

    private static async Task SeedAsync(PantmigDbContext db)
    {
        // Seed cities
        var city1 = new City { Id = 1, Name = "Copenhagen", Slug = "copenhagen" };
        var city2 = new City { Id = 2, Name = "Aarhus", Slug = "aarhus" };
        db.Cities.AddRange(city1, city2);
        await db.SaveChangesAsync();

        var l1 = new RecycleListing
        {
            Title = "A",
            Description = "",
            CityId = 1,
            CreatedByUserId = "donor-1",
            Status = ListingStatus.Completed,
            AssignedRecyclerUserId = "recycler-1",
            Items = [ new() { MaterialType = RecycleMaterialType.Can, Quantity = 10 } ],
            ReportedAmount = 20m,
            IsActive = false
        };
        var l2 = new RecycleListing
        {
            Title = "B",
            Description = "",
            CityId = 1,
            CreatedByUserId = "donor-1",
            Status = ListingStatus.Created,
            Items = [ new() { MaterialType = RecycleMaterialType.PlasticBottle, Quantity = 5 } ],
            IsActive = true
        };
        var l3 = new RecycleListing
        {
            Title = "C",
            Description = "",
            CityId = 1,
            CreatedByUserId = "donor-2",
            Status = ListingStatus.Completed,
            AssignedRecyclerUserId = "recycler-1",
            Items = [ new() { MaterialType = RecycleMaterialType.GlassBottle, Quantity = 4 } ],
            ReportedAmount = 5m,
            IsActive = false
        };
        // Another completed listing in a different city to validate city filtering
        var l4 = new RecycleListing
        {
            Title = "D",
            Description = "",
            CityId = 2,
            CreatedByUserId = "donor-3",
            Status = ListingStatus.Completed,
            AssignedRecyclerUserId = "recycler-2",
            Items = [ new() { MaterialType = RecycleMaterialType.PlasticBottle, Quantity = 6 } ],
            ReportedAmount = 12m,
            IsActive = false
        };
        db.RecycleListings.AddRange(l1, l2, l3, l4);
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task Donor_Stats_Sums_Only_Completed_For_Owner()
    {
        using var db = CreateDb();
        await SeedAsync(db);
        var svc = new StatisticsService(db);

        var stats = await svc.GetDonorStatisticsAsync("donor-1");
        Assert.Equal(1, stats.ListingCount);
        Assert.Equal(10, stats.TotalItems);
        Assert.Equal(23.30m, stats.TotalApproximateWorth);

        var other = await svc.GetDonorStatisticsAsync("donor-2");
        Assert.Equal(1, other.ListingCount);
        Assert.Equal(4, other.TotalItems);
        Assert.Equal(9.32m, other.TotalApproximateWorth);
    }

    [Fact]
    public async Task Recycler_Stats_Breakdown_And_Reported_Sum()
    {
        using var db = CreateDb();
        await SeedAsync(db);
        var svc = new StatisticsService(db);

        var stats = await svc.GetRecyclerStatisticsAsync("recycler-1");
        Assert.Equal(2, stats.ListingCount);
        Assert.Equal(14, stats.TotalItems);
        Assert.Equal(32.62m, stats.TotalApproximateWorth);
        Assert.Equal(25m, stats.TotalReportedAmount);
        Assert.Contains(stats.Breakdown, b => b.Material == RecycleMaterialType.Can && b.Quantity == 10);
        Assert.Contains(stats.Breakdown, b => b.Material == RecycleMaterialType.GlassBottle && b.Quantity == 4);
    }

    [Fact]
    public async Task City_Stats_By_Name_Breakdown_And_Total()
    {
        using var db = CreateDb();
        await SeedAsync(db);
        var svc = new StatisticsService(db);

        var cph = await svc.GetCityStatisticsAsync("Copenhagen");
        Assert.NotNull(cph);
        Assert.Equal("Copenhagen", cph!.CityName);
        Assert.Equal(14, cph.TotalItems); // 10 cans + 4 glass from completed in city 1
        Assert.Equal(32.62m, cph.TotalApproximateWorth);
        Assert.Contains(cph.Breakdown, b => b.Material == RecycleMaterialType.Can && b.Quantity == 10);
        Assert.Contains(cph.Breakdown, b => b.Material == RecycleMaterialType.GlassBottle && b.Quantity == 4);

        var aarhus = await svc.GetCityStatisticsAsync("Aarhus");
        Assert.NotNull(aarhus);
        Assert.Equal("Aarhus", aarhus!.CityName);
        Assert.Equal(6, aarhus.TotalItems);
        Assert.Equal(13.98m, aarhus.TotalApproximateWorth);
        Assert.Contains(aarhus.Breakdown, b => b.Material == RecycleMaterialType.PlasticBottle && b.Quantity == 6);
    }
}
