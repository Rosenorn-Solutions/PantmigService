using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using PantmigService;
using PantmigService.Data;
using PantmigService.Entities;
using PantmigService.Services;
using System.Linq;
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

    private sealed class NewsletterApiFactory : WebApplicationFactory<PantmigService.Program>
    {
        private readonly string _databaseName;
        public FakeEmailSender EmailSender { get; } = new();

        public NewsletterApiFactory(string? databaseName = null)
        {
            _databaseName = databaseName ?? Guid.NewGuid().ToString();
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.ConfigureTestServices(services =>
            {
                var dbContextDescriptor = services.SingleOrDefault(d => d.ServiceType == typeof(DbContextOptions<PantmigDbContext>));
                if (dbContextDescriptor is not null)
                {
                    services.Remove(dbContextDescriptor);
                }

                var contextDescriptor = services.SingleOrDefault(d => d.ServiceType == typeof(PantmigDbContext));
                if (contextDescriptor is not null)
                {
                    services.Remove(contextDescriptor);
                }

                services.RemoveAll<IEmailSender>();

                services.AddDbContext<PantmigDbContext>(opt =>
                    opt.UseInMemoryDatabase(_databaseName));
                services.AddSingleton<IEmailSender>(EmailSender);

                using var scope = services.BuildServiceProvider().CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<PantmigDbContext>();
                db.Database.EnsureCreated();
            });
        }
    }

    [Fact]
    public async Task Subscribe_Works_And_Sends_Email()
    {
        using var factory = new NewsletterApiFactory();
        var emailSender = factory.EmailSender;
        using var client = factory.CreateClient();

        var resp = await client.PostAsJsonAsync("/newsletter/subscribe", new { Name = "Jane Doe", Email = "jane@example.com" });
        resp.EnsureSuccessStatusCode();
        var payload = await resp.Content.ReadFromJsonAsync<ResponseDto>();
        Assert.NotNull(payload);
        Assert.True(payload!.Success);

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PantmigDbContext>();
            var sub = await db.NewsletterSubscriptions.FirstOrDefaultAsync();
            Assert.NotNull(sub);
            Assert.Equal("jane@example.com", sub!.Email);
            Assert.Equal("Jane Doe", sub.Name);
        }

        Assert.Single(emailSender.Sent);
        Assert.Equal("jane@example.com", emailSender.Sent[0].to);
    }

    [Fact]
    public async Task Unsubscribe_Removes_Subscription_Idempotent()
    {
        using var factory = new NewsletterApiFactory();
        var emailSender = factory.EmailSender;

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PantmigDbContext>();
            db.NewsletterSubscriptions.Add(new NewsletterSubscription { Name = "John", Email = "john@example.com", CreatedAt = DateTime.UtcNow });
            await db.SaveChangesAsync();
        }

        using var client = factory.CreateClient();
        var resp = await client.PostAsJsonAsync("/newsletter/unsubscribe", new { Email = "john@example.com" });
        resp.EnsureSuccessStatusCode();
        var payload = await resp.Content.ReadFromJsonAsync<ResponseDto>();
        Assert.NotNull(payload);
        Assert.True(payload!.Success);

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PantmigDbContext>();
            Assert.Equal(0, await db.NewsletterSubscriptions.CountAsync());
        }

        var resp2 = await client.PostAsJsonAsync("/newsletter/unsubscribe", new { Email = "john@example.com" });
        resp2.EnsureSuccessStatusCode();
        var payload2 = await resp2.Content.ReadFromJsonAsync<ResponseDto>();
        Assert.True(payload2!.Success);

        Assert.Empty(emailSender.Sent);
    }

    private sealed class ResponseDto { public bool Success { get; set; } public string? Error { get; set; } }
}
