using Microsoft.EntityFrameworkCore;
using PantmigService.Data;
using PantmigService.Entities;
using Microsoft.Extensions.Logging;

namespace PantmigService.Services
{
    public class RecycleListingService(PantmigDbContext db, ILogger<RecycleListingService> logger, INotificationService notifications) : IRecycleListingService
    {
        private readonly PantmigDbContext _db = db;
        private readonly ILogger<RecycleListingService> _logger = logger;
        private readonly INotificationService _notifications = notifications;

        public async Task<RecycleListing> CreateAsync(RecycleListing listing, CancellationToken ct = default)
        {
            _logger.LogInformation("Creating listing for user {UserId} with title {Title}", listing.CreatedByUserId, listing.Title);
            _db.RecycleListings.Add(listing);
            await _db.SaveChangesAsync(ct);
            _logger.LogInformation("Listing {ListingId} created", listing.Id);
            return listing;
        }

        public Task<RecycleListing?> GetByIdAsync(int id, CancellationToken ct = default)
        {
            _logger.LogDebug("Retrieving listing {ListingId}", id);
            return _db.RecycleListings.AsNoTracking().Include(l => l.Items).Include(l => l.Images).FirstOrDefaultAsync(x => x.Id == id, ct);
        }

        public async Task<IEnumerable<RecycleListing>> GetActiveAsync(CancellationToken ct = default)
        {
            _logger.LogDebug("Fetching active listings");
            var list = await _db.RecycleListings.AsNoTracking()
                .Where(x => x.IsActive && (x.Status == ListingStatus.Created || x.Status == ListingStatus.PendingAcceptance))
                .OrderByDescending(x => x.CreatedAt)
                .Include(l => l.Items)
                .Include(l => l.Images)
                .ToListAsync(ct);
            _logger.LogDebug("Fetched {Count} active listings", list.Count);
            return list;
        }

        public async Task<IEnumerable<RecycleListing>> GetByUserAsync(string userId, CancellationToken ct = default)
        {
            _logger.LogDebug("Fetching listings for user {UserId}", userId);
            var list = await _db.RecycleListings.AsNoTracking()
                .Where(x => x.CreatedByUserId == userId)
                .OrderByDescending(x => x.CreatedAt)
                .Include(l => l.Items)
                .Include(l => l.Images)
                .ToListAsync(ct);
            _logger.LogDebug("User {UserId} has {Count} listings", userId, list.Count);
            return list;
        }

        public async Task<IEnumerable<RecycleListing>> GetAppliedByRecyclerAsync(string recyclerUserId, CancellationToken ct = default)
        {
            _logger.LogDebug("Fetching applied listings for recycler {Recycler}", recyclerUserId);
            var list = await _db.RecycleListings
                .AsNoTracking()
                .Where(l => l.Applicants.Any(a => a.RecyclerUserId == recyclerUserId))
                .OrderByDescending(x => x.CreatedAt)
                .Include(l => l.Items)
                .Include(l => l.Images)
                .ToListAsync(ct);
            _logger.LogDebug("Recycler {Recycler} has applied to {Count} listings", recyclerUserId, list.Count);
            return list;
        }

        public async Task<bool> RequestPickupAsync(int id, string recyclerUserId, CancellationToken ct = default)
        {
            _logger.LogDebug("Pickup request for listing {ListingId} by recycler {Recycler}", id, recyclerUserId);
            var listing = await _db.RecycleListings.Include(l => l.Applicants).FirstOrDefaultAsync(x => x.Id == id, ct);
            if (listing is null)
            {
                _logger.LogWarning("Pickup request failed: listing {ListingId} not found", id);
                return false;
            }
            if (!listing.IsActive || listing.Status != ListingStatus.Created)
            {
                _logger.LogWarning("Pickup request rejected: listing {ListingId} not active or not in Created state (Status={Status}, Active={Active})", id, listing.Status, listing.IsActive);
                return false;
            }

            var exists = listing.Applicants.Any(a => a.RecyclerUserId == recyclerUserId);
            if (!exists)
            {
                listing.Applicants.Add(new RecycleListingApplicant
                {
                    ListingId = listing.Id,
                    RecyclerUserId = recyclerUserId
                });
                _logger.LogInformation("Recycler {Recycler} added as applicant to listing {ListingId}", recyclerUserId, id);
            }
            else
            {
                _logger.LogDebug("Recycler {Recycler} already applied to listing {ListingId}", recyclerUserId, id);
            }

            if (listing.Status == ListingStatus.Created)
            {
                listing.Status = ListingStatus.PendingAcceptance;
                _logger.LogInformation("Listing {ListingId} moved to PendingAcceptance", id);
            }

            await _db.SaveChangesAsync(ct);

            // Notify donator about new application
            await _notifications.CreateAsync(listing.CreatedByUserId, listing.Id, NotificationType.RecyclerApplied,
                message: "A recycler has applied to your listing.", ct);

            return true;
        }

        public async Task<IReadOnlyList<ApplicantInfo>?> GetApplicantsAsync(int id, string donatorUserId, CancellationToken ct = default)
        {
            _logger.LogDebug("Retrieving applicants for listing {ListingId} by donator {Donator}", id, donatorUserId);
            var listing = await _db.RecycleListings.Include(l => l.Applicants).AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
            if (listing is null)
            {
                _logger.LogWarning("GetApplicants failed: listing {ListingId} not found", id);
                return null;
            }
            if (!listing.IsActive)
            {
                _logger.LogWarning("GetApplicants rejected: listing {ListingId} inactive", id);
                return null;
            }
            if (listing.CreatedByUserId != donatorUserId)
            {
                _logger.LogWarning("GetApplicants rejected: user {UserId} not owner of listing {ListingId}", donatorUserId, id);
                return null;
            }

            var result = listing.Applicants
                .OrderByDescending(a => a.AppliedAt)
                .Select(a => new ApplicantInfo(a.RecyclerUserId, a.AppliedAt))
                .ToList();
            _logger.LogDebug("Listing {ListingId} has {Count} applicants", id, result.Count);
            return result;
        }

        public async Task<bool> AcceptPickupAsync(int id, string donatorUserId, string recyclerUserId, CancellationToken ct = default)
        {
            _logger.LogDebug("Accepting recycler {Recycler} for listing {ListingId} by donator {Donator}", recyclerUserId, id, donatorUserId);
            var listing = await _db.RecycleListings.Include(l => l.Applicants).FirstOrDefaultAsync(x => x.Id == id, ct);
            if (listing is null || !listing.IsActive)
            {
                _logger.LogWarning("Accept failed: listing {ListingId} not found or inactive", id);
                return false;
            }
            if (!string.Equals(listing.CreatedByUserId, donatorUserId, StringComparison.Ordinal))
            {
                _logger.LogWarning("Accept failed: user {UserId} not owner of listing {ListingId}", donatorUserId, id);
                return false;
            }
            if (listing.Status != ListingStatus.PendingAcceptance)
            {
                _logger.LogWarning("Accept failed: listing {ListingId} not in PendingAcceptance (Status={Status})", id, listing.Status);
                return false;
            }
            var applied = listing.Applicants.Any(a => a.RecyclerUserId == recyclerUserId);
            if (!applied)
            {
                _logger.LogWarning("Accept failed: recycler {Recycler} not an applicant for listing {ListingId}", recyclerUserId, id);
                return false;
            }
            listing.AssignedRecyclerUserId = recyclerUserId;
            listing.Status = ListingStatus.Accepted;
            listing.AcceptedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync(ct);
            _logger.LogInformation("Listing {ListingId} accepted recycler {Recycler}", id, recyclerUserId);

            // Notify recycler that they were accepted
            await _notifications.CreateAsync(recyclerUserId, listing.Id, NotificationType.DonorAccepted,
                message: "Your application was accepted.", ct);

            return true;
        }

        public async Task<bool> StartChatAsync(int id, string chatSessionId, CancellationToken ct = default)
        {
            _logger.LogDebug("Starting chat for listing {ListingId} with session {ChatId}", id, chatSessionId);
            var listing = await _db.RecycleListings.FirstOrDefaultAsync(x => x.Id == id, ct);
            if (listing is null || !listing.IsActive)
            {
                _logger.LogWarning("StartChat failed: listing {ListingId} not found or inactive", id);
                return false;
            }
            if (listing.Status != ListingStatus.Accepted)
            {
                _logger.LogWarning("StartChat failed: listing {ListingId} not in Accepted state (Status={Status})", id, listing.Status);
                return false;
            }
            listing.ChatSessionId = chatSessionId;
            await _db.SaveChangesAsync(ct);
            _logger.LogInformation("Chat started for listing {ListingId}", id);
            return true;
        }

        public async Task<bool> SetMeetingPointAsync(int id, string donatorUserId, decimal latitude, decimal longitude, CancellationToken ct = default)
        {
            _logger.LogDebug("Setting meeting point for listing {ListingId} by donator {Donator} to ({Lat},{Lon})", id, donatorUserId, latitude, longitude);
            if (latitude is < -90 or > 90 || longitude is < -180 or > 180)
            {
                _logger.LogWarning("SetMeetingPoint failed: invalid coordinates for listing {ListingId}", id);
                return false;
            }
            var listing = await _db.RecycleListings.FirstOrDefaultAsync(x => x.Id == id, ct);
            if (listing is null || !listing.IsActive)
            {
                _logger.LogWarning("SetMeetingPoint failed: listing {ListingId} not found or inactive", id);
                return false;
            }
            if (!string.Equals(listing.CreatedByUserId, donatorUserId, StringComparison.Ordinal))
            {
                _logger.LogWarning("SetMeetingPoint failed: user {UserId} not owner of listing {ListingId}", donatorUserId, id);
                return false;
            }
            if (string.IsNullOrWhiteSpace(listing.ChatSessionId))
            {
                _logger.LogWarning("SetMeetingPoint failed: chat not started for listing {ListingId}", id);
                return false;
            }
            var allowed = listing.Status == ListingStatus.Accepted
                          || listing.Status == ListingStatus.PickedUp
                          || listing.Status == ListingStatus.AwaitingVerification;
            if (!allowed)
            {
                _logger.LogWarning("SetMeetingPoint failed: invalid status {Status} for listing {ListingId}", listing.Status, id);
                return false;
            }
            listing.MeetingLatitude = decimal.Round(latitude, 6);
            listing.MeetingLongitude = decimal.Round(longitude, 6);
            listing.MeetingSetAt = DateTime.UtcNow;
            await _db.SaveChangesAsync(ct);
            _logger.LogInformation("Meeting point set for listing {ListingId}", id);

            // Notify recycler that meeting point was set
            if (!string.IsNullOrEmpty(listing.AssignedRecyclerUserId))
            {
                await _notifications.CreateAsync(listing.AssignedRecyclerUserId, listing.Id, NotificationType.MeetingSet,
                    message: "A meeting point has been set.", ct);
            }

            return true;
        }

        public async Task<bool> ConfirmPickupAsync(int id, string donatorUserId, CancellationToken ct = default)
        {
            _logger.LogDebug("Confirming pickup for listing {ListingId} by donator {Donator}", id, donatorUserId);
            var listing = await _db.RecycleListings.FirstOrDefaultAsync(x => x.Id == id, ct);
            if (listing is null || !listing.IsActive)
            {
                _logger.LogWarning("ConfirmPickup failed: listing {ListingId} not found or inactive", id);
                return false;
            }
            if (!string.Equals(listing.CreatedByUserId, donatorUserId, StringComparison.Ordinal))
            {
                _logger.LogWarning("ConfirmPickup failed: user {UserId} not owner of listing {ListingId}", donatorUserId, id);
                return false;
            }
            if (listing.Status != ListingStatus.Accepted)
            {
                _logger.LogWarning("ConfirmPickup failed: listing {ListingId} not in Accepted state (Status={Status})", id, listing.Status);
                return false;
            }
            if (string.IsNullOrWhiteSpace(listing.ChatSessionId) || listing.MeetingLatitude is null || listing.MeetingLongitude is null)
            {
                _logger.LogWarning("ConfirmPickup failed: chat not started or meeting point not set for listing {ListingId}", id);
                return false;
            }

            listing.Status = ListingStatus.Completed;
            listing.PickupConfirmedAt = DateTime.UtcNow;
            listing.CompletedAt = DateTime.UtcNow;
            listing.IsActive = false;
            await _db.SaveChangesAsync(ct);
            _logger.LogInformation("Pickup confirmed and listing {ListingId} completed", id);
            return true;
        }

        public async Task<bool> SubmitReceiptUploadAsync(int id, string recyclerUserId, string fileName, string contentType, byte[] data, decimal reportedAmount, CancellationToken ct = default)
        {
            _logger.LogDebug("Submitting receipt upload for listing {ListingId} by recycler {Recycler}", id, recyclerUserId);
            var listing = await _db.RecycleListings.FirstOrDefaultAsync(x => x.Id == id, ct);
            if (listing is null)
            {
                _logger.LogWarning("SubmitReceiptUpload failed: listing {ListingId} not found", id);
                return false;
            }
            if (!string.Equals(listing.AssignedRecyclerUserId, recyclerUserId, StringComparison.Ordinal))
            {
                _logger.LogWarning("SubmitReceiptUpload failed: recycler {Recycler} not assigned to listing {ListingId}", recyclerUserId, id);
                return false;
            }

            listing.ReceiptImageUrl = null;
            listing.ReceiptImageBytes = data;
            listing.ReceiptImageContentType = contentType;
            listing.ReceiptImageFileName = fileName;
            listing.ReportedAmount = reportedAmount;

            await _db.SaveChangesAsync(ct);
            _logger.LogInformation("Receipt uploaded for listing {ListingId}", id);
            return true;
        }

        public async Task<bool> CancelAsync(int id, string donatorUserId, CancellationToken ct = default)
        {
            _logger.LogDebug("Cancelling listing {ListingId} by donator {Donator}", id, donatorUserId);
            var listing = await _db.RecycleListings.FirstOrDefaultAsync(x => x.Id == id, ct);
            if (listing is null || !listing.IsActive)
            {
                _logger.LogWarning("Cancel failed: listing {ListingId} not found or inactive", id);
                return false;
            }
            if (!string.Equals(listing.CreatedByUserId, donatorUserId, StringComparison.Ordinal))
            {
                _logger.LogWarning("Cancel failed: user {UserId} not owner of listing {ListingId}", donatorUserId, id);
                return false;
            }
            if (listing.Status == ListingStatus.Completed || listing.Status == ListingStatus.Cancelled)
            {
                _logger.LogWarning("Cancel failed: listing {ListingId} already in terminal state {Status}", id, listing.Status);
                return false;
            }
            listing.Status = ListingStatus.Cancelled;
            listing.IsActive = false;
            await _db.SaveChangesAsync(ct);
            _logger.LogInformation("Listing {ListingId} cancelled", id);
            return true;
        }
    }
}
