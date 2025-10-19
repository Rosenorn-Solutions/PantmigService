using System.Net;
using System.Net.Http.Json;
using AuthService.Data;
using AuthService.Entities;
using AuthService.Endpoints;
using AuthService.Models;
using AuthService.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace PantMigTesting.Auth;

public class AvailabilityEndpointsTests
{
    // Minimal fake token service to satisfy DI for /auth endpoints
    private sealed class FakeTokenService : ITokenService
    {
        public (string accessToken, DateTime expiresAt) GenerateAccessToken(ApplicationUser user)
            => ("test-token", DateTime.UtcNow.AddMinutes(5));

        public Task<string> GenerateAndStoreRefreshTokenAsync(ApplicationUser user, CancellationToken ct = default)
            => Task.FromResult("refresh");

        public Task<(AuthResponse? response, string? error)> RotateRefreshTokenAsync(string accessToken, string refreshToken, CancellationToken ct = default)
            => Task.FromResult<(AuthResponse?, string?)>((null, "not-implemented"));
    }

    private static TestServer CreateServer(string? dbName = null)
    {
        var databaseName = dbName ?? Guid.NewGuid().ToString();

        var builder = new WebHostBuilder()
            .ConfigureServices(services =>
            {
                services.AddRouting();

                // Minimal configuration used by UsernameGenerator and Auth endpoints
                var dict = new Dictionary<string, string?>
                {
                    ["UsernameGenerators:0"] = "TestPrefix",
                    ["JwtSettings:SecretKey"] = "test_secret_key_12345678901234567890",
                    ["JwtSettings:Issuer"] = "test",
                    ["JwtSettings:Audience"] = "test",
                    ["Cache:UserRatingSeconds"] = "60"
                };
                IConfiguration config = new ConfigurationBuilder()
                    .AddInMemoryCollection(dict)
                    .Build();
                services.AddSingleton(config);

                services.AddDbContext<ApplicationDbContext>(o => o.UseInMemoryDatabase(databaseName));

                services.AddIdentity<ApplicationUser, IdentityRole>(options =>
                {
                    options.User.RequireUniqueEmail = true;
                })
                .AddEntityFrameworkStores<ApplicationDbContext>()
                .AddDefaultTokenProviders();

                // Register services used by mapped auth endpoints
                services.AddScoped<ICityResolver, CityResolver>();
                services.AddScoped<IUsernameGenerator, UsernameGenerator>();
                services.AddScoped<IAuthService, AuthServiceImpl>();
                services.AddScoped<ITokenService, FakeTokenService>();
                services.AddScoped<IEmailSender, SmtpEmailSender>();

                services.AddMemoryCache();
            })
            .Configure(app =>
            {
                app.UseRouting();
                app.UseEndpoints(endpoints =>
                {
                    endpoints.MapAuthEndpoints();
                });
            });

        return new TestServer(builder);
    }

    [Fact]
    public async Task CheckEmail_Returns_Taken_Status()
    {
        using var server = CreateServer();
        using var client = server.CreateClient();

        // Seed a user
        using (var scope = server.Services.CreateScope())
        {
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var user = new ApplicationUser
            {
                UserName = "user1",
                Email = "exists@example.com",
                PhoneNumber = "+4511122233"
            };
            var create = await userManager.CreateAsync(user, "Password!1");
            Assert.True(create.Succeeded, string.Join("; ", create.Errors.Select(e => e.Description)));
        }

        // Taken
        var respTaken = await client.GetAsync("/auth/check-email?email=exists@example.com");
        respTaken.EnsureSuccessStatusCode();
        var resTaken = await respTaken.Content.ReadFromJsonAsync<AvailabilityResult>();
        Assert.NotNull(resTaken);
        Assert.True(resTaken!.Taken);

        // Not taken
        var respFree = await client.GetAsync("/auth/check-email?email=free@example.com");
        respFree.EnsureSuccessStatusCode();
        var resFree = await respFree.Content.ReadFromJsonAsync<AvailabilityResult>();
        Assert.NotNull(resFree);
        Assert.False(resFree!.Taken);

        // Missing -> 400
        var bad = await client.GetAsync("/auth/check-email?email=");
        Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);
    }

    [Fact]
    public async Task CheckPhone_Returns_Taken_Status()
    {
        using var server = CreateServer();
        using var client = server.CreateClient();

        // Seed a user
        using (var scope = server.Services.CreateScope())
        {
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var user = new ApplicationUser
            {
                UserName = "user2",
                Email = "user2@example.com",
                PhoneNumber = "+4512345678"
            };
            var create = await userManager.CreateAsync(user, "Password!1");
            Assert.True(create.Succeeded, string.Join("; ", create.Errors.Select(e => e.Description)));
        }

        // Taken
        var respTaken = await client.GetAsync("/auth/check-phone?phone=%2B4512345678");
        respTaken.EnsureSuccessStatusCode();
        var resTaken = await respTaken.Content.ReadFromJsonAsync<AvailabilityResult>();
        Assert.NotNull(resTaken);
        Assert.True(resTaken!.Taken);

        // Not taken
        var respFree = await client.GetAsync("/auth/check-phone?phone=%2B4500000000");
        respFree.EnsureSuccessStatusCode();
        var resFree = await respFree.Content.ReadFromJsonAsync<AvailabilityResult>();
        Assert.NotNull(resFree);
        Assert.False(resFree!.Taken);

        // Missing -> 400
        var bad = await client.GetAsync("/auth/check-phone?phone=");
        Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);
    }
}
