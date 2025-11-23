using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Identity;
using AuthService.Entities;
using AuthService.Data;
using AuthService.Models; // UserType
using AuthService.Services; // PostalCodeCsvSeeder

namespace AuthService.Seed
{
    /// <summary>
    /// Performs non-critical startup seeding (roles, postal codes) after host starts to avoid blocking Program.Main.
    /// </summary>
    public class StartupSeedingHostedService : IHostedService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<StartupSeedingHostedService> _logger;
        private readonly IHostEnvironment _env;

        public StartupSeedingHostedService(IServiceScopeFactory scopeFactory, ILogger<StartupSeedingHostedService> logger, IHostEnvironment env)
        {
            _scopeFactory = scopeFactory;
            _logger = logger;
            _env = env;
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            _ = Task.Run(async () =>
            {
                using var scope = _scopeFactory.CreateScope();
                var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
                foreach (var roleName in new[] { nameof(AuthService.Models.UserType.Donator), nameof(AuthService.Models.UserType.Recycler) })
                {
                    try
                    {
                        if (!await roleManager.RoleExistsAsync(roleName))
                        {
                            await roleManager.CreateAsync(new IdentityRole(roleName));
                            _logger.LogInformation("Created missing role {Role}", roleName);
                        }
                    }
                    catch (System.Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed creating role {Role}", roleName);
                    }
                }

                var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                var csvPath = System.IO.Path.Combine(_env.ContentRootPath, "postal_codes_da.csv");
                try
                {
                    if (System.IO.File.Exists(csvPath))
                    {
                        await AuthService.Services.PostalCodeCsvSeeder.SeedAsync(db, csvPath, cancellationToken);
                        _logger.LogInformation("Postal codes seeded from {CsvPath}", csvPath);
                    }
                    else
                    {
                        _logger.LogInformation("Postal code CSV not found at {CsvPath}; skipping.", csvPath);
                    }
                }
                catch (System.Exception ex)
                {
                    _logger.LogWarning(ex, "Postal code seeding failed from {CsvPath}", csvPath);
                }
            }, cancellationToken);

            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
