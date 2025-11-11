using Microsoft.AspNetCore.Mvc;
using Microsoft.OpenApi.Models;
using PantmigService.Endpoints.Helpers;
using PantmigService.Entities;
using PantmigService.Security;
using PantmigService.Services;
using PantmigService.Utils; // added for PagedResult
using PantmigService.Utils.Extensions;
using System.Security.Claims;

namespace PantmigService.Endpoints
{
    public static class RecycleListingEndpoints
    {
        public record CreateRecycleListingItemRequest(RecycleMaterialType Type, int Quantity, string? DepositClass, decimal? EstimatedDepositPerUnit);
        public record CreateRecycleListingRequest(string Title, string Description, Guid? CityExternalId, string? City, string? Location, DateOnly AvailableFrom, DateOnly AvailableTo, decimal? Latitude, decimal? Longitude, List<CreateRecycleListingItemRequest> Items);
        public record PickupRequest(int ListingId);
        public record AcceptRequest(int ListingId, string RecyclerUserId);
        public record ChatStartRequest(int ListingId);
        public record PickupConfirmRequest(int ListingId);
        public record MeetingPointRequest(int ListingId, decimal Latitude, decimal Longitude);
        public record CancelRequest(int ListingId);
        public record SearchRequest(Guid CityExternalId, bool OnlyActive = true);
        public record PagedSearchRequest<T>(Guid? CityExternalId, int Page =1, int PageSize =20, bool OnlyActive = true, decimal? Latitude = null, decimal? Longitude = null);

        public static IEndpointRouteBuilder MapRecycleListingEndpoints(this IEndpointRouteBuilder app)
        {
            var group = app.MapGroup("/listings")
            .WithTags("Recycle Listings");

            // Updated: GET / with pagination via query
            group.MapGet("/", async ([FromQuery] int? page, [FromQuery] int? pageSize, IRecycleListingService svc, ILoggerFactory lf, HttpContext ctx) =>
            {
                var logger = lf.CreateLogger("Listings");
                try
                {
                    var p = page.GetValueOrDefault(1);
                    var ps = pageSize.GetValueOrDefault(20);
                    if (p <=0) p =1;
                    if (ps <=0) ps =20;
                    if (ps >100) ps =100;
                    var data = await svc.GetActivePagedAsync(p, ps, ctx.RequestAborted);
                    var dto = data.Map(l => l.ToResponse());
                    return Results.Ok(dto);
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
                op.Summary = "Get active recycle listings (paged)";
                op.Description = "Returns active listings, paginated via page and pageSize query params. Defaults: page=1, pageSize=20 (max100).";
                op.Parameters =
     [
     new OpenApiParameter { Name = "page", In = ParameterLocation.Query, Required = false, Description = "Page number (1-based)", Schema = new OpenApiSchema { Type = "integer", Default = new Microsoft.OpenApi.Any.OpenApiInteger(1) } },
 new OpenApiParameter { Name = "pageSize", In = ParameterLocation.Query, Required = false, Description = "Page size (max100)", Schema = new OpenApiSchema { Type = "integer", Default = new Microsoft.OpenApi.Any.OpenApiInteger(20) } }
     ];
                return op;
            })
            .RequireAuthorization()
            .Produces<PagedResult<RecycleListingResponse>>(StatusCodes.Status200OK, contentType: "application/json");

            // Updated search endpoint: supports optional cityExternalId and/or coordinates
            group.MapPost("/search", async (PagedSearchRequest<object> req, ClaimsPrincipal user, IRecycleListingService svc, ILoggerFactory lf, HttpContext ctx, ICityResolver cityResolver) =>
            {
                var logger = lf.CreateLogger("Listings");
                try
                {
                    if (!req.CityExternalId.HasValue && (!req.Latitude.HasValue || !req.Longitude.HasValue))
                    {
                        return Results.Problem(title: "Invalid search", detail: "Provide either cityExternalId or both latitude and longitude.", statusCode: StatusCodes.Status400BadRequest, instance: ctx.TraceIdentifier);
                    }

                    if ((req.Latitude.HasValue && !req.Longitude.HasValue) || (!req.Latitude.HasValue && req.Longitude.HasValue))
                    {
                        return Results.Problem(title: "Invalid search", detail: "Both latitude and longitude must be provided for coordinate search.", statusCode: StatusCodes.Status400BadRequest, instance: ctx.TraceIdentifier);
                    }

                    if (req.Latitude.HasValue && (req.Latitude < -90 || req.Latitude >90))
                    {
                        return Results.Problem(title: "Invalid search", detail: "Latitude must be between -90 and90.", statusCode: StatusCodes.Status400BadRequest, instance: ctx.TraceIdentifier);
                    }
                    if (req.Longitude.HasValue && (req.Longitude < -180 || req.Longitude >180))
                    {
                        return Results.Problem(title: "Invalid search", detail: "Longitude must be between -180 and180.", statusCode: StatusCodes.Status400BadRequest, instance: ctx.TraceIdentifier);
                    }

                    var userId = user.FindFirstValue(ClaimTypes.NameIdentifier) ?? user.FindFirstValue("sub");
                    if (string.IsNullOrEmpty(userId))
                    {
                        return Results.Unauthorized();
                    }

                    var pageVal = req.Page <=0 ?1 : req.Page;
                    var pageSizeVal = req.PageSize <=0 ?20 : Math.Min(req.PageSize,100);

                    int? cityId = null;
                    if (req.CityExternalId.HasValue)
                    {
                        try { cityId = await cityResolver.ResolveByExternalIdAsync(req.CityExternalId.Value, ctx.RequestAborted); }
                        catch (Exception ex)
                        {
                            logger.LogWarning(ex, "Failed to resolve city by external id: {CityExternalId}", req.CityExternalId);
                            return Results.Problem(title: "Invalid search", detail: "Unknown city.", statusCode: StatusCodes.Status400BadRequest, instance: ctx.TraceIdentifier);
                        }
                    }

                    var result = await svc.SearchAsync(cityId, userId, pageVal, pageSizeVal, req.OnlyActive, req.Latitude, req.Longitude, ctx.RequestAborted);
                    var mapped = result.Map(l => l.ToResponse());
                    return Results.Ok(mapped);
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Failed to search listings");
                    return Results.Problem(title: "Failed to search listings", detail: ex.Message, statusCode: StatusCodes.Status500InternalServerError, instance: ctx.TraceIdentifier);
                }
            })
            .RequireAuthorization()
            .Accepts<PagedSearchRequest<object>>("application/json")
            .WithOpenApi(op =>
            {
                op.OperationId = "Listings_Search";
                op.Summary = "Search listings";
                op.Description = "Search for listings with pagination. Filters: cityExternalId (optional) and/or coordinates (latitude+longitude within5km). Results exclude listings that the current user has already applied for.";
                op.RequestBody = new OpenApiRequestBody
                {
                    Required = true,
                    Content =
                {
                     ["application/json"] = new OpenApiMediaType
                     {
                        Schema = new OpenApiSchema
                     {
                     Type = "object",
                        Properties =
                        {
                             [nameof(PagedSearchRequest<object>.CityExternalId)] = new OpenApiSchema { Type = "string", Format = "uuid", Nullable = true, Description = "City external identifier (optional)" },
                             [nameof(PagedSearchRequest<object>.OnlyActive)] = new OpenApiSchema { Type = "boolean", Description = "If true, returns only active listings in Created or PendingAcceptance states.", Default = new Microsoft.OpenApi.Any.OpenApiBoolean(true) },
                             [nameof(PagedSearchRequest<object>.Page)] = new OpenApiSchema { Type = "integer", Format = "int32", Description = "Page number (1-based)", Default = new Microsoft.OpenApi.Any.OpenApiInteger(1) },
                             [nameof(PagedSearchRequest<object>.PageSize)] = new OpenApiSchema { Type = "integer", Format = "int32", Description = "Page size (max100)", Default = new Microsoft.OpenApi.Any.OpenApiInteger(20) },
                             [nameof(PagedSearchRequest<object>.Latitude)] = new OpenApiSchema { Type = "number", Format = "decimal", Nullable = true, Description = "Latitude for coordinate search (-90..90)" },
                             [nameof(PagedSearchRequest<object>.Longitude)] = new OpenApiSchema { Type = "number", Format = "decimal", Nullable = true, Description = "Longitude for coordinate search (-180..180)" }
                        }
                     }
                     }
                }
                };
                return op;
            })
            .Produces<PagedResult<RecycleListingResponse>>(StatusCodes.Status200OK, contentType: "application/json")
            .Produces(StatusCodes.Status400BadRequest)
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
                    return Results.Ok(item.ToResponse());
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
            .Produces<RecycleListingResponse>(StatusCodes.Status200OK, contentType: "application/json")
            .Produces(StatusCodes.Status404NotFound);

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
                    return Results.Ok(items.ToResponse());
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
            .Produces<IEnumerable<RecycleListingResponse>>(StatusCodes.Status200OK, contentType: "application/json")
            .Produces(StatusCodes.Status401Unauthorized);

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
                    // Returns all listings for this user regardless of status; front-end filters as needed.
                    var items = await svc.GetByUserAsync(userId, ctx.RequestAborted);
                    return Results.Ok(items.ToResponse());
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
            .Produces<IEnumerable<RecycleListingResponse>>(StatusCodes.Status200OK, contentType: "application/json")
            .Produces(StatusCodes.Status401Unauthorized);

            group.MapPost("/", async (HttpRequest httpRequest, ClaimsPrincipal user, IRecycleListingService svc, IRecycleListingValidationService validator, ICreateListingRequestParser parser, ICityResolver cityResolver, ILoggerFactory lf, HttpContext ctx) =>
            {
                var logger = lf.CreateLogger("Listings");
                try
                {
                    var userId = user.GetUserId();
                    if (string.IsNullOrEmpty(userId)) return Results.Unauthorized();

                    var parseResult = await parser.ParseAsync(httpRequest, ctx.RequestAborted);
                    if (!parseResult.IsSuccess)
                    {
                        var p = parseResult.Problem!;
                        return Results.Problem(title: p.Title, detail: p.Detail, statusCode: p.StatusCode, instance: ctx.TraceIdentifier);
                    }

                    var rawItems = parseResult.RawItems;
                    var itemInputs = rawItems?.Select(i => new CreateListingItemInput(i.Type, i.Quantity, i.DepositClass, i.EstimatedDepositPerUnit)).ToList();
                    var validation = validator.ValidateCreate(parseResult.Title, parseResult.Description, parseResult.City, parseResult.Location, parseResult.AvailableFrom, parseResult.AvailableTo, parseResult.Latitude, parseResult.Longitude, itemInputs);
                    if (!validation.IsValid)
                    {
                        var vp = validation.Problem!;
                        return Results.Problem(title: vp.Title, detail: vp.Detail, statusCode: vp.StatusCode, instance: ctx.TraceIdentifier);
                    }

                    var v = validation.Value!;
                    int cityId;
                    if (Guid.TryParse(httpRequest.Query["cityExternalId"], out var cityExtFromQuery))
                    {
                        cityId = await cityResolver.ResolveByExternalIdAsync(cityExtFromQuery, ctx.RequestAborted);
                    }
                    else if (Guid.TryParse(httpRequest.Headers["X-City-ExternalId"], out var cityExtFromHeader))
                    {
                        cityId = await cityResolver.ResolveByExternalIdAsync(cityExtFromHeader, ctx.RequestAborted);
                    }
                    else if (httpRequest.HasFormContentType && Guid.TryParse(httpRequest.Form["CityExternalId"], out var cityExtFromForm))
                    {
                        cityId = await cityResolver.ResolveByExternalIdAsync(cityExtFromForm, ctx.RequestAborted);
                    }
                    else
                    {
                        cityId = await cityResolver.ResolveOrCreateAsync(v.CityInput, ctx.RequestAborted);
                    }

                    var listing = new RecycleListing
                    {
                        Title = v.Title,
                        Description = v.Description,
                        AvailableFrom = v.AvailableFrom,
                        AvailableTo = v.AvailableTo,
                        CreatedByUserId = userId,
                        CreatedAt = DateTime.UtcNow,
                        IsActive = true,
                        Status = ListingStatus.Created,
                        CityId = cityId,
                        Items = [.. v.Items.Select(i => new RecycleListingItem
                        {
                             MaterialType = i.Type,
                             Quantity = i.Quantity,
                             DepositClass = i.DepositClass,
                             EstimatedDepositPerUnit = i.EstimatedDepositPerUnit
                        })],
                        Images = parseResult.Images,
                        MeetingLatitude = v.Latitude,
                        MeetingLongitude = v.Longitude,
                        MeetingSetAt = (v.Latitude.HasValue && v.Longitude.HasValue) ? DateTime.UtcNow : null
                    };

                    var created = await svc.CreateAsync(listing, ctx.RequestAborted);
                    logger.LogInformation("Listing {ListingId} created by {UserId} with {ImageCount} images", created.Id, userId, parseResult.Images.Count);
                    return Results.Created($"/listings/{created.Id}", created.ToResponse());
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Failed to create listing for user");
                    return Results.Problem(title: "Failed to create listing", detail: ex.Message, statusCode: StatusCodes.Status500InternalServerError, instance: ctx.TraceIdentifier);
                }
            })
            .RequireAuthorization("VerifiedDonator")
            .Accepts<CreateRecycleListingRequest>("application/json", "multipart/form-data")
            .WithOpenApi(op =>
            {
                op.OperationId = "Listings_Create";
                op.Summary = "Create a new listing";
                op.Description = "Creates a new recycle listing with structured item contents. Supports either JSON body (application/json) or multipart/form-data (fields: title, description, city, availableFrom, availableTo, optional latitude/longitude, items as JSON string, images as image/*). Requires a verified Donator.";

                op.RequestBody = new OpenApiRequestBody
                {
                    Required = true,
                    Content =
                    {
                         ["application/json"] = new OpenApiMediaType
                         {
                            Schema = new OpenApiSchema
                         {
                            Reference = new OpenApiReference
                            {
                                Type = ReferenceType.Schema,
                                Id = nameof(CreateRecycleListingRequest)
                            }
                         }
                         },
                             ["multipart/form-data"] = new OpenApiMediaType
                                {
                                    Schema = new OpenApiSchema
                                {
                             Type = "object",
                             Required = { "title", "description", "city", "availableFrom", "availableTo" },
                                 Properties =
                                 {
                                 ["title"] = new OpenApiSchema { Type = "string" },
                                 ["description"] = new OpenApiSchema { Type = "string" },
                                 ["cityExternalId"] = new OpenApiSchema { Type = "string", Format = "uuid", Nullable = true },
                                 ["city"] = new OpenApiSchema { Type = "string" },
                                 ["location"] = new OpenApiSchema { Type = "string", Nullable = true },
                                 ["availableFrom"] = new OpenApiSchema { Type = "string", Format = "date" },
                                 ["availableTo"] = new OpenApiSchema { Type = "string", Format = "date" },
                                 ["latitude"] = new OpenApiSchema { Type = "number", Format = "decimal", Nullable = true, Description = "Initial meeting point latitude (-90..90)" },
                                 ["longitude"] = new OpenApiSchema { Type = "number", Format = "decimal", Nullable = true, Description = "Initial meeting point longitude (-180..180)" },
                                 ["items"] = new OpenApiSchema
                                 {
                                 Description = "JSON array of items example: [{\"type\":1,\"quantity\":10}]",
                                 Type = "string"
                                 },
                             ["images"] = new OpenApiSchema
                             {
                             Type = "array",
                             Items = new OpenApiSchema { Type = "string", Format = "binary" },
                             Description = "Zero or more images (PNG/JPEG). Max6 images, each <=5 MB."
                             }
                             }
                             }
                         }
                    }
                };
                return op;
            })
            .WithMetadata(new RequestSizeLimitAttribute(64L *1024 *1024))
            .Produces<RecycleListingResponse>(StatusCodes.Status201Created, contentType: "application/json")
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized);

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
                    var result = list ?? [];
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

            group.MapPost("/chat/start", async (ChatStartRequest req, ClaimsPrincipal user, IRecycleListingService svc, IChatValidationService chatValidator, ILoggerFactory lf, HttpContext ctx) =>
            {
                var logger = lf.CreateLogger("Listings");
                try
                {
                    var userId = user.GetUserId();
                    if (string.IsNullOrEmpty(userId)) return Results.Unauthorized();

                    var listing = await svc.GetByIdAsync(req.ListingId, ctx.RequestAborted);
                    if (listing is null)
                    {
                        return Results.NotFound(new { error = "Listing not found" });
                    }

                    var chatValidation = chatValidator.ValidateStartChat(listing, userId);
                    if (!chatValidation.IsValid)
                    {
                        var p = chatValidation.Problem!;
                        if (p.StatusCode == StatusCodes.Status404NotFound)
                            return Results.NotFound(new { error = p.Detail });
                        if (p.StatusCode == StatusCodes.Status403Forbidden)
                            return Results.Forbid();
                        return p.ToProblemResult(ctx);
                    }

                    var ok = await svc.StartChatAsync(req.ListingId, chatValidation.Value!, ctx.RequestAborted);
                    if (!ok)
                    {
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

            group.MapPost("/receipt/upload", async ([FromForm] int listingId, [FromForm] decimal reportedAmount, [FromForm] IFormFile file, ClaimsPrincipal user, IRecycleListingService svc, IFileValidationService fileValidator, IAntivirusScanner av, ILoggerFactory lf, HttpContext ctx) =>
            {
                var logger = lf.CreateLogger("Listings");
                try
                {
                    var userId = user.GetUserId();
                    if (string.IsNullOrEmpty(userId)) return Results.Unauthorized();

                    var fileValidation = fileValidator.ValidateImage(file, "file");
                    if (!fileValidation.IsValid)
                    {
                        return fileValidation.Problem!.ToProblemResult(ctx);
                    }

                    await using var ms = new MemoryStream();
                    await file.CopyToAsync(ms, ctx.RequestAborted);
                    ms.Position = 0;

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
            .WithMetadata(new RequestSizeLimitAttribute(64L * 1024 * 1024))
            .Produces(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized);

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

            group.MapGet("/{id:int}/receipt", async (int id, ClaimsPrincipal user, IRecycleListingService svc, ILoggerFactory lf, HttpContext ctx) =>
            {
                var logger = lf.CreateLogger("Listings");
                try
                {
                    var userId = user.FindFirstValue(ClaimTypes.NameIdentifier) ?? user.FindFirstValue("sub");
                    if (string.IsNullOrEmpty(userId)) return Results.Unauthorized();

                    var item = await svc.GetByIdAsync(id, ctx.RequestAborted);
                    if (item is null) return Results.NotFound();

                    // Authorization: only allow listing owner or assigned recycler to download
                    var isOwner = string.Equals(item.CreatedByUserId, userId, StringComparison.Ordinal);
                    var isAssignedRecycler = string.Equals(item.AssignedRecyclerUserId, userId, StringComparison.Ordinal);
                    if (!isOwner && !isAssignedRecycler)
                    {
                        return Results.Forbid();
                    }

                    if (item.ReceiptImageBytes is null || item.ReceiptImageBytes.Length == 0)
                    {
                        return Results.NotFound();
                    }

                    var contentType = item.ReceiptImageContentType ?? "application/octet-stream";
                    var fileName = item.ReceiptImageFileName ?? $"receipt-{id}";
                    return Results.File(item.ReceiptImageBytes, contentType, fileName);
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Failed to get receipt for listing {ListingId}", id);
                    return Results.Problem(title: "Failed to get receipt", detail: ex.Message, statusCode: StatusCodes.Status500InternalServerError, instance: ctx.TraceIdentifier);
                }
            })
            .RequireAuthorization()
            .WithOpenApi(op =>
            {
                op.OperationId = "Listings_GetReceipt";
                op.Summary = "Download receipt image for a listing";
                op.Description = "Returns the raw receipt image bytes with correct content-type. Only the listing owner or assigned recycler may download.";
                return op;
            })
            .Produces(StatusCodes.Status200OK, contentType: "application/octet-stream")
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status404NotFound);

            return app;
        }
    }
}
