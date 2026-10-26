<!-- IMPL-REVIEW-REPORT -->
# Implementation Review: Server-side observability logging

- **Plan**: context/changes/server-side-observability-logging/plan.md
- **Scope**: Full plan
- **Reviewed phases**: 1, 2, 3, 4, 5
- **Date**: 2026-10-26
- **Verdict**: NEEDS ATTENTION
- **Findings**: 0 critical, 3 warnings, 5 observations

## Verdicts

| Dimension | Verdict |
|-----------|---------|
| Plan Adherence | PASS |
| Scope Discipline | PASS |
| Safety & Quality | WARNING |
| Architecture | PASS |
| Pattern Consistency | WARNING |
| Success Criteria | PASS |

Automated criteria re-run at review time: unit 220/220, integration 81/81, architecture 42/42, solution build 0 errors, no vulnerable packages in the integration project. All manual rows are checked.

## Findings

### F1 — Console scopes print the client-supplied RequestPath without escaping

- **Severity**: ⚠️ WARNING
- **Impact**: 🔎 MEDIUM — real tradeoff; pause to reason through it
- **Dimension**: Safety & Quality
- **Location**: PigeonWatch/Api/WebApi.Host/appsettings.json (Logging:Console:IncludeScopes)
- **Detail**: IncludeScopes is global to the console provider, so ASP.NET Core's own hosting scope (RequestId, ConnectionId, RequestPath) is printed on every line next to the new TraceId and UserId scope. RequestPath is client-supplied and the simple console formatter does not escape control characters. Whether a path such as `/x%0Afake` can forge a log line was not verified at runtime.
- **Fix**: Check that `/x%0Afake` does not produce a forged line; if it does, switch the console to the JSON formatter or drop IncludeScopes and put TraceId and UserId on the summary line.
  - Strength: Closes a log-forging path in the Azure log stream without touching responses.
  - Tradeoff: JSON output is less readable locally; moving fields to the summary line loses scope context on other lines.
  - Confidence: MEDIUM — the risk is real in principle but unverified.
  - Blind spot: Kestrel's handling of encoded control characters in the path.
- **Decision**: DISMISSED (verified at runtime: the hosting scope logs RequestPath percent-encoded, so `/x%0AFORGED` stays on one line; no fix needed)

### F2 — Login and refresh filter's UserId branch is effectively dead

- **Severity**: ⚠️ WARNING
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Safety & Quality
- **Location**: PigeonWatch/Api/DependencyInjection/PigeonWatchEndpointRouteBuilderExtensions.cs (~95-110)
- **Detail**: login and refresh are called without a bearer header, so HttpContext.User is anonymous and the "for {UserId}" variant is almost never logged. The branch is untested and misleading. This is consistent with anti-enumeration.
- **Fix**: Delete the UserId branch and the unused import, or document that it is intentional.
- **Decision**: FIXED (removed the dead UserId branch and unused using)

### F3 — Missing blank line between methods in AccountRepository

- **Severity**: ⚠️ WARNING
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Pattern Consistency
- **Location**: PigeonWatch/Api/Data/Repositories/AccountRepository.cs:~133
- **Detail**: the closing brace of ToAccountErrors is followed directly by `private void LogIdentityError`. The Api CLAUDE.md rule requires a blank line between methods; neither the build nor the tests check it.
- **Fix**: Add a blank line before LogIdentityError.
- **Decision**: FIXED (blank line added)

### F4 — Scope TraceId is the W3C traceparent string, not the bare trace id

- **Severity**: 👁 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Plan Adherence
- **Location**: PigeonWatch/Api/WebApi.Host/Program.cs:83
- **Detail**: `Activity.Current?.Id ?? context.TraceIdentifier` yields `00-<traceid>-<spanid>-01`. It correlates fine within logs, but is not the bare trace id.
- **Fix**: Use `Activity.Current?.TraceId.ToString()` when an activity exists.
- **Decision**: FIXED (bare TraceId)

### F5 — Cold-start SQL numbers duplicate the unavailable-errors set

- **Severity**: 👁 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Architecture
- **Location**: PigeonWatch/Api/Data/Diagnostics/DatabaseUnavailableExceptionHandler.cs
- **Detail**: coldStartErrorNumbers copies the first six entries of unavailableErrorNumbers, so the two can drift apart.
- **Fix**: Derive one set from the other or name them so the subset relationship is explicit.
- **Decision**: FIXED (unavailable set derived from cold-start set)

### F6 — Summary line reports 200 if the exception handler itself throws

- **Severity**: 👁 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Safety & Quality
- **Location**: PigeonWatch/Api/WebApi.Host/Program.cs (summary middleware finally)
- **Detail**: if UseExceptionHandler itself fails and the exception reaches the outer finally, Response.StatusCode is still 200. The ordinary 500 path is correct and tested.
- **Fix**: Accept, or add a catch that records 500 and rethrows.
- **Decision**: FIXED (catch sets 500 when the response has not started, then rethrows; no new test, double-failure path is not reachable through the factory)

### F7 — Auth filter looks up the logger three times per request

- **Severity**: 👁 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Pattern Consistency
- **Location**: PigeonWatch/Api/DependencyInjection/PigeonWatchEndpointRouteBuilderExtensions.cs
- **Detail**: `GetRequiredService<ILoggerFactory>().CreateLogger("PigeonWatch.AuthEvents")` repeats three times inside the filter lambda.
- **Fix**: Create the logger once per invocation in a local.
- **Decision**: SKIPPED (one lookup per request; textual duplication only)

### F8 — Test helper duplication and small test gaps

- **Severity**: 👁 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Pattern Consistency
- **Location**: UnitTests/ControllerFailureLoggingTests.cs, AccountRepositoryTests.cs, PigeonWatchUserStoreTests.cs; IntegrationTests/AuthEventLoggingTests.cs
- **Detail**: LogCall/LogCalls/ToLogCall are copied across test files. In AuthEventLoggingTests the refresh test reads the user after Dispose without a using, and the rate-limit no-IP check only looks for "10.0.". NotAllowed login outcome has no test.
- **Fix**: Extract a shared test helper and tighten those assertions.
- **Decision**: SKIPPED (refactor across four test files, no behavior change)
