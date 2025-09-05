using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using AuthService.Data;
using AuthService.Entities;
using AuthService.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;

namespace AuthService.Services
{
    public class TokenService : ITokenService
    {
        private readonly IConfiguration _config;
        private readonly ApplicationDbContext _db;

        public TokenService(IConfiguration config, ApplicationDbContext db)
        {
            _config = config;
            _db = db;
        }

        public (string accessToken, DateTime expiresAt) GenerateAccessToken(ApplicationUser user)
        {
            var jwtSection = _config.GetSection("JwtSettings");
            var secretKey = jwtSection["SecretKey"] ?? throw new InvalidOperationException("Jwt SecretKey not configured");
            var issuer = jwtSection["Issuer"];
            var audience = jwtSection["Audience"];
            var minutesString = jwtSection["AccessTokenMinutes"];
            var expires = DateTime.UtcNow.AddMinutes(int.TryParse(minutesString, out var m) ? m : 60);

            var claims = new List<Claim>
            {
                new(JwtRegisteredClaimNames.Sub, user.Id),
                new(ClaimTypes.NameIdentifier, user.Id),
                new(JwtRegisteredClaimNames.Email, user.Email ?? string.Empty),
                new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
                new(ClaimTypes.GivenName, user.FirstName ?? string.Empty),
                new(ClaimTypes.Surname, user.LastName ?? string.Empty),
                new(ClaimTypes.MobilePhone, user.PhoneNumber ?? string.Empty),
                new("userType", user.UserType.ToString()),
                new("isMitIdVerified", user.IsMitIdVerified.ToString()),
                // New: standard role claim so [Authorize(Roles = ...)] works
                new(ClaimTypes.Role, user.UserType.ToString())
            };

            var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secretKey));
            var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

            var token = new JwtSecurityToken(
                issuer: issuer,
                audience: audience,
                claims: claims,
                expires: expires,
                signingCredentials: creds
            );

            var encoded = new JwtSecurityTokenHandler().WriteToken(token);
            return (encoded, expires);
        }

        public async Task<string> GenerateAndStoreRefreshTokenAsync(ApplicationUser user, CancellationToken ct = default)
        {
            var token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(64));
            var daysCfg = _config.GetSection("JwtSettings")["RefreshTokenDays"];
            var expires = DateTime.UtcNow.AddDays(int.TryParse(daysCfg, out var d) ? d : 30);

            var rt = new RefreshToken
            {
                UserId = user.Id,
                Token = token,
                Expires = expires,
                Created = DateTime.UtcNow
            };

            _db.RefreshTokens.Add(rt);
            await _db.SaveChangesAsync(ct);
            return token;
        }

        public async Task<(AuthResponse? response, string? error)> RotateRefreshTokenAsync(string accessToken, string refreshToken, CancellationToken ct = default)
        {
            var principal = GetPrincipalFromExpiredToken(accessToken);
            if (principal == null) return (null, "Invalid access token");

            var userId = principal.FindFirstValue(JwtRegisteredClaimNames.Sub) ?? principal.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrWhiteSpace(userId)) return (null, "Missing user id in token");

            var rt = await _db.RefreshTokens
                .AsTracking()
                .Where(x => x.UserId == userId && x.Token == refreshToken)
                .FirstOrDefaultAsync(ct);

            if (rt == null || !rt.IsActive)
            {
                return (null, "Refresh token is invalid or expired");
            }

            // revoke current
            rt.Revoked = DateTime.UtcNow;

            // issue new tokens
            var user = await _db.Users.FirstAsync(u => u.Id == userId, ct);
            var (newAccess, exp) = GenerateAccessToken(user);
            var newRefresh = await GenerateAndStoreRefreshTokenAsync(user, ct);
            rt.ReplacedByToken = newRefresh;

            await _db.SaveChangesAsync(ct);

            var resp = new AuthResponse
            {
                UserId = user.Id,
                Email = user.Email ?? string.Empty,
                FirstName = user.FirstName,
                LastName = user.LastName,
                AccessToken = newAccess,
                AccessTokenExpiration = exp,
                RefreshToken = newRefresh,
                UserType = user.UserType
            };

            return (resp, null);
        }

        private ClaimsPrincipal? GetPrincipalFromExpiredToken(string token)
        {
            var jwtSection = _config.GetSection("JwtSettings");
            var secretKey = jwtSection["SecretKey"] ?? string.Empty;
            var issuer = jwtSection["Issuer"];
            var audience = jwtSection["Audience"];

            var tokenValidationParameters = new TokenValidationParameters
            {
                ValidateAudience = true,
                ValidateIssuer = true,
                ValidateIssuerSigningKey = true,
                ValidateLifetime = false, // allow expired token
                ValidIssuer = issuer,
                ValidAudience = audience,
                IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secretKey))
            };

            var tokenHandler = new JwtSecurityTokenHandler();
            try
            {
                var principal = tokenHandler.ValidateToken(token, tokenValidationParameters, out var securityToken);
                if (securityToken is not JwtSecurityToken jwt || !jwt.Header.Alg.Equals(SecurityAlgorithms.HmacSha256, StringComparison.InvariantCultureIgnoreCase))
                    return null;
                return principal;
            }
            catch
            {
                return null;
            }
        }
    }
}
