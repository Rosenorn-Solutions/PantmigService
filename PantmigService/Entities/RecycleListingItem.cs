namespace PantmigService.Entities;

// Enum representing the material/content type of a listing item
public enum RecycleMaterialType
{
    PlasticBottle = 1,
    GlassBottle = 2,
    Can = 3
}

public class RecycleListingItem
{
    public int Id { get; set; }
    public int ListingId { get; set; }
    public RecycleMaterialType MaterialType { get; set; }
    public int Quantity { get; set; }

    // Optional: deposit class or similar categorization (A/B/C) if needed later
    public string? DepositClass { get; set; }

    // Optional per-unit estimated deposit/value (can be aggregated to listing.EstimatedValue)
    public decimal? EstimatedDepositPerUnit { get; set; }

    public RecycleListing? Listing { get; set; }
}
