using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PantmigService.Data;
using PantmigService.Endpoints;
using PantmigService.Endpoints.Helpers;
using PantmigService.Entities;
using PantmigService.Security;
using PantmigService.Services;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using Xunit;

namespace PantMigTesting.Endpoints;

public class ListingImageCreationTests
{
    private sealed class TestNotifications : INotificationService
    {
        public Task<Notification> CreateAsync(string userId, int listingId, NotificationType type, string? message = null, CancellationToken ct = default)
            => Task.FromResult(new Notification { UserId = userId, ListingId = listingId, Type = type, Message = message, CreatedAt = DateTime.UtcNow, IsRead = true });

        public Task<int> MarkReadAsync(string userId, int[] ids, CancellationToken ct = default) => Task.FromResult(0);

        public Task<IReadOnlyList<Notification>> GetRecentAsync(string userId, int take = 50, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<Notification>>(Array.Empty<Notification>());
    }

    [Fact]
    public async Task Create_Listing_With_Images_Multipart_Works()
    {
        using var server = TestHostBuilder.CreateServer();
        using var client = server.CreateClient();

        client.SetTestUser("donator-1", userType: "Donator", isMitIdVerified: true);

        // Build a simple in-memory image (fake jpeg header + data)
        var imgBytes = BuildFakeJpeg();
        var imgBytes2 = BuildFakeJpeg();

        using var form = new MultipartFormDataContent();
        form.Add(new StringContent("Listing with images"), "Title");
        form.Add(new StringContent("Some description"), "Description");
        form.Add(new StringContent("CPH"), "City");
        form.Add(new StringContent(DateOnly.FromDateTime(DateTime.UtcNow).ToString("O")), "AvailableFrom");
        form.Add(new StringContent(DateOnly.FromDateTime(DateTime.UtcNow.AddDays(4)).ToString("O")), "AvailableTo");
        // Items field as JSON
        var itemsJson = "[{\"Type\":3,\"Quantity\":10}]"; // Type=3 (Can), Quantity=10
        form.Add(new StringContent(itemsJson, Encoding.UTF8, "application/json"), "Items");

        // Add two images
        var img1 = new ByteArrayContent(imgBytes);
        img1.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");
        form.Add(img1, "images", "photo1.jpg");
        var img2 = new ByteArrayContent(imgBytes2);
        img2.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");
        form.Add(img2, "images", "photo2.jpg");

        var resp = await client.PostAsync("/listings", form);
        resp.EnsureSuccessStatusCode();
        var created = await resp.Content.ReadFromJsonAsync<RecycleListing>();
        Assert.NotNull(created);
        Assert.Equal("Listing with images", created!.Title);
        Assert.NotNull(created.Images);
        Assert.Equal(2, created.Images.Count);
        Assert.All(created.Images.OrderBy(i => i.Order).Select((img, idx) => (img, idx)), pair => Assert.Equal(pair.idx, pair.img.Order));
    }

    [Fact]
    public async Task Create_Listing_With_NonImage_File_Fails()
    {
        using var server = TestHostBuilder.CreateServer();
        using var client = server.CreateClient();

        client.SetTestUser("donator-1", userType: "Donator", isMitIdVerified: true);

        using var form = new MultipartFormDataContent();
        form.Add(new StringContent("Bad listing"), "Title");
        form.Add(new StringContent("Bad file"), "Description");
        form.Add(new StringContent("CPH"), "City");
        form.Add(new StringContent(DateTime.UtcNow.ToString("O")), "AvailableFrom");
        form.Add(new StringContent(DateTime.UtcNow.AddHours(1).ToString("O")), "AvailableTo");
        form.Add(new StringContent("[{\"Type\":3,\"Quantity\":5}]", Encoding.UTF8, "application/json"), "Items");

        // Add a text file with wrong content type
        var fileBytes = Encoding.UTF8.GetBytes("hello world");
        var txt = new ByteArrayContent(fileBytes);
        txt.Headers.ContentType = new MediaTypeHeaderValue("text/plain");
        form.Add(txt, "images", "note.txt");

        var resp = await client.PostAsync("/listings", form);
        // Should be 400 due to validation error (non-image)
        Assert.Equal(System.Net.HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task Create_Listing_With_Infected_Image_Is_Blocked_When_AV_Enabled()
    {
        // Simulate ClamAV enabled by substituting real scanner with one that flags infected
        var builder = new WebHostBuilder()
            .ConfigureServices(services =>
            {
                services.AddRouting();
                services.AddAuthentication(o =>
                {
                    o.DefaultAuthenticateScheme = TestAuthHandler.SchemeName;
                    o.DefaultChallengeScheme = TestAuthHandler.SchemeName;
                }).AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(TestAuthHandler.SchemeName, _ => { });

                services.AddAuthorization(options =>
                {
                    options.AddPolicy("VerifiedDonator", policy =>
                    {
                        policy.RequireAuthenticatedUser();
                        policy.RequireAssertion(ctx =>
                        {
                            var type = ctx.User.FindFirst("userType")?.Value;
                            var verified = ctx.User.FindFirst("isMitIdVerified")?.Value;
                            return string.Equals(type, "Donator", StringComparison.OrdinalIgnoreCase)
                                   && string.Equals(verified, bool.TrueString, StringComparison.OrdinalIgnoreCase);
                        });
                    });
                });

                services.AddDbContext<PantmigDbContext>(opt => opt.UseInMemoryDatabase(Guid.NewGuid().ToString()));
                services.AddScoped<INotificationService, TestNotifications>();
                services.AddScoped<IRecycleListingService, RecycleListingService>();
                services.AddScoped<ICityResolver, CityResolver>();
                services.AddScoped<IRecycleListingValidationService, RecycleListingValidationService>();
                services.AddScoped<IFileValidationService, FileValidationService>();
                services.AddScoped<IChatValidationService, ChatValidationService>();
                services.AddScoped<ICreateListingRequestParser, CreateListingRequestParser>();
                services.AddSingleton<IAntivirusScanner>(new FakeInfectedScanner());
            })
            .Configure(app =>
            {
                app.UseRouting();
                app.UseAuthentication();
                app.UseAuthorization();
                app.UseEndpoints(endpoints =>
                {
                    endpoints.MapRecycleListingEndpoints();
                });
            });

        using var server = new TestServer(builder);
        using var client = server.CreateClient();
        client.SetTestUser("donator-1", userType: "Donator", isMitIdVerified: true);

        using var form = new MultipartFormDataContent();
        form.Add(new StringContent("Infected listing"), "Title");
        form.Add(new StringContent("Should be blocked"), "Description");
        form.Add(new StringContent("CPH"), "City");
        form.Add(new StringContent(DateTime.UtcNow.ToString("O")), "AvailableFrom");
        form.Add(new StringContent(DateTime.UtcNow.AddHours(1).ToString("O")), "AvailableTo");
        form.Add(new StringContent("[{\"Type\":3,\"Quantity\":1}]", Encoding.UTF8, "application/json"), "Items");

        var imgBytes = BuildFakeJpeg();
        var img = new ByteArrayContent(imgBytes);
        img.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");
        form.Add(img, "images", "infected.jpg");

        var resp = await client.PostAsync("/listings", form);
        Assert.Equal(System.Net.HttpStatusCode.BadRequest, resp.StatusCode); // Malware detected returns 400
    }

    private static byte[] BuildFakeJpeg()
    {
        // Minimal JPEG header + padding
        return new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, (byte)'J', (byte)'F', (byte)'I', (byte)'F', 0x00, 0x01, 0x02, 0x03 };
    }

    private sealed class FakeInfectedScanner : IAntivirusScanner
    {
        public Task<AntivirusScanResult> ScanAsync(Stream content, string? fileName = null, CancellationToken ct = default)
            => Task.FromResult(AntivirusScanResult.Infected("EICAR-Test"));
    }
}
