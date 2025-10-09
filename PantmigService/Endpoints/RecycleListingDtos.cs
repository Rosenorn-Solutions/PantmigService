using PantmigService.Entities;

namespace PantmigService.Endpoints;

public record RecycleListingItemResponse(int Id, RecycleMaterialType MaterialType, int Quantity, string? DepositClass, decimal? EstimatedDepositPerUnit);
public record RecycleListingImageResponse(int Id, string FileName, string ContentType, int Order);

public record RecycleListingResponse
{
    public int Id { get; init; }
    public string Title { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public decimal? EstimatedValue { get; init; }
    public DateOnly AvailableFrom { get; init; }
    public DateOnly AvailableTo { get; init; }
    public TimeOnly? PickupTimeFrom { get; init; }
    public TimeOnly? PickupTimeTo { get; init; }
    public string CreatedByUserId { get; init; } = string.Empty;
    public DateTime CreatedAt { get; init; }
    public bool IsActive { get; init; }
    public ListingStatus Status { get; init; }
    public int CityId { get; init; }
    public string? AssignedRecyclerUserId { get; init; }
    public string? ChatSessionId { get; init; }
    public decimal? ReportedAmount { get; init; }
    public decimal? VerifiedAmount { get; init; }
    public byte[]? ReceiptImageBytes { get; init; }
    public string? ReceiptImageUrl { get; init; }
    public List<RecycleListingItemResponse> Items { get; init; } = [];
    public List<RecycleListingImageResponse> Images { get; init; } = [];
}

public static class RecycleListingMapper
{
    public static RecycleListingResponse ToResponse(this RecycleListing l)
        => new()
        {
            Id = l.Id,
            Title = l.Title,
            Description = l.Description,
            EstimatedValue = l.EstimatedValue,
            AvailableFrom = l.AvailableFrom,
            AvailableTo = l.AvailableTo,
            PickupTimeFrom = l.PickupTimeFrom,
            PickupTimeTo = l.PickupTimeTo,
            CreatedByUserId = l.CreatedByUserId,
            CreatedAt = l.CreatedAt,
            IsActive = l.IsActive,
            Status = l.Status,
            CityId = l.CityId,
            AssignedRecyclerUserId = l.AssignedRecyclerUserId,
            ChatSessionId = l.ChatSessionId,
            ReportedAmount = l.ReportedAmount,
            VerifiedAmount = l.VerifiedAmount,
            ReceiptImageBytes = l.ReceiptImageBytes,
            ReceiptImageUrl = l.ReceiptImageUrl,
            Items = [.. l.Items.Select(i => new RecycleListingItemResponse(i.Id, i.MaterialType, i.Quantity, i.DepositClass, i.EstimatedDepositPerUnit))],
            Images = [.. l.Images.Select(img => new RecycleListingImageResponse(img.Id, img.FileName, img.ContentType, img.Order))]
        };

    public static IEnumerable<RecycleListingResponse> ToResponse(this IEnumerable<RecycleListing> listings) => listings.Select(ToResponse);
}