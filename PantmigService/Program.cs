using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.OpenApi.Models;
using Microsoft.IdentityModel.Tokens;
using System.Security.Claims;
using System.Text;
using PantmigService.Entities;
using PantmigService.Endpoints;
using PantmigService.Services;
using PantmigService.Data;
using Microsoft.EntityFrameworkCore;
using Serilog;
using PantmigService.Hubs;
using PantmigService.Seed;
using System.Linq;
using PantmigService.Security;
using PantmigService.Endpoints.Helpers;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Server.Kestrel.Core;

namespace PantmigService
{
    public class Program
    {
        public static void Main(string[] args)
        {
            var builder = WebApplication.CreateBuilder(args);

            // Only configure Serilog sinks in code, not in appsettings.json
            Log.Logger = new LoggerConfiguration()
                .ReadFrom.Configuration(builder.Configuration)
                .Enrich.FromLogContext()
                .WriteTo.Console()
                .WriteTo.MSSqlServer(
                    connectionString: builder.Configuration.GetConnectionString("PantmigConnection")!,
                    sinkOptions: new Serilog.Sinks.MSSqlServer.MSSqlServerSinkOptions
                    {
                        TableName = "Logs",
                        AutoCreateSqlTable = true
                    })
                .CreateLogger();

            builder.Host.UseSerilog();

            // Increase body size limits to support multi-image uploads via multipart/form-data
            const long MaxRequestBytes = 64L * 1024 * 1024; // 64 MB
            builder.WebHost.ConfigureKestrel(options =>
            {
                options.Limits.MaxRequestBodySize = MaxRequestBytes;
            });
            builder.Services.Configure<FormOptions>(options =>
            {
                options.MultipartBodyLengthLimit = MaxRequestBytes; // applies to form uploads
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

            // CORS to allow front-end origins from configuration
            var allowedOrigins = builder.Configuration
                .GetSection("Cors:AllowedOrigins")
                .GetChildren()
                .Select(c => c.Value)
                .Where(v => !string.IsNullOrWhiteSpace(v))
                .Cast<string>()
                .ToArray();

            if (allowedOrigins.Length == 0)
            {
                //defaults for local dev if no allowedOrigins found.
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

            // JWT Authentication
            var jwtSettings = builder.Configuration.GetSection("JwtSettings");
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

                // Allow SignalR to receive access token via query string for WebSockets
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

            // Swagger & API Explorer (OpenAPI specification)
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

            // EF Core SQL Server
            builder.Services.AddDbContext<PantmigDbContext>(opt =>
                opt.UseSqlServer(builder.Configuration.GetConnectionString("PantmigConnection")));

            // App services
            builder.Services.AddScoped<IRecycleListingService, RecycleListingService>();
            builder.Services.AddScoped<ICityResolver, CityResolver>();
            builder.Services.AddScoped<IRecycleListingValidationService, RecycleListingValidationService>();
            builder.Services.AddScoped<IFileValidationService, FileValidationService>();
            builder.Services.AddScoped<IChatValidationService, ChatValidationService>();
            builder.Services.AddScoped<ICreateListingRequestParser, CreateListingRequestParser>();
            builder.Services.AddScoped<IStatisticsService, StatisticsService>();
            builder.Services.AddScoped<INotificationService, NotificationService>();

            // Antivirus scanner (ClamAV)
            var clamSection = builder.Configuration.GetSection("ClamAV");
            var clamOptions = clamSection.Get<ClamAvOptions>() ?? new ClamAvOptions();
            builder.Services.AddSingleton<IAntivirusScanner>(_ => new ClamAvAntivirusScanner(clamOptions));

            // Real-time
            builder.Services.AddSignalR();

            var app = builder.Build();

            // Optional CSV seed of postal codes at startup
            using (var scope = app.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<PantmigDbContext>();
                var csvPath = Path.Combine(AppContext.BaseDirectory, "postal_codes_da.csv");
                try
                {
                    PostalCodeCsvSeeder.SeedAsync(db, csvPath).GetAwaiter().GetResult();
                }
                catch (Exception ex)
                {
                    Log.Warning(ex, "PostalCodeCsvSeeder failed");
                }
            }

            // Configure the HTTP request pipeline.
            if (app.Environment.IsDevelopment())
            {
                app.UseSwagger();
                app.UseSwaggerUI();
            }

            app.UseHttpsRedirection();

            // Enable CORS early to handle preflight requests
            app.UseCors("FrontendCors");

            app.UseAuthentication();
            app.UseAuthorization();

            // Endpoints
            app.MapRecycleListingEndpoints();
            app.MapCityEndpoints();
            app.MapStatisticsEndpoints();
            app.MapNotificationEndpoints();
            app.MapHub<ChatHub>("/hubs/chat");
            app.MapHub<NotificationsHub>("/hubs/notifications");

            app.Run();
        }
    }
}
