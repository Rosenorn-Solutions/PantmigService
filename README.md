# Pantmig Service Platform

Composed of two cooperating .NET 9 microservices plus a test project that together implement a recycle listing workflow between Donators (who have reusable deposit items) and Recyclers (who pick them up, recycle, and optionally upload a receipt).

## Contents
- [High-Level Overview](#high-level-overview)
- [Key Features](#key-features)
- [Project Structure](#project-structure)
- [Domain & Workflow](#domain--workflow)
- [Core Entities](#core-entities)
- [Architecture & Technologies](#architecture--technologies)
- [Authentication & Authorization](#authentication--authorization)
- [Listings API (Lifecycle Walkthrough)](#listings-api-lifecycle-walkthrough)
- [Chat (SignalR)](#chat-signalr)
- [Receipt Upload & Antivirus Scanning](#receipt-upload--antivirus-scanning)
- [City & Postal Code Normalization](#city--postal-code-normalization)
- [Configuration](#configuration)
- [Local Development Setup](#local-development-setup)
- [Database & Migrations](#database--migrations)
- [Running & Debugging](#running--debugging)
- [Testing](#testing)
- [Logging & Observability](#logging--observability)
- [Performance Notes](#performance-notes)
- [Error Handling Conventions](#error-handling-conventions)
- [Security Considerations](#security-considerations)
- [Extending The System](#extending-the-system)
- [Contribution Guidelines](#contribution-guidelines)
- [Roadmap / Future Ideas](#roadmap--future-ideas)
- [License](#license)

---
## High-Level Overview
Pantmig is an open source backend for coordinating environmentally friendly recycling pickups. Donators list collections of refundable deposit items (bottles, cans, etc.). Recyclers apply, get accepted, arrange a meeting, and optionally upload the recycling receipt.

Two main bounded contexts:
1. AuthService: Identity, JWT issuance, refresh tokens, role & (future) verification logic, light user profile & rating lookup.
2. PantmigService: Core domain (Listings, Applicants, Chat Messages, Cities, Postal Codes) + real?time chat via SignalR.

A test project (PantMigTesting) covers unit and endpoint flows with in?memory infrastructure substitutes.

---
## Key Features
- Minimal APIs (fast, low ceremony) with OpenAPI/Swagger documentation.
- JWT auth with access & refresh token rotation.
- Role-based policy (VerifiedDonator) gating listing creation & owner operations.
- Recycle listing lifecycle with explicit status transitions & invariants.
- Applicant management (recycler requests -> donor accepts).
- Real-time 1:1 listing chat (SignalR) restricted to Donator + assigned Recycler.
- Meeting point geocoordinates set post chat start.
- Receipt image upload (multipart/form-data) with ClamAV scanning (configurable / pluggable scanner).
- City & postal code normalization and seeding from CSV.
- Rich indexing strategy in EF Core to optimize frequent query patterns.
- Serilog structured logging (console + optional SQL Server sink).
- Unit & integration style tests for critical service and endpoint logic.

---
## Project Structure
| Project | Purpose |
|---------|---------|
| PantmigService | Core domain & listing API + SignalR Chat + EF Core migrations |
| AuthService | Identity (ASP.NET Core Identity), JWT issuance, refresh tokens, user lookups, role policies |
| PantMigTesting | Automated tests (xUnit) for domain services and Minimal API endpoints |

---
## Domain & Workflow
Listing Status progression (single forward path, some guarded transitions):
1. Created – Donator just created the listing.
2. PendingAcceptance – At least one recycler has requested pickup.
3. Accepted – Donator accepted one recycler (AssignedRecyclerUserId set); chat can start.
4. (Optional intermediate states reserved: PickedUp / AwaitingVerification not currently advanced explicitly – current flow jumps to Completed upon pickup confirmation.)
5. Completed – Donator confirmed pickup (listing becomes inactive).
6. Cancelled – Donator cancelled listing before completion.

Invariants enforced inside service layer:
- Only active listings in Created can accept pickup requests.
- Accepting a recycler only allowed from PendingAcceptance by the listing owner.
- Chat can only start when status == Accepted.
- Meeting point requires chat started and owner permission.
- Pickup confirmation requires Accepted, chat started, and meeting point set; it terminally completes listing.
- Receipt upload allowed even after completion (so recycler can submit proof later) but only by the assigned recycler.
- Cancel disallowed after Completed / Cancelled and only by owner.

---
## Core Entities
- RecycleListing (aggregate root) – Items, Applicants, status, city, meeting point, receipt data.
- RecycleListingItem – Structured material lines (type, quantity, optional deposit metadata).
- RecycleListingApplicant – Recycler application (ListingId + RecyclerUserId + timestamp).
- ChatMessage – Persisted per listing chat history (capped query on join).
- City & CityPostalCode – Normalized geographic reference model.
- ApplicationUser (AuthService) – Identity + profile (role, rating, optional demographics).
- RefreshToken – Rotation model for long-lived sessions.

Computed convenience:
- ApproximateWorth: Items.Sum(Quantity) * 2.33 default heuristic.

---
## Architecture & Technologies
- .NET 9 / C# 13 features enabled (file-scoped namespaces, collection expressions, etc.).
- ASP.NET Core Minimal APIs + Endpoint grouping & OpenAPI metadata.
- EF Core (SQL Server provider) with code-first migrations.
- SignalR for real-time listing chat.
- ASP.NET Core Identity in AuthService.
- JWT Bearer authentication (symmetric signing key) + refresh token persistence.
- Serilog for structured logging (console + SQL sink).
- Optional ClamAV (nClam) antivirus scanning for uploaded receipt images (scanner abstraction for swap/mock).
- In-memory DB & custom auth handler for test project.

---
## Authentication & Authorization
Roles: Donator, Recycler.
Policy: VerifiedDonator (currently requires role Donator; extensible to also require strong identity verification claim).

AuthService issues:
- Access token (short-lived; minutes configured).
- Refresh token (longer-lived; persisted; rotation endpoint provided).

Standard claims included: sub, nameidentifier, email, role, userType, isMitIdVerified, optional city, gender, birthDate.

PantmigService trusts upstream JWT and enforces policies at endpoints.

---
## Listings API (Lifecycle Walkthrough)
1. Donator (VerifiedDonator) POST /listings creates listing with items.
2. Recycler POST /listings/pickup/request to apply; status moves to PendingAcceptance.
3. Donator GET /listings/{id}/applicants to view applicants.
4. Donator POST /listings/pickup/accept chooses recycler; status -> Accepted.
5. Either participant POST /listings/chat/start (authorization ensures participant) to persist chat id.
6. Donator POST /listings/meeting/set sets coordinates.
7. Donator POST /listings/pickup/confirm finalizes -> Completed (inactivates listing).
8. Assigned recycler (any time after acceptance, including after completion) POST /listings/receipt/upload (multipart) to attach receipt bytes & reported amount.
9. Donator POST /listings/cancel allowed before completion to end listing -> Cancelled.

Query endpoints:
- GET /listings (active) – only Created + PendingAcceptance, descending creation.
- GET /listings/{id}
- GET /listings/my-listings (donator) – all owned listings.
- GET /listings/my-applications (recycler) – listings user has applied to.

---
## Chat (SignalR)
Hub: /hubs/chat (authorized).
Group naming: listing-{listingId} (persisted in listing.ChatSessionId).
Join rules: Only donator or assigned recycler; status must be Accepted or later (not Cancelled). Last 50 messages returned to caller on join.
Client methods:
- JoinListingChat(listingId)
- SendMessage(listingId, text)
- LeaveListingChat(listingId)
Events:
- Joined (includes backfilled history)
- ReceiveMessage (new message broadcast)
- Left

---
## Receipt Upload & Antivirus Scanning
Endpoint: POST /listings/receipt/upload (multipart/form-data)
Fields: listingId (int), reportedAmount (decimal), file (image/*)
Flow:
1. File streamed to memory.
2. Antivirus scanner abstraction invoked (ClamAV by default; NoOp in tests).
3. Infected -> 400 ProblemDetails (blocked); Error -> 503.
4. On success, binary & metadata stored on RecycleListing (ReceiptImageBytes, ContentType, FileName, ReportedAmount).
5. Upload does not alter status.

Abstraction: IAntivirusScanner (easy to replace with external scanning service or queue-based workflow).

---
## City & Postal Code Normalization
Both services can seed postal_codes_da.csv. When creating listings (PantmigService) or registering users (AuthService), free-text city inputs are normalized (create or reuse existing City row) via CityResolver.
Search endpoint: GET /cities/search?q=... performs name contains or postal code prefix match; returns limited set with aggregated postal codes.

---
## Configuration
Environment variables or appsettings.json keys (illustrative):

ConnectionStrings:
  PantmigConnection: Server=...;Database=Pantmig;Trusted_Connection=True;TrustServerCertificate=True

JwtSettings:
  SecretKey: <long-random-32+ chars>
  Issuer: pantmig-auth
  Audience: pantmig-clients
  AccessTokenMinutes: 60
  RefreshTokenDays: 30

Cors:
  AllowedOrigins: ["https://localhost:5173", "https://app.example.com", "https://*.pantmig.dk"]

ClamAV:
  Enabled: true|false
  Host: localhost
  Port: 3310
  MaxFileSizeBytes: (optional)

Cache:
  UserRatingSeconds: 300

Serilog (if using appsettings): configure sinks or rely on programmatic configuration already present.

---
## Local Development Setup
Prerequisites:
- .NET SDK 9.x
- SQL Server (localdb, container, or full instance)
- Optional: Running ClamAV daemon (if scanning enabled)

Steps:
1. Clone repository.
2. Create / update appsettings.Development.json in each service with ConnectionStrings & JwtSettings.
3. Apply migrations (first run will auto-create DB if not existing). Example:
   - cd AuthService && dotnet ef database update
   - cd ../PantmigService && dotnet ef database update
4. (Optional) Ensure postal_codes_da.csv exists in each project root for seeding.
5. Start services (from solution root):
   - dotnet run --project AuthService
   - dotnet run --project PantmigService
6. Navigate to Swagger UIs:
   - Auth: https://localhost:<auth-port>/swagger
   - Listings: https://localhost:<listing-port>/swagger
7. Register a user (Donator) -> capture JWT -> authorize subsequent listing endpoints.

Tips:
- Use separate terminals for each service.
- Ensure Issuer/Audience values match across services (PantmigService must validate tokens from AuthService).

---
## Database & Migrations
EF Core code-first. Migrations live under each project’s Migrations folder. Typical flow for schema change (e.g., new column on RecycleListing):
1. Modify entity model.
2. dotnet ef migrations add MeaningfulName --project PantmigService --startup-project PantmigService
3. dotnet ef database update

Naming guidance: Use clear intent-based names (AddMeetingPoint, ListingReceiptBytes, etc.). Avoid ambiguous terms.

---
## Running & Debugging
- Launch each service independently (they are decoupled through JWT boundary; no hard runtime dependency aside from authentication).
- If introducing cross-service messaging (future), add local RabbitMQ container (currently interface placeholders exist but not wired into listing flow).
- Configure breakpoints in endpoints or services; Minimal APIs allow fast iteration.

---
## Testing
Test project: PantMigTesting
- Uses in-memory EF Core provider and custom header-based auth handler.
- Covers service logic (RecycleListingServiceTests) & HTTP endpoint flows (RecycleListingEndpointsTests, ReceiptUploadEndpointsTests).

Run tests:
```
dotnet test
```

Add more tests by following existing patterns:
- For endpoints: spin up TestServer with test DI overrides.
- For services: isolate with in-memory context & NullLogger.

---
## Logging & Observability
Serilog configured programmatically:
- Console sink for local dev.
- MSSqlServer sink (Logs table auto-created) if connection string provided.

Guidelines:
- Use structured logging templates (Listing {ListingId} created...).
- Avoid PII in log message bodies.
- Use Information for domain events, Warning for business rule violations, Error for unexpected exceptions.

---
## Performance Notes
Implemented indexes (see DbContext) for: status filters, CreatedByUserId, composite active/status, city/time ranges, applicant uniqueness, chat message ordering, etc.

Other strategies:
- Use AsNoTracking for read queries (already applied).
- Limit chat history retrieval (last 50 messages).
- Project only needed columns for search endpoints.
- Potential future: caching city search or introducing pagination for active listings.

---
## Error Handling Conventions
- Standard JSON ProblemDetails for 4xx/5xx business rule or unexpected errors.
- 400 for validation / business rule failure (e.g., invalid status transition).
- 401 for unauthenticated, 403 for authenticated but not authorized (e.g., non-participant chat attempt).
- Sanitized messages (internal details not leaked).

---
## Security Considerations
- Never trust client userId: always derive from JWT claims (FindFirstValue(NameIdentifier) or sub).
- Policy VerifiedDonator restricts high-impact mutations (create, accept, meeting set, confirm, cancel).
- Receipt AV scanning prevents malicious content ingestion (pluggable; consider asynchronous scanning at scale).
- HTTPS redirection enforced by default templates; keep enabled.
- Future: Consider rate limiting (per-IP / per-user) for listing creation & chat message flood control.
- Future: Consider enabling strict content security headers if a unified gateway is introduced.

---
## Extending The System
When adding a new capability:
1. Model: Extend entity + migration.
2. Service: Add method with validation & logging.
3. Endpoint: Minimal API route with OpenAPI metadata + auth policy.
4. Tests: Add service unit test + endpoint integration test.
5. Docs: Update README & (if applicable) API reference.

Checklist (abbreviated): validation, authorization, invariant protection, logging, cancellation token, ProblemDetails, tests, indexes (if query patterns change), performance considerations.

---
## Contribution Guidelines
1. Fork & branch (feature/<short-description>).
2. Keep PRs focused & small; include rationale in description.
3. Follow logging & naming conventions (PascalCase methods, _camelCase private fields).
4. Avoid broad refactors in feature PR unless necessary.
5. All new logic requires tests (service or endpoint level) + docs update.
6. Run dotnet test before submitting.
7. Ensure EF migrations compile and apply (include generated migration files when schema changed).
8. Avoid introducing breaking API changes without deprecation notes.

Issue Reporting:
- Provide reproduction steps, expected vs actual behavior, environment details.
- Tag whether it is Bug, Enhancement, Question, or Documentation.

---
## Roadmap / Future Ideas
- Recycler reputation weighting & automated acceptance suggestions.
- Rate limiting & anti-spam for chat.
- Optional push notifications (SignalR backplane or external messaging).
- Outbox/inbox patterns for consistent cross-service events (e.g., awarding eco points).
- Geo distance filtering for listing discovery.
- Image optimization / external storage (currently bytes stored directly).
- Soft deletion & auditing (CreatedBy, UpdatedBy, timestamps via SaveChanges interceptor).
- Metrics integration (Prometheus/OpenTelemetry).
- Multi-language city/postal support.

---
## License
Suggested: MIT (add LICENSE file if not already present).

---
## Quick Start (TL;DR)
```
# 1. Configure appsettings with ConnectionStrings + JwtSettings
# 2. Apply migrations
cd AuthService && dotnet ef database update
cd ../PantmigService && dotnet ef database update
# 3. Run services
 dotnet run --project AuthService
 dotnet run --project PantmigService
# 4. Register Donator via Auth swagger -> copy JWT
# 5. Authorize in PantmigService swagger -> create / manage listings
# 6. Run tests
 dotnet test
```

---
Maintained with a focus on clarity, correctness, and testability. Contributions welcome.
