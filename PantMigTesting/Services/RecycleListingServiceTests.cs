using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using PantmigService.Data;
using PantmigService.Entities;
using PantmigService.Services;
using Xunit;

namespace PantMigTesting.Services
{
    public class RecycleListingServiceTests
    {
        private static PantmigDbContext CreateDb()
        {
            var options = new DbContextOptionsBuilder<PantmigDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options;
            return new PantmigDbContext(options);
        }

        private static RecycleListingService CreateService(PantmigDbContext db) => new(db);

        private static RecycleListing NewListing(string donatorId = "donator-1", DateTime? createdAt = null, bool isActive = true, ListingStatus status = ListingStatus.Created)
            => new()
            {
                Title = "Cans",
                Description = "Bag of cans",
                Location = "Copenhagen",
                EstimatedValue = null,
                EstimatedAmount = 50m,
                AvailableFrom = DateTime.UtcNow.AddHours(-1),
                AvailableTo = DateTime.UtcNow.AddHours(2),
                CreatedByUserId = donatorId,
                CreatedAt = createdAt ?? DateTime.UtcNow,
                IsActive = isActive,
                Status = status,
                CityId = 1
            };

        [Fact]
        public async Task CreateAsync_Persists_Listing()
        {
            using var db = CreateDb();
            var svc = CreateService(db);
            var listing = NewListing();

            var created = await svc.CreateAsync(listing);

            Assert.True(created.Id > 0);
            var fromDb = await db.RecycleListings.FindAsync(created.Id);
            Assert.NotNull(fromDb);
            Assert.Equal("Cans", fromDb!.Title);
        }

        [Fact]
        public async Task GetByIdAsync_Returns_Listing_When_Found()
        {
            using var db = CreateDb();
            var svc = CreateService(db);
            var listing = await svc.CreateAsync(NewListing());

            var found = await svc.GetByIdAsync(listing.Id);

            Assert.NotNull(found);
            Assert.Equal(listing.Id, found!.Id);
        }

        [Fact]
        public async Task GetByIdAsync_Returns_Null_When_Not_Found()
        {
            using var db = CreateDb();
            var svc = CreateService(db);

            var found = await svc.GetByIdAsync(123);

            Assert.Null(found);
        }

        [Fact]
        public async Task GetActiveAsync_Filters_And_Orders_By_CreatedAt_Desc()
        {
            using var db = CreateDb();
            var svc = CreateService(db);

            var older = await svc.CreateAsync(NewListing(createdAt: DateTime.UtcNow.AddHours(-3)));
            var newest = await svc.CreateAsync(NewListing(createdAt: DateTime.UtcNow));
            // Inactive and non-Created should be filtered out
            await svc.CreateAsync(NewListing(isActive: false));
            await svc.CreateAsync(NewListing(status: ListingStatus.PendingAcceptance));

            var actives = (await svc.GetActiveAsync()).ToList();

            Assert.Equal(2, actives.Count);
            Assert.Equal(newest.Id, actives[0].Id);
            Assert.Equal(older.Id, actives[1].Id);
        }

        [Fact]
        public async Task RequestPickupAsync_Succeeds_When_Created_And_Active()
        {
            using var db = CreateDb();
            var svc = CreateService(db);
            var listing = await svc.CreateAsync(NewListing());

            var ok = await svc.RequestPickupAsync(listing.Id, "recycler-1");

            Assert.True(ok);
            var fromDb = await db.RecycleListings.FindAsync(listing.Id);
            Assert.Equal(ListingStatus.PendingAcceptance, fromDb!.Status);
            Assert.Equal("recycler-1", fromDb.AssignedRecyclerUserId);
        }

        [Theory]
        [InlineData(false, ListingStatus.Created)]
        [InlineData(true, ListingStatus.PendingAcceptance)]
        public async Task RequestPickupAsync_Fails_When_Invalid_State(bool isActive, ListingStatus status)
        {
            using var db = CreateDb();
            var svc = CreateService(db);
            var listing = await svc.CreateAsync(NewListing(isActive: isActive, status: status));

            var ok = await svc.RequestPickupAsync(listing.Id, "recycler-1");

            Assert.False(ok);
        }

        [Fact]
        public async Task AcceptPickupAsync_Succeeds_When_PendingAcceptance_And_Creator_Matches()
        {
            using var db = CreateDb();
            var svc = CreateService(db);
            var listing = await svc.CreateAsync(NewListing());
            await svc.RequestPickupAsync(listing.Id, "recycler-1");

            var ok = await svc.AcceptPickupAsync(listing.Id, listing.CreatedByUserId);

            Assert.True(ok);
            var fromDb = await db.RecycleListings.FindAsync(listing.Id);
            Assert.Equal(ListingStatus.Accepted, fromDb!.Status);
            Assert.NotNull(fromDb.AcceptedAt);
        }

        [Fact]
        public async Task AcceptPickupAsync_Fails_When_Donator_Does_Not_Match()
        {
            using var db = CreateDb();
            var svc = CreateService(db);
            var listing = await svc.CreateAsync(NewListing(donatorId: "donator-1"));
            await svc.RequestPickupAsync(listing.Id, "recycler-1");

            var ok = await svc.AcceptPickupAsync(listing.Id, "someone-else");

            Assert.False(ok);
        }

        [Fact]
        public async Task StartChatAsync_Succeeds_When_Accepted()
        {
            using var db = CreateDb();
            var svc = CreateService(db);
            var listing = await svc.CreateAsync(NewListing());
            await svc.RequestPickupAsync(listing.Id, "recycler-1");
            await svc.AcceptPickupAsync(listing.Id, listing.CreatedByUserId);

            var ok = await svc.StartChatAsync(listing.Id, "chat-123");

            Assert.True(ok);
            var fromDb = await db.RecycleListings.FindAsync(listing.Id);
            Assert.Equal("chat-123", fromDb!.ChatSessionId);
        }

        [Fact]
        public async Task StartChatAsync_Fails_When_Not_Accepted()
        {
            using var db = CreateDb();
            var svc = CreateService(db);
            var listing = await svc.CreateAsync(NewListing());

            var ok = await svc.StartChatAsync(listing.Id, "chat-123");

            Assert.False(ok);
        }

        [Fact]
        public async Task SetMeetingPoint_Succeeds_When_Chat_Started_And_Donator_Matches()
        {
            using var db = CreateDb();
            var svc = CreateService(db);
            var listing = await svc.CreateAsync(NewListing(donatorId: "donator-1"));
            await svc.RequestPickupAsync(listing.Id, "recycler-1");
            await svc.AcceptPickupAsync(listing.Id, "donator-1");
            await svc.StartChatAsync(listing.Id, "chat-1");

            var ok = await svc.SetMeetingPointAsync(listing.Id, "donator-1", 55.6761m, 12.5683m);

            Assert.True(ok);
            var fromDb = await db.RecycleListings.FindAsync(listing.Id);
            Assert.Equal(55.676100m, fromDb!.MeetingLatitude);
            Assert.Equal(12.568300m, fromDb.MeetingLongitude);
            Assert.NotNull(fromDb.MeetingSetAt);
        }

        [Fact]
        public async Task SetMeetingPoint_Fails_When_No_Chat_Started_Or_Wrong_User()
        {
            using var db = CreateDb();
            var svc = CreateService(db);
            var listing = await svc.CreateAsync(NewListing(donatorId: "donator-1"));
            await svc.RequestPickupAsync(listing.Id, "recycler-1");
            await svc.AcceptPickupAsync(listing.Id, "donator-1");

            // No chat yet
            var okNoChat = await svc.SetMeetingPointAsync(listing.Id, "donator-1", 10m, 10m);
            Assert.False(okNoChat);

            // Start chat and try with wrong user
            await svc.StartChatAsync(listing.Id, "chat-1");
            var okWrongUser = await svc.SetMeetingPointAsync(listing.Id, "someone-else", 10m, 10m);
            Assert.False(okWrongUser);
        }

        [Fact]
        public async Task SetMeetingPoint_Fails_When_Invalid_Coordinates()
        {
            using var db = CreateDb();
            var svc = CreateService(db);
            var listing = await svc.CreateAsync(NewListing(donatorId: "donator-1"));
            await svc.RequestPickupAsync(listing.Id, "recycler-1");
            await svc.AcceptPickupAsync(listing.Id, "donator-1");
            await svc.StartChatAsync(listing.Id, "chat-1");

            Assert.False(await svc.SetMeetingPointAsync(listing.Id, "donator-1", -91m, 0m));
            Assert.False(await svc.SetMeetingPointAsync(listing.Id, "donator-1", 0m, 181m));
        }

        [Fact]
        public async Task ConfirmPickupAsync_Succeeds_When_Accepted_And_Recycler_Matches()
        {
            using var db = CreateDb();
            var svc = CreateService(db);
            var listing = await svc.CreateAsync(NewListing());
            await svc.RequestPickupAsync(listing.Id, "recycler-1");
            await svc.AcceptPickupAsync(listing.Id, listing.CreatedByUserId);

            var ok = await svc.ConfirmPickupAsync(listing.Id, "recycler-1");

            Assert.True(ok);
            var fromDb = await db.RecycleListings.FindAsync(listing.Id);
            Assert.Equal(ListingStatus.PickedUp, fromDb!.Status);
            Assert.NotNull(fromDb.PickupConfirmedAt);
        }

        [Fact]
        public async Task ConfirmPickupAsync_Fails_When_Recycler_Does_Not_Match()
        {
            using var db = CreateDb();
            var svc = CreateService(db);
            var listing = await svc.CreateAsync(NewListing());
            await svc.RequestPickupAsync(listing.Id, "recycler-1");
            await svc.AcceptPickupAsync(listing.Id, listing.CreatedByUserId);

            var ok = await svc.ConfirmPickupAsync(listing.Id, "recycler-2");

            Assert.False(ok);
        }

        [Fact]
        public async Task SubmitReceiptAsync_Succeeds_When_PickedUp_And_Recycler_Matches()
        {
            using var db = CreateDb();
            var svc = CreateService(db);
            var listing = await svc.CreateAsync(NewListing());
            await svc.RequestPickupAsync(listing.Id, "recycler-1");
            await svc.AcceptPickupAsync(listing.Id, listing.CreatedByUserId);
            await svc.ConfirmPickupAsync(listing.Id, "recycler-1");

            var ok = await svc.SubmitReceiptAsync(listing.Id, "recycler-1", "http://img/1.png", 123.45m);

            Assert.True(ok);
            var fromDb = await db.RecycleListings.FindAsync(listing.Id);
            Assert.Equal(ListingStatus.AwaitingVerification, fromDb!.Status);
            Assert.Equal("http://img/1.png", fromDb.ReceiptImageUrl);
            Assert.Equal(123.45m, fromDb.ReportedAmount);
        }

        [Fact]
        public async Task SubmitReceiptAsync_Fails_When_Status_Not_PickedUp()
        {
            using var db = CreateDb();
            var svc = CreateService(db);
            var listing = await svc.CreateAsync(NewListing());

            var ok = await svc.SubmitReceiptAsync(listing.Id, "recycler-1", "http://img/1.png", 10m);

            Assert.False(ok);
        }

        [Fact]
        public async Task VerifyReceiptAsync_Succeeds_When_AwaitingVerification_And_Donator_Matches()
        {
            using var db = CreateDb();
            var svc = CreateService(db);
            var listing = await svc.CreateAsync(NewListing(donatorId: "donator-1"));
            await svc.RequestPickupAsync(listing.Id, "recycler-1");
            await svc.AcceptPickupAsync(listing.Id, "donator-1");
            await svc.ConfirmPickupAsync(listing.Id, "recycler-1");
            await svc.SubmitReceiptAsync(listing.Id, "recycler-1", "http://img/1.png", 123m);

            var ok = await svc.VerifyReceiptAsync(listing.Id, "donator-1", 120m);

            Assert.True(ok);
            var fromDb = await db.RecycleListings.FindAsync(listing.Id);
            Assert.Equal(ListingStatus.Completed, fromDb!.Status);
            Assert.Equal(120m, fromDb.VerifiedAmount);
            Assert.False(fromDb.IsActive);
            Assert.NotNull(fromDb.CompletedAt);
        }

        [Fact]
        public async Task VerifyReceiptAsync_Fails_When_Wrong_Donator()
        {
            using var db = CreateDb();
            var svc = CreateService(db);
            var listing = await svc.CreateAsync(NewListing(donatorId: "donator-1"));
            await svc.RequestPickupAsync(listing.Id, "recycler-1");
            await svc.AcceptPickupAsync(listing.Id, "donator-1");
            await svc.ConfirmPickupAsync(listing.Id, "recycler-1");
            await svc.SubmitReceiptAsync(listing.Id, "recycler-1", "http://img/1.png", 123m);

            var ok = await svc.VerifyReceiptAsync(listing.Id, "donator-2", 100m);

            Assert.False(ok);
        }

        [Fact]
        public async Task EndToEnd_Recycle_Process_Completes_Successfully()
        {
            using var db = CreateDb();
            var svc = CreateService(db);

            // 1. Donator creates listing
            var listing = await svc.CreateAsync(NewListing(donatorId: "donator-1"));

            // 2. Recycler requests pickup
            Assert.True(await svc.RequestPickupAsync(listing.Id, "recycler-1"));

            // 3. Donator accepts
            Assert.True(await svc.AcceptPickupAsync(listing.Id, "donator-1"));

            // 4. Start chat
            Assert.True(await svc.StartChatAsync(listing.Id, "chat-xyz"));

            // 4.1 Donator sets meeting point
            Assert.True(await svc.SetMeetingPointAsync(listing.Id, "donator-1", 55.6761m, 12.5683m));

            // 5. Recycler confirms pickup
            Assert.True(await svc.ConfirmPickupAsync(listing.Id, "recycler-1"));

            // 6. Recycler submits receipt
            Assert.True(await svc.SubmitReceiptAsync(listing.Id, "recycler-1", "http://img/receipt.png", 250.00m));

            // 7. Donator verifies receipt
            Assert.True(await svc.VerifyReceiptAsync(listing.Id, "donator-1", 245.00m));

            var final = await db.RecycleListings.FindAsync(listing.Id);
            Assert.NotNull(final);
            Assert.Equal(ListingStatus.Completed, final!.Status);
            Assert.False(final.IsActive);
            Assert.Equal(245.00m, final.VerifiedAmount);
            Assert.Equal("chat-xyz", final.ChatSessionId);
            Assert.Equal(55.676100m, final.MeetingLatitude);
            Assert.Equal(12.568300m, final.MeetingLongitude);
        }
    }
}
