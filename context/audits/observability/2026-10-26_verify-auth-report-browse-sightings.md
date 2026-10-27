---
type: observability-audit
date: 2026-10-26 18:42
mode: verify
commit: 854765a
branch: main
dirty_tree: false
areas: [auth, report-browse-sightings]
area_source: arguments
runtime_proof: partial
error_tracker: none (ILogger to stdout only; App Service log stream assumed)
previous_report: context/audits/observability/2026-10-25_auth-sightings.md
findings: { critical: 0, high: 4, medium: 4, low: 6 }
---

# Observability audit (verify): auth, report/browse sightings (2026-10-26)

Verify run of `2026-10-25_auth-sightings.md` after the `server-side-observability-logging` change (PR #24, squash commit `813c763`) landed on `main`. Every earlier finding was re-checked by re-reading the cited code. The Angular SPA has no changes since the audited commit (`git diff 69e3a35 HEAD -- PigeonWatch/Frontend` is empty), so every SPA finding is unchanged. No probe harness existed in the first report, so this run has no scripted before/after suite. Runtime evidence comes from a manual local run (see section 8) plus the new fake-logger integration tests.

## 1. TL;DR

- **Server side: 12 of 26 findings fixed, 1 partly fixed (P1), 13 still open, all on the SPA or in polish.** The API now writes structured, privacy-safe logs for Result-style failures, Identity codes, user-store branches, auth outcomes, 429s, stubbed routes and the 503 classifier. Every log line carries `TraceId` (and `UserId` once authenticated), and each request leaves one summary line.
- **Open: the SPA reporting seam (root cause 4).** All 4 remaining high findings (A5, A6, S1, P4) are client-side. A bad deploy that breaks the SPA still leaves no server-side signal except what the API itself sees.
- **Residual P1:** the 503 classifier now separates cold starts (Warning) from outages (Error) with the SQL number and route, but the SPA still retries every 10 s for 120 s, so one outage logs about 12 identical Error lines per user, and the exhausted warm-up budget is a UI flag only.
- **Open and untouched on purpose:** version stamping (P5), second health probe (P6), `UiResourceService` first-use log (A13) are the separate "polish" step.
- **Most consequential scenario now:** a deploy that breaks the report-sighting payload shows up as a Warning with `ErrorCode=InvalidLocation` (and a 400 summary line) in the log stream. The same break in the SPA (chunk load, bootstrap, swallowed API error) is still invisible.

## 2. Capture model

Changed since the first report:

- **Pipeline (`Api/WebApi.Host/Program.cs`):** an inline middleware before `UseExceptionHandler()` opens a logging scope (`TraceId`, plus `UserId` once a second inline middleware after `UseAuthentication` fills it) and writes one summary line per request (method, route template, status, duration; no path or query). `Logging:Console:IncludeScopes` is on and `Microsoft.EntityFrameworkCore` is capped at Warning (`appsettings.json`).
- **Result-style failures:** `FailureLoggingControllerBase` (`Api/WebApi/Controllers/FailureLoggingControllerBase.cs`) replaces the two duplicated `GetValidationProblem` copies and logs lost-user 401s, malformed-claim 401s, the three invalid sighting codes and the sighting 404 miss.
- **Identity and user store:** `AccountRepository.cs:111-141` logs original Identity codes (Information for collapsed duplicates, Warning for `ConcurrencyFailure`, Error for unknown codes); `PigeonWatchUserStore.cs:304` logs the four concurrency/not-found branches.
- **503 classifier:** `DatabaseUnavailableExceptionHandler.cs:28-41` finds the first `SqlException` in the chain; cold-start numbers log Warning, everything else Error, with `SqlErrorNumber`, `Route`, `RetryAfterSeconds`.
- **Auth events:** an inline endpoint filter on the `auth` group (`PigeonWatchEndpointRouteBuilderExtensions.cs:25-80`) logs `Outcome` (Succeeded, Failed, LockedOut, NotAllowed, Rejected); the rate limiter `OnRejected` (`Program.cs:44-61`) logs policy, route and a missing-IP flag; stubbed-route and cookie-flag hits log at Information (`:103,:127`).
- **Audit fallback:** `HttpContextCurrentUserProvider.cs:24` logs a Warning on the authenticated-without-`NameIdentifier` branch.
- **Unchanged:** the client side (only `provideBrowserGlobalErrorListeners()`), no error tracker, no deploy identity in logs.

**Assumptions (outside the repo, not findings), unchanged and still unconfirmed:** App Service collects stdout and the log stream is available; no App Insights or alert rules were seen; production CORS is set on the platform; the Data Protection key ring survives restarts; the host forwards the client IP; failed-workflow notifications exist.

## 3. What reaches the logs now

Status labels: **observed** = seen in a manual local run on 2026-10-26 (console output supplied by the user) or asserted by the fake-logger integration tests; **inferred** = from reading the code.

| Failure shape | Response | Platform logs (stdout) | Verdict | Basis |
|---|---|---|---|---|
| Unhandled exception in a controller | 500 ProblemDetails | Error with exception from the exception middleware, now with `TraceId`/`UserId` scope; summary line with route template and status 500 | acceptable | observed (integration test closes the database) |
| SQL cold start (40613 and similar) | 503, `Retry-After: 10` | Warning with `SqlErrorNumber`, `Route`, exception | good | unit-tested |
| SQL timeout, network error, retry exhaustion | 503, `Retry-After: 10` | **Error** with `SqlErrorNumber`, `Route`, exception (local run: error 258 on `auth/login`, summary `responded 503 in 4047 ms`) | good | observed |
| Result error, invalid input (`InvalidLocation`, ...) | 400 | Warning with `ErrorCode`, `Route`, scope `UserId` | good | integration-tested |
| Valid token, user missing or claim malformed | bare 401 | Warning ("no longer exists" / "missing or malformed identifier") with `Route` and scope `UserId` where known | good | integration-tested |
| Identity `ConcurrencyFailure`/not-found in user store | 400 with raw code | Warning with operation and `UserId` | good | unit-tested |
| Duplicate registration | 400 `RegistrationFailed` (generic) | Information per original code (`DuplicateUserName`, `DuplicateEmail`), no email | good | observed |
| Login failure / lockout | 401 | Warning `Outcome=Failed`, then `Outcome=LockedOut`, `Route=auth/login`, no email or password | good | observed (4 failures, then lockout) |
| Rate limit (`/auth/*`) | 429 | Warning with policy, route, missing-IP flag; no IP | good | integration-tested |
| Refresh rejected | 400/401 | Warning `Outcome=Rejected`/`Failed` | good | integration-tested |
| Blocked Identity route (stubbed) | empty 404 | Information with route and method | good | integration-tested |
| Any request | any | one summary line (route template, status, duration), no query string | good | observed |
| SPA error (HTTP error, chunk load, bootstrap, geolocation, map, tiles) | UI state or blank page | nothing | **missed** | inferred (no SPA changes) |

## 4. Systemic root causes (status)

1. **Result-style failures converted to responses and never logged:** **fixed.** The mechanism (controllers and `AccountRepository`/`PigeonWatchUserStore` dropping the code and cause) now logs the code before converting. Responses are intentionally unchanged, including keeping unexpected Identity failures as 400.
2. **Log configuration and lack of correlation:** **fixed** except version stamping. Scope with `TraceId`/`UserId`, summary line, EF at Warning. P5 remains.
3. **One broad "database unavailable" classifier plus an SPA that retries it:** **partly fixed.** The classifier half is fixed; the SPA retry half (`warmup.interceptor.ts`, status 0 treated as warm-up, UI-only failure) is open.
4. **The SPA swallows failures at the edge and has no reporting seam:** **still open.** No SPA change.
5. **No auth security events:** **fixed.** Login, refresh, lockout and 429 outcomes are logged.

## 5. Findings by area (current state)

Only still-open and partly fixed findings are listed. Fixed findings are in section 7.

### Auth

| # | Location | Category | Severity | What happens in production | Fix direction |
|---|---|---|---|---|---|
| A5 | `Frontend/src/app/core/auth/auth.interceptor.ts:33`, `session.service.ts:206-216` | swallowed | high | Refresh failure silently clears the session. The server now logs the refresh outcome (`Outcome=Rejected`/`Failed`), but the client side still cannot say why the user was logged out. | Report status and trace id on force-expire (SPA reporting seam). |
| A6 | `Frontend/src/app/shared/auth/auth-errors.ts`, `problem-details.ts` | missing-context | high | Unknown codes and 5xx collapse to `common.unexpectedError`; the `traceId` is not shown, so a user cannot quote it even though server lines now carry it. | Show the `traceId` on the unexpected path. |
| A10 | `session.service.ts:67`, `resource.service.ts:69,83`, `profile-page.ts:171` | swallowed | medium | Failed config or label loads silently use fallbacks. | Report through the seam. |
| A11 | `Frontend/src/app/core/auth/token-store.ts:23-52` | swallowed | low | Storage failures fall back to memory with no signal. | Report once per session, status only. |
| A13 | `Api/BusinessLogic/Services/UiResourceService.cs:37` | logged-not-captured | low | Culture fallback is a bare catch; a missing embedded resource is still visible only in the framework 500 log. | Log explicitly at first use. |

### Report and browse sightings

| # | Location | Category | Severity | What happens in production | Fix direction |
|---|---|---|---|---|---|
| S1 | `nearby-map-page.ts:187`, `my-sightings.ts:53`, `sighting-detail-page.ts`, `report-sighting-page.ts:114`, `sighting-errors.ts` | swallowed | high | Every API failure becomes a generic UI state. The server side now logs the interesting ones, but network drops, malformed responses and 401s seen by the SPA are still invisible. | Shared `reportError(context, error)`. |
| S6 | `map-view.ts:105-130`, `map-config.ts:3` | swallowed | medium | Leaflet chunk-load failure and tile errors are still dropped. | Keep and report the import error. |
| S4 | `nearby-map-page.ts:251-257` | swallowed | low | A failed refresh clears markers. | Keep previous markers on error. |
| S5 | `geolocation.service.ts:43-53`, `location-picker.ts` | swallowed | low | Geolocation exceptions dropped. | Report once per session. |

### Platform / plumbing

| # | Location | Category | Severity | What happens in production | Fix direction |
|---|---|---|---|---|---|
| P1 (residual) | `Frontend/src/app/core/warmup/warmup.interceptor.ts:39-65,84-96` | flattened-response | medium | The API side is fixed. The SPA still retries a 503 every 10 s for 120 s, so one non-cold-start outage logs about 12 identical Error lines per user, and the exhausted warm-up budget is a UI flag only. | Report when the warm-up budget is exhausted; consider backing off. |
| P4 | `main.ts:6`, `app.config.ts:17`, `app.routes.ts` | coverage-gap | high | Bootstrap failures, unhandled rejections and chunk-load errors after a deploy stay in the user's console. | Custom `ErrorHandler`, `withNavigationErrorHandler`, client-report endpoint. |
| P2 | `warmup.interceptor.ts:189` | flattened-response | medium | Status 0 is still treated as warm-up and retried for 120 s. | Treat status 0 separately; report the final failure. |
| P5 | `Api/WebApi.Host/Program.cs`, `PigeonWatch.WebApi.Host.csproj` | config | low | Still no version, commit or environment in logs or responses (grep for informational version/commit stamping is empty). | Log the informational version at startup. |
| P6 | `.github/workflows/deploy-api.yml:148` | coverage-gap | low | The only probe is still the `/configuration/client` curl that never touches the database (documented free-tier decision). | Optional second probe. |

## 6. Recommended fix order (remaining)

1. **SPA reporting seam (P4, S1, S6, A5, A6, A10, A11, S4, S5, P2, P1 residual).** One `ErrorHandler`, one reporting service, one endpoint, and show the `traceId` in the error UI. The server side now prints `TraceId` on every line, so a client report or a user-quoted id can be matched. *Constraint:* the endpoint is an abuse vector, so rate-limit it and accept only status, route, trace id and error name, never tokens or emails.
2. **Polish (P5, P6, A13).** Version stamp at startup; optional second probe; first-use log for the label resource.

## 7. Changes since last audit

| Previous id | Status | Evidence |
|---|---|---|
| A1 | fixed (logging); status mapping intentionally unchanged | `AccountRepository.cs:111-141` logs the original code (Information for collapsed duplicates, Warning for `ConcurrencyFailure`, Error for unknown codes), never email or description. Unmapped Identity codes still return 400 by decision. Duplicate registration observed locally: two Information lines (`DuplicateUserName`, `DuplicateEmail`), response still generic. |
| A2 | fixed | `PigeonWatchUserStore.cs:304` logs operation and user id in all four branches. Unit-tested. |
| A3 | fixed | Filter logs `Outcome` and `Route`. Observed locally: four `Failed` Warnings then `LockedOut` Warnings, no email/password. User id is not logged because login is anonymous (consistent with anti-enumeration). |
| A4 | fixed, with a residual | `Program.cs:44-61` logs policy, route and missing-IP flag; response stays an empty 429. No partition key is logged by design. `/auth/refresh` is still exempt from the limiter. |
| A5, A6, A10, A11 | still open | SPA unchanged. A6 now has a server-side counterpart: the `traceId` in the 500 body matches the log scope. |
| A7 | fixed (logging); status unchanged | `FailureLoggingControllerBase.cs:13-23` logs Warning for missing/malformed identifier and for a vanished user; the 401 stays bare. |
| A8 | fixed | `PigeonWatchEndpointRouteBuilderExtensions.cs:103,127` log stub hits and cookie-flag 400 at Information with route and method. |
| A12 | fixed | `HttpContextCurrentUserProvider.cs:24` logs a Warning on the authenticated-without-claim branch; anonymous requests stay silent. |
| A13 | still open | `UiResourceService.cs:37` unchanged. |
| S1, S4, S5, S6 | still open | SPA unchanged. |
| S2 | fixed | `SightingsController.cs:46` logs Warning with `ErrorCode` for `InvalidLocation`, `InvalidInitialStatus`, `InvalidCriticality`; cooldown and model-binding 400s log nothing. |
| S3 | fixed | `FailureLoggingControllerBase.cs:34` logs Information with the sighting id; the scope supplies the user id. |
| S7 | fixed | Covered by the scope and summary line. 500s now carry `TraceId`/`UserId`, and the summary line has the route template. |
| S8 | fixed | `appsettings.json`: `Microsoft.EntityFrameworkCore: Warning`. A test asserts that no EF command lines at Information reach the logs. |
| P1 | changed (API half fixed, SPA half open) | `DatabaseUnavailableExceptionHandler.cs:28-41`: cold-start numbers Warning, everything else Error, with `SqlErrorNumber`, `Route`, `RetryAfterSeconds`. Observed locally: error 258 on `auth/login` logged at Error, 503 summary line. The client retry behaviour and UI-only exhaustion are unchanged. |
| P2 | still open | `warmup.interceptor.ts:189` unchanged. |
| P3 | fixed | Scope with `TraceId`/`UserId`, console scopes on, one summary line per request. Observed locally on every line. |
| P4 | still open | SPA unchanged. |
| P5 | still open | No version stamping. |
| P6 | still open | Probe unchanged. |

Totals of the 26 earlier findings: 12 fixed (A1, A2, A3, A4, A7, A8, A12, S2, S3, S7, S8, P3), 1 changed (P1), 13 still open (A5, A6, A10, A11, A13, S1, S4, S5, S6, P2, P4, P5, P6). Open severities (the frontmatter counts, including the P1 residual): high 4 (A5, A6, S1, P4), medium 4 (A10, S6, P2, P1 residual), low 6 (A11, A13, S4, S5, P5, P6).

New observations (not findings):

- The hosting scope prints the client-supplied `RequestPath` on every line. Checked locally with `/x%0AFORGED%20line%20here`: the path is logged percent-encoded on a single line, so no log forging.
- EF still logs a `fail` line at Error for each failed connection attempt (five retries per request in the local 503 run), in addition to the first-party handler line. Expected, not a gap.

## 8. Method and limits

- **Agents run:** none. This was a verify run done by reading the cited code and the implementation commits (`54b1dcb`, `75bdc62`, `0c35a7f`, `0d5de23`, `9008935`, review fixes `71d484b`, squash commit `813c763`). The implementation was separately reviewed (`archive/2026-10-25-server-side-observability-logging/reviews/impl-review.md`).
- **Runtime proof: partial.** No scripted probe suite or fake-ingest harness exists, so there is no before/after table from identical probes. Evidence is (a) a manual local run on 2026-10-26 whose console output was supplied by the user: duplicate registration, four wrong-password logins then lockout, an unreachable database (SQL error 258, 503), request summary lines and scope values; and (b) 81 integration tests that read the structured log records through a fake logger, plus 220 unit tests. A rerunnable probe suite would be needed to turn those into a repeatable before/after.
- **Verified by hand:** the logging call sites by grep and reading (`Data/`, `WebApi/`, `WebApi.Host/`, `DependencyInjection/`); the empty frontend diff since `69e3a35`; `UiResourceService.cs`, `appsettings.json`, the deploy workflow probe and the absence of version stamping.
- **Not verified:** how App Service renders stdout, log retention, whether anyone reads the logs, the live CORS state, the Data Protection key ring and forwarded-IP behaviour (unchanged assumptions).
- **Skipped on purpose:** no SPA findings were re-run against a browser; the SPA code is byte-identical to the audited commit.
- **No harness files or worktrees were created.**
