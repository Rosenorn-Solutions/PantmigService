using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using AuthService.Entities;

namespace AuthService.Extensions
{
    public static class UserManagerExtensions
    {
        public static Task<ApplicationUser?> FindByPhoneNumberAsync(this UserManager<ApplicationUser> userManager, string phoneNumber, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(phoneNumber)) return Task.FromResult<ApplicationUser?>(null);
            var normalized = phoneNumber.Trim();
            return userManager.Users.FirstOrDefaultAsync(u => u.PhoneNumber != null && u.PhoneNumber == normalized, ct);
        }
    }
}
