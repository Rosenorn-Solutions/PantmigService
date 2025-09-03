using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using PantmigService.Entities;
using PantmigService.Services;
using PantmigService.Data;
using Microsoft.EntityFrameworkCore;

namespace PantmigService.Hubs
{
    [Authorize]
    public class ChatHub : Hub
    {
        private readonly IRecycleListingService _listings;
        private readonly PantmigDbContext _db;

        public ChatHub(IRecycleListingService listings, PantmigDbContext db)
        {
            _listings = listings;
            _db = db;
        }

        public async Task JoinListingChat(int listingId)
        {
            var userId = Context.User?.FindFirstValue(ClaimTypes.NameIdentifier) ?? Context.User?.FindFirstValue("sub");
            if (string.IsNullOrEmpty(userId))
            {
                throw new HubException("Unauthorized");
            }

            var listing = await _listings.GetByIdAsync(listingId);
            if (listing is null)
            {
                throw new HubException("Listing not found");
            }

            // Listing must be in or past Accepted to chat
            var validStatus = listing.Status == ListingStatus.Accepted
                               || listing.Status == ListingStatus.PickedUp
                               || listing.Status == ListingStatus.AwaitingVerification
                               || listing.Status == ListingStatus.Completed;
            if (!validStatus)
            {
                throw new HubException("Chat not available for this listing status");
            }

            // Only donator or assigned recycler can join
            var isParticipant = string.Equals(userId, listing.CreatedByUserId, StringComparison.Ordinal)
                                || string.Equals(userId, listing.AssignedRecyclerUserId, StringComparison.Ordinal);
            if (!isParticipant)
            {
                throw new HubException("Forbidden");
            }

            var chatId = listing.ChatSessionId;
            if (string.IsNullOrWhiteSpace(chatId))
            {
                chatId = $"listing-{listingId}";
                // Persist chat id on the listing
                await _listings.StartChatAsync(listingId, chatId);
            }

            await Groups.AddToGroupAsync(Context.ConnectionId, chatId);

            // Send last N messages
            var recent = await _db.ChatMessages
                .AsNoTracking()
                .Where(m => m.ListingId == listingId)
                .OrderByDescending(m => m.SentAt)
                .Take(50)
                .OrderBy(m => m.SentAt)
                .ToListAsync();
            await Clients.Caller.SendAsync("Joined", new { ListingId = listingId, ChatSessionId = chatId, History = recent });
        }

        public async Task SendMessage(int listingId, string message)
        {
            if (string.IsNullOrWhiteSpace(message)) return;

            var userId = Context.User?.FindFirstValue(ClaimTypes.NameIdentifier) ?? Context.User?.FindFirstValue("sub");
            if (string.IsNullOrEmpty(userId))
            {
                throw new HubException("Unauthorized");
            }

            var listing = await _listings.GetByIdAsync(listingId);
            if (listing is null)
            {
                throw new HubException("Listing not found");
            }

            var chatId = listing.ChatSessionId;
            if (string.IsNullOrWhiteSpace(chatId))
            {
                throw new HubException("Chat not started");
            }

            var isParticipant = string.Equals(userId, listing.CreatedByUserId, StringComparison.Ordinal)
                                || string.Equals(userId, listing.AssignedRecyclerUserId, StringComparison.Ordinal);
            if (!isParticipant)
            {
                throw new HubException("Forbidden");
            }

            var now = DateTime.UtcNow;
            var record = new ChatMessage
            {
                ListingId = listingId,
                ChatSessionId = chatId,
                SenderUserId = userId,
                Text = message,
                SentAt = now
            };
            _db.ChatMessages.Add(record);
            await _db.SaveChangesAsync();

            var payload = new
            {
                ListingId = listingId,
                ChatSessionId = chatId,
                SenderUserId = userId,
                Text = message,
                SentAt = now
            };
            await Clients.Group(chatId).SendAsync("ReceiveMessage", payload);
        }

        public async Task LeaveListingChat(int listingId)
        {
            var listing = await _listings.GetByIdAsync(listingId);
            var chatId = listing?.ChatSessionId;
            if (string.IsNullOrWhiteSpace(chatId)) return;
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, chatId);
            await Clients.Caller.SendAsync("Left", new { ListingId = listingId, ChatSessionId = chatId });
        }
    }
}
