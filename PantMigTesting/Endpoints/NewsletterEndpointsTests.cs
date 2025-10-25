using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PantmigService.Data;
using PantmigService.Endpoints;
using PantmigService.Entities;
using PantmigService.Services;
using System.Net.Http.Json;

namespace PantMigTesting.Endpoints;

public class NewsletterEndpointsTests
{
    private sealed class FakeEmailSender : IEmailSender
    {
        public List<(string to, string subject, string body)> Sent { get; } = new();
        public Task SendAsync(string to, string subject, string body, CancellationToken ct = default)
        {
            Sent.Add((to, subject, body));
            return Task.CompletedTask;
        }
    }

    private static (TestServer server, FakeEmailSender emailSender) CreateServer(string? dbName = null)
    {
        var databaseName = dbName ?? Guid.NewGuid().ToString();
        var fake = new FakeEmailSender();

        var builder = new WebHostBuilder()
            .ConfigureServices(services =>
            {
                services.AddRouting();
                services.AddDbContext<PantmigDbContext>(opt => opt.UseInMemoryDatabase(databaseName));
                services.AddSingleton<IEmailSender>(fake);
            })
            .Configure(app =>
            {
                app.UseRouting();
                app.UseEndpoints(endpoints =>
                {
                    endpoints.MapNewsletterEndpoints();
                    endpoints.MapNewsletterUnsubscribe();
                });
            });

        return (new TestServer(builder), fake);
    }

    [Fact]
    public async Task Subscribe_Works_And_Sends_Email()
    {
        var (server, emailSender) = CreateServer();
        using var _ = server; // dispose at end of test
        using var client = server.CreateClient();

        var resp = await client.PostAsJsonAsync("/newsletter/subscribe", new { Name = "Jane Doe", Email = "jane@example.com" });
        resp.EnsureSuccessStatusCode();
        var payload = await resp.Content.ReadFromJsonAsync<ResponseDto>();
        Assert.NotNull(payload);
        Assert.True(payload!.Success);

        // Verify persisted
        using (var scope = server.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PantmigDbContext>();
            var sub = await db.NewsletterSubscriptions.FirstOrDefaultAsync();
            Assert.NotNull(sub);
            Assert.Equal("jane@example.com", sub!.Email);
            Assert.Equal("Jane Doe", sub.Name);
        }

        // Verify email was sent
        Assert.Single(emailSender.Sent);
        Assert.Equal("jane@example.com", emailSender.Sent[0].to);
    }

    [Fact]
    public async Task Unsubscribe_Removes_Subscription_Idempotent()
    {
        var (server, emailSender) = CreateServer();
        using var _ = server;

        // Seed a subscription
        using (var scope = server.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PantmigDbContext>();
            db.NewsletterSubscriptions.Add(new NewsletterSubscription { Name = "John", Email = "john@example.com", CreatedAt = DateTime.UtcNow });
            await db.SaveChangesAsync();
        }

        using var client = server.CreateClient();
        var resp = await client.PostAsJsonAsync("/newsletter/unsubscribe", new { Email = "john@example.com" });
        resp.EnsureSuccessStatusCode();
        var payload = await resp.Content.ReadFromJsonAsync<ResponseDto>();
        Assert.NotNull(payload);
        Assert.True(payload!.Success);

        // Verify removal
        using (var scope = server.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PantmigDbContext>();
            Assert.Equal(0, await db.NewsletterSubscriptions.CountAsync());
        }

        // Idempotent second call
        var resp2 = await client.PostAsJsonAsync("/newsletter/unsubscribe", new { Email = "john@example.com" });
        resp2.EnsureSuccessStatusCode();
        var payload2 = await resp2.Content.ReadFromJsonAsync<ResponseDto>();
        Assert.True(payload2!.Success);

        // No email sent on unsubscribe
        Assert.Empty(emailSender.Sent);
    }

    private sealed class ResponseDto { public bool Success { get; set; } public string? Error { get; set; } }
}
