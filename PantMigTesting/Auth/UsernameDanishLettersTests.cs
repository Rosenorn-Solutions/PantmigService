using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AuthService.Data;
using AuthService.Entities;
using AuthService.Models;
using AuthService.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace PantMigTesting.Auth
{
    public class UsernameDanishLettersTests
    {
        private static ServiceProvider BuildServiceProvider()
        {
            var services = new ServiceCollection();

            // Minimal configuration for UsernameGenerator prefixes
            var dict = new Dictionary<string, string?>
            {
                ["UsernameGenerators:0"] = "Odder",
                ["JwtSettings:SecretKey"] = "test_secret_key_12345678901234567890",
                ["JwtSettings:Issuer"] = "test",
                ["JwtSettings:Audience"] = "test"
            };
            IConfiguration config = new ConfigurationBuilder()
                .AddInMemoryCollection(dict)
                .Build();

            services.AddSingleton(config);

            services.AddDbContext<ApplicationDbContext>(o => o.UseInMemoryDatabase(Guid.NewGuid().ToString()));

            services.AddIdentity<ApplicationUser, IdentityRole>(options =>
            {
                options.Password.RequireDigit = true;
                options.Password.RequiredLength = 8;
                options.Password.RequireLowercase = true;
                options.Password.RequireUppercase = true;
                options.Password.RequireNonAlphanumeric = true;
                options.User.RequireUniqueEmail = true;
                // Use default AllowedUserNameCharacters without Danish letters
            })
            .AddEntityFrameworkStores<ApplicationDbContext>()
            .AddDefaultTokenProviders();

            services.AddLogging();

            services.AddScoped<IUsernameGenerator, UsernameGenerator>();
            services.AddScoped<IAuthService, AuthServiceImpl>();
            services.AddScoped<ITokenService, FakeTokenService>();

            return services.BuildServiceProvider();
        }

        private static (UserManager<ApplicationUser> users, RoleManager<IdentityRole> roles, IUsernameGenerator gen, IAuthService auth, IConfiguration config) Resolve(IServiceProvider sp)
        {
            return (
                sp.GetRequiredService<UserManager<ApplicationUser>>(),
                sp.GetRequiredService<RoleManager<IdentityRole>>(),
                sp.GetRequiredService<IUsernameGenerator>(),
                sp.GetRequiredService<IAuthService>(),
                sp.GetRequiredService<IConfiguration>()
            );
        }

        private sealed class FakeTokenService : ITokenService
        {
            public (string accessToken, DateTime expiresAt) GenerateAccessToken(ApplicationUser user)
                => ("test-token", DateTime.UtcNow.AddMinutes(5));

            public Task<string> GenerateAndStoreRefreshTokenAsync(ApplicationUser user, System.Threading.CancellationToken ct = default)
                => Task.FromResult("refresh");

            public Task<(AuthResponse? response, string? error)> RotateRefreshTokenAsync(string accessToken, string refreshToken, System.Threading.CancellationToken ct = default)
                => Task.FromResult<(AuthResponse?, string?)>((null, "not-implemented"));
        }

        [Fact]
        public async Task UsernameGenerator_Produces_ASCII_Only_And_CreateUser_Succeeds()
        {
            using var sp = BuildServiceProvider();
            var (userManager, _, gen, _, _) = Resolve(sp);

            var username = await gen.GenerateAsync("ורז", "ֵ״ֶ");

            // Assert username contains ASCII only (no Danish letters)
            Assert.True(username.All(ch => ch <= 127), $"Username '{username}' should be ASCII only");

            var user = new ApplicationUser
            {
                UserName = username,
                Email = "test@example.com"
            };

            var result = await userManager.CreateAsync(user, "Password!1");
            Assert.True(result.Succeeded, string.Join("; ", result.Errors.Select(e => e.Description)));
        }

        [Fact]
        public async Task RegisterAsync_Allows_ASCII_Usernames()
        {
            using var sp = BuildServiceProvider();
            var (userManager, roleManager, gen, auth, _) = Resolve(sp);

            // Ensure UsernameGenerator is the real one using userManager
            Assert.IsType<UsernameGenerator>(gen);

            var req = new RegisterRequest
            {
                Email = "ascii@example.com",
                Password = "Password!1",
                FirstName = "ורז",
                LastName = "ֵ״ֶ",
                UserType = UserType.Recycler
            };

            var (ok, err, user) = await auth.RegisterAsync(req, cityId: null, userManager, roleManager, new FakeTokenService(), gen);
            Assert.True(ok, err);
            Assert.NotNull(user);
            Assert.True(user!.UserName!.All(ch => ch <= 127), "Generated username should be ASCII only");
        }
    }
}
