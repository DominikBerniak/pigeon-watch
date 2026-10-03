<!-- PLAN-REVIEW-REPORT -->
# Plan Review: Persistence Wiring Smoke Test

- **Plan**: context/changes/persistence-wiring-smoke/plan.md
- **Mode**: Deep
- **Date**: 2026-10-03
- **Verdict**: REVISE → SOUND after triage
- **Findings**: 0 critical, 3 warnings, 3 observations

## Verdicts

| Dimension | Verdict |
|-----------|---------|
| End-State Alignment | PASS |
| Lean Execution | PASS |
| Architectural Fitness | PASS |
| Blind Spots | WARNING |
| Plan Completeness | WARNING |

## Grounding
13/13 paths ✓, 5/5 symbols ✓ (AddControllers/MapControllers, ConnectionStrings__Default, port 5285, deploy-plan.md line refs), brief↔plan ✓, Progress↔Phase ✓ (5/5 phases, 28/28 criteria)

## Findings

### F1 — "Stop LocalDB" can't produce the 503 path

- **Severity**: ⚠️ WARNING
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Blind Spots
- **Location**: Phase 1 Manual Verification (1.7), Testing Strategy step 2
- **Detail**: LocalDB auto-starts its instance on the next client connection, so stopping it yields 200, not 503. `EnableRetryOnFailure` also delays any transient-failure 503 by its retry budget.
- **Fix**: Exercise the failure path with a non-transient broken connection string override and note the retry-budget delay for real outages.
- **Decision**: FIXED

### F2 — Tool manifest location breaks repo-root `dotnet ef` commands

- **Severity**: ⚠️ WARNING
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Plan Completeness
- **Location**: Phase 1 §1, criteria 1.2/1.3/3.3, Phase 3 migrate job
- **Detail**: Tool manifests are found by searching upward from the current directory. Criteria and CI run from the repo root, so `PigeonWatch/Api/.config/dotnet-tools.json` would not be found.
- **Fix**: Place the manifest at repo-root `.config/dotnet-tools.json`.
- **Decision**: FIXED

### F3 — "SQL Server Contributor" grants far more than firewall rules

- **Severity**: ⚠️ WARNING
- **Impact**: 🔎 MEDIUM — real tradeoff; pause to reason through it
- **Dimension**: Blind Spots
- **Location**: Phase 2 §2 — Firewall-management role
- **Detail**: The intent is firewall-rule CRUD only, but SQL Server Contributor can delete or rescale `pigeonwatch-db` and change server settings, on a principal that runs on every merge to `main`.
- **Fix A ⭐ Recommended**: Custom role with only `Microsoft.Sql/servers/read` and `firewallRules/{read,write,delete}` at server scope
  - Strength: Matches the stated intent and the project's least-privilege pattern.
  - Tradeoff: Extra `az role definition create` step for Dominik.
  - Confidence: HIGH — firewall CRUD needs only these actions.
  - Blind spot: Whether Dominik can create custom role definitions on the subscription.
- **Fix B**: Keep SQL Server Contributor and record the accepted risk
  - Strength: Built-in role, no custom definition.
  - Tradeoff: A compromised workflow could delete the database.
  - Confidence: HIGH.
  - Blind spot: None significant.
- **Decision**: FIXED (Fix A)

### F4 — No workflow concurrency guard

- **Severity**: 💡 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Blind Spots
- **Location**: Phase 3 §1–2
- **Detail**: Two merges close together can interleave migrate and deploy, so older code can be deployed after a newer migration.
- **Fix**: Add `concurrency: { group: deploy-api, cancel-in-progress: false }`.
- **Decision**: FIXED

### F5 — CI command details left to the implementer

- **Severity**: 💡 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Plan Completeness
- **Location**: Phase 3 §1–2
- **Detail**: The plan doesn't say how the bundle receives its connection string (Production config has none), and "generous retry settings" for curl doesn't cover refused connections during a cold start.
- **Fix**: Pin `./efbundle --connection "$SQL_CONN"` in a bounded retry loop and `curl -f --retry 10 --retry-delay 15 --retry-connrefused --max-time 120`.
- **Decision**: FIXED

### F6 — Small text inconsistencies

- **Severity**: 💡 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Plan Completeness
- **Location**: Phase 4 §1 Contract; criterion 3.1
- **Detail**: "Phase 1 to 3 commits" leaves out Phase 0; the 3.1 assertion rejects the list form `needs: [migrate]`.
- **Fix**: Say "Phase 0 to 3" and accept both forms in 3.1.
- **Decision**: FIXED
