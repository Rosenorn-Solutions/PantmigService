using PantmigService.Entities;

namespace PantmigService.Services;

public record DonorStatisticsResult(
    int ListingCount,
    int TotalItems,
    decimal TotalApproximateWorth
);

public record RecyclerMaterialBreakdown(RecycleMaterialType Material, int Quantity);

public record RecyclerStatisticsResult(
    int ListingCount,
    int TotalItems,
    decimal TotalApproximateWorth,
    decimal TotalReportedAmount,
    IReadOnlyList<RecyclerMaterialBreakdown> Breakdown
);

// New: open-city statistics result
public record CityStatisticsResult(
    string CityName,
    int TotalItems,
    decimal TotalApproximateWorth,
    IReadOnlyList<RecyclerMaterialBreakdown> Breakdown
);

public interface IStatisticsService
{
    Task<DonorStatisticsResult> GetDonorStatisticsAsync(string donatorUserId, CancellationToken ct = default);
    Task<RecyclerStatisticsResult> GetRecyclerStatisticsAsync(string recyclerUserId, CancellationToken ct = default);
    // New: city-based statistics (open to anyone)
    Task<CityStatisticsResult?> GetCityStatisticsAsync(string city, CancellationToken ct = default);
}
