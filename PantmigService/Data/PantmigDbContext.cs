using Microsoft.EntityFrameworkCore;
using PantmigService.Entities;

namespace PantmigService.Data
{
    public class PantmigDbContext : DbContext
    {
        public PantmigDbContext(DbContextOptions<PantmigDbContext> options) : base(options)
        {
        }

        public DbSet<RecycleListing> RecycleListings => Set<RecycleListing>();
        public DbSet<ChatMessage> ChatMessages => Set<ChatMessage>();
        public DbSet<City> Cities => Set<City>();
        public DbSet<CityPostalCode> CityPostalCodes => Set<CityPostalCode>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            modelBuilder.Entity<City>()
                .HasIndex(c => c.Slug)
                .IsUnique();

            modelBuilder.Entity<CityPostalCode>()
                .HasIndex(cp => new { cp.CityId, cp.PostalCode })
                .IsUnique();

            modelBuilder.Entity<CityPostalCode>()
                .HasOne(cp => cp.City)
                .WithMany()
                .HasForeignKey(cp => cp.CityId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<RecycleListing>()
                .HasIndex(x => x.CreatedByUserId);

            modelBuilder.Entity<RecycleListing>()
                .Property(x => x.EstimatedAmount)
                .HasPrecision(18, 2);

            // Explicit decimal precision to avoid truncation warnings
            modelBuilder.Entity<RecycleListing>()
                .Property(x => x.ReportedAmount)
                .HasPrecision(18, 2);

            modelBuilder.Entity<RecycleListing>()
                .Property(x => x.VerifiedAmount)
                .HasPrecision(18, 2);

            // Meeting point precision (latitude/longitude)
            modelBuilder.Entity<RecycleListing>()
                .Property(x => x.MeetingLatitude)
                .HasPrecision(9, 6);

            modelBuilder.Entity<RecycleListing>()
                .Property(x => x.MeetingLongitude)
                .HasPrecision(9, 6);

            // Helpful composite indexes for searches
            modelBuilder.Entity<RecycleListing>()
                .HasIndex(x => new { x.IsActive, x.Status, x.CityId, x.AvailableFrom });

            modelBuilder.Entity<RecycleListing>()
                .HasIndex(x => new { x.CityId, x.AvailableFrom, x.AvailableTo });

            modelBuilder.Entity<RecycleListing>()
                .HasIndex(x => x.EstimatedAmount);

            modelBuilder.Entity<ChatMessage>()
                .HasIndex(x => new { x.ListingId, x.SentAt });
        }
    }
}
