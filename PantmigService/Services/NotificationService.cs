using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using PantmigService.Data;
using PantmigService.Entities;
using PantmigService.Hubs;

namespace PantmigService.Services
{
    public class NotificationService : INotificationService
    {
        private readonly PantmigDbContext _db;
        private readonly IHubContext<NotificationsHub> _hub;
        private readonly ILogger<NotificationService> _logger;

        public NotificationService(PantmigDbContext db, IHubContext<NotificationsHub> hub, ILogger<NotificationService> logger)
        {
            _db = db; _hub = hub; _logger = logger;
        }

        public async Task<Notification> CreateAsync(string userId, int listingId, NotificationType type, string? message = null, CancellationToken ct = default)
        {
            var n = new Notification
            {
                UserId = userId,
                ListingId = listingId,
                Type = type,
                Message = message,
                CreatedAt = DateTime.UtcNow,
                IsRead = false
            };
            _db.Notifications.Add(n);
            await _db.SaveChangesAsync(ct);

            try
            {
                await _hub.Clients.Group($"user-{userId}").SendAsync("Notify", new
                {
                    n.Id,
                    n.ListingId,
                    n.Type,
                    n.Message,
                    n.CreatedAt
                }, ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to push notification to user {UserId}", userId);
            }

            return n;
        }

        public async Task<int> MarkReadAsync(string userId, int[] ids, CancellationToken ct = default)
        {
            var toMark = await _db.Notifications.Where(n => n.UserId == userId && ids.Contains(n.Id)).ToListAsync(ct);
            foreach (var n in toMark) n.IsRead = true;
            await _db.SaveChangesAsync(ct);
            return toMark.Count;
        }

        public async Task<IReadOnlyList<Notification>> GetRecentAsync(string userId, int take = 50, CancellationToken ct = default)
        {
            var list = await _db.Notifications.AsNoTracking()
                .Where(n => n.UserId == userId)
                .OrderByDescending(n => n.CreatedAt)
                .Take(take)
                .ToListAsync(ct);
            return list;
        }
    }
}
