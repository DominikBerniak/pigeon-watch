---
type: observability-audit
date: 2026-10-25 19:17
mode: audit
commit: 69e3a35
branch: chore/playwright-e2e-setup
dirty_tree: true
areas: [auth, report-browse-sightings]
area_source: user
runtime_proof: not-run
error_tracker: none (ILogger to stdout only; App Service log stream assumed)
previous_report: null
findings: { critical: 0, high: 9, medium: 7, low: 10 }
---

# Observability audit: auth, report/browse sightings (2026-10-25)

Static audit, no runtime proof. The working tree was dirty (`.claude/.10x-cli-manifest.json`, `CLAUDE.md`, untracked `.claude/skills/10x-observability-audit/`), none of it product code.

## 1. TL;DR

- **The server writes almost no logs.** The whole API has one first-party log call (`Api/Data/Diagnostics/DatabaseUnavailableExceptionHandler.cs:112`). Every Result-style failure (validation, lost user, concurrency, lockout, rate limit) ends as a bare 400/401/404/429 with nothing in stdout.
- **The log configuration removes the framework's own evidence.** `Microsoft.AspNetCore: Warning` (`Api/WebApi.Host/appsettings.json`) drops the per-request status/route/duration lines. There are no logging scopes, so no user id or trace id reaches a log line, while the `traceId` in ProblemDetails responses is thrown away by the SPA.
- **One broad classifier turns many faults into "Service warming up".** An outage, a query timeout and a retry exhaustion all become a Warning plus a 503. The SPA then retries for up to 120 s, so one incident produces about 12 identical warnings.
- **The SPA has no way to report anything.** Every failure becomes UI state (`catchError(() => of(error))`, bare `catch {}`), there is no custom `ErrorHandler`, and a rejected refresh logs the user out without a trace.
- **Most consequential scenario:** a deploy that breaks the report-sighting payload (or a version skew between SPA and API) makes every `POST /sightings` return 400 or 401. The user sees a generic error, and no log line, status or user id records it.

## 2. Capture model

How a failure travels from code to a human today:

- **Runtime:** ASP.NET Core (.NET 10) API on Azure App Service F1, plus an Angular 22 SPA on Azure Static Web Apps. No background workers, hosted services or fire-and-forget work exist (grep empty). Migrations run in the CI `migrate` job, not at startup.
- **Error tracker:** none in the repo. No Sentry, App Insights, OpenTelemetry or Serilog package. The roadmap records "Observability: absent". This is a precondition, not a finding.
- **Capture boundary:** `app.UseExceptionHandler()` is first in the pipeline (`Api/WebApi.Host/Program.cs:58`), followed by routing, CORS, authentication, rate limiter, authorization and endpoints (`:58-80`). The only registered `IExceptionHandler` is `DatabaseUnavailableExceptionHandler` (`Api/DependencyInjection/DataServiceCollectionExtensions.cs:24-25`). It handles SQL-unavailable exceptions as 503, and every other exception falls through to the default 500 ProblemDetails writer, which the framework logs at Error with the exception.
- **Logging:** the default console provider, `Default: Information`, `Microsoft.AspNetCore: Warning` (`Api/WebApi.Host/appsettings.json`). One first-party call: `LogWarning(exception, ...)` with a constant message (`DatabaseUnavailableExceptionHandler.cs:112`). There are no `BeginScope`, trace id or user id usages anywhere in `Api` (grep empty).
- **Result style:** services return result objects with error codes. Controllers map them to `ValidationProblem` 400 or bare `Unauthorized()`. The mapper is duplicated in `AccountController.cs:74-80` and `SightingsController.cs:80-94`. `AccountRepository.ToAccountErrors` (`Api/Data/Repositories/AccountRepository.cs:77-102`) passes unmapped Identity codes through and collapses duplicates into `RegistrationFailed`.
- **Client side:** only `provideBrowserGlobalErrorListeners()` (`Frontend/src/app/app.config.ts:17`), so unhandled errors reach `console.error` and nothing else. Bootstrap failure: `console.error` (`Frontend/src/main.ts:6`). No endpoint exists that could receive a client report.
- **Scrubbing and sampling:** none, and no PII is logged anywhere. `HttpContextCurrentUserProvider` falls back to the `SYSTEM` audit user without a log.
- **Deploy identity:** no version, commit or environment is logged or returned by code. The deploy workflow targets one app.

**Assumptions (outside the repo, not findings), all unconfirmed. The user answered "nothing yet / not looked" about production monitoring:**

- App Service collects stdout and the log stream is available (`context/foundation/infrastructure.md` documents `az webapp log tail`).
- No App Insights, tracker or alert rules are configured, or they exist in the portal. Not visible from the repo.
- Production CORS is configured on the App Service platform (`az webapp cors add`, recorded in `context/deployment/deploy-plan.md:126,134`). The in-app policy allows only `http://localhost:4200` (`Program.cs:27-35`). Not re-verified live.
- The Data Protection key ring survives restarts and instances. Nothing in the code configures it (`AddDataProtection`/`PersistKeysTo` grep empty), and tokens depend on it.
- The host rewrites the client IP from forwarded headers, so the per-IP rate limit partitions by real client. There is no `UseForwardedHeaders` in code.
- Notifications for failed GitHub Actions runs (migration or probe) exist outside the repo.

## 3. What reaches the tracker

Static-only (no runtime probes were run, so every row is **inferred**). There is no tracker, so the third column is "what an on-call engineer gets from stdout". Items marked [fw] rely on ASP.NET Core, Identity or EF behaviour I did not verify in this repo.

| Failure shape | Response | Platform logs (stdout) | Verdict |
|---|---|---|---|
| Unhandled exception in a controller or service | 500 ProblemDetails with `traceId` | Error line with exception and stack from ExceptionHandlerMiddleware [fw]; no user, route or scope | acceptable, poor context |
| SQL transient, timeout (-2) or retry exhaustion | 503 "Service warming up", Retry-After 10 | Warning with exception, constant message, repeated about 12x per user by SPA retries | poor: outage looks like cold start |
| Result error, validation or invalid input (`InvalidLocation`, ...) | 400 ValidationProblem | nothing | missed |
| Valid token, user missing or claim malformed | bare 401 | nothing | missed |
| Identity `ConcurrencyFailure` or not-found in user store | 400 with raw Identity code | nothing | missed |
| Login failure or lockout | 401 (`LockedOut` detail) | nothing first-party; Identity's own lines are not shown at the configured level [fw] | missed |
| Rate limit (`/auth/*` 10/min/IP) | 429 | nothing (no `OnRejected`) | missed |
| Refresh rejected | 400/401, SPA silently logs the user out | nothing | missed |
| Blocked Identity route (stubbed) | empty 404 | nothing | missed |
| Any request (status, route, duration) | any | no request line (`Microsoft.AspNetCore: Warning`) [fw] | missed |
| SPA error (HTTP error, chunk load, bootstrap, geolocation, map, tiles) | UI state or blank page | nothing: console of the user's device only | missed |

## 4. Systemic root causes

1. **Result-style failures are converted to responses and never logged.** Controllers return `ValidationProblem` or bare `Unauthorized()` (`AccountController.cs:41-47,60-66,74-80`, `SightingsController.cs:27-37,80-94`, `ConfigurationController.cs:35-41`). `AccountRepository.cs:77-102` and `PigeonWatchUserStore.cs:58-59,76-81,94-99,108-113` turn faults into `IdentityResult.Failed`/codes with the cause dropped. This is a reasonable layering choice (the anti-enumeration rule forces a generic client answer), but nothing records the real code server-side. Explains A1, A2, A7, S2, S3, S7.
2. **Log configuration and the lack of correlation remove the evidence the framework would give.** `Microsoft.AspNetCore: Warning` hides request lines; there are no scopes, no user id and no trace id in logs; EF commands log at Information; nothing stamps a version. Explains P3, S8, P5, A13 and makes every other server finding harder to use.
3. **One broad "database unavailable" classifier plus an SPA that retries it.** `DatabaseUnavailableExceptionHandler.cs:101-105,130-139` treats timeouts, network errors and any `RetryLimitExceededException` as warm-up; `warmup.interceptor.ts:189` also retries status 0. Explains P1, P2.
4. **The SPA swallows failures at the edge and has no reporting seam.** `catchError(() => of(errorState))` in `nearby-map-page.ts:187`, `my-sightings.ts:53`, `profile-page.ts:171`, `map-view.ts:137`; `session.service.ts:67,210`; bare `catch {}` in `token-store.ts`, `geolocation.service.ts:46`; `app.config.ts:17` registers no `ErrorHandler`. Explains S1, A5, A6, A10, A11, S5, S6, P4.
5. **No auth security events.** There is no log for login, lockout, refresh or registration outcomes and no `OnRejected` on the rate limiter (`Program.cs:37-54`). Explains A3, A4, A8.

## 5. Findings by area

Severity rationale: none is rated critical. Real unhandled exceptions do reach the framework's Error log with a stack [fw], so no flow is completely invisible. The high findings are the ones where a realistic failure in a critical flow is undiagnosable.

### Auth (register, login, refresh, session expiry, profile and password)

| # | Location | Category | Severity | What happens in production | Fix direction |
|---|---|---|---|---|---|
| A1 | `Api/Data/Repositories/AccountRepository.cs:77-102`, `Api/WebApi/Controllers/AccountController.cs:74-80` | flattened-response | high | Any unmapped Identity code (`ConcurrencyFailure`, `DefaultError`) goes out as a 400 validation error, so a server fault on register, rename or password change looks like a user mistake. The `RegistrationFailed` collapse drops the original code (`DuplicateEmail`, ...) with no log. User sees the generic "unexpected" text; responder sees nothing. | Log the original Identity codes (codes and user id only, never email or display name) before collapsing; map unexpected codes to a 500 or a distinct code. Keep the client answer generic. |
| A2 | `Api/Data/Identity/PigeonWatchUserStore.cs:58-59,76-81,94-99,108-113` | swallowed | high | Concurrency and not-found branches return `IdentityResult.Failed` with the exception dropped and the entity detached. Lost updates on profile or password surface as 400 codes, no log. | Log each branch (Warning, user id, operation) before converting. |
| A3 | `Api/DependencyInjection/AccountServiceCollectionExtensions.cs:20-22` (lockout), `/auth/login` | logged-not-captured | high | A credential-stuffing run, a mass lockout or a login bug shows only as 401s. There is no application log of outcome, user id or count. Identity's own SignInManager lines are not visible at the configured level [fw]. | Observe `/auth/login` and `/auth/refresh` outcomes (succeeded, failed, locked out, not allowed) in an endpoint filter; log outcome and user id when known, never email, password or tokens. |
| A5 | `Frontend/src/app/core/auth/auth.interceptor.ts:33`, `Frontend/src/app/core/auth/session.service.ts:206-216` | swallowed | high | A 400/401 on refresh silently clears the session and sends the user to login; any other refresh error (5xx, warm-up budget exhausted) becomes `of(undefined)`, so the app starts anonymous. Support hears "keeps logging me out" and nobody can tell expired token, deleted user or key-ring change. | Report the status and trace id when the session is force-expired or restore fails; log refresh outcomes server-side (A3). |
| A6 | `Frontend/src/app/shared/auth/auth-errors.ts:35-41,116-149`, `Frontend/src/app/shared/http/problem-details.ts` | missing-context | high | Unknown codes and 5xx collapse to `common.unexpectedError`; status, codes and the ProblemDetails `traceId` are never shown or logged. The user cannot quote anything to support. | Show the `traceId` on the unexpected path and send it to the reporting seam (P4). |
| A4 | `Api/WebApi.Host/Program.cs:37-54` | logged-not-captured | medium | 429s leave no log (no `OnRejected`): abuse and a mis-set limit are invisible. If the host does not forward the client IP, all users share one 10/min bucket (unverified, see assumptions). `/auth/refresh` is exempt, so refresh abuse is unmonitored. | `OnRejected` logging policy, path and a truncated or hashed partition key. |
| A7 | `AccountController.cs:41-47,60-66`, `ConfigurationController.cs:35-41`, `SightingsController.cs:27-34` + `Api/Data/Repositories/SightingRepository.cs:12-15` | flattened-response | medium | Valid token with a deleted user (`UserNotFound`, `ReporterNotFound`) or a malformed `NameIdentifier` claim becomes a bare 401 with no body or log. The SPA refreshes, retries once, gets 401 again and shows a generic error. Indistinguishable from an expired session; drift after a DB restore would look like mass 401s. | Warning log with user id and endpoint; treat a missing claim on an authenticated principal as a server fault. |
| A10 | `session.service.ts:67`, `Frontend/src/app/core/resources/resource.service.ts:69,83`, `Frontend/src/app/features/profile/profile-page/profile-page.ts:171` | swallowed | medium | A failed `/configuration/client` load silently switches validation to the SPA's fallback rules; label loads fail silently; after a successful rename a failed reload leaves the header with the old name while the page says "saved". | Report through the seam; keep the cause. |
| A8 | `Api/DependencyInjection/PigeonWatchEndpointRouteBuilderExtensions.cs:32-57` | flattened-response | low | Calls to stubbed Identity routes return an empty 404 and the cookie-flag 400 is unlogged; stale clients and probes are invisible. | Log hits at Information with route and method. |
| A11 | `Frontend/src/app/core/auth/token-store.ts:23-52` | swallowed | low | Bare `catch {}` falls back to memory when storage is blocked or full; the user is signed out on reload with no signal. Acceptable fallback, but the rate is unknown. | Report once per session with status only, never the token. |
| A12 | `Api/Data/Auditing/HttpContextCurrentUserProvider.cs:13-19` | missing-context | low | Authenticated user without a `NameIdentifier` claim, or an anonymous request, is audited as `SYSTEM` with no log. Anonymous register and lockout-counter writes are attributed to `SYSTEM`. | Warning on the authenticated-but-no-claim branch. |
| A13 | `Api/BusinessLogic/Services/UiResourceService.cs:15-16` | logged-not-captured | low | A missing embedded `UiLabels.resx` throws a 500 for `GET /resources/{culture}` visible only in the framework log; the SPA hides it behind its bundled snapshot. | Log explicitly at first use. |

### Report and browse sightings (report, nearby map, detail, my sightings)

| # | Location | Category | Severity | What happens in production | Fix direction |
|---|---|---|---|---|---|
| S1 | `Frontend/src/app/features/sightings/nearby-map-page/nearby-map-page.ts:187`, `my-sightings/my-sightings.ts:53`, `sighting-detail-page/sighting-detail-page.ts:123,161-163`, `report-sighting-page/report-sighting-page.ts:114`, `sighting-errors.ts:13-23` | swallowed | high | Every API failure in the north-star loop becomes a generic error state; status, URL and `traceId` are discarded before the global handler could see them. A 401, a malformed response and a network drop are all invisible; only a server-side 500 leaves a trace. | One shared `reportError(context, error)` used in each handler (see P4). |
| S2 | `Api/WebApi/Controllers/SightingsController.cs:36-37,80-94`, `Api/BusinessLogic/Services/SightingService.cs:23-24,37-39` | flattened-response | medium | `InvalidLocation`, `InvalidInitialStatus` and `InvalidCriticality` can only occur if the SPA sent something it never should (frontend bug, version skew) but look exactly like user mistakes. If a deploy breaks the form payload, every report returns 400 and no log shows it. Cooldown is a legitimate 400. | Warning with user id and code for the three invalid-input codes; no request bodies. |
| S6 | `Frontend/src/app/shared/map/map-view/map-view.ts:105-115,128-130`, `Frontend/src/app/shared/map/map-config.ts:3` | swallowed | medium | A Leaflet chunk load failure (typical after a deploy) sets `loadFailed` with the import error dropped; tile 4xx/5xx or an OSM policy block gives a grey map with working markers and no state or signal. | Keep and report the import error; rate-limited `tileerror` listener. |
| S8 | `Api/WebApi.Host/appsettings.json` | noise | medium | `Default: Information` makes EF write an "Executed DbCommand" line for every query; the nearby search and `/mine` flood the stream and bury the Warning/Error lines that matter [fw]. | `Microsoft.EntityFrameworkCore: Warning`. |
| S3 | `SightingsController.cs:44-52`, `SightingRepository.cs:25-33`, `sighting-errors.ts:16` | flattened-response | low | A deleted sighting, a stale link and a malformed GUID (routing 404, never reaches a handler) all look the same; no record of which ids users hit. | Log misses at Information with id and user id. |
| S4 | `nearby-map-page.ts:251-257` | swallowed | low | On error `applyState` clears the markers, so a failed refresh looks like an empty area on a screenshot (the status text does show the error). | Keep previous markers on error and report (S1). |
| S5 | `Frontend/src/app/core/geolocation/geolocation.service.ts:43-53`, `nearby-map-page.ts:259-269`, `location-picker.ts:47-54,81-86` | swallowed | low | Geolocation failure is mapped to a status and the bare `catch {}` drops real exceptions (e.g. SecurityError). The UI informs the user, but a regression looks like "user denied" with no volume signal. | Report once per session with status and caught exception. |
| S7 | `Api/Data/Repositories/SightingRepository.cs:20,45-55` | missing-context | low | DB errors propagate correctly (not swallowed) but the 500 log has no operation, user or coordinates. | Covered by the logging scope (P3); do not add a catch. |

### Platform / plumbing

| # | Location | Category | Severity | What happens in production | Fix direction |
|---|---|---|---|---|---|
| P1 | `Api/Data/Diagnostics/DatabaseUnavailableExceptionHandler.cs:101-105,112,130-139`, `Frontend/src/app/core/warmup/warmup.interceptor.ts:39-65,84-96` | flattened-response | high | Auto-pause, a real outage, a query timeout (-2) and a `RetryLimitExceededException` all return 503 "Service warming up" with a Warning that has a constant message and no SQL number, route or user. The SPA retries every 10 s for 120 s, so one user produces about 12 identical warnings; at the end the "failed" outcome is a UI flag only. A 20-minute outage looks like a stream of cold-start warnings. | Log SQL error number and path as structured fields; split timeouts from availability errors; escalate to Error after the expected cold-start window; report when the warm-up budget is exhausted. |
| P3 | `Api/WebApi.Host/appsettings.json:5`, `Api/WebApi.Host/Program.cs` (no scopes) | config | high | No per-request line (route, status, duration) exists, and no log line carries a user id or the response's `traceId`. "Which endpoint is failing and how often" cannot be answered from stdout, and a user quoting a `traceId` cannot be matched to a log line [fw: default console logger does not print scopes]. | Middleware opening a scope with user id (not email) and trace id after authentication; enable console scopes; a small request-summary log. |
| P4 | `Frontend/src/main.ts:6`, `Frontend/src/app/app.config.ts:17`, `Frontend/src/app/app.routes.ts` (six `loadComponent`) | coverage-gap | high | Bootstrap failures, unhandled rejections and chunk-load or navigation errors after a deploy stay in the user's console. A blank page produces no server-side signal. | Custom `ErrorHandler`, `withNavigationErrorHandler`, and a small client-report endpoint (rate-limited, no PII) or a tracker SDK. |
| P2 | `warmup.interceptor.ts:189` | flattened-response | medium | Status 0 (network drop, blocked request, any CORS regression) is treated as warm-up and retried for 120 s, then ends as a UI "failed" state; the server logs nothing because the request never arrives. | Treat status 0 separately from 503; report a final status-0 failure with the URL. |
| P5 | `Api/WebApi.Host/Program.cs`, `Api/WebApi.Host/PigeonWatch.WebApi.Host.csproj` | config | low | No version, commit or environment in logs or responses; a responder cannot tell which build produced a failure. | Log the informational version at startup. |
| P6 | `.github/workflows/deploy-api.yml` (`Probe API`), `context/deployment/deploy-plan.md:107` | coverage-gap | low | The only probe is a one-off `curl` of `/configuration/client` after deploy; it never touches the database, so a DB outage or CORS regression is invisible to it. This is a documented free-tier cost decision, noted for completeness. | Optional second probe or monitor that checks the missing property cheaply. |

## 6. Recommended fix order

1. **Logging baseline (P3, S8, S7, A12).** Add a middleware that opens a logging scope with the user id from `NameIdentifier` and `TraceIdentifier` after `UseAuthentication`, turn on console scopes, keep `Microsoft.AspNetCore.Hosting.Diagnostics` at Information (or add a minimal request-summary line) and cap EF at Warning. One small change gives context to every other server finding. *Constraint:* user id only, never email or display name.
2. **One shared error-to-response helper with logging (A1, A7, S2, S3).** Replace the duplicated `GetValidationProblem`. Log original codes before collapsing, log lost-user 401s, and map unexpected Identity codes to a 500. *Constraint:* the anti-enumeration rule in `PigeonWatch/CLAUDE.md` stays; the client keeps the generic `RegistrationFailed`, only the server log gets the real code, with no email or display name.
3. **Log the user store branches (A2).** Warning with operation and user id before converting to `IdentityResult.Failed`.
4. **Fix the 503 classifier (P1).** Structured SQL number and path, treat timeouts separately, escalate after the cold-start window. Keep the client contract (503 plus `Retry-After`) so the warm-up panel still works.
5. **Auth events and rate-limit rejections (A3, A4, A8).** Outcome logging for login and refresh, `OnRejected` with a truncated or hashed IP key. *Constraint:* no email, password or token values.
6. **SPA reporting seam (P4, S1, S6, A5, A6, A10, A11, S4, S5, P2).** One `ErrorHandler`, one reporting service, one endpoint (or a tracker), and show the `traceId` in the error UI. Do this after step 1, so the report can be matched to a scoped server log. *Constraint:* the endpoint is an abuse vector, so rate-limit it and accept only status, route, trace id and error name, never tokens or emails.
7. **Polish (P5, P6, A13).** Version stamp at startup; optional second probe.

## 7. Changes since last audit

Omitted: first run.

## 8. Method and limits

- **Agents run (all read-only):** one auditor per area (auth, report/browse sightings) plus one plumbing auditor. Static only: runtime proof was offered and declined (`runtime_proof: not-run`), so no finding is runtime-proven.
- **Verified by hand:** `session.service.ts:206-216` (restore swallows non-session errors), `nearby-map-page.ts:187` (error discarded), `SightingsController.cs:27-34` + `SightingRepository.cs:12-15` (silent 401 when the reporter is missing), `AccountRepository.cs:77-102` (code passthrough and collapse), `PigeonWatchUserStore.cs:76-81` (concurrency swallowed), and greps showing no `AddDataProtection`, `ForwardedHeaders`, `OnRejected` or `BeginScope` anywhere in `Api` outside tests.
- **Dropped after checking:** an auditor's high finding that production CORS only allows localhost. The deploy plan records platform CORS configured via `az webapp cors add` (`deploy-plan.md:126,134`), so it moved to Assumptions. The related status-0-as-warm-up behaviour stays as P2.
- **Not verified (framework behaviour, reasoned from knowledge, marked [fw]):** that the exception-handler middleware logs 500s at Error with the stack; that Identity's SignInManager failure lines are below the configured level; that EF commands log at Information; that the default console logger omits scopes. Two further claims by the auth auditor (a failed lockout-counter write being ignored by SignInManager, and unprotected refresh tokens after a key change) were not carried into findings because they depend on library internals and hosting.
- **Repo-wide sweep (excluding tests, `node_modules`, `dist`, Migrations, `playwright-report`):** backend: 1 `ILogger` call, 5 `catch` clauses (4 in `PigeonWatchUserStore`, 1 culture fallback in `UiResourceService`), 0 empty catches, 0 `Console.*`. Frontend: 7 bare `catch {}` (storage, geolocation), 0 `.catch(` outside `main.ts`, about 6 `catchError` that substitute a default, 2 production `console.*`, about 6 `subscribe` calls without an error callback.
- **What was not verifiable:** how App Service actually renders stdout, log retention, whether anyone reads the logs, the live CORS state, the Data Protection key ring and the forwarded-IP behaviour.
- **No harness files or worktrees were created.**
