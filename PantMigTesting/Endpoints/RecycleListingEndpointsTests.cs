using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PantmigService.Data;
using PantmigService.Endpoints;
using PantmigService.Endpoints.Helpers;
using PantmigService.Entities;
using PantmigService.Security;
using PantmigService.Services;
using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.Extensions.Caching.Memory;

namespace PantMigTesting.Endpoints
{
    // Simple auth handler that reads claims from headers so tests can switch identities per request
    // Headers: X-UserId, X-UserType, X-MitIdVerified
    public class TestAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
    {
        public const string SchemeName = "Test";

        public TestAuthHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
            : base(options, logger, encoder)
        { }

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var userId = Request.Headers["X-UserId"].FirstOrDefault();
            if (string.IsNullOrEmpty(userId))
            {
                return Task.FromResult(AuthenticateResult.NoResult());
            }

            var claims = new List<Claim>
            {
                new(ClaimTypes.NameIdentifier, userId)
            };

            var userType = Request.Headers["X-UserType"].FirstOrDefault();
            if (!string.IsNullOrEmpty(userType))
                claims.Add(new("userType", userType));

            var verified = Request.Headers["X-MitIdVerified"].FirstOrDefault();
            if (!string.IsNullOrEmpty(verified))
                claims.Add(new("isMitIdVerified", verified));

            var identity = new ClaimsIdentity(claims, SchemeName);
            var principal = new ClaimsPrincipal(identity);
            var ticket = new AuthenticationTicket(principal, SchemeName);

            return Task.FromResult(AuthenticateResult.Success(ticket));
        }
    }


    public static class TestHostBuilder
    {
        private sealed class TestNotifications : INotificationService
        {
            public Task<Notification> CreateAsync(string userId, int listingId, NotificationType type, string? message = null, CancellationToken ct = default)
                => Task.FromResult(new Notification { UserId = userId, ListingId = listingId, Type = type, Message = message, CreatedAt = DateTime.UtcNow, IsRead = true });

            public Task<int> MarkReadAsync(string userId, int[] ids, CancellationToken ct = default) => Task.FromResult(0);

            public Task<IReadOnlyList<Notification>> GetRecentAsync(string userId, int take = 50, CancellationToken ct = default)
                => Task.FromResult<IReadOnlyList<Notification>>(Array.Empty<Notification>());
        }
        public static TestServer CreateServer(string? dbName = null)
        {

            // Ensure the in-memory database name is constant for the lifetime of this server instance.
            var databaseName = dbName ?? Guid.NewGuid().ToString();

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
                        opt.UseInMemoryDatabase(databaseName));
                    services.AddScoped<IRecycleListingService, RecycleListingService>();
                    services.AddScoped<ICityResolver, CityResolver>();
                    services.AddScoped<IRecycleListingValidationService, RecycleListingValidationService>();
                    services.AddScoped<IFileValidationService, FileValidationService>();
                    services.AddScoped<IChatValidationService, ChatValidationService>();
                    services.AddScoped<ICreateListingRequestParser, CreateListingRequestParser>();
                    services.AddScoped<IStatisticsService, StatisticsService>();
                    services.AddScoped<INotificationService, TestNotifications>();

                    // Register a no-op antivirus scanner for tests
                    services.AddSingleton<IAntivirusScanner, NoOpAntivirusScanner>();

                    // Add memory cache required by RecycleListingService
                    services.AddMemoryCache();

                    // Ignore reference cycles introduced by navigation properties (Listing <-> Items)
                    services.ConfigureHttpJsonOptions(o =>
                    {
                        o.SerializerOptions.ReferenceHandler = System.Text.Json.Serialization.ReferenceHandler.IgnoreCycles;
                    });
                })
                .Configure(app =>
                {
                    app.UseRouting();
                    app.UseAuthentication();
                    app.UseAuthorization();
                    app.UseEndpoints(endpoints =>
                    {
                        endpoints.MapRecycleListingEndpoints();
                        endpoints.MapStatisticsEndpoints();
                    });
                });

            return new TestServer(builder);
        }
    }

    public static class HttpClientAuthExtensions
    {
        public static void SetTestUser(this HttpClient client, string userId, string? userType = null, bool? isMitIdVerified = null)
        {
            client.DefaultRequestHeaders.Remove("X-UserId");
            client.DefaultRequestHeaders.Add("X-UserId", userId);

            client.DefaultRequestHeaders.Remove("X-UserType");
            if (!string.IsNullOrWhiteSpace(userType))
                client.DefaultRequestHeaders.Add("X-UserType", userType);

            client.DefaultRequestHeaders.Remove("X-MitIdVerified");
            if (isMitIdVerified.HasValue)
                client.DefaultRequestHeaders.Add("X-MitIdVerified", isMitIdVerified.Value.ToString());
        }
    }

    public class RecycleListingEndpointsTests
    {
        // Enum numeric values: PlasticBottle=1, GlassBottle=2, Can=3
        [Fact]
        public async Task Create_Requires_VerifiedDonator()
        {
            using var server = TestHostBuilder.CreateServer();
            using var client = server.CreateClient();

            client.SetTestUser("recycler-1", userType: "Recycler", isMitIdVerified: true);
            var badResp = await client.PostAsJsonAsync("/listings", new
            {
                Title = "Cans",
                Description = "Bag of cans",
                City = "CPH",
                AvailableFrom = DateOnly.FromDateTime(DateTime.UtcNow),
                AvailableTo = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(2)),
                Items = new[] { new { Type = 3, Quantity = 100 } }
            });
            Assert.Equal(HttpStatusCode.Forbidden, badResp.StatusCode);

            client.SetTestUser("donator-1", userType: "Donator", isMitIdVerified: true);
            var resp = await client.PostAsJsonAsync("/listings", new
            {
                Title = "Cans",
                Description = "Bag of cans",
                City = "CPH",
                AvailableFrom = DateOnly.FromDateTime(DateTime.UtcNow),
                AvailableTo = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(2)),
                Items = new[] { new { Type = 3, Quantity = 100 } }
            });

            Assert.Equal(HttpStatusCode.Created, resp.StatusCode);
            var created = await resp.Content.ReadFromJsonAsync<RecycleListing>();
            Assert.NotNull(created);
            Assert.True(created!.Id > 0);
            Assert.Equal("donator-1", created.CreatedByUserId);
            Assert.Single(created.Items);
        }

        [Fact]
        public async Task EndToEnd_Endpoints_Flow_Works_With_Donator_Confirm_Completing()
        {
            using var server = TestHostBuilder.CreateServer();
            using var client = server.CreateClient();

            client.SetTestUser("donator-1", userType: "Donator", isMitIdVerified: true);
            var createResp = await client.PostAsJsonAsync("/listings", new
            {
                Title = "Cans",
                Description = "Bag of cans",
                City = "CPH",
                AvailableFrom = DateOnly.FromDateTime(DateTime.UtcNow),
                AvailableTo = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(2)),
                Items = new[] { new { Type = 3, Quantity = 50 } }
            });
            createResp.EnsureSuccessStatusCode();
            var listing = await createResp.Content.ReadFromJsonAsync<RecycleListing>();
            Assert.NotNull(listing);
            var id = listing!.Id;
            Assert.True(id > 0);

            // GET active includes it (no trailing slash)
            var active1 = await client.GetFromJsonAsync<List<RecycleListing>>("/listings");
            Assert.Contains(active1!, x => x.Id == id);

            // 2. Recycler requests pickup
            client.SetTestUser("recycler-1", userType: "Recycler", isMitIdVerified: true);
            var pickupReqResp = await client.PostAsJsonAsync("/listings/pickup/request", new { ListingId = id });
            pickupReqResp.EnsureSuccessStatusCode();

            // 2.5 Donator views applicants list
            client.SetTestUser("donator-1", userType: "Donator", isMitIdVerified: true);
            var applicants = await client.GetFromJsonAsync<List<ApplicantInfo>>($"/listings/{id}/applicants");
            Assert.NotNull(applicants);
            Assert.Single(applicants!);
            Assert.Equal("recycler-1", applicants.First()!.Id);

            // 3. Donator accepts recycler-1
            var acceptResp = await client.PostAsJsonAsync("/listings/pickup/accept", new { ListingId = id, RecyclerUserId = "recycler-1" });
            acceptResp.EnsureSuccessStatusCode();

            // 4. Start chat
            client.SetTestUser("recycler-1", userType: "Recycler", isMitIdVerified: true);
            var chatResp = await client.PostAsJsonAsync("/listings/chat/start", new { ListingId = id });
            chatResp.EnsureSuccessStatusCode();

            // 4.1 Donator sets meeting point
            client.SetTestUser("donator-1", userType: "Donator", isMitIdVerified: true);
            var setMeetingResp = await client.PostAsJsonAsync("/listings/meeting/set", new { ListingId = id, Latitude = 55.6761m, Longitude = 12.5683m });
            setMeetingResp.EnsureSuccessStatusCode();

            // 5. Donator confirms pickup
            var confirmResp = await client.PostAsJsonAsync("/listings/pickup/confirm", new { ListingId = id });
            confirmResp.EnsureSuccessStatusCode();
        }

        [Fact]
        public async Task Applicants_Get_Authorization_And_Ownership()
        {
            using var server = TestHostBuilder.CreateServer();
            using var client = server.CreateClient();

            // Create listing as donator-1
            client.SetTestUser("donator-1", userType: "Donator", isMitIdVerified: true);
            var createResp = await client.PostAsJsonAsync("/listings", new
            {
                Title = "Bottles",
                Description = "Box of bottles",
                City = "CPH",
                AvailableFrom = DateOnly.FromDateTime(DateTime.UtcNow),
                AvailableTo = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(2)),
                Items = new[] { new { Type = 1, Quantity = 25 } }
            });
            createResp.EnsureSuccessStatusCode();
            var listing = await createResp.Content.ReadFromJsonAsync<RecycleListing>();
            var id = listing!.Id;

            // Recycler cannot access
            client.SetTestUser("recycler-1", userType: "Recycler", isMitIdVerified: true);
            var recyclerResp = await client.GetAsync($"/listings/{id}/applicants");
            Assert.Equal(HttpStatusCode.Forbidden, recyclerResp.StatusCode);

            // Other donator (verified) but not owner -> BadRequest
            client.SetTestUser("donator-2", userType: "Donator", isMitIdVerified: true);
            var otherDonorResp = await client.GetAsync($"/listings/{id}/applicants");
            Assert.Equal(HttpStatusCode.BadRequest, otherDonorResp.StatusCode);

            // Owner donator -> OK (empty list)
            client.SetTestUser("donator-1", userType: "Donator", isMitIdVerified: true);
            var ownerResp = await client.GetAsync($"/listings/{id}/applicants");
            Assert.Equal(HttpStatusCode.OK, ownerResp.StatusCode);
            var list = await ownerResp.Content.ReadFromJsonAsync<List<string>>();
            Assert.NotNull(list);
            Assert.Empty(list!);
        }

        [Fact]
        public async Task Cancel_Endpoint_Behavior()
        {
            using var server = TestHostBuilder.CreateServer();
            using var client = server.CreateClient();

            // Create listing as donator-1
            client.SetTestUser("donator-1", userType: "Donator", isMitIdVerified: true);
            var createResp = await client.PostAsJsonAsync("/listings", new
            {
                Title = "Cans to cancel",
                Description = "Bag of cans",
                City = "CPH",
                AvailableFrom = DateOnly.FromDateTime(DateTime.UtcNow),
                AvailableTo = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(2)),
                Items = new[] { new { Type = 3, Quantity = 10 } }
            });
            createResp.EnsureSuccessStatusCode();
            var listing = await createResp.Content.ReadFromJsonAsync<RecycleListing>();
            var id = listing!.Id;

            // Recycler cannot cancel (policy)
            client.SetTestUser("recycler-1", userType: "Recycler", isMitIdVerified: true);
            var recyclerCancel = await client.PostAsJsonAsync("/listings/cancel", new { ListingId = id });
            Assert.Equal(HttpStatusCode.Forbidden, recyclerCancel.StatusCode);

            // Other donator (verified) cannot cancel -> BadRequest
            client.SetTestUser("donator-2", userType: "Donator", isMitIdVerified: true);
            var otherDonorCancel = await client.PostAsJsonAsync("/listings/cancel", new { ListingId = id });
            Assert.Equal(HttpStatusCode.BadRequest, otherDonorCancel.StatusCode);

            // Owner donator can cancel -> OK
            client.SetTestUser("donator-1", userType: "Donator", isMitIdVerified: true);
            var ownerCancel = await client.PostAsJsonAsync("/listings/cancel", new { ListingId = id });
            Assert.Equal(HttpStatusCode.OK, ownerCancel.StatusCode);

            // Verify listing is cancelled
            var final = await client.GetFromJsonAsync<RecycleListing>($"/listings/{id}");
            Assert.NotNull(final);
            Assert.Equal(ListingStatus.Cancelled, final!.Status);
            Assert.False(final.IsActive);

            // Active should not include it
            var active = await client.GetFromJsonAsync<List<RecycleListing>>("/listings");
            Assert.DoesNotContain(active!, x => x.Id == id);

            // Second cancel attempt should be BadRequest
            var secondCancel = await client.PostAsJsonAsync("/listings/cancel", new { ListingId = id });
            Assert.Equal(HttpStatusCode.BadRequest, secondCancel.StatusCode);
        }

        [Fact]
        public async Task Search_Endpoint_Filters_By_City_And_OnlyActive_Defaults_To_True()
        {
            using var server = TestHostBuilder.CreateServer();
            using var client = server.CreateClient();

            // Create three listings in city1 with different states + one in city2
            client.SetTestUser("donator-1", userType: "Donator", isMitIdVerified: true);
            var l1Resp = await client.PostAsJsonAsync("/listings", new { Title = "L1", Description = "desc", City = "CPH", AvailableFrom = DateOnly.FromDateTime(DateTime.UtcNow), AvailableTo = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(1)), Items = new[] { new { Type = 3, Quantity = 5 } } });
            l1Resp.EnsureSuccessStatusCode();
            var l1 = await l1Resp.Content.ReadFromJsonAsync<RecycleListing>();
            Assert.NotNull(l1);

            var l2Resp = await client.PostAsJsonAsync("/listings", new { Title = "L2", Description = "desc", City = "CPH", AvailableFrom = DateOnly.FromDateTime(DateTime.UtcNow), AvailableTo = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(1)), Items = new[] { new { Type = 3, Quantity = 6 } } });
            l2Resp.EnsureSuccessStatusCode();
            var l2 = await l2Resp.Content.ReadFromJsonAsync<RecycleListing>();
            Assert.NotNull(l2);

            // Request pickup to make l2 PendingAcceptance
            client.SetTestUser("recycler-1", userType: "Recycler", isMitIdVerified: true);
            var pickup = await client.PostAsJsonAsync("/listings/pickup/request", new { ListingId = l2!.Id });
            pickup.EnsureSuccessStatusCode();

            // Create another in same city and then cancel it to make inactive
            client.SetTestUser("donator-1", userType: "Donator", isMitIdVerified: true);
            var l3Resp = await client.PostAsJsonAsync("/listings", new { Title = "L3", Description = "desc", City = "CPH", AvailableFrom = DateOnly.FromDateTime(DateTime.UtcNow), AvailableTo = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(1)), Items = new[] { new { Type = 3, Quantity = 7 } } });
            l3Resp.EnsureSuccessStatusCode();
            var l3 = await l3Resp.Content.ReadFromJsonAsync<RecycleListing>();
            var cancelResp = await client.PostAsJsonAsync("/listings/cancel", new { ListingId = l3!.Id });
            cancelResp.EnsureSuccessStatusCode();

            // Create listing in a different city
            var otherCityResp = await client.PostAsJsonAsync("/listings", new { Title = "OtherCity", Description = "desc", City = "Aalborg", AvailableFrom = DateOnly.FromDateTime(DateTime.UtcNow), AvailableTo = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(1)), Items = new[] { new { Type = 3, Quantity = 1 } } });
            otherCityResp.EnsureSuccessStatusCode();
            var other = await otherCityResp.Content.ReadFromJsonAsync<RecycleListing>();

            // Search for cityId of CPH
            var searchResp = await client.PostAsJsonAsync("/listings/search", new { CityId = l1!.CityId });
            searchResp.EnsureSuccessStatusCode();
            var results = await searchResp.Content.ReadFromJsonAsync<List<RecycleListing>>();
            Assert.NotNull(results);
            // Only l1 (Created) and l2 (PendingAcceptance) should be present
            Assert.Contains(results!, x => x.Id == l1.Id);
            Assert.Contains(results!, x => x.Id == l2!.Id);
            Assert.DoesNotContain(results!, x => x.Id == l3!.Id);
            Assert.DoesNotContain(results!, x => x.Id == other!.Id);
        }

        [Fact]
        public async Task Search_Endpoint_Includes_All_Statuses_When_OnlyActive_False()
        {
            using var server = TestHostBuilder.CreateServer();
            using var client = server.CreateClient();

            // Create two listings in city1 and cancel one
            client.SetTestUser("donator-1", userType: "Donator", isMitIdVerified: true);
            var l1Resp = await client.PostAsJsonAsync("/listings", new { Title = "L1", Description = "desc", City = "CPH", AvailableFrom = DateOnly.FromDateTime(DateTime.UtcNow), AvailableTo = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(1)), Items = new[] { new { Type = 3, Quantity = 5 } } });
            l1Resp.EnsureSuccessStatusCode();
            var l1 = await l1Resp.Content.ReadFromJsonAsync<RecycleListing>();
            var l2Resp = await client.PostAsJsonAsync("/listings", new { Title = "L2", Description = "desc", City = "CPH", AvailableFrom = DateOnly.FromDateTime(DateTime.UtcNow), AvailableTo = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(1)), Items = new[] { new { Type = 3, Quantity = 6 } } });
            l2Resp.EnsureSuccessStatusCode();
            var l2 = await l2Resp.Content.ReadFromJsonAsync<RecycleListing>();

            // Cancel l2
            var cancel = await client.PostAsJsonAsync("/listings/cancel", new { ListingId = l2!.Id });
            cancel.EnsureSuccessStatusCode();

            // Search for cityId with onlyActive=false
            var searchResp = await client.PostAsJsonAsync("/listings/search", new { CityId = l1!.CityId, OnlyActive = false });
            searchResp.EnsureSuccessStatusCode();
            var results = await searchResp.Content.ReadFromJsonAsync<List<RecycleListing>>();
            Assert.NotNull(results);
            Assert.Contains(results!, x => x.Id == l1.Id);
            Assert.Contains(results!, x => x.Id == l2!.Id);
        }
    }
}
