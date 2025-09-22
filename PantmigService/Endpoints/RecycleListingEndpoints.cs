using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi.Models;
using PantmigService.Entities;
using PantmigService.Services;
using Microsoft.Extensions.Logging;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Antiforgery;
using PantmigService.Security;

namespace PantmigService.Endpoints
{
    public static class RecycleListingEndpoints
    {
        public record CreateRecycleListingRequest(string Title, string Description, string? City, string? Location, decimal? EstimatedValue, string? EstimatedAmount, DateTime AvailableFrom, DateTime AvailableTo);
        public record PickupRequest(int ListingId);
        public record AcceptRequest(int ListingId, string RecyclerUserId);
        public record ChatStartRequest(int ListingId);
        public record PickupConfirmRequest(int ListingId);
        public record MeetingPointRequest(int ListingId, decimal Latitude, decimal Longitude);
        public record CancelRequest(int ListingId);

        public static IEndpointRouteBuilder MapRecycleListingEndpoints(this IEndpointRouteBuilder app)
        {
            var group = app.MapGroup("/listings")
                .WithTags("Recycle Listings");

            // Active listings
            group.MapGet("/", async (IRecycleListingService svc, ILoggerFactory lf, HttpContext ctx) =>
            {
                var logger = lf.CreateLogger("Listings");
                try
                {
                    var data = await svc.GetActiveAsync(ctx.RequestAborted);
                    return Results.Ok(data);
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Failed to get active listings");
                    return Results.Problem(title: "Failed to get active listings", detail: ex.Message, statusCode: StatusCodes.Status500InternalServerError, instance: ctx.TraceIdentifier);
                }
            })
                .WithOpenApi(op =>
                {
                    op.OperationId = "Listings_GetActive";
                    op.Summary = "Get active recycle listings";
                    op.Description = "Returns all listings that are currently active and available.";
                    return op;
                })
                .RequireAuthorization()
                .Produces<IEnumerable<RecycleListing>>(StatusCodes.Status200OK, contentType: "application/json");

            // My applications (recycler)
            group.MapGet("/my-applications", async (ClaimsPrincipal user, IRecycleListingService svc, ILoggerFactory lf, HttpContext ctx) =>
            {
                var logger = lf.CreateLogger("Listings");
                try
                {
                    var userId = user.FindFirstValue(ClaimTypes.NameIdentifier) ?? user.FindFirstValue("sub");
                    if (string.IsNullOrEmpty(userId))
                    {
                        logger.LogWarning("Unauthorized access to my-applications");
                        return Results.Unauthorized();
                    }
                    var items = await svc.GetAppliedByRecyclerAsync(userId, ctx.RequestAborted);
                    return Results.Ok(items);
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Failed to get my applications");
                    return Results.Problem(title: "Failed to get applications", detail: ex.Message, statusCode: StatusCodes.Status500InternalServerError, instance: ctx.TraceIdentifier);
                }
            })
            .RequireAuthorization()
            .WithOpenApi(op =>
            {
                op.OperationId = "Listings_MyApplications";
                op.Summary = "Get my applications";
                op.Description = "Returns all listings the authenticated recycler has applied to.";
                return op;
            })
            .Produces<IEnumerable<RecycleListing>>(StatusCodes.Status200OK, contentType: "application/json")
            .Produces(StatusCodes.Status401Unauthorized);

            // My listings (donator)
            group.MapGet("/my-listings", async (ClaimsPrincipal user, IRecycleListingService svc, ILoggerFactory lf, HttpContext ctx) =>
            {
                var logger = lf.CreateLogger("Listings");
                try
                {
                    var userId = user.FindFirstValue(ClaimTypes.NameIdentifier) ?? user.FindFirstValue("sub");
                    if (string.IsNullOrEmpty(userId))
                    {
                        logger.LogWarning("Unauthorized access to my-listings");
                        return Results.Unauthorized();
                    }
                    // Returns all listings for this user regardless of status
                    var items = await svc.GetByUserAsync(userId, ctx.RequestAborted);
                    return Results.Ok(items);
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Failed to get my listings");
                    return Results.Problem(title: "Failed to get listings", detail: ex.Message, statusCode: StatusCodes.Status500InternalServerError, instance: ctx.TraceIdentifier);
                }
            })
            .RequireAuthorization("VerifiedDonator")
            .WithOpenApi(op =>
            {
                op.OperationId = "Listings_My";
                op.Summary = "Get my listings";
                op.Description = "Returns all listings created by the authenticated donator, including cancelled and completed.";
                return op;
            })
            .Produces<IEnumerable<RecycleListing>>(StatusCodes.Status200OK, contentType: "application/json")
            .Produces(StatusCodes.Status401Unauthorized);

            group.MapGet("/{id:int}", async (int id, IRecycleListingService svc, ILoggerFactory lf, HttpContext ctx) =>
            {
                var logger = lf.CreateLogger("Listings");
                try
                {
                    var item = await svc.GetByIdAsync(id, ctx.RequestAborted);
                    if (item is null)
                    {
                        logger.LogWarning("Listing {ListingId} not found", id);
                        return Results.NotFound(new { error = "Listing not found" });
                    }
                    return Results.Ok(item);
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Failed to get listing {ListingId}", id);
                    return Results.Problem(title: "Failed to get listing", detail: ex.Message, statusCode: StatusCodes.Status500InternalServerError, instance: ctx.TraceIdentifier);
                }
            })
            .WithOpenApi(op =>
            {
                op.OperationId = "Listings_GetById";
                op.Summary = "Get a listing by id";
                op.Description = "Retrieves a single recycle listing by its identifier.";
                return op;
            })
            .RequireAuthorization()
            .Produces<RecycleListing>(StatusCodes.Status200OK, contentType: "application/json")
            .Produces(StatusCodes.Status404NotFound);

            // Donator creates listing
            group.MapPost("/", async (CreateRecycleListingRequest req, ClaimsPrincipal user, IRecycleListingService svc, ICityResolver cityResolver, ILoggerFactory lf, HttpContext ctx) =>
            {
                var logger = lf.CreateLogger("Listings");
                try
                {
                    var userId = user.FindFirstValue(ClaimTypes.NameIdentifier) ?? user.FindFirstValue("sub");
                    if (string.IsNullOrEmpty(userId)) return Results.Unauthorized();

                    if (string.IsNullOrWhiteSpace(req.Title) || string.IsNullOrWhiteSpace(req.Description))
                    {
                        return Results.Problem(title: "Validation error", detail: "Title and Description are required", statusCode: StatusCodes.Status400BadRequest, instance: ctx.TraceIdentifier);
                    }

                    var cityInput = req.City ?? req.Location;
                    if (string.IsNullOrWhiteSpace(cityInput))
                    {
                        return Results.Problem(title: "Validation error", detail: "City or Location is required", statusCode: StatusCodes.Status400BadRequest, instance: ctx.TraceIdentifier);
                    }

                    if (req.AvailableTo <= req.AvailableFrom)
                    {
                        return Results.Problem(title: "Validation error", detail: "AvailableTo must be after AvailableFrom", statusCode: StatusCodes.Status400BadRequest, instance: ctx.TraceIdentifier);
                    }

                    var cityId = await cityResolver.ResolveOrCreateAsync(cityInput, ctx.RequestAborted);

                    var listing = new RecycleListing
                    {
                        Title = req.Title,
                        Description = req.Description,
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

                    var created = await svc.CreateAsync(listing, ctx.RequestAborted);
                    logger.LogInformation("Listing {ListingId} created by {UserId}", created.Id, userId);
                    return Results.Created($"/listings/{created.Id}", created);
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Failed to create listing for user");
                    return Results.Problem(title: "Failed to create listing", detail: ex.Message, statusCode: StatusCodes.Status500InternalServerError, instance: ctx.TraceIdentifier);
                }
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
            group.MapPost("/pickup/request", async (PickupRequest req, ClaimsPrincipal user, IRecycleListingService svc, ILoggerFactory lf, HttpContext ctx) =>
            {
                var logger = lf.CreateLogger("Listings");
                try
                {
                    var userId = user.FindFirstValue(ClaimTypes.NameIdentifier) ?? user.FindFirstValue("sub");
                    if (string.IsNullOrEmpty(userId)) return Results.Unauthorized();
                    var ok = await svc.RequestPickupAsync(req.ListingId, userId, ctx.RequestAborted);
                    if (!ok)
                    {
                        logger.LogWarning("Pickup request failed for listing {ListingId} by recycler {Recycler}", req.ListingId, userId);
                        return Results.Problem(title: "Pickup request failed", detail: "Listing not available for pickup or already requested.", statusCode: StatusCodes.Status400BadRequest, instance: ctx.TraceIdentifier);
                    }
                    logger.LogInformation("Pickup requested for listing {ListingId} by {UserId}", req.ListingId, userId);
                    return Results.Ok();
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Pickup request error for listing {ListingId}", req.ListingId);
                    return Results.Problem(title: "Pickup request error", detail: ex.Message, statusCode: StatusCodes.Status500InternalServerError, instance: ctx.TraceIdentifier);
                }
            })
            .RequireAuthorization()
            .Accepts<PickupRequest>("application/json")
            .WithOpenApi(op =>
            {
                op.OperationId = "Listings_PickupRequest";
                op.Summary = "Request pickup for a listing";
                op.Description = "Recycler requests to pick up a specific listing. Adds the recycler to the applicants list.";
                return op;
            })
            .Produces(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized);

            // Donator views applicants list
            group.MapGet("/{id:int}/applicants", async (int id, ClaimsPrincipal user, IRecycleListingService svc, ILoggerFactory lf, HttpContext ctx) =>
            {
                var logger = lf.CreateLogger("Listings");
                try
                {
                    var userId = user.FindFirstValue(ClaimTypes.NameIdentifier) ?? user.FindFirstValue("sub");
                    if (string.IsNullOrEmpty(userId)) return Results.Unauthorized();
                    var list = await svc.GetApplicantsAsync(id, userId, ctx.RequestAborted);
                    if (list is null)
                    {
                        logger.LogWarning("Applicants retrieval failed for listing {ListingId} by user {UserId}", id, userId);
                        return Results.Problem(title: "Cannot retrieve applicants", detail: "Listing not found, inactive, or you are not the owner.", statusCode: StatusCodes.Status400BadRequest, instance: ctx.TraceIdentifier);
                    }
                    var result = list ?? Array.Empty<ApplicantInfo>();
                    return Results.Ok(result);
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Error retrieving applicants for listing {ListingId}", id);
                    return Results.Problem(title: "Failed to get applicants", detail: ex.Message, statusCode: StatusCodes.Status500InternalServerError, instance: ctx.TraceIdentifier);
                }
            })
            .RequireAuthorization("VerifiedDonator")
            .WithOpenApi(op =>
            {
                op.OperationId = "Listings_Applicants_Get";
                op.Summary = "Get applicants for a listing";
                op.Description = "Donator retrieves the list of applicants with their user IDs and appliedAt timestamps.";
                return op;
            })
            .Produces<IEnumerable<ApplicantInfo>>(StatusCodes.Status200OK, contentType: "application/json")
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status400BadRequest);

            // Donator accepts recycler
            group.MapPost("/pickup/accept", async (AcceptRequest req, ClaimsPrincipal user, IRecycleListingService svc, ILoggerFactory lf, HttpContext ctx) =>
            {
                var logger = lf.CreateLogger("Listings");
                try
                {
                    var userId = user.FindFirstValue(ClaimTypes.NameIdentifier) ?? user.FindFirstValue("sub");
                    if (string.IsNullOrEmpty(userId)) return Results.Unauthorized();
                    var ok = await svc.AcceptPickupAsync(req.ListingId, userId, req.RecyclerUserId, ctx.RequestAborted);
                    if (!ok)
                    {
                        logger.LogWarning("Accept recycler failed for listing {ListingId} by donator {Donator} for recycler {Recycler}", req.ListingId, userId, req.RecyclerUserId);
                        return Results.Problem(title: "Accept failed", detail: "Cannot accept recycler for this listing.", statusCode: StatusCodes.Status400BadRequest, instance: ctx.TraceIdentifier);
                    }
                    logger.LogInformation("Accept recycler for listing {ListingId} by donator {Donator} for recycler {Recycler}", req.ListingId, userId, req.RecyclerUserId);
                    return Results.Ok();
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Error accepting recycler for listing {ListingId}", req.ListingId);
                    return Results.Problem(title: "Accept error", detail: ex.Message, statusCode: StatusCodes.Status500InternalServerError, instance: ctx.TraceIdentifier);
                }
            })
            .RequireAuthorization("VerifiedDonator")
            .Accepts<AcceptRequest>("application/json")
            .WithOpenApi(op =>
            {
                op.OperationId = "Listings_PickupAccept";
                op.Summary = "Accept a recycler for pickup";
                op.Description = "Donator selects one of the applicants and accepts them for pickup.";
                return op;
            })
            .Produces(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized);

            // Start direct chat between donator and assigned recycler (restricted to those two users)
            group.MapPost("/chat/start", async (ChatStartRequest req, ClaimsPrincipal user, IRecycleListingService svc, ILoggerFactory lf, HttpContext ctx) =>
            {
                var logger = lf.CreateLogger("Listings");
                try
                {
                    var userId = user.FindFirstValue(ClaimTypes.NameIdentifier) ?? user.FindFirstValue("sub");
                    if (string.IsNullOrEmpty(userId)) return Results.Unauthorized();

                    var listing = await svc.GetByIdAsync(req.ListingId, ctx.RequestAborted);
                    if (listing is null)
                    {
                        logger.LogWarning("Listing {ListingId} not found for chat start", req.ListingId);
                        return Results.NotFound(new { error = "Listing not found" });
                    }
                    if (!listing.IsActive || listing.Status != ListingStatus.Accepted)
                    {
                        logger.LogWarning("Chat start invalid state for listing {ListingId}", req.ListingId);
                        return Results.Problem(title: "Chat unavailable", detail: "Listing is not in an accepted state.", statusCode: StatusCodes.Status400BadRequest, instance: ctx.TraceIdentifier);
                    }

                    var donatorId = listing.CreatedByUserId;
                    var recyclerId = listing.AssignedRecyclerUserId;
                    if (string.IsNullOrEmpty(recyclerId))
                    {
                        logger.LogWarning("No recycler assigned for listing {ListingId}", req.ListingId);
                        return Results.Problem(title: "Chat unavailable", detail: "No recycler assigned.", statusCode: StatusCodes.Status400BadRequest, instance: ctx.TraceIdentifier);
                    }

                    var isParticipant = string.Equals(userId, donatorId, StringComparison.Ordinal) || string.Equals(userId, recyclerId, StringComparison.Ordinal);
                    if (!isParticipant) return Results.Forbid();

                    var chatId = $"listing-{req.ListingId}";

                    var ok = await svc.StartChatAsync(req.ListingId, chatId, ctx.RequestAborted);
                    if (!ok)
                    {
                        logger.LogWarning("Failed to start chat for listing {ListingId}", req.ListingId);
                        return Results.Problem(title: "Chat start failed", detail: "Unable to start chat for this listing.", statusCode: StatusCodes.Status400BadRequest, instance: ctx.TraceIdentifier);
                    }
                    logger.LogInformation("Chat started for listing {ListingId}", req.ListingId);
                    return Results.Ok();
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Error starting chat for listing {ListingId}", req.ListingId);
                    return Results.Problem(title: "Chat start error", detail: ex.Message, statusCode: StatusCodes.Status500InternalServerError, instance: ctx.TraceIdentifier);
                }
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
            group.MapPost("/meeting/set", async (MeetingPointRequest req, ClaimsPrincipal user, IRecycleListingService svc, ILoggerFactory lf, HttpContext ctx) =>
            {
                var logger = lf.CreateLogger("Listings");
                try
                {
                    var userId = user.FindFirstValue(ClaimTypes.NameIdentifier) ?? user.FindFirstValue("sub");
                    if (string.IsNullOrEmpty(userId)) return Results.Unauthorized();
                    var ok = await svc.SetMeetingPointAsync(req.ListingId, userId, req.Latitude, req.Longitude, ctx.RequestAborted);
                    if (!ok)
                    {
                        logger.LogWarning("Set meeting point failed for listing {ListingId} by user {UserId}", req.ListingId, userId);
                        return Results.Problem(title: "Set meeting point failed", detail: "Invalid coordinates, listing state, or permissions.", statusCode: StatusCodes.Status400BadRequest, instance: ctx.TraceIdentifier);
                    }
                    logger.LogInformation("Meeting point set for listing {ListingId}", req.ListingId);
                    return Results.Ok();
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Error setting meeting point for listing {ListingId}", req.ListingId);
                    return Results.Problem(title: "Set meeting point error", detail: ex.Message, statusCode: StatusCodes.Status500InternalServerError, instance: ctx.TraceIdentifier);
                }
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

            // Donator confirms pickup (this now completes the listing)
            group.MapPost("/pickup/confirm", async (PickupConfirmRequest req, ClaimsPrincipal user, IRecycleListingService svc, ILoggerFactory lf, HttpContext ctx) =>
            {
                var logger = lf.CreateLogger("Listings");
                try
                {
                    var userId = user.FindFirstValue(ClaimTypes.NameIdentifier) ?? user.FindFirstValue("sub");
                    if (string.IsNullOrEmpty(userId)) return Results.Unauthorized();
                    var ok = await svc.ConfirmPickupAsync(req.ListingId, userId, ctx.RequestAborted);
                    if (!ok)
                    {
                        logger.LogWarning("Pickup confirm failed for listing {ListingId} by user {UserId}", req.ListingId, userId);
                        return Results.Problem(title: "Pickup confirm failed", detail: "Listing not accepted, chat/meeting not set, inactive, or user not owner.", statusCode: StatusCodes.Status400BadRequest, instance: ctx.TraceIdentifier);
                    }
                    logger.LogInformation("Pickup confirmed for listing {ListingId}", req.ListingId);
                    return Results.Ok();
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Error confirming pickup for listing {ListingId}", req.ListingId);
                    return Results.Problem(title: "Pickup confirm error", detail: ex.Message, statusCode: StatusCodes.Status500InternalServerError, instance: ctx.TraceIdentifier);
                }
            })
            .RequireAuthorization("VerifiedDonator")
            .Accepts<PickupConfirmRequest>("application/json")
            .WithOpenApi(op =>
            {
                op.OperationId = "Listings_PickupConfirm";
                op.Summary = "Confirm pickup and complete listing";
                op.Description = "Donator confirms that the pickup has been performed (after chat and meeting point). This completes the listing.";
                return op;
            })
            .Produces(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized);

            // Recycler uploads receipt image (binary upload)
            group.MapPost("/receipt/upload", async ([FromForm] int listingId, [FromForm] decimal reportedAmount, [FromForm] IFormFile file, ClaimsPrincipal user, IRecycleListingService svc, IAntivirusScanner av, ILoggerFactory lf, HttpContext ctx) =>
            {
                var logger = lf.CreateLogger("Listings");
                try
                {
                    var userId = user.FindFirstValue(ClaimTypes.NameIdentifier) ?? user.FindFirstValue("sub");
                    if (string.IsNullOrEmpty(userId)) return Results.Unauthorized();

                    if (file is null || file.Length == 0)
                    {
                        return Results.Problem(title: "Validation error", detail: "Receipt file is required", statusCode: StatusCodes.Status400BadRequest, instance: ctx.TraceIdentifier);
                    }
                    // Optional: basic content-type guard
                    if (!string.IsNullOrEmpty(file.ContentType) && !file.ContentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
                    {
                        return Results.Problem(title: "Validation error", detail: "Only image files are allowed", statusCode: StatusCodes.Status400BadRequest, instance: ctx.TraceIdentifier);
                    }

                    await using var ms = new MemoryStream();
                    await file.CopyToAsync(ms, ctx.RequestAborted);
                    ms.Position = 0;

                    // Antivirus scan
                    var scan = await av.ScanAsync(ms, file.FileName, ctx.RequestAborted);
                    if (scan.Status == AntivirusScanStatus.Infected)
                    {
                        logger.LogWarning("Infected file upload blocked for listing {ListingId} by user {UserId}. Signature: {Signature}", listingId, userId, scan.Signature);
                        return Results.Problem(title: "Malware detected", detail: $"The uploaded file appears to be infected: {scan.Signature}", statusCode: StatusCodes.Status400BadRequest, instance: ctx.TraceIdentifier);
                    }
                    if (scan.Status == AntivirusScanStatus.Error)
                    {
                        logger.LogError("Antivirus scan error for listing {ListingId} by user {UserId}: {Error}", listingId, userId, scan.Error);
                        return Results.Problem(title: "Scan failed", detail: "Unable to scan the uploaded file. Please try again later.", statusCode: StatusCodes.Status503ServiceUnavailable, instance: ctx.TraceIdentifier);
                    }

                    var data = ms.ToArray();

                    var ok = await svc.SubmitReceiptUploadAsync(listingId, userId, file.FileName, file.ContentType, data, reportedAmount, ctx.RequestAborted);
                    if (!ok)
                    {
                        logger.LogWarning("Receipt upload failed for listing {ListingId} by user {UserId}", listingId, userId);
                        return Results.Problem(title: "Receipt upload failed", detail: "Listing not found or you are not the assigned recycler.", statusCode: StatusCodes.Status400BadRequest, instance: ctx.TraceIdentifier);
                    }
                    logger.LogInformation("Receipt uploaded for listing {ListingId}", listingId);
                    return Results.Ok();
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Error uploading receipt for listing {ListingId}", listingId);
                    return Results.Problem(title: "Receipt upload error", detail: ex.Message, statusCode: StatusCodes.Status500InternalServerError, instance: ctx.TraceIdentifier);
                }
            })
            .RequireAuthorization()
            .DisableAntiforgery()
            .WithOpenApi(op =>
            {
                op.OperationId = "Listings_ReceiptUpload";
                op.Summary = "Upload receipt image";
                op.Description = "Recycler uploads the receipt image as multipart/form-data with fields: listingId, reportedAmount, file. This does not affect listing status and is available even after completion.";
                return op;
            })
            .Produces(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized);

            // Donator cancels listing
            group.MapPost("/cancel", async (CancelRequest req, ClaimsPrincipal user, IRecycleListingService svc, ILoggerFactory lf, HttpContext ctx) =>
            {
                var logger = lf.CreateLogger("Listings");
                try
                {
                    var userId = user.FindFirstValue(ClaimTypes.NameIdentifier) ?? user.FindFirstValue("sub");
                    if (string.IsNullOrEmpty(userId)) return Results.Unauthorized();
                    var ok = await svc.CancelAsync(req.ListingId, userId, ctx.RequestAborted);
                    if (!ok)
                    {
                        logger.LogWarning("Cancel failed for listing {ListingId} by user {UserId}", req.ListingId, userId);
                        return Results.Problem(title: "Cancel failed", detail: "Listing cannot be cancelled in its current state or you are not the owner.", statusCode: StatusCodes.Status400BadRequest, instance: ctx.TraceIdentifier);
                    }
                    logger.LogInformation("Listing {ListingId} cancelled by user {UserId}", req.ListingId, userId);
                    return Results.Ok();
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Error cancelling listing {ListingId}", req.ListingId);
                    return Results.Problem(title: "Cancel error", detail: ex.Message, statusCode: StatusCodes.Status500InternalServerError, instance: ctx.TraceIdentifier);
                }
            })
            .RequireAuthorization("VerifiedDonator")
            .Accepts<CancelRequest>("application/json")
            .WithOpenApi(op =>
            {
                op.OperationId = "Listings_Cancel";
                op.Summary = "Cancel a listing";
                op.Description = "Donator cancels their own listing if not already completed or cancelled.";
                return op;
            })
            .Produces(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized);

            return app;
        }
    }
}
