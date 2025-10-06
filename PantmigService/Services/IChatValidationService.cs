using PantmigService.Entities;

namespace PantmigService.Services;

public interface IChatValidationService
{
    ValidationResult<string> ValidateStartChat(RecycleListing listing, string userId);
}
