using PantmigService.Entities;
using PantmigService.Services;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;

namespace PantMigTesting.Endpoints;

public class StatisticsEndpointsTests
{
    [Fact]
    public async Task Donor_Statistics_Returns_Only_Own_Completed_Listings()
    {
        using var server = TestHostBuilder.CreateServer();
        using var client = server.CreateClient();

        // Create 2 listings for donator-1, different items
        client.SetTestUser("donator-1", userType: "Donator", isMitIdVerified: true);
        var l1 = await client.PostAsJsonAsync("/listings", new
        {
            Title = "Cans1",
            Description = "description",
            City = "CPH",
            AvailableFrom = DateOnly.FromDateTime(DateTime.UtcNow),
            AvailableTo = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(1)),
            Items = new[] { new { Type = 3, Quantity = 10 } }
        });
        l1.EnsureSuccessStatusCode();
        var r1 = await l1.Content.ReadFromJsonAsync<RecycleListing>();

        var l2 = await client.PostAsJsonAsync("/listings", new
        {
            Title = "Bottles2",
            Description = "description",
            City = "CPH",
            AvailableFrom = DateOnly.FromDateTime(DateTime.UtcNow),
            AvailableTo = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(1)),
            Items = new[] { new { Type = 1, Quantity = 5 } }
        });
        l2.EnsureSuccessStatusCode();
        var r2 = await l2.Content.ReadFromJsonAsync<RecycleListing>();

        // Complete only one of them
        client.SetTestUser("recycler-1", userType: "Recycler", isMitIdVerified: true);
        (await client.PostAsJsonAsync("/listings/pickup/request", new { ListingId = r1!.Id })).EnsureSuccessStatusCode();

        client.SetTestUser("donator-1", userType: "Donator", isMitIdVerified: true);
        (await client.PostAsJsonAsync("/listings/pickup/accept", new { ListingId = r1!.Id, RecyclerUserId = "recycler-1" })).EnsureSuccessStatusCode();
        client.SetTestUser("recycler-1", userType: "Recycler", isMitIdVerified: true);
        (await client.PostAsJsonAsync("/listings/chat/start", new { ListingId = r1!.Id })).EnsureSuccessStatusCode();
        client.SetTestUser("donator-1", userType: "Donator", isMitIdVerified: true);
        (await client.PostAsJsonAsync("/listings/meeting/set", new { ListingId = r1!.Id, Latitude = 10m, Longitude = 10m })).EnsureSuccessStatusCode();
        (await client.PostAsJsonAsync("/listings/pickup/confirm", new { ListingId = r1!.Id })).EnsureSuccessStatusCode();

        // Call donor statistics
        var statsResp = await client.GetAsync("/statistics/donor");
        statsResp.EnsureSuccessStatusCode();
        var stats = await statsResp.Content.ReadFromJsonAsync<DonorStatisticsResult>();

        Assert.NotNull(stats);
        Assert.Equal(1, stats!.ListingCount); // only one completed
        Assert.Equal(10, stats.TotalItems); // only items from completed listing
        Assert.Equal(23.30m, stats.TotalApproximateWorth);

        // Another donor should have 0
        client.SetTestUser("donator-2", userType: "Donator", isMitIdVerified: true);
        var otherResp = await client.GetAsync("/statistics/donor");
        otherResp.EnsureSuccessStatusCode();
        var other = await otherResp.Content.ReadFromJsonAsync<DonorStatisticsResult>();
        Assert.Equal(0, other!.ListingCount);
        Assert.Equal(0, other.TotalItems);
        Assert.Equal(0m, other.TotalApproximateWorth);
    }

    [Fact]
    public async Task Recycler_Statistics_Breakdown_And_Totals()
    {
        using var server = TestHostBuilder.CreateServer();
        using var client = server.CreateClient();

        // Setup two completed listings both assigned to recycler-1
        client.SetTestUser("donator-1", userType: "Donator", isMitIdVerified: true);
        var c1 = await client.PostAsJsonAsync("/listings", new
        {
            Title = "Mix1",
            Description = "description",
            City = "CPH",
            AvailableFrom = DateOnly.FromDateTime(DateTime.UtcNow),
            AvailableTo = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(1)),
            Items = new[] { new { Type = 1, Quantity = 3 }, new { Type = 3, Quantity = 7 } }
        });
        c1.EnsureSuccessStatusCode();
        var l1 = await c1.Content.ReadFromJsonAsync<RecycleListing>();

        client.SetTestUser("recycler-1", userType: "Recycler", isMitIdVerified: true);
        (await client.PostAsJsonAsync("/listings/pickup/request", new { ListingId = l1!.Id })).EnsureSuccessStatusCode();
        client.SetTestUser("donator-1", userType: "Donator", isMitIdVerified: true);
        (await client.PostAsJsonAsync("/listings/pickup/accept", new { ListingId = l1!.Id, RecyclerUserId = "recycler-1" })).EnsureSuccessStatusCode();
        client.SetTestUser("recycler-1", userType: "Recycler", isMitIdVerified: true);
        (await client.PostAsJsonAsync("/listings/chat/start", new { ListingId = l1!.Id })).EnsureSuccessStatusCode();
        client.SetTestUser("donator-1", userType: "Donator", isMitIdVerified: true);
        (await client.PostAsJsonAsync("/listings/meeting/set", new { ListingId = l1!.Id, Latitude = 10m, Longitude = 10m })).EnsureSuccessStatusCode();
        (await client.PostAsJsonAsync("/listings/pickup/confirm", new { ListingId = l1!.Id })).EnsureSuccessStatusCode();

        // Upload receipt with reportedAmount for l1
        client.SetTestUser("recycler-1", userType: "Recycler", isMitIdVerified: true);
        using var form1 = new MultipartFormDataContent();
        form1.Add(new StringContent(l1!.Id.ToString()), "listingId");
        form1.Add(new StringContent("50.00"), "reportedAmount");
        var fileContent1 = new ByteArrayContent(new byte[] { 1, 2, 3 });
        fileContent1.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");
        form1.Add(fileContent1, "file", "r1.jpg");
        var upload1 = await client.PostAsync("/listings/receipt/upload", form1);
        upload1.EnsureSuccessStatusCode();

        // Second listing
        client.SetTestUser("donator-1", userType: "Donator", isMitIdVerified: true);
        var c2 = await client.PostAsJsonAsync("/listings", new
        {
            Title = "Mix2",
            Description = "Description",
            City = "CPH",
            AvailableFrom = DateOnly.FromDateTime(DateTime.UtcNow),
            AvailableTo = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(1)),
            Items = new[] { new { Type = 2, Quantity = 4 } }
        });
        c2.EnsureSuccessStatusCode();
        var l2 = await c2.Content.ReadFromJsonAsync<RecycleListing>();

        client.SetTestUser("recycler-1", userType: "Recycler", isMitIdVerified: true);
        (await client.PostAsJsonAsync("/listings/pickup/request", new { ListingId = l2!.Id })).EnsureSuccessStatusCode();
        client.SetTestUser("donator-1", userType: "Donator", isMitIdVerified: true);
        (await client.PostAsJsonAsync("/listings/pickup/accept", new { ListingId = l2!.Id, RecyclerUserId = "recycler-1" })).EnsureSuccessStatusCode();
        client.SetTestUser("recycler-1", userType: "Recycler", isMitIdVerified: true);
        (await client.PostAsJsonAsync("/listings/chat/start", new { ListingId = l2!.Id })).EnsureSuccessStatusCode();
        client.SetTestUser("donator-1", userType: "Donator", isMitIdVerified: true);
        (await client.PostAsJsonAsync("/listings/meeting/set", new { ListingId = l2!.Id, Latitude = 10m, Longitude = 10m })).EnsureSuccessStatusCode();
        (await client.PostAsJsonAsync("/listings/pickup/confirm", new { ListingId = l2!.Id })).EnsureSuccessStatusCode();

        // Call recycler statistics for recycler-1
        client.SetTestUser("recycler-1", userType: "Recycler", isMitIdVerified: true);
        var statsResp = await client.GetAsync("/statistics/recycler");
        statsResp.EnsureSuccessStatusCode();
        var stats = await statsResp.Content.ReadFromJsonAsync<RecyclerStatisticsResult>();

        Assert.NotNull(stats);
        Assert.Equal(2, stats!.ListingCount);
        // total items: l1 (3+7=10) + l2 (4) = 14
        Assert.Equal(14, stats.TotalItems);
        Assert.Equal(32.62m, stats.TotalApproximateWorth);
        // total reported amount: 50 + (0 for l2, no upload)
        Assert.Equal(50m, stats.TotalReportedAmount);
        // breakdown counts
        Assert.Contains(stats.Breakdown, b => b.Material == RecycleMaterialType.PlasticBottle && b.Quantity == 3);
        Assert.Contains(stats.Breakdown, b => b.Material == RecycleMaterialType.Can && b.Quantity == 7);
        Assert.Contains(stats.Breakdown, b => b.Material == RecycleMaterialType.GlassBottle && b.Quantity == 4);
    }

    [Fact]
    public async Task Donor_Statistics_Requires_VerifiedDonator()
    {
        using var server = TestHostBuilder.CreateServer();
        using var client = server.CreateClient();

        // Recycler calling donor stats -> Forbidden
        client.SetTestUser("recycler-1", userType: "Recycler", isMitIdVerified: true);
        var resp = await client.GetAsync("/statistics/donor");
        Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);

        // Donator but not verified -> Forbidden due to policy
        client.SetTestUser("donator-1", userType: "Donator", isMitIdVerified: false);
        var resp2 = await client.GetAsync("/statistics/donor");
        Assert.Equal(HttpStatusCode.Forbidden, resp2.StatusCode);
    }
}
