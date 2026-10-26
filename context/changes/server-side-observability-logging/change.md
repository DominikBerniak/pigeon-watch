---
change_id: server-side-observability-logging
title: Server-side observability logging from the 2026-10-25 audit
status: implementing
created: 2026-10-25
updated: 2026-10-26
archived_at: null
---

## Notes

Implement fixes from context/audits/observability/2026-10-25_auth-sightings.md steps 1-5 (API only): logging scope with user id + trace id, request summary, EF log level, shared error-to-response helper that logs original Identity/Result codes (keeping anti-enumeration generic client answers), user store branch logging, DB-unavailable handler structured SQL number/path and timeout split, auth outcome logging and rate-limit OnRejected. Each fix starts with a failing test. SPA reporting seam (step 6) and polish (step 7) are separate later changes.
