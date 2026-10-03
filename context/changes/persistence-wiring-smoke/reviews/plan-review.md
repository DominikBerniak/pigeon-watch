<!-- PLAN-REVIEW-REPORT -->
# Plan Review: Persistence Wiring Smoke Test

- **Plan**: context/changes/persistence-wiring-smoke/plan.md
- **Mode**: Deep (direct verification, no sub-agent)
- **Date**: 2026-10-03
- **Verdict**: REVISE → SOUND after triage
- **Findings**: 0 critical, 6 warnings, 1 observation

## Verdicts

| Dimension | Verdict |
|-----------|---------|
| End-State Alignment | PASS |
| Lean Execution | PASS |
| Architectural Fitness | WARNING |
| Blind Spots | WARNING |
| Plan Completeness | WARNING |

## Grounding
8/8 paths ✓, 6/6 symbols ✓ (UserSecretsId, EnableRetryOnFailure, dotnet-ef 10.0.12, appId d5757dbf…, AllowAzureServices, SP name), brief↔plan ✓, Progress↔Phase 6/6 ✓

## Findings

### F1 — Public repository can't take the internal DbContext

- **Severity**: ⚠️ WARNING
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Architectural Fitness
- **Location**: Phase 1 §3 — Data layer
- **Detail**: The plan made the entity and context `internal` but the repository implementation public. A public class whose public constructor takes an internal type does not compile (CS0051), and MS DI only activates through public constructors.
- **Fix**: Make SmokeCheckRepository `internal sealed` with a public constructor; only ISmokeCheckRepository public.
- **Decision**: FIXED (fixed differently) — DbContext and repository are public; entities stay internal and the context's `DbSet` properties are `internal` (a public `DbSet` of an internal entity does not compile). Fallback: configure entities in `OnModelCreating` if EF does not pick up the internal `DbSet` properties. Brief decision row updated.

### F2 — "Any database failure" can't be named from BusinessLogic

- **Severity**: ⚠️ WARNING
- **Impact**: 🔎 MEDIUM — real tradeoff; pause to reason through it
- **Dimension**: Architectural Fitness
- **Location**: Phase 1 §4 — BusinessLogic layer
- **Detail**: With `EnableRetryOnFailure`, failures surface as EF Core or SqlClient exception types, which BusinessLogic is forbidden to reference by the Phase 2 layer rule.
- **Fix A ⭐ Recommended**: Service catches `Exception` when `!ct.IsCancellationRequested`, as in the draft controller.
  - Strength: No new types; architecture rule intact; matches draft code.
  - Tradeoff: A programming bug also surfaces as 503.
  - Confidence: HIGH — the pattern exists in the draft HealthController.
  - Blind spot: None significant for a probe endpoint.
- **Fix B**: Repository translates DB exceptions into a `DataUnavailableException` in BusinessObjects.
  - Strength: Precise "database failure" semantics.
  - Tradeoff: Extra type and translation code for a throwaway probe.
  - Confidence: MED — translation boundary must cover open, strategy and SaveChanges.
  - Blind spot: Which exceptions count as "database" stays a judgement call.
- **Decision**: FIXED (Fix A)

### F3 — Return-type arch rule is bypassed by IActionResult

- **Severity**: ⚠️ WARNING
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Blind Spots
- **Location**: Phase 2 §1 — Architecture test project
- **Detail**: The reflection rule only inspected declared return types, so `Task<IActionResult>` returning `Ok(businessObject)` would pass.
- **Fix**: Require a generic `ActionResult<T>` with `T` in `WebApi.Models`; extend manual check 2.4.
- **Decision**: FIXED (fixed differently) — controller actions must be `async` and return exactly `Task<ActionResult<TApiModel>>` with `TApiModel` in `WebApi.Models`; added to the Phase 1 WebApi contract and the Phase 2 rule; manual check 2.4 covers the `Task<IActionResult>` + `Ok(bo)` case.

### F4 — Migrate job may fetch the SQL token after the OIDC assertion expires

- **Severity**: ⚠️ WARNING
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Blind Spots
- **Location**: Phase 4 §2 — Migrate job step order
- **Detail**: The slow self-contained bundle build sat between `azure/login` and the first `database.windows.net` token request, which needs a still-valid GitHub OIDC assertion (AADSTS700024 risk), only observable post-merge.
- **Fix**: Build the bundle before `azure/login`, then firewall rule, run with retry, delete rule under `if: always()`.
- **Decision**: FIXED

### F5 — Live sqlcmd checks need a client firewall rule the plan omits

- **Severity**: ⚠️ WARNING
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Blind Spots
- **Location**: Phase 5 — criteria 5.3, 5.4, 5.5
- **Detail**: Local `sqlcmd -G` against the live server is blocked by the firewall (only `AllowAzureServices`); adding a rule conflicts with 5.3 unless ordered.
- **Fix**: Dominik adds a temporary client-IP rule for 5.4/5.5, deletes it, and 5.3 is checked last.
- **Decision**: FIXED

### F6 — Secret-grep criterion contradicts the new migrate job

- **Severity**: ⚠️ WARNING
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Plan Completeness
- **Location**: Phase 4 — criterion 4.2
- **Detail**: The migrate job adds a second `azure/login` with the same three secrets, so a correct workflow would fail "only the three existing references".
- **Fix**: Expect only the three `secrets.AZURE_*` names, one set per job that logs in.
- **Decision**: FIXED

### F7 — Phase 3 doc entry has no success criterion

- **Severity**: 💡 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Plan Completeness
- **Location**: Phase 3 §3 — Record the grants
- **Detail**: The `deploy-plan.md` entry for the grants was a required change with no criterion checking it.
- **Fix**: Add a manual criterion and Progress item 3.5.
- **Decision**: FIXED

## Triage Summary

- Fixed: F1 (differently), F2 (Fix A), F3 (differently), F4, F5, F6, F7 (7)
- Skipped / Accepted / Dismissed: none
- Verdict after fixes: SOUND
