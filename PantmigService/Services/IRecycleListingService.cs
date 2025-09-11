using PantmigService.Entities;

namespace PantmigService.Services
{
    // Lightweight DTO for applicants list - id and appliedAt
    public record ApplicantInfo(string Id, DateTime AppliedAt);

    public interface IRecycleListingService
    {
        Task<RecycleListing> CreateAsync(RecycleListing listing, CancellationToken ct = default);
        Task<RecycleListing?> GetByIdAsync(int id, CancellationToken ct = default);
        Task<IEnumerable<RecycleListing>> GetActiveAsync(CancellationToken ct = default);
        Task<IEnumerable<RecycleListing>> GetByUserAsync(string userId, CancellationToken ct = default);
        Task<IEnumerable<RecycleListing>> GetAppliedByRecyclerAsync(string recyclerUserId, CancellationToken ct = default);

        Task<bool> RequestPickupAsync(int id, string recyclerUserId, CancellationToken ct = default);
        Task<bool> AcceptPickupAsync(int id, string donatorUserId, string recyclerUserId, CancellationToken ct = default);
        Task<IReadOnlyList<ApplicantInfo>?> GetApplicantsAsync(int id, string donatorUserId, CancellationToken ct = default);
        Task<bool> StartChatAsync(int id, string chatSessionId, CancellationToken ct = default);
        Task<bool> ConfirmPickupAsync(int id, string recyclerUserId, CancellationToken ct = default);
        Task<bool> SubmitReceiptAsync(int id, string recyclerUserId, string receiptImageUrl, decimal reportedAmount, CancellationToken ct = default);
        Task<bool> VerifyReceiptAsync(int id, string donatorUserId, decimal verifiedAmount, CancellationToken ct = default);
        Task<bool> SetMeetingPointAsync(int id, string donatorUserId, decimal latitude, decimal longitude, CancellationToken ct = default);
        Task<bool> CancelAsync(int id, string donatorUserId, CancellationToken ct = default);
    }
}
