using PantmigService.Entities;
using PantmigService.Services;

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
    public Guid? CityExternalId { get; init; } // new: provided external id when JSON body contains it
    public string? Location { get; init; }
    public DateOnly AvailableFrom { get; init; }
    public DateOnly AvailableTo { get; init; }
    public decimal? Latitude { get; init; }
    public decimal? Longitude { get; init; }
    public List<RecycleListingEndpoints.CreateRecycleListingItemRequest>? RawItems { get; init; }
    public List<RecycleListingImage> Images { get; init; } = new();
    public ValidationProblem? Problem { get; init; }
    public bool IsSuccess => Problem is null;
}
