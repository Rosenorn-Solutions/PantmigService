using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Microsoft.OpenApi.Models;
using PantmigService.Services;
using PantmigService.Utils.Extensions;

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
        .WithOpenApi(op =>
        {
            op.OperationId = "Statistics_Donor";
            op.Summary = "Get donor statistics for current user";
            op.Description = "Returns count of completed listings donated, total items, and total approximate worth.";
            return op;
        })
        .Produces<DonorStatisticsResult>(StatusCodes.Status200OK, contentType: "application/json")
        .Produces(StatusCodes.Status401Unauthorized);

        group.MapGet("/recycler", async (ClaimsPrincipal user, IStatisticsService stats, HttpContext ctx) =>
        {
            var userId = user.GetUserId();
            if (string.IsNullOrEmpty(userId)) return Results.Unauthorized();

            var result = await stats.GetRecyclerStatisticsAsync(userId, ctx.RequestAborted);
            return Results.Ok(result);
        })
        .RequireAuthorization() // any authenticated user can fetch their own recycler stats
        .WithOpenApi(op =>
        {
            op.OperationId = "Statistics_Recycler";
            op.Summary = "Get recycler statistics for current user";
            op.Description = "Returns count of completed pickups, total items recycled, material breakdown, approximate worth, and total reported amount.";
            return op;
        })
        .Produces<RecyclerStatisticsResult>(StatusCodes.Status200OK, contentType: "application/json")
        .Produces(StatusCodes.Status401Unauthorized);

        // Open endpoint: city-based statistics
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
        .WithOpenApi(op =>
        {
            op.OperationId = "Statistics_City";
            op.Summary = "Get city-based recycling statistics";
            op.Description = "Open endpoint. Supply city name (case-insensitive). Returns material breakdown and total approximate worth for completed listings in that city.";
            // Describe query parameter
            op.Parameters =
            [
                new Microsoft.OpenApi.Models.OpenApiParameter
                {
                    Name = "city",
                    In = Microsoft.OpenApi.Models.ParameterLocation.Query,
                    Required = true,
                    Description = "City name"
                }
            ];
            op.Responses ??= new Microsoft.OpenApi.Models.OpenApiResponses();
            return op;
        })
        .Produces<CityStatisticsResult>(StatusCodes.Status200OK, contentType: "application/json")
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status404NotFound);

        return app;
    }
}
