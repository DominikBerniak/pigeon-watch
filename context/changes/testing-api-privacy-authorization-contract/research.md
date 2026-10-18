---
date: 2026-10-18T18:26:44+02:00
researcher: Dominik Berniak
git_commit: af9e987bf5ecdd9668a6df00c16259913c3a2a78
branch: chore/close-cold-start-manual-rows
repository: 10xDevs
topic: "Ground rollout Phase 2 (API privacy and authorization contract) for Risks #2, #3 and #6 of test-plan.md"
tags: [research, codebase, api, privacy, authorization, enumeration, identity, rate-limiting, integration-tests]
status: complete
last_updated: 2026-10-18
last_updated_by: Dominik Berniak
---

# Research: Phase 2 grounding for Risks #2, #3 and #6

**Date**: 2026-10-18T18:26:44+02:00
**Researcher**: Dominik Berniak
**Git Commit**: af9e987bf5ecdd9668a6df00c16259913c3a2a78 (working tree also carries an uncommitted `test-plan.md` edit that marks Phase 1 `complete`)
**Branch**: chore/close-cold-start-manual-rows
**Repository**: 10xDevs

## Research Question

Ground rollout Phase 2 of `context/foundation/test-plan.md`. For Risks #2 (another user's email in a response), #3 (cross-user access and stale sessions) and #6 (email enumeration and brute force): ground the real failure path in code, verify or correct the response guidance, locate existing tests, identify the cheapest useful test layer, and flag speculative risks or misleading hot-spot evidence. Challenged assumptions: "display name rejects `@`, so it is safe", "a few spot checks cover it", "authenticated means authorized", "a 401 without a token covers ownership", "the generic error message means no leak", "the rate limit is configured, so it works".

## Evidence grades

- **Code** — read in the repository (by this session or by a worker whose anchor was spot-checked by this session). Anchors are `path:line` from the repo root.
- **Worker** — read by a research worker only; not re-read here.
- **FW** — behaviour of framework code that is not in this repository (ASP.NET Core Identity, bearer handler, rate limiter). It comes from archive citations and framework knowledge. Nothing was built or run, so every FW statement is unverified until a test observes it.

## Summary

1. **The test plan's Phase 2 wording needs four corrections before planning.**
   - There is **no server-side logout**, so "old tokens stop working after logout" cannot be proven; it is an accepted decision (Code: `context/archive/2026-10-04-register-and-login/plan.md:71`; grep of `PigeonWatch/Api` `*.cs` for `logout|BearerToken|SecurityStampValidator|AddAuthorization|FallbackPolicy` returned no matches).
   - After a password change the old **access** token keeps working until its own expiry; only the old **refresh** token is rejected (Worker + FW).
   - "User B cannot modify user A's records" cannot be tested against sightings today: no sighting update, delete or status endpoint exists (Code: `PigeonWatch/Api/WebApi/Controllers/SightingsController.cs:24-78`, four actions, one `POST`).
   - "Registration responses are indistinguishable for existing and new emails" does not hold as stated: a taken display name changes the answer (see Risk #6).
2. **Risk #2 holds on today's code.** No response model in the inspected endpoint set carries another user's email (Worker, with `SightingModel` and the sighting mapper confirmed in Code). The weak point is future surfaces. Spot checks of today's fields cannot catch those, and an `ApiExplorer`-based endpoint sweep would skip the 404-stubbed Identity routes.
3. **Risk #6 has one genuine oracle the docs do not mention.** `DuplicateDisplayName` versus `RegistrationFailed` can reveal whether an email is registered, because the display-name check runs in the user store after Identity's duplicate-email validation (Code for the store half; FW for the validator ordering).
4. **The cheapest layer for all three risks is the existing API integration harness.** It needs token handling, a bearer-aware client, endpoint enumeration and a controllable clock added; none exist today.
5. **Hot-spot evidence is partly misleading for Risk #3.** The token and stamp lifecycle lives in Identity defaults wired in `PigeonWatch/Api/DependencyInjection` and in the framework, not in `PigeonWatch/Api/WebApi` (the 20 commits/30d hot-spot directory).

Status is `partial`: the code-side questions are answered, but the FW behaviours listed under Open Questions need one throwaway probe each before a plan relies on them.

## Detailed Findings

### Risk #2 — another user's email in a response

**Endpoint inventory (Code unless marked).** Mapped by `app.MapControllers()` and `app.MapPigeonWatch()` (`PigeonWatch/Api/WebApi.Host/Program.cs:78,80`).

| Route | Auth | Success body |
|---|---|---|
| `POST account/register` | anonymous, rate limited | `{email, displayName}` echoing the submitter's own trimmed input (Worker) |
| `PUT account/profile` | `[Authorize]` | `{email, displayName}` of the caller (Worker) |
| `PUT account/password` | `[Authorize]`, rate limited | `{changed}` (Worker) |
| `GET configuration/client` | anonymous | validation and sighting rules (Worker) |
| `GET configuration/general` | `[Authorize]` | caller's own id, email, display name, roles; claim-scoped (Worker) |
| `GET resources/{culture}` | anonymous | UI labels (Worker) |
| `POST sightings`, `GET sightings/{id:guid}`, `GET sightings/nearby`, `GET sightings/mine` | class-level `[Authorize]` (`SightingsController.cs:13-15`) | `SightingModel` |
| `POST auth/login`, `POST auth/refresh` | rate-limited group (`PigeonWatchEndpointRouteBuilderExtensions.cs:17-20`) | Identity token response (FW) |

- `SightingModel` carries `ReporterDisplayName` and no reporter id, no email and no audit field (Worker, `PigeonWatch/Api/WebApi/Models/SightingModel.cs`). The display name is read from `entity.Reporter.DisplayName` (Code: `PigeonWatch/Api/Data/Mappers/SightingMapper.cs:34`). Of the four sighting reads, `nearby` returns other users' rows in bulk (Worker, `SightingRepository.cs:35-58`).
- **The un-exposed Identity routes are stubbed, not removed.** A `.Finally` convention replaces the request delegate of every route except `POST auth/login` and `POST auth/refresh` with a bare 404 and adds `ExcludeFromDescriptionAttribute` (Code: `PigeonWatchEndpointRouteBuilderExtensions.cs:32-43`). Consequences: they stay in the `EndpointDataSource`, and an `ApiExplorer`-based enumeration silently skips them. If the stub regressed, Identity's `manage/info` would return the caller's email (FW). No test touches these stubs (Worker: grep for `MapPigeonWatch`, `manage/info`, `EndpointDataSource`, `ApiExplorer` hit only source and docs).
- **Username equals email.** `AccountRepository.CreateAsync` sets `Email` and `UserName` to the same value (Code: `PigeonWatch/Api/Data/Repositories/AccountRepository.cs:17-22`), so the Identity principal's name claim would carry the email (FW). Today's code reads only `ClaimTypes.NameIdentifier` for audit (Worker: `HttpContextCurrentUserProvider.cs:17-19`); any new code that serialises `User.Identity.Name` or the claims would leak it.
- **Error bodies.** Duplicate-email and duplicate-username Identity errors are collapsed to `RegistrationFailed` with the fixed text "The account could not be created." (Code: `AccountRepository.cs:10,85-101`). `InvalidEmail` keeps Identity's description, which echoes the submitter's own input, not another user's (Worker + FW). `DuplicateDisplayName` echoes the submitted display name (Worker: `PigeonWatchUserStore.cs:317-324`).
- **Audit columns** hold the user id or `SYSTEM`, never the email (Worker: `AuditSaveChangesInterceptor.cs:32,39-49`; `Api/CLAUDE.md` states the same). No business object or API model carries `CREATE_USER` or `UPDATE_USER` (Worker, inspected set: `Sighting` and the account models).
- **Logs** are the one non-response path that could carry an email: a SQL Server duplicate-key message embeds the duplicate value. This is unreachable on the SQLite harness and unverified; treat as speculative (Worker).

**Challenge 1: "display name rejects `@`, so it is safe".** The rule is a single ordinal `Contains('@')` in `PigeonWatch/Api/BusinessLogic/Services/AccountService.cs:62-67`, reached from register (`:16`) and rename (`:28`). It is not an Identity validator, an entity rule or a database constraint (Worker: `UserAccountEntity.DisplayName` has only `[MaxLength]`). What it does not cover, from the inspected code:
- any writer of `DisplayName` that bypasses `AccountService` — `AccountRepository` and `PigeonWatchUserStore` are public and do not enforce it (Worker);
- look-alike characters such as the fullwidth at-sign pass an ordinal `'@'` check (inference, not run);
- obfuscated or non-email contact details within 3-30 characters (Worker);
- other free text shown to other users: sighting `Name` (60), `Description` (1000) and `Behaviour` (500) are returned verbatim by `nearby` and `{id}` with no `@` or contact check (Worker: `CreateSightingRequestModel.cs:8-17`, `SightingViewModelCreator.cs:14-16`). That is user self-disclosure, not a system leak, but it shows the display-name rule is not the barrier for those fields. The structural barrier is the response contract: user responses contain no user-table field except display name.
- The SPA hard-codes the `@` rule (Worker: `PigeonWatch/Frontend/src/app/shared/validation/account-validation.ts:28-31`); the API does not serve it (`DisplayNameRulesModel` has min and max length only).

**Challenge 2: "a few spot checks cover it".** Contradicted by the existing test base:
- the only HTTP-level privacy assertion is the repeat-register body check (Code: `PigeonWatch/Api/IntegrationTests/RegisterContractTests.cs:35-50`, one endpoint, one status, one input);
- `Sighting_model_exposes_no_reporter_id_or_email_and_no_business_object_types` is reflection-only and covers `SightingModel` alone (Code: `PigeonWatch/Api/UnitTests/SightingsControllerTests.cs:198-205`);
- the duplicate-error mapping test feeds a mocked `UserManager` (Worker: `AccountRepositoryTests.cs:60-73`);
- no integration test calls an authenticated endpoint (Worker: the project has five files; none sends a bearer token).
A new endpoint only gets coverage if a sweep discovers it, which is what the plan's "extends to every new endpoint" requires.

**Anti-pattern check: "asserting only the fields the code currently returns".** The existing reflection test and the repeat-register assertion are exactly that shape. A contract sweep should search decoded response text, headers and `Set-Cookie` for the seeded email and its upper-case normalised form, not whitelist fields. `System.Text.Json` escapes `+` as `+` by default, so a raw-substring search for an email containing `+` can miss it (Worker; general behaviour, not run); decode the JSON values before searching.

**Cheapest layer.** API integration on the real pipeline. Cheapest sweep design (Worker, design only):
1. seed users A and B (A's email with a `+` tag and mixed case), log both in, give A one sighting (the 60 s cooldown makes more than one per user need a fake clock);
2. enumerate `EndpointDataSource.Endpoints.OfType<RouteEndpoint>()` rather than `ApiExplorer`, so the stubbed Identity routes are included;
3. call each route anonymously, as B and as A with route parameters substituted and a few malformed inputs;
4. assert no response body, header or cookie contains A's email, its normalised form or the username, unless the caller is A;
5. add a coverage guard that fails when an enumerated route has no fixture and no explicit exemption, so a new endpoint cannot be silently skipped.
Valid request bodies are not derivable automatically; register, profile and sighting bodies need a small hand-written fixture list.

**Speculation flag.** The roadmap surfaces named as the likelihood source (S-04, S-07, S-08) are `proposed`/not built (Worker: `roadmap.md:52`), so their future response contracts are unknowable now. Phase 2 should prove the current surface and install the guard that makes new surfaces fail loudly.

### Risk #3 — cross-user access and stale sessions

**Acting user and record scoping (Code unless marked).**
- Controllers read the acting user inline from `Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), ...)` and return 401 otherwise (`PigeonWatch/Api/WebApi/Controllers/SightingsController.cs:27-28,72-73`; Worker for the account and configuration controllers). `ICurrentUserProvider` is used only by the audit interceptor (Worker: `HttpContextCurrentUserProvider.cs:10-20`). The plan's "avoid mocking the current-user provider" is therefore moot for ownership: ownership never goes through that provider, and the real pipeline exercises the claim path.
- Per record:
  - `USER_ACCOUNT`: reads and writes are claim-scoped; no endpoint takes a user id from route, query or body (Worker).
  - `SIGHTING` reads: `GET sightings/{id}` and `GET sightings/nearby` return any user's sighting to any authenticated user, by design (Worker: `SightingRepository.cs:25-33`; PRD Access Control, `context/foundation/prd.md:160`, says unauthenticated visitors cannot view sightings and logged-in members share a flat permission set). `GET sightings/mine` is scoped to the caller (Worker: `SightingRepository.cs:60-71`).
  - `SIGHTING` writes: only `POST`, with `ReporterId` taken from the claim (Code: `SightingsController.cs:30`); the request model has no reporter or ownership field (Worker).
- **There is no cross-user write surface for sightings today.** Edit, status change and admin removal are roadmap S-09, S-10 and S-11, all `proposed` (Worker: `roadmap.md:53-55`).
- **What a "B versus A" test can honestly assert today:** (a) B's `mine` never lists A's sightings and vice versa; (b) B's `PUT account/profile` and `PUT account/password` change only B (A's display name and password are unchanged and A still logs in with the old password); (c) B's `POST sightings` is attributed to B; (d) B can read A's sighting by id, which is intended and should be documented as a test of the by-design rule, not forbidden. The "modify" half for sightings must be re-run when S-09 to S-11 land.
- **Challenge "authenticated means authorized" / "a 401 without a token covers ownership".** Partly right: ownership is enforced by construction (claim-scoped queries), and by-id and nearby reads are deliberately open. The real exposure is the day an endpoint takes an id for a write; the first such endpoint is the first place ownership can fail. A deleted user's token still returns 200 on the three sighting reads that do not look the user up (Worker: `mine` returns an empty list), while the account and create endpoints map a missing user to 401 (Worker: `ConfigurationController.cs:40`, `AccountController.cs:46,65`, `SightingsController.cs:33`). Not a leak; no delete endpoint exists to reach it through the API.
- **Existing tests do not exercise ownership through the pipeline.** The controller unit tests build a `ClaimsPrincipal` by hand and mock the service (Worker: `SightingsControllerTests.cs:51-65,171-183`). They bypass `[Authorize]` and the middleware. The only real scoping test is repository-level on SQLite (Worker: `SightingRepositoryTests.cs:145-165`). No architecture test checks `[Authorize]` presence (Worker: `ControllerActionTests` covers return types only).

**Token lifecycle.**
- The app registers `AddIdentityApiEndpoints<ApplicationUser>` with the custom user store and configures no bearer, stamp-validator or authorization options (Code: `PigeonWatch/Api/DependencyInjection/AccountServiceCollectionExtensions.cs:12-29`; the grep above found no `BearerToken`, `SecurityStampValidator`, `AddAuthorization` or `FallbackPolicy` in the API `*.cs` files). Bearer options are the framework defaults: access 1 hour, refresh 14 days (Code: archive `external-research.md:46`, `plan.md:251`). Tokens are opaque, protected by the Data Protection key ring (`Api/CLAUDE.md`).
- **Logout does not exist on the server.** The only Identity routes kept are login and refresh (Code: `PigeonWatchEndpointRouteBuilderExtensions.cs:14,32-43`); the SPA's logout clears tokens locally and makes no API call (Worker: `PigeonWatch/Frontend/src/app/core/auth/session.service.ts:128-131`). The archive records this as decision D1: "Logout discards the tokens on this device only. A stolen refresh token stays valid until it expires" (Code: `context/archive/2026-10-04-register-and-login/plan.md:71`).
  - **Correction for the plan:** test-plan.md §2 Risk #3 and its guidance row (and `change.md` Notes) assume logout invalidates tokens. A test asserting that would fail on today's code, and the behaviour is accepted, not a bug.
- **Access token versus password change.** The bearer handler only unprotects the ticket and checks expiry; it does not re-validate the security stamp per request (FW, per archive: `context/archive/2026-10-10-edit-profile-name/plan.md:45`, "Bearer tokens are not revocable... other sessions end when their access token expires and the refresh is rejected"). So an old access token should keep returning 200 after a password change until its own `ExpiresUtc`.
- **Refresh token versus password change.** `ChangePasswordAsync` rotates the security stamp (FW; Code: the store implements `IUserSecurityStampStore` at `PigeonWatchUserStore.cs:17` and persists the stamp per `UserAccountMapper.CopyToEntity`, Worker), and `POST auth/refresh` validates the stamp (FW), so a refresh token issued before the change should get 401 at its next refresh. A rename does not rotate the stamp (Worker: `UpdateDisplayNameAsync` calls `UpdateAsync` only), so other sessions survive a rename.
- **Corrected assertion set for the plan:** (i) old access token still 200 immediately after a password change; (ii) old refresh token 401 immediately; (iii) after the clock passes access-token expiry, the old access token gets 401. Wording "old tokens stop working once the access token expires" is accurate only in this sense, and only for the password-change case.
- **Time control (unverified).** `TimeProvider.System` is registered with `TryAddSingleton` (Code: `PigeonWatch/Api/DependencyInjection/DataServiceCollectionExtensions.cs:20`), so a factory can replace it; the sighting cooldown and the audit interceptor use it (Worker). Access-token expiry in the bearer handler is believed to use `BearerTokenOptions.TimeProvider` (FW), not the DI provider, which would require `PostConfigure<BearerTokenOptions>` on the factory; shortening `BearerTokenExpiration` the same way is the alternative. `ManualTimeProvider` exists only in the UnitTests assembly, which the integration project does not reference (Worker: `UnitTests/ManualTimeProvider.cs:3-11`). Neither the DI route nor the options route has been tried.

**Cheapest layer.** API integration with two users on one factory. The harness gives distinct client addresses per client so the shared 10/min limiter does not interfere unless one client exceeds 10 requests (Code: `PigeonWatch/Api/IntegrationTests/PigeonWatchApiFactory.cs:25-32`).

### Risk #6 — enumeration and brute force

**Register responses (Worker unless marked).**
- Success: 200 `{email, displayName}`. Any failure: 400 `ValidationProblemDetails` keyed by error code. Success versus failure is inherently distinguishable; the leak question is which 400 for which input.
- Check order: model binding, then `AccountService` trims and validates email length and display name (returns before any repository call, so those 400s do not depend on existence), then `UserManager.CreateAsync`, which hashes and validates the password and runs Identity's duplicate validation before calling the store (FW), then the store's display-name check and insert (Code: `PigeonWatch/Api/Data/Identity/PigeonWatchUserStore.cs:26-31`).
- Existing email → 400 `["RegistrationFailed"]` only, no email or display name in the body, one account row, original password still works (Code: `RegisterContractTests.cs:35-50`, `:53-67` for a different password).
- **Oracle found.** Register with a taken display name D:
  - existing email → `RegistrationFailed` (Identity's duplicate-email validation fails first; the store's display-name check is not reached) — FW ordering;
  - new email → `DuplicateDisplayName`, specific by design (`PigeonWatch/CLAUDE.md:10` hard rule) — Code for the store, Worker for the mapping.
  
  No account is created in either case, so the probe repeats at the per-IP limit. The documented reasoning for keeping `DuplicateDisplayName` specific ("display names are public") does not consider this interaction. Result: "registration responses are indistinguishable for existing and new emails" is false unless the claim is scoped to a freshly chosen, valid display name. This is the concrete answer to the challenge "the generic error message means no leak".
- Case and whitespace variants: the service trims edges (Code: `AccountService.cs:12`); a case variant of an existing email should also yield `RegistrationFailed` via the normalised lookup (FW, inferred, untested).

**Login (stock `MapIdentityApi`, FW unless marked).**
- Failure is 401 with detail `Failed` or `LockedOut`; unknown email and wrong password both give `Failed`. There is no dummy-hash or timing equalisation in the repo, and the archive accepted that explicitly (Code: `context/archive/2026-10-04-register-and-login/plan.md:85`). A CI timing assertion would be flaky and the plan's "error shapes and timing" should drop timing.
- **Lockout is a second, accepted oracle.** It exists only for real accounts: at most 5 wrong passwords against one email reveal `LockedOut` for an existing account and never for an unknown one (Code: `AccountServiceCollectionExtensions.cs:20-22` for 5 attempts, 5 minutes; `PigeonWatch/CLAUDE.md:10` records "Login keeps the `LockedOut` detail (accepted)"). The same mechanism lets an attacker lock a known victim for 5 minutes with 5 requests; the docs do not mention that consequence.
- `PUT account/password` does not touch the lockout counter (FW), so a stolen access token can guess the current password limited only by the per-IP limiter; this is outside Risk #6 as written but belongs in the same record.

**Rate limiter (Code: `PigeonWatch/Api/WebApi.Host/Program.cs:37-54,72-76`).**
- One policy, fixed window: 10 permits per minute, no queue, partitioned by `RemoteIpAddress`; `/auth/refresh` is exempt by path suffix; status 429 with no `OnRejected` handler (so likely an empty body and no `Retry-After`, FW).
- Applied to `POST account/register`, `PUT account/password` and the whole `auth` group (Worker; group at `PigeonWatchEndpointRouteBuilderExtensions.cs:19`). Register, login and password change share one bucket per IP. The 404-stubbed Identity routes sit in the group, so they also consume permits.
- Middleware order is `UseAuthentication`, `UseRateLimiter`, `UseAuthorization` (Code: `Program.cs:72-76`), so an unauthenticated `PUT account/password` consumes a permit and can answer 429 before 401.
- There is no `UseForwardedHeaders` in code; forwarded headers rely on the App Service setting `ASPNETCORE_FORWARDEDHEADERS_ENABLED=true`, set manually (Code: `context/archive/2026-10-04-register-and-login/plan.md:310`). It cannot be observed through the harness, which sets `RemoteIpAddress` directly from `X-Test-Client-Address` (Code: `IntegrationTests/ClientAddressStartupFilter.cs:12-27`). Do not assert spoof-resistance in tests.

**Challenge "the rate limit is configured, so it works", and the order of limiter and lockout.** Lockout fires before the limiter from a single client: one register, five wrong logins (the fifth already answers `LockedOut`, FW) and one correct login are 7 permits, under 10 (arithmetic on the Code constants above). The 11th request in the window is 429, and a 429 does not reach the login handler, so it cannot increment the lockout counter. So the guidance's implied premise that the limiter might pre-empt lockout is false in the one-client case; a lockout test only has to budget at most 10 requests. Brute force across many addresses bypasses the per-IP limiter but not the per-account lockout. A test should observe the 11th-request 429 and the lockout response, not the configured numbers (anti-pattern: "asserting configuration values").
- The limiter has no `TimeProvider` seam, so a test can observe the 429 but cannot advance the window (a new factory resets it). Lockout expiry through a fake clock is doubtful, since the lockout end is believed to be computed from the framework's own clock (FW, unverified); setting the lockout-end column through the harness's held-open SQLite connection is an untried alternative.

**Existing tests (Worker).** Only `RegisterContractTests` exists at the HTTP level (three tests: register then login, repeat register, repeat with a different password). Nothing covers login failure shapes, lockout, 429, case or whitespace variants, display name with an existing email, refresh or password change. `AccountRepositoryTests.cs:84-91` passes `[identityCode, DuplicateDisplayName]` together, a combination the real flow should not produce. No test asserts configuration values; the gap is missing behavioural coverage, not config-mirroring.

**Recovery login (Worker, archive `2026-10-18-testing-cold-start-registration-resilience`).** It is client-side only, sends the typed credentials to the same `/auth/login`, and adds no server-side oracle; it costs one extra permit from the shared bucket.

## Code References

- `PigeonWatch/Api/WebApi.Host/Program.cs:37-54` — `auth` fixed-window policy: 10 per minute per remote IP, refresh exempt, 429, no `OnRejected`
- `PigeonWatch/Api/WebApi.Host/Program.cs:72-76` — middleware order: authentication, rate limiter, authorization
- `PigeonWatch/Api/DependencyInjection/AccountServiceCollectionExtensions.cs:12-29` — Identity options: lockout 5 / 5 min, unique email, no bearer or stamp options
- `PigeonWatch/Api/DependencyInjection/PigeonWatchEndpointRouteBuilderExtensions.cs:14-43` — only `auth/login` and `auth/refresh` exposed; other Identity routes stubbed to 404 and hidden from `ApiExplorer`
- `PigeonWatch/Api/DependencyInjection/DataServiceCollectionExtensions.cs:20` — `TimeProvider.System` registered with `TryAddSingleton`
- `PigeonWatch/Api/Data/Repositories/AccountRepository.cs:17-22,60-75,77-101` — `UserName = Email`; password change; duplicate-error collapse to `RegistrationFailed`
- `PigeonWatch/Api/Data/Identity/PigeonWatchUserStore.cs:26-31` — store runs the display-name check before inserting
- `PigeonWatch/Api/BusinessLogic/Services/AccountService.cs:62-67` — the only `@` check on display names
- `PigeonWatch/Api/WebApi/Controllers/SightingsController.cs:13-78` — four sighting actions, class-level `[Authorize]`, claim-derived user, no update or delete
- `PigeonWatch/Api/Data/Mappers/SightingMapper.cs:34` — `ReporterDisplayName` from the reporter entity
- `PigeonWatch/Api/IntegrationTests/PigeonWatchApiFactory.cs:25-32,47-56` — per-client address, DbContext swap, startup filter
- `PigeonWatch/Api/IntegrationTests/ClientAddressStartupFilter.cs:12-27` — sets `RemoteIpAddress` from `X-Test-Client-Address`
- `PigeonWatch/Api/IntegrationTests/RegisterContractTests.cs:35-67` — the only HTTP-level privacy and enumeration assertions
- `PigeonWatch/Api/UnitTests/SightingsControllerTests.cs:198-205` — reflection-only `SightingModel` check

## Architecture Insights

- The acting user is a claim read inline per action; ownership is enforced by claim-scoped queries, not by an authorization policy or a fallback policy. A new endpoint that takes an id has no framework guard unless it adds one.
- Privacy is structural (response models never include user-table fields except display name) plus a collapse of identifying Identity errors. There is no output filter, so only a sweep that decodes and searches real responses can notice a regression.
- The enumeration defences are the per-IP limiter and per-account lockout; the documented oracles (`LockedOut`, taken display name) are accepted trade-offs. The display-name oracle is the one that interacts with registration of unknown emails.
- Session invalidation is by stamp rotation at refresh time only; there is no revocation list and no logout call.

## Historical Context (from prior changes)

Each historical statement is scored against the current code.

- `context/archive/2026-10-04-register-and-login/plan.md:71` — "logout discards tokens on this device only": **supported** (no logout route, no API call in the SPA).
- `context/archive/2026-10-04-register-and-login/plan.md:85` — no full enumeration protection, limiter and lockout bound probing, no dummy-hash fix: **supported**; the display-name interaction is **not addressed** there.
- `context/archive/2026-10-04-register-and-login/external-research.md:46` — bearer defaults access 1 hour, refresh 14 days: **supported in the code** (no overriding options registered); the numbers themselves are FW.
- `context/archive/2026-10-10-edit-profile-name/plan.md:34,45` — password change rotates the stamp and ends other devices at next refresh; access tokens not immediately revoked: **partly supported** (store and mapper persist the stamp; rotation and the refresh check are FW).
- `PigeonWatch/CLAUDE.md:9` — "email is returned only to its owner (`GET /configuration/general`)": **supported** on the inspected response set.
- `context/archive/2026-10-18-testing-cold-start-registration-resilience/` — Phase 1 harness and the recovery-login decision: **supported**; `test-plan.md` §6.3 matches `PigeonWatchApiFactory` and `ClientAddressStartupFilter`.

## Related Research

- `context/archive/2026-10-18-testing-cold-start-registration-resilience/research.md` — Phase 1 research on register paths and the harness

## Open Questions

Facts a plan should resolve with one throwaway probe each before depending on them (all FW, unverified):
1. Does replacing the DI `TimeProvider` age bearer access tokens, or is `PostConfigure<BearerTokenOptions>` required? Which one lets a test pass the 1-hour expiry?
2. Does a password change reject the old refresh token and keep the old access token alive, as the archive says?
3. Is the `DuplicateDisplayName` versus `RegistrationFailed` difference observable end to end, for a new email and for an existing email with the same taken display name?
4. What exact status and body do the 404-stubbed Identity routes give anonymously and when authenticated (401, 404 or 429), and what does a 429 and a generic 500 look like?
5. Does the fifth wrong login already return `LockedOut`, and can lockout expiry be driven through the SQLite connection?
6. Does a case variant of an existing email return `RegistrationFailed`?

Product decisions the plan must settle with the user (research does not decide them):
- Treat the display-name oracle as a defect to fix, an accepted trade-off to document and pin in a test, or out of scope for Phase 2.
- Whether the "logout" clause of Risk #3 is dropped, or rewritten to pin the current accepted behaviour.
- Whether free-text fields visible to other users need a contact-detail rule (currently none).

Backport candidates for `test-plan.md` §2 (wording and source only, never anchors): the logout clause and the access/refresh distinction in Risk #3; the "modify" half being account-only until S-09 to S-11; the display-name oracle and the dropped timing clause in Risk #6; the stubbed-route sweep and free-text note in Risk #2; and a note that the hot-spot directory `PigeonWatch/Api/WebApi` is only partial evidence for Risk #3.

## Probe Outcomes

Observed on 2026-10-18 with throwaway xunit probes on the real pipeline (`WebApplicationFactory<Program>`, SQLite in-memory, .NET 10). Every statement below was observed in a run, not inferred. The probe class is deleted; the answers live here.

### FW1 — Clock control for access-token expiry

- **Route (a), replace the DI `TimeProvider` with a manual clock: works.** Login at manual time T, `GET configuration/general` returned 200; after advancing the manual clock 61 minutes the same access token returned 401. The login response reports `expiresIn: 3600`.
- Route (b), `PostConfigure<BearerTokenOptions>` with the manual clock as the options `TimeProvider`: also works (200 before, 401 after +61 minutes).
- Route (c), `PostConfigure<BearerTokenOptions>` with `BearerTokenExpiration` of 1 s and a 1.5 s real delay: also works (200 before, 401 after).
- With route (a), `POST auth/refresh` with the pre-advance refresh token still returned 200 after +61 minutes (refresh lifetime is 14 days).
- **Decision:** route (a) wins as the first route in the plan's order. The Phase 4 expiry assertion stays in scope and needs no real delay. The committed harness keeps only (a): `PigeonWatchApiFactory` takes an optional `TimeProvider`, replaces the DI registration with it, and `ManualTimeProvider` (`SetUtcNow`, `Advance`) lives in the integration project. The same clock also controls the 60 s sighting cooldown, so one user can report more than one sighting in a test (a second create without advancing was refused with 400; after `Advance(61 s)` it succeeded). The options routes (b) and (c) are not kept.

### FW2 — Password change versus old tokens

- Control: `POST auth/refresh` with a freshly issued refresh token returned 200 with `{tokenType, accessToken, expiresIn, refreshToken}`.
- Rename (`PUT account/profile`) returned 200 `{email, displayName}`; a refresh token issued before the rename still refreshed with 200. A rename does not end a session.
- Password change (`PUT account/password`) returned 200 `{"changed":true}`.
- After the change: the old access token on `GET configuration/general` returned **200**; the access token from the original login also **200**.
- After the change: `POST auth/refresh` with a refresh token issued before the change returned **401** with an empty body and `WWW-Authenticate: Bearer`; the original login's refresh token also **401**.
- Login with the old password returned 401 with detail `Failed`; login with the new password returned 200.
- **Decision:** the archive's claim holds exactly. Phase 4 asserts old refresh 401 and old access 200 right after the change, then old access 401 after `Advance` past one hour (FW1). Both controls (plain refresh 200, refresh after a rename 200) are confirmed observable.

### FW3 — Display-name oracle

With A registered as `alpha@example.com` and display name `TakenName` (200):

| Request | Answer |
|---|---|
| new email, `TakenName` | 400 `{"errors":{"DuplicateDisplayName":["Display name 'TakenName' is already taken."]}}` |
| existing email, fresh name | 400 `{"errors":{"RegistrationFailed":["The account could not be created."]}}` |
| existing email, `TakenName` | 400 `{"errors":{"RegistrationFailed":["The account could not be created."]}}` |
| new email, `takenname` (case differs) | 400 `DuplicateDisplayName`, description echoes the submitted `takenname` |
| new email, ` TakenName ` (padded) | 400 `DuplicateDisplayName`, description echoes the trimmed `TakenName` |
| new email, fresh name | 200 |

Account rows after all attempts: 2 (A and the one fresh success). The 400 bodies share status, title, type and shape and differ only in the error code and description.

- **Decision:** the oracle is real. Taken display name with a new email answers `DuplicateDisplayName`; the same name with an existing email answers `RegistrationFailed`. Phase 5 proceeds as planned; its red test compares exactly these two answers. The duplicate check is case-insensitive and trim-aware, so the Phase 5 edge cases (case and padding) are observable.

### FW4 — Stubs, 429 and 500 shapes

Routed set (from `EndpointDataSource`): ten controller routes plus ten `auth/*` routes: `POST auth/register`, `POST auth/login`, `POST auth/refresh`, `GET auth/confirmEmail`, `POST auth/resendConfirmationEmail`, `POST auth/forgotPassword`, `POST auth/resetPassword`, `POST auth/manage/2fa`, `GET auth/manage/info`, `POST auth/manage/info`. Route patterns come back without a leading slash (`account/register`, `auth/login`).

- Stubbed routes, anonymous and signed in, with a fresh address each:
  - `POST auth/register`, `GET auth/confirmEmail`, `POST auth/resendConfirmationEmail`, `POST auth/forgotPassword`, `POST auth/resetPassword`: **404, no headers, empty body**, signed in or not.
  - `POST auth/manage/2fa`, `GET auth/manage/info`, `POST auth/manage/info`: anonymous **401** with `WWW-Authenticate: Bearer` and an empty body (the authorization middleware runs before the stub delegate); signed in **404** with an empty body.
- **429**: ten `POST auth/login` requests from one address answered 401 `Failed`; the 11th answered **429 with no headers and an empty body** (no `Retry-After`, no ProblemDetails).
- **500 (forced)**: closing the held-open SQLite connection and then calling `GET sightings/mine` with a valid token answered **500** with headers `Cache-Control: no-store, no-cache`, `Pragma: no-cache`, `Expires: -1`, `Content-Type: application/problem+json; charset=utf-8` and body `{"type":"https://tools.ietf.org/html/rfc9110#section-15.6.1","title":"An error occurred while processing your request.","status":500,"traceId":"..."}`. It is cheap to force, so the error-path case stays in Phase 2. The body carries no `detail`.
- **Unexpected finding, malformed login and refresh bodies answer 500.** `POST auth/login` with an empty body, `not json`, `{}`, `null`, `{"email":null,"password":null}` or `{"email":"a@b.c"}` (password missing) all answered **500** with the same generic ProblemDetails as above (no `detail`). `POST auth/refresh` with an empty body, `not json`, `{}`, `null` or a login-shaped body also answered **500**, while `{"refreshToken":null}` and `{"refreshToken":"garbage"}` answered 401 (empty body, `WWW-Authenticate: Bearer`). `POST account/register` with the same malformed bodies answers a clean 400 ValidationProblemDetails naming the missing properties. So the Identity routes turn a body that fails binding into a 500 instead of a 400; this is a pre-existing behaviour, not a leak (the body names nothing).
  - Anonymous calls to protected routes (`GET sightings/mine`, `GET configuration/general`, `GET sightings/nearby`, `GET sightings/{id}`, `PUT account/password`) answered 401 with `WWW-Authenticate: Bearer` and an empty body, even when the query was invalid (authorization precedes validation). Signed in, an out-of-range latitude answered 400 `{"errors":{"Latitude":[...]}}` and an unknown sighting id answered 404 ProblemDetails; `GET resources/xx` answered 200 with the English map and `Content-Language: en`.
- **Decision:** Phase 2 fixtures send well-formed bodies to `auth/login` and `auth/refresh` (the sweep fails on an unexpected 5xx), and the malformed variants apply to `account/register`, `PUT account/profile`, `PUT account/password` and `POST sightings`, which answer 400. The malformed-body 500 on login and refresh goes into §7 as a known deviation and is flagged for the user (candidate product fix, out of scope for this change unless decided otherwise). The stub assertion is: the five always-404 routes give 404 with an empty body for both callers; the three `auth/manage/*` routes give 401 empty anonymous and 404 empty signed in. 429 is asserted as a status with an empty body; no `Retry-After` is promised.

### FW5 — Lockout

One registered account, five wrong logins from five different addresses:

| Attempt | Answer |
|---|---|
| 1 to 4 | 401 ProblemDetails, `detail: "Failed"` |
| 5 | 401 ProblemDetails, `detail: "LockedOut"` |

- The fifth wrong login already answers `LockedOut`. The correct password afterwards also answers 401 `LockedOut`.
- Unknown email: six wrong logins (against an email that does not exist) all answered `Failed`; `LockedOut` never appears for an unknown email.
- `USER_ACCOUNT` after the lockout: `LOCKOUT_END` is a TEXT value in the form `2026-10-18 17:59:31.3226535+00:00` (about five minutes after the fifth failure), `LOCKOUT_ENABLED = 1`, `ACCESS_FAILED_COUNT = 0` (reset when the lockout starts).
- `UPDATE USER_ACCOUNT SET LOCKOUT_END = '2000-01-01 00:00:00+00:00'` on the held-open connection affected 1 row, and the next correct login returned **200 with tokens**.
- **Decision:** lockout expiry can be driven through the held-open connection, so the conditional Phase 6 expiry test is in scope (the §7 n/a note is not needed). The distributed lockout test is valid because the lockout counter is per account, not per address.

### FW6 — Case and whitespace variants

`User@Example.com` registered (200, body echoes `User@Example.com`). Then:

| Request | Answer |
|---|---|
| `user@example.com`, new name | 400 `{"errors":{"RegistrationFailed":["The account could not be created."]}}` |
| ` USER@EXAMPLE.COM `, new name | 400 `RegistrationFailed`, same body |
| `User@Example.com` again, new name | 400 `RegistrationFailed`, same body |

Account rows afterwards: 1.

- **Decision:** case and padding variants of an existing email are indistinguishable from an exact repeat, so Phase 6 can include them in the existing-email variant set. Together with FW3, the taken-display-name answer for a new email becomes identical to these once Phase 5 collapses it.
