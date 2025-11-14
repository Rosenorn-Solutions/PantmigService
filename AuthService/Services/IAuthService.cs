using AuthService.Entities;
using AuthService.Models;
using Microsoft.AspNetCore.Identity;

namespace AuthService.Services
{
    public interface IAuthService
    {
        Task<(bool Success, string? Error, ApplicationUser? User)> RegisterAsync(RegisterRequest req, int? cityId, UserManager<ApplicationUser> userManager, RoleManager<IdentityRole> roleManager, ITokenService tokenService, IUsernameGenerator usernameGenerator, CancellationToken ct = default);

        Task<(bool Success, string? Error, ApplicationUser? User)> LoginAsync(LoginRequest req, SignInManager<ApplicationUser> signInManager, UserManager<ApplicationUser> userManager, CancellationToken ct = default);
        // New operations separated into a dedicated account service; keep interface lean.
    }
}
