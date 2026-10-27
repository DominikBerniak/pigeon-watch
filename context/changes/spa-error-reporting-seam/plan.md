# SPA error reporting seam Implementation Plan

## Overview

Close the client-side half of the 2026-10-26 observability verify audit (`context/audits/observability/2026-10-26_verify-auth-report-browse-sightings.md`, section 6 step 1). The API now logs its own failures with `TraceId` and `UserId`, but every failure the Angular SPA swallows, plus uncaught, bootstrap and lazy-route errors, still dies in the user's console. This plan adds one reporter in the SPA that sends a small, PII-free, allowlisted report to a new anonymous API endpoint, where it becomes a structured log line, and shows the API `traceId` to the user as a quotable reference in unexpected-error states. It closes audit findings A5, A6, A10, A11, S1, S4, S5, S6, P2, P4 and the SPA half of P1. Version stamping, the second probe and the `UiResourceService` log (P5, P6, A13) stay out.

## Current State Analysis

- No reporting path exists: `Frontend/src/app/app.config.ts:15-25` provides `provideBrowserGlobalErrorListeners()` only, with no custom `ErrorHandler` and no `withNavigationErrorHandler`; `main.ts:6` only calls `console.error`; every route in `app.routes.ts:5-54` uses `loadComponent` with no `.catch`.
- Swallow sites: `catchError(() => of(...))` at `nearby-map-page.ts:187`, `my-sightings.ts:53`, `sighting-detail-page.ts:161`, `profile-page.ts:171`, `resource.service.ts:69,83`, `map-view.ts:137`, `session.service.ts:67,210`; bare `catch {}` in `token-store.ts:25,39,50`, `culture-store.ts:28,37` and `geolocation.service.ts:46`; `auth.interceptor.ts` and `warmup.interceptor.ts` rethrow without reporting.
- `shared/http/problem-details.ts` has `problemExtension(error, name)` but nothing reads `traceId`; the unexpected-error text is `common.unexpectedError` (`auth-errors.ts:108`) and `sighting.errors.generic`, shown in `app-alert` / `app-error-retry` with no field for an id.
- Every API log line carries the request `TraceId` scope and the 500 ProblemDetails body carries the same `traceId`, so a user-quoted id can be matched to a log line today.
- API conventions that shape a new endpoint: every action must return `Task<ActionResult<T>>` with `T` in `PigeonWatch.WebApi.Models` (`ArchitectureTests/ControllerActionTests.cs:11-82`), so a body-less 202 needs an empty marker model; `IntegrationTests/EndpointCoverageTests.cs` requires a fixture in `EndpointFixtureRegistry` and `EndpointProtectionTests.cs:13-25` has a hard-coded anonymous-endpoint list; the `auth` rate-limit policy is a per-IP fixed window defined in `WebApi.Host/Program.cs:41-76`; no body size limit is configured; CORS allows only `http://localhost:4200` in code (`Program.cs:31-39`), and the SPA CSP `connect-src` already admits the API origin.

## Desired End State

An on-call responder can see, in the API log stream, one structured line per client-reported failure (kind, context, HTTP status, error name, SPA route template, session id, the API `traceId` the SPA saw) at Warning or Information, never Error, and a user who hits an unexpected error can read out a reference id that matches a server log scope. Verification: the new unit, integration and e2e tests pass, existing suites pass unchanged, and a local run shows the log lines with no emails, tokens, query strings or coordinates.

### Key Discoveries:

- Initial-bundle code (`app.ts`, `core/`) must import `shared/ui` by file path, and the bundle sits near its budget (`Frontend/CLAUDE.md`, last build 512.1 kB), so the reporter must stay small and lazy where it can.
- Frontend rules: Observables only (no `async/await`/`firstValueFrom` in production code), `inject()` with `@Injectable({ providedIn: 'root' })`, blank line before every `if`/`return`, every UI string is a key in `Api/BusinessObjects/Resources/UiLabels.resx`.
- API calls from the reporter must set `SKIP_AUTH` and `SKIP_WARMUP` (`resource.service.ts:58` shows the pattern), otherwise a failing report would trigger refresh and warm-up loops.
- `OnRejected` already logs the rate-limit policy name from `EnableRateLimitingAttribute` metadata, so a new policy applied with `[EnableRateLimiting(...)]` logs correctly.
- The log forging check in the implementation review showed the console formatter keeps encoded paths on one line, but client-supplied strings in a log template are not covered by that, so every reported string is restricted by an allowlist pattern server-side.

## What We're NOT Doing

- No error tracker SDK (Sentry, App Insights); the transport is the API log stream.
- No messages, stack traces, URLs, query strings, request or response bodies, emails, tokens or coordinates in a report; no free-text field.
- No change to any existing response, status code or error code, and no change to the existing retry, refresh or warm-up behaviour (reports are side effects only).
- No client-chosen severity: the server maps a closed `kind` to Warning or Information and never writes Error for a client report.
- No reporting of expected user conditions (validation 400s, invalid-credential 401, 404 on a missing sighting, 429, cooldown); only failures the UI currently shows as unexpected or hides.
- No `ForwardedHeaders` or production CORS configuration changes (unverified hosting assumptions from the audit); no version stamping, second probe or `UiResourceService` log (audit P5, P6, A13).
- No server-side storage of reports, no alert rules, no dashboard.

## Implementation Approach

Five phases, API first so every SPA phase tests against a real contract. Phase 1 adds the endpoint, its rate-limit policy and the level mapping with API tests. Phase 2 adds the SPA reporter and the global handlers. Phases 3 and 4 wire it into the interceptors, core services and feature swallow sites, and add the reference-id UI. Phase 5 adds one Playwright test for the full path and updates the project docs. Each phase is test-first.

The report is a closed allowlist: `kind` (uncaught, bootstrap, navigation, api-failure, session-expired, warmup-exhausted, fallback), a `context` key, an optional HTTP status, an error name, the API `traceId` when the failed response carried one, the SPA route template (query string removed, ids replaced), and a random per-page-load session id. The SPA dedupes by (kind, context, status), caps at 20 reports per page load, and never reports a failure of the reporter itself.

Server level policy: `uncaught`, `bootstrap`, `navigation`, `api-failure`, `session-expired` and `warmup-exhausted` log at Warning; `fallback` (degraded but working: blocked storage, label or config load, geolocation, map tiles) logs at Information.

## Critical Implementation Details

- **Timing & lifecycle:** a bootstrap failure happens before dependency injection exists, so the bootstrap report cannot use `HttpClient` or the service; it needs a small DI-free path (`navigator.sendBeacon` to the API origin) used from `main.ts`. The reporter itself must opt out of both interceptors and swallow its own failure (one `console.warn`, no recursion).
- **User experience spec:** the reference id is shown only on the generic unexpected-error variants and only when the failed response carried a `traceId`; network failures (status 0) have none and show nothing extra.

## Phase 1: Report endpoint (API)

### Overview

Add an anonymous `POST client-errors` that validates an allowlisted body, applies a per-IP rate limit and a small body cap, and writes one structured log line at a level chosen by the server.

### Changes Required:

#### 1. Request model and controller

**File**: new `PigeonWatch/Api/WebApi/Models/ClientErrorReportModel.cs` (plus an empty receipt model), new `PigeonWatch/Api/WebApi/Controllers/ClientErrorsController.cs`

**Intent**: One endpoint that accepts the allowlisted report and logs it, responding 202 with no content. Every string is constrained so no user-controlled text can reach the log beyond identifier-like tokens.

**Contract**: `POST client-errors`, `[AllowAnonymous]`, `[RequestSizeLimit]` of about 2 KB, derived from `FailureLoggingControllerBase`. Request model fields: `Kind` (required, one of the closed set above, enforced with `[AllowedValues]`), `Context` (required, max 60, lowercase dotted-key pattern), `HttpStatus` (optional int 0-599), `ErrorName` (optional, max 80, identifier pattern), `TraceId` (optional, max 64, hex-and-dash pattern), `Route` (required, max 100, pattern of path template characters only), `SessionId` (required GUID). Action returns `Task<ActionResult<ClientErrorReceiptModel>>` with `Accepted()`; an invalid body gets the framework 400 and logs nothing. Log template uses placeholders `{Kind}`, `{Context}`, `{HttpStatus}`, `{ErrorName}`, `{Route}`, `{SessionId}`, `{ClientTraceId}`; the request's own `TraceId` stays in the scope. The kind-to-level map lives in the controller class (non-static) per the level policy above.

#### 2. Rate-limit policy

**File**: `PigeonWatch/Api/WebApi/RateLimiting/RateLimitPolicyNames.cs`, `PigeonWatch/Api/WebApi.Host/Program.cs`

**Intent**: A separate per-IP bucket so reports cannot consume the login budget and vice versa.

**Contract**: new const `ClientErrors = "clientErrors"`; fixed window, 20 permits per minute, `QueueLimit = 0`, partitioned by remote IP like the `auth` policy; applied with `[EnableRateLimiting(RateLimitPolicyNames.ClientErrors)]` on the action. The empty 429 and the existing `OnRejected` Warning are reused unchanged.

#### 3. Endpoint registries and tests

**File**: `PigeonWatch/Api/IntegrationTests/EndpointFixtureRegistry.cs`, `EndpointProtectionTests.cs`, new `IntegrationTests/ClientErrorReportTests.cs`, new `UnitTests/ClientErrorsControllerTests.cs`

**Intent**: Register the endpoint where the existing sweeps require it, and cover its behaviour.

**Contract**: add a `POST client-errors` fixture, add the route to the anonymous-endpoint list; integration tests read logs through the fake logger like `RequestLoggingTests`; unit tests follow the substituted-`ILogger` style in `ControllerFailureLoggingTests`.

### Success Criteria:

#### Automated Verification:

- New integration tests, written first and seen failing: a valid report returns 202 and logs exactly one record at the expected level with the structured fields and the request `TraceId` scope; an `uncaught` kind logs Warning and a `fallback` kind logs Information; an unknown kind, an over-long field, a field with a newline or a bad character, and a body over the cap return an error and log nothing; the 21st request from one client in a minute returns the same empty 429 and logs the `clientErrors` rate-limit Warning; no log record contains an email-like string or a query string: `dotnet test PigeonWatch/Api/IntegrationTests/PigeonWatch.IntegrationTests.csproj`
- New unit tests, written first and seen failing, for the kind-to-level mapping and the logged fields: `dotnet test PigeonWatch/Api/UnitTests/PigeonWatch.UnitTests.csproj`
- Existing endpoint coverage, endpoint protection, privacy and contract tests pass with the new endpoint registered: same integration command.
- Architecture tests pass: `dotnet test PigeonWatch/Api/ArchitectureTests/PigeonWatch.ArchitectureTests.csproj`
- Solution builds with style rules enforced: `dotnet build PigeonWatch/Api/PigeonWatchApi.slnx`

#### Manual Verification:

- With the API running locally, a `curl.exe` POST of a valid report returns 202 and the console shows one line with the expected level and no personal data; a 21st call in a minute returns 429.

**Implementation Note**: After completing this phase and all automated verification passes, pause here for manual confirmation from the human that the manual testing was successful before proceeding to the next phase.

---

## Phase 2: Reporter core and global handlers (SPA)

### Overview

Build the SPA reporter and register the handlers that catch what nobody catches today: uncaught errors, router navigation errors (lazy chunk loads) and bootstrap failures.

### Changes Required:

#### 1. Reporter service and types

**File**: new `PigeonWatch/Frontend/src/app/core/error-reporting/error-reporting.service.ts` and `error-report.types.ts`, `PigeonWatch/Frontend/src/app/shared/http/problem-details.ts`

**Intent**: One place that turns a caught error into an allowlisted report and sends it, with the volume rules from the design. Also a reader for the API `traceId`.

**Contract**: `@Injectable({ providedIn: 'root' })` service with a `report(kind, context, error?)` method (Observables only, no `async`); builds the allowlisted payload from the error (HTTP status for `HttpErrorResponse`, error name, `traceId` via a new `problemTraceId(error)` helper built on `problemExtension`), the SPA route template (query and fragment removed, GUID segments replaced with `:id`), and a `crypto.randomUUID()` session id created once per page load; dedupes by (kind, context, status); stops after 20 reports; sends through `HttpClient` with `SKIP_AUTH` and `SKIP_WARMUP`; on a failed send emits a single `console.warn` and never reports it.

#### 2. Global handlers

**File**: new `core/error-reporting/app-error-handler.ts`, `PigeonWatch/Frontend/src/app/app.config.ts`, `PigeonWatch/Frontend/src/main.ts`, new `core/error-reporting/bootstrap-report.ts`

**Intent**: Route uncaught errors, navigation errors and bootstrap failures into the reporter while keeping today's console output.

**Contract**: a custom `ErrorHandler` registered in `app.config.ts` that calls `console.error` as before and reports kind `uncaught`; `provideRouter(routes, withNavigationErrorHandler(...))` reporting kind `navigation` and keeping default navigation failure behaviour; `main.ts` bootstrap `.catch` keeps `console.error` and calls a DI-free helper that posts the report with `navigator.sendBeacon` to `environment.apiUrl` (kind `bootstrap`).

### Success Criteria:

#### Automated Verification:

- New specs, written first and seen failing, for the reporter: payload contains only allowlisted fields and no query string, id or message; dedupe by (kind, context, status); the 21st distinct report is dropped; a failed send is not reported and does not throw; the request opts out of auth and warm-up: `npm test --prefix PigeonWatch/Frontend -- --watch=false`
- New specs for the `ErrorHandler`, the navigation error handler, the bootstrap beacon and `problemTraceId`: same command.
- Production build passes within the bundle budgets: `npm run build --prefix PigeonWatch/Frontend`
- Full frontend suite passes with no existing spec edited: `npm test --prefix PigeonWatch/Frontend -- --watch=false`

#### Manual Verification:

- With the API and SPA running locally, throwing an uncaught error from the browser console produces one `uncaught` line in the API log, and a second identical throw produces none.
- Blocking a lazy route chunk in the browser dev tools network panel and navigating to it produces one `navigation` line.

**Implementation Note**: After completing this phase and all automated verification passes, pause here for manual confirmation from the human that the manual testing was successful before proceeding to the next phase.

---

## Phase 3: Interceptors and core services (SPA)

### Overview

Report the failures that today turn into a silent logout, an endless retry or a quiet fallback in the core layer.

### Changes Required:

#### 1. Auth interceptor and session

**File**: `PigeonWatch/Frontend/src/app/core/auth/auth.interceptor.ts`, `PigeonWatch/Frontend/src/app/core/auth/session.service.ts`

**Intent**: Make the forced logout and the swallowed restore failure visible (audit A5).

**Contract**: report kind `session-expired` (context identifies refresh rejected, no refresh token, or restore failed) with the HTTP status and `traceId` when the session is force-expired or the restore swallows a non-session error; behaviour of retry, refresh and navigation is unchanged.

#### 2. Warm-up interceptor

**File**: `PigeonWatch/Frontend/src/app/core/warmup/warmup.interceptor.ts`

**Intent**: Record when the 120 s warm-up budget is exhausted and keep status 0 distinguishable from 503 (audit P1 residual, P2).

**Contract**: on budget exhaustion report kind `warmup-exhausted` once with the last status (0 or 503); retry timing and the user-visible warm-up state are unchanged. A status-0 exhaustion means the API itself is unreachable, so that report is expected to fail to send (one `console.warn`, no retry); the useful case is a 503 from an unavailable database behind a reachable API.

#### 3. Config, label and storage fallbacks

**File**: `core/auth/session.service.ts` (client config load), `core/resources/resource.service.ts`, `core/auth/token-store.ts`, `core/resources/culture-store.ts`

**Intent**: Replace silent fallbacks with a reported fallback (audit A10, A11).

**Contract**: report kind `fallback` with a context per site (client config load failed, labels load failed, label snapshot import failed, storage unavailable) and the status where there is one; the storage fallbacks report once per session and never include token or culture values; fallbacks themselves behave as today.

### Success Criteria:

#### Automated Verification:

- New specs, written first and seen failing: a rejected refresh and a missing refresh token each report `session-expired` once and still expire the session; a failed restore with a 5xx reports and still starts anonymous: `npm test --prefix PigeonWatch/Frontend -- --watch=false`
- New specs: warm-up budget exhaustion reports once with status 503 and once with status 0 and does not change retry timing; a recovered request reports nothing: same command.
- New specs: each config, label and storage fallback reports `fallback` once with its context and no token or culture value in the payload: same command.
- Production build within budgets and full frontend suite pass with no existing assertion edited: `npm run build --prefix PigeonWatch/Frontend` and `npm test --prefix PigeonWatch/Frontend -- --watch=false`

#### Manual Verification:

- Clearing the refresh token in local storage and then using the app logs the user out as before and produces one `session-expired` line in the API log.
- With the API running against an unreachable database, after the 120 s warm-up budget one `warmup-exhausted` line with status 503 is logged.

**Implementation Note**: After completing this phase and all automated verification passes, pause here for manual confirmation from the human that the manual testing was successful before proceeding to the next phase.

---

## Phase 4: Feature swallow sites and reference id UI (SPA)

### Overview

Report from the feature-level handlers that turn API failures into generic UI states, and show the API `traceId` as a reference in the unexpected-error states (audit A6, S1, S4, S5, S6).

### Changes Required:

#### 1. Reference UI and label

**File**: new `PigeonWatch/Frontend/src/app/shared/ui/error-reference/` component, `PigeonWatch/Api/BusinessObjects/Resources/UiLabels.resx`

**Intent**: One shared element that shows "Reference: <id>" so every error state renders it the same way.

**Contract**: new label key `common.errorReference` with placeholder `{0}`; component takes the id as an input and renders nothing when it is empty; imported by file path from core code if used there, never through the barrel.

#### 2. Failure types and unexpected-error states

**File**: `shared/auth/auth-errors.ts`, `features/sightings/sighting-errors.ts`, `features/auth/login-page`, `register-page`, `features/profile/profile-page`, `password-dialog`, `features/sightings/report-sighting-page`, `sighting-detail-page`, `my-sightings`, `nearby-map-page`

**Intent**: Carry the `traceId` from the failed response to the UI and report unexpected failures, while expected user conditions stay silent.

**Contract**: the failure results for the generic variants (`unexpected`, `generic`, error states) gain an optional trace id; those variants render the reference element; an API failure is reported as `api-failure` only when it maps to the generic or unexpected UI state (validation 400s, invalid-credential 401, 404 miss, 429 and cooldown are not reported); `profile-page.ts:171` and `my-sightings.ts:53` stop dropping the error; the nearby map keeps the previous markers when a refresh fails (audit S4) and still shows the error status.

#### 3. Map and geolocation

**File**: `shared/map/map-view/map-view.ts`, `core/geolocation/geolocation.service.ts`

**Intent**: Report a Leaflet or CSS import failure, a first tile error and a geolocation exception that today vanish (audit S6, S5).

**Contract**: kind `fallback` with contexts for map import failed, tile error (reported once through the dedupe) and geolocation exception (error name only); the displayed statuses stay as they are.

### Success Criteria:

#### Automated Verification:

- New specs, written first and seen failing, for the reference element and the failure types: renders the id when present, nothing when absent: `npm test --prefix PigeonWatch/Frontend -- --watch=false`
- New specs for the pages: a 500 with a `traceId` on nearby, my sightings, detail, report, profile and the auth forms shows the reference and reports `api-failure`; a 400 validation error, a login 401, a detail 404, a 429 and a cooldown report nothing and show no reference; a failed nearby refresh keeps the previous markers: same command.
- New specs for the map and geolocation fallbacks: same command.
- API resource and architecture tests pass with the new label key: `dotnet test PigeonWatch/Api/UnitTests/PigeonWatch.UnitTests.csproj` and `dotnet test PigeonWatch/Api/ArchitectureTests/PigeonWatch.ArchitectureTests.csproj`
- Production build within budgets and full frontend suite pass with no existing assertion edited: `npm run build --prefix PigeonWatch/Frontend` and `npm test --prefix PigeonWatch/Frontend -- --watch=false`

#### Manual Verification:

- With the API pointed at an unreachable database, the nearby map error state shows a reference that matches the `TraceId` scope of the 500 line in the API log, and one `api-failure` report line is logged.
- A wrong login password and an unknown sighting id show no reference and log no client report.

**Implementation Note**: After completing this phase and all automated verification passes, pause here for manual confirmation from the human that the manual testing was successful before proceeding to the next phase.

---

## Phase 5: Playwright e2e and docs

### Overview

Prove the full path in a real browser and record the new rules for future contributors.

### Changes Required:

#### 1. E2E test

**File**: new `PigeonWatch/Frontend/tests/e2e/error-reporting.spec.ts`

**Intent**: Force an API failure in the browser and assert both the report request and the shown reference.

**Contract**: uses the existing `setup` project and `storageState`; intercepts `**/sightings/nearby**` with a 500 ProblemDetails body carrying a known `traceId`, asserts the page shows `Reference: <id>`, and asserts a `POST client-errors` request whose body contains only allowlisted keys and the same trace id; follows the e2e skill rules (role and label locators, no `waitForTimeout`, independent test).

#### 2. Documentation

**File**: `PigeonWatch/CLAUDE.md`, `PigeonWatch/Api/CLAUDE.md`, `PigeonWatch/Frontend/CLAUDE.md`

**Intent**: Record the endpoint, its policy and the contributor rule so the next agent reports instead of swallowing.

**Contract**: add `POST client-errors` and the `clientErrors` policy to the API surface lists; add a Frontend rule that caught errors shown as unexpected UI go through `ErrorReportingService` and that expected user conditions do not.

### Success Criteria:

#### Automated Verification:

- The new e2e test passes against a running API and SPA, and goes red when the report call is removed (deliberate break): `npx playwright test tests/e2e/error-reporting.spec.ts` from `PigeonWatch/Frontend`
- Full frontend suite and production build still pass: `npm test --prefix PigeonWatch/Frontend -- --watch=false` and `npm run build --prefix PigeonWatch/Frontend`

#### Manual Verification:

- The three CLAUDE.md files describe the endpoint, the policy and the reporting rule accurately, and the audit report's open findings A5, A6, A10, A11, S1, S4, S5, S6, P2, P4 can each be matched to a report kind or the reference UI, ready for a `--verify` run.

**Implementation Note**: After completing this phase and all automated verification passes, pause here for manual confirmation from the human that the manual testing was successful before proceeding.

---

## Testing Strategy

### Unit Tests:

- SPA: reporter payload allowlist, dedupe, cap and self-failure, `ErrorHandler`, navigation handler, bootstrap beacon, `problemTraceId`, interceptor and service reporting, reference element, page-level report-or-silent rules (Vitest, `provideHttpClientTesting`, existing spec conventions).
- API: kind-to-level mapping and logged fields with a substituted `ILogger<T>`.

### Integration Tests:

- API: the full pipeline through `PigeonWatchApiFactory` with fake logging for levels, fields, scope, rejection of bad bodies, the rate limit and the existing privacy sweeps.
- Browser: one Playwright test for the SPA-to-API path.

### Manual Testing Steps:

1. Post a valid report with `curl.exe` and check the log line and the 21st-call 429.
2. Throw an uncaught error in the browser and block a lazy chunk; check the lines and dedupe.
3. Clear the refresh token; check the logout and the `session-expired` line.
4. Run with an unreachable database; check the reference on the nearby map against the 500 log line.
5. Confirm a wrong password and a missing sighting show no reference.

## Performance Considerations

The initial bundle is near its budget, so the reporter and handlers must stay small; heavier pieces (reference element, feature wiring) load with their lazy routes. Each report is one small POST, deduped and capped at 20 per page load, so a failing page cannot flood the API.

## Migration Notes

None: no schema, response or existing contract changes. One new anonymous route, one rate-limit policy and one label key. The production SPA origin must already be allowed by the platform CORS setting (assumed from the deploy plan, not verifiable in the repo), otherwise browsers block the report.

## References

- Audit report (evidence for this change): `context/audits/observability/2026-10-26_verify-auth-report-browse-sightings.md`
- Previous change (server side): `context/archive/2026-10-25-server-side-observability-logging/plan.md`
- Change identity: `context/changes/spa-error-reporting-seam/change.md`
- Controller template and tests: `PigeonWatch/Api/WebApi/Controllers/AccountController.cs:25-28`, `PigeonWatch/Api/IntegrationTests/AuthEventLoggingTests.cs`
- Rate-limit policy: `PigeonWatch/Api/WebApi.Host/Program.cs:41-76`
- Frontend rules: `PigeonWatch/Frontend/CLAUDE.md`

## Progress

> Convention: `- [ ]` pending, `- [x]` done. Append ` — <commit sha>` when a step lands. Do not rename step titles. See `references/progress-format.md`.

### Phase 1: Report endpoint (API)

#### Automated

- [x] 1.1 New integration tests for the report endpoint, level mapping, bad bodies and rate limit pass — 5b851a7
- [x] 1.2 New unit tests for the kind-to-level mapping and logged fields pass — 5b851a7
- [x] 1.3 Existing endpoint coverage, protection and privacy tests pass with the endpoint registered — 5b851a7
- [x] 1.4 Architecture tests pass — 5b851a7
- [x] 1.5 Solution builds with style rules enforced — 5b851a7

#### Manual

- [x] 1.6 A local curl.exe report returns 202 with one clean log line and the 21st call returns 429 — 5b851a7

### Phase 2: Reporter core and global handlers (SPA)

#### Automated

- [x] 2.1 New reporter specs for allowlist, dedupe, cap and self-failure pass — 09edf30
- [x] 2.2 New specs for the ErrorHandler, navigation handler, bootstrap beacon and problemTraceId pass — 09edf30
- [x] 2.3 Production build passes within the bundle budgets — 09edf30
- [x] 2.4 Full frontend suite passes with no existing spec edited — 09edf30

#### Manual

- [x] 2.5 An uncaught error from the browser console logs one uncaught line and a repeat logs none — 09edf30
- [x] 2.6 A blocked lazy chunk produces one navigation line — 09edf30

### Phase 3: Interceptors and core services (SPA)

#### Automated

- [x] 3.1 New specs for session-expired reporting pass
- [x] 3.2 New specs for warm-up exhaustion reporting pass
- [x] 3.3 New specs for config, label and storage fallback reporting pass
- [x] 3.4 Production build within budgets and full frontend suite pass

#### Manual

- [x] 3.5 Clearing the refresh token logs the user out as before and logs one session-expired line
- [x] 3.6 With the API running against an unreachable database, one warmup-exhausted report with status 503 is logged after the warm-up budget

### Phase 4: Feature swallow sites and reference id UI (SPA)

#### Automated

- [ ] 4.1 New specs for the reference element and failure types pass
- [ ] 4.2 New page specs for report-or-silent rules and previous markers pass
- [ ] 4.3 New specs for map and geolocation fallbacks pass
- [ ] 4.4 API unit and architecture tests pass with the new label key
- [ ] 4.5 Production build within budgets and full frontend suite pass

#### Manual

- [ ] 4.6 With an unreachable database the nearby map shows a reference matching the 500 log TraceId and one api-failure line is logged
- [ ] 4.7 A wrong login password and an unknown sighting id show no reference and log no client report

### Phase 5: Playwright e2e and docs

#### Automated

- [ ] 5.1 The new e2e test passes and goes red when the report call is removed
- [ ] 5.2 Full frontend suite and production build still pass

#### Manual

- [ ] 5.3 The CLAUDE.md files are accurate and the audit findings can be matched to report kinds or the reference UI
