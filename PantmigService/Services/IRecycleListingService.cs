using PantmigService.Entities;
using PantmigService.Utils;

namespace PantmigService.Services
{
    public record ApplicantInfo(string Id, DateTime AppliedAt);

    public interface IRecycleListingService
    {
        Task<RecycleListing> CreateAsync(RecycleListing listing, CancellationToken ct = default);
        Task<RecycleListing?> GetByIdAsync(int id, CancellationToken ct = default);
        Task<IEnumerable<RecycleListing>> GetActiveAsync(CancellationToken ct = default);
        Task<PagedResult<RecycleListing>> GetActivePagedAsync(int page, int pageSize, CancellationToken ct = default);
        Task<IEnumerable<RecycleListing>> GetByUserAsync(string userId, CancellationToken ct = default);
        Task<IEnumerable<RecycleListing>> GetAppliedByRecyclerAsync(string recyclerUserId, CancellationToken ct = default);

        Task<bool> RequestPickupAsync(int id, string recyclerUserId, CancellationToken ct = default);
        Task<bool> AcceptPickupAsync(int id, string donatorUserId, string recyclerUserId, CancellationToken ct = default);
        Task<IReadOnlyList<ApplicantInfo>?> GetApplicantsAsync(int id, string donatorUserId, CancellationToken ct = default);
        Task<bool> StartChatAsync(int id, string chatSessionId, CancellationToken ct = default);
        Task<bool> ConfirmPickupAsync(int id, string donatorUserId, CancellationToken ct = default);
        Task<bool> SubmitReceiptUploadAsync(int id, string recyclerUserId, string fileName, string contentType, byte[] data, decimal reportedAmount, CancellationToken ct = default);
        Task<bool> SetMeetingPointAsync(int id, string donatorUserId, decimal latitude, decimal longitude, CancellationToken ct = default);
        Task<bool> CancelAsync(int id, string donatorUserId, CancellationToken ct = default);
        Task<IEnumerable<RecycleListing>> SearchAsync(int cityId, bool onlyActive = true, CancellationToken ct = default);
        // Legacy paged search by city only
        Task<PagedResult<RecycleListing>> SearchAsync(int cityId, string userId, int page, int pageSize, bool onlyActive = true, CancellationToken ct = default);
        // New paged search supporting optional cityId and/or coordinates (5km radius)
        Task<PagedResult<RecycleListing>> SearchAsync(int? cityId, string userId, int page, int pageSize, bool onlyActive = true, decimal? latitude = null, decimal? longitude = null, CancellationToken ct = default);
    }
}
