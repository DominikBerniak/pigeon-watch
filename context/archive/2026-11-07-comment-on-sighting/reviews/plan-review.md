<!-- PLAN-REVIEW-REPORT -->
# Plan Review: Comment on a Sighting

- **Plan**: context/changes/comment-on-sighting/plan.md
- **Mode**: Deep
- **Date**: 2026-11-07
- **Verdict**: REVISE (SOUND after fixes)
- **Findings**: 0 critical, 2 warnings, 5 observations

## Verdicts

| Dimension | Verdict |
|-----------|---------|
| End-State Alignment | PASS |
| Lean Execution | PASS |
| Architectural Fitness | PASS |
| Blind Spots | WARNING |
| Plan Completeness | WARNING |

## Grounding

8/8 paths ✓, symbols ✓ (route keys, `ClientConfigurationModel`, `isExpectedClientStatus`), Progress↔Phase ✓ (21/21 rows), brief↔plan ✓ (except F5). Deep check confirmed: the route keys come out as `GET sightings/{sightingId:guid}/comments` etc. (`EndpointCatalog.cs:17-23`); class-level `[Authorize]` is discovered automatically (`EndpointCatalog.cs:18-19`); JSON `[MaxLength]` gets the default 400 (`WebApiServiceCollectionExtensions.cs:43-52`); the privacy sweep tolerates a 400 cooldown but fails on a 429 (`PrivacySweep.cs:34-41`).

## Findings

### F1: Adding the comments section breaks existing detail cells and specs

- **Severity**: ⚠️ WARNING
- **Impact**: 🔎 MEDIUM (real tradeoff; pause to reason through it)
- **Dimension**: Blind Spots
- **Location**: Phase 3 §3, §4, §6, §7
- **Detail**: `CellStubs.sightingsApi` lacks `comments` (`cell-stubs.ts:289-297`), so 9 of the 12 detail cells would throw. The detail specs use partial stubs (`sighting-detail-page.spec.ts:55`, `-reporting.spec.ts:28`). The assertions on `app-alert` being null, the `a` count, `dl > div` length 7 and `report` call counts break if the empty state is an `app-alert`. The archived `visual-notes.md` baseline goes stale.
- **Fix A ⭐ Recommended**: Default empty-page stub in `CellStubs` and both specs. Empty state as muted text. No `dl` or links in the component. Changed assertions scoped and listed. New card heights recorded in the Frontend `CLAUDE.md`.
  - Strength: Keeps existing detail behaviour pinned; the ripple is known up front.
  - Tradeoff: Some assertions get narrowed (the requirement changed).
  - Confidence: HIGH (line-level evidence).
  - Blind spot: Exact pixel heights are known only after build.
- **Fix B**: An opt-in flag that renders comments only when enabled.
  - Strength: No churn in existing tests.
  - Tradeoff: A test-only production toggle; tests no longer match the real page.
  - Confidence: MED
  - Blind spot: The toggle outlives its purpose.
- **Decision**: FIXED (Fix A)

### F2: 403 handling would hide a real bug signal

- **Severity**: ⚠️ WARNING
- **Impact**: 🏃 LOW (quick decision; fix is obvious and narrowly scoped)
- **Dimension**: Blind Spots
- **Location**: Phase 3 §2
- **Detail**: The plan made 403 silent. Adding 403 to `expectedClientStatuses` (`problem-details.ts:31`) would silence it in 5 callers. Edit and Delete show only on `isMine`, so a 403 is a client bug.
- **Fix**: Report 403 locally in `comment-errors.ts` with the forbidden message and an error reference; the shared set stays unchanged.
- **Decision**: FIXED

### F3: The cooldown query style is unspecified

- **Severity**: OBSERVATION
- **Impact**: 🏃 LOW (quick decision; fix is obvious and narrowly scoped)
- **Dimension**: Plan Completeness
- **Location**: Phase 1 §4
- **Detail**: There is no `Max` aggregate in the repo. The proven pattern is `OrderByDescending(...).FirstOrDefault` plus `SpecifyKind(Utc)` (`SightingRepository.cs:132-141`).
- **Fix**: Two queries in that style, combined in C# with the Utc kind set.
- **Decision**: FIXED

### F4: FixtureContext is built in two places

- **Severity**: OBSERVATION
- **Impact**: 🏃 LOW (quick decision; fix is obvious and narrowly scoped)
- **Dimension**: Plan Completeness
- **Location**: Phase 2 §6
- **Detail**: It is constructed at `PrivacySweepTests.cs:233-241` and `EndpointProtectionTests.cs:76-79`. The sweep requires the cooldown to be a 400, not a 429.
- **Fix**: Name both sites, seed the comment over HTTP as Alice, and state that the cooldown stays 400.
- **Decision**: FIXED

### F5: The brief misstates the paging risk

- **Severity**: OBSERVATION
- **Impact**: 🏃 LOW (quick decision; fix is obvious and narrowly scoped)
- **Dimension**: Plan Completeness
- **Location**: plan-brief.md, Open Risks
- **Detail**: Inserts only push rows down and soft delete removes nothing, so paging can show duplicates but never miss a row.
- **Fix**: Reword the risk line.
- **Decision**: FIXED

### F6: The E2E user and edit step are ambiguous

- **Severity**: OBSERVATION
- **Impact**: 🏃 LOW (quick decision; fix is obvious and narrowly scoped)
- **Dimension**: Blind Spots
- **Location**: Phase 4 §1
- **Detail**: A shared auth user would hit an earlier run's comment cooldown on a rerun or retry. The photo spec registers a fresh user (`sighting-photo.spec.ts:7,19`).
- **Fix**: Register a fresh user with an empty storageState; the edit changes the text.
- **Decision**: FIXED

### F7: The client configuration model name is left open

- **Severity**: OBSERVATION
- **Impact**: 🏃 LOW (quick decision; fix is obvious and narrowly scoped)
- **Dimension**: Plan Completeness
- **Location**: Phase 2 §4
- **Detail**: `ClientConfigurationModel.cs` exists, yet the plan says "or its existing name".
- **Fix**: Pin the name; the nested `CommentRulesModel` comes from §2.
- **Decision**: FIXED
