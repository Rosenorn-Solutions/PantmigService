using System.Net.Http.Json;
using System.Text;
using System.Text.RegularExpressions;
using AuthService.Models;
using AuthService.Services;
using Xunit;

namespace PantMigTesting.AuthServiceTests;

[Collection("AuthServiceSequential")] // ensure sequential execution for shared state
public class ConfirmationEmailTests
{
    [Fact]
    public async Task Register_Sends_Confirmation_Email_With_Link()
    {
        using var server = AuthTestServer.Create();
        using var client = server.CreateClient();

        var reg = new RegisterRequest
        {
            Email = $"user{Guid.NewGuid():N}@example.com",
            Password = "P@ssw0rd!1",
            FirstName = "Test",
            LastName = "User"
        };

        var resp = await client.PostAsJsonAsync("/auth/register", reg);
        resp.EnsureSuccessStatusCode();

        var sent = AuthTestServer.GetSentEmails();
        var email = Assert.Single(sent);
        Assert.Equal(reg.Email, email.To);
        Assert.Contains("/auth/confirm-email?", email.Body);
        Assert.Contains("userId=", email.Body);
        Assert.Contains("token=", email.Body);
    }

    [Fact]
    public async Task Register_Then_ConfirmEmail_Succeeds_And_Sets_EmailConfirmed()
    {
        using var server = AuthTestServer.Create();
        using var client = server.CreateClient();

        var reg = new RegisterRequest
        {
            Email = $"confirm{Guid.NewGuid():N}@example.com",
            Password = "P@ssw0rd!1",
            FirstName = "Test",
            LastName = "User"
        };

        var regResp = await client.PostAsJsonAsync("/auth/register", reg);
        regResp.EnsureSuccessStatusCode();

        var sent = AuthTestServer.GetSentEmails();
        var email = Assert.Single(sent);

        // Extract confirm URL from email body
        var match = Regex.Match(email.Body, @"https?://[^\s]+/auth/confirm-email\?[^\s]+", RegexOptions.IgnoreCase);
        Assert.True(match.Success);
        var confirmUrl = new Uri(match.Value);
        var pathAndQuery = confirmUrl.PathAndQuery; // use path relative to test server

        var confirmResp = await client.GetAsync(pathAndQuery);
        confirmResp.EnsureSuccessStatusCode();

        // Login and call /auth/me to verify EmailConfirmed via DTO
        var loginReq = new LoginRequest { EmailOrUsername = reg.Email, Password = reg.Password };
        var loginResp = await client.PostAsJsonAsync("/auth/login", loginReq);
        loginResp.EnsureSuccessStatusCode();
        var loginResult = await loginResp.Content.ReadFromJsonAsync<LoginResult>();
        Assert.NotNull(loginResult);
        Assert.True(loginResult!.Success);

        client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", loginResult!.AuthResponse!.AccessToken);
        var meResp = await client.GetAsync("/auth/me");
        meResp.EnsureSuccessStatusCode();
        var me = await meResp.Content.ReadFromJsonAsync<UserInformationDTO>();
        Assert.NotNull(me);
        Assert.True(me!.IsEmailConfirmed);
    }
}
