namespace PantmigService.Entities;

public class RecycleListingImage
{
    public int Id { get; set; }
    public int ListingId { get; set; }
    public RecycleListing? Listing { get; set; }
    public byte[] Data { get; set; } = [];
    public string ContentType { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public int Order { get; set; }
}
