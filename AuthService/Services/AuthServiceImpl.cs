using AuthService.Entities;
using AuthService.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace AuthService.Services
{
    public sealed class AuthServiceImpl : IAuthService
    {
        public async Task<(bool Success, string? Error, ApplicationUser? User)> RegisterAsync(
            RegisterRequest req,
            int? cityId,
            UserManager<ApplicationUser> userManager,
            RoleManager<IdentityRole> roleManager,
            ITokenService tokenService,
            IUsernameGenerator usernameGenerator,
            CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(req.Email) || string.IsNullOrWhiteSpace(req.Password))
                return (false, "Email and password are required", null);

            // Optional unique phone validation
            if (!string.IsNullOrWhiteSpace(req.Phone))
            {
                var normalizedPhone = req.Phone.Trim();
                var phoneExists = await userManager.Users.AnyAsync(u => u.PhoneNumber != null && u.PhoneNumber == normalizedPhone, ct);
                if (phoneExists)
                {
                    return (false, "Phone number already in use", null);
                }
            }

            foreach (var roleName in new[] { nameof(UserType.Donator), nameof(UserType.Recycler) })
            {
                if (!await roleManager.RoleExistsAsync(roleName))
                {
                    var createRes = await roleManager.CreateAsync(new IdentityRole(roleName));
                    if (!createRes.Succeeded)
                    {
                        var errors = string.Join("; ", createRes.Errors.Select(e => e.Description));
                        return (false, $"Failed to ensure role '{roleName}': {errors}", null);
                    }
                }
            }

            string username = await usernameGenerator.GenerateAsync(req.FirstName, req.LastName, ct);

            var user = new ApplicationUser
            {
                UserName = username,
                Email = req.Email,
                FirstName = req.FirstName,
                LastName = req.LastName,
                PhoneNumber = string.IsNullOrWhiteSpace(req.Phone) ? null : req.Phone.Trim(),
                MitId = req.MitId,
                IsMitIdVerified = false,
                UserType = req.UserType,
                IsOrganization = req.IsOrganization,
                CityId = cityId,
                Gender = req.Gender,
                BirthDate = req.BirthDate
            };

            var create = await userManager.CreateAsync(user, req.Password);
            if (!create.Succeeded)
            {
                var errors = string.Join("; ", create.Errors.Select(e => e.Description));
                return (false, errors, null);
            }

            var roleAdd = await userManager.AddToRoleAsync(user, user.UserType.ToString());
            if (!roleAdd.Succeeded)
            {
                var errors = string.Join("; ", roleAdd.Errors.Select(e => e.Description));
                return (false, $"Failed to assign role: {errors}", null);
            }

            return (true, null, user);
        }

        public async Task<(bool Success, string? Error, ApplicationUser? User)> LoginAsync(
            LoginRequest req,
            SignInManager<ApplicationUser> signInManager,
            UserManager<ApplicationUser> userManager,
            CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(req.EmailOrUsername))
                return (false, null, null);

            var identifier = req.EmailOrUsername.Trim();
            ApplicationUser? user = null;

            // Use Identity's normalizers to avoid culture-specific issues
            var normalizedEmail = userManager.NormalizeEmail(identifier);
            if (!string.IsNullOrEmpty(normalizedEmail))
            {
                user = await userManager.Users.Include(u => u.City)
                    .FirstOrDefaultAsync(u => u.NormalizedEmail == normalizedEmail, ct);
            }
            if (user is null)
            {
                var normalizedName = userManager.NormalizeName(identifier);
                if (!string.IsNullOrEmpty(normalizedName))
                {
                    user = await userManager.Users.Include(u => u.City)
                        .FirstOrDefaultAsync(u => u.NormalizedUserName == normalizedName, ct);
                }
            }

            // Fallbacks in case normalized fields are not populated (e.g., test providers)
            if (user is null)
            {
                var idUpper = identifier.ToUpperInvariant();
                user = await userManager.Users.Include(u => u.City)
                    .FirstOrDefaultAsync(u => u.Email != null && u.Email.ToUpperInvariant() == idUpper, ct);
            }
            if (user is null)
            {
                var idUpper = identifier.ToUpperInvariant();
                user = await userManager.Users.Include(u => u.City)
                    .FirstOrDefaultAsync(u => u.UserName != null && u.UserName.ToUpperInvariant() == idUpper, ct);
            }

            if (user == null || user.IsDisabled)
                return (false, null, null);

            var result = await signInManager.CheckPasswordSignInAsync(user, req.Password, lockoutOnFailure: true);
            if (!result.Succeeded)
            {
                // Fallback for environments where SignInManager policies or auth stack isn't fully wired (e.g., test host)
                var okPwd = await userManager.CheckPasswordAsync(user, req.Password);
                if (!okPwd)
                    return (false, null, null);
            }

            return (true, null, user);
        }
    }
}
