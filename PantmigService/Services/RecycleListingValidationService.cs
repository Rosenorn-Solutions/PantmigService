namespace PantmigService.Services;

public class RecycleListingValidationService : IRecycleListingValidationService
{
    private const int MaxQuantity = 10_000;

    public ValidationResult<CreateListingValidated> ValidateCreate(
        string? title,
        string? description,
        string? city,
        string? location,
        DateOnly availableFrom,
        DateOnly availableTo,
        decimal? latitude,
        decimal? longitude,
        List<CreateListingItemInput>? items)
    {
        // Title required; description optional
        if (string.IsNullOrWhiteSpace(title))
            return ValidationResult<CreateListingValidated>.Failure("Validation error", "Title is required", StatusCodes.Status400BadRequest);

        if (string.IsNullOrWhiteSpace(location))
            return ValidationResult<CreateListingValidated>.Failure("Validation error", "Location is required", StatusCodes.Status400BadRequest);

        // With external-id now provided separately we no longer fall back to location text as city input.
        // City name is optional when a cityExternalId is provided upstream; we validate later when mapping resolver.
        var cityInput = city; // remove location fallback

        if (string.IsNullOrWhiteSpace(cityInput))
        {
            // Allow create when cityExternalId flows; caller will detect absence of both cityExternalId and coordinates.
            // We return success with empty cityInput so endpoint can branch.
            cityInput = string.Empty;
        }

        if (availableTo <= availableFrom)
            return ValidationResult<CreateListingValidated>.Failure("Validation error", "AvailableTo must be after AvailableFrom", StatusCodes.Status400BadRequest);

        // Validate coordinates if provided: both must be present and within range
        var hasLat = latitude.HasValue;
        var hasLon = longitude.HasValue;
        if (hasLat ^ hasLon)
            return ValidationResult<CreateListingValidated>.Failure("Validation error", "Both latitude and longitude must be supplied together", StatusCodes.Status400BadRequest);
        if (hasLat && hasLon)
        {
            if (latitude is < -90 or > 90)
                return ValidationResult<CreateListingValidated>.Failure("Validation error", "Latitude must be between -90 and 90", StatusCodes.Status400BadRequest);
            if (longitude is < -180 or > 180)
                return ValidationResult<CreateListingValidated>.Failure("Validation error", "Longitude must be between -180 and 180", StatusCodes.Status400BadRequest);
        }

        if (items is null || items.Count == 0)
            return ValidationResult<CreateListingValidated>.Failure("Validation error", "At least one item is required", StatusCodes.Status400BadRequest);

        foreach (var it in items)
        {
            if (it.Quantity <= 0)
                return ValidationResult<CreateListingValidated>.Failure("Validation error", "All item quantities must be greater than zero", StatusCodes.Status400BadRequest);
            if (it.Quantity > MaxQuantity)
                return ValidationResult<CreateListingValidated>.Failure("Validation error", "Item quantity too large", StatusCodes.Status400BadRequest);
        }

        decimal? estimatedValue = null;
        if (items.Any(i => i.EstimatedDepositPerUnit.HasValue))
        {
            decimal sum = 0;
            foreach (var i in items)
            {
                if (i.EstimatedDepositPerUnit.HasValue)
                    sum += i.EstimatedDepositPerUnit.Value * i.Quantity;
            }
            estimatedValue = sum;
        }

        var sanitizedTitle = title!.Trim();
        var sanitizedDescription = string.IsNullOrWhiteSpace(description) ? string.Empty : description!.Trim();
        var sanitizedCity = cityInput.Trim();

        return ValidationResult<CreateListingValidated>.Success(new CreateListingValidated(
            sanitizedTitle,
            sanitizedDescription,
            sanitizedCity,
            location,
            availableFrom,
            availableTo,
            latitude,
            longitude,
            items,
            estimatedValue));
    }

    public ValidationResult<object> ValidateReceiptUpload(IFormFile? file)
    {
        if (file is null || file.Length == 0)
            return ValidationResult<object>.Failure("Validation error", "Receipt file is required", StatusCodes.Status400BadRequest);

        if (!IsImage(file.ContentType))
            return ValidationResult<object>.Failure("Validation error", "Only image files are allowed", StatusCodes.Status400BadRequest);

        return ValidationResult<object>.Success(new object());
    }

    public bool IsImage(string? contentType) => !string.IsNullOrEmpty(contentType) && contentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase);
}
