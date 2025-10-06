using Microsoft.AspNetCore.Http;

namespace PantmigService.Services;

public class FileValidationService(IRecycleListingValidationService listingValidator) : IFileValidationService
{
    private readonly IRecycleListingValidationService _listingValidator = listingValidator;

    public ValidationResult<object> ValidateImage(IFormFile? file, string requiredName = "file")
    {
        if (file is null || file.Length == 0)
            return ValidationResult<object>.Failure("Validation error", $"{requiredName} is required", StatusCodes.Status400BadRequest);
        if (!_listingValidator.IsImage(file.ContentType))
            return ValidationResult<object>.Failure("Validation error", "Only image files are allowed", StatusCodes.Status400BadRequest);
        return ValidationResult<object>.Success(new object());
    }
}
