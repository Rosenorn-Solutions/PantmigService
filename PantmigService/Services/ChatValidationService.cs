using PantmigService.Entities;

namespace PantmigService.Services;

public class ChatValidationService : IChatValidationService
{
    public ValidationResult<string> ValidateStartChat(RecycleListing listing, string userId)
    {
        if (listing is null)
            return ValidationResult<string>.Failure("Chat unavailable", "Listing not found", StatusCodes.Status404NotFound);

        if (!listing.IsActive || listing.Status != ListingStatus.Accepted)
            return ValidationResult<string>.Failure("Chat unavailable", "Listing is not in an accepted state.", StatusCodes.Status400BadRequest);

        if (string.IsNullOrEmpty(listing.AssignedRecyclerUserId))
            return ValidationResult<string>.Failure("Chat unavailable", "No recycler assigned.", StatusCodes.Status400BadRequest);

        var donatorId = listing.CreatedByUserId;
        var recyclerId = listing.AssignedRecyclerUserId;
        var isParticipant = string.Equals(userId, donatorId, StringComparison.Ordinal) || string.Equals(userId, recyclerId, StringComparison.Ordinal);
        if (!isParticipant)
            return ValidationResult<string>.Failure("Forbidden", "You are not a participant in this listing", StatusCodes.Status403Forbidden);

        var chatId = $"listing-{listing.Id}";
        return ValidationResult<string>.Success(chatId);
    }
}
