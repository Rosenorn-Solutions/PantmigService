using AuthService.Data;
using AuthService.Models;
using AuthService.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using System.IdentityModel.Tokens.Jwt;
using System.Net.Http.Json;

namespace PantMigTesting.AuthServiceTests;

[Collection("AuthServiceSequential")] // ensure sequential with other auth tests
public class TokenRefreshTests
{
    private static async Task<RegisterResult> RegisterTestUser(HttpClient client, string email, bool org = false)
    {
        var reg = new RegisterRequest
        {
            Email = email,
            Password = "P@ssw0rd!1",
            FirstName = "Test",
            LastName = "User",
            City = "CPH",
            UserType = UserType.Recycler,
            IsOrganization = org
        };
        var resp = await client.PostAsJsonAsync("/auth/register", reg);
        resp.EnsureSuccessStatusCode();
        var result = await resp.Content.ReadFromJsonAsync<RegisterResult>();
        Assert.NotNull(result);
        Assert.True(result!.Success);
        Assert.NotNull(result.AuthResponse);
        return result!;
    }

    [Fact]
    public async Task Refresh_Rotates_Tokens_And_Revokes_Previous()
    {
        using var server = AuthTestServer.Create();
        using var client = server.CreateClient();
        var db = server.Services.GetRequiredService<ApplicationDbContext>();

        // Register user and capture initial tokens
        var reg = await RegisterTestUser(client, $"user{Guid.NewGuid():N}@example.com");
        var initialAccess = reg.AuthResponse!.AccessToken;
        var initialRefresh = reg.AuthResponse!.RefreshToken;

        // Perform refresh
        var refreshReq = new TokenRefreshRequest { AccessToken = initialAccess, RefreshToken = initialRefresh };
        var refreshResp = await client.PostAsJsonAsync("/auth/refresh", refreshReq);
        refreshResp.EnsureSuccessStatusCode();
        var rotated = await refreshResp.Content.ReadFromJsonAsync<AuthResponse>();
        Assert.NotNull(rotated);

        // Assert new tokens differ
        Assert.NotEqual(initialAccess, rotated!.AccessToken);
        Assert.NotEqual(initialRefresh, rotated.RefreshToken);

        // DB: old refresh token revoked, new added
        var oldRt = await db.RefreshTokens.FirstOrDefaultAsync(r => r.Token == initialRefresh);
        Assert.NotNull(oldRt);
        Assert.NotNull(oldRt!.Revoked);
        Assert.Equal(rotated.RefreshToken, oldRt.ReplacedByToken);

        var newRt = await db.RefreshTokens.FirstOrDefaultAsync(r => r.Token == rotated.RefreshToken);
        Assert.NotNull(newRt);
        Assert.Null(newRt!.Revoked);
        Assert.True(newRt.Expires > DateTime.UtcNow);
    }

    [Fact]
    public async Task Cannot_Reuse_Revoked_Refresh_Token()
    {
        using var server = AuthTestServer.Create();
        using var client = server.CreateClient();
        var db = server.Services.GetRequiredService<ApplicationDbContext>();

        var reg = await RegisterTestUser(client, $"user{Guid.NewGuid():N}@example.com");
        var access1 = reg.AuthResponse!.AccessToken;
        var refresh1 = reg.AuthResponse!.RefreshToken;

        // First rotation
        var refreshResp1 = await client.PostAsJsonAsync("/auth/refresh", new TokenRefreshRequest { AccessToken = access1, RefreshToken = refresh1 });
        refreshResp1.EnsureSuccessStatusCode();
        var rotated1 = await refreshResp1.Content.ReadFromJsonAsync<AuthResponse>();
        var access2 = rotated1!.AccessToken;
        var refresh2 = rotated1.RefreshToken;

        // Attempt rotation again with old (revoked) refresh token -> should fail
        var failResp = await client.PostAsJsonAsync("/auth/refresh", new TokenRefreshRequest { AccessToken = access2, RefreshToken = refresh1 });
        Assert.False(failResp.IsSuccessStatusCode);
        Assert.Equal(System.Net.HttpStatusCode.BadRequest, failResp.StatusCode);
        var failObj = await failResp.Content.ReadFromJsonAsync<Dictionary<string, string>>();
        Assert.NotNull(failObj);
        Assert.True(failObj!.Values.Any(v => v.Contains("Invalid"))); // error message contains Invalid or expired

        // Rotation with current refresh token still works
        var refreshResp2 = await client.PostAsJsonAsync("/auth/refresh", new TokenRefreshRequest { AccessToken = access2, RefreshToken = refresh2 });
        refreshResp2.EnsureSuccessStatusCode();
        var rotated2 = await refreshResp2.Content.ReadFromJsonAsync<AuthResponse>();
        Assert.NotEqual(access2, rotated2!.AccessToken);
        Assert.NotEqual(refresh2, rotated2.RefreshToken);
    }

    [Fact]
    public async Task Refresh_Fails_For_Expired_Token()
    {
        using var server = AuthTestServer.Create(services =>
        {
            // could override config here if needed
        });
        using var client = server.CreateClient();
        var db = server.Services.GetRequiredService<ApplicationDbContext>();

        var reg = await RegisterTestUser(client, $"user{Guid.NewGuid():N}@example.com");
        var refreshTokenValue = reg.AuthResponse!.RefreshToken;

        // Expire token manually
        var tokenEntity = await db.RefreshTokens.FirstAsync(r => r.Token == refreshTokenValue);
        tokenEntity.Expires = DateTime.UtcNow.AddSeconds(-1); // set past expiry
        await db.SaveChangesAsync();

        var resp = await client.PostAsJsonAsync("/auth/refresh", new TokenRefreshRequest { AccessToken = reg.AuthResponse.AccessToken, RefreshToken = refreshTokenValue });
        Assert.Equal(System.Net.HttpStatusCode.BadRequest, resp.StatusCode);
        var obj = await resp.Content.ReadFromJsonAsync<Dictionary<string, string>>();
        Assert.NotNull(obj);
        Assert.True(obj!.Values.Any(v => v.Contains("expired")));
    }

    [Fact]
    public async Task Claims_Preserved_Across_Rotation()
    {
        using var server = AuthTestServer.Create();
        using var client = server.CreateClient();

        var reg = await RegisterTestUser(client, $"user{Guid.NewGuid():N}@example.com", org: true);
        var initialAccess = reg.AuthResponse!.AccessToken;
        var initialRefresh = reg.AuthResponse!.RefreshToken;

        var handler = new JwtSecurityTokenHandler();
        var initialJwt = handler.ReadJwtToken(initialAccess);
        var orgClaimBefore = initialJwt.Claims.FirstOrDefault(c => c.Type == "isOrganization");
        Assert.Equal(bool.TrueString, orgClaimBefore!.Value);

        var rotatedResp = await client.PostAsJsonAsync("/auth/refresh", new TokenRefreshRequest { AccessToken = initialAccess, RefreshToken = initialRefresh });
        rotatedResp.EnsureSuccessStatusCode();
        var rotated = await rotatedResp.Content.ReadFromJsonAsync<AuthResponse>();
        var rotatedJwt = handler.ReadJwtToken(rotated!.AccessToken);
        var orgClaimAfter = rotatedJwt.Claims.FirstOrDefault(c => c.Type == "isOrganization");
        Assert.Equal(bool.TrueString, orgClaimAfter!.Value);
    }
}
