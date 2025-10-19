using System.Security.Claims;
using AuthService.Data;
using AuthService.Entities;
using AuthService.Models;
using AuthService.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using System.Text;
using System.Globalization;
using System.Security.Cryptography;

namespace AuthService.Endpoints
{
    public static class AuthEndpoints
    {
        private static TimeSpan GetRatingTtl(IConfiguration config)
        {
            var seconds = config.GetSection("Cache")["UserRatingSeconds"];
            return TimeSpan.FromSeconds(int.TryParse(seconds, out var s) ? Math.Max(s, 1) : 300);
        }

        public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder app)
        {
            var group = app.MapGroup("/auth").WithTags("Auth");

            group.MapGet("/check-email", async (string email, UserManager<ApplicationUser> userManager, CancellationToken ct) =>
            {
                if (string.IsNullOrWhiteSpace(email))
                    return Results.BadRequest(new { error = "Email is required" });

                var normalized = userManager.NormalizeEmail(email.Trim());
                var taken = await userManager.Users.AnyAsync(u => u.NormalizedEmail == normalized, ct);
                return Results.Ok(new AvailabilityResult { Taken = taken });
            })
            .WithOpenApi(op =>
            {
                op.OperationId = "Auth_CheckEmail";
                op.Summary = "Check if email is already taken";
                op.Description = "Returns whether a user with the provided email already exists.";
                return op;
            })
            .Produces<AvailabilityResult>(StatusCodes.Status200OK, contentType: "application/json")
            .Produces(StatusCodes.Status400BadRequest);

            group.MapGet("/check-phone", async (string phone, UserManager<ApplicationUser> userManager, CancellationToken ct) =>
            {
                if (string.IsNullOrWhiteSpace(phone))
                    return Results.BadRequest(new { error = "Phone is required" });

                var normalized = phone.Trim();
                var taken = await userManager.Users.AnyAsync(u => u.PhoneNumber != null && u.PhoneNumber == normalized, ct);
                return Results.Ok(new AvailabilityResult { Taken = taken });
            })
            .WithOpenApi(op =>
            {
                op.OperationId = "Auth_CheckPhone";
                op.Summary = "Check if phone number is already taken";
                op.Description = "Returns whether a user with the provided phone number already exists.";
                return op;
            })
            .Produces<AvailabilityResult>(StatusCodes.Status200OK, contentType: "application/json")
            .Produces(StatusCodes.Status400BadRequest);

            group.MapPost("/register", async (RegisterRequest req, UserManager<ApplicationUser> userManager, RoleManager<IdentityRole> roleManager, ITokenService tokenService, 
                ICityResolver cityResolver, 
                IConfiguration config, 
                IUsernameGenerator usernameGenerator, 
                IAuthService authService,
                IEmailSender emailSender,
                LinkGenerator links,
                HttpContext httpCtx) =>
            {
                if (string.IsNullOrWhiteSpace(req.Email) || string.IsNullOrWhiteSpace(req.Password))
                    return Results.BadRequest(new RegisterResult { Success = false, ErrorMessage = "Email and password are required" });

                int? cityId = null;
                string? cityName = null;
                if (!string.IsNullOrWhiteSpace(req.City))
                {
                    try
                    {
                        cityId = await cityResolver.ResolveOrCreateAsync(req.City);
                        cityName = req.City.Trim();
                    }
                    catch (Exception ex)
                    {
                        return Results.BadRequest(new RegisterResult { Success = false, ErrorMessage = $"Invalid city: {ex.Message}" });
                    }
                }

                var (ok, err, user) = await authService.RegisterAsync(req, cityId, userManager, roleManager, tokenService, usernameGenerator);
                if (!ok || user is null)
                {
                    return Results.BadRequest(new RegisterResult { Success = false, ErrorMessage = err ?? "Registration failed" });
                }

                if (cityId.HasValue)
                {
                    user = await userManager.Users.Include(u => u.City).FirstAsync(u => u.Id == user.Id);
                }

                var (access, exp) = tokenService.GenerateAccessToken(user);
                var refresh = await tokenService.GenerateAndStoreRefreshTokenAsync(user);

                var resp = new AuthResponse
                {
                    UserId = user.Id,
                    Email = user.Email!,
                    UserName = user.UserName!,
                    FirstName = user.FirstName,
                    LastName = user.LastName,
                    AccessToken = access,
                    AccessTokenExpiration = exp,
                    RefreshToken = refresh,
                    UserType = user.UserType,
                    IsOrganization = user.IsOrganization,
                    CityId = user.CityId,
                    CityName = cityName ?? user.City?.Name,
                    Gender = user.Gender,
                    BirthDate = user.BirthDate
                };

                try
                {
                    var token = await userManager.GenerateEmailConfirmationTokenAsync(user);
                    var tokenBytes = System.Text.Encoding.UTF8.GetBytes(token);
                    var tokenEnc = Microsoft.AspNetCore.WebUtilities.WebEncoders.Base64UrlEncode(tokenBytes);
                    var baseUrl = "https://auth.pantmig.dk";
                    var confirmUrl = $"{baseUrl.TrimEnd('/')}/auth/confirm-email?userId={Uri.EscapeDataString(user.Id)}&token={tokenEnc}";

                    var subject = "Bekræft din e-mail til PantMig";
                    var body = $"Hej {user.FirstName},\n\nTak for din registrering. Bekræft venligst din e-mail ved at klikke på linket:\n{confirmUrl}\n\nHvis du ikke har oprettet en konto, kan du ignorere denne mail.";
                    await emailSender.SendAsync(user.Email!, subject, body, httpCtx.RequestAborted);
                }
                catch
                {
                }
                return Results.Ok(new RegisterResult { Success = true, AuthResponse = resp });
            })
            .Accepts<RegisterRequest>("application/json")
            .WithOpenApi(op =>
            {
                op.OperationId = "Auth_Register";
                op.Summary = "Register a new user";
                op.Description = "Creates a new user account and returns access and refresh tokens.";
                return op;
            })
            .Produces<RegisterResult>(StatusCodes.Status200OK, contentType: "application/json")
            .Produces<RegisterResult>(StatusCodes.Status400BadRequest, contentType: "application/json");

            group.MapPost("/login", async (LoginRequest req, SignInManager<ApplicationUser> signInManager, UserManager<ApplicationUser> userManager, ITokenService tokenService,
                IAuthService authService) =>
            {
                var (ok, _, user) = await authService.LoginAsync(req, signInManager, userManager);
                if (!ok || user is null)
                    return Results.Unauthorized();

                var (access, exp) = tokenService.GenerateAccessToken(user);
                var refresh = await tokenService.GenerateAndStoreRefreshTokenAsync(user);

                var resp = new AuthResponse
                {
                    UserId = user.Id,
                    Email = user.Email!,
                    UserName = user.UserName!,
                    FirstName = user.FirstName,
                    LastName = user.LastName,
                    AccessToken = access,
                    AccessTokenExpiration = exp,
                    RefreshToken = refresh,
                    UserType = user.UserType,
                    IsOrganization = user.IsOrganization,
                    CityId = user.CityId,
                    CityName = user.City?.Name,
                    Gender = user.Gender,
                    BirthDate = user.BirthDate
                };

                return Results.Ok(new LoginResult { Success = true, AuthResponse = resp });
            })
            .Accepts<LoginRequest>("application/json")
            .WithOpenApi(op =>
            {
                op.OperationId = "Auth_Login";
                op.Summary = "Login with email/username and password";
                op.Description = "Authenticates a user with either email or username and returns access and refresh tokens.";
                return op;
            })
            .Produces<LoginResult>(StatusCodes.Status200OK, contentType: "application/json")
            .Produces(StatusCodes.Status401Unauthorized);

            group.MapPost("/refresh", async (TokenRefreshRequest req, ITokenService tokenService) =>
            {
                var (resp, error) = await tokenService.RotateRefreshTokenAsync(req.AccessToken, req.RefreshToken);
                return resp is not null ? Results.Ok(resp) : Results.BadRequest(new { error });
            })
            .Accepts<TokenRefreshRequest>("application/json")
            .WithOpenApi(op =>
            {
                op.OperationId = "Auth_Refresh";
                op.Summary = "Refresh access token";
                op.Description = "Rotates the refresh token and issues a new access token.";
                return op;
            })
            .Produces<AuthResponse>(StatusCodes.Status200OK, contentType: "application/json")
            .Produces(StatusCodes.Status400BadRequest);

            group.MapGet("/confirm-email", async (string userId, string token, UserManager<ApplicationUser> userManager) =>
            {
                if (string.IsNullOrWhiteSpace(userId) || string.IsNullOrWhiteSpace(token))
                    return Results.BadRequest("Missing userId or token");

                var user = await userManager.FindByIdAsync(userId);
                if (user is null)
                    return Results.BadRequest("Invalid user");

                try
                {
                    var tokenBytes = Microsoft.AspNetCore.WebUtilities.WebEncoders.Base64UrlDecode(token);
                    var decodedToken = System.Text.Encoding.UTF8.GetString(tokenBytes);
                    var result = await userManager.ConfirmEmailAsync(user, decodedToken);
                    if (result.Succeeded)
                    {
                        return Results.Text("Email confirmed. You can close this window.", "text/plain");
                    }

                    var errors = string.Join("; ", result.Errors.Select(e => e.Description));
                    return Results.BadRequest($"Failed to confirm email: {errors}");
                }
                catch (Exception ex)
                {
                    return Results.BadRequest($"Invalid token format: {ex.Message}");
                }
            })
            .WithOpenApi(op =>
            {
                op.OperationId = "Auth_ConfirmEmail";
                op.Summary = "Confirm user email";
                op.Description = "Verifies the email confirmation token and marks the user's email as confirmed.";
                return op;
            })
            .Produces(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest);

            group.MapGet("/me", async (ClaimsPrincipal user, ApplicationDbContext db, IMemoryCache cache, IConfiguration config) =>
            {
                if (!user.Identity?.IsAuthenticated ?? true) return Results.Unauthorized();

                int? cityId = null;
                string? cityName = null;

                var cityIdClaim = user.FindFirst("cityId")?.Value;
                if (int.TryParse(cityIdClaim, out var cid)) cityId = cid;
                cityName = user.FindFirst("cityName")?.Value;

                var userId = user.FindFirstValue(ClaimTypes.NameIdentifier) ?? user.FindFirstValue("sub") ?? string.Empty;

                decimal rating = 0m;
                if (!string.IsNullOrEmpty(userId))
                {
                    var key = $"rating:{userId}";
                    if (!cache.TryGetValue(key, out rating))
                    {
                        rating = await db.Users.Where(u => u.Id == userId).Select(u => u.Rating).FirstOrDefaultAsync();
                        cache.Set(key, rating, GetRatingTtl(config));
                    }
                }

                if (cityId.HasValue && string.IsNullOrWhiteSpace(cityName))
                {
                    cityName = await db.Cities.Where(c => c.Id == cityId.Value)
                                              .Select(c => c.Name)
                                              .FirstOrDefaultAsync();
                }

                var genderClaim = user.FindFirst("gender")?.Value;
                var isOrgClaim = user.FindFirst("isOrganization")?.Value;
                var birthDateClaim = user.FindFirst("birthDate")?.Value;
                Gender gender = Enum.TryParse<Gender>(genderClaim, out var g) ? g : Gender.Unknown;
                DateOnly? birthDate = null;
                if (DateOnly.TryParse(birthDateClaim, out var bd)) birthDate = bd;
                bool isOrg = bool.TryParse(isOrgClaim, out var io) && io;

                bool emailConfirmed = false;
                if (!string.IsNullOrEmpty(userId))
                {
                    emailConfirmed = await db.Users.Where(u => u.Id == userId).Select(u => u.EmailConfirmed).FirstOrDefaultAsync();
                }

                var dto = new UserInformationDTO
                {
                    Id = userId,
                    Email = user.FindFirstValue(ClaimTypes.Email) ?? string.Empty,
                    UserName = user.Identity?.Name ?? string.Empty,
                    FirstName = user.FindFirstValue(ClaimTypes.GivenName) ?? string.Empty,
                    LastName = user.FindFirstValue(ClaimTypes.Surname) ?? string.Empty,
                    CityId = cityId,
                    CityName = cityName,
                    Rating = rating,
                    Gender = gender,
                    IsOrganization = isOrg,
                    BirthDate = birthDate,
                    IsEmailConfirmed = emailConfirmed
                };
                return Results.Ok(dto);
            })
            .RequireAuthorization()
            .WithOpenApi(op =>
            {
                op.OperationId = "Auth_Me";
                op.Summary = "Get current user info";
                op.Description = "Returns basic profile information for the authenticated user.";
                return op;
            })
            .Produces<UserInformationDTO>(StatusCodes.Status200OK, contentType: "application/json")
            .Produces(StatusCodes.Status401Unauthorized);

            group.MapGet("/users/{id}", async (string id, ApplicationDbContext db, IMemoryCache cache, IConfiguration config) =>
            {
                var user = await db.Users.Include(u => u.City).AsNoTracking().FirstOrDefaultAsync(u => u.Id == id);
                if (user is null) return Results.NotFound();

                var key = $"rating:{user.Id}";
                cache.Set(key, user.Rating, GetRatingTtl(config));

                var dto = new UserInformationDTO
                {
                    Id = user.Id,
                    Email = user.Email ?? string.Empty,
                    UserName = user.UserName ?? string.Empty,
                    FirstName = user.FirstName,
                    LastName = user.LastName,
                    Phone = user.PhoneNumber ?? string.Empty,
                    CreatedAt = user.CreatedAt,
                    IsOrganization = user.IsOrganization,
                    CityId = user.CityId,
                    CityName = user.City?.Name,
                    Rating = user.Rating,
                    Gender = user.Gender,
                    BirthDate = user.BirthDate
                };
                return Results.Ok(new UserInformationResult { Success = true, UserInformation = dto });
            })
            .WithOpenApi(op =>
            {
                op.OperationId = "Auth_GetUserById";
                op.Summary = "Get user info by id";
                op.Description = "Returns public profile information for a user, including rating.";
                return op;
            })
            .Produces<UserInformationResult>(StatusCodes.Status200OK, contentType: "application/json")
            .Produces(StatusCodes.Status404NotFound);

            group.MapPost("/users/lookup", async (UsersLookupRequest req, ApplicationDbContext db, IMemoryCache cache, IConfiguration config) =>
            {
                if (req?.Ids == null || req.Ids.Count == 0)
                {
                    return Results.Ok(new UsersLookupResult { Success = true, Users = new List<UserRatingDTO>() });
                }

                var ids = req.Ids.Where(id => !string.IsNullOrWhiteSpace(id)).Distinct().ToList();
                if (ids.Count == 0)
                {
                    return Results.Ok(new UsersLookupResult { Success = true, Users = new List<UserRatingDTO>() });
                }

                var nameMap = await db.Users.AsNoTracking()
                    .Where(u => ids.Contains(u.Id))
                    .Select(u => new { u.Id, u.UserName })
                    .ToDictionaryAsync(x => x.Id, x => x.UserName ?? string.Empty);

                var users = new List<UserRatingDTO>(ids.Count);
                var missing = new List<string>();
                foreach (var id in ids)
                {
                    var cacheKey = $"rating:{id}";
                    if (cache.TryGetValue(cacheKey, out decimal r))
                    {
                        var name = nameMap.TryGetValue(id, out var n) ? n : string.Empty;
                        users.Add(new UserRatingDTO { Id = id, UserName = name, Rating = r });
                    }
                    else
                    {
                        missing.Add(id);
                    }
                }

                if (missing.Count > 0)
                {
                    var fromDb = await db.Users.AsNoTracking()
                        .Where(u => missing.Contains(u.Id))
                        .Select(u => new { u.Id, u.Rating })
                        .ToListAsync();

                    var ttl = GetRatingTtl(config);
                    foreach (var u in fromDb)
                    {
                        cache.Set($"rating:{u.Id}", u.Rating, ttl);
                        var name = nameMap.TryGetValue(u.Id, out var n) ? n : string.Empty;
                        users.Add(new UserRatingDTO { Id = u.Id, UserName = name, Rating = u.Rating });
                    }
                }

                return Results.Ok(new UsersLookupResult { Success = true, Users = users });
            })
            .Accepts<UsersLookupRequest>("application/json")
            .WithOpenApi(op =>
            {
                op.OperationId = "Auth_UsersLookup";
                op.Summary = "Batch lookup users' ratings";
                op.Description = "Returns ratings for a list of user ids to reduce round-trips. Uses in-memory cache for faster responses.";
                return op;
            })
            .Produces<UsersLookupResult>(StatusCodes.Status200OK, contentType: "application/json");

            return app;
        }
    }
}
