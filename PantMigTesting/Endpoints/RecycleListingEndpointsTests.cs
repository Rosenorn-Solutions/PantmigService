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

    public record Paged<T>(List<T> Items, int Total, int Page, int PageSize); // helper for paged responses

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

            var server = new TestServer(builder);
            // Seed cities required for tests
            using (var scope = server.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<PantmigDbContext>();
                if (!db.Cities.Any())
                {
                    db.Cities.Add(new City { Name = "CPH", Slug = "cph", ExternalId = Guid.Parse("FACB9519-A654-9D5B-ADBA-25B9B6493EC1") });
                    db.Cities.Add(new City { Name = "Aalborg", Slug = "aalborg", ExternalId = Guid.Parse("E21D5CB7-9F1A-4D8F-9E21-0F3F3A5D1234") });
                    db.SaveChanges();
                }
            }
            return server;
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
                CityExternalId = "facb9519-a654-9d5b-adba-25b9b6493ec1",
                Location = "street",
                AvailableFrom = DateOnly.FromDateTime(DateTime.UtcNow),
                AvailableTo = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(2)),
                Items = new[] { new { Type =3, Quantity =100 } }
            });
            Assert.Equal(HttpStatusCode.Forbidden, badResp.StatusCode);

            client.SetTestUser("donator-1", userType: "Donator", isMitIdVerified: true);
            var resp = await client.PostAsJsonAsync("/listings", new
            {
                Title = "Cans",
                Description = "Bag of cans",
                CityExternalId = "facb9519-a654-9d5b-adba-25b9b6493ec1",
                Location = "street",
                AvailableFrom = DateOnly.FromDateTime(DateTime.UtcNow),
                AvailableTo = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(2)),
                Items = new[] { new { Type =3, Quantity =100 } }
            });

            Assert.Equal(HttpStatusCode.Created, resp.StatusCode);
            var created = await resp.Content.ReadFromJsonAsync<RecycleListingResponse>();
            Assert.NotNull(created);
            Assert.True(created!.Id >0);
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
                CityExternalId = "facb9519-a654-9d5b-adba-25b9b6493ec1",
                Location = "street",
                AvailableFrom = DateOnly.FromDateTime(DateTime.UtcNow),
                AvailableTo = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(2)),
                Items = new[] { new { Type = 3, Quantity = 50 } }
            });
            createResp.EnsureSuccessStatusCode();
            var listing = await createResp.Content.ReadFromJsonAsync<RecycleListingResponse>();
            Assert.NotNull(listing);
            var id = listing!.Id;
            Assert.True(id > 0);

            // GET active includes it (no trailing slash)
            var active1 = await client.GetFromJsonAsync<Paged<RecycleListingResponse>>("/listings");
            Assert.Contains(active1!.Items, x => x.Id == id);

            //2. Recycler requests pickup
            client.SetTestUser("recycler-1", userType: "Recycler", isMitIdVerified: true);
            var pickupReqResp = await client.PostAsJsonAsync("/listings/pickup/request", new { ListingId = id });
            pickupReqResp.EnsureSuccessStatusCode();

            //2.5 Donator views applicants list
            client.SetTestUser("donator-1", userType: "Donator", isMitIdVerified: true);
            var applicants = await client.GetFromJsonAsync<List<ApplicantInfo>>($"/listings/{id}/applicants");
            Assert.NotNull(applicants);
            Assert.Single(applicants!);
            Assert.Equal("recycler-1", applicants.First()!.Id);

            //3. Donator accepts recycler-1
            var acceptResp = await client.PostAsJsonAsync("/listings/pickup/accept", new { ListingId = id, RecyclerUserId = "recycler-1" });
            acceptResp.EnsureSuccessStatusCode();

            //4. Start chat
            client.SetTestUser("recycler-1", userType: "Recycler", isMitIdVerified: true);
            var chatResp = await client.PostAsJsonAsync("/listings/chat/start", new { ListingId = id });
            chatResp.EnsureSuccessStatusCode();

            //4.1 Donator sets meeting point
            client.SetTestUser("donator-1", userType: "Donator", isMitIdVerified: true);
            var setMeetingResp = await client.PostAsJsonAsync("/listings/meeting/set", new { ListingId = id, Latitude = 55.6761m, Longitude = 12.5683m });
            setMeetingResp.EnsureSuccessStatusCode();

            //5. Donator confirms pickup
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
                CityExternalId = "facb9519-a654-9d5b-adba-25b9b6493ec1",
                Location = "street",
                AvailableFrom = DateOnly.FromDateTime(DateTime.UtcNow),
                AvailableTo = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(2)),
                Items = new[] { new { Type = 1, Quantity = 25 } }
            });
            createResp.EnsureSuccessStatusCode();
            var listing = await createResp.Content.ReadFromJsonAsync<RecycleListingResponse>();
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
            var list = await ownerResp.Content.ReadFromJsonAsync<List<ApplicantInfo>>();
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
                CityExternalId = "facb9519-a654-9d5b-adba-25b9b6493ec1",
                Location = "street",
                AvailableFrom = DateOnly.FromDateTime(DateTime.UtcNow),
                AvailableTo = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(2)),
                Items = new[] { new { Type = 3, Quantity = 10 } }
            });
            createResp.EnsureSuccessStatusCode();
            var listing = await createResp.Content.ReadFromJsonAsync<RecycleListingResponse>();
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
            var final = await client.GetFromJsonAsync<RecycleListingResponse>($"/listings/{id}");
            Assert.NotNull(final);
            Assert.Equal(ListingStatus.Cancelled, final!.Status);
            Assert.False(final.IsActive);

            // Active should not include it
            var active = await client.GetFromJsonAsync<Paged<RecycleListingResponse>>("/listings");
            Assert.DoesNotContain(active!.Items, x => x.Id == id);

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
            var l1Resp = await client.PostAsJsonAsync("/listings", new { Title = "L1", Description = "desc", CityExternalId = "facb9519-a654-9d5b-adba-25b9b6493ec1", Location = "street", AvailableFrom = DateOnly.FromDateTime(DateTime.UtcNow), AvailableTo = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(1)), Items = new[] { new { Type = 3, Quantity = 5 } } });
            l1Resp.EnsureSuccessStatusCode();
            var l1 = await l1Resp.Content.ReadFromJsonAsync<RecycleListingResponse>();
            Assert.NotNull(l1);

            var l2Resp = await client.PostAsJsonAsync("/listings", new { Title = "L2", Description = "desc", CityExternalId = "facb9519-a654-9d5b-adba-25b9b6493ec1", Location = "street", AvailableFrom = DateOnly.FromDateTime(DateTime.UtcNow), AvailableTo = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(1)), Items = new[] { new { Type = 3, Quantity = 6 } } });
            l2Resp.EnsureSuccessStatusCode();
            var l2 = await l2Resp.Content.ReadFromJsonAsync<RecycleListingResponse>();
            Assert.NotNull(l2);

            // Request pickup to make l2 PendingAcceptance
            client.SetTestUser("recycler-1", userType: "Recycler", isMitIdVerified: true);
            var pickup = await client.PostAsJsonAsync("/listings/pickup/request", new { ListingId = l2!.Id });
            pickup.EnsureSuccessStatusCode();

            // Create another in same city and then cancel it to make inactive
            client.SetTestUser("donator-1", userType: "Donator", isMitIdVerified: true);
            var l3Resp = await client.PostAsJsonAsync("/listings", new { Title = "L3", Description = "desc", CityExternalId = "facb9519-a654-9d5b-adba-25b9b6493ec1", Location = "street", AvailableFrom = DateOnly.FromDateTime(DateTime.UtcNow), AvailableTo = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(1)), Items = new[] { new { Type = 3, Quantity = 7 } } });
            l3Resp.EnsureSuccessStatusCode();
            var l3 = await l3Resp.Content.ReadFromJsonAsync<RecycleListingResponse>();
            var cancelResp = await client.PostAsJsonAsync("/listings/cancel", new { ListingId = l3!.Id });
            cancelResp.EnsureSuccessStatusCode();

            // Create listing in a different city
            var otherCityResp = await client.PostAsJsonAsync("/listings", new { Title = "OtherCity", Description = "desc", CityExternalId = "e21d5cb7-9f1a-4d8f-9e21-0f3f3a5d1234", Location = "street", AvailableFrom = DateOnly.FromDateTime(DateTime.UtcNow), AvailableTo = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(1)), Items = new[] { new { Type = 3, Quantity = 1 } } });
            otherCityResp.EnsureSuccessStatusCode();
            var other = await otherCityResp.Content.ReadFromJsonAsync<RecycleListingResponse>();

            // Search for CityExternalId of CPH
            var searchResp = await client.PostAsJsonAsync("/listings/search", new { CityExternalId = l1!.CityExternalId });
            searchResp.EnsureSuccessStatusCode();
            var results = await searchResp.Content.ReadFromJsonAsync<Paged<RecycleListingResponse>>();
            Assert.NotNull(results);
            // Only l1 (Created) and l2 (PendingAcceptance) should be present
            Assert.Contains(results!.Items, x => x.Id == l1.Id);
            Assert.Contains(results!.Items, x => x.Id == l2!.Id);
            Assert.DoesNotContain(results!.Items, x => x.Id == l3!.Id);
            Assert.DoesNotContain(results!.Items, x => x.Id == other!.Id);
        }

        [Fact]
        public async Task Search_Endpoint_Includes_All_Statuses_When_OnlyActive_False()
        {
            using var server = TestHostBuilder.CreateServer();
            using var client = server.CreateClient();

            // Create two listings in city1 and cancel one
            client.SetTestUser("donator-1", userType: "Donator", isMitIdVerified: true);
            var l1Resp = await client.PostAsJsonAsync("/listings", new { Title = "L1", Description = "desc", CityExternalId = "facb9519-a654-9d5b-adba-25b9b6493ec1", Location = "street", AvailableFrom = DateOnly.FromDateTime(DateTime.UtcNow), AvailableTo = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(1)), Items = new[] { new { Type = 3, Quantity = 5 } } });
            l1Resp.EnsureSuccessStatusCode();
            var l1 = await l1Resp.Content.ReadFromJsonAsync<RecycleListingResponse>();
            var l2Resp = await client.PostAsJsonAsync("/listings", new { Title = "L2", Description = "desc", CityExternalId = "facb9519-a654-9d5b-adba-25b9b6493ec1", Location = "street", AvailableFrom = DateOnly.FromDateTime(DateTime.UtcNow), AvailableTo = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(1)), Items = new[] { new { Type = 3, Quantity = 6 } } });
            l2Resp.EnsureSuccessStatusCode();
            var l2 = await l2Resp.Content.ReadFromJsonAsync<RecycleListingResponse>();

            // Cancel l2
            var cancel = await client.PostAsJsonAsync("/listings/cancel", new { ListingId = l2!.Id });
            cancel.EnsureSuccessStatusCode();

            // Search for city with onlyActive=false
            var searchResp = await client.PostAsJsonAsync("/listings/search", new { CityExternalId = l1!.CityExternalId, OnlyActive = false });
            searchResp.EnsureSuccessStatusCode();
            var results = await searchResp.Content.ReadFromJsonAsync<Paged<RecycleListingResponse>>();
            Assert.NotNull(results);
            Assert.Contains(results!.Items, x => x.Id == l1.Id);
            Assert.Contains(results!.Items, x => x.Id == l2!.Id);
        }

        [Fact]
        public async Task Search_Endpoint_Excludes_Listings_Already_Applied_By_User()
        {
            using var server = TestHostBuilder.CreateServer();
            using var client = server.CreateClient();

            // Create two listings in same city
            client.SetTestUser("donator-1", userType: "Donator", isMitIdVerified: true);
            var l1Resp = await client.PostAsJsonAsync("/listings", new { Title = "L1", Description = "desc", CityExternalId = "facb9519-a654-9d5b-adba-25b9b6493ec1", Location = "street", AvailableFrom = DateOnly.FromDateTime(DateTime.UtcNow), AvailableTo = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(1)), Items = new[] { new { Type = 3, Quantity = 5 } } });
            l1Resp.EnsureSuccessStatusCode();
            var l1 = await l1Resp.Content.ReadFromJsonAsync<RecycleListingResponse>();
            var l2Resp = await client.PostAsJsonAsync("/listings", new { Title = "L2", Description = "desc", CityExternalId = "facb9519-a654-9d5b-adba-25b9b6493ec1", Location = "street", AvailableFrom = DateOnly.FromDateTime(DateTime.UtcNow), AvailableTo = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(1)), Items = new[] { new { Type = 3, Quantity = 6 } } });
            l2Resp.EnsureSuccessStatusCode();
            var l2 = await l2Resp.Content.ReadFromJsonAsync<RecycleListingResponse>();

            // Recycler applies for l2
            client.SetTestUser("recycler-1", userType: "Recycler", isMitIdVerified: true);
            var pickup = await client.PostAsJsonAsync("/listings/pickup/request", new { ListingId = l2!.Id });
            pickup.EnsureSuccessStatusCode();

            // Search should exclude l2 for recycler-1
            var searchResp = await client.PostAsJsonAsync("/listings/search", new { CityExternalId = l1!.CityExternalId });
            searchResp.EnsureSuccessStatusCode();
            var results = await searchResp.Content.ReadFromJsonAsync<Paged<RecycleListingResponse>>();
            Assert.NotNull(results);
            Assert.Contains(results!.Items, x => x.Id == l1.Id);
            Assert.DoesNotContain(results!.Items, x => x.Id == l2!.Id);
        }

        [Fact]
        public async Task Create_With_Initial_Coordinates_Sets_Meeting_Fields()
        {
            using var server = TestHostBuilder.CreateServer();
            using var client = server.CreateClient();

            client.SetTestUser("donator-1", userType: "Donator", isMitIdVerified: true);
            var createResp = await client.PostAsJsonAsync("/listings", new
            {
                Title = "Cans",
                Description = "Bag of cans",
                CityExternalId = "facb9519-a654-9d5b-adba-25b9b6493ec1",
                Location = "street",
                AvailableFrom = DateOnly.FromDateTime(DateTime.UtcNow),
                AvailableTo = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(2)),
                Latitude =55.6761m,
                Longitude =12.5683m,
                Items = new[] { new { Type =3, Quantity =50 } }
            });
            createResp.EnsureSuccessStatusCode();
            var listing = await createResp.Content.ReadFromJsonAsync<RecycleListingResponse>();
            Assert.NotNull(listing);
            Assert.Equal(55.6761m, listing!.MeetingPointLatitude);
            Assert.Equal(12.5683m, listing!.MeetingPointLongtitude);
        }

        [Fact]
        public async Task Search_By_Coordinates_Only_Returns_Within_5km()
        {
            using var server = TestHostBuilder.CreateServer();
            using var client = server.CreateClient();
            client.SetTestUser("donator-1", userType: "Donator", isMitIdVerified: true);

            // Create two listings with coordinates: one near, one far
            var near = await client.PostAsJsonAsync("/listings", new { Title = "Near", Description = "desc", CityExternalId = "facb9519-a654-9d5b-adba-25b9b6493ec1", Location = "street", AvailableFrom = DateOnly.FromDateTime(DateTime.UtcNow), AvailableTo = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(1)), Latitude =55.6761m, Longitude =12.5683m, Items = new[] { new { Type =3, Quantity =1 } } });
            near.EnsureSuccessStatusCode();
            _ = await near.Content.ReadFromJsonAsync<RecycleListingResponse>();

            var far = await client.PostAsJsonAsync("/listings", new { Title = "Far", Description = "desc", CityExternalId = "facb9519-a654-9d5b-adba-25b9b6493ec1", Location = "street", AvailableFrom = DateOnly.FromDateTime(DateTime.UtcNow), AvailableTo = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(1)), Latitude =55.0m, Longitude =12.0m, Items = new[] { new { Type =3, Quantity =1 } } });
            far.EnsureSuccessStatusCode();
            _ = await far.Content.ReadFromJsonAsync<RecycleListingResponse>();

            // Search with coordinates near Copenhagen
            client.SetTestUser("recycler-1", userType: "Recycler", isMitIdVerified: true);
            var search = await client.PostAsJsonAsync("/listings/search", new { Latitude = 55.6761m, Longitude = 12.5683m });
            search.EnsureSuccessStatusCode();
            var results = await search.Content.ReadFromJsonAsync<Paged<RecycleListingResponse>>();
            Assert.NotNull(results);
            Assert.Contains(results!.Items, x => x.Title == "Near");
            // The far one should be filtered by bounding box and distance approx; with bounding box only, it's already far enough
            Assert.DoesNotContain(results!.Items, x => x.Title == "Far");
        }

        [Fact]
        public async Task Search_By_City_And_Coordinates_Unions_Results()
        {
            using var server = TestHostBuilder.CreateServer();
            using var client = server.CreateClient();
            client.SetTestUser("donator-1", userType: "Donator", isMitIdVerified: true);

            // Create one listing in CPH without coordinates
            var cph = await client.PostAsJsonAsync("/listings", new { Title = "CityOnly", Description = "desc", CityExternalId = "facb9519-a654-9d5b-adba-25b9b6493ec1", Location = "street", AvailableFrom = DateOnly.FromDateTime(DateTime.UtcNow), AvailableTo = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(1)), Items = new[] { new { Type =3, Quantity =1 } } });
            cph.EnsureSuccessStatusCode();
            var cphListing = await cph.Content.ReadFromJsonAsync<RecycleListingResponse>();

            // Another listing in other city but near coordinates
            var aal = await client.PostAsJsonAsync("/listings", new { Title = "NearCoord", Description = "desc", CityExternalId = "e21d5cb7-9f1a-4d8f-9e21-0f3f3a5d1234", Location = "street", AvailableFrom = DateOnly.FromDateTime(DateTime.UtcNow), AvailableTo = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(1)), Latitude =55.6762m, Longitude =12.5684m, Items = new[] { new { Type =3, Quantity =1 } } });
            aal.EnsureSuccessStatusCode();
            _ = await aal.Content.ReadFromJsonAsync<RecycleListingResponse>();

            // Search for CPH city plus coordinates near Copenhagen
            client.SetTestUser("recycler-1", userType: "Recycler", isMitIdVerified: true);
            var search = await client.PostAsJsonAsync("/listings/search", new { CityExternalId = cphListing!.CityExternalId, Latitude = 55.6761m, Longitude = 12.5683m });
            search.EnsureSuccessStatusCode();
            var results = await search.Content.ReadFromJsonAsync<Paged<RecycleListingResponse>>();
            Assert.NotNull(results);
            Assert.Contains(results!.Items, x => x.Title == "CityOnly");
            Assert.Contains(results!.Items, x => x.Title == "NearCoord");
        }
    }
}
