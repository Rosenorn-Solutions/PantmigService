using AuthService.Entities;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace AuthService.Data
{
    public class ApplicationDbContext : IdentityDbContext<ApplicationUser>
    {
        public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
            : base(options)
        {
        }

        // Add DbSet for RefreshToken entity
        public DbSet<RefreshToken> RefreshTokens { get; set; }

        // Add DbSet for City entity
        public DbSet<City> Cities { get; set; }

        public DbSet<CityPostalCode> CityPostalCodes { get; set; }

        protected override void OnModelCreating(ModelBuilder builder)
        {
            base.OnModelCreating(builder);

            // Configure RefreshToken relationships
            builder.Entity<RefreshToken>()
                .HasOne(rt => rt.User)
                .WithMany(u => u.RefreshTokens)
                .HasForeignKey(rt => rt.UserId)
                .IsRequired()
                .OnDelete(DeleteBehavior.Cascade); // Cascade delete if user is removed

            // Ensure the UserId in RefreshToken is indexed for faster lookups
            builder.Entity<RefreshToken>()
                .HasIndex(rt => rt.UserId);

            // Ensure the Token string in RefreshToken is unique
            builder.Entity<RefreshToken>()
                .HasIndex(rt => rt.Token)
                .IsUnique();

            // City unique slug
            builder.Entity<City>()
                .HasIndex(c => c.Slug)
                .IsUnique();

            builder.Entity<City>()
                .HasIndex(c => c.ExternalId)
                .IsUnique();

            // CityPostalCode: unique CityId+PostalCode and FK
            builder.Entity<CityPostalCode>()
                .HasIndex(cp => new { cp.CityId, cp.PostalCode })
                .IsUnique();

            builder.Entity<CityPostalCode>()
                .HasOne(cp => cp.City)
                .WithMany()
                .HasForeignKey(cp => cp.CityId)
                .OnDelete(DeleteBehavior.Cascade);

            // Optional FK from user to city
            builder.Entity<ApplicationUser>()
                .HasOne(u => u.City)
                .WithMany()
                .HasForeignKey(u => u.CityId)
                .OnDelete(DeleteBehavior.SetNull);

            // Unique phone number (ignore nulls)
            builder.Entity<ApplicationUser>()
                .HasIndex(u => u.PhoneNumber)
                .IsUnique()
                .HasFilter("[PhoneNumber] IS NOT NULL");
        }
    }
}