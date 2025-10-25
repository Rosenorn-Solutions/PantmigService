using Microsoft.AspNetCore.Mvc;
using PantmigService.Services;
using System.Security.Claims;

namespace PantmigService.Endpoints
{
    public static class NotificationEndpoints
    {
        public record MarkReadRequest(int[] Ids);

        public static IEndpointRouteBuilder MapNotificationEndpoints(this IEndpointRouteBuilder app)
        {
            var group = app.MapGroup("/notifications").WithTags("Notifications").RequireAuthorization();

            group.MapGet("/recent", async (ClaimsPrincipal user, [FromQuery] int take, INotificationService svc, HttpContext ctx) =>
            {
                var userId = user.FindFirstValue(ClaimTypes.NameIdentifier) ?? user.FindFirstValue("sub");
                if (string.IsNullOrEmpty(userId)) return Results.Unauthorized();
                take = take <= 0 ? 50 : Math.Min(take, 200);
                var data = await svc.GetRecentAsync(userId, take, ctx.RequestAborted);
                return Results.Ok(data);
            });

            group.MapPost("/mark-read", async (MarkReadRequest req, ClaimsPrincipal user, INotificationService svc, HttpContext ctx) =>
            {
                var userId = user.FindFirstValue(ClaimTypes.NameIdentifier) ?? user.FindFirstValue("sub");
                if (string.IsNullOrEmpty(userId)) return Results.Unauthorized();
                var count = await svc.MarkReadAsync(userId, req.Ids, ctx.RequestAborted);
                return Results.Ok(new { Updated = count });
            });

            return app;
        }
    }
}
