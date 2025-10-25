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
using Microsoft.OpenApi.Models;
using Serilog;
using System.Text;

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

builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("PantmigConnection")));

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
    // Use default AllowedUserNameCharacters; do not include locale-specific letters
})
.AddEntityFrameworkStores<ApplicationDbContext>()
.AddDefaultTokenProviders();

var jwtSettings = builder.Configuration.GetSection("JwtSettings");
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
    // Optional: policy that mirrors the test policy using role claim
    options.AddPolicy("VerifiedDonator", policy =>
    {
        policy.RequireAuthenticatedUser();
        policy.RequireRole(nameof(UserType.Donator));
        //policy.RequireAssertion(ctx =>
        //{
        //    var verified = ctx.User.FindFirst("isMitIdVerified")?.Value;
        //    return string.Equals(verified, bool.TrueString, StringComparison.OrdinalIgnoreCase);
        //});
    });
});

// Memory cache for ratings and small profile data
builder.Services.AddMemoryCache();

builder.Services.AddHttpClient();
builder.Services.AddEndpointsApiExplorer();

builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo { Title = "AuthService API", Version = "v1" });

    // Define the security scheme for JWT Bearer tokens
    c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Description = @"JWT Authorization header using the Bearer scheme.
                      Enter 'Bearer' [space] and then your token in the text input below.
                      Example: 'Bearer 12345abcdef'",
        Name = "Authorization",
        In = ParameterLocation.Header,
        Type = SecuritySchemeType.ApiKey,
        Scheme = "Bearer"
    });

    // Add security requirement to operations that use the "Bearer" scheme
    c.AddSecurityRequirement(new OpenApiSecurityRequirement()
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
                In = ParameterLocation.Header,
            },
            new List<string>()
        }
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

        // Wildcard subdomain support like https://*.pantmig.dk
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
            continue;
        }

        if (Uri.TryCreate(pattern, UriKind.Absolute, out var p))
        {
            var schemeOk = string.Equals(o.Scheme, p.Scheme, StringComparison.OrdinalIgnoreCase);
            var hostOk = string.Equals(o.Host, p.Host, StringComparison.OrdinalIgnoreCase);
            var portOk = p.IsDefaultPort || p.Port == -1 || p.Port == o.Port; // allow any port if not specified
            if (schemeOk && hostOk && portOk)
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
        policy
            .AllowAnyHeader()
            .AllowAnyMethod()
            .SetIsOriginAllowed(IsOriginAllowed);
    });
});

// Service registrations
builder.Services.AddScoped<ITokenService, TokenService>();
builder.Services.AddScoped<ICityResolver, CityResolver>();
builder.Services.AddScoped<IUsernameGenerator, UsernameGenerator>();
builder.Services.AddScoped<IAuthService, AuthServiceImpl>();
builder.Services.AddScoped<IEmailSender, SmtpEmailSender>();

var app = builder.Build();

// Seed Identity roles on startup
using (var scope = app.Services.CreateScope())
{
    var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
    foreach (var roleName in new[] { nameof(UserType.Donator), nameof(UserType.Recycler) })
    {
        if (!await roleManager.RoleExistsAsync(roleName))
        {
            await roleManager.CreateAsync(new IdentityRole(roleName));
        }
    }
}

// Optional CSV seed of postal codes at startup
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    // Use ContentRootPath so we can read the CSV from the project directory without copying to bin
    var csvPath = Path.Combine(app.Environment.ContentRootPath, "postal_codes_da.csv");
    try
    {
        if (!File.Exists(csvPath))
        {
            Log.Information("Postal code CSV not found at {CsvPath}. Skipping postal seed.", csvPath);
        }
        else
        {
            await PostalCodeCsvSeeder.SeedAsync(db, csvPath);
            Log.Information("PostalCodeCsvSeeder executed for {CsvPath}", csvPath);
        }
    }
    catch (Exception ex)
    {
        Log.Warning(ex, "PostalCodeCsvSeeder failed");
    }
}

app.UseForwardedHeaders(new ForwardedHeadersOptions
{
    ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto | ForwardedHeaders.XForwardedHost
});

app.UseCors("ConfiguredCors");

app.UseSwagger();
app.UseSwaggerUI();

app.UseHttpsRedirection();

app.UseAuthentication();
app.UseAuthorization();

app.MapAuthEndpoints();

app.Run();