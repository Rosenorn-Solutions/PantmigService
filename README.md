# Pantmig Service

A .NET 9 recycling coordination platform where Donators publish recyclable item listings and Recyclers request, coordinate pickup via a restricted chat, and complete the transaction. The listing now completes immediately when the Donator confirms pickup.

---
## Table of Contents
1. Quick Overview
2. Tech Stack
3. Solution Structure
4. Getting Started
5. Configuration & Environment
6. Database & Migrations
7. Running / Debugging
8. API & Workflow (Core Domain)
9. Coding & Architectural Conventions (Summary)
10. Testing
11. Logging & Observability
12. Security Notes
13. Roadmap / Future Cleanup
14. Troubleshooting

---
## 1. Quick Overview
Pantmig enables:
- Donators to create active recycle listings.
- Recyclers to apply to pick them up.
- A single accepted Recycler to coordinate via a private chat (SignalR).
- Meeting point set by the Donator.
- Pickup confirmation finalizes the listing (Status = Completed).
- Optional receipt image upload (no status change; archival only).

---
## 2. Tech Stack
Core:
- .NET 9, C# 13 (Minimal APIs)
- ASP.NET Core + Swagger / OpenAPI
- EF Core (SQL Server)
- SignalR (real-time chat)
- JWT Bearer Authentication
- Authorization policies (e.g., VerifiedDonator)
- Serilog structured logging (console + optional MSSQL sink)

Supporting / Present (may expand):
- MassTransit (RabbitMQ) in AuthService (foundation for eventual messaging/events)
- Antivirus scanning via ClamAV integration for receipt uploads

---
## 3. Solution Structure
Projects:
- PantmigService/  (Primary API, Listings, Chat, Cities)
- AuthService/     (User auth, identity helpers, JWT issuance)
- PantMigTesting/  (Test project placeholder)

Key Folders (PantmigService):
- Entities/        Domain entities (RecycleListing, Applicants, ChatMessage, City, etc.)
- Endpoints/       Minimal API endpoint mapping extensions
- Services/        Business logic (e.g., IRecycleListingService)
- Hubs/            SignalR ChatHub
- Infrastructure/  Antivirus, city resolution, seeding
- Migrations/      EF Core migrations (generated)

---
## 4. Getting Started
Prerequisites:
- .NET 9 SDK (preview / current as required)
- SQL Server (LocalDB or full instance). For quick dev: (localdb)\\MSSQLLocalDB
- (Optional) Docker if you want to run SQL Server in a container

Clone & Restore:
1. git clone <repo-url>
2. cd PantmigService
3. dotnet restore

Create Database (first time):
- Update connection string in appsettings.Development.json (see below)
- Apply migrations: dotnet ef database update --project PantmigService

Run API:
- dotnet run --project PantmigService
- Swagger UI: https://localhost:<port>/swagger

Auth Service (if needed):
- dotnet run --project AuthService

---
## 5. Configuration & Environment
Primary configuration: appsettings.json + appsettings.Development.json

Important keys (PantmigService):
- ConnectionStrings:PantmigConnection
- Jwt:Authority / Issuer / Audience (if validating token issuer)
- ClamAV:Host / Port (if antivirus enabled)
- Logging:Serilog sinks configuration

Sample (partial) appsettings.Development.json snippet (do NOT commit secrets):
```
{
  "ConnectionStrings": {
    "PantmigConnection": "Server=(localdb)\\MSSQLLocalDB;Database=PantmigDb;Trusted_Connection=True;TrustServerCertificate=True"
  },
  "ClamAV": {
    "Host": "localhost",
    "Port": 3310
  }
}
```

Environment Variables (override):
- ASPNETCORE_ENVIRONMENT=Development
- ConnectionStrings__PantmigConnection=...

---
## 6. Database & Migrations
Add a migration (example):
- dotnet ef migrations add AddSomeFeature --project PantmigService
Apply migrations:
- dotnet ef database update --project PantmigService

Guidelines:
- Use clear names (AddMeetingPointColumns)
- Keep backward compatible when possible
- Prefer Fluent API over data annotations if constraints grow

Seeding:
- Postal code CSV optionally ingested on startup (postal_codes_da.csv if present in output directory)

---
## 7. Running / Debugging
Run all services (if both needed):
- dotnet run --project AuthService
- dotnet run --project PantmigService

Swagger UI available only in Development environment.
SignalR Hub endpoint: /hubs/chat

JWT Auth:
- Obtain token from AuthService (implementation specific) or stub via dev tools.
- Supply Authorization: Bearer <token>

CORS:
- A named policy FrontendCors is applied early (configure origins in Program.cs if adjusting frontend host).

---
## 8. API & Workflow (Core Domain)
Active lifecycle (simplified after refactor):
```
Created -> PendingAcceptance -> Accepted -> Completed (pickup confirmation)
```
Legacy enum members (PickedUp, AwaitingVerification) remain for backward compatibility and may be pruned later.

Endpoint Flow:
1. POST /listings                       (create; Donator; Status: Created)
2. POST /listings/pickup/request        (Recycler applies; first moves to PendingAcceptance)
3. GET  /listings/{id}/applicants       (Donator views applicants)
4. POST /listings/pickup/accept         (Donator selects Recycler; Status: Accepted)
5. POST /listings/chat/start            (Either accepted party; stores chatSessionId)
6. POST /listings/meeting/set           (Donator sets geo point)
7. POST /listings/pickup/confirm        (Donator confirms; Status -> Completed; IsActive = false)

Optional Receipt Handling:
- POST /listings/receipt/upload (multipart)
  - Allowed after assignment (even post-completion)
  - Stored binary; no status mutation
  - Antivirus scan enforced

Removed / Deprecated Endpoints:
- /listings/receipt/submit
- /listings/receipt/verify
- /listings/finalize (pickup confirmation replaces)

Service Interface Changes:
- Removed: SubmitReceiptAsync, VerifyReceiptAsync, FinalizeAsync
- Modified: ConfirmPickupAsync now finalizes listing
- SubmitReceiptUploadAsync: only persists receipt data

Data Model Legacy Fields (still present, may remove later):
- VerifiedAmount
- Status members not used in new flow

---
## 9. Coding & Architectural Conventions (Summary)
- Minimal APIs; endpoint mapping extensions grouped logically
- Async all the way; cancellation propagated (CancellationToken)
- Structured logging (Serilog) with contextual properties
- Authorization: derive user id from claims (NameIdentifier or sub)
- Use ProblemDetails style responses for errors (400–500)
- Entities loaded with AsNoTracking unless updating
- Domain rules enforced inside service layer (IRecycleListingService)
- Chat hub restricts access to listing creator + assigned recycler only
- Keep hub methods thin: authorize, validate state, persist, broadcast

---
## 10. Testing
Framework recommendations:
- xUnit + FluentAssertions (PantMigTesting project placeholder)
Testing Focus:
- Service layer invariants (status transitions, authorization checks)
- Edge cases (double accept, confirm without meeting point if business rule requires, etc.)
- Optionally integrate an EF Core InMemory relational provider for read/write tests

Potential Future Additions:
- Integration tests with WebApplicationFactory
- SignalR hub tests (using TestServer)

---
## 11. Logging & Observability
- Serilog configured in Program.cs (console + optional MSSQL sink)
- Use structured templates: logger.LogInformation("Pickup confirmed {ListingId}", id)
- Consider adding request logging middleware and correlation ids later

---
## 12. Security Notes
- Always derive user identity from JWT claims; never trust client-sent IDs
- Policies (e.g., VerifiedDonator) restrict creation actions
- Chat endpoints and HUB require authenticated users
- Receipt upload scanned by ClamAV before persistence
- Avoid logging PII or raw tokens

---
## 13. Roadmap / Future Cleanup
Potential cleanup tasks:
- Remove unused ListingStatus values (PickedUp, AwaitingVerification)
- Drop verification-era columns (VerifiedAmount, timestamps) if no longer needed
- Consolidate completion timestamps (PickupConfirmedAt vs CompletedAt)
- Introduce events (MassTransit) for listing lifecycle activities
- Add caching for Cities / Postal Codes
- Add auditing (CreatedBy/UpdatedAt interceptors)

---
## 14. Troubleshooting
Port / SSL Issues:
- Delete obj/bin; ensure dev certificate trusted: dotnet dev-certs https --trust

Database Errors:
- Confirm connection string; run migrations again

JWT Validation Failures:
- Ensure Issuer/Audience match token
- Check system clock skew

ClamAV Timeouts:
- Verify daemon host/port; disable scanner in dev by supplying unreachable host only if explicitly handled

SignalR Connection Fails:
- Confirm correct hub URL (/hubs/chat) and Bearer token present

---
## Contribution Guidelines (Lightweight)
1. Open issue for non-trivial changes
2. Branch naming: feature/<short-desc>, fix/<short-desc>
3. Keep PRs focused; update README if altering workflow
4. Add/adjust tests for business rule changes

---
Last updated: workflow finalized at pickup confirmation (immediate completion design).
