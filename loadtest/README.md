This folder contains k6 load-test scripts for PantmigService.

Prerequisites
- Install k6: https://k6.io/docs/getting-started/installation
- Ensure the API is reachable (set `BASE_URL`).

Environment variables (examples)
- BASE_URL - e.g. http://localhost:5000
- DONATOR_USER / DONATOR_PASS OR DONATOR_TOKEN
- RECYCLER_USER / RECYCLER_PASS OR RECYCLER_TOKEN
- TEST_LISTING_ID - used by receipt upload script (default1)
- RECEIPT_FILE - path to image for upload (default: loadtest/assets/receipt.jpg)

Quick run examples
- Smoke (default scenario in `listings.js`):
 k6 run loadtest/scripts/listings.js

- Run receipt upload test (ensure TEST_LISTING_ID exists):
 k6 run loadtest/scripts/receipt_upload.js

Customization
- Edit `loadtest/k6-config.json` to change thresholds and default base URL.
- The auth helper tries to use `${ROLE}_TOKEN` first; if missing it will POST to `${BASE_URL}/auth/login` with `{username,password}` and expect a JSON token in `token` or `access_token`.

Notes
- These scripts are templates: adapt login endpoint, fields, and object shapes to your real auth API and production data.
- For heavy tests, run against a non-production environment with a dedicated DB and stub expensive services (AV scanner) if needed.