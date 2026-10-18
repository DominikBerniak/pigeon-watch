# Cold-Start Registration Resilience Implementation Plan

## Overview

Rollout Phase 1 of `context/foundation/test-plan.md` (Risk #1). A first registration that is slow, fails at the network level or is retried must end in exactly one usable account and an honest message, never a false "failed" for an account that was created. The plan bootstraps the API integration harness (reused by rollout Phases 2 and 3), pins the server-side repeat-register contract, and fixes the false failure with a scoped recovery login, driven by a frontend spec that composes the real retry chain.

## Current State Analysis

- The warm-up interceptor resends the same register POST after status `0` or `503` for up to 120 s. Nothing makes the resend idempotent. If the first attempt created the account and its response was lost, the resend gets the generic `RegistrationFailed` 400 and the form says "We couldn't create an account with these details." for an account that exists.
- After the 120 s budget the form shows the warm-up text and invites a resubmit; the resubmit's first attempt reaches the app and hits the same generic 400, so a manual resend is also a false failure.
- No test runs a register POST through the warm-up interceptor, and nothing in the backend drives `POST /account/register` through the HTTP pipeline.
- The API has no integration test project. `api-pr-checks.yml` builds the `.slnx` and runs the architecture and unit test projects by name.

## Desired End State

- `PigeonWatch/Api/IntegrationTests` exists, runs in PR checks and observes the real register pipeline on SQLite in-memory. A repeat identical register is observed as a 400 carrying only `RegistrationFailed`, with one `USER_ACCOUNT` row, the original password still logging in and a different password not.
- A register that ends without an answer, or is resent, or is resubmitted after an unanswered attempt, and then gets `RegistrationFailed`, is recovered by logging in with the typed credentials: the user lands signed in. A genuine duplicate costs no extra login request.
- When the warm-up ends with no answer at all, the register form shows a register-specific "we couldn't confirm whether your account was created" message, not the generic warm-up text.
- test-plan §6.2 and §6.3 describe how to add a slow/failing-HTTP frontend test and an API integration test; §3 and §5 agree on where the CI step lives; §7 records the SQL-Server-only gaps.

### Key Discoveries:

- Resend is blind and method-agnostic: `isWarmupError` = status `503` or `0` (`PigeonWatch/Frontend/src/app/core/warmup/warmup.interceptor.ts:83-85`); the retry loop is `:44-71`.
- `SessionService.register` is register-then-login with `AutoLoginError` wrapping (`PigeonWatch/Frontend/src/app/core/auth/session.service.ts:89-99`); `RegisterPage.handleFailure` maps `AutoLoginError` to the login page with the existing notice (`register-page.ts:183-188`).
- `registerFailure` sends status `0`/`503` to `warmupFailed` through `transportFailure` (`PigeonWatch/Frontend/src/app/shared/auth/auth-errors.ts:42-56`, `:104-110`); the same helper serves login, profile and password changes.
- `AUTH_RETRIED` is the existing pattern for an interceptor flag riding on the request's `HttpContext` (`http-context-tokens.ts:5`, `auth.interceptor.ts:21`, `:38`).
- `AuthApi.register` builds its own context with only `SKIP_AUTH` (`auth-api.ts:43-47`).
- The register endpoint is anonymous and rate limited at 10 requests per minute per remote IP (`PigeonWatch/Api/WebApi.Host/Program.cs:37-54`); `Program` is a public class, so `WebApplicationFactory<Program>` applies (`Program.cs:8-10`).
- Production registers the context with `UseSqlServer(..., EnableRetryOnFailure())` inside `AddDbContext`, reading a connection string that lives in user-secrets (`PigeonWatch/Api/DependencyInjection/DataServiceCollectionExtensions.cs:27-35`).
- `PigeonWatchUserStoreTests` already runs the real store on `SqliteConnection("DataSource=:memory:")` with `EnsureCreated`; the new project follows it.
- Non-enumeration is a hard rule (`PigeonWatch/CLAUDE.md`, "Hard rules"): no message may confirm that an email is registered. A recovery that only uses the credentials the submitter typed does not conflict with it.
- `lessons.md`: any command handed to the user in PowerShell must use `curl.exe`; no such command is needed in this plan.

## What We're NOT Doing

- No server-side idempotency key, and no change to the register endpoint, its error codes or response bodies.
- No message that says the email exists, and no automatic navigation that would imply it.
- No change to the interceptor's retry policy: same status set (`0`, `503`), same `Retry-After` handling, same 120 s budget, no method awareness.
- No browser e2e test and no real Azure cold start (rollout Phase 3 owns the one e2e flow).
- No server-side 503 integration test and no SQL Server container; the SQL-Server-only branches (duplicate-insert translation, the `SqlException` 503 mapping, execution-strategy commit ambiguity) are recorded as accepted gaps in test-plan §7.
- No handling of a `429` on a resend (the form still shows the too-many-requests message), and no handling of two browser tabs racing.
- No tests for email leaks, ownership, session invalidation, enumeration or lockout (rollout Phase 2); this harness only enables them.
- No cookbook entries beyond §6.2 and §6.3.
- The §2 row 3 hot-spot count discrepancy flagged in research is left for the next `--refresh`.
- No ESLint, no CI lint step.

## Implementation Approach

Backend first so the frontend scripts assert an observed fact, not an assumption: Phase 1 builds the harness and records what the real pipeline does on a repeat register. Phase 2 is test-first: the real-chain spec is written before the recovery code and must fail for the recovery scenarios, then the resend signal, the unanswered-email marker and the recovery login make it pass. Phase 3 adds the register-specific message and closes out the cookbook and the test-plan contradictions.

Recovery rule, fixed by the planning interview: recover only when the `RegistrationFailed` answer follows an inconclusive attempt: the register request was resent inside this call, or the submitted email equals the in-memory "unanswered email" left by an earlier register that ended with status `0`/`503` after the budget. Only the email is remembered, never the password. Recovery success is a normal success (signed in, navigate as usual). Recovery failure surfaces the original generic error, so nothing new is revealed.

Repository conventions that bind the new code: .NET test code follows the Api coding rules (explicit types, block bodies, blank lines before `if`/`return`, no comments) and the build enforces part of them through `EnforceCodeStyleInBuild`; TypeScript follows the Frontend layout rules; every UI text is a resx key; no code comments anywhere. The working copy is CRLF: convert files that are written with LF before finishing a phase.

## Phase 1: API integration harness and server contract

### Overview

Create the integration test project, a factory that runs the real pipeline on SQLite in-memory with the real rate limiter isolated per client, and the register contract tests. Wire the project into the solution and the PR workflow, and write cookbook §6.3.

### Changes Required:

#### 1. Spike the two inferred harness facts

**File**: scratch work inside the new project; not committed as a test

**Intent**: Settle the two points research could only infer before the factory design is frozen: whether every `TestServer` request lands in one limiter partition, and what must be removed so a SQLite `DbContext` replaces the SQL Server one without a "two providers" error. Also confirm the default environment, HTTPS redirection and ephemeral Data Protection keys do not change responses or break login within one factory.

**Contract**: Outcome recorded as two or three lines in the cookbook §6.3 entry written at the end of this phase. If the limiter inference is wrong, drop the per-client IP middleware and keep the rest.

#### 2. Integration test project

**File**: `PigeonWatch/Api/IntegrationTests/PigeonWatch.IntegrationTests.csproj`, `PigeonWatch/Api/PigeonWatchApi.slnx`

**Intent**: A new xunit.v3 test project mirroring `UnitTests` (same target framework, test SDK, xunit packages and EF Core Sqlite version) that references the host project and adds `Microsoft.AspNetCore.Mvc.Testing` at the current 10.0.x patch. It is added to the `.slnx` so the solution build compiles it.

**Contract**: Assembly `PigeonWatch.IntegrationTests`. References `PigeonWatch.WebApi.Host` and whatever the factory needs from `Data` to replace the context registration. It is not added to `PigeonWatchAssemblies`, so the architecture rules do not apply to it, but the shared `Directory.Build.props` style enforcement does. The first restore needs network (the Mvc.Testing package is not in the local NuGet cache).

#### 3. Factory and client isolation

**File**: `PigeonWatch/Api/IntegrationTests/` (factory and helper classes)

**Intent**: A `WebApplicationFactory<Program>` subclass that removes the production `PigeonWatchDbContext` registration (and its options configuration) and registers it on a held-open `SqliteConnection("DataSource=:memory:")` with `EnsureCreated`, so each factory instance owns a clean database. A test-only startup step sets the remote IP of each request from a header (default: a distinct address per test client), so the real `Auth` limiter stays in place and tests do not share its 10-per-minute partition. A small helper wraps register and login calls and reads the `USER_ACCOUNT` row count through the held-open connection with a raw query (entities are internal).

**Contract**: The limiter is never replaced or disabled. A test that wants to hit the limit simply reuses one client address. Each test class gets its own factory instance; no static shared state.

#### 4. Register contract tests

**File**: `PigeonWatch/Api/IntegrationTests/` (one test class for the register contract)

**Intent**: Pin the server half of the Risk #1 contract on the real pipeline. Cases:
- A new registration returns 200 and the account can log in (so the suite cannot pass on a server that fails everything).
- A second identical register returns 400 whose error codes are exactly `RegistrationFailed`, the body contains neither the email nor the display name, `USER_ACCOUNT` holds exactly one row, and the first password still logs in.
- A repeat with the same email but a different password returns 400, the original password still logs in and the new password gets 401 (the property that makes a recovery login safe).

**Contract**: Expected values come from the documented contract (the error code name and the non-enumeration rule), not from copying production constants. If the observed repeat response differs from `RegistrationFailed` alone (for example it also names the display name), stop before Phase 2 and adjust the Phase 2 trigger; the trigger keys on `RegistrationFailed` being present in the 400.

#### 5. CI step and project docs

**File**: `.github/workflows/api-pr-checks.yml`, `PigeonWatch/Api/CLAUDE.md`, `PigeonWatch/CLAUDE.md`

**Intent**: Add one `dotnet test` step for the new project after the unit tests in the existing job, using `--no-build` like its siblings. Document the third test project: the layout list, the commands block (including the `dotnet list ... --vulnerable` audit line for it) and the "What this owns" sentence that names the test projects.

**Contract**: No new workflow design; one step in the existing job. The `.slnx` entry makes the existing `dotnet build` compile the project first.

#### 6. Cookbook §6.3

**File**: `context/foundation/test-plan.md`

**Intent**: Replace the §6.3 placeholder with the working pattern: where the project lives, the factory and its database swap, the limiter isolation, how to read back state, the reference test name and the run command, plus the spike outcomes and the SQLite fidelity gaps.

**Contract**: Section 6.3 heading unchanged; strategy sections §1–§5 untouched in this phase.

### Success Criteria:

#### Automated Verification:

- Solution builds with style enforcement: `dotnet build PigeonWatch/Api/PigeonWatchApi.slnx`
- Integration tests pass: `dotnet test PigeonWatch/Api/IntegrationTests/PigeonWatch.IntegrationTests.csproj`
- Integration tests pass again on an immediate second run, inside the same minute: `dotnet test PigeonWatch/Api/IntegrationTests/PigeonWatch.IntegrationTests.csproj --no-build`
- Architecture tests still pass: `dotnet test PigeonWatch/Api/ArchitectureTests/PigeonWatch.ArchitectureTests.csproj --no-build`
- Unit tests still pass: `dotnet test PigeonWatch/Api/UnitTests/PigeonWatch.UnitTests.csproj --no-build`
- New project has no vulnerable packages: `dotnet list PigeonWatch/Api/IntegrationTests/PigeonWatch.IntegrationTests.csproj package --vulnerable --include-transitive`
- The workflow names the new project: `Select-String -Path .github/workflows/api-pr-checks.yml -Pattern "IntegrationTests"`

#### Manual Verification:

- Temporarily break the contract (for example make the duplicate mapping return success on a repeat) and confirm the repeat-register test fails, then revert.
- The first PR's checks show the new integration step running and green.
- A reader can follow the §6.3 entry and add a trivial extra test that runs without touching the factory.

**Implementation Note**: After completing this phase and all automated verification passes, pause here for manual confirmation from the human that the manual testing was successful before proceeding to the next phase. Phase blocks use plain bullets — the corresponding `- [ ]` checkboxes for these items live in the `## Progress` section at the bottom of the plan.

---

## Phase 2: Scoped recovery after an inconclusive register (frontend)

### Overview

Write the real-chain spec first, then give the register flow the ability to recognize an inconclusive attempt and recover with a login using the typed credentials.

### Changes Required:

#### 1. Real-chain spec (written first)

**File**: `PigeonWatch/Frontend/src/app/core/auth/` (new spec file for register under cold start)

**Intent**: Compose the real `authInterceptor`, `warmupInterceptor`, `AuthApi` and `SessionService.register` over `provideHttpClientTesting()` with fake timers, following `warmup.interceptor.spec.ts`. Script only the network; keep the retry real. Scenarios:
- First attempt answered with status `0`, resend answered with 400 `RegistrationFailed`, recovery login answered 200: the user ends authenticated, the resend body equals the original body, exactly one login request carries the typed credentials, no error reaches the caller.
- Same start, recovery login answered 401: the caller gets the original 400 `RegistrationFailed`, no further request is sent, the user is not authenticated.
- First attempt answered 503 with `Retry-After`, resend answered 200: normal register-then-login, a single login, no recovery.
- Warm-up budget exhausted on status `0`, then a second `register` for the same email whose first attempt answers 400 `RegistrationFailed`: recovery login runs and the user ends authenticated.
- An ordinary duplicate (first attempt answers 400 `RegistrationFailed`, no resend, nothing remembered): no login request is sent and the caller gets the 400.
- The unanswered email is compared trimmed and case-insensitively, and a different email's `RegistrationFailed` does not trigger recovery nor clear the remembered email.
- Recovery login that itself ends in status `0`/`503` after the budget propagates the transport error and keeps the email remembered.

**Contract**: The scenarios that exercise recovery fail before the implementation changes below and pass after. The 400 body is shaped as the Phase 1 harness observed it.

#### 2. Resend signal from the warm-up interceptor

**File**: `PigeonWatch/Frontend/src/app/core/http/http-context-tokens.ts`, `PigeonWatch/Frontend/src/app/core/warmup/warmup.interceptor.ts`, `PigeonWatch/Frontend/src/app/core/warmup/warmup.interceptor.spec.ts`

**Intent**: Let the caller learn that a request was resent. A new boolean context token (default `false`, same style as `AUTH_RETRIED`) is set on the request's context when the interceptor schedules its first resend. Add spec cases: set after a resend, untouched when the first attempt answers.

**Contract**: The interceptor stays generic and stateless across requests; retry timing, budget, status set and error propagation are unchanged, and the existing interceptor specs pass unmodified.

#### 3. Let `register` expose the signal

**File**: `PigeonWatch/Frontend/src/app/core/auth/auth-api.ts`

**Intent**: `AuthApi.register` accepts an optional `HttpContext` from its caller (still stamped with `SKIP_AUTH`), so `SessionService` can read the resend token after the call. The login, refresh and password calls are unchanged.

**Contract**: `register(request, context?)`; when omitted, behaviour is identical to today.

#### 4. Recovery in `SessionService.register`

**File**: `PigeonWatch/Frontend/src/app/core/auth/session.service.ts`

**Intent**: Keep register-then-login. Add the in-memory unanswered-email marker and the scoped recovery login described under Implementation Approach.

**Contract**:

| Register outcome | Condition | Result |
| --- | --- | --- |
| 200 | any | login as today; marker for this email cleared |
| 400 with `RegistrationFailed` | resend token set, or email equals marker | one recovery login with the typed credentials |
| 400 with `RegistrationFailed` | otherwise | original error, no extra request |
| status `0` or `503` after the budget | any | marker set to this email (normalized: trimmed, lowercased), error propagates |
| anything else | any | error propagates unchanged |

Recovery login outcomes: success is a normal sign-in and clears the marker; 401 clears the marker and rethrows the original 400; a transport failure keeps the marker and rethrows that failure; any other failure rethrows the original 400. A failed recovery never exposes lockout or credential detail. The marker holds one email, is replaced by a newer unanswered email, and is untouched by outcomes for other emails.

### Success Criteria:

#### Automated Verification:

- Before the implementation edits, the recovery scenarios of the new spec fail: `npm test -- --watch=false` from `PigeonWatch/Frontend`
- After the implementation, the whole frontend suite passes: `npm test -- --watch=false` from `PigeonWatch/Frontend`
- Production build stays within the bundle budget: `npm run build` from `PigeonWatch/Frontend`
- The pre-existing interceptor and session specs pass without edits to their existing cases.

#### Manual Verification:

- With the local API and `npm start` running, a normal registration lands signed in on the home page.
- Registering an already-used email shows the generic "couldn't create" message and the browser Network panel shows no `/auth/login` call after the register request.
- With the API running, a valid registration after a short API stop and restart (warm-up panel shown, then recovered) still ends with exactly one account and a signed-in user.

**Implementation Note**: After completing this phase and all automated verification passes, pause here for manual confirmation from the human that the manual testing was successful before proceeding to the next phase. Phase blocks use plain bullets — the corresponding `- [ ]` checkboxes for these items live in the `## Progress` section at the bottom of the plan.

---

## Phase 3: Honest unconfirmed message and close-out

### Overview

Replace the misleading "still waking up" text on the register form for the no-answer state, and bring the test plan and project docs in line with what shipped.

### Changes Required:

#### 1. Resource label

**File**: `PigeonWatch/Api/BusinessObjects/Resources/UiLabels.resx`

**Intent**: Add the key `auth.errors.registrationUnconfirmed` with the English text "We couldn't confirm whether your account was created. Submit again, or log in if you registered a moment ago." It is used only by the register form.

**Contract**: Key format `<area>.<group>.<name>`; no placeholders; generated bundles are rebuilt by the existing `pretest`/`prebuild` scripts.

#### 2. Register-only form error

**File**: `PigeonWatch/Frontend/src/app/shared/auth/auth-errors.ts`

**Intent**: Add `registrationUnconfirmed` to `AuthFormError`. `registerFailure` returns it for a transport failure of status `0` or `503` (including a recovery login that ended unanswered); `429` still maps to `tooManyRequests` and everything else is unchanged. `authErrorMessage` resolves it to the new key. `loginFailure`, `profileFailure` and `passwordChangeFailure` keep `warmupFailed`.

**Contract**: `registerFailure(error).formError === 'registrationUnconfirmed'` for status `0` and `503`; no other function's output changes.

#### 3. Specs for the mapping and the page

**File**: `PigeonWatch/Frontend/src/app/shared/auth/auth-errors.spec.ts`, `PigeonWatch/Frontend/src/app/features/auth/register-page/register-page.spec.ts`

**Intent**: Cover the register mapping that no spec covers today (`RegistrationFailed`, `DuplicateDisplayName`, `0`, `503`, `429`) and assert login still maps `0`/`503` to `warmupFailed`. In the page spec, a rejected register with status `503` shows the new text, keeps the typed values and leaves the form submittable again.

**Contract**: Mapping assertions use the status and codes, not the English text; the page case reads the text through the resource service the way the neighbouring cases do.

#### 4. Test plan and project docs

**File**: `context/foundation/test-plan.md`, `PigeonWatch/Frontend/CLAUDE.md`, `PigeonWatch/CLAUDE.md`

**Intent**: In the test plan, fill §6.2 (slow or failing HTTP: real retry chain, script the network, reference spec and run command), append a short §6.7 note, add the accepted gaps to §7 (SQL-Server-only duplicate-insert translation, `SqlException` 503 mapping, retry commit ambiguity, `429` on resend, two-tab race) and resolve the §3 vs §5 contradiction by rewording the Phase 5 goal so it no longer claims to wire the API integration suite, which Phase 1 wired. In the frontend CLAUDE.md add the resend token, the recovery rule and the register-only message; in the root PigeonWatch CLAUDE.md note under the non-enumeration rule that a recovery login with the typed credentials is allowed and reveals nothing.

**Contract**: §3 Status for row 1 is advanced by the rollout orchestrator, not hand-edited here; §1–§2 and §4 are untouched; §8 gets one line recording the §3/§5 edit.

### Success Criteria:

#### Automated Verification:

- The label exists: `Select-String -Path PigeonWatch/Api/BusinessObjects/Resources/UiLabels.resx -Pattern "auth.errors.registrationUnconfirmed"`
- Frontend suite passes: `npm test -- --watch=false` from `PigeonWatch/Frontend`
- Production build stays within the budget: `npm run build` from `PigeonWatch/Frontend`
- The API still builds with the new label embedded: `dotnet build PigeonWatch/Api/PigeonWatchApi.slnx`
- §6.2 and §6.3 are no longer placeholders: `Select-String -Path context/foundation/test-plan.md -Pattern "TBD — see §3 Phase 1"` returns no match

#### Manual Verification:

- With the API stopped and `npm start` running, submit a valid registration; after the warm-up panel ends (about 2 minutes) the form shows the unconfirmed text and keeps the typed values.
- Start the API and submit the same form again: the account is created and the user lands signed in.
- The login page still shows the original warm-up text when the API is stopped.

**Implementation Note**: After completing this phase and all automated verification passes, pause here for manual confirmation from the human that the manual testing was successful before proceeding to the next phase. Phase blocks use plain bullets — the corresponding `- [ ]` checkboxes for these items live in the `## Progress` section at the bottom of the plan.

---

## Testing Strategy

### Unit Tests:

- Warm-up interceptor: resend token set on a resend, untouched on first-attempt success; existing cases unchanged.
- `registerFailure` and `authErrorMessage`: register mapping including the new unconfirmed error; login mapping unchanged.
- Register page: 503 shows the unconfirmed text, values kept, form resubmittable.

### Integration Tests:

- Real-chain frontend spec (real interceptors, `AuthApi`, `SessionService.register`, fake network and timers): the seven scenarios in Phase 2.
- API integration on the real pipeline with SQLite in-memory: new registration, identical repeat, same email with a different password.

### Manual Testing Steps:

1. Normal registration against the local API lands signed in.
2. Registering a used email shows the generic message with no `/auth/login` call.
3. With the API stopped, the register form ends in the unconfirmed text after about 2 minutes with values kept; restart the API and resubmit to land signed in.
4. The login page still shows the original warm-up text.

## Performance Considerations

The integration suite is a handful of in-process requests on SQLite and adds seconds to the PR job. The recovery adds at most one login request, only after an inconclusive register. Resends already count against the per-IP limit; the recovery login is one more request in that window.

## Migration Notes

None. No schema, API contract or stored data changes. The remembered email lives only in memory and is gone on reload.

## References

- Related research: `context/changes/testing-cold-start-registration-resilience/research.md`
- Quality contract: `context/foundation/test-plan.md` (§2 Risk #1, §3 Phase 1, §5, §6.2, §6.3, §7)
- Cold-start decision: `context/archive/2026-10-04-register-and-login/plan.md`
- Similar interceptor flag: `PigeonWatch/Frontend/src/app/core/auth/auth.interceptor.ts:21`, `:38`
- Existing SQLite harness pattern: `PigeonWatch/Api/UnitTests/` (`PigeonWatchUserStoreTests`)
- Existing interceptor spec style: `PigeonWatch/Frontend/src/app/core/warmup/warmup.interceptor.spec.ts`

## Progress

> Convention: `- [ ]` pending, `- [x]` done. Append ` — <commit sha>` when a step lands. Do not rename step titles. See `references/progress-format.md`.

### Phase 1: API integration harness and server contract

#### Automated

- [x] 1.1 Solution builds with style enforcement: `dotnet build PigeonWatch/Api/PigeonWatchApi.slnx`
- [x] 1.2 Integration tests pass: `dotnet test PigeonWatch/Api/IntegrationTests/PigeonWatch.IntegrationTests.csproj`
- [x] 1.3 Integration tests pass again on an immediate second run, inside the same minute: `dotnet test PigeonWatch/Api/IntegrationTests/PigeonWatch.IntegrationTests.csproj --no-build`
- [x] 1.4 Architecture tests still pass: `dotnet test PigeonWatch/Api/ArchitectureTests/PigeonWatch.ArchitectureTests.csproj --no-build`
- [x] 1.5 Unit tests still pass: `dotnet test PigeonWatch/Api/UnitTests/PigeonWatch.UnitTests.csproj --no-build`
- [x] 1.6 New project has no vulnerable packages: `dotnet list PigeonWatch/Api/IntegrationTests/PigeonWatch.IntegrationTests.csproj package --vulnerable --include-transitive`
- [x] 1.7 The workflow names the new project: `Select-String -Path .github/workflows/api-pr-checks.yml -Pattern "IntegrationTests"`

#### Manual

- [x] 1.8 Temporarily break the contract (for example make the duplicate mapping return success on a repeat) and confirm the repeat-register test fails, then revert.
- [ ] 1.9 The first PR's checks show the new integration step running and green.
- [ ] 1.10 A reader can follow the §6.3 entry and add a trivial extra test that runs without touching the factory.

### Phase 2: Scoped recovery after an inconclusive register (frontend)

#### Automated

- [ ] 2.1 Before the implementation edits, the recovery scenarios of the new spec fail: `npm test -- --watch=false` from `PigeonWatch/Frontend`
- [ ] 2.2 After the implementation, the whole frontend suite passes: `npm test -- --watch=false` from `PigeonWatch/Frontend`
- [ ] 2.3 Production build stays within the bundle budget: `npm run build` from `PigeonWatch/Frontend`
- [ ] 2.4 The pre-existing interceptor and session specs pass without edits to their existing cases.

#### Manual

- [ ] 2.5 With the local API and `npm start` running, a normal registration lands signed in on the home page.
- [ ] 2.6 Registering an already-used email shows the generic "couldn't create" message and the browser Network panel shows no `/auth/login` call after the register request.
- [ ] 2.7 With the API running, a valid registration after a short API stop and restart (warm-up panel shown, then recovered) still ends with exactly one account and a signed-in user.

### Phase 3: Honest unconfirmed message and close-out

#### Automated

- [ ] 3.1 The label exists: `Select-String -Path PigeonWatch/Api/BusinessObjects/Resources/UiLabels.resx -Pattern "auth.errors.registrationUnconfirmed"`
- [ ] 3.2 Frontend suite passes: `npm test -- --watch=false` from `PigeonWatch/Frontend`
- [ ] 3.3 Production build stays within the budget: `npm run build` from `PigeonWatch/Frontend`
- [ ] 3.4 The API still builds with the new label embedded: `dotnet build PigeonWatch/Api/PigeonWatchApi.slnx`
- [ ] 3.5 §6.2 and §6.3 are no longer placeholders: `Select-String -Path context/foundation/test-plan.md -Pattern "TBD — see §3 Phase 1"` returns no match

#### Manual

- [ ] 3.6 With the API stopped and `npm start` running, submit a valid registration; after the warm-up panel ends (about 2 minutes) the form shows the unconfirmed text and keeps the typed values.
- [ ] 3.7 Start the API and submit the same form again: the account is created and the user lands signed in.
- [ ] 3.8 The login page still shows the original warm-up text when the API is stopped.
