using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
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
using PantmigService.Entities;
using PantmigService.Services;

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
        [Fact]
        public async Task Create_Requires_VerifiedDonator()
        {
            using var server = TestHostBuilder.CreateServer();
            using var client = server.CreateClient();

            // Recycler should be forbidden on donator-only endpoint
            client.SetTestUser("recycler-1", userType: "Recycler", isMitIdVerified: true);
            var badResp = await client.PostAsJsonAsync("/listings", new
            {
                Title = "Cans",
                Description = "Bag of cans",
                City = "CPH",
                EstimatedValue = (string?)null,
                EstimatedAmount = 100m,
                AvailableFrom = DateTime.UtcNow,
                AvailableTo = DateTime.UtcNow.AddHours(2)
            });
            Assert.Equal(HttpStatusCode.Forbidden, badResp.StatusCode);

            // Verified donator should succeed
            client.SetTestUser("donator-1", userType: "Donator", isMitIdVerified: true);
            var resp = await client.PostAsJsonAsync("/listings", new
            {
                Title = "Cans",
                Description = "Bag of cans",
                City = "CPH",
                EstimatedValue = (string?)null,
                EstimatedAmount = 100m,
                AvailableFrom = DateTime.UtcNow,
                AvailableTo = DateTime.UtcNow.AddHours(2)
            });

            Assert.Equal(HttpStatusCode.Created, resp.StatusCode);
            var created = await resp.Content.ReadFromJsonAsync<RecycleListing>();
            Assert.NotNull(created);
            Assert.True(created!.Id > 0);
            Assert.Equal("donator-1", created.CreatedByUserId);
        }

        [Fact]
        public async Task EndToEnd_Endpoints_Flow_Works()
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
                EstimatedValue = (string?)null,
                EstimatedAmount = 50m,
                AvailableFrom = DateTime.UtcNow,
                AvailableTo = DateTime.UtcNow.AddHours(2)
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

            // Now active should not include it
            client.SetTestUser("donator-1", userType: "Donator", isMitIdVerified: true);
            var active2 = await client.GetFromJsonAsync<List<RecycleListing>>("/listings");
            Assert.DoesNotContain(active2!, x => x.Id == id);

            // 3. Donator accepts
            var acceptResp = await client.PostAsJsonAsync("/listings/pickup/accept", new { ListingId = id });
            acceptResp.EnsureSuccessStatusCode();

            // 4. Start chat
            client.SetTestUser("recycler-1", userType: "Recycler", isMitIdVerified: true);
            var chatResp = await client.PostAsJsonAsync("/listings/chat/start", new { ListingId = id });
            chatResp.EnsureSuccessStatusCode();

            // 4.1 Donator sets meeting point
            client.SetTestUser("donator-1", userType: "Donator", isMitIdVerified: true);
            var setMeetingResp = await client.PostAsJsonAsync("/listings/meeting/set", new { ListingId = id, Latitude = 55.6761m, Longitude = 12.5683m });
            setMeetingResp.EnsureSuccessStatusCode();

            // 5. Recycler confirms pickup
            client.SetTestUser("recycler-1", userType: "Recycler", isMitIdVerified: true);
            var confirmResp = await client.PostAsJsonAsync("/listings/pickup/confirm", new { ListingId = id });
            confirmResp.EnsureSuccessStatusCode();

            // 6. Recycler submits receipt
            var submitReceiptResp = await client.PostAsJsonAsync("/listings/receipt/submit", new { ListingId = id, ReceiptImageUrl = "http://img/1.png", ReportedAmount = 123.45m });
            submitReceiptResp.EnsureSuccessStatusCode();

            // 7. Donator verifies receipt
            client.SetTestUser("donator-1", userType: "Donator", isMitIdVerified: true);
            var verifyResp = await client.PostAsJsonAsync("/listings/receipt/verify", new { ListingId = id, VerifiedAmount = 120m });
            verifyResp.EnsureSuccessStatusCode();

            // GET by id and assert final state
            var final = await client.GetFromJsonAsync<RecycleListing>($"/listings/{id}");
            Assert.NotNull(final);
            Assert.Equal(ListingStatus.Completed, final!.Status);
            Assert.Equal(120m, final.VerifiedAmount);
            Assert.False(final.IsActive);
            Assert.Equal($"listing-{id}", final.ChatSessionId);
            Assert.Equal(55.676100m, final.MeetingLatitude);
            Assert.Equal(12.568300m, final.MeetingLongitude);
        }
    }
}
