using Microsoft.AspNetCore.TestHost;
using System.Net;
using System.Net.Http.Json;
using AuthService.Models;
using Xunit;

namespace PantMigTesting.AuthServiceTests;

public class LoginMissingEmailTests
{
 [Fact]
 public async Task Login_WithNonExistingEmail_Returns401_Not500()
 {
 using var host = AuthTestServer.Create();
 using var client = host.GetTestClient();

 var req = new LoginRequest
 {
 EmailOrUsername = "does-not-exist@example.com",
 Password = "SomePassword123!"
 };

 var resp = await client.PostAsJsonAsync("/auth/login", req);

 Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
 }
}
