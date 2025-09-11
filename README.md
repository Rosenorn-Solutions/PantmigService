PantmigService

An ASP.NET Core (.NET 9, C# 13) backend for coordinating recycling handoffs between donators and recyclers. It exposes minimal APIs for listing lifecycle, uses EF Core with SQL Server, and provides real-time chat via SignalR.

Projects
- PantmigService: Main Web API and SignalR hub
- AuthService: Auth-related project (if used separately)
- PantMigTesting: xUnit integration/unit tests

Tech
- ASP.NET Core Minimal APIs + Swagger
- EF Core (SQL Server)
- SignalR (WebSockets)
- JWT Bearer authentication

Prerequisites
- .NET 9 SDK
- SQL Server (LocalDB or full SQL Server)

Quick start
1) Clone and open the solution.
2) Configure appsettings.json in PantmigService with a connection string and JWT settings:
{
  "ConnectionStrings": {
    "PantmigConnection": "Server=(localdb)\\MSSQLLocalDB;Database=PantmigDb;Trusted_Connection=True;MultipleActiveResultSets=true;TrustServerCertificate=True"
  },
  "Jwt": {
    "Issuer": "your-issuer",
    "Audience": "your-audience",
    "SecretKey": "your-very-long-secret"
  }
}
3) Apply database migrations (first restore and build):
- dotnet restore
- dotnet build -c Debug
- dotnet ef database update --project PantmigService
4) Run the API:
- dotnet run --project PantmigService
5) Browse Swagger UI:
- https://localhost:5001/swagger (port may differ)

Database and migrations
- Migrations live in PantmigService/Migrations
- Example migration: 20250831144839_RecycleMeetingPoint adds meeting point columns to RecycleListings
- Create a new migration:
  - dotnet ef migrations add <Name> --project PantmigService
- Update database:
  - dotnet ef database update --project PantmigService

Postal code seeding (optional)
- On startup, PantmigService attempts to seed postal codes from a CSV at the app base directory: postal_codes_da.csv
- Supported CSV formats (headers optional, lines starting with # ignored):
  - CityName,PostalCode
  - PostalCode,CityName
- To force reseed even when data exists: set env var PANTMIG_FORCE_POSTAL_SEED=true

Authentication
- JWT Bearer is required for protected endpoints
- Provide a token with sub (or nameidentifier) claim
- Some endpoints require policy: VerifiedDonator
- For SignalR, access_token can be passed in query string when connecting to /hubs/chat

SignalR
- Hub path: /hubs/chat
- JWT is read from access_token in query string for WebSockets

API overview (base: /listings)
- GET /listings
  - Returns active listings
- GET /listings/{id}
  - Returns a listing by id
- POST /listings (VerifiedDonator)
  - Body: { title, description, city?, location?, estimatedValue?, estimatedAmount, availableFrom, availableTo }
  - Creates a listing
- POST /listings/pickup/request (Authenticated)
  - Body: { listingId }
  - Recycler requests pickup. Adds the recycler to the applicants list and moves listing to PendingAcceptance.
- GET /listings/{id}/applicants (VerifiedDonator; must be creator)
  - Returns: [ "recyclerUserId1", "recyclerUserId2", ... ]
  - Donator views the list of applicants for the listing.
- POST /listings/pickup/accept (VerifiedDonator; must be creator)
  - Body: { listingId, recyclerUserId }
  - Donator accepts a selected applicant. Assigns AssignedRecyclerUserId and moves to Accepted.
- POST /listings/chat/start (Authenticated; only donator or assigned recycler)
  - Body: { listingId }
  - Starts direct chat for the listing
- POST /listings/meeting/set (VerifiedDonator; requires chat started)
  - Body: { listingId, latitude, longitude }
  - Sets meeting point
- POST /listings/pickup/confirm (Authenticated; assigned recycler only)
  - Body: { listingId }
  - Recycler confirms pickup
- POST /listings/receipt/submit (Authenticated; assigned recycler only)
  - Body: { listingId, receiptImageUrl, reportedAmount }
- POST /listings/receipt/verify (VerifiedDonator)
  - Body: { listingId, verifiedAmount }

End-to-end workflow (updated)
- 1. Create listing (donator)
  - Endpoint: POST /listings (VerifiedDonator)
  - State: Created, IsActive = true
  - Visible in GET /listings
- 2. Request pickup (recycler)
  - Endpoint: POST /listings/pickup/request (Auth)
  - Adds recycler to applicants, State: PendingAcceptance
  - Listing is no longer returned by GET /listings
- 2.5 View applicants (donator)
  - Endpoint: GET /listings/{id}/applicants (VerifiedDonator, creator)
  - Donator reviews applicants list
- 3. Accept pickup (donator)
  - Endpoint: POST /listings/pickup/accept (VerifiedDonator, creator)
  - Body includes recyclerUserId of the chosen applicant
  - State: Accepted, AcceptedAt set; AssignedRecyclerUserId set
- 4. Start chat (donator or assigned recycler)
  - Endpoint: POST /listings/chat/start (Auth, participants only)
  - Sets ChatSessionId = "listing-{id}"
- 4.1 Set meeting point (donator)
  - Endpoint: POST /listings/meeting/set (VerifiedDonator, creator; requires chat started)
  - Sets MeetingLatitude, MeetingLongitude, MeetingSetAt
- 5. Confirm pickup (assigned recycler)
  - Endpoint: POST /listings/pickup/confirm (Auth; assigned recycler)
  - State: PickedUp, PickupConfirmedAt set
- 6. Submit receipt (assigned recycler)
  - Endpoint: POST /listings/receipt/submit (Auth; assigned recycler)
  - Sets ReceiptImageUrl, ReportedAmount; State: AwaitingVerification
- 7. Verify receipt (donator)
  - Endpoint: POST /listings/receipt/verify (VerifiedDonator, creator)
  - Sets VerifiedAmount, CompletedAt; State: Completed, IsActive = false

Model updates (applicants)
- RecycleListing now has Applicants (collection of RecycleListingApplicant) and a convenience AppliedForRecyclementUserIdList (not mapped) listing applicant user IDs.
- New entity RecycleListingApplicant with unique index on (ListingId, RecyclerUserId) prevents duplicate applications.
- DbContext has DbSet<RecycleListingApplicant> and configured relationships with cascade delete.

Testing
- Run all tests: dotnet test
- PantMigTesting includes end-to-end tests that drive the minimal APIs and exercise the updated applicants flow

Configuration notes
- Swagger is enabled in Development
- Connection string key: ConnectionStrings:PantmigConnection
- JWT configuration must match Program.cs expectations (Issuer, Audience, SecretKey)

Repository structure (key folders)
- PantmigService/
  - Endpoints/ (Minimal APIs)
  - Services/ (Business logic)
  - Entities/ (EF entities)
  - Data/ (DbContext)
  - Hubs/ (SignalR ChatHub)
  - Seed/ (CSV seeding)
  - Migrations/
- PantMigTesting/ (tests)
- AuthService/ (auth project)

License
- Add your preferred license.
