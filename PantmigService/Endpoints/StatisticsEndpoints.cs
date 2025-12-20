using Microsoft.AspNetCore.Mvc;
using PantmigService.Services;
using PantmigService.Utils.Extensions;
using System.Security.Claims;

namespace PantmigService.Endpoints;

public static class StatisticsEndpoints
{
    public static IEndpointRouteBuilder MapStatisticsEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/statistics").WithTags("Statistics");

        group.MapGet("/donor", async (ClaimsPrincipal user, IStatisticsService stats, HttpContext ctx) =>
        {
            var userId = user.GetUserId();
            if (string.IsNullOrEmpty(userId)) return Results.Unauthorized();

            var result = await stats.GetDonorStatisticsAsync(userId, ctx.RequestAborted);
            return Results.Ok(result);
        })
        .RequireAuthorization("VerifiedDonator")
        .WithName("Statistics_Donor")
        .WithSummary("Get donor statistics for current user")
        .WithDescription("Returns the count of completed listings donated, total items, and total approximate worth.")
         .Produces<DonorStatisticsResult>(StatusCodes.Status200OK, contentType: "application/json")
         .Produces(StatusCodes.Status401Unauthorized);

        group.MapGet("/recycler", async (ClaimsPrincipal user, IStatisticsService stats, HttpContext ctx) =>
        {
            var userId = user.GetUserId();
            if (string.IsNullOrEmpty(userId)) return Results.Unauthorized();

            var result = await stats.GetRecyclerStatisticsAsync(userId, ctx.RequestAborted);
            return Results.Ok(result);
        })
        .RequireAuthorization()
        .WithName("Statistics_Recycler")
        .WithSummary("Get recycler statistics for current user")
        .WithDescription("Returns count of completed pickups, total items recycled, breakdown, approximate worth, and total reported amount.")
         .Produces<RecyclerStatisticsResult>(StatusCodes.Status200OK, contentType: "application/json")
         .Produces(StatusCodes.Status401Unauthorized);

        group.MapGet("/city", async ([FromQuery] string? city, IStatisticsService stats, HttpContext ctx) =>
        {
            if (string.IsNullOrWhiteSpace(city))
            {
                return Results.Problem(title: "City required", detail: "Query parameter 'city' is required.", statusCode: StatusCodes.Status400BadRequest, instance: ctx.TraceIdentifier);
            }

            var result = await stats.GetCityStatisticsAsync(city, ctx.RequestAborted);
            if (result is null)
            {
                return Results.NotFound(new { error = "City not found" });
            }
            return Results.Ok(result);
        })
        .WithName("Statistics_City")
        .WithSummary("Get city-based recycling statistics")
        .WithDescription("Open endpoint. Supply city name to get material breakdown and approximate worth for completed listings in that city.")
         .Produces<CityStatisticsResult>(StatusCodes.Status200OK, contentType: "application/json")
         .Produces(StatusCodes.Status400BadRequest)
         .Produces(StatusCodes.Status404NotFound);

        return app;
    }
}
