namespace PantmigService.Entities;

public class RecycleListingApplicant
{
    public int Id { get; set; }
    public int ListingId { get; set; }
    public string RecyclerUserId { get; set; } = string.Empty;

    // The date/time when the recycler applied
    public DateTime AppliedAt { get; set; } = DateTime.UtcNow;

    public RecycleListing? Listing { get; set; }
}
