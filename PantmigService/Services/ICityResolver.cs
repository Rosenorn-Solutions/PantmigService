namespace PantmigService.Services
{
    public interface ICityResolver
    {
        // Returns CityId for a given input name/slug; creates if not existing when allowed
        Task<int> ResolveOrCreateAsync(string input, CancellationToken ct = default);
        // Resolve by cross-service stable ExternalId
        Task<int> ResolveByExternalIdAsync(Guid externalId, CancellationToken ct = default);
    }
}
