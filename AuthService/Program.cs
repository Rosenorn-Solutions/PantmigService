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

builder.Services.AddAuthorizationBuilder()
    .AddPolicy("VerifiedDonator", policy =>
    {
        policy.RequireAuthenticatedUser();
        policy.RequireRole(nameof(UserType.Donator));
    });

builder.Services.AddMemoryCache();
builder.Services.AddHttpClient();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddOpenApi();

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

app.UseCors("FrontendCors");

app.UseHttpsRedirection();
app.UseAuthentication();
app.UseAuthorization();
app.MapAuthEndpoints();

// Reduce console verbosity after startup
consoleLevelSwitch.MinimumLevel = LogEventLevel.Information;

app.Run();