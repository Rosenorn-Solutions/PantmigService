using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Serilog;
using Serilog.Core;
using Serilog.Events;
using Serilog.Sinks.MSSqlServer;

namespace PantmigService.Logging
{
    /// <summary>
    /// Defers heavy MSSqlServer sink initialization until after the host has started.
    /// </summary>
    public class DeferredSqlLoggerInitializer : IHostedService
    {
        private readonly IConfiguration _configuration;
        private readonly IHostEnvironment _env;
        private readonly LoggingLevelSwitch _sqlLevelSwitch; // retained for potential dynamic adjustments
        private readonly LoggingLevelSwitch _consoleLevelSwitch;

        public DeferredSqlLoggerInitializer(
            IConfiguration configuration,
            IHostEnvironment env,
            LoggingLevelSwitch sqlLevelSwitch,
            LoggingLevelSwitch consoleLevelSwitch)
        {
            _configuration = configuration;
            _env = env;
            _sqlLevelSwitch = sqlLevelSwitch;
            _consoleLevelSwitch = consoleLevelSwitch;
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            // Only add MSSql sink in Production (can be adjusted if needed)
            if (!_env.IsProduction()) return Task.CompletedTask;
            var conn = _configuration.GetConnectionString("PantmigConnection");
            if (string.IsNullOrWhiteSpace(conn)) return Task.CompletedTask;

            try
            {
                var logger = new LoggerConfiguration()
                    .MinimumLevel.ControlledBy(_consoleLevelSwitch) // global control
                    .Enrich.FromLogContext()
                    .WriteTo.Console(levelSwitch: _consoleLevelSwitch)
                    .WriteTo.MSSqlServer(
                        connectionString: conn!,
                        sinkOptions: new MSSqlServerSinkOptions
                        {
                            TableName = "Logs",
                            AutoCreateSqlTable = false, // assume table already created
                            BatchPostingLimit = 200,
                            BatchPeriod = System.TimeSpan.FromSeconds(10)
                        },
                        restrictedToMinimumLevel: LogEventLevel.Information
                    )
                    .CreateLogger();

                Log.Logger = logger; // swap global logger
            }
            catch (System.Exception ex)
            {
                // Fall back silently; keep console-only logger
                Log.Warning(ex, "Deferred MSSqlServer logger initialization failed; continuing with console only.");
            }
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
