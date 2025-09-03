using AuthService.Entities;
using AuthService.Models;

namespace AuthService.Services
{
    public interface ITokenService
    {
        (string accessToken, DateTime expiresAt) GenerateAccessToken(ApplicationUser user);
        Task<string> GenerateAndStoreRefreshTokenAsync(ApplicationUser user, CancellationToken ct = default);
        Task<(AuthResponse? response, string? error)> RotateRefreshTokenAsync(string accessToken, string refreshToken, CancellationToken ct = default);
    }
}
