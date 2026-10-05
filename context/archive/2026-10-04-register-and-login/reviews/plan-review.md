<!-- PLAN-REVIEW-REPORT -->
# Plan Review: Register and Log In (S-01)

- **Plan**: context/changes/register-and-login/plan.md
- **Mode**: Deep
- **Date**: 2026-10-04
- **Verdict**: REVISE → SOUND after triage
- **Findings**: 2 critical, 5 warnings, 2 observations

## Verdicts

| Dimension | Verdict |
|-----------|---------|
| End-State Alignment | WARNING |
| Lean Execution | PASS |
| Architectural Fitness | WARNING |
| Blind Spots | WARNING |
| Plan Completeness | FAIL |

## Grounding

10/10 paths ✓, 6/6 symbols ✓ (including SignInResult "LockedOut"), brief↔plan ✓, Progress↔Phase ✓ (all 46 criteria matched)

## Findings

### F1 — Generator and label-coverage specs won't compile under ng test

- **Severity**: ❌ CRITICAL
- **Impact**: 🔎 MEDIUM — real tradeoff; pause to reason through it
- **Dimension**: Plan Completeness
- **Location**: Phase 3 §8 + 3.5; Phase 4 §5 + 4.10
- **Detail**: The project has no @types/node, and tsconfig.spec.json sets types: ["vitest/globals"]. Importing `node:fs` gives TS2591 and importing `scripts/*.mjs` gives TS7016, so `npm test` fails. Bundling itself is fine: @angular/build externalizes `node:` builtins, and Vitest runs in Node with jsdom. The spec is inlined into a bundle served under its own module id, though, so `import.meta.url` inside the script won't point to scripts/.
- **Fix A ⭐ Recommended**: Add @types/node and "node" in the tsconfig.spec types; give the generator a typed export surface (.d.mts); resolve paths from process.cwd().
  - Strength: One test command stays, and CI is unchanged.
  - Tradeoff: Node typings become visible to every spec.
  - Confidence: HIGH — both errors were reproduced with the project's tsc.
  - Blind spot: A future `loader` option in angular.json would break the externalization of `node:` builtins.
- **Fix B**: Run the generator and coverage checks as a separate `node --test` runner.
  - Strength: No Angular bundling caveats.
  - Tradeoff: A second runner and an extra CI step.
  - Confidence: HIGH.
  - Blind spot: None significant.
- **Decision**: FIXED (Fix A)

### F2 — Duplicate display name can't be detected in the SQLite tests

- **Severity**: ❌ CRITICAL
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Plan Completeness
- **Location**: Phase 1 §2 vs criterion 1.3
- **Detail**: The store mapped only SqlException 2601/2627, matched by index name. SQLite raises SqliteException (code 19) with column names and no index name, so the planned unit test could not pass.
- **Fix**: Add a store-level pre-check on NormalizedDisplayName in Create/UpdateAsync, and keep the index mapping as a SQL-Server-only race backstop. Not an IUserValidator: its ValidateAsync has no CancellationToken, which fails CancellationTokenTests.
- **Decision**: FIXED

### F3 — Refresh shares the 10/min login budget, and a 429 on refresh is unhandled

- **Severity**: ⚠️ WARNING
- **Impact**: 🔎 MEDIUM — real tradeoff; pause to reason through it
- **Dimension**: Blind Spots
- **Location**: Phase 2 §3; Phase 3 §4
- **Detail**: The rate limit on the auth group also covered /auth/refresh. The access token lives in memory only, so every reload or new tab costs one refresh. The interceptor did not define what a 429 on refresh does, so a rate-limited startup refresh redirected a validly logged-in user to /login.
- **Fix**: Exempt auth/refresh from the auth policy (GetNoLimiter partition). A 429 on refresh propagates and keeps the tokens.
  - Strength: Removes a self-inflicted logout and keeps spray protection.
  - Tradeoff: Refresh calls are unbounded per IP.
  - Confidence: HIGH.
  - Blind spot: None significant.
- **Decision**: FIXED

### F4 — whenReady waits on loadClient, so routes are blocked for up to 2 minutes

- **Severity**: ⚠️ WARNING
- **Impact**: 🔎 MEDIUM — real tradeoff; pause to reason through it
- **Dimension**: End-State Alignment
- **Location**: Phase 3 §3; criterion 3.6; Critical Implementation Details
- **Detail**: The guards awaited whenReady(), which waited for loadClient(). With the API down, no route rendered for 120 s, so manual check 3.6 showed a blank page in Phase 3.
- **Fix**: Start loadClient() in parallel without gating. whenReady resolves immediately when no refresh token is stored; otherwise it waits only for loadGeneral(). The spec was updated to match.
  - Strength: The login page renders at once, and 3.6 becomes testable.
  - Tradeoff: The warm-up panel still overlays the page on a cold start, which is intended.
  - Confidence: HIGH.
  - Blind spot: The interaction with startedDuringStartup.
- **Decision**: FIXED

### F5 — The 503 classifier relies on an internal EF API and unverified error numbers

- **Severity**: ⚠️ WARNING
- **Impact**: 🔎 MEDIUM — real tradeoff; pause to reason through it
- **Dimension**: Blind Spots
- **Location**: Phase 2 §2; criterion 2.7
- **Detail**: SqlServerTransientExceptionDetector lives in a `.Internal` namespace (EF1001 warning). The SqlException branch only sees errors EF already treats as non-transient. The local unreachable-host repro may return 53/258 and produce a 500.
- **Fix**: Handle RetryLimitExceededException plus an explicit set of SqlException numbers owned by the handler, and unit-test it.
  - Strength: No internal API, and deterministic.
  - Tradeoff: We maintain the list ourselves.
  - Confidence: MED — the exact Number for the local repro is not measured.
  - Blind spot: Which numbers Azure SQL serverless returns mid-resume beyond 40613.
- **Decision**: FIXED

### F6 — "No other test changes" leaves architecture-test traps unnamed

- **Severity**: ⚠️ WARNING
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Architectural Fitness
- **Location**: Phase 1 §2, §6, §7
- **Detail**: An IEndpointFilter class fails CancellationTokenTests. Private helpers in the extension class fail StaticClassTests. The provider must be registered by type. The plan did not say what Get/SetEmailConfirmedAsync do.
- **Fix**: Use an inline lambda filter with no helpers; register via AddScoped<ICurrentUserProvider, HttpContextCurrentUserProvider>() and call AddHttpContextAccessor explicitly; GetEmailConfirmedAsync returns false and SetEmailConfirmedAsync is a no-op.
- **Decision**: FIXED

### F7 — Identity error codes the SPA never maps

- **Severity**: ⚠️ WARNING
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Blind Spots
- **Location**: Phase 1 §4; Phase 4 §2
- **Detail**: Because UserName = Email, Identity returns DuplicateUserName and InvalidUserName (the latter for apostrophes or accented letters), and the SPA mapped neither.
- **Fix**: Set AllowedUserNameCharacters = ""; the repository collapses user-name codes into email codes.
- **Decision**: FIXED

### F8 — Logout leaves other open tabs logged in for up to 1 h

- **Severity**: ℹ️ OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Blind Spots
- **Location**: Phase 3 §1/§3
- **Detail**: Other tabs keep their in-memory access token until it expires.
- **Fix**: SessionService listens for the `storage` event and calls expire when the refresh token is removed. Manual step 4.4 was extended to cover it.
- **Decision**: FIXED

### F9 — "Supported culture" is undefined on the API side

- **Severity**: ℹ️ OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Plan Completeness
- **Location**: Phase 2 §6
- **Detail**: The contract never said how the service decides support, and CultureInfo behaviour depends on ICU.
- **Fix**: Supported = the default culture, or a culture with a satellite resource set (tryParents: false); a CultureNotFoundException resolves to `en`.
- **Decision**: FIXED
