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
                PhoneNumber = req.Phone,
                MitId = req.MitId,
                IsMitIdVerified = false,
                UserType = req.UserType,
                CityId = cityId
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

            user = await userManager.Users.Include(u => u.City).FirstOrDefaultAsync(u => u.NormalizedEmail == identifier.ToUpper());
            if (user is null)
            {
                user = await userManager.Users.Include(u => u.City).FirstOrDefaultAsync(u => u.NormalizedUserName == identifier.ToUpper());
            }

            if (user == null)
                return (false, null, null);

            var result = await signInManager.CheckPasswordSignInAsync(user, req.Password, lockoutOnFailure: true);
            if (!result.Succeeded)
                return (false, null, null);

            return (true, null, user);
        }
    }
}
