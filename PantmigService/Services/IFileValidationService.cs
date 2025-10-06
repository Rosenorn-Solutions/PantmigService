using Microsoft.AspNetCore.Http;

namespace PantmigService.Services;

public interface IFileValidationService
{
    ValidationResult<object> ValidateImage(IFormFile? file, string requiredName = "file");
}
