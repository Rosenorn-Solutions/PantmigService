using AuthService.Data;
using AuthService.Entities;
using AuthService.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;

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
                new(ClaimTypes.Name, user.UserName ?? string.Empty),
                new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
                new(ClaimTypes.GivenName, user.FirstName ?? string.Empty),
                new(ClaimTypes.Surname, user.LastName ?? string.Empty),
                new(ClaimTypes.MobilePhone, user.PhoneNumber ?? string.Empty),
                new("userType", user.UserType.ToString()),
                new("isMitIdVerified", user.IsMitIdVerified.ToString()),
                new(ClaimTypes.Role, user.UserType.ToString()),
                // New demographic claims
                new("gender", user.Gender.ToString()),
                new("isOrganization", user.IsOrganization.ToString())
            };

            if (user.BirthDate.HasValue)
            {
                claims.Add(new Claim("birthDate", user.BirthDate.Value.ToString("yyyy-MM-dd")));
            }

            if (user.CityId.HasValue)
            {
                claims.Add(new Claim("cityId", user.CityId.Value.ToString()));
                if (user.City is not null && !string.IsNullOrWhiteSpace(user.City.Name))
                {
                    claims.Add(new Claim("cityName", user.City.Name));
                }
            }

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
            // unchanged ... existing implementation
            var tokenBytes = RandomNumberGenerator.GetBytes(64);
            var token = Microsoft.IdentityModel.Tokens.Base64UrlEncoder.Encode(tokenBytes);

            var jwtSection = _config.GetSection("JwtSettings");
            var refreshDays = int.TryParse(jwtSection["RefreshTokenDays"], out var d) ? d : 30;

            var rt = new RefreshToken
            {
                UserId = user.Id,
                Token = token,
                Expires = DateTime.UtcNow.AddDays(refreshDays)
            };

            _db.RefreshTokens.Add(rt);
            await _db.SaveChangesAsync(ct);

            return token;
        }

        public async Task<(AuthResponse? response, string? error)> RotateRefreshTokenAsync(string accessToken, string refreshToken, CancellationToken ct = default)
        {
            try
            {
                // Normalize refresh token to guard against formatting issues in transport
                var incomingRt = (refreshToken ?? string.Empty).Trim();
                if (incomingRt.Contains(' ') && !incomingRt.Contains('+'))
                {
                    incomingRt = incomingRt.Replace(' ', '+');
                }

                string? userId = null;
                try
                {
                    var principal = ValidateAccessToken(accessToken, validateLifetime: false);
                    userId = principal.FindFirstValue(ClaimTypes.NameIdentifier) ?? principal.FindFirstValue(JwtRegisteredClaimNames.Sub);
                }
                catch
                {
                    // ignore here; we'll try to resolve user via refresh token below
                }

                RefreshToken? rt = null;
                if (!string.IsNullOrEmpty(userId))
                {
                    rt = await _db.RefreshTokens.FirstOrDefaultAsync(t => t.Token == incomingRt && t.UserId == userId, ct);
                }

                // Fallback: resolve by refresh token only
                if (rt is null)
                {
                    rt = await _db.RefreshTokens.FirstOrDefaultAsync(t => t.Token == incomingRt, ct);
                    userId ??= rt?.UserId;
                }

                if (rt is null || rt.Expires <= DateTime.UtcNow || string.IsNullOrEmpty(userId))
                    return (null, "Invalid or expired refresh token");

                // Load user with city
                var user = await _db.Users.Include(u => u.City).FirstOrDefaultAsync(u => u.Id == userId, ct);
                if (user is null) return (null, "User not found");

                var (newAccess, exp) = GenerateAccessToken(user);
                return (new AuthResponse
                {
                    UserId = user.Id,
                    Email = user.Email ?? string.Empty,
                    UserName = user.UserName ?? string.Empty,
                    FirstName = user.FirstName ?? string.Empty,
                    LastName = user.LastName ?? string.Empty,
                    AccessToken = newAccess,
                    AccessTokenExpiration = exp,
                    RefreshToken = incomingRt,
                    UserType = user.UserType,
                    IsOrganization = user.IsOrganization,
                    CityId = user.CityId,
                    CityName = user.City?.Name,
                    Gender = user.Gender,
                    BirthDate = user.BirthDate
                }, null);
            }
            catch (Exception ex)
            {
                return (null, ex.Message);
            }
        }

        private ClaimsPrincipal ValidateAccessToken(string token, bool validateLifetime)
        {
            var jwtSection = _config.GetSection("JwtSettings");
            var secretKey = jwtSection["SecretKey"] ?? throw new InvalidOperationException("Jwt SecretKey not configured");
            var issuer = jwtSection["Issuer"];
            var audience = jwtSection["Audience"];

            var tokenHandler = new JwtSecurityTokenHandler();
            var key = Encoding.UTF8.GetBytes(secretKey);
            var parameters = new TokenValidationParameters
            {
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = new SymmetricSecurityKey(key),
                ValidateIssuer = true,
                ValidIssuer = issuer,
                ValidateAudience = true,
                ValidAudience = audience,
                ValidateLifetime = validateLifetime,
                ClockSkew = TimeSpan.Zero
            };

            return tokenHandler.ValidateToken(token, parameters, out _);
        }
    }
}
