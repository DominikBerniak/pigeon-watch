<!-- IMPL-REVIEW-REPORT -->
# Implementation Review: Urgency-Ranked Sighting List (S-06)

- **Plan**: context/changes/urgency-ranked-sighting-list/plan.md
- **Scope**: Full plan
- **Reviewed phases**: 1, 2, 3, 4, 5
- **Date**: 2026-11-01
- **Verdict**: NEEDS ATTENTION
- **Findings**: 0 critical, 4 warnings, 5 observations

## Verdicts

| Dimension | Verdict |
|-----------|---------|
| Plan Adherence | WARNING |
| Scope Discipline | WARNING |
| Safety & Quality | WARNING |
| Architecture | PASS |
| Pattern Consistency | PASS |
| Success Criteria | WARNING |

## Verification evidence

- `dotnet test` UnitTests 292/292, ArchitectureTests 42/42, IntegrationTests 130/130 pass. The test binaries are newer than the last API commit and the tree is clean.
- `dotnet build PigeonWatch/Api/PigeonWatchApi.slnx`: passes with 0 errors once the local API host is stopped (an earlier run failed only on MSB3021/MSB3027 file locks held by the running host). After triage fixes, ArchitectureTests 42/42, UnitTests 294/294 and IntegrationTests 130/130 pass, as do frontend 789/789 and Playwright 5/5.
- `dotnet ef migrations has-pending-model-changes --no-build`: "No changes have been made to the model since the last migration."
- `npm test -- --watch=false`: 70 files, 787 tests pass.
- `npm run build`: passes. Initial total is 528.18 kB (baseline 516.31 kB at 39d47f4).
- `npx playwright test`: 5/5 pass, including `sighting-list.spec.ts`.

## Findings

### F1 — Map reads and persists navigation state, and the detail page gained an unplanned round trip

- **Severity**: ⚠️ WARNING
- **Impact**: 🔬 HIGH — architectural stakes; think carefully before deciding
- **Dimension**: Scope Discipline
- **Location**: PigeonWatch/Frontend/src/app/features/sightings/nearby-map-page/nearby-map-page.ts:132
- **Detail**: Plan "What We're NOT Doing" says the link from the list to the map is plain, and that the map keeps its page-local filter state and does not read a hand-over. The code does the opposite in four places:
  - The map seeds its filters from `readNearbyMapFilters(currentNavigation().extras.state)` (:132).
  - It writes the filters into history state with `Location.replaceState` on every change (:305, :345-352).
  - "Map view" passes `[state]="mapReturn()"` (sighting-list-page.html:10, :44).
  - The detail page adds "Back to list" with `returnToList`, and "Back to map" now carries `mapFilters` (sighting-detail-page.ts:93-97, .html:2-7). Three new exports from `sighting-list-hand-over.ts` and a `linkState` input on the row support this.

  None of it is recorded in the plan. It is documented in Frontend/CLAUDE.md and the p4 commit message. Manual check 4.6 ("Map view returns to the map with its default state") is ticked but contradicts the code.
- **Fix A ⭐ Recommended**: Add a plan addendum that records the map/detail state round trip as accepted scope, and reword 4.6.
  - Strength: The behaviour is guarded (same type guard, open statuses only), tested, documented in Frontend/CLAUDE.md and green in E2E, so nothing needs to be redone.
  - Tradeoff: The map's filter state is no longer purely page-local, which the earlier "Nearby filters" decision kept deliberately.
  - Confidence: HIGH — the subagent verified the guard and the history-state merge preserve router keys.
  - Blind spot: No one decided explicitly that the map should restore filters from history. A product owner may disagree.
- **Fix B**: Revert to the plan: plain map links, no `rememberFilters`, no "Back to list".
  - Strength: Restores the scope the plan and research agreed on.
  - Tradeoff: Removes working, tested UX and touches 4–5 files plus specs and the kitchen sink.
  - Confidence: MED — the revert is mechanical, but the specs depend on the new behaviour.
  - Blind spot: Users may already rely on Back from detail returning to the list.
- **Decision**: FIXED (Fix A): plan addendum A1, Progress 4.6 annotated

### F2 — Initial bundle grew 11.9 kB and the budget was raised to hide it

- **Severity**: ⚠️ WARNING
- **Impact**: 🔎 MEDIUM — real tradeoff; pause to reason through it
- **Dimension**: Success Criteria
- **Location**: PigeonWatch/Frontend/angular.json:77
- **Detail**: Criterion 4.2 ("initial bundle is not grown") is ticked, but the initial bundle went from 516.31 kB to 528.18 kB. The `maximumWarning` budget moved from 520 kB to 540 kB, so no warning fires. Measured growth:
  - Material icon-button: +8.15 kB
  - CDK a11y: +4.18 kB
  - Generated ui-labels JSON: +1.31 kB
  - Global theme overrides and tokens: +0.35 kB

  The plan's assumption that the list stays out of the initial bundle missed that resx labels and global theme overrides always land there, and that `mat-paginator` pulls shared code into the common chunk.
- **Fix A ⭐ Recommended**: Record the deviation in the plan (new baseline 528 kB, budget 540 kB, cause), and correct 4.2's evidence.
  - Strength: Honest record; the growth is modest and explained.
  - Tradeoff: The budget headroom the project protected (3.7 kB) is now 11.8 kB at a higher level, which is easier to drift into.
  - Confidence: HIGH — the numbers come from builds of both commits.
  - Blind spot: Mobile load-time impact is unmeasured.
- **Fix B**: Drop `mat-paginator` (the page already renders its own range and page-size select) for plain `mat-button`s or a custom pager, and restore the 520 kB budget.
  - Strength: Removes most of the +12 kB, and the intl class with its empty range label.
  - Tradeoff: More code to own and re-test (a11y labels, kitchen-sink `pressLabelled`, E2E Last/Previous).
  - Confidence: LOW — icon-button and a11y may still be pulled in by other initial-bundle code.
  - Blind spot: Not verified which imports actually pull those chunks into the initial bundle.
- **Decision**: FIXED (Fix A): plan addendum A2, Progress 4.2 annotated

### F3 — Unbounded page number overflows skip

- **Severity**: ⚠️ WARNING
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Safety & Quality
- **Location**: PigeonWatch/Api/BusinessLogic/Services/SightingService.cs:113
- **Detail**: `Page` is checked only for `< 1`, and `int skip = (query.Page - 1) * query.PageSize` is unchecked. `page = 2147483647, pageSize = 30` wraps to `skip = -60`, which is a 500 on SQL Server (negative OFFSET) and page 1 on SQLite. `page = 143165578` wraps to `skip = 14`, a wrong slice returned with 200. No test or endpoint fixture covers it.
- **Fix**: Add `Page > int.MaxValue / PageSize` → `InvalidPage` in validation (:173), plus a service test and an `int.MaxValue` page fixture in `EndpointFixtureRegistry.BuildList`.
- **Decision**: FIXED: `List_rejects_a_page_whose_offset_overflows` (red, then green), `Page > int.MaxValue / PageSize` → `InvalidPage` in `SightingService`, `page int.MaxValue` fixture; unit 294/294, integration 130/130

### F4 — List page UI differs from the plan in several undocumented ways

- **Severity**: ⚠️ WARNING
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Plan Adherence
- **Location**: PigeonWatch/Frontend/src/app/features/sightings/sighting-list-page/sighting-list-page.html:107
- **Detail**: The plan did not foresee any of these, and none is recorded in it (all are in Frontend/CLAUDE.md):
  - The pager is a hybrid: `mat-paginator` with `hidePageSize`, a custom page-size `mat-select`, and `getRangeLabel` returning ''. The page renders "current / last" instead of the planned "{0} – {1} of {2}", and `list.paginator.rangeEmpty` was never added.
  - The filters sit behind a toggle, closed by default (ts:107), instead of "below the header, always visible".
  - The title shows the match count (`list.page.titleWithCount`).
  - The route declares `layout: 'wide'` against "no layout flag" (app.routes.ts:43).
- **Fix**: Add a plan addendum listing these UI deviations alongside F1/F2.
- **Decision**: FIXED: plan addendum A3

### F5 — Manual check 4.6 ticked against contradicting code

- **Severity**: 💡 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Success Criteria
- **Location**: context/changes/urgency-ranked-sighting-list/plan.md:478
- **Detail**: "Map view returns to the map with its default state" is `[x]`, but Map view passes `{ mapFilters }` and the map restores them. Either the check was rubber-stamped or it was run against the new behaviour.
- **Fix**: Reword 4.6 to the actual behaviour (resolved together with F1).
- **Decision**: FIXED: covered by the F1 annotation on Progress 4.6

### F6 — Frontend/CLAUDE.md contradicts itself on the "Map view" link

- **Severity**: 💡 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Plan Adherence
- **Location**: PigeonWatch/Frontend/CLAUDE.md:26
- **Detail**: The Sighting list bullet says "Map view" is a plain `routerLink="/"`, while the Nearby filters paragraph and the code say it passes `{ mapFilters }`.
- **Fix**: Change line 26 to say Map view carries the list's current map filters as `mapFilters` state.
- **Decision**: FIXED: Frontend/CLAUDE.md:26 corrected

### F7 — E2E paging step can race the re-render, and the no-match step depends on no status endpoint

- **Severity**: 💡 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Safety & Quality
- **Location**: PigeonWatch/Frontend/tests/e2e/sighting-list.spec.ts:99
- **Detail**: Two risks:
  - `lastPage.isEnabled()` is a one-shot check right after `waitForResponse`, while the pager still shows the old `totalCount`. Clicking Last on stale state can request a past-end page and catch the wrong response.
  - The Adopted no-match assertion (:123) breaks once S-09 lets statuses change, because the database accumulates across runs.
- **Fix**: Wait for the rendered page number or title count before paging, and scope the no-match check to a combination unique to this run.
- **Decision**: FIXED: wait for the rendered Urgent count heading and page only when it exceeds one page; no-match asserted when the resolved response is empty, otherwise the count heading; own row absent either way. E2E green.

### F8 — Past-end recovery can end on an empty last page with a stale count

- **Severity**: 💡 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Safety & Quality
- **Location**: PigeonWatch/Frontend/src/app/features/sightings/sighting-list-page/sighting-list-page.ts:219
- **Detail**: The `recovering` flag correctly limits recovery to one retry. If the recovered last page is also empty (rows removed in between), the page shows `list.page.empty` while the title still shows a count above 0 and the pager stays visible.
- **Fix**: Accept it, or show the empty state using the recovery response's `totalCount`.
- **Decision**: FIXED: a still-empty recovered page now falls back to page 1 (never past the end, so recursion stops); spec "does not chase the last page twice" replaced by two red→green specs; Frontend/CLAUDE.md updated

### F9 — A 400 from sightings/list is shown without a reference and not reported

- **Severity**: 💡 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Safety & Quality
- **Location**: PigeonWatch/Frontend/src/app/features/sightings/sighting-list-page/sighting-list-page.ts:239
- **Detail**: `isExpectedClientStatus` treats 400 as expected, so it is not reported. The SPA never sends user-correctable input to this endpoint, so a 400 would mean an SPA bug. The API still logs the codes at Warning, so the evidence is kept. This matches My sightings and nearby.
- **Fix**: Optional: report 400s from `sightings.list`, or accept the current pattern.
- **Decision**: FIXED: `failureState` reports a 400 and shows its trace id (401/404/429 stay silent); reporting spec red→green; Frontend/CLAUDE.md updated
