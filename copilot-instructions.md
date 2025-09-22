# Copilot Instructions for PantmigService Repository

## 1. Solution Overview
Projects:
- PantmigService: Primary API (.NET 9, C# 13) using Minimal APIs + SignalR + EF Core (SQL Server).
- AuthService: Identity + username generation utilities.
- PantMigTesting: (Add tests here; prefer xUnit + FluentAssertions if expanded.)

Primary Domain: Recycle listings workflow between Donator and Recycler (creation -> pickup -> receipt -> verification -> completion) with a restricted chat channel (SignalR) once accepted.

## 2. Tech & Packages
- ASP.NET Core Minimal APIs
- EF Core (SQL Server)
- SignalR (real-time chat)
- Swashbuckle (Swagger/OpenAPI)
- Authentication: JWT Bearer; claims: NameIdentifier or `sub`.

## 3. Coding Conventions
- Language: C# 13 features allowed (file-scoped namespaces, primary constructors, collection expressions, etc.) but stay readable.
- Async everywhere: always use `async/await`; suffix async methods with `Async`.
- Cancellation: Pass `CancellationToken ct` through service layers; in Minimal API endpoints use `ctx.RequestAborted`.
- Nullability: Treat warnings seriously; prefer `?` for optional reference types.
- Logging: Use structured logging (`logger.LogInformation("Pickup confirmed for listing {ListingId}", id)`). Avoid string interpolation in the message template.
- Avoid throwing generic exceptions inside endpoints; convert to ProblemDetails responses.
- Keep hub methods small; validate authorization, state, persist, broadcast.

## 4. Entity / EF Core Guidelines
Entities (RecycleListing, RecycleListingApplicant, ChatMessage, City, CityPostalCode) live in PantmigService.Entities.
- Always query with `AsNoTracking()` unless modifying.
- When adding new entities: update `PantmigDbContext` DbSet + configure via Fluent API in OnModelCreating if needed.
- Migrations: name clearly (e.g., `dotnet ef migrations add ListingMeetingPoint`) and keep schema changes backward compatible when possible.

## 5. Recycle Listing Workflow (Key Invariants)
Statuses (Created -> Accepted -> PickedUp -> AwaitingVerification -> Completed OR Cancelled).
Rules:
- Chat allowed only from Accepted onward.
- Meeting point only after chat started and status == Accepted.
- Receipt submission only after PickedUp.
- Verification only after AwaitingVerification.
- Cancel only if not Completed/Cancelled.

Ensure any new mutations enforce status transitions atomically.

## 6. Services Layer (IRecycleListingService)
If extending service:
- Perform validation & authorization checks inside service where reuse is expected.
- Return `bool` for business rule failures (not exceptions) unless truly exceptional.
- Expose DTOs or read models if projection complexity grows (avoid over-fetching navigation collections).

## 7. Minimal API Endpoints
Pattern:
- Extract user id: `user.FindFirstValue(ClaimTypes.NameIdentifier) ?? user.FindFirstValue("sub")`.
- Validate input early; return `Results.Problem(...)` with proper status codes (400, 401, 403, 404, 500).
- Tag endpoints (`WithTags`), set OperationId, Summary, Description.
- For new endpoints keep consistent response types and add `.Produces<...>()` metadata.

## 8. SignalR Chat (ChatHub)
Join rules:
- Only listing creator or assigned recycler.
- Listing status must be Accepted, PickedUp, AwaitingVerification, or Completed.
- Persist chat session id if missing via `StartChatAsync`.
Send rules:
- Validate participant + chat started.
- Persist message then broadcast to group named by `ChatSessionId`.
Enhancements should:
- Support typing indicators or message deletion via new hub methods.
- Use group naming pattern `listing-{listingId}` consistently.

## 9. Security
- Never trust client-provided user IDs; derive from claims.
- Restrict donator-only actions via policy `VerifiedDonator`.
- When adding new policies, configure in Program.cs before `builder.Build()`.
- Ensure chat methods remain `[Authorize]` and do not leak listing details to non-participants.

## 10. Error Handling & Responses
- Prefer ProblemDetails for failures (400–500 range).
- Do not expose internal exception messages directly for security-sensitive areas; log full details, return sanitized message.

## 11. Logging
- Use category names that reflect area ("Listings", "Chat").
- Log warnings for business rule failures; errors for unhandled exceptions.
- Avoid logging PII.

## 12. Adding New Features (Checklist)
1. Define/extend entity & migration if persistence needed.
2. Add service method (interface + implementation) with cancellation token.
3. Add endpoint or hub method; follow auth + validation patterns.
4. Unit test service logic (mock DbContext or use InMemory provider for simple cases).
5. Update OpenAPI metadata.
6. Consider concurrency & race conditions (e.g., double accept, duplicate chat start) — enforce with transactional save or row version if needed.

## 13. Performance Notes
- Use `Take(n)` when returning lists with potential growth.
- Add indexes via migrations for frequently filtered columns (Status, CreatedByUserId, AssignedRecyclerUserId, ListingId on ChatMessages).
- Avoid N+1: include related data or project explicitly when needed.

## 14. Future Enhancements (Guidance)
- Add soft deletion flag if logical deletes are needed.
- Introduce read models / query objects for complex filtering.
- Add caching layer (e.g., MemoryCache) for Cities / Postal Codes.
- Consider auditing (Created/Updated metadata) via SaveChanges interceptor.

## 15. Style & Naming
- Methods: PascalCase.
- Private fields: `_camelCase`.
- Constants: `SCREAMING_SNAKE_CASE` only when truly constant.
- Groups/hub events: PascalCase event names (e.g., `ReceiveMessage`, `Joined`).

## 16. Testing Guidance
- Prefer deterministic tests: freeze time via abstraction if adding time-dependent logic.
- Use test data builders for entities instead of inline constructors if they grow.

## 17. Do / Avoid Summary
Do:
- Enforce domain invariants centrally.
- Validate inputs early.
- Use structured logging.
Avoid:
- Returning raw exceptions.
- Hardcoding user IDs.
- Blocking on async calls.

## 18. How Copilot Should Respond
When asked for changes:
- Identify target file(s) first (search if unsure).
- Provide minimal diff describing only changed parts.
- Keep responses concise and implementation-focused.
- Do not introduce unrelated refactors unless necessary for the change.

---
These instructions should guide consistent, secure, and maintainable contributions. Update this file when architectural decisions change.
