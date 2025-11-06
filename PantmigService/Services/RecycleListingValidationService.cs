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
        if (string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(description))
            return ValidationResult<CreateListingValidated>.Failure("Validation error", "Title and Description are required", StatusCodes.Status400BadRequest);

        var cityInput = city ?? location;
        if (string.IsNullOrWhiteSpace(cityInput))
            return ValidationResult<CreateListingValidated>.Failure("Validation error", "City or Location is required", StatusCodes.Status400BadRequest);

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

        return ValidationResult<CreateListingValidated>.Success(new CreateListingValidated(
            title.Trim(),
            description.Trim(),
            cityInput.Trim(),
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
