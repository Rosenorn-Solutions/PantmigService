using AuthService.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;

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
        // True when account represents a non-person entity (company or NGO)
        public bool IsOrganization { get; set; } = false;

        // New demographic fields
        public Gender Gender { get; set; } = Gender.Unknown;
        // Stored as SQL 'date'; optional at registration
        public DateOnly? BirthDate { get; set; }

        // Optional link to the city (Bopæls by)
        public int? CityId { get; set; }
        public City? City { get; set; }

        // Aggregated rating for the user, 0.00 - 5.00
        [Range(0, 5)]
        [Precision(3, 2)]
        public decimal Rating { get; set; } = 0m;

        // Indicates a self-disabled account (soft delete / deactivation)
        public bool IsDisabled { get; set; } = false;
    }
}