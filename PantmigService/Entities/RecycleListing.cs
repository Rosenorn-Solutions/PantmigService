using System.ComponentModel.DataAnnotations.Schema;

namespace PantmigService.Entities
{
    public enum ListingStatus
    {
        Created = 0,
        PendingAcceptance = 1,
        Accepted = 2,
        PickedUp = 3,
        AwaitingVerification = 4,
        Completed = 5,
        Cancelled = 6
    }

    public class RecycleListing
    {
        public int Id { get; set; }
        public string Title { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string Location { get; set; } = string.Empty;
        public decimal? EstimatedValue { get; set; }
        // Availability date range (date-only now)
        public DateOnly AvailableFrom { get; set; }
        public DateOnly AvailableTo { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public string CreatedByUserId { get; set; } = string.Empty; // Foreign key to ApplicationUser
        public bool IsActive { get; set; } = true;

        // Normalized city reference
        public int CityId { get; set; }
        public City? City { get; set; }

        // Workflow fields
        public ListingStatus Status { get; set; } = ListingStatus.Created;
        public string? AssignedRecyclerUserId { get; set; }
        public DateTime? AcceptedAt { get; set; }
        public DateTime? PickupConfirmedAt { get; set; }
        public string? ChatSessionId { get; set; }

        // Receipt storage: either an external URL or stored bytes
        public string? ReceiptImageUrl { get; set; }
        public byte[]? ReceiptImageBytes { get; set; }
        public string? ReceiptImageContentType { get; set; }
        public string? ReceiptImageFileName { get; set; }

        public decimal? ReportedAmount { get; set; }
        public decimal? VerifiedAmount { get; set; }
        public DateTime? CompletedAt { get; set; }

        // Meeting point selected by donator once chat is started
        public decimal? MeetingLatitude { get; set; }
        public decimal? MeetingLongitude { get; set; }
        public DateTime? MeetingSetAt { get; set; }

        // Applicants for this listing
        public ICollection<RecycleListingApplicant> Applicants { get; set; } = [];

        // Structured content items (plastic bottles, glass bottles, cans, etc.)
        public ICollection<RecycleListingItem> Items { get; set; } = [];

        // Listing images (uploaded at creation time). Stored inline for now.
        public ICollection<RecycleListingImage> Images { get; set; } = [];

        // Approximate worth based on average deposit per item (2.33)
        [NotMapped]
        public decimal ApproximateWorth
        {
            get
            {
                if (Items is null || Items.Count == 0) return 0m;
                var totalUnits = Items.Sum(i => i.Quantity);
                const decimal AVERAGE_DEPOSIT = 2.33m; // Approx between 1.5 and 3
                return Math.Round(totalUnits * AVERAGE_DEPOSIT, 2, MidpointRounding.AwayFromZero);
            }
        }

        // Convenience property
        [NotMapped]
        public List<string> AppliedForRecyclementUserIdList => [.. Applicants.Select(a => a.RecyclerUserId)];
    }
}
