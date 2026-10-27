# SPA error reporting seam — Plan Brief

> Full plan: `context/changes/spa-error-reporting-seam/plan.md`

## What & Why

The API now logs its own failures with a trace id and user id, but every failure the Angular SPA swallows (and every uncaught, bootstrap and lazy-route error) still dies in the user's console. This change adds one SPA reporter that sends small, PII-free reports to a new API endpoint, where they become structured log lines, and shows users a reference id they can quote. It closes the SPA half of the 2026-10-26 observability verify audit.

## Starting Point

The SPA has only `provideBrowserGlobalErrorListeners()`, about a dozen `catchError(() => of(...))` and bare `catch {}` sites, and an unexpected-error text with no id. The API already scopes every log line with `TraceId` and returns `traceId` in 500 ProblemDetails, but the SPA never reads it.

## Desired End State

A responder sees one structured log line per client-reported failure (Warning or Information, never Error), and a user with an unexpected error can read out a reference that matches a server log scope. Expected user conditions (wrong password, validation errors, missing sighting) stay silent.

## Key Decisions Made

| Decision | Choice | Why (1 sentence) | Source |
| --- | --- | --- | --- |
| Transport | Own anonymous API endpoint, no tracker SDK | Reuses the log stream and `TraceId` scope with no vendor or cost on the F1 tier. | Plan |
| Access and abuse | Anonymous, new per-IP policy (20/min), allowlisted fields, ~2 KB body cap | Bootstrap, login and warm-up failures happen before any token exists. | Plan |
| Scope | All open SPA audit findings through one reporter | One consistent mechanism closes A5, A6, A10, A11, S1, S4-S6, P2, P4, P1 residual. | Plan |
| Payload | Allowlist only: kind, context, status, error name, API traceId, route template, session id | No free text means no email, token or coordinates can reach the log. | Plan |
| Severity | Server maps a closed `kind`; Warning or Information, never Error | A client cannot raise Error-level noise or alerts. | Plan |
| Client limits | Dedupe by (kind, context, status), 20 reports per page load, never report the reporter's own failure | Bounds volume without timers and cannot loop. | Plan |
| User-facing id | "Reference: <traceId>" in generic unexpected-error states only | It is exactly where users hit dead ends and matches the server log scope. | Plan |
| Tests | SPA unit, API unit and integration, plus one Playwright e2e | Matches the repo's pyramid and proves the full path once. | Plan |

## Scope

**In scope:** the `POST client-errors` endpoint and its rate-limit policy; the SPA reporter, `ErrorHandler`, navigation error handler and bootstrap report; reporting from interceptors, core services and feature swallow sites; the reference-id UI and label; one e2e test; CLAUDE.md updates.

**Out of scope:** error tracker SDKs; messages, stacks, URLs or bodies in reports; any change to existing responses, retries or refresh behaviour; client-chosen severity; reporting of expected user conditions; forwarded-headers and production CORS config; version stamping, second probe and `UiResourceService` log (audit P5, P6, A13); alerts and dashboards.

## Architecture / Approach

```
SPA failure site → ErrorReportingService (allowlist, dedupe, cap) → POST client-errors (anonymous, per-IP limit)
                 → ClientErrorsController maps kind → level → structured log line (TraceId scope)
bootstrap failure (no DI) → navigator.sendBeacon → same endpoint
API failure with traceId → "Reference: <id>" in the unexpected-error UI
```

## Phases at a Glance

| Phase | What it delivers | Key risk |
| --- | --- | --- |
| 1. Report endpoint (API) | Anonymous `POST client-errors`, `clientErrors` rate-limit policy, kind-to-level mapping, tests | Architecture and endpoint-sweep rules (marker model, fixture, anonymous list) |
| 2. Reporter core and global handlers (SPA) | `ErrorReportingService`, `ErrorHandler`, navigation and bootstrap reporting | Initial bundle already near its budget |
| 3. Interceptors and core services (SPA) | Session-expired, warm-up exhaustion and fallback reports | Reporting must not change retry, refresh or logout behaviour |
| 4. Feature swallow sites and reference id UI (SPA) | Page-level reports, shared reference element, label key, map and geolocation reports | Telling expected user conditions from unexpected failures at each site |
| 5. Playwright e2e and docs | One browser test for the full path, updated CLAUDE.md files | E2E needs a real API and credentials and has no CI job yet |

**Prerequisites:** the server-side logging change is on `main`; a local API and database for manual and e2e checks; e2e credentials in the Frontend `.env`.
**Estimated effort:** about 5-6 sessions across 5 phases.

## Open Risks & Assumptions

- Production CORS must already allow the production SPA origin through the platform setting (assumed from the deploy plan, not verifiable in the repo), otherwise browsers block the report.
- The rate limit partitions by remote IP and there is no forwarded-headers config, so behind the host all clients may share one bucket (existing, unverified assumption).
- A status-0 failure means the API is unreachable, so its report cannot be delivered; only 503-style failures behind a reachable API are visible.
- The initial bundle is near its budget; the reporter must stay small.

## Success Criteria (Summary)

- A client-reported failure shows up in the API log as one clean line with the expected level and no personal data, and repeats are deduped.
- A user who hits an unexpected error can quote a reference that matches a server log scope; wrong passwords and missing items show none.
- The open SPA findings in the audit can each be matched to a report kind or the reference UI, ready for a `--verify` run.
