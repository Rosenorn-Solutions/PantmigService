using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PantmigService.Data;
using PantmigService.Entities;
using PantmigService.Services;

namespace PantmigService.Endpoints
{
    public static class NewsletterEndpoints
    {
        public record SubscribeRequest(string Name, string Email);
        public record SubscribeResponse(bool Success, string? Error = null);
        public record UnsubscribeRequest(string Email);
        public record UnsubscribeResponse(bool Success, string? Error = null);

        public static IEndpointRouteBuilder MapNewsletterEndpoints(this IEndpointRouteBuilder app)
        {
            var group = app.MapGroup("/newsletter").WithTags("Newsletter");

            group.MapPost("/subscribe", async ([FromBody] SubscribeRequest req, PantmigDbContext db, IEmailSender emailSender, IConfiguration config, HttpContext ctx) =>
            {
                if (req is null || string.IsNullOrWhiteSpace(req.Email))
                {
                    return Results.BadRequest(new SubscribeResponse(false, "Email is required"));
                }

                var email = req.Email.Trim();
                var name = (req.Name ?? string.Empty).Trim();

                if (!IsValidEmail(email))
                    return Results.BadRequest(new SubscribeResponse(false, "Invalid email"));

                // Idempotent insert if not exists
                var exists = await db.NewsletterSubscriptions.AnyAsync(n => n.Email == email, ctx.RequestAborted);
                if (!exists)
                {
                    db.NewsletterSubscriptions.Add(new NewsletterSubscription
                    {
                        Name = name,
                        Email = email,
                        CreatedAt = DateTime.UtcNow
                    });
                    try
                    {
                        await db.SaveChangesAsync(ctx.RequestAborted);
                    }
                    catch (DbUpdateException)
                    {
                        // ignore unique race
                    }
                }

                // Send confirmation email (no auth required)
                var domain = config["Domain"] ?? config["Urls"] ?? "pantmig.dk";
                var subject = "PantMig Newsletter";
                var body = $"Hej {name?.Trim()},\n\nDu er nu tilmeldt PantMig's nyhedsbrev med {email}.\n\nVenlig hilsen\nPantMig";
                try
                {
                    await emailSender.SendAsync(email, subject, body, ctx.RequestAborted);
                }
                catch
                {
                    // Don't fail subscription on mail issues
                }

                return Results.Ok(new SubscribeResponse(true));
            })
            .Accepts<SubscribeRequest>("application/json")
            .Produces<SubscribeResponse>(StatusCodes.Status200OK, contentType: "application/json")
            .Produces<SubscribeResponse>(StatusCodes.Status400BadRequest, contentType: "application/json")
            .WithOpenApi(op =>
            {
                op.OperationId = "Newsletter_Subscribe";
                op.Summary = "Subscribe to the newsletter";
                op.Description = "Stores the subscriber in the database and sends a confirmation email.";
                return op;
            });

            return app;
        }

        public static IEndpointRouteBuilder MapNewsletterUnsubscribe(this IEndpointRouteBuilder app)
        {
            var group = app.MapGroup("/newsletter").WithTags("Newsletter");

            group.MapPost("/unsubscribe", async ([FromBody] UnsubscribeRequest req, PantmigDbContext db, HttpContext ctx) =>
            {
                if (req is null || string.IsNullOrWhiteSpace(req.Email))
                {
                    return Results.BadRequest(new UnsubscribeResponse(false, "Email is required"));
                }

                var email = req.Email.Trim();
                if (!IsValidEmail(email))
                    return Results.BadRequest(new UnsubscribeResponse(false, "Invalid email"));

                // Remove any entries for the email (idempotent)
                var matches = await db.NewsletterSubscriptions
                    .Where(n => n.Email == email)
                    .ToListAsync(ctx.RequestAborted);
                if (matches.Count > 0)
                {
                    db.NewsletterSubscriptions.RemoveRange(matches);
                    await db.SaveChangesAsync(ctx.RequestAborted);
                }

                return Results.Ok(new UnsubscribeResponse(true));
            })
            .Accepts<UnsubscribeRequest>("application/json")
            .Produces<UnsubscribeResponse>(StatusCodes.Status200OK, contentType: "application/json")
            .Produces<UnsubscribeResponse>(StatusCodes.Status400BadRequest, contentType: "application/json")
            .WithOpenApi(op =>
            {
                op.OperationId = "Newsletter_Unsubscribe";
                op.Summary = "Unsubscribe from the newsletter";
                op.Description = "Removes the email from the newsletter subscriber list. Idempotent.";
                return op;
            });

            // One-click unsubscribe (GET) for email clients like Outlook
            group.MapGet("/unsubscribe", async ([FromQuery] string email, PantmigDbContext db, HttpContext ctx) =>
            {
                if (string.IsNullOrWhiteSpace(email))
                    return Results.BadRequest("Email is required");

                if (!IsValidEmail(email))
                    return Results.BadRequest("Invalid email");

                var rows = await db.NewsletterSubscriptions
                    .Where(n => n.Email == email.Trim())
                    .ToListAsync(ctx.RequestAborted);
                if (rows.Count > 0)
                {
                    db.NewsletterSubscriptions.RemoveRange(rows);
                    await db.SaveChangesAsync(ctx.RequestAborted);
                }

                // Small friendly confirmation
                return Results.Text("You have been unsubscribed.", "text/plain");
            })
            .Produces(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .WithOpenApi(op =>
            {
                op.OperationId = "Newsletter_Unsubscribe_Get";
                op.Summary = "One-click unsubscribe";
                op.Description = "Removes the email using a simple GET. Intended for List-Unsubscribe one-click.";
                return op;
            });

            return app;
        }

        private static bool IsValidEmail(string email)
        {
            try
            {
                var addr = new System.Net.Mail.MailAddress(email);
                return addr.Address.Equals(email, StringComparison.OrdinalIgnoreCase);
            }
            catch { return false; }
        }
    }
}
