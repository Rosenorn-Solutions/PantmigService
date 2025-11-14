using System.Net;
using System.Net.Http.Json;
using AuthService.Models;
using AuthService.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection; // for GetRequiredService
using Xunit;

namespace PantMigTesting.AuthServiceTests;

[Collection("AuthServiceSequential")]
public class AccountManagementEndpointsTests
{
    private static async Task<RegisterResult> Register(HttpClient client, string email)
    {
        var reg = new RegisterRequest { Email = email, Password = "P@ssw0rd!1", FirstName = "First", LastName = "Last", City = "CPH", UserType = UserType.Recycler };
        var resp = await client.PostAsJsonAsync("/auth/register", reg);
        resp.EnsureSuccessStatusCode();
        var data = await resp.Content.ReadFromJsonAsync<RegisterResult>();
        Assert.True(data!.Success);
        return data!;
    }

    [Fact]
    public async Task ChangePassword_RotatesTokens()
    {
        using var server = AuthTestServer.Create(); using var client = server.CreateClient();
        var reg = await Register(client, $"user{Guid.NewGuid():N}@example.com");
        var access1 = reg.AuthResponse!.AccessToken; var refresh1 = reg.AuthResponse.RefreshToken;
        var changeReq = new ChangePasswordRequest { OldPassword = "P@ssw0rd!1", NewPassword = "N3wP@ssw0rd!1" };
        client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", access1);
        var changeResp = await client.PostAsJsonAsync("/auth/change-password", changeReq);
        Assert.Equal(HttpStatusCode.OK, changeResp.StatusCode);
        var changeData = await changeResp.Content.ReadFromJsonAsync<ChangePasswordResult>();
        Assert.True(changeData!.Success);
        Assert.NotNull(changeData.AuthResponse);
        Assert.NotEqual(access1, changeData.AuthResponse!.AccessToken);
        Assert.NotEqual(refresh1, changeData.AuthResponse.RefreshToken);
        var db = server.Services.CreateScope().ServiceProvider.GetRequiredService<AuthService.Data.ApplicationDbContext>();
        var oldToken = await db.RefreshTokens.FirstAsync(t => t.Token == refresh1); Assert.NotNull(oldToken.Revoked);
    }

    [Fact]
    public async Task ChangeEmail_Flows_SendConfirmation()
    {
        using var server = AuthTestServer.Create();
        using var client = server.CreateClient();
        var reg = await Register(client, $"user{Guid.NewGuid():N}@example.com");
        client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", reg.AuthResponse!.AccessToken);
        var newEmail = $"new{Guid.NewGuid():N}@example.com"; var req = new ChangeEmailRequest { NewEmail = newEmail, CurrentPassword = "P@ssw0rd!1" };
        var resp = await client.PostAsJsonAsync("/auth/change-email", req);
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var data = await resp.Content.ReadFromJsonAsync<ChangeEmailResult>();
        Assert.True(data!.Success);
        Assert.True(data.RequiresConfirmation);
        var sent = AuthTestServer.GetSentEmails(); Assert.Contains(sent, s => s.To == newEmail && s.Body.Contains("confirm-email-change"));
    }

    [Fact]
    public async Task DisableAccount_Prevents_Login_And_Rotation()
    {
        using var server = AuthTestServer.Create(); using var client = server.CreateClient();
        var reg = await Register(client, $"user{Guid.NewGuid():N}@example.com");
        client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", reg.AuthResponse!.AccessToken);
        var disableReq = new DisableAccountRequest { CurrentPassword = "P@ssw0rd!1", Reason = "Testing" };
        var disableResp = await client.PostAsJsonAsync("/auth/disable-account", disableReq);
        Assert.Equal(HttpStatusCode.OK, disableResp.StatusCode);
        var disableData = await disableResp.Content.ReadFromJsonAsync<OperationResult>();
        Assert.True(disableData!.Success);
        var loginAttempt = await client.PostAsJsonAsync("/auth/login", new LoginRequest { EmailOrUsername = reg.AuthResponse.Email, Password = "P@ssw0rd!1" });
        Assert.Equal(HttpStatusCode.Unauthorized, loginAttempt.StatusCode);
        var refreshResp = await client.PostAsJsonAsync("/auth/refresh", new TokenRefreshRequest { AccessToken = reg.AuthResponse.AccessToken, RefreshToken = reg.AuthResponse.RefreshToken });
        Assert.Equal(HttpStatusCode.BadRequest, refreshResp.StatusCode);
    }
}
