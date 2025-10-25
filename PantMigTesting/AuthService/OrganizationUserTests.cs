using AuthService.Models;
using System.IdentityModel.Tokens.Jwt;
using System.Net.Http.Headers;
using System.Net.Http.Json;

namespace PantMigTesting.AuthServiceTests;

public class OrganizationUserTests
{
    [Fact]
    public async Task Register_Login_Token_Contain_IsOrganization()
    {
        using var server = AuthTestServer.Create();
        using var client = server.CreateClient();

        // 1) Register an organization account
        var reg = new RegisterRequest
        {
            Email = $"org{Guid.NewGuid():N}@example.com",
            Password = "P@ssw0rd!1",
            FirstName = "Org",
            LastName = "User",
            Phone = "12345678",
            City = "CPH",
            UserType = UserType.Donator,
            IsOrganization = true
        };

        var regResp = await client.PostAsJsonAsync("/auth/register", reg);
        regResp.EnsureSuccessStatusCode();
        var result = await regResp.Content.ReadFromJsonAsync<RegisterResult>();
        Assert.NotNull(result);
        Assert.True(result!.Success);
        Assert.NotNull(result!.AuthResponse);
        Assert.True(result!.AuthResponse!.IsOrganization);

        // Decode JWT and verify claim is present
        var token = result!.AuthResponse!.AccessToken;
        var handler = new JwtSecurityTokenHandler();
        var jwt = handler.ReadJwtToken(token);
        var orgClaim = jwt.Claims.FirstOrDefault(c => c.Type == "isOrganization");
        Assert.NotNull(orgClaim);
        Assert.Equal(bool.TrueString, orgClaim!.Value);

        // 2) Call /auth/me with the registration access token and check DTO
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", result!.AuthResponse!.AccessToken);
        var meResp = await client.GetAsync("/auth/me");
        meResp.EnsureSuccessStatusCode();
        var me = await meResp.Content.ReadFromJsonAsync<UserInformationDTO>();
        Assert.NotNull(me);
        Assert.True(me!.IsOrganization);

        // 3) Refresh token and verify IsOrganization also present in refreshed token and response
        var refreshReq = new TokenRefreshRequest
        {
            AccessToken = result!.AuthResponse!.AccessToken,
            RefreshToken = result!.AuthResponse!.RefreshToken
        };
        var refreshResp = await client.PostAsJsonAsync("/auth/refresh", refreshReq);
        refreshResp.EnsureSuccessStatusCode();
        var refreshed = await refreshResp.Content.ReadFromJsonAsync<AuthResponse>();
        Assert.NotNull(refreshed);
        Assert.True(refreshed!.IsOrganization);
        var refreshedJwt = handler.ReadJwtToken(refreshed!.AccessToken);
        var refreshedClaim = refreshedJwt.Claims.FirstOrDefault(c => c.Type == "isOrganization");
        Assert.NotNull(refreshedClaim);
        Assert.Equal(bool.TrueString, refreshedClaim!.Value);
    }

    [Fact]
    public async Task NonOrganization_User_Should_Have_IsOrganization_False()
    {
        using var server = AuthTestServer.Create();
        using var client = server.CreateClient();

        var reg = new RegisterRequest
        {
            Email = $"person{Guid.NewGuid():N}@example.com",
            Password = "P@ssw0rd!1",
            FirstName = "Person",
            LastName = "User",
            Phone = "87654321",
            City = "CPH",
            UserType = UserType.Recycler,
            IsOrganization = false
        };

        var regResp = await client.PostAsJsonAsync("/auth/register", reg);
        regResp.EnsureSuccessStatusCode();
        var result = await regResp.Content.ReadFromJsonAsync<RegisterResult>();
        Assert.NotNull(result);
        Assert.True(result!.Success);
        Assert.False(result!.AuthResponse!.IsOrganization);

        var handler = new JwtSecurityTokenHandler();
        var jwt = handler.ReadJwtToken(result!.AuthResponse!.AccessToken);
        var claim = jwt.Claims.FirstOrDefault(c => c.Type == "isOrganization");
        Assert.NotNull(claim);
        Assert.Equal(bool.FalseString, claim!.Value);
    }
}
