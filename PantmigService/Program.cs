using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using PantmigService.Data;
using PantmigService.Endpoints;
using PantmigService.Endpoints.Helpers;
using PantmigService.Hubs;
using PantmigService.Security;
using PantmigService.Seed;
using PantmigService.Services;
using PantmigService.Logging; // added
using Serilog;
using Serilog.Core; // added
using Serilog.Events; // added
using System.Text;

namespace PantmigService
{
    public class Program
    {
        public static void Main(string[] args)
        {
            var builder = WebApplication.CreateBuilder(args);

            // Cache configuration sections once
            var configuration = builder.Configuration;
            var jwtSettings = configuration.GetSection("JwtSettings");

            // Level switches allow dynamic adjustment
            var consoleLevelSwitch = new LoggingLevelSwitch(LogEventLevel.Debug); // verbose during startup
            var sqlLevelSwitch = new LoggingLevelSwitch(LogEventLevel.Information);

            // Minimal bootstrap logger (console only)
            Log.Logger = new LoggerConfiguration()
                .MinimumLevel.ControlledBy(consoleLevelSwitch)
                .Enrich.FromLogContext()
                .WriteTo.Console(levelSwitch: consoleLevelSwitch)
                .CreateLogger();

            builder.Host.UseSerilog();

            // Register level switches & deferred initializer
            builder.Services.AddSingleton(consoleLevelSwitch);
            builder.Services.AddSingleton(sqlLevelSwitch);
            builder.Services.AddHostedService<DeferredSqlLoggerInitializer>();
            builder.Services.AddHostedService<PostalCodeSeedingHostedService>(); // added hosted seeding

            const long MaxRequestBytes = 64L * 1024 * 1024;
            builder.WebHost.ConfigureKestrel(options =>
            {
                options.Limits.MaxRequestBodySize = MaxRequestBytes;
            });
            builder.Services.Configure<FormOptions>(options =>
            {
                options.MultipartBodyLengthLimit = MaxRequestBytes;
            });

            builder.Services.AddAuthorizationBuilder()
                .AddPolicy("VerifiedDonator", policy =>
                {
                    policy.RequireAuthenticatedUser();
                    policy.RequireAssertion(ctx =>
                    {
                        var type = ctx.User.FindFirst("userType")?.Value;
                        return string.Equals(type, "Donator", StringComparison.OrdinalIgnoreCase);
                    });
                });

            // Cache allowed origins once
            var allowedOrigins = configuration
                .GetSection("Cors:AllowedOrigins")
                .GetChildren()
                .Select(c => c.Value)
                .Where(v => !string.IsNullOrWhiteSpace(v))
                .Cast<string>()
                .ToArray();

            if (allowedOrigins.Length == 0)
            {
                allowedOrigins =
                [
                    "http://localhost:8081",
                    "https://localhost:8081",
                    "http://127.0.0.1:8081",
                    "https://127.0.0.1:8081"
                ];
            }

            builder.Services.AddCors(options =>
            {
                options.AddPolicy("FrontendCors", policy =>
                {
                    policy.WithOrigins(allowedOrigins)
                          .AllowAnyHeader()
                          .AllowAnyMethod()
                          .AllowCredentials();
                });
            });

            var secretKey = jwtSettings["SecretKey"];
            var issuer = jwtSettings["Issuer"];
            var audience = jwtSettings["Audience"];

            builder.Services.AddAuthentication(options =>
            {
                options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
                options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
            }).AddJwtBearer(options =>
            {
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidateAudience = true,
                    ValidateLifetime = true,
                    ValidateIssuerSigningKey = true,
                    ValidIssuer = issuer,
                    ValidAudience = audience,
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secretKey!)),
                    ClockSkew = TimeSpan.Zero
                };

                options.Events = new JwtBearerEvents
                {
                    OnMessageReceived = context =>
                    {
                        var accessToken = context.Request.Query["access_token"];
                        var path = context.HttpContext.Request.Path;
                        if (!string.IsNullOrEmpty(accessToken) && (path.StartsWithSegments("/hubs/chat") || path.StartsWithSegments("/hubs/notifications")))
                        {
                            context.Token = accessToken;
                        }
                        return Task.CompletedTask;
                    }
                };
            });

            builder.Services.AddEndpointsApiExplorer();
            builder.Services.AddSwaggerGen(c =>
            {
                c.SwaggerDoc("v1", new OpenApiInfo { Title = "PantmigService API", Version = "v1" });

                c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
                {
                    Description = "JWT Authorization header using the Bearer scheme. Example: 'Bearer 12345abcdef'",
                    Name = "Authorization",
                    In = ParameterLocation.Header,
                    Type = SecuritySchemeType.ApiKey,
                    Scheme = "Bearer"
                });

                c.AddSecurityRequirement(new OpenApiSecurityRequirement
                {
                    {
                        new OpenApiSecurityScheme
                        {
                            Reference = new OpenApiReference
                            {
                                Type = ReferenceType.SecurityScheme,
                                Id = "Bearer"
                            },
                            Scheme = "oauth2",
                            Name = "Bearer",
                            In = ParameterLocation.Header
                        },
                        new List<string>()
                    }
                });
            });

            builder.Services.AddDbContext<PantmigDbContext>(opt =>
                opt.UseSqlServer(configuration.GetConnectionString("PantmigConnection")));

            builder.Services.AddScoped<IRecycleListingService, RecycleListingService>();
            builder.Services.AddScoped<ICityResolver, CityResolver>();
            builder.Services.AddScoped<IRecycleListingValidationService, RecycleListingValidationService>();
            builder.Services.AddScoped<IFileValidationService, FileValidationService>();
            builder.Services.AddScoped<IChatValidationService, ChatValidationService>();
            builder.Services.AddScoped<ICreateListingRequestParser, CreateListingRequestParser>();
            builder.Services.AddScoped<IStatisticsService, StatisticsService>();
            builder.Services.AddScoped<INotificationService, NotificationService>();
            builder.Services.AddSingleton<IEmailSender, SmtpEmailSender>();

            var clamSection = configuration.GetSection("ClamAV");
            var clamOptions = clamSection.Get<ClamAvOptions>() ?? new ClamAvOptions();
            builder.Services.AddSingleton<IAntivirusScanner>(_ => new ClamAvAntivirusScanner(clamOptions));

            builder.Services.AddSignalR();
            builder.Services.AddMemoryCache();

            var app = builder.Build();

            if (app.Environment.IsDevelopment())
            {
                app.UseSwagger();
                app.UseSwaggerUI();
            }

            // Drop console verbosity after startup
            consoleLevelSwitch.MinimumLevel = LogEventLevel.Information;

            app.UseHttpsRedirection();
            app.UseCors("FrontendCors");
            app.UseAuthentication();
            app.UseAuthorization();

            app.MapRecycleListingEndpoints();
            app.MapCityEndpoints();
            app.MapStatisticsEndpoints();
            app.MapNotificationEndpoints();
            app.MapNewsletterEndpoints();
            app.MapNewsletterUnsubscribe();
            app.MapHub<ChatHub>("/hubs/chat");
            app.MapHub<NotificationsHub>("/hubs/notifications");

            app.Run();
        }
    }
}
