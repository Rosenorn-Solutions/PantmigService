using Microsoft.EntityFrameworkCore;
using PantmigService.Data;
using PantmigService.Entities;

namespace PantmigService.Services
{
    public class RecycleListingService : IRecycleListingService
    {
        private readonly PantmigDbContext _db;
        public RecycleListingService(PantmigDbContext db)
        {
            _db = db;
        }

        public async Task<RecycleListing> CreateAsync(RecycleListing listing, CancellationToken ct = default)
        {
            _db.RecycleListings.Add(listing);
            await _db.SaveChangesAsync(ct);
            return listing;
        }

        public Task<RecycleListing?> GetByIdAsync(int id, CancellationToken ct = default)
        {
            return _db.RecycleListings.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
        }

        public async Task<IEnumerable<RecycleListing>> GetActiveAsync(CancellationToken ct = default)
        {
            return await _db.RecycleListings.AsNoTracking().Where(x => x.IsActive && x.Status == ListingStatus.Created).OrderByDescending(x => x.CreatedAt).ToListAsync(ct);
        }

        public async Task<bool> RequestPickupAsync(int id, string recyclerUserId, CancellationToken ct = default)
        {
            var listing = await _db.RecycleListings.FirstOrDefaultAsync(x => x.Id == id, ct);
            if (listing is null || !listing.IsActive || listing.Status != ListingStatus.Created) return false;
            listing.AssignedRecyclerUserId = recyclerUserId;
            listing.Status = ListingStatus.PendingAcceptance;
            await _db.SaveChangesAsync(ct);
            return true;
        }

        public async Task<bool> AcceptPickupAsync(int id, string donatorUserId, CancellationToken ct = default)
        {
            var listing = await _db.RecycleListings.FirstOrDefaultAsync(x => x.Id == id, ct);
            if (listing is null || !listing.IsActive) return false;
            if (!string.Equals(listing.CreatedByUserId, donatorUserId, StringComparison.Ordinal)) return false;
            if (listing.Status != ListingStatus.PendingAcceptance || string.IsNullOrEmpty(listing.AssignedRecyclerUserId)) return false;
            listing.Status = ListingStatus.Accepted;
            listing.AcceptedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync(ct);
            return true;
        }

        public async Task<bool> StartChatAsync(int id, string chatSessionId, CancellationToken ct = default)
        {
            var listing = await _db.RecycleListings.FirstOrDefaultAsync(x => x.Id == id, ct);
            if (listing is null || !listing.IsActive) return false;
            if (listing.Status != ListingStatus.Accepted) return false;
            listing.ChatSessionId = chatSessionId;
            await _db.SaveChangesAsync(ct);
            return true;
        }

        public async Task<bool> SetMeetingPointAsync(int id, string donatorUserId, decimal latitude, decimal longitude, CancellationToken ct = default)
        {
            // basic range validation
            if (latitude is < -90 or > 90) return false;
            if (longitude is < -180 or > 180) return false;

            var listing = await _db.RecycleListings.FirstOrDefaultAsync(x => x.Id == id, ct);
            if (listing is null || !listing.IsActive) return false;
            if (!string.Equals(listing.CreatedByUserId, donatorUserId, StringComparison.Ordinal)) return false;
            // Only after chat started
            if (string.IsNullOrWhiteSpace(listing.ChatSessionId)) return false;
            // Allow setting in Accepted or later (until Completed/Cancelled)
            var allowed = listing.Status == ListingStatus.Accepted
                          || listing.Status == ListingStatus.PickedUp
                          || listing.Status == ListingStatus.AwaitingVerification;
            if (!allowed) return false;

            listing.MeetingLatitude = decimal.Round(latitude, 6);
            listing.MeetingLongitude = decimal.Round(longitude, 6);
            listing.MeetingSetAt = DateTime.UtcNow;
            await _db.SaveChangesAsync(ct);
            return true;
        }

        public async Task<bool> ConfirmPickupAsync(int id, string recyclerUserId, CancellationToken ct = default)
        {
            var listing = await _db.RecycleListings.FirstOrDefaultAsync(x => x.Id == id, ct);
            if (listing is null || !listing.IsActive) return false;
            if (listing.Status != ListingStatus.Accepted) return false;
            if (!string.Equals(listing.AssignedRecyclerUserId, recyclerUserId, StringComparison.Ordinal)) return false;
            listing.Status = ListingStatus.PickedUp;
            listing.PickupConfirmedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync(ct);
            return true;
        }

        public async Task<bool> SubmitReceiptAsync(int id, string recyclerUserId, string receiptImageUrl, decimal reportedAmount, CancellationToken ct = default)
        {
            var listing = await _db.RecycleListings.FirstOrDefaultAsync(x => x.Id == id, ct);
            if (listing is null || !listing.IsActive) return false;
            if (listing.Status != ListingStatus.PickedUp) return false;
            if (!string.Equals(listing.AssignedRecyclerUserId, recyclerUserId, StringComparison.Ordinal)) return false;
            listing.ReceiptImageUrl = receiptImageUrl;
            listing.ReportedAmount = reportedAmount;
            listing.Status = ListingStatus.AwaitingVerification;
            await _db.SaveChangesAsync(ct);
            return true;
        }

        public async Task<bool> VerifyReceiptAsync(int id, string donatorUserId, decimal verifiedAmount, CancellationToken ct = default)
        {
            var listing = await _db.RecycleListings.FirstOrDefaultAsync(x => x.Id == id, ct);
            if (listing is null || !listing.IsActive) return false;
            if (listing.Status != ListingStatus.AwaitingVerification) return false;
            if (!string.Equals(listing.CreatedByUserId, donatorUserId, StringComparison.Ordinal)) return false;
            listing.VerifiedAmount = verifiedAmount;
            listing.Status = ListingStatus.Completed;
            listing.CompletedAt = DateTime.UtcNow;
            listing.IsActive = false;
            await _db.SaveChangesAsync(ct);
            return true;
        }
    }
}
