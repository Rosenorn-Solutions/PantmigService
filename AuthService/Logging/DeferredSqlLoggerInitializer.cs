using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Configuration;
using Serilog;
using Serilog.Core;
using Serilog.Events;
using Serilog.Sinks.MSSqlServer;

namespace AuthService.Logging
{
    /// <summary>
    /// Deferred initialization of MSSqlServer Serilog sink to reduce startup blocking time.
    /// Only activates in Production when connection string is present.
    /// </summary>
    public class DeferredSqlLoggerInitializer : IHostedService
    {
        private readonly IConfiguration _configuration;
        private readonly IHostEnvironment _env;
        private readonly LoggingLevelSwitch _consoleLevelSwitch;

        public DeferredSqlLoggerInitializer(IConfiguration configuration, IHostEnvironment env, LoggingLevelSwitch consoleLevelSwitch)
        {
            _configuration = configuration;
            _env = env;
            _consoleLevelSwitch = consoleLevelSwitch;
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            if (!_env.IsProduction()) return Task.CompletedTask;
            var conn = _configuration.GetConnectionString("PantmigConnection");
            if (string.IsNullOrWhiteSpace(conn)) return Task.CompletedTask;

            try
            {
                var logger = new LoggerConfiguration()
                    .MinimumLevel.ControlledBy(_consoleLevelSwitch)
                    .Enrich.FromLogContext()
                    .WriteTo.Console(levelSwitch: _consoleLevelSwitch)
                    .WriteTo.MSSqlServer(
                        connectionString: conn!,
                        sinkOptions: new MSSqlServerSinkOptions
                        {
                            TableName = "Logs",
                            AutoCreateSqlTable = false,
                            BatchPostingLimit = 200,
                            BatchPeriod = System.TimeSpan.FromSeconds(10)
                        },
                        restrictedToMinimumLevel: LogEventLevel.Information
                    )
                    .CreateLogger();

                Log.Logger = logger;
            }
            catch (System.Exception ex)
            {
                Log.Warning(ex, "Deferred MSSqlServer logger init failed; using console only.");
            }
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
