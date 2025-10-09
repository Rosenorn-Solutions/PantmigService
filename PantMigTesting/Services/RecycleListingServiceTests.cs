using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using PantmigService.Data;
using PantmigService.Entities;
using PantmigService.Services;
using Xunit;
using Microsoft.Extensions.Logging.Abstractions;
using System.Collections.Generic;

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

        private static RecycleListingService CreateService(PantmigDbContext db) => new(db, NullLogger<RecycleListingService>.Instance);

        private static RecycleListing NewListing(string donatorId = "donator-1", DateTime? createdAt = null, bool isActive = true, ListingStatus status = ListingStatus.Created, int quantity = 50)
            => new()
            {
                Title = "Cans",
                Description = "Bag of cans",
                Location = "Copenhagen",
                EstimatedValue = null,
                AvailableFrom = DateOnly.FromDateTime(DateTime.UtcNow.Date),
                AvailableTo = DateOnly.FromDateTime(DateTime.UtcNow.Date.AddDays(1)),
                CreatedByUserId = donatorId,
                CreatedAt = createdAt ?? DateTime.UtcNow,
                IsActive = isActive,
                Status = status,
                CityId = 1,
                Items =
                [
                    new() { MaterialType = RecycleMaterialType.Can, Quantity = quantity }
                ]
            };

        [Fact]
        public void ApproximateWorth_Computes_Correctly()
        {
            var listing = NewListing(quantity: 10); // 10 * 2.33 = 23.30
            Assert.Equal(23.30m, listing.ApproximateWorth);

            listing.Items.Add(new RecycleListingItem { MaterialType = RecycleMaterialType.PlasticBottle, Quantity = 5 }); // total 15 * 2.33 = 34.95
            Assert.Equal(34.95m, listing.ApproximateWorth);
        }

        [Fact]
        public void ApproximateWorth_Zero_When_No_Items()
        {
            var listing = NewListing(quantity: 0);
            listing.Items.Clear();
            Assert.Equal(0m, listing.ApproximateWorth);
        }

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
            var pending = await svc.CreateAsync(NewListing(status: ListingStatus.PendingAcceptance, createdAt: DateTime.UtcNow.AddHours(-2)));
            
            //Don't show
            await svc.CreateAsync(NewListing(isActive: false));
            await svc.CreateAsync(NewListing(status: ListingStatus.Completed));

            var actives = (await svc.GetActiveAsync()).ToList();

            Assert.Equal(3, actives.Count);
            Assert.Equal(newest.Id, actives[0].Id);
            Assert.Equal(pending.Id, actives[1].Id);
            Assert.Equal(older.Id, actives[2].Id);
        }

        [Fact]
        public async Task RequestPickupAsync_Succeeds_When_Created_And_Active()
        {
            using var db = CreateDb();
            var svc = CreateService(db);
            var listing = await svc.CreateAsync(NewListing());

            var ok = await svc.RequestPickupAsync(listing.Id, "recycler-1");

            Assert.True(ok);
            var fromDb = await db.RecycleListings.Include(l => l.Applicants).FirstAsync(x => x.Id == listing.Id);
            Assert.Equal(ListingStatus.PendingAcceptance, fromDb!.Status);
            Assert.Null(fromDb.AssignedRecyclerUserId);
            Assert.Contains(fromDb.Applicants, a => a.RecyclerUserId == "recycler-1");
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

            var ok = await svc.AcceptPickupAsync(listing.Id, listing.CreatedByUserId, "recycler-1");

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

            var ok = await svc.AcceptPickupAsync(listing.Id, "someone-else", "recycler-1");

            Assert.False(ok);
        }

        [Fact]
        public async Task StartChatAsync_Succeeds_When_Accepted()
        {
            using var db = CreateDb();
            var svc = CreateService(db);
            var listing = await svc.CreateAsync(NewListing());
            await svc.RequestPickupAsync(listing.Id, "recycler-1");
            await svc.AcceptPickupAsync(listing.Id, listing.CreatedByUserId, "recycler-1");

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
            await svc.AcceptPickupAsync(listing.Id, "donator-1", "recycler-1");
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
            await svc.AcceptPickupAsync(listing.Id, "donator-1", "recycler-1");

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
            await svc.AcceptPickupAsync(listing.Id, "donator-1", "recycler-1");
            await svc.StartChatAsync(listing.Id, "chat-1");

            Assert.False(await svc.SetMeetingPointAsync(listing.Id, "donator-1", -91m, 0m));
            Assert.False(await svc.SetMeetingPointAsync(listing.Id, "donator-1", 0m, 181m));
        }

        [Fact]
        public async Task ConfirmPickupAsync_Succeeds_When_Accepted_And_Donator_Matches_And_Meeting_Set()
        {
            using var db = CreateDb();
            var svc = CreateService(db);
            var listing = await svc.CreateAsync(NewListing(donatorId: "donator-1"));
            await svc.RequestPickupAsync(listing.Id, "recycler-1");
            await svc.AcceptPickupAsync(listing.Id, "donator-1", "recycler-1");
            await svc.StartChatAsync(listing.Id, "chat-1");
            await svc.SetMeetingPointAsync(listing.Id, "donator-1", 10m, 10m);

            var ok = await svc.ConfirmPickupAsync(listing.Id, "donator-1");

            Assert.True(ok);
            var fromDb = await db.RecycleListings.FindAsync(listing.Id);
            Assert.Equal(ListingStatus.Completed, fromDb!.Status);
            Assert.NotNull(fromDb.PickupConfirmedAt);
        }

        [Fact]
        public async Task ConfirmPickupAsync_Fails_When_Not_Owner_Or_Wrong_Status_Or_Missing_Chat_Meeting()
        {
            using var db = CreateDb();
            var svc = CreateService(db);
            var listing = await svc.CreateAsync(NewListing(donatorId: "donator-1"));
            await svc.RequestPickupAsync(listing.Id, "recycler-1");
            await svc.AcceptPickupAsync(listing.Id, "donator-1", "recycler-1");

            // Wrong owner
            Assert.False(await svc.ConfirmPickupAsync(listing.Id, "someone-else"));

            // Missing chat/meeting
            Assert.False(await svc.ConfirmPickupAsync(listing.Id, "donator-1"));

            // Setup and move status beyond Accepted
            await svc.StartChatAsync(listing.Id, "chat-1");
            await svc.SetMeetingPointAsync(listing.Id, "donator-1", 10m, 10m);
            await svc.ConfirmPickupAsync(listing.Id, "donator-1");
            Assert.False(await svc.ConfirmPickupAsync(listing.Id, "donator-1"));
        }

        [Fact]
        public async Task EndToEnd_Recycle_Process_Completes_Successfully_Without_Finalize()
        {
            using var db = CreateDb();
            var svc = CreateService(db);

            // 1. Donator creates listing
            var listing = await svc.CreateAsync(NewListing(donatorId: "donator-1"));

            // 2. Recycler requests pickup
            Assert.True(await svc.RequestPickupAsync(listing.Id, "recycler-1"));

            // 3. Donator accepts
            Assert.True(await svc.AcceptPickupAsync(listing.Id, "donator-1", "recycler-1"));

            // 4. Start chat
            Assert.True(await svc.StartChatAsync(listing.Id, "chat-xyz"));

            // 4.1 Donator sets meeting point
            Assert.True(await svc.SetMeetingPointAsync(listing.Id, "donator-1", 55.6761m, 12.5683m));

            // 5. Donator confirms pickup
            Assert.True(await svc.ConfirmPickupAsync(listing.Id, "donator-1"));
        }
    }
}
