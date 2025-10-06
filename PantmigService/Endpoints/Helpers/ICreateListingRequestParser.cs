using PantmigService.Services;
using PantmigService.Entities;

namespace PantmigService.Endpoints.Helpers;

public interface ICreateListingRequestParser
{
    Task<ParseCreateListingResult> ParseAsync(HttpRequest request, CancellationToken ct);
}

public class ParseCreateListingResult
{
    public string? Title { get; init; }
    public string? Description { get; init; }
    public string? City { get; init; }
    public string? Location { get; init; }
    public DateTime AvailableFrom { get; init; }
    public DateTime AvailableTo { get; init; }
    public List<RecycleListingEndpoints.CreateRecycleListingItemRequest>? RawItems { get; init; }
    public List<RecycleListingImage> Images { get; init; } = new();
    public ValidationProblem? Problem { get; init; }
    public bool IsSuccess => Problem is null;
}
