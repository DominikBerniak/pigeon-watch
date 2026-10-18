# Test Plan

> Phased test rollout for this project. Strategy is frozen at the top
> (§1–§5); cookbook patterns at the bottom (§6) fill in as phases ship.
> Read before writing any new test.
>
> Refresh: re-run `/10x-test-plan --refresh` when stale (see §8).
>
> Last updated: 2026-10-18

## 1. Strategy

Tests follow three non-negotiable principles for this project:

1. **Cost × signal.** The cheapest test that gives a real signal for the
   risk wins. Do not promote to e2e because e2e "feels safer." Do not put a
   vision model on top of a deterministic visual diff that already catches
   the regression.
2. **User concerns are first-class evidence.** Risks anchored in "the
   team is worried about X, and the failure would surface somewhere in
   <area>" carry the same weight as PRD lines or hot-spot data.
3. **Risks are scenarios, not code locations.** This plan documents *what
   could fail* and *why we believe it's likely* — drawn from documents,
   interview, and codebase *signal* (churn, structure, test base). It does
   NOT claim to know which line owns the failure. That knowledge is
   produced by `/10x-research` during each rollout phase. If the plan and
   research disagree about where the failure lives, research is the
   ground truth.

Hot-spot scope used for likelihood weighting: `PigeonWatch/Api` (all layered
projects) and `PigeonWatch/Frontend/src`, excluding docs, archive, migrations,
lockfiles, build output and `node_modules`. Window: last 30 days, 30 commits.

## 2. Risk Map

The top failure scenarios this project must protect against, ordered by
risk = impact × likelihood. Risks are failure scenarios in user / business
terms, not test names. The Source column cites the *evidence that surfaced
this risk* — never a specific file as "where the failure lives" (that is
research's job, see §1 principle #3).

| # | Risk (failure scenario) | Impact | Likelihood | Source (evidence — not anchor) |
|---|-------------------------|--------|------------|--------------------------------|
| 1 | A new user registers while the Azure free-tier backend is cold-starting and ends up with a failure message, a duplicate or half-created account, or gives up | High | High | interview Q1; tech-stack.md (App Service F1 sleeps on idle); PRD FR-001 (login wall in front of every feature); archive `register-and-login` (cold-start retry decision) |
| 2 | Another user's raw email or contact detail appears in some response as new surfaces land (messaging, comments, photos) | High | Medium | PRD Guardrails + NFR (contact info never exposed); interview Q4; roadmap S-04, S-07, S-08 (new surfaces); `PigeonWatch/CLAUDE.md` hard rule |
| 3 | A logged-in user reads another user's records or changes another user's account, or a stale session keeps access after a password change for longer than the design allows | High | Medium | PRD Access Control; roadmap S-09, S-10, S-11 (ownership, admin); archive `register-and-login` (no server-side logout, accepted); archive `edit-profile-name` (session rotation on password change); hot-spot dir `PigeonWatch/Api/WebApi` (about 20 commits/30d; partial evidence only, the session lifecycle is framework behaviour that sits outside that directory) |
| 4 | A reported sighting does not appear on the nearby map, appears in the wrong place, or the wrong set shows (resolved ones, outside the radius) | High | Medium | PRD US-01 and first primary success criterion; archive `report-sighting-on-map`, `nearby-map-review-fixes`; hot-spot dir `PigeonWatch/Frontend/src/app/features` (87 file touches/30d) |
| 5 | A change to a shared UI component silently breaks behaviour or accessibility on several screens | Medium | High | interview Q3; hot-spot dir `PigeonWatch/Frontend/src/app/shared` (94 file touches/30d); archive UI-contract slices; open change `app-wide-large-screen-layout` |
| 6 | An attacker probes registration or login to learn which emails are registered (including through the display-name check), or brute-forces accounts | Medium | Medium | `PigeonWatch/CLAUDE.md` hard rule (no email confirmation, generic registration failure); archive `register-and-login`; abuse lens; Phase 2 research (a taken display name answered differently for a new and an existing email) |

Out of the map by design: "deploy goes green while the database is broken"
is high impact, low likelihood and not a test; it is handled as a gate in §5
(post-deploy smoke), not as a risk. "Photo upload accepts hostile files" is
not mapped because the slice does not exist yet; any new response it adds is
covered by the Risk #2 leak check.

### Risk Response Guidance

| Risk | What would prove protection | Must challenge | Context `/10x-research` must ground | Likely cheapest layer | Anti-pattern to avoid |
|------|-----------------------------|----------------|--------------------------------------|-----------------------|-----------------------|
| #1 | A first registration that is slow, fails at the network level, or is retried ends in exactly one usable account and an honest message, never a false "failed" for an account that was created | "Retry logic exists, so cold start is covered"; "retrying a POST is safe" | Warm-up and retry behaviour on the client; what the server does on a repeated register; how a failed-then-succeeded request is reported to the user | unit (fake HTTP: network failure, delay, then success) + API integration (repeated register) | Mocking the retry itself; asserting only the happy path |
| #2 | No response from any user-reachable endpoint carries another user's email, including the Identity routes the app stubs out (still routed, answering 404), and the check extends to every new endpoint | "Display name rejects `@`, so it is safe"; "a few spot checks cover it"; "contact details typed into free text are a system leak" (it is self-disclosure and no rule exists, see §7) | Every response contract that names a user; which routes are stubbed rather than removed; persisted audit fields; error bodies | API integration (contract-wide sweep that enumerates every routed endpoint and is seeded with a known test email) | Asserting only the fields the code currently returns; enumerating only the documented endpoints |
| #3 | User B cannot read or change user A's account data, and every endpoint is either protected or on an explicit anonymous list; after a password change the old refresh token is rejected at once and the old access token stops working once it expires (no server-side logout exists, so none is asserted). "Modify" is account-only until S-09 to S-11 add sighting edit and admin endpoints | "Authenticated means authorized"; "a 401 without a token covers ownership"; "logout ends the session" | Ownership checks per endpoint; access versus refresh token lifetime and the token lifecycle after a password change | API integration (two users, real HTTP pipeline) | Mocking the current-user provider so ownership can never fail; asserting that an old access token is revoked |
| #4 | A reported sighting shows up where the reporter put it, within the 10 km rule, and open and resolved sightings are separated per the PRD | "Unit-tested distance maths equals map correctness"; "the map stub equals the real map" | Report to persistence to nearby query to map render path; status and radius rules; what the map stub hides | integration for the rules, one e2e for the full flow | Expected values copied from production code; e2e where integration is enough |
| #5 | Shared components keep their behaviour (focus, states, keyboard, accessibility) when edited, checked against the screens that consume them | "Component specs pass, so consumers are fine"; "a snapshot diff means something" | Which screens consume each shared component; existing spec coverage and gaps | component-level specs + accessibility check; selective multimodal review on 1–3 screens | Pixel or markup snapshots (excluded, §7) |
| #6 | Registration failure answers are indistinguishable for existing and new emails, including when the display name is already taken, and login failure answers are the same for an unknown email and a wrong password; the rate limit and lockout actually fire. The register success versus failure split is inherent without email confirmation, and the lockout detail on login is an accepted exception | "The generic error message means no leak"; "the rate limit is configured, so it works"; "the display name check cannot reveal an email" | Error shapes and the order of checks that decide them; per-IP limiter and per-account lockout behaviour | API integration | Asserting configuration values instead of observed behaviour; timing assertions |

## 3. Phased Rollout

Each row is a discrete rollout phase that will open its own change folder
via `/10x-new`. Status moves left-to-right through the values below; the
orchestrator updates Status as artifacts appear on disk.

| # | Phase name | Goal (one line) | Risks covered | Test types | Status | Change folder |
|---|------------|-----------------|---------------|------------|--------|---------------|
| 1 | Cold-start registration resilience | Prove one usable account and an honest message when registration is slow or retried; bootstrap the API integration harness | #1 | unit (fake HTTP) + integration | complete | testing-cold-start-registration-resilience |
| 2 | API privacy and authorization contract | Prove no email leak, ownership enforced, sessions invalidated, no email enumeration | #2, #3, #6 | integration (contract) | change opened | testing-api-privacy-authorization-contract |
| 3 | Report-to-map flow | Prove a reported sighting lands on the nearby map under the PRD rules, plus one critical-path e2e | #4 | integration + 1 e2e | not started | — |
| 4 | Shared UI behaviour guard | Prove shared components keep behaviour and accessibility; add a selective multimodal review on 1–3 screens | #5 | component spec, accessibility, multimodal review (selective) | not started | — |
| 5 | Quality-gates wiring | Wire the suites added after Phase 1 into PR checks (the API integration step is already wired) and make the post-deploy smoke touch the database | cross-cutting | gates | not started | — |

Order rationale: Phase 1 attacks the top risk and builds the API integration
harness that Phases 2 and 3 reuse. Phase 4 has the highest likelihood but the
lowest impact, so it follows the integrity risks. Phase 5 locks the floor once
the suites exist.

**Status vocabulary** (fixed — parser literals): `not started`,
`change opened`, `researched`, `planned`, `implementing`, `complete`.

## 4. Stack

The classic test base for this project. AI-native tools carry a `checked:`
date so future readers can see which lines need re-verification.

Test-base profile: **meaningful** — 43 Angular spec files and 21 xUnit files
(unit and architecture tests), all run in PR checks. No API integration
harness, e2e, lint step or accessibility tooling yet.

| Layer | Tool | Version | Notes |
|-------|------|---------|-------|
| frontend unit + component | Vitest via `ng test` | ^5.0.3 (Angular 22.2) | Wired in PR checks; runs with `--watch=false` in CI |
| backend unit | xUnit + NSubstitute, SQLite in-memory for the user store | .NET 10 | Wired in PR checks |
| backend architecture | xUnit architecture tests | .NET 10 | Enforces layer and coding rules; do not extend for behaviour |
| API integration (real HTTP pipeline) | none yet — see Phase 1 | — | Phase 1 chooses and bootstraps the harness |
| API mocking (frontend) | none yet — fake HTTP at the Angular HTTP boundary | — | Decided in Phase 1 research |
| e2e | none yet — see Phase 3 | — | One critical-path flow only |
| accessibility | none yet — see Phase 4 | — | Chosen in Phase 4 research |
| (optional) AI-native | in-app browser tool for multimodal review of 1–3 screens — checked: 2026-10-18 | n/a | When NOT to use: any screen a deterministic assertion already covers; layout facts that can be measured; every-page review |

**Stack grounding tools (current session):**
- Docs: none (Context7 or framework docs MCP not available in current session) — local manifests and configs used instead; checked: 2026-10-18
- Search: generic web search only, no Exa.ai-style MCP — not used in this pass; checked: 2026-10-18
- Runtime/browser: in-app browser tool available — possible e2e or verification layer (Phase 3, Phase 4); not used yet; checked: 2026-10-18
- Provider/platform: Azure DevOps MCP exposed but CI is GitHub Actions — not relevant to quality gates; not used; checked: 2026-10-18

## 5. Quality Gates

The full set of gates that must pass before a change reaches production.
"Required after §3 Phase <N>" means the gate is enforced once that rollout
phase lands; before that, the gate is `planned`.

| Gate | Where | Required? | Catches |
|------|-------|-----------|---------|
| build + typecheck (both stacks, .NET code style enforced in build) | local + CI | required (wired) | syntactic, type and style drift |
| frontend lint | none | not part of this rollout | — |
| unit + architecture tests (both stacks) | local + CI | required (wired) | logic and layer-rule regressions |
| migrations match the model | CI | required (wired) | schema drift |
| API integration tests | local + CI on PR | required after §3 Phase 1 | cold-start registration, privacy, ownership, enumeration, nearby-rule regressions |
| shared-component behaviour and accessibility specs | local + CI on PR | required after §3 Phase 4 | silent UI regressions across screens |
| e2e on the critical flow | CI on PR | required after §3 Phase 3 | broken register, login, report, map path |
| multimodal visual review (1–3 screens) | local or CI on PR | optional, after §3 Phase 4 | visual issues deterministic specs miss |
| post-deploy smoke that touches the database | between merge and live | required after §3 Phase 5 | deploys that go green with a broken database path |

## 6. Cookbook Patterns

How to add new tests in this project. Each sub-section is filled in once
the relevant rollout phase ships; before that, the sub-section reads
"TBD — see §3 Phase <N>."

### 6.1 Adding a frontend unit or component test

- TBD — see §3 Phase 4 for the shared-component behaviour and accessibility pattern. Existing specs live next to the unit as `*.spec.ts`; run with `ng test` from `PigeonWatch/Frontend`.

### 6.2 Adding a frontend test for slow or failing HTTP (cold start)

- **Where**: next to the flow it covers, here `PigeonWatch/Frontend/src/app/core/auth/register-cold-start.spec.ts` (Vitest via `ng test`, no extra tooling). The interceptor's own cases live in `core/warmup/warmup.interceptor.spec.ts`.
- **Rule: keep the retry real, script the network.** Compose the real `authInterceptor`, `warmupInterceptor`, `AuthApi` and the service under test with `provideHttpClient(withInterceptors([...]))` and `provideHttpClientTesting()`; never mock the retry, the interceptor or the service. Drive the whole flow through `HttpTestingController`: `request.error(new ProgressEvent('error'))` for status `0`, `flush(null, { status: 503, headers: { 'Retry-After': '10' } })` for a platform 503, `flush(body, { status: 400, ... })` for an application answer.
- **Time**: `vi.useFakeTimers()` in `beforeEach` and `vi.useRealTimers()` in `afterEach`; move the clock with `vi.advanceTimersByTime`. Import `warmupBudgetMs` from the interceptor instead of hard-coding 120 s, and finish a case with `httpMock.verify()` after advancing past the budget so a stray resend or extra request fails the test.
- **Contract**: script the response bodies exactly as the API integration suite observed them (§6.3); the frontend fake is only as honest as that pairing. Assert the resent body equals the original, count the requests of the follow-up call (`expectOne` / `expectNone`), and assert outcomes by status and error codes, not by English text.
- **Naming**: one `describe` per situation (`register during a cold start`), one `it` per scenario written as the user-visible outcome; `it.each([0, 503] as const)` for the two unanswered statuses.
- **Reference test**: `register-cold-start.spec.ts`, `recovers with a login when a resend answers RegistrationFailed`.
- **Run**: `npm test -- --watch=false` from `PigeonWatch/Frontend` (add `--include src/app/core/auth/register-cold-start.spec.ts` for one file).

### 6.3 Adding an API integration test

- **Where**: `PigeonWatch/Api/IntegrationTests/` (assembly `PigeonWatch.IntegrationTests`, xunit.v3, in `PigeonWatchApi.slnx`, run by the `Integration tests` step of `api-pr-checks.yml`). It is not part of `PigeonWatchAssemblies`, so the architecture rules do not apply to it; the shared style enforcement does.
- **Factory**: `PigeonWatchApiFactory : WebApplicationFactory<Program>` removes `DbContextOptions<PigeonWatchDbContext>` and `IDbContextOptionsConfiguration<PigeonWatchDbContext>`, then registers the context on a held-open `SqliteConnection("DataSource=:memory:")` with the audit interceptors and runs `EnsureCreated`. Each factory instance owns a clean database and is disposed with the test; create one per test (a test class that builds it in its constructor gets one per test method), never a static shared one.
- **Limiter isolation**: the real `Auth` limiter (10 per minute per remote IP) stays in place. `ClientAddressStartupFilter` sets the remote IP from the `X-Test-Client-Address` header; `factory.CreateApiClient()` gives every client a distinct address, so tests never share a partition. A test that wants a 429 reuses one client or passes the same address twice.
- **Read back state**: `factory.CountUserAccounts()` runs a raw `SELECT COUNT(*) FROM USER_ACCOUNT` on the held-open connection (entities are internal to `Data`). `AccountApiClient` wraps register, login and error-code reading.
- **Signed-in user**: `factory.CreateUserAsync(email, password, displayName)` registers and logs in through the real endpoints from a throwaway address and returns a `TestUser`: email, password, display name, `Id` (read from `GET /configuration/general`), `AccessToken`, `RefreshToken` and a `Client` that carries the bearer header and its own fresh address, so the user starts with a full limiter budget. `TestUser.CreateSightingAsync(...)` posts a valid sighting and returns its id. Tokens always come from a real login; never hand-build a `ClaimsPrincipal`, or ownership can never fail. Dispose the users with the test.
- **Per-request address**: `factory.SendFromFreshAddressAsync(request, accessToken)` sends one `HttpRequestMessage` from a new address with an optional bearer token. Use it for anything that sends more than 10 `auth`-group requests in total (the sweep sends about 30 per caller) and for any call that must not share a limiter partition with the user's own `Client`.
- **Audit columns**: `factory.ReadUserAccountAuditUsers()` and `factory.ReadSightingAuditUsers()` return an `AuditUsers(CreateUser, UpdateUser)` pair for every row of `USER_ACCOUNT` and `SIGHTING` over the held-open connection. Two more raw-SQL seams: `factory.ExpireLockout(email)` moves `LOCKOUT_END` into the past (returns the affected row count) and `factory.CloseDatabase()` closes the held-open connection so a request answers 500.
- **Clock**: `new PigeonWatchApiFactory(clock)` with a `ManualTimeProvider` (`SetUtcNow`, `Advance`, lives in the integration project) replaces the DI `TimeProvider`. That one registration ages bearer access tokens (advance 61 minutes and an old access token answers 401) and also drives the 60 s sighting cooldown, so one user can report more than once after `Advance`. Pass nothing when time does not matter. The limiter has no clock seam: a new factory is the only way to reset its window.
- **Reference test**: `RegisterContractTests.Register_repeat_returns_only_registration_failed_and_keeps_one_account`. Expected values are the documented contract (error code name, non-enumeration), not production constants.
- **Run**: `dotnet test PigeonWatch/Api/IntegrationTests/PigeonWatch.IntegrationTests.csproj`.
- **Spike outcomes**: (1) without the header every `TestServer` request lands in one limiter partition (the 11th request in a minute gets 429), confirmed; with distinct addresses it does not. (2) Removing only `DbContextOptions<PigeonWatchDbContext>` is not enough: EF throws the "two database providers" error until `IDbContextOptionsConfiguration<PigeonWatchDbContext>` is removed too. (3) The default factory environment is Development, so there is no HTTPS redirect (none observed), and the ephemeral Data Protection keys are enough for register then login inside one factory. A repeat register returns 400 with `RegistrationFailed` alone and no email or display name in the body. (4) Clock: replacing the DI `TimeProvider` ages bearer access tokens (200 before, 401 after +61 minutes); a `BearerTokenOptions` clock and a one-second expiry also worked, so the simplest route was kept and the other two were dropped. A refresh token issued before the advance still refreshes (14-day lifetime). (5) A password change rotates the security stamp: the old refresh token answers 401 (empty body), the old access token still answers 200 until it expires, and the old password no longer logs in. A rename leaves every session alive. (6) Stubbed Identity routes: five answer 404 with an empty body to every caller; the three `auth/manage/*` routes answer 401 with an empty body anonymously and 404 with an empty body when signed in. A 429 has an empty body and no `Retry-After`. A forced 500 (closed connection) is a ProblemDetails with no `detail`. Malformed bodies sent to `auth/login` and `auth/refresh` also answer 500 instead of 400. (7) The fifth wrong login already answers `LockedOut`, never for an unknown email, and `LOCKOUT_END` can be moved through the held-open connection, so lockout expiry is testable. (8) Case variants and whitespace-padded copies of an existing email answer exactly like an exact repeat. (9) A taken display name answered `DuplicateDisplayName` for a new email and `RegistrationFailed` for an existing one: a real enumeration oracle, closed on register in this change (rename keeps the specific code).
- **SQLite fidelity gaps**: SQL-Server-only branches are not reachable here: the duplicate-insert translation (`SqlException` 2601/2627), the `SqlException` mapping to 503, and the execution-strategy commit ambiguity of `EnableRetryOnFailure`. `RetryLimitExceededException` is the only 503 trigger a test could fake.
- **Dependency note**: the project pins `System.Security.Cryptography.Pkcs` 10.0.13 directly because the version pulled in transitively through `Data` (9.0.11, via the SQL Server client) carries known advisories and the audit command would otherwise flag this project.

### 6.4 Adding a test for a new API endpoint

- **Where**: `PigeonWatch/Api/IntegrationTests/`, on the harness from §6.3 (one `PigeonWatchApiFactory` per test, real tokens from `CreateUserAsync`). One class per contract (`OwnershipContractTests`, `EnumerationContractTests`, `TokenLifecycleContractTests`).
- **Naming**: `Subject_behavior` written as the observable outcome, for example `Sighting_is_attributed_to_bob_when_the_body_carries_forged_ownership_fields`. Accepted behaviour is named as accepted, never as a defect: `Old_access_token_survives_a_password_change_until_its_own_expiry_by_design` ties to decision D1 (no server-side logout, tokens expire on their own).
- **Three guards fail until you act**, and that is the point: `EndpointCoverageTests` (every routed endpoint has a fixture or an exemption, no stale or duplicate keys), `EndpointProtectionTests` (every endpoint without authorization metadata is on the anonymous list, no stale entries, and every protected endpoint answers an anonymous caller 401 with no email in the response) and the three `PrivacySweepTests.Sweep_as_*` tests (anonymous, Bob, Alice: no other user's email in any status line, header, `Set-Cookie`, raw body or decoded JSON name and value, no 429, no 5xx).
- **Steps for a new endpoint**:
  1. Add a fixture to `EndpointFixtureRegistry.Fixtures`. The key is `"<METHOD> <pattern>"` exactly as routed, with no leading slash (`GET sightings/{id:guid}`). The builder takes a `FixtureContext` (registered emails, display names, Alice's sighting id) and returns `FixtureRequest`s: one valid request plus the error variants that exercise error bodies (invalid values, an unknown id, and `AddMalformed` for bodies that answer 400). Only when no request can be built for an unprotected endpoint, add an `EndpointExemption(key, reason)` to `Exemptions` with a written reason; the sweep and the coverage guard then skip the key (`Exemptions` is empty today). A protected endpoint needs a fixture, because the anonymous-calls test looks it up.
  2. Protect it or list it. A protected endpoint needs nothing more: the anonymous-calls test sends your fixture requests without a token and expects 401. A deliberately public endpoint gets an `EndpointExemption(key, reason)` in the `anonymousEndpoints` list of `EndpointProtectionTests`; the guard fails if an unprotected route is missing from the list or an entry is stale.
  3. Run the sweep (below). If the new response names a user, the sweep is the test that proves no email rides along; do not replace it with a field check.
  4. If the endpoint takes an id or acts on a user's data, add an ownership test in the shape of `OwnershipContractTests`: seed Alice and Bob with `CreateUserAsync`, let Bob act (send forged ownership fields in the body too), then read Alice's state back through Alice's own `Client` rather than trusting Bob's response. Reads that are open by design (sighting by id and nearby) are asserted as open, with a display name and no email.
  5. If it adds a registration or login failure answer, compare answers rather than code paths: parse the ProblemDetails, remove only `traceId` and assert the existing-email variants equal the new-email answer (`EnumerationContractTests`).
- **Rate-limit budget**: the real limiter stays on. Give every request its own address (`SendFromFreshAddressAsync` or a fresh `CreateApiClient()`); one address sends at most 10 `auth`-group requests (register, login, password change) in a test. Only the limiter tests (`Eleventh_login_request_from_one_address_is_limited_while_another_address_still_reaches_lockout`, `Password_guessing_with_a_stolen_access_token_is_limited_on_the_eleventh_attempt`) reuse one address on purpose, and they observe the 11th answer, not an option value. No test reads limiter or lockout options, and none asserts timing or forwarded-header spoofing.
- **Time**: tests that move the clock build the factory with a `ManualTimeProvider` (`TokenLifecycleContractTests`); everything else leaves it out.
- **Reference tests**: `PrivacySweepTests.Sweep_as_bob_finds_no_other_users_email_in_any_response` (privacy) and `OwnershipContractTests.Sighting_is_attributed_to_bob_when_the_body_carries_forged_ownership_fields` (ownership). Expected values are the documented contract (PRD Access Control, `PigeonWatch/CLAUDE.md`, `Api/CLAUDE.md`), never production constants.
- **Run**: `dotnet test PigeonWatch/Api/IntegrationTests/PigeonWatch.IntegrationTests.csproj`; narrow with `--filter "FullyQualifiedName~Sweep_as_"`, `~Every_`, `~Anonymous_list`, `~Anonymous_calls` or the class name. After adding a fixture, run the sweep and both guards; before closing, run the whole project.

### 6.5 Adding a test for a sighting or map rule

- TBD — see §3 Phase 3 for the report-to-nearby-map rule pattern (oracle from the PRD, not from production code).

### 6.6 Adding an e2e test

- TBD — see §3 Phase 3 (one critical-path flow only).

### 6.7 Per-rollout-phase notes

(Optional. After each phase lands, `/10x-implement` appends a 2–3 line note
here capturing anything surprising the phase taught.)

- **Phase 1, cold-start registration resilience**: the risk was a false "failed" for an account that was created, not a duplicate row (unique indexes already prevent that). The resend after a lost response lands on the generic `RegistrationFailed` 400, so the fix is a scoped recovery login with the typed credentials after an inconclusive register, plus a register-only "couldn't confirm" message after an unanswered warm-up. The real-chain spec (§6.2) and the API integration suite (§6.3) pin the two halves of the same contract.
- **Phase 2, API privacy and authorization contract**: the display-name oracle was a real finding (a taken name answered differently for a new and an existing email), so the phase's one production change collapsed it to the generic `RegistrationFailed` on register; rename keeps the specific code because display names are public. Logout does not exist on the server and an access token outlives a password change until its own expiry, by design (decision D1); the tests pin that as accepted behaviour and pin the refresh token as the thing a password change ends. The stubbed Identity routes stay in the sweep because only the routing table shows them, and the coverage guard makes a new endpoint fail loudly until it has a fixture (§6.4).

## 7. What We Deliberately Don't Test

Exclusions agreed during the rollout (Phase 2 interview, Q5). Future
contributors should respect these unless the underlying assumption changes.

- **Pixel-level snapshot tests** — they break on every styling change and catch nothing meaningful. Re-evaluate if the design system freezes and a stable visual baseline exists. (Source: Phase 2 interview Q5.)
- **The dev-only kitchen-sink page** — it ships no user-facing behaviour. Re-evaluate if it becomes reachable by end users. (Source: Phase 2 interview Q5.)
- **SQL-Server-only register branches** — the duplicate-insert translation (`SqlException` 2601/2627), the `SqlException` to 503 mapping and the execution-strategy commit ambiguity of `EnableRetryOnFailure` cannot run on the SQLite in-memory harness, and a SQL Server container is not worth its CI cost for them today. Re-evaluate if a duplicate or half-created account is ever seen in production. (Source: `testing-cold-start-registration-resilience` plan.)
- **A `429` on a resend and two browser tabs registering at once** — a resend that the rate limiter rejects still shows the too-many-requests message, and two tabs racing one registration are not recovered or tested. Re-evaluate if support reports either. (Source: `testing-cold-start-registration-resilience` plan.)
- **A contact-detail rule on free text** — sighting name, description and behaviour are returned verbatim to other users and no test looks for contact details in them: a user typing their own contact into free text is self-disclosure, not a system leak. Re-evaluate when messaging or comment surfaces (roadmap S-04, S-07, S-08) add free text one user reads from another. (Source: `testing-api-privacy-authorization-contract` plan decision.)
- **The register success versus failure split** — without email confirmation a successful registration (200) is distinguishable from a failed one (400), so registering can still hint at an existing account; the suite asserts only that failure answers are identical and does not treat the split as a defect. Re-evaluate if email confirmation is introduced. (Source: `testing-api-privacy-authorization-contract` plan.)
- **The accepted `LockedOut` oracle and its victim-lockout consequence** — five wrong logins reveal `LockedOut` for an existing account (never for an unknown email, which is asserted) and let anyone lock a known victim out for the lockout window with five requests; there is no dummy-hash equalisation. The tests observe both defences firing and do not try to remove the oracle. Re-evaluate if the team decides to trade the detail or the lockout for another brute-force defence. (Source: `PigeonWatch/CLAUDE.md` hard rule; `testing-api-privacy-authorization-contract` plan.)
- **Spoof-resistance of the client address and timing assertions** — the harness sets the remote address directly, so it cannot observe forwarded-header handling, and a timing comparison between known and unknown accounts would be flaky in CI. Re-evaluate if forwarded-header handling moves into code or timing equalisation is added. (Source: `testing-api-privacy-authorization-contract` plan.)
- **A deleted user's token and sighting write-ownership** — no account-delete endpoint exists, so a deleted user's token cannot be reached through the API, and no sighting edit, status or admin endpoint exists, so only account data can be changed by another user today. Re-evaluate when S-09 to S-11 land; the coverage guard forces a fixture the day those endpoints are routed. (Source: `testing-api-privacy-authorization-contract` plan.)
- **Malformed bodies to the login and refresh routes** — the sweep sends well-formed bodies there because an unparseable body answers 500 instead of 400 (pre-existing framework behaviour, no data in the body, flagged as a candidate product fix). Re-evaluate if the answer becomes a 400; the malformed variants can then join the sweep. (Source: `testing-api-privacy-authorization-contract` research, probe outcome FW4.)

## 8. Freshness Ledger

- Strategy (§1–§5) last reviewed: 2026-10-18
- Stack versions last verified: 2026-10-18
- AI-native tool references last verified: 2026-10-18
- 2026-10-18: §3 Phase 1 marked `complete` (all Progress items `[x]`, manual rows 1.9 and 1.10 ticked).
- 2026-10-18: §3 Phase 5 goal reworded so it no longer claims to wire the API integration suite (Phase 1 wired it; §5 already said so); §6.2, §6.7 and §7 filled by `testing-cold-start-registration-resilience`.
- 2026-10-18: cookbook §6.3 (harness seams and probe outcomes), §6.4 and §6.7, six §7 exclusions and the approved §2 wording backport (Risks #2, #3, #6) filled by `testing-api-privacy-authorization-contract` Phase 7; the §3 Phase 2 row stays `change opened` until the human confirms the phase.

Refresh (`/10x-test-plan --refresh`) when:

- a new top-3 risk surfaces from the roadmap or archive,
- a recommended tool's `checked:` date is older than three months,
- the project's tech stack changes (new framework, new test runner),
- §7 negative-space no longer matches what the team believes.
