using AuthService.Data;
using AuthService.Entities;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using System.Text;
using Serilog;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using AuthService.Models;
using System.Security.Cryptography;
using AuthService.Endpoints;
using AuthService.Services;

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

// CORS: read allowed origins from configuration
var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? Array.Empty<string>();

bool IsOriginAllowed(string origin)
{
    if (!Uri.TryCreate(origin, UriKind.Absolute, out var o)) return false;

    foreach (var pattern in allowedOrigins)
    {
        if (string.IsNullOrWhiteSpace(pattern)) continue;

        // Wildcard subdomain support like https://*.findjob.nu
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
        // To support cookies, also call .AllowCredentials() and ensure origins are not "*"
    });
});

// Register services
builder.Services.AddScoped<ITokenService, TokenService>();

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

app.UseForwardedHeaders(new ForwardedHeadersOptions
{
    ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto | ForwardedHeaders.XForwardedHost
});

// Enable CORS before other middleware
app.UseCors("ConfiguredCors");

app.UseSwagger();
app.UseSwaggerUI();

app.UseHttpsRedirection();

app.UseAuthentication();
app.UseAuthorization();

// Map endpoints
app.MapAuthEndpoints();

app.Run();

// Helpers
static (string token, DateTime expires) GenerateAccessToken(ApplicationUser user, IConfiguration config)
{
    var jwtSection = config.GetSection("JwtSettings");
    var secretKey = jwtSection["SecretKey"] ?? throw new InvalidOperationException("Jwt SecretKey not configured");
    var issuer = jwtSection["Issuer"];
    var audience = jwtSection["Audience"];
    var minutesString = jwtSection["AccessTokenMinutes"];
    var expires = DateTime.UtcNow.AddMinutes(int.TryParse(minutesString, out var m) ? m : 60);

    var claims = new List<Claim>
    {
        new(JwtRegisteredClaimNames.Sub, user.Id),
        new(ClaimTypes.NameIdentifier, user.Id),
        new(JwtRegisteredClaimNames.Email, user.Email ?? string.Empty),
        new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
        new(ClaimTypes.GivenName, user.FirstName ?? string.Empty),
        new(ClaimTypes.Surname, user.LastName ?? string.Empty),
        new(ClaimTypes.MobilePhone, user.PhoneNumber ?? string.Empty),
        new("userType", user.UserType.ToString()),
        new("isMitIdVerified", user.IsMitIdVerified.ToString())
    };

    var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secretKey));
    var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

    var token = new JwtSecurityToken(
        issuer: issuer,
        audience: audience,
        claims: claims,
        expires: expires,
        signingCredentials: creds
    );

    var encoded = new JwtSecurityTokenHandler().WriteToken(token);
    return (encoded, expires);
}

static async Task<string> GenerateAndStoreRefreshTokenAsync(ApplicationUser user, ApplicationDbContext db, IConfiguration config)
{
    var tokenBytes = RandomNumberGenerator.GetBytes(64);
    var token = Convert.ToBase64String(tokenBytes);
    var daysCfg = config.GetSection("JwtSettings")["RefreshTokenDays"];
    var expires = DateTime.UtcNow.AddDays(int.TryParse(daysCfg, out var d) ? d : 30);

    var rt = new RefreshToken
    {
        UserId = user.Id,
        Token = token,
        Expires = expires,
        Created = DateTime.UtcNow
    };

    db.RefreshTokens.Add(rt);
    await db.SaveChangesAsync();
    return token;
}

static (ClaimsPrincipal? principal, string? error) GetPrincipalFromExpiredToken(string token, IConfiguration config)
{
    var jwtSection = config.GetSection("JwtSettings");
    var secretKey = jwtSection["SecretKey"] ?? string.Empty;
    var issuer = jwtSection["Issuer"];
    var audience = jwtSection["Audience"];

    var tokenValidationParameters = new TokenValidationParameters
    {
        ValidateAudience = true,
        ValidateIssuer = true,
        ValidateIssuerSigningKey = true,
        ValidateLifetime = false, // allow expired token
        ValidIssuer = issuer,
        ValidAudience = audience,
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secretKey))
    };

    var tokenHandler = new JwtSecurityTokenHandler();
    try
    {
        var principal = tokenHandler.ValidateToken(token, tokenValidationParameters, out var securityToken);
        if (securityToken is not JwtSecurityToken jwt || !jwt.Header.Alg.Equals(SecurityAlgorithms.HmacSha256, StringComparison.InvariantCultureIgnoreCase))
            return (null, "Invalid token algorithm");
        return (principal, null);
    }
    catch (Exception ex)
    {
        return (null, ex.Message);
    }
}