using AuthService.Data;
using AuthService.Endpoints;
using AuthService.Entities;
using AuthService.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using System.Collections.Concurrent;
using System.Text;

namespace PantMigTesting.AuthServiceTests;

public static class AuthTestServer
{
    private sealed class DefaultCapturingEmailSender : IEmailSender
    {
        public static readonly ConcurrentQueue<(string To, string Subject, string Body)> Sent = new();
        public static void Clear() { while (Sent.TryDequeue(out _)) { } }
        public Task SendAsync(string to, string subject, string body, CancellationToken ct = default)
        {
            Sent.Enqueue((to, subject, body));
            return Task.CompletedTask;
        }
    }

    public static TestServer Create() => Create(configureTestServices: null);

    public static TestServer Create(Action<IServiceCollection>? configureTestServices)
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

        var dbName = Guid.NewGuid().ToString();
        DefaultCapturingEmailSender.Clear();

        var builder = new WebHostBuilder()
            .ConfigureAppConfiguration(cfg => cfg.AddInMemoryCollection(inMemorySettings))
            .ConfigureServices((context, services) =>
            {
                var config = context.Configuration;
                var secret = config["JwtSettings:SecretKey"]!;
                var issuer = config["JwtSettings:Issuer"]!;
                var audience = config["JwtSettings:Audience"]!;

                services.AddRouting();
                services.AddDataProtection();
                services.AddDbContext<ApplicationDbContext>(opt => opt.UseInMemoryDatabase(dbName));

                services.AddIdentity<ApplicationUser, IdentityRole>(options =>
                {
                    options.User.RequireUniqueEmail = true;
                })
                .AddEntityFrameworkStores<ApplicationDbContext>()
                .AddDefaultTokenProviders();

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
                services.AddSingleton<IEmailSender, DefaultCapturingEmailSender>();
                services.AddEndpointsApiExplorer();
            })
            .ConfigureTestServices(services =>
            {
                configureTestServices?.Invoke(services);
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

    public static IReadOnlyCollection<(string To, string Subject, string Body)> GetSentEmails()
        => DefaultCapturingEmailSender.Sent.ToArray();
}
