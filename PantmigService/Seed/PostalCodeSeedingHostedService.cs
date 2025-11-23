using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using PantmigService.Data;

namespace PantmigService.Seed
{
    /// <summary>
    /// Runs postal code CSV seeding after the host has started to avoid blocking startup.
    /// </summary>
    public class PostalCodeSeedingHostedService : IHostedService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<PostalCodeSeedingHostedService> _logger;
        private readonly IHostEnvironment _env;

        public PostalCodeSeedingHostedService(IServiceScopeFactory scopeFactory, ILogger<PostalCodeSeedingHostedService> logger, IHostEnvironment env)
        {
            _scopeFactory = scopeFactory;
            _logger = logger;
            _env = env;
        }

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            // Fire-and-forget style: run seeding but do not block startup for a long time.
            _ = Task.Run(async () =>
            {
                using var scope = _scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<PantmigDbContext>();
                var csvPath = System.IO.Path.Combine(System.AppContext.BaseDirectory, "postal_codes_da.csv");
                try
                {
                    await PostalCodeCsvSeeder.SeedAsync(db, csvPath, cancellationToken);
                    _logger.LogInformation("Postal code seeding finished (path={CsvPath}).", csvPath);
                }
                catch (System.Exception ex)
                {
                    _logger.LogWarning(ex, "Postal code seeding failed (path={CsvPath}).", csvPath);
                }
            }, cancellationToken);
        }

        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
