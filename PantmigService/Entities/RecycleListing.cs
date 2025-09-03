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
        public string? EstimatedValue { get; set; }
        public decimal EstimatedAmount { get; set; } // numeric for queries
        public DateTime AvailableFrom { get; set; }
        public DateTime AvailableTo { get; set; }
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
        public string? ReceiptImageUrl { get; set; }
        public decimal? ReportedAmount { get; set; }
        public decimal? VerifiedAmount { get; set; }
        public DateTime? CompletedAt { get; set; }

        // Meeting point selected by donator once chat is started
        public decimal? MeetingLatitude { get; set; }
        public decimal? MeetingLongitude { get; set; }
        public DateTime? MeetingSetAt { get; set; }
    }
}
