using AuthService.Models;
using Microsoft.AspNetCore.Identity;

namespace AuthService.Entities
{
    public class ApplicationUser : IdentityUser
    {
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        // Navigation property for RefreshTokens
        public ICollection<RefreshToken> RefreshTokens { get; set; } = new List<RefreshToken>();
        public string FirstName { get; set; } = string.Empty;
        public string LastName { get; set; } = string.Empty;
        public bool IsMitIdVerified { get; set; } = false;
        public string? MitId { get; set; } = string.Empty;
        public UserType UserType { get; set; } = UserType.Recycler;
    }
}