namespace AuthService.Services
{
    public interface IUsernameGenerator
    {
        Task<string> GenerateAsync(string? firstName, string? lastName, CancellationToken ct = default);
    }
}
