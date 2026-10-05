<!-- IMPL-REVIEW-REPORT -->
# Implementation Review: Register and Log In (S-01)

- **Plan**: context/changes/register-and-login/plan.md
- **Scope**: Full plan (all four phases' code reviewed; live post-merge checks 2.8–2.13, 2.15, 4.7–4.9, 4.11 still pending)
- **Reviewed phases**: 1, 2, 3, 4
- **Date**: 2026-10-05
- **Verdict**: NEEDS ATTENTION
- **Findings**: 0 critical, 4 warnings, 5 observations

## Verdicts

| Dimension | Verdict |
|-----------|---------|
| Plan Adherence | WARNING |
| Scope Discipline | PASS |
| Safety & Quality | WARNING |
| Architecture | PASS |
| Pattern Consistency | WARNING |
| Success Criteria | PASS |

## Success criteria evidence

- `dotnet build PigeonWatch/Api/PigeonWatchApi.slnx`: succeeded, 0 warnings.
- Architecture tests: 42/42 passed. Unit tests: 46/46 passed.
- `dotnet ef migrations has-pending-model-changes`: no changes.
- `git grep -n "health/db" -- PigeonWatch .github`: no matches.
- `npm test -- --watch=false`: 21 files, 147 tests passed.
- `npm run build`: succeeded, initial bundle 365 kB. `staticwebapp.config.json` is in dist, `index.html` has no `onload=` and no Google Fonts, the styles define `--mat-sys-primary: var(--pw-color-primary)` and `.pw-stack`, and `package.json` pins Material/CDK `~22.2.1` with no `@angular/animations`.
- PR DominikBerniak/pigeon-watch#6: `architecture-tests` and `frontend-build-and-test` both pass (Progress 4.3 not yet ticked).
- Manual: the ticked items are consistent with the diff. The unticked items are all live or post-merge checks.

## Findings

### F1 — In-flight token refresh restores the session after logout

- **Severity**: ⚠️ WARNING
- **Impact**: 🔎 MEDIUM — real tradeoff; pause to reason through it
- **Dimension**: Safety & Quality
- **Location**: PigeonWatch/Frontend/src/app/core/auth/session.service.ts:102-118 (also :92-100, :138-141)
- **Detail**: `refreshTokens()` writes the response with `tap((response) => this.tokens.setTokens(response))`. `shareReplay({ refCount: false })` keeps the source subscribed. `logout()`/`expire()` only call `clearSession()` and never invalidate `refreshInFlight`. A refresh that completes after logout therefore writes the refresh token back to `localStorage`. The user sees `/login`, but the next reload silently restores the session, which matters on a shared computer. The same happens across tabs: tab B's refresh rewrites the token that tab A's logout removed, and tab A ignores that `storage` event because `newValue !== null`. No spec covers logout during a refresh.
- **Fix**: Keep a session generation counter that `clearSession()` increments and that `refreshTokens()` captures. The `tap` drops a response whose generation is stale, and `clearSession()` also sets `refreshInFlight = null`. Add a spec: start a refresh, log out, flush the refresh, and expect no token in storage.
  - Strength: Closes both the same-tab and cross-tab paths at the single place tokens are written, with no change to the interceptor contract.
  - Tradeoff: Adds a small amount of state to `SessionService`. The tab-B request that triggered the refresh then fails with 401 and calls `expire`, which is the desired outcome.
  - Confidence: HIGH — verified by reading the code; the race is deterministic once the timing lines up.
  - Blind spot: The server-side refresh token is not revoked anyway (accepted in D1), so this only restores the device-local logout guarantee.
- **Decision**: FIXED — `sessionGeneration` in `SessionService` (bumped and `refreshInFlight` reset by `clearSession()`, stale refresh filtered before `setTokens`, `finalize` clears only its own in-flight). Spec "drops a refresh that completes after logout" added; it fails without the fix, and 148/148 tests pass.

### F2 — Anonymous /resources/{culture} grows the culture cache without bound

- **Severity**: ⚠️ WARNING
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Safety & Quality
- **Location**: PigeonWatch/Api/BusinessLogic/Services/UiResourceService.cs:35
- **Detail**: `CultureInfo.GetCultureInfo(culture)` caches every culture it creates for the life of the process, and with ICU almost any well-formed tag (`qaa-AB`, `en-ZZ`) succeeds. The endpoint is anonymous and has no rate limit. Every URL is unique, so `max-age=300` does not help. The review agent measured about 800 B retained per unique tag, which over time is an unbounded memory sink on the 1 GB F1 instance. Each non-`en` tag also triggers a satellite-assembly probe.
- **Fix**: Resolve with `CultureInfo.GetCultureInfo(culture, predefinedOnly: true)`, which bounds the cache to the predefined cultures, and add a unit test where a made-up tag such as `zz-q1` resolves to `en`.
- **Decision**: FIXED — `predefinedOnly: true` in `UiResourceService`. `zz-q1` and `qaa-AB` were added to `Unsupported_cultures_resolve_to_english`; they pin the fallback but cannot observe the cache. Build has 0 warnings; 48/48 unit tests and 42/42 architecture tests pass.

### F3 — Email over 256 characters becomes a 500 and can log the address

- **Severity**: ⚠️ WARNING
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Safety & Quality
- **Location**: PigeonWatch/Api/BusinessLogic/Services/AccountService.cs:10-21
- **Detail**: `EMAIL` and `USER_NAME` are `nvarchar(256)`, but nothing checks length: Identity's email validator does not, and `AllowedUserNameCharacters = ""` disables the user-name check. A valid email over 256 characters reaches `SaveChangesAsync`. SQL error 2628 ("would be truncated") is not matched by the store's 2601/2627 filter, so it becomes a generic 500. SQL Server's 2628 message includes the truncated value, so the unhandled-exception log would likely contain the email. This was not reproduced against SQL Server.
- **Fix**: Add `AccountRules.EmailMaxLength = 256`, reject longer emails in `AccountService.RegisterAsync` with `InvalidEmail` before calling the repository, and add the boundary to `AccountServiceTests`.
- **Decision**: FIXED — `AccountRules.EmailMaxLength = 256`; `AccountService` combines `ValidateEmail` with the display-name errors and checks the length after trimming. Two boundary tests were added (257 rejected without calling the repository, 256 accepted). 50/50 unit tests and 42/42 architecture tests pass, and there are no pending model changes.

### F4 — Undocumented deviations and stale docs and Progress

- **Severity**: ⚠️ WARNING
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Plan Adherence
- **Location**: context/changes/register-and-login/change.md (Notes); PigeonWatch/Api/CLAUDE.md; PigeonWatch/CLAUDE.md; PigeonWatch/Frontend/CLAUDE.md
- **Detail**: Four justified deviations are recorded only in commit messages, not in the change.md Notes:
  1. Identity routes are blocked by a `.Finally` convention that replaces `RequestDelegate` (404 before body binding), not by an `AddEndpointFilter` lambda (`PigeonWatchEndpointRouteBuilderExtensions.cs:21-61`).
  2. `?useCookies=false` is accepted; only a truthy flag returns 400 (`:48-50`).
  3. The `'**'` route uses an `unknownRouteRedirect` function that keeps the `returnUrl` (`app.routes.ts:22-25`, `auth.guards.ts:26-42`), not `redirectTo: ''`.
  4. Cross-tab logout also fires on `localStorage.clear()` and navigates only when the user is authenticated (`session.service.ts:143-151`).

  The docs are stale in three places:
  - `Api/CLAUDE.md` still says "its filter returns 404… and 400 for useCookies/useSessionCookies".
  - `PigeonWatch/CLAUDE.md` still says "The frontend is still a placeholder shell… until the S-01 SPA phases land".
  - `Frontend/CLAUDE.md` still says "Until those phases land, some of the files below don't exist yet".

  Progress 4.3 (PR workflow green) is unticked although both PR #6 checks pass.
- **Fix**: Add one change.md Notes entry listing deviations 1–4, update the three CLAUDE.md sentences to match the code, and tick 4.3 in Progress.
- **Decision**: FIXED — Added a change.md note (deviations 1–4 plus the F1–F3 review fixes), updated the stale sentences in Api/CLAUDE.md, PigeonWatch/CLAUDE.md and Frontend/CLAUDE.md, and ticked Progress 4.3 (34c3f4a).

### F5 — Database-unavailable 503s leave no log entry of their own

- **Severity**: 💡 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Safety & Quality
- **Location**: PigeonWatch/Api/Data/Diagnostics/DatabaseUnavailableExceptionHandler.cs:19-39
- **Detail**: In .NET 10 the exception-handler middleware suppresses the unhandled-exception log when an `IExceptionHandler` returns `true`, and this handler has no logger. EF's own connection-error logs may still record the failure, but the 503 decision itself is invisible. The plan also lists `-2`/`258` (command timeout), so a slow query is shown as "Service warming up" and retried for up to 120 s.
- **Fix**: Inject `ILogger<DatabaseUnavailableExceptionHandler>` and log a Warning with the SQL error number before writing the 503.
- **Decision**: FIXED — The handler takes `ILogger<DatabaseUnavailableExceptionHandler>` and logs a Warning with the exception (the inner `SqlException` carries the number) before writing the 503. The tests assert one Warning for handled exceptions and no log for unhandled ones. Build has 0 warnings; 50/50 unit tests and 42/42 architecture tests pass. The `-2`/`258` classification is unchanged (it is in the plan).

### F6 — Identity route blocking and the cookie-flag guard have no automated test

- **Severity**: 💡 OBSERVATION
- **Impact**: 🔎 MEDIUM — real tradeoff; pause to reason through it
- **Dimension**: Safety & Quality
- **Location**: PigeonWatch/Api/DependencyInjection/PigeonWatchEndpointRouteBuilderExtensions.cs:21-61
- **Detail**: The most security-relevant lines in the branch are covered only by manual check 1.9: 404 for `/auth/register`, `/manage/*` and `/forgotPassword`, 400 for `useCookies`, and the refresh rate-limit exemption. A future `MapIdentityApi` version that adds an endpoint is blocked automatically. A refactor of the `.Finally` lambda that breaks blocking would go unnoticed, though. Disabled `manage/*` routes also answer 401 rather than 404 for anonymous callers, because their authorization metadata remains.
- **Fix A ⭐ Recommended**: Add a few `WebApplicationFactory` tests to `UnitTests` (with the DbContext swapped for SQLite) asserting the 404, 400 and refresh-exemption behaviour.
  - Strength: Locks down the security boundary in CI, where `api-pr-checks.yml` already runs `UnitTests`.
  - Tradeoff: Adds `Microsoft.AspNetCore.Mvc.Testing` and sits close to the "no API integration test project" decision in the plan's What We're NOT Doing.
  - Confidence: MED — needs a host-level SQLite override and may need a test-only connection string.
  - Blind spot: Not checked whether the Host's `Program` is visible to the test project (`public partial class Program`).
- **Fix B**: Keep the manual coverage and record the gap in change.md.
  - Strength: Respects the explicit user decision against integration tests.
  - Tradeoff: Regressions in route blocking are caught only by manual re-checks.
  - Confidence: HIGH — no code change.
  - Blind spot: Future slices may not re-run check 1.9.
- **Decision**: FIXED via Fix B — the gap and the 401-vs-404 behaviour are recorded in the change.md Notes ("Known test gap"), with a re-run trigger for manual check 1.9.

### F7 — Warm-up interceptor retries non-idempotent POSTs on status 0

- **Severity**: 💡 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Safety & Quality
- **Location**: PigeonWatch/Frontend/src/app/core/warmup/warmup.interceptor.ts:83-85
- **Detail**: Status 0 is retried like a 503, as the plan specified. Suppose a `POST /account/register` succeeded but its response was lost. The retry then gets the generic `RegistrationFailed`, so the user is told registration failed for an account that exists. A 503 from the handler is safe, because nothing was committed.
- **Fix**: For non-GET requests, retry only on 503, and let status 0 surface as the existing transport-failure message.
- **Decision**: SKIPPED — The proposed fix would regress the cold-start login warm-up: CORS-less platform errors reach the SPA as status 0, and login is a POST. The plan's status-0 retry stays.

### F8 — Bearer token attached by URL prefix without an origin boundary

- **Severity**: 💡 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Safety & Quality
- **Location**: PigeonWatch/Frontend/src/app/core/auth/auth.interceptor.ts:11 (same check in warmup.interceptor.ts:14)
- **Detail**: `request.url.startsWith(environment.apiUrl)` uses `apiUrl` without a trailing slash, so `https://pigeonwatch-api.azurewebsites.net.evil.tld/...` would also receive the bearer token. Nothing builds such a URL today, and CSP `connect-src` blocks other hosts in production, so this is defence in depth.
- **Fix**: Match `request.url === apiUrl || request.url.startsWith(apiUrl + '/')` in both interceptors.
- **Decision**: FIXED — Added `core/http/api-url.ts` `isApiUrl()` (exact `apiUrl` or `apiUrl + '/'` prefix), used by the auth and warm-up interceptors. Added the spec "does not add the header to a host that only starts with the API URL". 149/149 tests pass and the production build succeeds.

### F9 — Minor pattern inconsistencies

- **Severity**: 💡 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Pattern Consistency
- **Location**: PigeonWatch/Api/Data/Repositories/AccountRepository.cs:25,36; PigeonWatch/Frontend/src/app/app.scss:32; PigeonWatch/Frontend/src/app/features/auth/register-page/register-page.ts:60-70
- **Detail**:
  - `AccountRepository` builds `RegisteredAccount`/`CurrentUser` inline, while Api/CLAUDE.md says repositories map to business objects through a `Data/Mappers` mapper.
  - `app.scss` uses a raw `100dvh` length (the base styles use `100vh`). `style-token-coverage.spec.ts` only matches `px|rem|em`, so viewport units slip past the token rule.
  - The register page hard-codes the forbidden `@` character because `/configuration/client` does not expose it, so it can drift from `AccountRules`.
- **Fix**: Add `ToRegisteredAccount`/`ToCurrentUser` to `IUserAccountMapper`, add a `--pw-viewport-min-height` token and extend the spec's unit regex to `vh|dvh|vw`, and leave the `@` duplication as is unless a second forbidden character appears.
- **Decision**: FIXED — (a) `IUserAccountMapper.ToRegisteredAccount`/`ToCurrentUser` are used by `AccountRepository`, which now takes the mapper. (b) Added the tokens `--pw-viewport-min-height` (100vh: body, `.pw-center-screen`) and `--pw-viewport-min-height-dynamic` (100dvh: guest layout); the token spec now also flags `vh|dvh|svh|lvh|vw|vmin|vmax` literals. (c) The `@` duplication is left as is. Build has 0 warnings; 50 unit, 42 architecture and 149 Vitest tests pass, and the production build succeeds.

## Triage summary

- Fixed: F1, F2, F3, F4, F5, F6 (Fix B), F8, F9 (8)
- Skipped: F7 (1)
