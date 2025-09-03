using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi.Models;
using PantmigService.Entities;
using PantmigService.Services;

namespace PantmigService.Endpoints
{
    public static class RecycleListingEndpoints
    {
        public record CreateRecycleListingRequest(string Title, string Description, string? City, string? Location, string? EstimatedValue, decimal EstimatedAmount, DateTime AvailableFrom, DateTime AvailableTo);
        public record PickupRequest(int ListingId);
        public record AcceptRequest(int ListingId);
        public record ChatStartRequest(int ListingId);
        public record PickupConfirmRequest(int ListingId);
        public record ReceiptSubmitRequest(int ListingId, string ReceiptImageUrl, decimal ReportedAmount);
        public record ReceiptVerifyRequest(int ListingId, decimal VerifiedAmount);
        public record MeetingPointRequest(int ListingId, decimal Latitude, decimal Longitude);

        public static IEndpointRouteBuilder MapRecycleListingEndpoints(this IEndpointRouteBuilder app)
        {
            var group = app.MapGroup("/listings")
                .WithTags("Recycle Listings");

            // Active listings
            group.MapGet("/", async (IRecycleListingService svc) => Results.Ok(await svc.GetActiveAsync()))
                .WithOpenApi(op =>
                {
                    op.OperationId = "Listings_GetActive";
                    op.Summary = "Get active recycle listings";
                    op.Description = "Returns all listings that are currently active and available.";
                    return op;
                })
                .Produces<IEnumerable<RecycleListing>>(StatusCodes.Status200OK, contentType: "application/json");

            group.MapGet("/{id:int}", async (int id, IRecycleListingService svc) =>
            {
                var item = await svc.GetByIdAsync(id);
                return item is null ? Results.NotFound() : Results.Ok(item);
            })
            .WithOpenApi(op =>
            {
                op.OperationId = "Listings_GetById";
                op.Summary = "Get a listing by id";
                op.Description = "Retrieves a single recycle listing by its identifier.";
                return op;
            })
            .Produces<RecycleListing>(StatusCodes.Status200OK, contentType: "application/json")
            .Produces(StatusCodes.Status404NotFound);

            // Donator creates listing
            group.MapPost("/", async (CreateRecycleListingRequest req, ClaimsPrincipal user, IRecycleListingService svc, ICityResolver cityResolver) =>
            {
                var userId = user.FindFirstValue(ClaimTypes.NameIdentifier) ?? user.FindFirstValue("sub");
                if (string.IsNullOrEmpty(userId)) return Results.Unauthorized();

                var cityInput = req.City ?? req.Location;
                if (string.IsNullOrWhiteSpace(cityInput)) return Results.BadRequest(new { error = "City or Location is required" });
                var cityId = await cityResolver.ResolveOrCreateAsync(cityInput);

                var listing = new RecycleListing
                {
                    Title = req.Title,
                    Description = req.Description,
                    Location = req.Location ?? req.City ?? string.Empty,
                    EstimatedValue = req.EstimatedValue,
                    EstimatedAmount = req.EstimatedAmount,
                    AvailableFrom = req.AvailableFrom,
                    AvailableTo = req.AvailableTo,
                    CreatedByUserId = userId,
                    CreatedAt = DateTime.UtcNow,
                    IsActive = true,
                    Status = ListingStatus.Created,
                    CityId = cityId
                };

                var created = await svc.CreateAsync(listing);
                return Results.Created($"/listings/{created.Id}", created);
            })
            .RequireAuthorization("VerifiedDonator")
            .Accepts<CreateRecycleListingRequest>("application/json")
            .WithOpenApi(op =>
            {
                op.OperationId = "Listings_Create";
                op.Summary = "Create a new listing";
                op.Description = "Creates a new recycle listing. Requires a verified Donator.";
                return op;
            })
            .Produces<RecycleListing>(StatusCodes.Status201Created, contentType: "application/json")
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized);

            // Recycler requests pickup
            group.MapPost("/pickup/request", async (PickupRequest req, ClaimsPrincipal user, IRecycleListingService svc) =>
            {
                var userId = user.FindFirstValue(ClaimTypes.NameIdentifier) ?? user.FindFirstValue("sub");
                if (string.IsNullOrEmpty(userId)) return Results.Unauthorized();
                var ok = await svc.RequestPickupAsync(req.ListingId, userId);
                return ok ? Results.Ok() : Results.BadRequest();
            })
            .RequireAuthorization()
            .Accepts<PickupRequest>("application/json")
            .WithOpenApi(op =>
            {
                op.OperationId = "Listings_PickupRequest";
                op.Summary = "Request pickup for a listing";
                op.Description = "Recycler requests to pick up a specific listing.";
                return op;
            })
            .Produces(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized);

            // Donator accepts recycler
            group.MapPost("/pickup/accept", async (AcceptRequest req, ClaimsPrincipal user, IRecycleListingService svc) =>
            {
                var userId = user.FindFirstValue(ClaimTypes.NameIdentifier) ?? user.FindFirstValue("sub");
                if (string.IsNullOrEmpty(userId)) return Results.Unauthorized();
                var ok = await svc.AcceptPickupAsync(req.ListingId, userId);
                return ok ? Results.Ok() : Results.BadRequest();
            })
            .RequireAuthorization("VerifiedDonator")
            .Accepts<AcceptRequest>("application/json")
            .WithOpenApi(op =>
            {
                op.OperationId = "Listings_PickupAccept";
                op.Summary = "Accept a recycler for pickup";
                op.Description = "Donator accepts a recycler's pickup request for the listing.";
                return op;
            })
            .Produces(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized);

            // Start direct chat between donator and assigned recycler (restricted to those two users)
            group.MapPost("/chat/start", async (ChatStartRequest req, ClaimsPrincipal user, IRecycleListingService svc) =>
            {
                var userId = user.FindFirstValue(ClaimTypes.NameIdentifier) ?? user.FindFirstValue("sub");
                if (string.IsNullOrEmpty(userId)) return Results.Unauthorized();

                var listing = await svc.GetByIdAsync(req.ListingId);
                if (listing is null) return Results.NotFound();
                if (!listing.IsActive || listing.Status != ListingStatus.Accepted) return Results.BadRequest();

                var donatorId = listing.CreatedByUserId;
                var recyclerId = listing.AssignedRecyclerUserId;
                if (string.IsNullOrEmpty(recyclerId)) return Results.BadRequest();

                var isParticipant = string.Equals(userId, donatorId, StringComparison.Ordinal) || string.Equals(userId, recyclerId, StringComparison.Ordinal);
                if (!isParticipant) return Results.Forbid();

                var chatId = $"listing-{req.ListingId}";

                var ok = await svc.StartChatAsync(req.ListingId, chatId);
                return ok ? Results.Ok() : Results.BadRequest();
            })
            .RequireAuthorization()
            .Accepts<ChatStartRequest>("application/json")
            .WithOpenApi(op =>
            {
                op.OperationId = "Listings_ChatStart";
                op.Summary = "Start a direct chat for a listing";
                op.Description = "Starts a chat between the donator and the assigned recycler for the listing.";
                return op;
            })
            .Produces(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status404NotFound);

            // Donator sets meeting point (requires chat started)
            group.MapPost("/meeting/set", async (MeetingPointRequest req, ClaimsPrincipal user, IRecycleListingService svc) =>
            {
                var userId = user.FindFirstValue(ClaimTypes.NameIdentifier) ?? user.FindFirstValue("sub");
                if (string.IsNullOrEmpty(userId)) return Results.Unauthorized();
                var ok = await svc.SetMeetingPointAsync(req.ListingId, userId, req.Latitude, req.Longitude);
                return ok ? Results.Ok() : Results.BadRequest();
            })
            .RequireAuthorization("VerifiedDonator")
            .Accepts<MeetingPointRequest>("application/json")
            .WithOpenApi(op =>
            {
                op.OperationId = "Listings_MeetingSet";
                op.Summary = "Set meeting point for a listing";
                op.Description = "Donator sets the meeting point coordinates. Requires chat to be started.";
                return op;
            })
            .Produces(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized);

            // Recycler confirms pickup
            group.MapPost("/pickup/confirm", async (PickupConfirmRequest req, ClaimsPrincipal user, IRecycleListingService svc) =>
            {
                var userId = user.FindFirstValue(ClaimTypes.NameIdentifier) ?? user.FindFirstValue("sub");
                if (string.IsNullOrEmpty(userId)) return Results.Unauthorized();
                var ok = await svc.ConfirmPickupAsync(req.ListingId, userId);
                return ok ? Results.Ok() : Results.BadRequest();
            })
            .RequireAuthorization()
            .Accepts<PickupConfirmRequest>("application/json")
            .WithOpenApi(op =>
            {
                op.OperationId = "Listings_PickupConfirm";
                op.Summary = "Confirm pickup";
                op.Description = "Recycler confirms that the pickup has been performed.";
                return op;
            })
            .Produces(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized);

            // Recycler submits receipt
            group.MapPost("/receipt/submit", async (ReceiptSubmitRequest req, ClaimsPrincipal user, IRecycleListingService svc) =>
            {
                var userId = user.FindFirstValue(ClaimTypes.NameIdentifier) ?? user.FindFirstValue("sub");
                if (string.IsNullOrEmpty(userId)) return Results.Unauthorized();
                var ok = await svc.SubmitReceiptAsync(req.ListingId, userId, req.ReceiptImageUrl, req.ReportedAmount);
                return ok ? Results.Ok() : Results.BadRequest();
            })
            .RequireAuthorization()
            .Accepts<ReceiptSubmitRequest>("application/json")
            .WithOpenApi(op =>
            {
                op.OperationId = "Listings_ReceiptSubmit";
                op.Summary = "Submit receipt";
                op.Description = "Recycler submits the receipt image URL and reported amount for the listing.";
                return op;
            })
            .Produces(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized);

            // Donator verifies receipt amount
            group.MapPost("/receipt/verify", async (ReceiptVerifyRequest req, ClaimsPrincipal user, IRecycleListingService svc) =>
            {
                var userId = user.FindFirstValue(ClaimTypes.NameIdentifier) ?? user.FindFirstValue("sub");
                if (string.IsNullOrEmpty(userId)) return Results.Unauthorized();
                var ok = await svc.VerifyReceiptAsync(req.ListingId, userId, req.VerifiedAmount);
                return ok ? Results.Ok() : Results.BadRequest();
            })
            .RequireAuthorization("VerifiedDonator")
            .Accepts<ReceiptVerifyRequest>("application/json")
            .WithOpenApi(op =>
            {
                op.OperationId = "Listings_ReceiptVerify";
                op.Summary = "Verify receipt amount";
                op.Description = "Donator verifies the receipt amount reported by the recycler.";
                return op;
            })
            .Produces(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized);

            return app;
        }
    }
}
