using AuthService.Data;
using AuthService.Endpoints;
using AuthService.Entities;
using AuthService.Models;
using AuthService.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.AspNetCore.OpenApi;
using Serilog;
using Serilog.Core; // added
using Serilog.Events; // added
using AuthService.Logging; // added
using AuthService.Seed; // added
using System.Text;

var builder = WebApplication.CreateBuilder(args);

// Cache configuration
var configuration = builder.Configuration;
var jwtSettings = configuration.GetSection("JwtSettings");

// Level switches
var consoleLevelSwitch = new LoggingLevelSwitch(LogEventLevel.Debug);

// Minimal bootstrap logger (console only)
Log.Logger = new LoggerConfiguration()
    .MinimumLevel.ControlledBy(consoleLevelSwitch)
    .Enrich.FromLogContext()
    .WriteTo.Console(levelSwitch: consoleLevelSwitch)
    .CreateLogger();

builder.Host.UseSerilog();

// Deferred MSSql sink hosted service & seeding hosted service
builder.Services.AddSingleton(consoleLevelSwitch);
builder.Services.AddHostedService<DeferredSqlLoggerInitializer>();
builder.Services.AddHostedService<StartupSeedingHostedService>();

builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlServer(configuration.GetConnectionString("PantmigConnection")));

builder.Services.AddIdentity<ApplicationUser, IdentityRole>(options =>
{
    options.Password.RequireDigit = true;
    options.Password.RequiredLength = 8;
    options.Password.RequireLowercase = true;
    options.Password.RequireUppercase = true;
    options.Password.RequireNonAlphanumeric = true;
    options.Lockout.MaxFailedAccessAttempts = 5;
    options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
    options.User.RequireUniqueEmail = true;
})
.AddEntityFrameworkStores<ApplicationDbContext>()
.AddDefaultTokenProviders();

var secretKey = jwtSettings["SecretKey"];
var issuer = jwtSettings["Issuer"];
var audience = jwtSettings["Audience"];

builder.Services.AddAuthentication(options =>
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
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secretKey!)),
        ClockSkew = TimeSpan.Zero
    };
});

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("VerifiedDonator", policy =>
    {
        policy.RequireAuthenticatedUser();
        policy.RequireRole(nameof(UserType.Donator));
    });
});

builder.Services.AddMemoryCache();
builder.Services.AddHttpClient();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddOpenApi();

// CORS configuration
var allowedOrigins = configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? Array.Empty<string>();

    });
});

// CORS configuration: read allowed origins from configuration
var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? Array.Empty<string>();

bool IsOriginAllowed(string origin)
{
    if (!Uri.TryCreate(origin, UriKind.Absolute, out var o)) return false;
    foreach (var pattern in allowedOrigins)
    {
        if (string.IsNullOrWhiteSpace(pattern)) continue;
        if (pattern.Contains("*"))
        {
            if (pattern.StartsWith("https://*.", StringComparison.OrdinalIgnoreCase))
            {
                var domain = pattern.Substring("https://*.".Length);
                if (string.Equals(o.Scheme, "https", StringComparison.OrdinalIgnoreCase) &&
                    (string.Equals(o.Host, domain, StringComparison.OrdinalIgnoreCase) ||
                     o.Host.EndsWith("." + domain, StringComparison.OrdinalIgnoreCase)))
                {
                    return true;
                }
            }
            var portOk = p.IsDefaultPort || p.Port == -1 || p.Port == o.Port;
            if (schemeOk && hostOk && portOk) return true;
        }
    }
            {
                return true;
            }
        }
    }

    return false;
}

builder.Services.AddCors(options =>
{
    options.AddPolicy("ConfiguredCors", policy =>
    {
        policy.AllowAnyHeader().AllowAnyMethod().SetIsOriginAllowed(IsOriginAllowed);
    });
});

// Service registrations
builder.Services.AddScoped<ITokenService, TokenService>();
builder.Services.AddScoped<ICityResolver, CityResolver>();
builder.Services.AddScoped<IUsernameGenerator, UsernameGenerator>();
builder.Services.AddScoped<IAuthService, AuthServiceImpl>();
builder.Services.AddScoped<IEmailSender, SmtpEmailSender>();
builder.Services.AddScoped<IUserAccountService, UserAccountService>();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseCors("ConfiguredCors");

app.UseSwagger();
app.UseSwaggerUI();

app.UseHttpsRedirection();
app.UseAuthentication();
app.UseAuthorization();
app.MapAuthEndpoints();

// Reduce console verbosity after startup
consoleLevelSwitch.MinimumLevel = LogEventLevel.Information;

app.Run();