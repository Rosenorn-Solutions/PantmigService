using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using PantmigService.Endpoints;
using PantmigService.Entities;
using Microsoft.AspNetCore.TestHost;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging;
using System.Text.Encodings.Web;
using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using PantmigService.Data;
using PantmigService.Services;
using PantmigService.Security;
using Microsoft.Extensions.Configuration;

namespace PantMigTesting.Endpoints;

public class ReceiptUploadEndpointsTests
{
    [Fact]
    public async Task Receipt_Upload_Works_EndToEnd()
    {
        using var server = TestHostBuilder.CreateServer();
        using var client = server.CreateClient();

        // 1. Donator creates listing
        client.SetTestUser("donator-1", userType: "Donator", isMitIdVerified: true);
        var createResp = await client.PostAsJsonAsync("/listings", new
        {
            Title = "Cans",
            Description = "Bag of cans",
            City = "CPH",
            EstimatedValue = (decimal?)null,
            EstimatedAmount = "50",
            AvailableFrom = DateTime.UtcNow,
            AvailableTo = DateTime.UtcNow.AddHours(2)
        });
        createResp.EnsureSuccessStatusCode();
        var listing = await createResp.Content.ReadFromJsonAsync<RecycleListing>();
        Assert.NotNull(listing);
        var id = listing!.Id;
        Assert.True(id > 0);

        // 2. Recycler requests pickup
        client.SetTestUser("recycler-1", userType: "Recycler", isMitIdVerified: true);
        var pickupReqResp = await client.PostAsJsonAsync("/listings/pickup/request", new { ListingId = id });
        pickupReqResp.EnsureSuccessStatusCode();

        // 3. Donator accepts recycler
        client.SetTestUser("donator-1", userType: "Donator", isMitIdVerified: true);
        var acceptResp = await client.PostAsJsonAsync("/listings/pickup/accept", new { ListingId = id, RecyclerUserId = "recycler-1" });
        acceptResp.EnsureSuccessStatusCode();

        // 4. Start chat and set meeting, donor confirms pickup
        client.SetTestUser("recycler-1", userType: "Recycler", isMitIdVerified: true);
        (await client.PostAsJsonAsync("/listings/chat/start", new { ListingId = id })).EnsureSuccessStatusCode();
        client.SetTestUser("donator-1", userType: "Donator", isMitIdVerified: true);
        (await client.PostAsJsonAsync("/listings/meeting/set", new { ListingId = id, Latitude = 55.0m, Longitude = 12.0m })).EnsureSuccessStatusCode();
        (await client.PostAsJsonAsync("/listings/pickup/confirm", new { ListingId = id })).EnsureSuccessStatusCode();

        // Listing is now completed
        var afterConfirm = await client.GetFromJsonAsync<RecycleListing>($"/listings/{id}");
        Assert.Equal(ListingStatus.Completed, afterConfirm!.Status);
        Assert.False(afterConfirm.IsActive);

        // 5. Recycler uploads receipt via multipart/form-data (allowed even after completion)
        var projectDir = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", ".."));
        var dataDir = Path.Combine(projectDir, "Data");
        var jpgPath = Directory.GetFiles(dataDir, "*.jpg").FirstOrDefault();
        Assert.False(string.IsNullOrEmpty(jpgPath));
        var bytes = await File.ReadAllBytesAsync(jpgPath!);

        client.SetTestUser("recycler-1", userType: "Recycler", isMitIdVerified: true);
        using var form = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(bytes);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");
        form.Add(new StringContent(id.ToString(), Encoding.UTF8), name: "listingId");
        form.Add(new StringContent("123.45", Encoding.UTF8), name: "reportedAmount");
        form.Add(fileContent, name: "file", fileName: Path.GetFileName(jpgPath));

        var uploadResp = await client.PostAsync("/listings/receipt/upload", form);
        uploadResp.EnsureSuccessStatusCode();

        // 6. Verify state remains Completed with uploaded bytes stored
        var final = await client.GetFromJsonAsync<RecycleListing>($"/listings/{id}");
        Assert.NotNull(final);
        Assert.Equal(ListingStatus.Completed, final!.Status);
        Assert.False(final.IsActive);
        Assert.Null(final.ReceiptImageUrl);
        Assert.Equal(123.45m, final.ReportedAmount);
        Assert.NotNull(final.ReceiptImageBytes);
        Assert.True(final.ReceiptImageBytes!.Length > 0);
    }

    // Optional integration test: requires a running ClamAV daemon.
    // Enable by setting ClamAV:Enabled=true in appsettings.Testing.json or environment variable CLAMAV_IT=1.
    [Fact]
    public async Task Receipt_Upload_Blocked_When_Infected_If_ClamAV_Available()
    {
        // Load config from appsettings.Testing.json in output, then env vars
        var config = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.Testing.json", optional: true)
            .AddEnvironmentVariables()
            .Build();

        var enabledFromConfig = config.GetValue<bool?>("ClamAV:Enabled");
        var runEnv = Environment.GetEnvironmentVariable("CLAMAV_IT");
        var shouldRun = (enabledFromConfig == true) || string.Equals(runEnv, "1", StringComparison.Ordinal);
        if (!shouldRun)
        {
            return; // disabled
        }

        var host = config["ClamAV:Host"] ?? Environment.GetEnvironmentVariable("CLAMAV_HOST") ?? "127.0.0.1";
        var port = config.GetValue("ClamAV:Port", 3310);

        using var server = CreateServerWithClamAV(host, port);
        using var client = server.CreateClient();

        // 1. Donator creates listing
        client.SetTestUser("donator-1", userType: "Donator", isMitIdVerified: true);
        var createResp = await client.PostAsJsonAsync("/listings", new
        {
            Title = "E2E infected test",
            Description = "Should be blocked",
            City = "CPH",
            EstimatedValue = (decimal?)null,
            EstimatedAmount = "1",
            AvailableFrom = DateTime.UtcNow,
            AvailableTo = DateTime.UtcNow.AddHours(1)
        });
        createResp.EnsureSuccessStatusCode();
        var listing = await createResp.Content.ReadFromJsonAsync<RecycleListing>();
        var id = listing!.Id;

        // 2. Flow to pickup confirmed (Completed)
        client.SetTestUser("recycler-1", userType: "Recycler", isMitIdVerified: true);
        (await client.PostAsJsonAsync("/listings/pickup/request", new { ListingId = id })).EnsureSuccessStatusCode();

        client.SetTestUser("donator-1", userType: "Donator", isMitIdVerified: true);
        (await client.PostAsJsonAsync("/listings/pickup/accept", new { ListingId = id, RecyclerUserId = "recycler-1" })).EnsureSuccessStatusCode();

        client.SetTestUser("recycler-1", userType: "Recycler", isMitIdVerified: true);
        (await client.PostAsJsonAsync("/listings/chat/start", new { ListingId = id })).EnsureSuccessStatusCode();
        client.SetTestUser("donator-1", userType: "Donator", isMitIdVerified: true);
        (await client.PostAsJsonAsync("/listings/meeting/set", new { ListingId = id, Latitude = 10m, Longitude = 10m })).EnsureSuccessStatusCode();
        (await client.PostAsJsonAsync("/listings/pickup/confirm", new { ListingId = id })).EnsureSuccessStatusCode();

        // 3. Try to upload EICAR as image/jpeg -> should be blocked by AV
        const string eicar = "X5O!P%@AP[4\\PZX54(P^)7CC)7}$EICAR-STANDARD-ANTIVIRUS-TEST-FILE!$H+H*";
        var eicarBytes = Encoding.ASCII.GetBytes(eicar);

        client.SetTestUser("recycler-1", userType: "Recycler", isMitIdVerified: true);
        using var form = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(eicarBytes);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");
        form.Add(new StringContent(id.ToString(), Encoding.UTF8), name: "listingId");
        form.Add(new StringContent("9.99", Encoding.UTF8), name: "reportedAmount");
        form.Add(fileContent, name: "file", fileName: "infected.jpg");

        var uploadResp = await client.PostAsync("/listings/receipt/upload", form);
        Assert.Equal(System.Net.HttpStatusCode.BadRequest, uploadResp.StatusCode);
    }

    private static TestServer CreateServerWithClamAV(string host, int port)
    {
        var dbName = Guid.NewGuid().ToString();
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

                services.AddDbContext<PantmigDbContext>(opt =>
                    opt.UseInMemoryDatabase(dbName));
                services.AddScoped<IRecycleListingService, RecycleListingService>();
                services.AddScoped<ICityResolver, CityResolver>();

                // Use real ClamAV for this server
                services.AddSingleton<IAntivirusScanner>(_ => new ClamAvAntivirusScanner(new ClamAvOptions
                {
                    Host = host,
                    Port = port,
                    Enabled = true
                }));
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

        return new TestServer(builder);
    }
}
