# Server-side observability logging Implementation Plan

## Overview

Implement steps 1 to 5 of the 2026-10-25 observability audit (`context/audits/observability/2026-10-25_auth-sightings.md`), API only. Today the API has one first-party log call, so every Result-style failure ends as a bare 400, 401, 404 or 429 with nothing in stdout, no log line carries a user id or trace id, and a database outage is indistinguishable from a cold start. This plan adds structured, privacy-safe server logs without changing any client-facing response, including the generic `RegistrationFailed`. The SPA reporting seam (audit step 6) and polish (step 7) are separate later changes.

## Current State Analysis

- One `ILogger` call in the whole API: `Api/Data/Diagnostics/DatabaseUnavailableExceptionHandler.cs:112`, a constant-message Warning with no route or SQL number.
- Pipeline in `Api/WebApi.Host/Program.cs:58-80`: `UseExceptionHandler`, routing, CORS, authentication, rate limiter, authorization, controllers. The rate limiter only sets `RejectionStatusCode = 429` (`:37-54`), no `OnRejected`.
- `Api/WebApi.Host/appsettings.json`: `Default: Information`, `Microsoft.AspNetCore: Warning`, so there are no request lines, and EF writes an "Executed DbCommand" line for every query. No `BeginScope` anywhere, console scopes are off.
- Controllers turn Result errors into `ValidationProblem` or bare `Unauthorized()` with the mapper duplicated in `AccountController.cs:74-80` and `SightingsController.cs:80-94`; bare 401s at `AccountController.cs:41-47,60-66`, `ConfigurationController.cs:35-41`, `SightingsController.cs:27-34`; 404 miss at `SightingsController.cs:44-52`.
- `Api/Data/Repositories/AccountRepository.cs:77-102` passes unmapped Identity codes through and collapses `DuplicateEmail`/`DuplicateUserName`/`DuplicateDisplayName` into `RegistrationFailed` without a trace. `Api/Data/Identity/PigeonWatchUserStore.cs:58-59,76-81,94-99,108-113` returns `ConcurrencyFailure` and drops the exception.
- `Api/Data/Auditing/HttpContextCurrentUserProvider.cs:13-19` silently falls back to `SYSTEM` for an authenticated principal without a `NameIdentifier`.
- Login and refresh are Identity's own minimal endpoints mapped in `Api/DependencyInjection/PigeonWatchEndpointRouteBuilderExtensions.cs:12-60` with no outcome logging; stubbed routes and the cookie-flag 400 return silently (`:32-57`).
- Tests: no integration test captures logs and no fake-logger package is referenced; unit tests assert logs with a substituted `ILogger<T>` (`UnitTests/DatabaseUnavailableExceptionHandlerTests.cs:16,73-76`).

## Desired End State

A responder reading stdout can:

- see one summary line per request (method, route template, status, duration) with no query string or ids;
- see every log line of a request carry the trace id, and the user id once the caller is authenticated, including the 500 logged by the exception middleware;
- see why a request ended in an unexplained 400, 401 or 404 (code, user id, route), why registration collapsed to `RegistrationFailed` (original code, no email), and when the user store lost an update;
- tell a cold-start 503 (Warning) from a real database outage (Error), with the SQL error number and route;
- see login outcomes (succeeded, failed, locked out, not allowed), 429 rejections (policy, route, missing-IP flag) and hits on stubbed routes.

Verification: the new integration and unit tests below pass, all existing suites pass unchanged except for constructor updates, and a manual local run shows the expected lines with no emails, tokens, query strings or IPs.

### Key Discoveries:

- `ExceptionHandlerMiddleware` clears the endpoint and route values before running `IExceptionHandler`s and logs the exception outside any scope opened by inner middleware. A scope opened after `UseAuthentication` therefore does not carry the user id on the 500 log line, and the endpoint is gone when the summary line is written for a 500 (`IExceptionHandlerFeature.Endpoint` still holds it).
- A named middleware or endpoint-filter class with `InvokeAsync` fails the `CancellationTokenTests` rule (`ArchitectureTests/CancellationTokenTests.cs:10-42`); use inline lambdas (`app.Use(async (context, next) => ...)`, `AddEndpointFilter(...)`, `OnRejected = async (context, token) => ...`).
- An `IApplicationBuilder` extension class violates `StaticClassTests` (`ArchitectureTests/StaticClassTests.cs:14-62`), so the middleware lives inline in `Program.cs`.
- Types named `*Service`, `*Provider`, `*Mapper`, `*Repository` or `*ViewModelCreator` need an interface and a `DependencyInjection` registration (`ServiceRegistrationTests.cs:7`); `*Middleware`, `*Filter`, `*Handler`, `*Helper`, `*Writer` do not.
- WebApi.Host may not reference Data or BusinessLogic types; `ILogger<T>` is allowed in every layer (`LayerDependencyTests.cs`).
- `IntegrationTests/PigeonWatchApiFactory.cs` leaves logging untouched and already has `CloseDatabase` (triggers a real DB exception for 500 paths), `CreateUserAsync` and per-client rate-limit partitions.
- The existing 429 test asserts an empty body (`IntegrationTests/LockoutAndRateLimitContractTests.cs`); `OnRejected` must not write a body.
- Constructor changes break existing test helpers: `CreateHandler` in `DatabaseUnavailableExceptionHandlerTests`, `CreateStore` in `PigeonWatchUserStoreTests`, `AccountRepository` construction in `AccountRepositoryTests`, `HttpContextCurrentUserProvider` tests.

## What We're NOT Doing

- No SPA changes: no `ErrorHandler`, no reporting endpoint, no `traceId` in the UI (audit step 6, a separate change).
- No version or commit stamping, no second health probe, no `UiResourceService` startup log (audit step 7).
- No error tracker, no App Insights, no database log table (rejected in discussion; the platform log stream is the pipeline).
- No change to any response status, body, header or error code, including keeping unexpected Identity failures as 400 (decided: log only) and the generic `RegistrationFailed`.
- No change to the 503 contract: status 503, `Retry-After: 10` and ProblemDetails body stay, so the SPA warm-up retry keeps working.
- No logging of emails, display names, passwords, tokens, query strings, request bodies or client IP addresses.
- No `Data Protection` key-ring configuration and no forwarded-headers change (unverified hosting assumptions from the audit).
- No logging of cooldown rejections or model-binding validation 400s (expected user conditions).

## Implementation Approach

Five phases, each test-first, ordered so the baseline lands first. Phase 1 adds the fake-logger test infrastructure and the two pieces of middleware that give every later log line its context. Phases 2 to 5 each add logging for one failure family and assert it through the real pipeline (integration) or with the repo's existing substituted-`ILogger` style (unit). Message templates use structured placeholders (`{ErrorCode}`, `{Route}`, `{SqlErrorNumber}`), never string interpolation, so values stay queryable and no PII is formatted into text.

Level policy used across phases: expected-but-invisible conditions (collapsed duplicates, stubbed-route hits, 404 misses) log at Information; things that should not happen from a correct client or token (orphan user, malformed claim, invalid sighting input codes, concurrency lost update, missing-claim audit fallback, 429 rejections, failed logins) log at Warning; server faults (unknown Identity code, non-cold-start database unavailability) log at Error.

## Critical Implementation Details

- **Timing & lifecycle:** the exception middleware logs outside any inner scope and clears the endpoint, so the scope and summary middleware must be registered before `UseExceptionHandler()`, open the scope with a mutable user-id slot, and fill that slot with the `NameIdentifier` claim from a second inline `app.Use` placed after `UseAuthentication`. Build the scope as a single mutable state object whose enumeration is evaluated at log time (console and fake logger read it lazily), not a snapshot taken at scope creation. For the summary line, read the route template from the endpoint when present and fall back to `IExceptionHandlerFeature.Endpoint` when the exception middleware has cleared it, and never log `Request.Path` or `Request.QueryString`.

## Phase 1: Log capture and baseline logging

### Overview

Make logs assertable and give every later log line context: fake-logger test infrastructure, a scope carrying trace id and user id, a per-request summary line, EF capped at Warning, console scopes on, and a Warning for the `SYSTEM` audit fallback.

### Changes Required:

#### 1. Fake-logger test infrastructure

**File**: `PigeonWatch/Api/IntegrationTests/PigeonWatch.IntegrationTests.csproj`, `PigeonWatch/Api/IntegrationTests/PigeonWatchApiFactory.cs`

**Intent**: Add `Microsoft.Extensions.Diagnostics.Testing` (version matching the other 10.0.x packages) and register fake logging in the factory so tests can read recorded entries, their structured state and scopes.

**Contract**: The factory exposes the fake log collector (for example a `Logs` accessor returning recorded `FakeLogRecord`s) and a way to clear it between scenarios. Existing tests are unaffected. Run the vulnerability audit command for the integration project afterwards.

#### 2. Scope and request-summary middleware

**File**: `PigeonWatch/Api/WebApi.Host/Program.cs`

**Intent**: Before `UseExceptionHandler()`, add an inline `app.Use` that opens a logging scope carrying `TraceId` (and a `UserId` slot that starts empty), calls the pipeline in `try/finally`, and writes one summary log line in the `finally`. After `UseAuthentication()`, add a second inline `app.Use` that writes the `NameIdentifier` claim, when it is a valid GUID, into the scope's user-id slot.

**Contract**: Summary line at Information: method, route template (endpoint pattern text, or `unmatched` when none), status code, duration in ms. No path, query string, ids or user data in the message. The scope state exposes `TraceId` and `UserId` key/value pairs; `UserId` is absent for anonymous requests. Both lambdas are inline (no named class, no extra static class). See Critical Implementation Details for ordering and the 500-path fallback.

#### 3. Logging configuration

**File**: `PigeonWatch/Api/WebApi.Host/appsettings.json`

**Intent**: Turn on console scopes and cap EF chatter so Warning and Error lines and the new summary line stand out.

**Contract**: `Logging:Console:IncludeScopes = true`; add `Microsoft.EntityFrameworkCore` at `Warning`. `Default` and `Microsoft.AspNetCore` stay as they are. Do not touch `appsettings.Development.json`.

#### 4. Audit fallback warning

**File**: `PigeonWatch/Api/Data/Auditing/HttpContextCurrentUserProvider.cs`

**Intent**: Log a Warning when an authenticated principal has no `NameIdentifier` claim and the provider falls back to `SYSTEM`, so the silent fallback is visible. Anonymous requests and a missing `HttpContext` stay silent.

**Contract**: Adds an `ILogger<HttpContextCurrentUserProvider>` constructor parameter (primary constructor); update the existing unit tests that construct it.

### Success Criteria:

#### Automated Verification:

- New integration tests, written first and seen failing, pass: every request logs one summary line with method, route template, status and duration; the summary line for a `GET /sightings/nearby?latitude=..&longitude=..` request contains no coordinates; an authenticated request's log records carry the `UserId` and `TraceId` scope values; a 500 triggered by closing the database logs the exception with the same scope values and a summary line carrying the route template and status 500: `dotnet test PigeonWatch/Api/IntegrationTests/PigeonWatch.IntegrationTests.csproj`
- New unit test, written first and seen failing: an authenticated principal without a `NameIdentifier` logs one Warning and returns `SYSTEM`; an anonymous request logs nothing: `dotnet test PigeonWatch/Api/UnitTests/PigeonWatch.UnitTests.csproj`
- Appsettings test or assertion: EF command lines at Information are not emitted for a request through the factory.
- Architecture tests pass: `dotnet test PigeonWatch/Api/ArchitectureTests/PigeonWatch.ArchitectureTests.csproj`
- Solution builds with style rules enforced: `dotnet build PigeonWatch/Api/PigeonWatchApi.slnx`
- New test package has no known vulnerabilities: `dotnet list PigeonWatch/Api/IntegrationTests/PigeonWatch.IntegrationTests.csproj package --vulnerable --include-transitive`

#### Manual Verification:

- Running the API locally and calling login, `GET /sightings/nearby` and a missing route shows scoped lines and one summary line per request in the console, with no query string, email or token visible.

**Implementation Note**: After completing this phase and all automated verification passes, pause here for manual confirmation from the human that the manual testing was successful before proceeding to the next phase.

---

## Phase 2: Controller failure logging

### Overview

Make the bare 401s, the invalid-input 400s and the detail 404 leave a log line, with one shared helper replacing the two duplicated `GetValidationProblem` copies. Client responses stay byte-for-byte the same.

### Changes Required:

#### 1. Shared failure logging helper

**File**: new file in `PigeonWatch/Api/WebApi/Controllers/` (for example a shared abstract controller base), plus `AccountController.cs`, `ConfigurationController.cs`, `SightingsController.cs`

**Intent**: One place that builds the `ValidationProblem` response from error codes (keeping the `retryAfterSeconds` extension and `Retry-After` header behaviour of the sightings copy) and logs the failure. Controllers call it instead of their private copies.

**Contract**: A non-static type in the `PigeonWatch.WebApi.Controllers` namespace that satisfies `ControllerActionTests`/`TypeLocationTests` (preferred: abstract base deriving from `ControllerBase` taking an `ILogger`). If the architecture tests reject a base class, fall back to injecting `ILogger<TController>` in each controller and keep one non-`*Mapper`/`*Provider` helper class for the shared response building; the choice must not change any public action signature.

#### 2. Failure log points

**File**: the three controllers above

**Intent**: Log, with structured fields and the user id supplied by the scope (never email, display name or request bodies): malformed or missing `NameIdentifier` on an authenticated endpoint (Warning, `Route`), `UserNotFound`/`ReporterNotFound` 401s (Warning, "authenticated user no longer exists"), the three invalid-input sighting codes `InvalidLocation`, `InvalidInitialStatus`, `InvalidCriticality` (Warning, `ErrorCode`), and the sighting detail 404 miss (Information, `SightingId`). `CooldownActive` and model-binding validation errors log nothing.

**Contract**: Message templates use `{ErrorCode}`, `{Route}`, `{SightingId}` placeholders. Responses (status, body, headers) are identical to today; the existing integration and unit contract tests are the regression net.

### Success Criteria:

#### Automated Verification:

- New integration tests, written first and seen failing: a token for a deleted user calling `POST /sightings` logs a Warning with the user id scope and returns the same bare 401; an invalid-location create logs a Warning with `ErrorCode=InvalidLocation` and returns the same 400 body; a cooldown rejection logs no Warning or Error; a missing sighting id logs one Information with the id and returns the same 404: `dotnet test PigeonWatch/Api/IntegrationTests/PigeonWatch.IntegrationTests.csproj`
- New unit tests, written first and seen failing, for the helper or controllers where a unit seam exists: `dotnet test PigeonWatch/Api/UnitTests/PigeonWatch.UnitTests.csproj`
- Architecture tests pass: `dotnet test PigeonWatch/Api/ArchitectureTests/PigeonWatch.ArchitectureTests.csproj`
- Solution builds with style rules enforced: `dotnet build PigeonWatch/Api/PigeonWatchApi.slnx`

#### Manual Verification:

- Calling `POST /sightings` with a coordinate outside range and `GET /sightings/{unknown-guid}` locally shows one log line each with the expected level and no personal data, and the SPA still behaves as before.

**Implementation Note**: After completing this phase and all automated verification passes, pause here for manual confirmation from the human that the manual testing was successful before proceeding to the next phase.

---

## Phase 3: Identity and user-store logging

### Overview

Record the original Identity codes before they are collapsed or passed through, and record the user store's swallowed concurrency and not-found branches. Responses stay 400 with the same bodies.

### Changes Required:

#### 1. Account repository logging

**File**: `PigeonWatch/Api/Data/Repositories/AccountRepository.cs`

**Intent**: In `ToAccountErrors` (or at its call sites), log each original Identity code before collapse or passthrough. Codes in the known `AccountErrorCodes` validation set log nothing except the collapsed duplicates; `DuplicateEmail`, `DuplicateUserName` and `DuplicateDisplayName` that collapse into `RegistrationFailed` log at Information with the original `ErrorCode` and the operation (register, rename, password change); `ConcurrencyFailure` logs a Warning; `DefaultError` and any code outside the known set log an Error.

**Contract**: Adds `ILogger<AccountRepository>` to the primary constructor. Never log the email, user name, display name, password or the Identity `Description`. The client-facing codes and descriptions produced by `ToAccountErrors` do not change (the anti-enumeration rule in `PigeonWatch/CLAUDE.md` stays intact: the generic answer is client-side only).

#### 2. User store logging

**File**: `PigeonWatch/Api/Data/Identity/PigeonWatchUserStore.cs`

**Intent**: Log a Warning (operation name and user id) in each branch that converts a missing entity or `DbUpdateConcurrencyException` into `IdentityResult.Failed(ConcurrencyFailure)` (`:58-59,76-81,94-99,108-113`), keeping the existing detach-and-return behaviour. The expected duplicate-index branches stay silent.

**Contract**: Adds `ILogger<PigeonWatchUserStore>` to the primary constructor; the user id is a GUID, not PII. Update `CreateStore` in `UnitTests/PigeonWatchUserStoreTests.cs` and the `AccountRepository` construction in `AccountRepositoryTests`.

### Success Criteria:

#### Automated Verification:

- New unit tests, written first and seen failing: registering a duplicate email logs one Information with `ErrorCode=DuplicateEmail` and no email text, while the returned error is still `RegistrationFailed`; a stale concurrency stamp on update logs a Warning with the user id and returns the same `ConcurrencyFailure`; an unknown Identity code logs an Error and is passed through unchanged; no log message or state contains the email or display name used in the test: `dotnet test PigeonWatch/Api/UnitTests/PigeonWatch.UnitTests.csproj`
- New integration test, written first and seen failing: a duplicate registration through `POST /account/register` records the original code in the log while the response body stays the generic `RegistrationFailed`, and the existing privacy and anti-enumeration contract tests pass unchanged: `dotnet test PigeonWatch/Api/IntegrationTests/PigeonWatch.IntegrationTests.csproj`
- Architecture tests pass: `dotnet test PigeonWatch/Api/ArchitectureTests/PigeonWatch.ArchitectureTests.csproj`
- Solution builds with style rules enforced: `dotnet build PigeonWatch/Api/PigeonWatchApi.slnx`

#### Manual Verification:

- Registering the same email twice locally logs one Information line with the original code and no email text, while the browser form shows the same generic message as before.

**Implementation Note**: After completing this phase and all automated verification passes, pause here for manual confirmation from the human that the manual testing was successful before proceeding to the next phase.

---

## Phase 4: 503 handler

### Overview

Keep the 503 + `Retry-After` contract and make the log distinguish a cold start from an outage, with the SQL error number and route as structured fields.

### Changes Required:

#### 1. Classify and log

**File**: `PigeonWatch/Api/Data/Diagnostics/DatabaseUnavailableExceptionHandler.cs`

**Intent**: Find the first `SqlException` in the inner-exception chain and use its number. Known cold-start numbers (`40613`, `40197`, `40501`, `49918`, `49919`, `49920`) keep logging at Warning; other unavailable causes (the remaining numbers in the existing set, and `RetryLimitExceededException` with no `SqlException` in its chain) log at Error. The response is unchanged.

**Contract**: Message template with `{SqlErrorNumber}`, `{Route}` and `{RetryAfterSeconds}` placeholders (no raw path, no query string); `Route` comes from the route pattern of `IExceptionHandlerFeature.Endpoint` (the endpoint is cleared on the context by the time handlers run), `unknown` when absent. The exception object is still passed as the exception argument. Constructor signature stays `(IProblemDetailsService, ILogger<...>)`; update the assertions in `UnitTests/DatabaseUnavailableExceptionHandlerTests.cs` (the existing `ReceivedCalls` style) for the new levels and fields.

### Success Criteria:

#### Automated Verification:

- New unit tests, written first and seen failing: error 40613 logs Warning with `SqlErrorNumber=40613`; a timeout (-2) logs Error; `RetryLimitExceededException` wrapping a 40613 logs Warning; `RetryLimitExceededException` with no SQL cause logs Error; every case still produces 503, `Retry-After: 10` and the same problem body; non-database exceptions still return `false` and log nothing: `dotnet test PigeonWatch/Api/UnitTests/PigeonWatch.UnitTests.csproj`
- Existing warm-up and cold-start integration tests pass unchanged: `dotnet test PigeonWatch/Api/IntegrationTests/PigeonWatch.IntegrationTests.csproj`
- Architecture tests pass: `dotnet test PigeonWatch/Api/ArchitectureTests/PigeonWatch.ArchitectureTests.csproj`
- Solution builds with style rules enforced: `dotnet build PigeonWatch/Api/PigeonWatchApi.slnx`

#### Manual Verification:

- With LocalDB stopped, a request locally returns the same 503 as before and logs an Error with a SQL error number and route template.

**Implementation Note**: After completing this phase and all automated verification passes, pause here for manual confirmation from the human that the manual testing was successful before proceeding to the next phase.

---

## Phase 5: Auth events and 429 logging

### Overview

Log login and refresh outcomes, rate-limit rejections and hits on the stubbed Identity routes, all without email, tokens or IP addresses.

### Changes Required:

#### 1. Login and refresh outcome filter

**File**: `PigeonWatch/Api/DependencyInjection/PigeonWatchEndpointRouteBuilderExtensions.cs`

**Intent**: Add an inline endpoint filter on the `auth` group that records the outcome of `POST auth/login` and `POST auth/refresh`: succeeded, failed, locked out, not allowed (read from the Identity problem result detail, with the HTTP status as fallback), and for refresh the rejected cases that silently log the user out in the SPA. Failed and locked-out outcomes log at Warning, successes at Information.

**Contract**: Inline lambda filter (no named class, no `InvokeAsync`). Log fields: `Outcome`, `Route`. Never the email, password, token or user name; user id is added only when the principal is known. The extension class keeps containing only extension methods. Behaviour and responses of login and refresh are unchanged.

#### 2. Stubbed-route and cookie-flag hits

**File**: same file, in the existing `.Finally` convention (`:32-57`)

**Intent**: Log Information (route template, HTTP method) when a replaced Identity route returns its 404 stub, and when the cookie-flag 400 is returned.

**Contract**: The ILogger comes from `context.RequestServices`. The stub's empty 404 body is unchanged.

#### 3. Rate-limit rejection logging

**File**: `PigeonWatch/Api/WebApi.Host/Program.cs`

**Intent**: Add `OnRejected` to the rate limiter that logs a Warning with the policy name, the route template and a flag saying whether the remote IP was missing, and leaves the response an empty 429.

**Contract**: Inline `OnRejected = async (context, token) => ...`; does not log the IP, truncated or full; does not write a body (the existing 429 test asserts an empty body).

### Success Criteria:

#### Automated Verification:

- New integration tests, written first and seen failing: a successful login logs `Outcome=Succeeded` at Information; a wrong-password login logs `Outcome=Failed` at Warning; after five failures the next login logs `Outcome=LockedOut`; none of these log lines or their state contain the email, password or tokens; an eleventh request inside a minute is rejected with the same empty 429 and logs one Warning with the policy name and route; a call to a stubbed Identity route logs one Information and returns the same empty 404: `dotnet test PigeonWatch/Api/IntegrationTests/PigeonWatch.IntegrationTests.csproj`
- Existing lockout, rate-limit and auth contract tests pass unchanged: same command.
- Architecture tests pass: `dotnet test PigeonWatch/Api/ArchitectureTests/PigeonWatch.ArchitectureTests.csproj`
- Unit tests and solution build pass: `dotnet test PigeonWatch/Api/UnitTests/PigeonWatch.UnitTests.csproj` and `dotnet build PigeonWatch/Api/PigeonWatchApi.slnx`

#### Manual Verification:

- Logging in with a wrong password six times locally logs failed attempts then a locked-out line; the console shows no email or password anywhere in those lines.
- The audit findings A1 to A4, A8, A12, P1, P3, S2, S3 and S8 can each be checked against the new log output, ready for a `--verify` run of the audit.

**Implementation Note**: After completing this phase and all automated verification passes, pause here for manual confirmation from the human that the manual testing was successful before proceeding.

---

## Testing Strategy

### Unit Tests:

- Repo style: `Sentence_with_underscores` names, `[Fact]`/`[Theory]`, `TestContext.Current.CancellationToken`, substituted `ILogger<T>` with `ReceivedCalls()` filtered to `Log` for level, state and exception assertions.
- Cover: audit fallback warning, Identity code logging and no-PII assertions, user-store concurrency branches, handler level split, handler contract unchanged.

### Integration Tests:

- Fake logging through the real pipeline for scope, summary line, 500 path, failure families, login outcomes, 429 and stubbed routes.
- Each assertion that a response is unchanged reuses existing helpers (`CreateUserAsync`, `CloseDatabase`, `AccountApiClient`, per-client rate-limit partitions); no existing assertion is edited.

### Manual Testing Steps:

1. Run the API locally; log in, open `GET /sightings/nearby`, hit a missing route; confirm scoped lines and summary lines with no query string.
2. Register the same email twice; confirm the original code in the log and the generic message in the SPA.
3. Stop LocalDB; confirm 503 plus an Error with SQL number and route.
4. Fail login six times; confirm failed then locked-out lines and no email in them.

## Performance Considerations

One extra log line per request and a scope allocation; the EF cap to Warning removes one line per query, so net log volume drops. The F1 log storage is small, so keep message templates short and avoid logging bodies.

## Migration Notes

None: no schema, response or configuration contract changes. `appsettings.json` gains the console scope flag and one EF level. The existing deploy workflow needs no change.

## References

- Audit report (evidence for this change): `context/audits/observability/2026-10-25_auth-sightings.md`
- Change identity: `context/changes/server-side-observability-logging/change.md`
- Existing log assertion style: `PigeonWatch/Api/UnitTests/DatabaseUnavailableExceptionHandlerTests.cs:16,73-76`
- Integration factory: `PigeonWatch/Api/IntegrationTests/PigeonWatchApiFactory.cs`
- Architecture constraints: `PigeonWatch/Api/ArchitectureTests/CancellationTokenTests.cs`, `StaticClassTests.cs`, `ServiceRegistrationTests.cs`, `LayerDependencyTests.cs`
- Privacy and anti-enumeration rules: `PigeonWatch/CLAUDE.md`, `PigeonWatch/Api/CLAUDE.md`

## Progress

> Convention: `- [ ]` pending, `- [x]` done. Append ` — <commit sha>` when a step lands. Do not rename step titles. See `references/progress-format.md`.

### Phase 1: Log capture and baseline logging

#### Automated

- [x] 1.1 New integration tests for scope, summary line, no query string and 500 path pass — 54b1dcb
- [x] 1.2 New unit test for the SYSTEM audit fallback warning passes — 54b1dcb
- [x] 1.3 EF command lines at Information are not emitted through the factory — 54b1dcb
- [x] 1.4 Architecture tests pass — 54b1dcb
- [x] 1.5 Solution builds with style rules enforced — 54b1dcb
- [x] 1.6 New test package has no known vulnerabilities — 54b1dcb

#### Manual

- [x] 1.7 Local run shows scoped lines and one summary line per request with no query string, email or token — 54b1dcb

### Phase 2: Controller failure logging

#### Automated

- [x] 2.1 New integration tests for orphan-token 401, invalid-location 400, cooldown silence and 404 miss pass
- [x] 2.2 New unit tests for the shared helper or controllers pass
- [x] 2.3 Architecture tests pass
- [x] 2.4 Solution builds with style rules enforced

#### Manual

- [x] 2.5 Local invalid create and unknown sighting id each log one line with the expected level and no personal data

### Phase 3: Identity and user-store logging

#### Automated

- [ ] 3.1 New unit tests for Identity code logging, concurrency branches and no-PII assertions pass
- [ ] 3.2 New integration test for duplicate registration logging with unchanged generic response passes
- [ ] 3.3 Architecture tests pass
- [ ] 3.4 Solution builds with style rules enforced

#### Manual

- [ ] 3.5 Duplicate registration locally logs the original code without the email while the form shows the same generic message

### Phase 4: 503 handler

#### Automated

- [ ] 4.1 New unit tests for the level split, structured fields and unchanged 503 contract pass
- [ ] 4.2 Existing warm-up and cold-start integration tests pass unchanged
- [ ] 4.3 Architecture tests pass
- [ ] 4.4 Solution builds with style rules enforced

#### Manual

- [ ] 4.5 With LocalDB stopped, the 503 is unchanged and an Error with SQL number and route is logged

### Phase 5: Auth events and 429 logging

#### Automated

- [ ] 5.1 New integration tests for login outcomes, 429 rejection and stubbed-route logging pass
- [ ] 5.2 Existing lockout, rate-limit and auth contract tests pass unchanged
- [ ] 5.3 Architecture tests pass
- [ ] 5.4 Unit tests and solution build pass

#### Manual

- [ ] 5.5 Six wrong-password logins locally log failed then locked-out lines with no email or password
- [ ] 5.6 Audit findings covered by this change can be checked off against the new log output
