# Pantmig Service

## Recycle Listing Workflow (Updated)

This document describes the current recycle listing workflow after refactoring.

### Overview
A Donator publishes a listing that Recyclers can request. Once a Donator accepts a Recycler, they start a chat, set a meeting point and finally confirm the pickup. The pickup confirmation now completes (finalizes) the listing. A Recycler may optionally upload a receipt image afterwards for record keeping � uploading a receipt does not alter the listing status.

### States (Enum `ListingStatus`)
The enum still contains historical values but only these transitions are active now:

```
Created -> PendingAcceptance -> Accepted -> Completed (at pickup confirm)
```

Other enum values (`PickedUp`, `AwaitingVerification`) remain for backward compatibility but are not used by the new flow. They may be removed in a future migration if desired.

### Endpoint Flow
1. POST /listings
   - Donator (policy: VerifiedDonator) creates a listing (Status: Created)
2. POST /listings/pickup/request
   - Recycler requests pickup; first request moves status to PendingAcceptance
3. GET /listings/{id}/applicants
   - Donator views list of applicants
4. POST /listings/pickup/accept
   - Donator accepts a Recycler (Status: Accepted)
5. POST /listings/chat/start
   - Donator or assigned Recycler starts a chat (chatSessionId stored)
6. POST /listings/meeting/set
   - Donator sets meeting point (latitude/longitude)
7. POST /listings/pickup/confirm
   - Donator confirms pickup; this now directly sets Status = Completed, IsActive = false

### Optional Receipt Handling
- POST /listings/receipt/upload (multipart/form-data)
  - Allowed any time after a Recycler has been assigned (even after completion)
  - Stores binary data (and reported amount) but does not change listing status
  - Only checks that the caller is the assigned Recycler and the file passes antivirus scan

### Removed Endpoints
- POST /listings/receipt/submit (URL based)
- POST /listings/receipt/verify
- POST /listings/finalize (pickup confirmation replaces finalize)

### Still Available Utility Endpoints
- GET /listings (active listings)
- GET /listings/my-applications (Recycler)
- GET /listings/my-listings (Donator)
- GET /listings/{id}
- POST /listings/cancel (Donator can cancel if not already terminal)

### Service Interface Changes
Removed methods:
- SubmitReceiptAsync
- VerifyReceiptAsync
- FinalizeAsync

Modified behavior:
- ConfirmPickupAsync now completes the listing.
- SubmitReceiptUploadAsync only stores receipt data; no state transition.

### Data Model Notes
The entity still contains fields for receipt verification and intermediate states (e.g. VerifiedAmount, Status values). They are unused by the new flow but retained to avoid immediate migrations. A future cleanup might:
- Remove unused enum members
- Drop VerifiedAmount / receipt verification fields
- Rename fields to reflect simplified workflow

### Tests
Tests were updated to reflect the new completion point at pickup confirmation and removal of receipt submit / verify / finalize flows.

### Future Cleanup (Optional)
If you want to fully streamline the model:
- Remove ListingStatus values PickedUp and AwaitingVerification
- Remove VerifiedAmount and any verification timestamps
- Possibly collapse PickupConfirmedAt and CompletedAt into one field

Open an issue or task before performing that migration.

---
Last updated: refactor to immediate completion on pickup confirmation.
