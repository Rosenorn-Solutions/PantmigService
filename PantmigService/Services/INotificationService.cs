using PantmigService.Entities;

namespace PantmigService.Services
{
    public interface INotificationService
    {
        Task<Notification> CreateAsync(string userId, int listingId, NotificationType type, string? message = null, CancellationToken ct = default);
        Task<int> MarkReadAsync(string userId, int[] ids, CancellationToken ct = default);
        Task<IReadOnlyList<Notification>> GetRecentAsync(string userId, int take = 50, CancellationToken ct = default);
    }
}
