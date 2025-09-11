namespace AuthService.Services
{
    public interface ICityResolver
    {
        // Returns CityId for a given input name/slug; creates if not existing when allowed
        Task<int> ResolveOrCreateAsync(string input, CancellationToken ct = default);
    }
}
