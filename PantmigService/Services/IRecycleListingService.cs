using PantmigService.Entities;

namespace PantmigService.Services
{
    public interface IRecycleListingService
    {
        Task<RecycleListing> CreateAsync(RecycleListing listing, CancellationToken ct = default);
        Task<RecycleListing?> GetByIdAsync(int id, CancellationToken ct = default);
        Task<IEnumerable<RecycleListing>> GetActiveAsync(CancellationToken ct = default);

        Task<bool> RequestPickupAsync(int id, string recyclerUserId, CancellationToken ct = default);
        Task<bool> AcceptPickupAsync(int id, string donatorUserId, CancellationToken ct = default);
        Task<bool> StartChatAsync(int id, string chatSessionId, CancellationToken ct = default);
        Task<bool> ConfirmPickupAsync(int id, string recyclerUserId, CancellationToken ct = default);
        Task<bool> SubmitReceiptAsync(int id, string recyclerUserId, string receiptImageUrl, decimal reportedAmount, CancellationToken ct = default);
        Task<bool> VerifyReceiptAsync(int id, string donatorUserId, decimal verifiedAmount, CancellationToken ct = default);
        Task<bool> SetMeetingPointAsync(int id, string donatorUserId, decimal latitude, decimal longitude, CancellationToken ct = default);
    }
}
