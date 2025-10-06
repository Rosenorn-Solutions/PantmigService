using System.Text.Json;
using PantmigService.Services;
using PantmigService.Entities;
using Microsoft.Extensions.Logging;
using PantmigService.Security;

namespace PantmigService.Endpoints.Helpers;

public class CreateListingRequestParser : ICreateListingRequestParser
{
    private readonly IRecycleListingValidationService _validator;
    private readonly IAntivirusScanner _antivirus;
    private readonly ILogger<CreateListingRequestParser> _logger;

    public CreateListingRequestParser(IRecycleListingValidationService validator, IAntivirusScanner antivirus, ILogger<CreateListingRequestParser> logger)
    {
        _validator = validator;
        _antivirus = antivirus;
        _logger = logger;
    }

    public async Task<ParseCreateListingResult> ParseAsync(HttpRequest request, CancellationToken ct)
    {
        if (request.HasFormContentType)
        {
            var form = await request.ReadFormAsync(ct);
            string? title = form["Title"].FirstOrDefault();
            string? description = form["Description"].FirstOrDefault();
            string? city = form["City"].FirstOrDefault();
            string? location = form["Location"].FirstOrDefault();
            DateTime.TryParse(form["AvailableFrom"].FirstOrDefault(), out var availableFrom);
            DateTime.TryParse(form["AvailableTo"].FirstOrDefault(), out var availableTo);

            List<RecycleListingEndpoints.CreateRecycleListingItemRequest>? rawItems = null;
            var itemsJson = form["Items"].FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(itemsJson))
            {
                try
                {
                    rawItems = JsonSerializer.Deserialize<List<RecycleListingEndpoints.CreateRecycleListingItemRequest>>(itemsJson);
                }
                catch
                {
                    return new ParseCreateListingResult { Problem = new ValidationProblem("Validation error", "Items JSON malformed", StatusCodes.Status400BadRequest) };
                }
            }

            var images = new List<RecycleListingImage>();
            int order = 0;
            foreach (var file in form.Files.Where(f => f.Name == "images"))
            {
                if (file.Length == 0) continue;
                if (!_validator.IsImage(file.ContentType))
                {
                    return new ParseCreateListingResult { Problem = new ValidationProblem("Validation error", "All uploaded files must be images", StatusCodes.Status400BadRequest) };
                }
                await using var ms = new MemoryStream();
                await file.CopyToAsync(ms, ct);
                ms.Position = 0;
                var scan = await _antivirus.ScanAsync(ms, file.FileName, ct);
                if (scan.Status == AntivirusScanStatus.Infected)
                {
                    _logger.LogWarning("Infected image blocked in create listing. Signature: {Signature}", scan.Signature);
                    return new ParseCreateListingResult { Problem = new ValidationProblem("Malware detected", $"Uploaded image appears infected: {scan.Signature}", StatusCodes.Status400BadRequest) };
                }
                if (scan.Status == AntivirusScanStatus.Error)
                {
                    _logger.LogError("Antivirus scan error during listing create: {Error}", scan.Error);
                    return new ParseCreateListingResult { Problem = new ValidationProblem("Scan failed", "Unable to scan one of the images.", StatusCodes.Status503ServiceUnavailable) };
                }
                images.Add(new RecycleListingImage
                {
                    Data = ms.ToArray(),
                    ContentType = file.ContentType ?? "application/octet-stream",
                    FileName = file.FileName,
                    Order = order++
                });
            }

            return new ParseCreateListingResult
            {
                Title = title,
                Description = description,
                City = city,
                Location = location,
                AvailableFrom = availableFrom,
                AvailableTo = availableTo,
                RawItems = rawItems,
                Images = images
            };
        }
        else
        {
            try
            {
                var body = await request.ReadFromJsonAsync<RecycleListingEndpoints.CreateRecycleListingRequest>(cancellationToken: ct);
                if (body is null)
                {
                    return new ParseCreateListingResult { Problem = new ValidationProblem("Validation error", "Invalid request body", StatusCodes.Status400BadRequest) };
                }
                return new ParseCreateListingResult
                {
                    Title = body.Title,
                    Description = body.Description,
                    City = body.City,
                    Location = body.Location,
                    AvailableFrom = body.AvailableFrom,
                    AvailableTo = body.AvailableTo,
                    RawItems = body.Items
                };
            }
            catch (JsonException)
            {
                return new ParseCreateListingResult { Problem = new ValidationProblem("Validation error", "Invalid JSON", StatusCodes.Status400BadRequest) };
            }
        }
    }
}
