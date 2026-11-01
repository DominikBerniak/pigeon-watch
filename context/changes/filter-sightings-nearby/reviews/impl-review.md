<!-- IMPL-REVIEW-REPORT -->
# Implementation Review: Filter sightings by nearby area, status and criticality

- **Plan**: context/changes/filter-sightings-nearby/plan.md
- **Scope**: Full plan
- **Reviewed phases**: 1, 2, 3, 4
- **Date**: 2026-11-01
- **Verdict**: NEEDS ATTENTION
- **Findings**: 0 critical, 3 warnings, 5 observations

## Verdicts

| Dimension | Verdict |
|-----------|---------|
| Plan Adherence | WARNING |
| Scope Discipline | WARNING |
| Safety & Quality | WARNING |
| Architecture | PASS |
| Pattern Consistency | WARNING |
| Success Criteria | WARNING |

Automated verification (re-run 2026-11-01): `dotnet build` 0 errors (31 pre-existing NuGet/RequestLoggingTests warnings, none in changed files); ArchitectureTests 42/42, UnitTests 249/249, IntegrationTests 126/126; `npm run build` OK (initial 516.31 kB, under the 520 kB warning); `npm test` 65 files / 676 tests passed. Playwright (4.1, 4.2) not re-run in this review.

## Findings

### F1 — Unplanned collapsible filter panel, toggle and Clear button

- **Severity**: ⚠️ WARNING
- **Impact**: 🔎 MEDIUM — real tradeoff; pause to reason through it
- **Dimension**: Plan Adherence
- **Location**: PigeonWatch/Frontend/src/app/features/sightings/nearby-map-page/nearby-map-page.html:19-34, :95-101; nearby-map-page.ts:140, :277; nearby-map-filters.html:98-107
- **Detail**: Plan Phase 3 says the filters component "sits in the toolbar next to 'Search this area'" with input disabled and output filtersChange only. The implementation adds a "Show filters / Show filters (N) / Hide filters" toggle (`filtersOpen` signal, starts collapsed), renders the filters in a separate row below the toolbar hidden with `[hidden]`, and adds a "Clear filters" button. Knock-ons: 4 extra labels (`map.filters.show/showActive/hide/clear`), toggle styling split between `_material-theme.scss:135-141` and `nearby-map-page.scss`, toolbar layout changes, a new global theme selector keyed on the feature element `app-nearby-map-filters`, and E2E/kitchen-sink steps that must open the panel first. The toggle's accessible name also changes with state while `aria-expanded` already carries it. The decision is recorded only in `Frontend/CLAUDE.md:45`, not in the plan. Likely driven by manual check 3.3 (phone-width fit).
- **Fix A ⭐ Recommended**: Record the collapsible panel and Clear button as a plan addendum (Phase 3) with the reason, and give the toggle a stable name ("Filters (N)") so only `aria-expanded` changes.
  - Strength: Keeps tested, working UI; plan becomes accurate ground truth for S-06, which is expected to reuse these components.
  - Tradeoff: Plan changes after the fact; the label rename touches resx, template, page spec, kitchen-sink and E2E selectors.
  - Confidence: HIGH — specs, kitchen-sink and E2E already cover the toggle.
  - Blind spot: Whether the user wanted the collapsed-by-default behaviour has not been confirmed.
- **Fix B**: Remove the toggle and put the two selects inline in the toolbar as planned.
  - Strength: Strict plan adherence; drops 4 labels and the split styling.
  - Tradeoff: Reopens the phone-width fit problem that check 3.3 likely surfaced; reworks specs, cells and E2E.
  - Confidence: MED — phone-width layout without the panel is unverified.
  - Blind spot: Toolbar overflow at 375 px with both selects plus "Search this area".
- **Decision**: FIXED (differently) — plan Phase 3 addendum #5 records the toggle, filter row and Clear button; functionality left as is (no toggle rename)

### F2 — Filter change re-searches while the map is zoomed out

- **Severity**: ⚠️ WARNING
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Safety & Quality
- **Location**: PigeonWatch/Frontend/src/app/features/sightings/nearby-map-page/nearby-map-page.ts:281-285
- **Detail**: `changeFilters` calls `search(lastSearchCentre)` without checking `searchDisabled()`, which `searchThisArea` and `retry` respect. Zoomed out below `minSearchZoom`, a filter change sends a request and swaps markers, but `status()` returns `zoomToSearch` first (:180), so the status line says "zoom in to search" while the map shows fresh results. No spec covers this case.
- **Fix**: Store the filters without searching when `searchDisabled()` is true (as already done before a centre exists) and add a page spec for the zoomed-out case.
- **Decision**: FIXED — `changeFilters` returns after storing when `zoomedOut()` or `mapFailed()` (not the full `searchDisabled()`, which would also drop a change during loading); two red→green page specs (zoomed out, map failed); Frontend/CLAUDE.md nearby-filters line updated. 678/678 specs pass.

### F3 — Unrelated observability audit committed with Phase 4

- **Severity**: ⚠️ WARNING
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Scope Discipline
- **Location**: context/audits/observability/2026-10-30_verify-spa-error-reporting-seam.md (commit cc19de1)
- **Detail**: A verify report for the earlier `spa-error-reporting-seam` change (frontmatter `commit: c56a3aa`) was swept into the S-05 docs commit. It has nothing to do with this change.
- **Fix**: Leave history as is and add a note to `change.md` that cc19de1 also carries that audit report, so archive/blame readers are not misled.
- **Decision**: FIXED — note added to change.md; history unchanged

### F4 — E2E test registers its own user instead of reusing the auth.setup session

- **Severity**: 💡 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Plan Adherence
- **Location**: PigeonWatch/Frontend/tests/e2e/nearby-filters.spec.ts:6, :16-22
- **Detail**: Plan Phase 4 says the test "reuses the `auth.setup.ts` session". It clears `storageState` and registers a fresh user, probably to avoid the per-user report cooldown on the shared seed user. Not recorded anywhere.
- **Fix**: Note the deviation and its reason in the plan's Phase 4 (addendum).
- **Decision**: FIXED — Phase 4 Playwright addendum added to plan.md

### F5 — Manual check 4.3 ticked although roadmap status fields changed

- **Severity**: 💡 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Success Criteria
- **Location**: context/foundation/roadmap.md:49, :159 (commit cc19de1)
- **Detail**: Plan says "update S-05's Unknowns text only" and check 4.3 reads "no other file's status fields changed", yet cc19de1 moved S-05 from `proposed` to `in-progress` in the table and the item, and bumped `updated`. The plan also says status fields are left to `/10x-implement`, so the value is probably sanctioned, but the ticked check is inaccurate.
- **Fix**: Accept the status change as `/10x-implement` bookkeeping; let `/10x-archive` set the final status.
- **Decision**: ACCEPTED — status change is `/10x-implement` bookkeeping; no file change

### F6 — Initial bundle grew to 516.31 kB; documented figure is stale

- **Severity**: 💡 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Success Criteria
- **Location**: PigeonWatch/Frontend/CLAUDE.md:31
- **Detail**: Plan expected no initial-bundle growth because the map page is lazy. Measured initial total is 516.31 kB vs the documented 512.1 kB (warning at 520 kB, ~3.7 kB headroom left). Likely sources are the bundled label JSON and the global theme overrides; exact attribution between this change and the intervening spa-error-reporting-seam work was not measured.
- **Fix**: Update the measured figure in `Frontend/CLAUDE.md:31` to 516.3 kB.
- **Decision**: FIXED — Frontend/CLAUDE.md now states 516.3 kB and ~3.7 kB headroom

### F7 — Controller casts filter ints inline; repository criticality param nullable

- **Severity**: 💡 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Pattern Consistency
- **Location**: PigeonWatch/Api/WebApi/Controllers/SightingsController.cs:76-77; PigeonWatch/Api/Data/Repositories/Interfaces/ISightingRepository.cs:9
- **Detail**: `Api/CLAUDE.md` puts int→enum casts in request mappers (`CreateSightingRequestMapper`); `Nearby` casts inline. `ListInBoxAsync` takes `IReadOnlyCollection<SightingCriticality>?` while `statuses` is non-nullable and the service always passes a list.
- **Fix**: Move the casts into a small nearby request mapper and make the criticality parameter non-nullable.
- **Decision**: FIXED — new `NearbySightingFilters` BO, `INearbySightingsRequestMapper`/`NearbySightingsRequestMapper` (registered in `AddWebApi`, used by `Nearby`); `ListInBoxAsync` criticalities non-nullable; tests and Api/CLAUDE.md updated. Build 0 errors, Arch 42, Unit 249, Integration 126 pass.

### F8 — No index supports the new criticality predicate

- **Severity**: 💡 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Safety & Quality
- **Location**: PigeonWatch/Api/Data/Repositories/SightingRepository.cs:47-64
- **Detail**: Explicitly out of scope ("What We're NOT Doing"), noted for the record: a selective criticality filter makes SQL Server read more box rows before the 400 newest matches. Fine at MVP volume.
- **Fix**: No action now; revisit with an `INCLUDE (Status, Criticality, CreateDate)` index if volume grows.
- **Decision**: ACCEPTED — out of scope by plan; revisit if volume grows
