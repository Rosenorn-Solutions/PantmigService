using System.Security.Claims;
using AuthService.Data;
using AuthService.Entities;
using AuthService.Models;
using AuthService.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace AuthService.Endpoints
{
    public static class AuthEndpoints
    {
        public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder app)
        {
            var group = app.MapGroup("/auth").WithTags("Auth");

            group.MapPost("/register", async (RegisterRequest req, UserManager<ApplicationUser> userManager, ITokenService tokenService) =>
            {
                if (string.IsNullOrWhiteSpace(req.Email) || string.IsNullOrWhiteSpace(req.Password))
                    return Results.BadRequest(new RegisterResult { Success = false, ErrorMessage = "Email and password are required" });

                var user = new ApplicationUser
                {
                    UserName = req.Email,
                    Email = req.Email,
                    FirstName = req.FirstName,
                    LastName = req.LastName,
                    PhoneNumber = req.Phone,
                    MitId = req.MitId,
                    IsMitIdVerified = !string.IsNullOrEmpty(req.MitId) // naive demo rule
                };

                var create = await userManager.CreateAsync(user, req.Password);
                if (!create.Succeeded)
                {
                    var errors = string.Join("; ", create.Errors.Select(e => e.Description));
                    return Results.BadRequest(new RegisterResult { Success = false, ErrorMessage = errors });
                }

                var (access, exp) = tokenService.GenerateAccessToken(user);
                var refresh = await tokenService.GenerateAndStoreRefreshTokenAsync(user);

                var resp = new AuthResponse
                {
                    UserId = user.Id,
                    Email = user.Email!,
                    FirstName = user.FirstName,
                    LastName = user.LastName,
                    AccessToken = access,
                    AccessTokenExpiration = exp,
                    RefreshToken = refresh
                };
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

            group.MapPost("/login", async (LoginRequest req, SignInManager<ApplicationUser> signInManager, UserManager<ApplicationUser> userManager, ITokenService tokenService) =>
            {
                var user = await userManager.Users.FirstOrDefaultAsync(u => u.Email == req.Email);
                if (user == null)
                    return Results.Unauthorized();

                var result = await signInManager.CheckPasswordSignInAsync(user, req.Password, lockoutOnFailure: true);
                if (!result.Succeeded)
                    return Results.Unauthorized();

                var (access, exp) = tokenService.GenerateAccessToken(user);
                var refresh = await tokenService.GenerateAndStoreRefreshTokenAsync(user);

                var resp = new AuthResponse
                {
                    UserId = user.Id,
                    Email = user.Email!,
                    FirstName = user.FirstName,
                    LastName = user.LastName,
                    AccessToken = access,
                    AccessTokenExpiration = exp,
                    RefreshToken = refresh
                };

                return Results.Ok(new LoginResult { Success = true, AuthResponse = resp });
            })
            .Accepts<LoginRequest>("application/json")
            .WithOpenApi(op =>
            {
                op.OperationId = "Auth_Login";
                op.Summary = "Login with email and password";
                op.Description = "Authenticates a user and returns access and refresh tokens.";
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

            group.MapGet("/me", (ClaimsPrincipal user) =>
            {
                if (!user.Identity?.IsAuthenticated ?? true) return Results.Unauthorized();
                var dto = new UserInformationDTO
                {
                    Id = user.FindFirstValue(ClaimTypes.NameIdentifier) ?? user.FindFirstValue("sub") ?? string.Empty,
                    Email = user.FindFirstValue(ClaimTypes.Email) ?? string.Empty,
                    UserName = user.Identity?.Name ?? string.Empty,
                    FirstName = user.FindFirstValue(ClaimTypes.GivenName) ?? string.Empty,
                    LastName = user.FindFirstValue(ClaimTypes.Surname) ?? string.Empty,
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

            return app;
        }
    }
}
