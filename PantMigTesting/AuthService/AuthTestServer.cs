using System.Text;
using System.Text.Json;
using AuthService.Data;
using AuthService.Entities;
using AuthService.Endpoints;
using AuthService.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using Microsoft.AspNetCore.Authentication.JwtBearer;

namespace PantMigTesting.AuthServiceTests;

public static class AuthTestServer
{
    public static TestServer Create()
    {
        var inMemorySettings = new Dictionary<string, string?>
        {
            ["JwtSettings:SecretKey"] = "a-very-long-test-secret-key-12345678901234567890",
            ["JwtSettings:Issuer"] = "TestIssuer",
            ["JwtSettings:Audience"] = "TestAudience",
            ["JwtSettings:AccessTokenMinutes"] = "60",
            ["JwtSettings:RefreshTokenDays"] = "14",
            ["UsernameGenerators:0"] = "User",
            ["UsernameGenerators:1"] = "Pantmig",
            ["ConnectionStrings:PantmigConnection"] = "Data Source=(localdb)\\MSSQLLocalDB;Initial Catalog=PantmigAuthTest;Integrated Security=True;TrustServerCertificate=True"
        };

        // Use one in-memory database name for the lifetime of this TestServer instance
        var dbName = Guid.NewGuid().ToString();

        var builder = new WebHostBuilder()
            .ConfigureAppConfiguration(cfg => cfg.AddInMemoryCollection(inMemorySettings))
            .ConfigureServices(services =>
            {
                services.AddRouting();

                // Use in-memory DB for tests
                services.AddDbContext<ApplicationDbContext>(opt => opt.UseInMemoryDatabase(dbName));

                services.AddIdentity<ApplicationUser, IdentityRole>(options =>
                {
                    options.User.RequireUniqueEmail = true;
                })
                .AddEntityFrameworkStores<ApplicationDbContext>()
                .AddDefaultTokenProviders();

                var provider = services.BuildServiceProvider();
                var config = provider.GetRequiredService<IConfiguration>();
                var secret = config["JwtSettings:SecretKey"]!;
                var issuer = config["JwtSettings:Issuer"]!;
                var audience = config["JwtSettings:Audience"]!;

                services.AddAuthentication(options =>
                {
                    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
                    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
                })
                .AddJwtBearer(options =>
                {
                    options.TokenValidationParameters = new TokenValidationParameters
                    {
                        ValidateIssuer = true,
                        ValidateAudience = true,
                        ValidateLifetime = true,
                        ValidateIssuerSigningKey = true,
                        ValidIssuer = issuer,
                        ValidAudience = audience,
                        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret)),
                        ClockSkew = TimeSpan.Zero
                    };
                });

                services.AddAuthorization();

                services.AddMemoryCache();
                services.AddScoped<ITokenService, TokenService>();
                services.AddScoped<ICityResolver, CityResolver>();
                services.AddScoped<IUsernameGenerator, UsernameGenerator>();
                services.AddScoped<IAuthService, AuthServiceImpl>();

                services.AddEndpointsApiExplorer();
            })
            .Configure(app =>
            {
                app.UseRouting();
                app.UseAuthentication();
                app.UseAuthorization();
                app.UseEndpoints(endpoints =>
                {
                    endpoints.MapAuthEndpoints();
                });
            });

        return new TestServer(builder);
    }
}
