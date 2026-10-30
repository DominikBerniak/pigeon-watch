<!-- IMPL-REVIEW-REPORT -->
# Implementation Review: SPA error reporting seam

- **Plan**: context/changes/spa-error-reporting-seam/plan.md
- **Scope**: Full plan
- **Reviewed phases**: 1, 2, 3, 4, 5
- **Date**: 2026-10-30
- **Verdict**: NEEDS ATTENTION
- **Findings**: 0 critical, 2 warnings, 3 observations

## Verdicts

| Dimension | Verdict |
|-----------|---------|
| Plan Adherence | PASS |
| Scope Discipline | PASS |
| Safety & Quality | PASS |
| Architecture | PASS |
| Pattern Consistency | PASS |
| Success Criteria | WARNING |

Automated checks run: `dotnet test PigeonWatchApi.slnx` (unit 230, architecture 42, integration 124, all passed); `npm test` (64 files, 651 tests passed, 1 unhandled error); `npm run build` passed.

## Findings

### F1 — Vitest run ends with an unhandled teardown error from culture-store.spec

- **Severity**: ⚠️ WARNING
- **Impact**: 🔎 MEDIUM — real tradeoff; pause to reason through it
- **Dimension**: Success Criteria
- **Location**: src/app/core/resources/culture-store.spec.ts
- **Detail**: All 651 tests pass, but Vitest reports `EnvironmentTeardownError: Closing rpc while "onUserConsoleLog" was pending`, which makes the run exit non-zero and is a likely CI failure. `CultureStore` now reports storage failures through `ErrorReportingService`, whose failed send calls `console.warn`, so a storage-throwing spec probably logs after teardown. Not checked against the pre-change baseline.
- **Fix**: Provide an `ErrorReportingService` stub in `culture-store.spec.ts` (same stub the other edited specs use), then re-run and confirm a clean exit.
  - Strength: Same pattern as the other six edited specs.
  - Tradeoff: One more existing spec touched.
  - Confidence: MED — cause inferred, not reproduced.
  - Blind spot: Whether the error exists on the base commit.
- **Decision**: FIXED (stub in culture-store.spec.ts; suite now exits clean, 652 passed)

### F2 — Progress row 5.2 has no checkbox and no commit sha

- **Severity**: ⚠️ WARNING
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Plan Adherence
- **Location**: plan.md:385
- **Detail**: `5.2 Full frontend suite and production build still pass` lacks `- [x]` and a sha, so tooling that counts checkboxes (and /10x-status, /10x-archive) sees 24 of 24 rather than 25 steps.
- **Fix**: Make it `- [x] 5.2 Full frontend suite and production build still pass — 4e9e54b`, after F1 is resolved.
- **Decision**: FIXED (row 5.2 checked with sha)

### F3 — Dedupe key hides distinct uncaught errors

- **Severity**: 💡 OBSERVATION
- **Impact**: 🔎 MEDIUM — real tradeoff; pause to reason through it
- **Dimension**: Safety & Quality
- **Location**: src/app/core/error-reporting/error-reporting.service.ts:27
- **Detail**: The key is kind|context|status, and every uncaught error uses context `app.uncaught` with no status, so only the first uncaught error per page load is reported; a different second error is dropped. This matches the plan text but limits diagnostic value.
- **Fix A ⭐ Recommended**: Add `errorName` to the dedupe key.
  - Strength: Distinct error types still reach the log; the cap of 20 bounds volume.
  - Tradeoff: Slightly more reports.
  - Confidence: HIGH — one-line change with an existing spec to extend.
  - Blind spot: Many different error names in one session consume the cap.
- **Fix B**: Leave as is and note it in the Frontend CLAUDE.md.
  - Strength: Matches the plan.
  - Tradeoff: Second root cause is invisible.
  - Confidence: MEDIUM
  - Blind spot: None significant.
- **Decision**: FIXED via Fix A (errorName in dedupe key, new spec)

### F4 — Manual step 3.5 checked although it was not run

- **Severity**: 💡 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Success Criteria
- **Location**: plan.md:362
- **Detail**: change.md records that 3.5 could not be run in the browser and was accepted on the specs; the row is still `[x]` with no note in the plan.
- **Fix**: Append "(accepted on specs, see change.md)" to row 3.5.
- **Decision**: FIXED (row 3.5 annotated)

### F5 — Rate limit keyed on RemoteIpAddress without ForwardedHeaders

- **Severity**: 💡 OBSERVATION
- **Impact**: 🔬 HIGH — architectural stakes; think carefully before deciding
- **Dimension**: Safety & Quality
- **Location**: PigeonWatch/Api/WebApi.Host/Program.cs:76
- **Detail**: Behind an App Service front end all clients may share one IP, so the 20/min `clientErrors` bucket could be shared and drop reports (the same already holds for `auth`). The plan explicitly excludes ForwardedHeaders work.
- **Fix**: Leave out of scope; track with the unverified hosting assumptions from the audit.
- **Decision**: SKIPPED (out of scope per plan)
