# API Privacy and Authorization Contract Tests Implementation Plan

## Overview

Rollout Phase 2 of `context/foundation/test-plan.md`. Build a contract suite on the real HTTP pipeline (the Phase 1 `WebApplicationFactory<Program>` harness) that proves, for Risks #2, #3 and #6: no response from any user-reachable endpoint carries another user's email; user B cannot read or change user A's account data and the token lifecycle after a password change is exactly what the design says; and registration and login failures do not reveal which emails exist while the limiter and lockout demonstrably fire. One small product fix ships with it: on register, a taken display name returns the generic `RegistrationFailed` (decided in planning), which removes the display-name enumeration oracle found in research.

## Current State Analysis

- The integration harness exists (`PigeonWatchApiFactory`, `ClientAddressStartupFilter`, `AccountApiClient`, three register tests) but has no token handling, no bearer client, no endpoint enumeration and no controllable clock. No integration test calls an authenticated endpoint.
- Privacy today is structural (response models carry no user-table field except display name) and verified only by one repeat-register body check and one reflection test on `SightingModel`. Nothing would notice a new endpoint that returns an email.
- Ownership is enforced by claim-scoped queries read inline in each action; no sighting update or delete endpoint exists, so the "modify" half of Risk #3 is account-only until S-09 to S-11.
- There is no server-side logout (decision D1). After a password change the old access token should keep working until its own expiry and the old refresh token should be rejected; neither is observed by any test.
- `DuplicateDisplayName` versus `RegistrationFailed` is an enumeration oracle: with a taken display name, an existing email answers `RegistrationFailed` and a new email answers `DuplicateDisplayName`, and no account is created either way.
- Six framework behaviours are unverified (research.md Open Questions); the plan resolves them in Phase 1 with throwaway probes.

## Desired End State

A contract suite in `PigeonWatch/Api/IntegrationTests` that fails loudly when:
- an enumerated endpoint (including the 404-stubbed Identity routes) returns another user's email in a decoded body, header or cookie, or has no fixture or exemption;
- an endpoint is neither `[Authorize]`-protected nor on an explicit anonymous list;
- B can affect A's account, or the password-change token lifecycle drifts (old refresh 401, old access 200 until expiry then 401);
- an existing-email register variant is distinguishable from a taken-display-name register, an unknown email is distinguishable from a wrong password on login, lockout stops working across addresses, or the 11th request from one address stops getting 429.

`test-plan.md` §6.4 (and §6.3 where the harness grew) tells a newcomer how to add a test for a new endpoint. Verify by `dotnet test` on the three API test projects, `npm test -- --watch=false`, and the manual checks in each phase.

### Key Discoveries:

- Un-exposed Identity routes are stubbed, not removed, and hidden from `ApiExplorer`; only `EndpointDataSource` shows them (`PigeonWatch/Api/DependencyInjection/PigeonWatchEndpointRouteBuilderExtensions.cs:14-43`).
- Per-IP limiter is 10 per minute, shared by register, login and password change; `auth/refresh` is exempt; middleware order is authentication, limiter, authorization (`PigeonWatch/Api/WebApi.Host/Program.cs:37-54,72-76`).
- Lockout is 5 attempts for 5 minutes (`PigeonWatch/Api/DependencyInjection/AccountServiceCollectionExtensions.cs:20-22`); there is no bearer, stamp-validator or authorization option, so bearer lifetimes are framework defaults.
- The register error collapse lives in one place: `AccountRepository.ToAccountErrors` (`PigeonWatch/Api/Data/Repositories/AccountRepository.cs:77-101`); the same method serves rename and password change.
- The store raises `DuplicateDisplayName` on create and on update (`PigeonWatch/Api/Data/Identity/PigeonWatchUserStore.cs:26-31,54-64`).
- The SPA maps `DuplicateDisplayName` to a field message on both register and profile (`PigeonWatch/Frontend/src/app/shared/auth/auth-errors.ts:123-142`).
- Controllers take the acting user from `ClaimTypes.NameIdentifier` (`PigeonWatch/Api/WebApi/Controllers/AccountController.cs:41,60`), so the real pipeline exercises the claim path; there is no current-user provider to mock for ownership.
- The harness gives each client a distinct address (`PigeonWatch/Api/IntegrationTests/PigeonWatchApiFactory.cs:25-32`); a sweep that fires more than 10 `auth`-group requests must use a fresh address per request.
- Existing tests to extend or replace: `RegisterContractTests.cs:35-67`, `AccountRepositoryTests.cs:83-92` (asserts `DuplicateDisplayName` beside email errors on create, which stops being true).

## What We're NOT Doing

- No server-side logout or token revocation, and no change to the 1 h / 14 d bearer lifetimes; D1 stays an accepted decision.
- No sighting update or delete ownership tests (S-09 to S-11 do not exist); the coverage guard forces fixtures the day those endpoints land.
- No contact-detail rule on sighting name, description or behaviour (decision: no rule; a user pasting their own contact into free text is self-disclosure, recorded in §7).
- No timing assertions and no spoof-resistance (forwarded headers) assertion; the harness sets the remote address directly and cannot observe either.
- No removal of the accepted `LockedOut` login detail, no dummy-hash equalisation, and no fix for the victim-lockout consequence (recorded in §7).
- No SQL-Server-only paths (duplicate-key log text, `SqlException` mapping) and no deleted-user token path (no delete endpoint).
- No change to the rename error code: rename keeps `DuplicateDisplayName` (caller is authenticated and display names are public).
- No new CI YAML: the `Integration tests` step of `api-pr-checks.yml` already runs this project.
- The register success (200) versus failure (400) split is inherent without email confirmation; it is documented, not treated as a defect.
- No AI-native layer: deterministic decoded-text assertions fully cover these risks (cost × signal; checked: 2026-10-18). When NOT to use one: any assertion a decoded-text search or a status comparison already makes.

## Implementation Approach

Order is by cost × signal and risk priority. Phase 1 buys certainty about six framework behaviours cheaply before any test depends on them and builds the shared seams. Phase 2 (Risk #2, high impact) and Phases 3 and 4 (Risk #3, high impact) follow; Phase 5 is the only production change and is isolated and test-first; Phase 6 adds the Risk #6 behaviour tests on top of the fix; Phase 7 writes the cookbook.

Rules that apply to every phase:
- One `PigeonWatchApiFactory` per test (existing rule); real bearer tokens obtained through register and login, never a hand-built `ClaimsPrincipal`.
- Expected values come from the documented contract (PRD, `Api/CLAUDE.md`, `PigeonWatch/CLAUDE.md`, the research's corrected assertion set), never from production constants or option values. Numbers such as 10 requests per minute are asserted as observed behaviour at the boundary, once.
- Rate-limit budget: a client address never sends more than 10 `auth`-group requests in one test unless the test is the limiter test. Setup (register and login) uses a throwaway address; the user's own client gets a fresh one.
- Repo code rules apply to tests too: no `var`, block-bodied methods, primary constructors, blank line before `if` and `return`, camelCase private fields, and no comments in code.
- Files written on Windows come out LF; convert every touched file to CRLF before finishing a phase (`git ls-files --eol` must show no `w/lf`).

## Phase 1: Probes and harness seams

### Overview

Resolve the six framework questions with throwaway probes, record the answers in `research.md`, and add the shared harness seams the later phases need. Nothing in this phase asserts product behaviour permanently; the probe class is deleted before the phase closes.

### Changes Required:

#### 1. Harness helpers

**File**: `PigeonWatch/Api/IntegrationTests/` (new helpers beside `AccountApiClient.cs`; `PigeonWatchApiFactory.cs` extended)

**Intent**: Let a test get a signed-in user in one call, send any request from a fresh client address, create a sighting, read the audit columns, and (once the clock decision lands) control time, so Phases 2 to 6 stay short.

**Contract**:
- A `TestUser` value (email, password, display name, user id, access token, refresh token, an `HttpClient` with its own address and the bearer header). Created through the real register and login endpoints; setup uses a throwaway address so the user's own client starts with a full limiter budget. The user id is read from `GET configuration/general`.
- A factory method that sends an `HttpRequestMessage` from a fresh address with an optional bearer token (the sweep's way around the shared 10-per-minute bucket).
- A sighting helper that posts a valid `CreateSightingRequestModel` body (status 1 or 2, criticality 1 to 3, in-range coordinates) for a user and returns the new id.
- A factory method that reads `CREATE_USER` and `UPDATE_USER` from `USER_ACCOUNT` and `SIGHTING` over the held-open SQLite connection (same pattern as `CountUserAccounts`).
- A small `ManualTimeProvider` inside the integration project (the UnitTests one is not referenced), with `SetUtcNow` and `Advance`, wired in only the way the clock probe selects.

#### 2. Throwaway probes

**File**: `PigeonWatch/Api/IntegrationTests/ProbeScratch.cs` (deleted before the phase closes)

**Intent**: Observe each framework behaviour once and write the observed status and body shape into `research.md`.

**Contract** (one probe per open question, each with its decision rule):
- **FW1 clock**: log in, then try in order (a) replace the DI `TimeProvider` with the manual one, (b) `PostConfigure<BearerTokenOptions>` for the bearer scheme setting its `TimeProvider` to the manual one, (c) `PostConfigure<BearerTokenOptions>` with a one-second `BearerTokenExpiration` and a short real delay. Advance past one hour (a, b) or wait (c) and call an authorized read. First route that returns 200 before and 401 after wins; the decision rule if (a) and (b) both fail is (c) with a delay of at most two seconds; if all three fail, the Phase 4 expiry assertion is dropped and recorded in §7.
- **FW2 password change**: sign in, change the password, then call an authorized read with the old access token and `POST auth/refresh` with the old refresh token. Record both statuses.
- **FW3 display-name oracle**: register A with display name D; then register a new email with D, an existing email with a fresh name, and the existing email with D. Record each error-code list; this confirms the oracle exists before the fix.
- **FW4 stubs and error shapes**: call every `auth/*` stub route anonymously and signed in; record the status and body of each; record the status, headers and body of a 429; try to force a 500 (for example by closing the held-open connection before an authorized sighting read) and record its body shape. If a 500 cannot be forced cheaply, record that and exclude the error-path case from Phase 2.
- **FW5 lockout**: register one account; send five wrong logins from five addresses and record each status and detail (does the fifth already say `LockedOut`); then send the correct password. Inspect `LOCKOUT_END` on the held-open connection and try moving it into the past with raw SQL, then log in again.
- **FW6 case variants**: register `User@Example.com`, then register `user@example.com` and ` USER@EXAMPLE.COM ` with new display names. Record the error-code lists.

#### 3. Probe record

**File**: `context/changes/testing-api-privacy-authorization-contract/research.md`

**Intent**: Make the six answers durable so later phases cite them instead of re-deriving them.

**Contract**: Append a `## Probe Outcomes` section with `### FW1` to `### FW6` entries, each stating the observed result and the decision it drives; flip the frontmatter `status` from `partial` to `complete` and bump `last_updated`.

### Success Criteria:

#### Automated Verification:

- Solution builds with the new harness helpers: `dotnet build PigeonWatch/Api/PigeonWatchApi.slnx`
- Existing integration tests still pass: `dotnet test PigeonWatch/Api/IntegrationTests/PigeonWatch.IntegrationTests.csproj`
- research.md records an entry for each of the six framework questions: `grep -c "^### FW[1-6]" context/changes/testing-api-privacy-authorization-contract/research.md` prints 6
- The probe class is gone: `test ! -e PigeonWatch/Api/IntegrationTests/ProbeScratch.cs`

#### Manual Verification:

- The clock decision (which of the four branches, and what the Phase 4 expiry test will use) is read and agreed before Phase 4 depends on it

**Implementation Note**: After completing this phase and all automated verification passes, pause here for manual confirmation from the human that the manual testing was successful before proceeding to the next phase.

---

## Phase 2: Risk #2 leak sweep

### Overview

One sweep that enumerates every routed endpoint, calls each as an anonymous caller, as B and as A, and fails if another user's email appears anywhere in a decoded response, plus a coverage guard so a new endpoint cannot be skipped.

**Test contract**
- **Behavior asserted**: for every caller, no response body, header or cookie contains the email (as registered, upper-cased, URL-encoded or JSON-decoded) of any user other than the caller; anonymous callers see neither user's email. The 404-stubbed Identity routes behave as stubs (no body).
- **Regression caught**: a new endpoint or response field that exposes an email or username (username equals email), a regression of the stub convention that would re-expose Identity `manage/info`, an error body that echoes another user's email, or an audit column that starts holding an email.
- **Research source**: research.md Risk #2 (endpoint inventory, stubbed routes, username equals email, error bodies, audit columns, Challenges 1 and 2), plus FW4.
- **Edge / error / boundary case**: A's email carries a `+` tag and mixed case (a raw-substring search misses the JSON-escaped `+`); malformed JSON, empty bodies, out-of-range coordinates, an unknown culture and an unknown sighting id exercise error bodies; a request that submits A's email as B (register) must not echo it; the sweep fails on any 429 (isolation broke) or unexpected 5xx (the check would be vacuous).
- **Anti-pattern avoided**: asserting only the fields the code currently returns, and reflection-only checks. The sweep searches decoded text, not a field whitelist, and the scanner proves itself with a positive control (A's own `configuration/general` response must be found to contain A's email through the same path).

### Changes Required:

#### 1. Endpoint catalog and coverage guard

**File**: `PigeonWatch/Api/IntegrationTests/` (new catalog, fixture registry and guard tests)

**Intent**: Enumerate what the app actually routes and force every route to have a fixture or a justified exemption.

**Contract**: Enumerate `EndpointDataSource.Endpoints.OfType<RouteEndpoint>()` from the factory's services (not `ApiExplorer`), yielding method, route pattern and whether authorization metadata is present. A hand-written fixture registry is keyed by method plus pattern and builds a valid request given the seeded users and A's sighting id (route parameters substituted: A's sighting id, `en` for culture; `nearby` gets in-range latitude and longitude). Stubbed `auth/*` routes are covered by one group fixture. Exemptions carry a reason string. Two guard tests: every enumerated route has a fixture or an exemption, and every fixture or exemption matches an enumerated route (stale entries fail).

#### 2. Response leak scanner

**File**: `PigeonWatch/Api/IntegrationTests/` (new scanner and its self-tests)

**Intent**: Turn a response into a searchable haystack that cannot be defeated by JSON escaping.

**Contract**: The haystack is the status line, every header value, `Set-Cookie`, the raw body, and every decoded JSON property name and string value (nested). Needles for a user are the email as registered, its upper-case form, its URL-encoded form and the username (same as email); search is ordinal-ignore-case. Self-tests feed fabricated responses carrying the needle raw, JSON-escaped, in a header and in `Set-Cookie` and expect a hit, and a clean response and expect none.

#### 3. Sweep and audit tests

**File**: `PigeonWatch/Api/IntegrationTests/PrivacySweepTests.cs`

**Intent**: Run the sweep per caller and check the persisted audit columns.

**Contract**: Seed A (`Alice.Tester+pw@Example.com`) and B (`bob.tester@example.net`), sign both in, give A one sighting. Three facts, one per caller (anonymous, B, A); each sends every fixture request and its malformed variants from a fresh address, then scans with the needles of every other user. A positive-control test, a stub-route test (authenticated stub routes return 404 with an empty body, anonymous ones return the status FW4 recorded) and an audit-column test (after register, login, rename, sighting and password change, `CREATE_USER` and `UPDATE_USER` on both tables hold a user id or `SYSTEM`, never a needle). If FW4 showed a 500 can be forced, one error-path fixture is added (read a sighting after closing the held-open connection) and its body is scanned too.

### Success Criteria:

#### Automated Verification:

- Scanner self-tests find the seeded email raw, JSON-escaped, in a header and in Set-Cookie: `dotnet test PigeonWatch/Api/IntegrationTests/PigeonWatch.IntegrationTests.csproj --filter "FullyQualifiedName~ResponseLeakScanner"`
- Positive control finds A's own email in A's configuration response: `dotnet test PigeonWatch/Api/IntegrationTests/PigeonWatch.IntegrationTests.csproj --filter "FullyQualifiedName~Scanner_finds_the_own_email"`
- Coverage guard passes (every route has a fixture or exemption, no stale entries): `dotnet test PigeonWatch/Api/IntegrationTests/PigeonWatch.IntegrationTests.csproj --filter "FullyQualifiedName~Every_"`
- Sweep as anonymous, as B and as A finds no other user's email and no 429 or unexpected 5xx: `dotnet test PigeonWatch/Api/IntegrationTests/PigeonWatch.IntegrationTests.csproj --filter "FullyQualifiedName~Sweep_as_"`
- Stubbed Identity routes behave as stubs with no body: `dotnet test PigeonWatch/Api/IntegrationTests/PigeonWatch.IntegrationTests.csproj --filter "FullyQualifiedName~Stubbed_identity_routes"`
- Audit columns hold user ids or SYSTEM, never an email: `dotnet test PigeonWatch/Api/IntegrationTests/PigeonWatch.IntegrationTests.csproj --filter "FullyQualifiedName~Audit_columns"`
- Full integration suite passes: `dotnet test PigeonWatch/Api/IntegrationTests/PigeonWatch.IntegrationTests.csproj`

#### Manual Verification:

- Mutation check one: temporarily add the reporter's email to the sighting response; the sweep fails on `nearby`; revert and confirm `git diff` shows no production change
- Mutation check two: temporarily delete one fixture; the coverage guard fails; restore it

**Implementation Note**: After completing this phase and all automated verification passes, pause here for manual confirmation from the human that the manual testing was successful before proceeding to the next phase.

---

## Phase 3: Risk #3 ownership and endpoint protection

### Overview

Prove through real bearer tokens that B cannot read or change A's account data or sightings, that the open reads are open by design, and that every endpoint is either authorize-protected or on an explicit anonymous list.

**Test contract**
- **Behavior asserted**: B's `mine` never lists A's sightings and vice versa; B's profile rename and password change affect only B (A's display name, email and password are unchanged and A still logs in with the old password); B supplying A's current password to the password endpoint fails (`PasswordMismatch`); B's sighting is attributed to B even when the body carries a forged reporter id, display name, email or user id; B can read A's sighting by id and sees it in `nearby`, with a display name and no email (the by-design flat permission set, PRD Access Control, `context/foundation/prd.md:160`); every endpoint without authorization metadata is on the anonymous list and anonymous calls to every protected endpoint get 401.
- **Regression caught**: an endpoint that starts taking a user id from route, query or body; `mine` losing its claim scope; a new controller or action shipped without `[Authorize]`; mass-assignment of ownership fields.
- **Research source**: research.md Risk #3 ("What a B versus A test can honestly assert today", per-record scoping, "authenticated means authorized" challenge).
- **Edge / error / boundary case**: unknown JSON properties in the profile and sighting bodies; B renaming to A's display name (400, A unchanged); the cooldown of one sighting per 60 s per user is satisfied because each user reports once; an anonymous request to a limiter-guarded protected route uses a fresh address so 401, not 429, is observed.
- **Anti-pattern avoided**: mocking the current-user provider or the claims path. Tokens come from real login, so ownership can fail for real; the by-design open reads are documented as such and not forbidden.

### Changes Required:

#### 1. Ownership contract tests

**File**: `PigeonWatch/Api/IntegrationTests/OwnershipContractTests.cs`

**Intent**: Two users on one factory exercising every claim-scoped surface that exists today.

**Contract**: `TestUser` A and B from the Phase 1 helper. Facts for `mine` isolation, profile rename and password change scoped to the caller, the forged-body attribution and mass-assignment case, the `PasswordMismatch` cross-supply case, and the open reads (by id and `nearby` return A's sighting to B with display name only, anonymous gets 401). Each test reads A's state back through A's own client (`configuration/general`, a fresh login) rather than asserting on B's response alone.

#### 2. Endpoint protection guard

**File**: `PigeonWatch/Api/IntegrationTests/EndpointProtectionTests.cs`

**Intent**: A new endpoint cannot be public by accident.

**Contract**: Using the Phase 2 catalog: every endpoint without authorization metadata (or with `AllowAnonymous`) must appear in an explicit anonymous list with a reason (register, login, refresh, `configuration/client`, `resources/{culture}`, and the stubbed `auth/*` routes, which are stubbed rather than protected); every entry in the list must exist. Anonymous calls built from the Phase 2 fixtures to each protected route return 401 and no body containing a needle.

### Success Criteria:

#### Automated Verification:

- `mine` lists only the caller's sightings for A and for B: `dotnet test PigeonWatch/Api/IntegrationTests/PigeonWatch.IntegrationTests.csproj --filter "FullyQualifiedName~Mine_lists_only"`
- B's profile rename and password change leave A's account and password unchanged: `dotnet test PigeonWatch/Api/IntegrationTests/PigeonWatch.IntegrationTests.csproj --filter "FullyQualifiedName~Account_changes_by"`
- B cannot change A's password by supplying A's current password: `dotnet test PigeonWatch/Api/IntegrationTests/PigeonWatch.IntegrationTests.csproj --filter "FullyQualifiedName~Password_change_with_another"`
- B's sighting is attributed to B despite forged ownership fields in the body: `dotnet test PigeonWatch/Api/IntegrationTests/PigeonWatch.IntegrationTests.csproj --filter "FullyQualifiedName~Sighting_is_attributed"`
- B reads A's sighting by id and in nearby with a display name and no email, anonymous gets 401: `dotnet test PigeonWatch/Api/IntegrationTests/PigeonWatch.IntegrationTests.csproj --filter "FullyQualifiedName~Open_reads"`
- Every endpoint is authorize-protected or on the anonymous list, with no stale list entries: `dotnet test PigeonWatch/Api/IntegrationTests/PigeonWatch.IntegrationTests.csproj --filter "FullyQualifiedName~Anonymous_list"`
- Anonymous calls to every protected endpoint get 401: `dotnet test PigeonWatch/Api/IntegrationTests/PigeonWatch.IntegrationTests.csproj --filter "FullyQualifiedName~Anonymous_calls"`
- Full integration suite passes: `dotnet test PigeonWatch/Api/IntegrationTests/PigeonWatch.IntegrationTests.csproj`

#### Manual Verification:

- Mutation check: temporarily remove `[Authorize]` from one sighting action; the protection guard fails; restore it and confirm `git diff` shows no production change

**Implementation Note**: After completing this phase and all automated verification passes, pause here for manual confirmation from the human that the manual testing was successful before proceeding to the next phase.

---

## Phase 4: Risk #3 token lifecycle after a password change

### Overview

Pin the session lifecycle as it is: a password change rejects the old refresh token at its next use, leaves the old access token alive until its own expiry, and that access token is rejected after the clock passes the lifetime. The logout clause is dropped (decision D1).

**Test contract**
- **Behavior asserted**: after a password change the old refresh token gets 401 and the new password logs in; the old access token still returns 200 on an authorized read immediately after the change; after the clock passes the access lifetime the old access token returns 401. Controls: a refresh token with no intervening change returns working tokens, and a rename does not end a session (the refresh token issued before a rename still works), so the 401 above is caused by the password change, not by refresh in general.
- **Regression caught**: a security-stamp or refresh-validation change that lets a stolen refresh token survive a password change; an access token lifetime that silently becomes unlimited; a rename starting to rotate the stamp.
- **Research source**: research.md Risk #3 token lifecycle (corrected assertion set i to iii), the D1 correction, FW1 and FW2 outcomes.
- **Edge / error / boundary case**: the instant after the change (access 200, refresh 401) versus after expiry (access 401); the old password fails to log in; the clock-passing mechanism is whichever FW1 selected, with the fallback to a one-to-two-second real delay named in Phase 1.
- **Anti-pattern avoided**: asserting that logout invalidates tokens (no server logout exists), and mocking the claims path. The test uses real tokens; time is controlled through the seam FW1 chose, not by faking the bearer handler.

### Changes Required:

#### 1. Token lifecycle contract tests

**File**: `PigeonWatch/Api/IntegrationTests/TokenLifecycleContractTests.cs`

**Intent**: One user, several tokens, one password change, observed from the outside.

**Contract**: Uses `TestUser` plus the clock seam from Phase 1 (factory configured with the manual clock the probe selected). Tests: refresh control; rename-keeps-session control; password change then old refresh 401 and new-password login 200 and old-password login 401; old access 200 right after the change; old access 401 after the clock passes the lifetime. Test names state the accepted behaviour ("old access token survives until expiry") rather than implying revocation.

### Success Criteria:

#### Automated Verification:

- A refresh with an unrevoked refresh token returns working tokens: `dotnet test PigeonWatch/Api/IntegrationTests/PigeonWatch.IntegrationTests.csproj --filter "FullyQualifiedName~Refresh_without_a_password_change"`
- A rename does not end other sessions: `dotnet test PigeonWatch/Api/IntegrationTests/PigeonWatch.IntegrationTests.csproj --filter "FullyQualifiedName~Rename_keeps"`
- After a password change the old refresh token gets 401 and the new password logs in: `dotnet test PigeonWatch/Api/IntegrationTests/PigeonWatch.IntegrationTests.csproj --filter "FullyQualifiedName~Password_change_rejects_the_old_refresh_token"`
- After a password change the old access token still gets 200: `dotnet test PigeonWatch/Api/IntegrationTests/PigeonWatch.IntegrationTests.csproj --filter "FullyQualifiedName~Old_access_token_survives"`
- After the clock passes the access lifetime the old access token gets 401: `dotnet test PigeonWatch/Api/IntegrationTests/PigeonWatch.IntegrationTests.csproj --filter "FullyQualifiedName~Old_access_token_is_rejected_after_expiry"`
- The lifecycle tests pass three runs in a row: `for i in 1 2 3; do dotnet test PigeonWatch/Api/IntegrationTests/PigeonWatch.IntegrationTests.csproj --filter "FullyQualifiedName~TokenLifecycleContractTests" || break; done`

#### Manual Verification:

- Test names and the Phase 7 cookbook wording present "old access token survives until expiry" as accepted behaviour tied to D1, not as a defect

**Implementation Note**: After completing this phase and all automated verification passes, pause here for manual confirmation from the human that the manual testing was successful before proceeding to the next phase.

---

## Phase 5: Close the display-name oracle

### Overview

The only production change. Test first: a contract test shows that a taken display name answers differently for a new email and an existing email, then the fix makes register return `RegistrationFailed` for a taken display name so the two answers are identical. Rename keeps `DuplicateDisplayName`.

**Test contract**
- **Behavior asserted**: register with a taken display name returns 400 with the error-code list `["RegistrationFailed"]` and the same problem body (minus the trace id) whether the email is new or already registered; no account is created in either case; rename to a taken display name still returns `DuplicateDisplayName`.
- **Regression caught**: any reintroduction of a display-name-specific code on register, or a check reorder that makes the answer depend on email existence.
- **Research source**: research.md Risk #6 "Oracle found", Challenge "the generic error message means no leak", FW3 outcome; planning decision (register collapses to `RegistrationFailed`, rename unchanged).
- **Edge / error / boundary case**: email taken and display name taken together (one `RegistrationFailed`, not two); taken display name differing only by case or surrounding whitespace; display-name length or `@` violations still produce their own specific codes because those checks run before any repository call and do not depend on email existence.
- **Anti-pattern avoided**: a unit test that feeds a mocked `UserManager` a combination the real flow cannot produce. The red and green evidence is the integration contract test on the real pipeline; unit tests only cover the repository mapping contract.

### Changes Required:

#### 1. Red contract test

**File**: `PigeonWatch/Api/IntegrationTests/RegisterContractTests.cs`

**Intent**: Prove the oracle first so the fix is justified and the test is known to fail for the right reason.

**Contract**: One new test that registers A with display name D, then compares the 400 answer for a new email with D against the 400 answer for the existing email with D; written and run red before the production change.

#### 2. Repository collapse on create

**File**: `PigeonWatch/Api/Data/Repositories/AccountRepository.cs`

**Intent**: On the create path only, turn `DuplicateDisplayName` into the existing generic `RegistrationFailed` with the generic description, de-duplicated against an email duplicate; the rename and password paths keep their codes.

**Contract**: The create result never contains `DuplicateDisplayName`; when both email and name are duplicates the result holds exactly one `RegistrationFailed`; `UpdateDisplayNameAsync` still returns `DuplicateDisplayName`. The same collapse covers the SQL Server duplicate-index race path because it flows through the same method.

#### 3. Repository unit tests

**File**: `PigeonWatch/Api/UnitTests/AccountRepositoryTests.cs`

**Intent**: Replace the assertion that create keeps the display-name error beside email errors (it stops being true) with the new contract, and keep the rename assertion.

**Contract**: The theory at `:83-92` becomes a test that a display-name duplicate on create (alone or beside an email duplicate) yields a single `RegistrationFailed` with the generic description; the rename test at `:157` stays unchanged.

#### 4. SPA register mapping

**Files**: `PigeonWatch/Frontend/src/app/shared/auth/auth-errors.ts`, `auth-errors.spec.ts`, `PigeonWatch/Frontend/src/app/features/auth/register-page/register-page.ts`, `register-page.spec.ts`

**Intent**: The register screen no longer maps a display-name-specific server code, so a taken name shows the generic form-level message; the profile screen keeps its field message.

**Contract**: Remove the `duplicatedisplayname` branch from `applyErrorCode` (register only, `auth-errors.ts:128`) and the `duplicateDisplayName` entry in the register page messages; replace the register-side specs for `DuplicateDisplayName` with one that asserts `RegistrationFailed` shows the generic message and keeps the typed values; `applyProfileCode` and the profile specs are untouched; the `auth.errors.duplicateDisplayName` label stays (profile uses it).

#### 5. Documentation of the rule

**File**: `PigeonWatch/CLAUDE.md`

**Intent**: Rewrite the hard-rule sentence that says `DuplicateDisplayName` stays specific on register.

**Contract**: State that register returns the generic `RegistrationFailed` for a taken display name (so it cannot be used to probe emails), and that only rename keeps the specific code.

### Success Criteria:

#### Automated Verification:

- Register with a taken display name answers 400 RegistrationFailed only, for a new and for an existing email: `dotnet test PigeonWatch/Api/IntegrationTests/PigeonWatch.IntegrationTests.csproj --filter "FullyQualifiedName~Register_with_a_taken_display_name"`
- Rename to a taken display name still returns DuplicateDisplayName: `dotnet test PigeonWatch/Api/UnitTests/PigeonWatch.UnitTests.csproj --filter "FullyQualifiedName~AccountRepositoryTests"`
- Unit tests pass: `dotnet test PigeonWatch/Api/UnitTests/PigeonWatch.UnitTests.csproj`
- Architecture tests pass: `dotnet test PigeonWatch/Api/ArchitectureTests/PigeonWatch.ArchitectureTests.csproj`
- Frontend tests pass: `npm test -- --watch=false` (run from `PigeonWatch/Frontend`)
- Production frontend build stays within budget: `npm run build` (run from `PigeonWatch/Frontend`)
- Full integration suite passes: `dotnet test PigeonWatch/Api/IntegrationTests/PigeonWatch.IntegrationTests.csproj`

#### Manual Verification:

- The new contract test was run red against the unfixed code and failed because the two answers differed (`DuplicateDisplayName` versus `RegistrationFailed`), before the fix was written
- In the running SPA, registering with a taken display name shows the generic form message and keeps the typed values, and renaming to a taken name on the profile page still shows "This name is already taken."

**Implementation Note**: After completing this phase and all automated verification passes, pause here for manual confirmation from the human that the manual testing was successful before proceeding to the next phase. Deploy note: the API and SPA ship from one merge; during the deployment gap an old API answering `DuplicateDisplayName` to the new SPA renders the generic unexpected-error message.

---

## Phase 6: Risk #6 enumeration, lockout and limiter

### Overview

Prove registration and login failures reveal nothing about which emails exist, that lockout holds across addresses, and that the limiter fires at the 11th request from one address and bounds password guessing with a stolen token.

**Test contract**
- **Behavior asserted**: every failure variant for an existing email (same password, different password, fresh display name, case variant, whitespace-padded) returns the identical generic 400 body (minus the trace id) as a new email with a taken display name, and echoes neither email nor display name; no extra account is created; login 401 is identical for an unknown email and a wrong password and an unknown email never answers `LockedOut`; five wrong logins from five different addresses lock the account and the correct password is then refused with `LockedOut`; the 11th login request from one address gets 429 while another address still reaches the lockout answer for the same account; ten wrong current-password attempts on `PUT account/password` return 400 and the 11th returns 429.
- **Regression caught**: a new register error branch that differs by email existence (including any future check reorder), a login branch that distinguishes unknown from known accounts, lockout disabled or moved to a per-IP counter, the limiter dropped from `auth` routes or from the password route, or a rate-limiting middleware moved so it no longer pre-empts handlers.
- **Research source**: research.md Risk #6 (register check order, case and whitespace variants, login failure shapes, accepted `LockedOut` oracle, limiter order and budget arithmetic, password endpoint not touching the lockout counter), FW3, FW5, FW6 outcomes.
- **Edge / error / boundary case**: case and padding variants of the email; lockout from distributed addresses (the per-IP limiter does not help the attacker, the per-account lockout does); the exact 10-permit boundary asserted once in one test with a request budget of at most 11 per address; lockout expiry only if FW5 showed the lockout end can be moved through the held-open connection, otherwise that row is closed with an n/a note and a §7 entry.
- **Anti-pattern avoided**: asserting configuration values or option numbers. Tests observe statuses and bodies; the boundary count comes from the documented contract in `Api/CLAUDE.md`, and no test reads `PermitLimit` or lockout options. No timing assertions.

### Changes Required:

#### 1. Enumeration contract tests

**File**: `PigeonWatch/Api/IntegrationTests/EnumerationContractTests.cs`

**Intent**: Compare answers, not code paths.

**Contract**: A normalizer that parses a ProblemDetails body and removes only the trace id, so two bodies can be compared for equality. Registration tests compare the answer to "taken display name, new email" against each existing-email variant; login tests compare unknown-email and wrong-password answers; account counts are read back with `CountUserAccounts`. The register success versus failure split is not asserted as a defect.

#### 2. Lockout and limiter contract tests

**File**: `PigeonWatch/Api/IntegrationTests/LockoutAndRateLimitContractTests.cs`

**Intent**: Prove the two brute-force defences fire, within the 10-permit budget.

**Contract**: Distributed lockout test (five wrong logins from five fresh addresses, then the correct password from a sixth); limiter test (ten wrong logins from one address, the 11th gets 429 even though lockout is active, a different address still gets `LockedOut`); password-guessing test (a token obtained from another address sends ten wrong current passwords expecting 400, the 11th expecting 429); a conditional lockout-expiry test per FW5. Every client address stays within 10 `auth`-group requests except where the limiter itself is under test.

### Success Criteria:

#### Automated Verification:

- Every existing-email register failure variant matches the taken-display-name answer exactly: `dotnet test PigeonWatch/Api/IntegrationTests/PigeonWatch.IntegrationTests.csproj --filter "FullyQualifiedName~Register_failure_variants"`
- Failed registrations create no extra account and echo neither email nor display name: `dotnet test PigeonWatch/Api/IntegrationTests/PigeonWatch.IntegrationTests.csproj --filter "FullyQualifiedName~Register_failures_keep"`
- Login answers are identical for an unknown email and a wrong password, and never LockedOut for an unknown email: `dotnet test PigeonWatch/Api/IntegrationTests/PigeonWatch.IntegrationTests.csproj --filter "FullyQualifiedName~Login_failure"`
- Five wrong logins from five addresses lock the account and the correct password is then refused: `dotnet test PigeonWatch/Api/IntegrationTests/PigeonWatch.IntegrationTests.csproj --filter "FullyQualifiedName~Lockout_holds_across"`
- A lockout moved into the past lets the correct password in again, or the row is closed with an n/a note when FW5 showed it cannot be driven: `dotnet test PigeonWatch/Api/IntegrationTests/PigeonWatch.IntegrationTests.csproj --filter "FullyQualifiedName~Lockout_expiry"`
- The 11th login request from one address gets 429 while another address still gets LockedOut: `dotnet test PigeonWatch/Api/IntegrationTests/PigeonWatch.IntegrationTests.csproj --filter "FullyQualifiedName~Eleventh_login_request"`
- A stolen access token guessing the current password gets 429 on the 11th attempt from one address: `dotnet test PigeonWatch/Api/IntegrationTests/PigeonWatch.IntegrationTests.csproj --filter "FullyQualifiedName~Password_guessing"`
- Full integration suite passes: `dotnet test PigeonWatch/Api/IntegrationTests/PigeonWatch.IntegrationTests.csproj`

#### Manual Verification:

- Mutation check: temporarily raise the limiter's permit limit to 100; the 11th-request and password-guessing tests fail; revert and confirm `git diff` shows no production change

**Implementation Note**: After completing this phase and all automated verification passes, pause here for manual confirmation from the human that the manual testing was successful before proceeding to the next phase.

---

## Phase 7: Cookbook update

### Overview

Write down what shipped so the next endpoint author follows it: §6.4 (new endpoint test), §6.3 (harness growth), §6.7 (surprises), §7 (negative space), §8 (ledger), plus the approved §2 wording backport.

### Changes Required:

#### 1. §6.4 and §6.3

**File**: `context/foundation/test-plan.md`

**Intent**: Replace the §6.4 placeholder with the working pattern and extend §6.3 with the new seams.

**Contract**: §6.4 states where the suite lives, the naming rule (`Subject_behavior`), the reference tests (the sweep and the ownership test), the steps for a new endpoint (add a fixture or an exemption to the registry or the coverage guard fails; add it to the anonymous list or authorize it; run the sweep), the rate-limit budget rule (fresh address per request, at most 10 `auth`-group requests per address), and the run command. §6.3 gains the `TestUser` helper, per-request address sending, the audit-column reader, the clock seam FW1 selected, and the probe outcomes under "Spike outcomes".

#### 2. §6.7, §7, §8

**File**: `context/foundation/test-plan.md`

**Intent**: Capture the surprises and the deliberate exclusions this phase produced.

**Contract**: §6.7 gets a Phase 2 note (the oracle was a real finding, logout does not exist, access tokens outlive a password change by design). §7 gains: no contact-detail rule on free text (self-disclosure), the inherent register success versus failure split, the accepted `LockedOut` oracle and the victim-lockout consequence, no spoof-resistance assertion, no timing assertions, no deleted-user token path, no sighting write-ownership until S-09 to S-11, and any FW-driven exclusion (lockout expiry, forced-500 case). §8 gets a dated ledger line and the §3 Phase 2 row moves per the orchestrator.

#### 3. §2 wording backport (approved)

**File**: `context/foundation/test-plan.md`

**Intent**: Make the risk map truthful about what the tests prove, wording and sources only, never file anchors.

**Contract**: Risk #3 row and guidance: drop "after logout", state the access versus refresh distinction and that "modify" is account-only until S-09 to S-11, and note the hot-spot directory is partial evidence. Risk #6 row and guidance: scope "indistinguishable" to failure answers, add the display-name interaction, drop "timing". Risk #2 guidance: add the stubbed-route sweep and the free-text note.

### Success Criteria:

#### Automated Verification:

- The §6.4 placeholder is gone: `grep -c "TBD — see §3 Phase 2" context/foundation/test-plan.md` prints 0
- §6.3 names the new harness seams: `grep -c "TestUser" context/foundation/test-plan.md` prints a count of at least 1
- All three API test projects pass: `dotnet test PigeonWatch/Api/ArchitectureTests/PigeonWatch.ArchitectureTests.csproj && dotnet test PigeonWatch/Api/UnitTests/PigeonWatch.UnitTests.csproj && dotnet test PigeonWatch/Api/IntegrationTests/PigeonWatch.IntegrationTests.csproj`
- No touched file has LF-only line endings: `git ls-files --eol -m -o --exclude-standard | grep -c "w/lf"` prints 0

#### Manual Verification:

- A reader unfamiliar with the suite can follow §6.4 to add a test for a hypothetical new endpoint (fixture or exemption, anonymous list, sweep run)
- The §2 backport diff is wording and sources only, with no file or line anchors

**Implementation Note**: After completing this phase and all automated verification passes, pause here for manual confirmation from the human that the manual testing was successful. Then mark the §3 Phase 2 row `complete` through the orchestrator.

---

## Testing Strategy

### Unit Tests:

- `AccountRepositoryTests`: create-path collapse of a display-name duplicate into one generic `RegistrationFailed`; rename keeps `DuplicateDisplayName`.
- Frontend specs: register page and `registerFailure` show the generic message for `RegistrationFailed`; profile specs unchanged.

### Integration Tests:

- Real HTTP pipeline, one factory per test, real bearer tokens: privacy sweep and coverage guard, ownership, endpoint protection, token lifecycle, enumeration, lockout and limiter, plus the register oracle contract.

### Manual Testing Steps:

1. Run each mutation check named in Phases 2, 3 and 6 and confirm the suite goes red, then revert.
2. In the running SPA, register with a taken display name and rename to a taken name on the profile page (Phase 5).
3. Read test-plan §6.4 as a newcomer (Phase 7).

## Performance Considerations

Each test builds its own factory on an in-memory SQLite database, which is cheap. The sweep sends about 30 requests per caller and each uses a fresh address. If FW1 forces the real-delay fallback, the delay is at most two seconds in one test. No lifecycle test waits on wall-clock time otherwise.

## Migration Notes

No data migration. The API and SPA deploy from one merge; the only visible effect during the deployment gap is described in the Phase 5 implementation note.

## References

- Related research: `context/changes/testing-api-privacy-authorization-contract/research.md`
- Phase 1 archive (harness, recovery login): `context/archive/2026-10-18-testing-cold-start-registration-resilience/`
- Harness: `PigeonWatch/Api/IntegrationTests/PigeonWatchApiFactory.cs:25-32`, `PigeonWatch/Api/IntegrationTests/RegisterContractTests.cs:35-67`
- Error collapse: `PigeonWatch/Api/Data/Repositories/AccountRepository.cs:77-101`
- Stubbed routes: `PigeonWatch/Api/DependencyInjection/PigeonWatchEndpointRouteBuilderExtensions.cs:14-43`
- Limiter and middleware order: `PigeonWatch/Api/WebApi.Host/Program.cs:37-54,72-76`
- Cookbook target: `context/foundation/test-plan.md` §6.3, §6.4

## Progress

> Convention: `- [ ]` pending, `- [x]` done. Append ` — <commit sha>` when a step lands. Do not rename step titles. See `references/progress-format.md`.

### Phase 1: Probes and harness seams

#### Automated

- [x] 1.1 Solution builds with the new harness helpers: `dotnet build PigeonWatch/Api/PigeonWatchApi.slnx` — 8009738
- [x] 1.2 Existing integration tests still pass: `dotnet test PigeonWatch/Api/IntegrationTests/PigeonWatch.IntegrationTests.csproj` — 8009738
- [x] 1.3 research.md records an entry for each of the six framework questions: `grep -c "^### FW[1-6]" context/changes/testing-api-privacy-authorization-contract/research.md` prints 6 — 8009738
- [x] 1.4 The probe class is gone: `test ! -e PigeonWatch/Api/IntegrationTests/ProbeScratch.cs` — 8009738

#### Manual

- [x] 1.5 The clock decision (which of the four branches, and what the Phase 4 expiry test will use) is read and agreed before Phase 4 depends on it — 8009738

### Phase 2: Risk #2 leak sweep

#### Automated

- [x] 2.1 Scanner self-tests find the seeded email raw, JSON-escaped, in a header and in Set-Cookie: `dotnet test PigeonWatch/Api/IntegrationTests/PigeonWatch.IntegrationTests.csproj --filter "FullyQualifiedName~ResponseLeakScanner"`
- [x] 2.2 Positive control finds A's own email in A's configuration response: `dotnet test PigeonWatch/Api/IntegrationTests/PigeonWatch.IntegrationTests.csproj --filter "FullyQualifiedName~Scanner_finds_the_own_email"`
- [x] 2.3 Coverage guard passes (every route has a fixture or exemption, no stale entries): `dotnet test PigeonWatch/Api/IntegrationTests/PigeonWatch.IntegrationTests.csproj --filter "FullyQualifiedName~Every_"`
- [x] 2.4 Sweep as anonymous, as B and as A finds no other user's email and no 429 or unexpected 5xx: `dotnet test PigeonWatch/Api/IntegrationTests/PigeonWatch.IntegrationTests.csproj --filter "FullyQualifiedName~Sweep_as_"`
- [x] 2.5 Stubbed Identity routes behave as stubs with no body: `dotnet test PigeonWatch/Api/IntegrationTests/PigeonWatch.IntegrationTests.csproj --filter "FullyQualifiedName~Stubbed_identity_routes"`
- [x] 2.6 Audit columns hold user ids or SYSTEM, never an email: `dotnet test PigeonWatch/Api/IntegrationTests/PigeonWatch.IntegrationTests.csproj --filter "FullyQualifiedName~Audit_columns"`
- [x] 2.7 Full integration suite passes: `dotnet test PigeonWatch/Api/IntegrationTests/PigeonWatch.IntegrationTests.csproj`

#### Manual

- [x] 2.8 Mutation check one: temporarily add the reporter's email to the sighting response; the sweep fails on `nearby`; revert and confirm `git diff` shows no production change
- [x] 2.9 Mutation check two: temporarily delete one fixture; the coverage guard fails; restore it

### Phase 3: Risk #3 ownership and endpoint protection

#### Automated

- [ ] 3.1 `mine` lists only the caller's sightings for A and for B: `dotnet test PigeonWatch/Api/IntegrationTests/PigeonWatch.IntegrationTests.csproj --filter "FullyQualifiedName~Mine_lists_only"`
- [ ] 3.2 B's profile rename and password change leave A's account and password unchanged: `dotnet test PigeonWatch/Api/IntegrationTests/PigeonWatch.IntegrationTests.csproj --filter "FullyQualifiedName~Account_changes_by"`
- [ ] 3.3 B cannot change A's password by supplying A's current password: `dotnet test PigeonWatch/Api/IntegrationTests/PigeonWatch.IntegrationTests.csproj --filter "FullyQualifiedName~Password_change_with_another"`
- [ ] 3.4 B's sighting is attributed to B despite forged ownership fields in the body: `dotnet test PigeonWatch/Api/IntegrationTests/PigeonWatch.IntegrationTests.csproj --filter "FullyQualifiedName~Sighting_is_attributed"`
- [ ] 3.5 B reads A's sighting by id and in nearby with a display name and no email, anonymous gets 401: `dotnet test PigeonWatch/Api/IntegrationTests/PigeonWatch.IntegrationTests.csproj --filter "FullyQualifiedName~Open_reads"`
- [ ] 3.6 Every endpoint is authorize-protected or on the anonymous list, with no stale list entries: `dotnet test PigeonWatch/Api/IntegrationTests/PigeonWatch.IntegrationTests.csproj --filter "FullyQualifiedName~Anonymous_list"`
- [ ] 3.7 Anonymous calls to every protected endpoint get 401: `dotnet test PigeonWatch/Api/IntegrationTests/PigeonWatch.IntegrationTests.csproj --filter "FullyQualifiedName~Anonymous_calls"`
- [ ] 3.8 Full integration suite passes: `dotnet test PigeonWatch/Api/IntegrationTests/PigeonWatch.IntegrationTests.csproj`

#### Manual

- [ ] 3.9 Mutation check: temporarily remove `[Authorize]` from one sighting action; the protection guard fails; restore it and confirm `git diff` shows no production change

### Phase 4: Risk #3 token lifecycle after a password change

#### Automated

- [ ] 4.1 A refresh with an unrevoked refresh token returns working tokens: `dotnet test PigeonWatch/Api/IntegrationTests/PigeonWatch.IntegrationTests.csproj --filter "FullyQualifiedName~Refresh_without_a_password_change"`
- [ ] 4.2 A rename does not end other sessions: `dotnet test PigeonWatch/Api/IntegrationTests/PigeonWatch.IntegrationTests.csproj --filter "FullyQualifiedName~Rename_keeps"`
- [ ] 4.3 After a password change the old refresh token gets 401 and the new password logs in: `dotnet test PigeonWatch/Api/IntegrationTests/PigeonWatch.IntegrationTests.csproj --filter "FullyQualifiedName~Password_change_rejects_the_old_refresh_token"`
- [ ] 4.4 After a password change the old access token still gets 200: `dotnet test PigeonWatch/Api/IntegrationTests/PigeonWatch.IntegrationTests.csproj --filter "FullyQualifiedName~Old_access_token_survives"`
- [ ] 4.5 After the clock passes the access lifetime the old access token gets 401: `dotnet test PigeonWatch/Api/IntegrationTests/PigeonWatch.IntegrationTests.csproj --filter "FullyQualifiedName~Old_access_token_is_rejected_after_expiry"`
- [ ] 4.6 The lifecycle tests pass three runs in a row: `for i in 1 2 3; do dotnet test PigeonWatch/Api/IntegrationTests/PigeonWatch.IntegrationTests.csproj --filter "FullyQualifiedName~TokenLifecycleContractTests" || break; done`

#### Manual

- [ ] 4.7 Test names and the Phase 7 cookbook wording present "old access token survives until expiry" as accepted behaviour tied to D1, not as a defect

### Phase 5: Close the display-name oracle

#### Automated

- [ ] 5.1 Register with a taken display name answers 400 RegistrationFailed only, for a new and for an existing email: `dotnet test PigeonWatch/Api/IntegrationTests/PigeonWatch.IntegrationTests.csproj --filter "FullyQualifiedName~Register_with_a_taken_display_name"`
- [ ] 5.2 Rename to a taken display name still returns DuplicateDisplayName: `dotnet test PigeonWatch/Api/UnitTests/PigeonWatch.UnitTests.csproj --filter "FullyQualifiedName~AccountRepositoryTests"`
- [ ] 5.3 Unit tests pass: `dotnet test PigeonWatch/Api/UnitTests/PigeonWatch.UnitTests.csproj`
- [ ] 5.4 Architecture tests pass: `dotnet test PigeonWatch/Api/ArchitectureTests/PigeonWatch.ArchitectureTests.csproj`
- [ ] 5.5 Frontend tests pass: `npm test -- --watch=false` (run from `PigeonWatch/Frontend`)
- [ ] 5.6 Production frontend build stays within budget: `npm run build` (run from `PigeonWatch/Frontend`)
- [ ] 5.7 Full integration suite passes: `dotnet test PigeonWatch/Api/IntegrationTests/PigeonWatch.IntegrationTests.csproj`

#### Manual

- [ ] 5.8 The new contract test was run red against the unfixed code and failed because the two answers differed (`DuplicateDisplayName` versus `RegistrationFailed`), before the fix was written
- [ ] 5.9 In the running SPA, registering with a taken display name shows the generic form message and keeps the typed values, and renaming to a taken name on the profile page still shows "This name is already taken."

### Phase 6: Risk #6 enumeration, lockout and limiter

#### Automated

- [ ] 6.1 Every existing-email register failure variant matches the taken-display-name answer exactly: `dotnet test PigeonWatch/Api/IntegrationTests/PigeonWatch.IntegrationTests.csproj --filter "FullyQualifiedName~Register_failure_variants"`
- [ ] 6.2 Failed registrations create no extra account and echo neither email nor display name: `dotnet test PigeonWatch/Api/IntegrationTests/PigeonWatch.IntegrationTests.csproj --filter "FullyQualifiedName~Register_failures_keep"`
- [ ] 6.3 Login answers are identical for an unknown email and a wrong password, and never LockedOut for an unknown email: `dotnet test PigeonWatch/Api/IntegrationTests/PigeonWatch.IntegrationTests.csproj --filter "FullyQualifiedName~Login_failure"`
- [ ] 6.4 Five wrong logins from five addresses lock the account and the correct password is then refused: `dotnet test PigeonWatch/Api/IntegrationTests/PigeonWatch.IntegrationTests.csproj --filter "FullyQualifiedName~Lockout_holds_across"`
- [ ] 6.5 A lockout moved into the past lets the correct password in again, or the row is closed with an n/a note when FW5 showed it cannot be driven: `dotnet test PigeonWatch/Api/IntegrationTests/PigeonWatch.IntegrationTests.csproj --filter "FullyQualifiedName~Lockout_expiry"`
- [ ] 6.6 The 11th login request from one address gets 429 while another address still gets LockedOut: `dotnet test PigeonWatch/Api/IntegrationTests/PigeonWatch.IntegrationTests.csproj --filter "FullyQualifiedName~Eleventh_login_request"`
- [ ] 6.7 A stolen access token guessing the current password gets 429 on the 11th attempt from one address: `dotnet test PigeonWatch/Api/IntegrationTests/PigeonWatch.IntegrationTests.csproj --filter "FullyQualifiedName~Password_guessing"`
- [ ] 6.8 Full integration suite passes: `dotnet test PigeonWatch/Api/IntegrationTests/PigeonWatch.IntegrationTests.csproj`

#### Manual

- [ ] 6.9 Mutation check: temporarily raise the limiter's permit limit to 100; the 11th-request and password-guessing tests fail; revert and confirm `git diff` shows no production change

### Phase 7: Cookbook update

#### Automated

- [ ] 7.1 The §6.4 placeholder is gone: `grep -c "TBD — see §3 Phase 2" context/foundation/test-plan.md` prints 0
- [ ] 7.2 §6.3 names the new harness seams: `grep -c "TestUser" context/foundation/test-plan.md` prints a count of at least 1
- [ ] 7.3 All three API test projects pass: `dotnet test PigeonWatch/Api/ArchitectureTests/PigeonWatch.ArchitectureTests.csproj && dotnet test PigeonWatch/Api/UnitTests/PigeonWatch.UnitTests.csproj && dotnet test PigeonWatch/Api/IntegrationTests/PigeonWatch.IntegrationTests.csproj`
- [ ] 7.4 No touched file has LF-only line endings: `git ls-files --eol -m -o --exclude-standard | grep -c "w/lf"` prints 0

#### Manual

- [ ] 7.5 A reader unfamiliar with the suite can follow §6.4 to add a test for a hypothetical new endpoint (fixture or exemption, anonymous list, sweep run)
- [ ] 7.6 The §2 backport diff is wording and sources only, with no file or line anchors
