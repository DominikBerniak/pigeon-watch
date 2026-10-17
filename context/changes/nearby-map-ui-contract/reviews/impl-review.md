<!-- IMPL-REVIEW-REPORT -->
# Implementation Review: Nearby Map UI Contract

- **Plan**: context/changes/nearby-map-ui-contract/plan.md
- **Scope**: Full plan
- **Reviewed phases**: 1, 2, 3, 4, 5
- **Date**: 2026-10-17
- **Verdict**: NEEDS ATTENTION
- **Findings**: 0 critical, 3 warnings, 6 observations

## Verdicts

| Dimension | Verdict |
|-----------|---------|
| Plan Adherence | WARNING |
| Scope Discipline | WARNING |
| Safety & Quality | WARNING |
| Architecture | PASS |
| Pattern Consistency | PASS |
| Success Criteria | WARNING |

## Automated verification (re-run 2026-10-17)

- `npm test -- --watch=false`: PASS, 43 files, 466 tests
- `npm run build`: PASS, initial total 487.73 kB (warning budget 500 kB)
- `dist/` contains no `kitchen-sink`: PASS
- No `mat-progress-spinner` in templates outside `shared/ui`: PASS
- No `spinnerDiameter` under `src/app`: FAIL as worded (`shared/ui/submit-button/submit-button.ts:22`), see F7
- Hardcoded-value scan on view, feature, shared, map, sink files and `app.scss`: 0 hits
- `Frontend/CLAUDE.md` names the five required terms and has no `context/changes/profile-ui-contract/` path: PASS

## Findings

### F1 — Search can run while locating, and a late location answer overrides it

- **Severity**: ⚠️ WARNING
- **Impact**: 🔎 MEDIUM — real tradeoff; pause to reason through it
- **Dimension**: Safety & Quality
- **Location**: PigeonWatch/Frontend/src/app/features/sightings/nearby-map-page/nearby-map-page.ts:125-127, 133-134, 236-255
- **Detail**: `searchDisabled` is `zoomedOut() || state().kind === 'loading'` and ignores `locating()`. After a failed lookup at zoom ≥ 10, pressing "Use my location" leaves "Search this area" enabled (also on first load if the user zooms to 10 while the permission prompt is open). A search then runs, but `status()` returns `'locating'` first, so the user never sees their loading or found text. When the location answers `ok`, `applyLocation` recentres the map and calls `search(centre)`, and `switchMap` drops the user's search. The plan's "only the latest request moves the map" is enforced between location requests only, not against a manual search. No spec covers it. `aria-describedby` on the disabled button also announces "Finding your location…" while the real reason is zoom or loading.
- **Fix A ⭐ Recommended**: Add `|| this.locating()` to `searchDisabled`, plus a spec
  - Strength: One-line change, consistent with the existing "disabled while loading" rule; the status already says why ("Finding your location…").
  - Tradeoff: On first load a user who wants to search before answering the permission prompt must wait for the prompt or timeout.
  - Confidence: MED — simple, but blocks a user action during a browser prompt of unknown length.
  - Blind spot: The configured geolocation timeout length (how long Search could stay disabled).
- **Fix B**: A manual search cancels the pending location request (e.g. `takeUntil` the search stream on the location request, then `locating.set(false)`), plus a spec
  - Strength: Respects the user's latest explicit action; never blocks Search.
  - Tradeoff: More state transitions in the page; the "Use my location" press is silently abandoned.
  - Confidence: MED — needs care so `positionUnknown`/heading and the button stay coherent.
  - Blind spot: Interaction with the kitchen-sink `timeoutThenNext` stub cells.
- **Decision**: FIXED (Fix A: Search disabled while locating; spec added; Frontend/CLAUDE.md geolocation bullet updated)

### F2 — Keyboard focus drops to body after "Use my location" and Retry

- **Severity**: ⚠️ WARNING
- **Impact**: 🔎 MEDIUM — real tradeoff; pause to reason through it
- **Dimension**: Safety & Quality
- **Location**: PigeonWatch/Frontend/src/app/features/sightings/nearby-map-page/nearby-map-page.html:18-27
- **Detail**: "Use my location" gets native `[disabled]="locating()"` while focused, so the browser moves focus to `body`; on `ok` the button is removed (measured in visual-notes F1, deferred to this review). Retry exists only while `status() === 'error'`; pressing it sets `loading` and the `@if` removes the focused button. The page already uses `disabledInteractive` on "Search this area" for exactly this reason. Keyboard and screen-reader users lose their place right after the page's main recovery actions.
- **Fix**: Add `disabledInteractive` to "Use my location" and guard `useMyLocation()` with `if (this.locating()) return;`; when the button or Retry disappears (location `ok`, retry started), move focus to "Search this area" via `afterNextRender`; add specs for both.
  - Strength: Matches the pattern already used on Search; fixes both measured focus losses in one place.
  - Tradeoff: Moving focus programmatically after success is a behaviour decision the plan did not make (Phase 4 said "natively disabled").
  - Confidence: HIGH — mechanism measured in the browser in Phase 5.
  - Blind spot: Screen-reader announcement order of the status text vs. the focus move is not verified.
- **Decision**: FIXED (Use my location is disabledInteractive with a guard; focus moves to Search this area after a successful retry or Try again; page and kitchen-sink specs updated)

### F3 — Map home hand-builds its error+retry instead of `app-error-retry`; plan not amended

- **Severity**: ⚠️ WARNING
- **Impact**: 🔎 MEDIUM — real tradeoff; pause to reason through it
- **Dimension**: Plan Adherence
- **Location**: PigeonWatch/Frontend/src/app/features/sightings/nearby-map-page/nearby-map-page.html:18-22, 63-65
- **Detail**: Plan (Phase 4 #3, Desired End State) says the error state renders `app-error-retry` in the slot and that it is "the only implementation of those blocks in the app". Phases 2-4 did that; commit 39af1f5 replaced it with a stroked Retry button in the toolbar plus a separate `app-alert kind="error"` row, after manual review. `Frontend/CLAUDE.md` now carries an exception and `visual-notes.md:78,114` records it, but `plan.md` was never amended, and `visual-notes.md:47` says "No production view file changed in this phase" although 39af1f5 changed `nearby-map-page.{html,ts,spec.ts}`. This reintroduces one of the C3 duplications the change set out to remove.
- **Fix A ⭐ Recommended**: Add an addendum to `plan.md` (Phase 5 revision of the map error layout, and the exception in `Frontend/CLAUDE.md`) and correct `visual-notes.md:47`
  - Strength: The layout was a deliberate decision after manual review; the plan becomes accurate before archive and before the `sighting-detail-ui-contract` follow-up reads it.
  - Tradeoff: Accepts one feature-local error+retry pair.
  - Confidence: HIGH — documentation-only, the code and CLAUDE.md already agree.
  - Blind spot: S-05/S-06 may copy the toolbar pattern and widen the exception.
- **Fix B**: Give `app-error-retry` a layout that projects the button and the message into separate slots, and use it on the map home
  - Strength: Restores "one implementation" and the promote-to-shared/ui rule.
  - Tradeoff: A two-slot component spanning two rows is awkward API; changes a component that three other places use.
  - Confidence: LOW — the CLAUDE.md note already says one component cannot span both rows.
  - Blind spot: Whether content projection into two separate DOM places is clean in this layout.
- **Decision**: FIXED (Fix A: plan.md addendum; visual-notes.md:47 corrected)

### F4 — A second failed location lookup shows the hint over real results

- **Severity**: 💡 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Safety & Quality
- **Location**: PigeonWatch/Frontend/src/app/features/sightings/nearby-map-page/nearby-map-page.ts:240-243
- **Detail**: `applyLocation` sets `awaitingSearch(true)` on every failure. Location fails → user searches (3 results) → "Use my location" fails again → status shows `map.hint` while the 3 markers remain, instead of the found count.
- **Fix**: Use `this.awaitingSearch.set(this.state().kind === 'idle')` in the failure branch, plus a spec.
- **Decision**: FIXED (hint only when no search has run; spec added)

### F5 — Zooming below 10 hides an error and its Retry

- **Severity**: 💡 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Safety & Quality
- **Location**: PigeonWatch/Frontend/src/app/features/sightings/nearby-map-page/nearby-map-page.ts:136
- **Detail**: `zoomToSearch` is checked before `error`, so zooming out after a failed search hides the alert and Retry, although `retry()` repeats `lastSearchCentre` regardless of zoom. The plan's precedence list leaves error's position open; `visual-notes.md:92` records the behaviour but not as a decision.
- **Fix**: Record the precedence as intended in the plan addendum (F3), or check `error` before `zoomToSearch` if Retry should stay reachable.
- **Decision**: FIXED (precedence recorded as intended in the plan.md addendum; no code change)

### F6 — Deferred layout shifts and a status that contradicts a failed map

- **Severity**: 💡 OBSERVATION
- **Impact**: 🔎 MEDIUM — real tradeoff; pause to reason through it
- **Dimension**: Success Criteria
- **Location**: context/changes/nearby-map-ui-contract/visual-notes.md:132-134
- **Detail**: Phase 5 deferred three measured defects without a home: at 375px a failed geolocation moves the map 60 px down (Use my location wraps to a second row); a nearby error moves the map 61 px down at every width (the alert row); with the map library failed the status still reports "N open sightings within 10 km". The plan's C4 goal was "the map stops jumping". Not verified either: the Tab pass at 375/`md`, screen-reader announcements, the page against the local API.
- **Fix**: Queue them in `context/changes/nearby-map-ui-contract/follow-ups/review-fixes.md` (or the `sighting-detail-ui-contract` change) with the layout options already noted, so they survive archive.
  - Strength: Keeps the evidence and options already measured; no code churn now.
  - Tradeoff: The jump stays in production until the follow-up.
  - Confidence: HIGH — the defects and their causes are measured in visual-notes.
  - Blind spot: Whether a reserved second toolbar row at 375 looks acceptable empty.
- **Decision**: FIXED (queued in follow-ups/review-fixes.md)

### F7 — Criterion 2.2 marked passing although its grep returns a hit

- **Severity**: 💡 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Success Criteria
- **Location**: PigeonWatch/Frontend/src/app/shared/ui/submit-button/submit-button.ts:22
- **Detail**: 2.2 says "no `spinnerDiameter` remains under `src/app` (grep returns nothing)"; the pre-existing `submit-button` keeps `spinnerDiameter = 18`. The intent (no feature-level diameter, Desired End State) holds; the criterion's wording does not, and it was checked anyway.
- **Fix**: Reword 2.2 in `plan.md` to "no `spinnerDiameter` under `src/app/features` or `shared/map`".
- **Decision**: FIXED (fixed differently: scope note in the plan.md addendum, Progress titles untouched per the Progress convention)

### F8 — Holding Enter or Space on a marker selects repeatedly

- **Severity**: 💡 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Safety & Quality
- **Location**: PigeonWatch/Frontend/src/app/shared/map/map-view/map-view.ts:207-212
- **Detail**: `onMarkerKey` emits `markerSelect` on every `keydown`, including auto-repeat; each emission schedules another `afterNextRender` focus. Harmless today.
- **Fix**: Return early when `event.originalEvent.repeat` is true (still `preventDefault` for Space), plus a spec case.
- **Decision**: FIXED (auto-repeat keydown ignored, default still prevented; spec added)

### F9 — Unplanned but benign additions

- **Severity**: 💡 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Scope Discipline
- **Location**: PigeonWatch/Frontend/src/app/dev/dev-routes.ts, PigeonWatch/Frontend/src/app/shared/map/map-marker.ts, PigeonWatch/Frontend/src/app/dev/kitchen-sink/cell-stubs.ts:119-133, cell-definitions.ts:267-289
- **Detail**: Not in Changes Required: the sink route declares `layout: 'wide'` (dev only, replaced by `dev-routes.prod.ts` in production); `mapMarkerSelectedClassName` constant in `map-marker.ts`; the tile-less loader also forces `zoomAnimation: false`; the selected cells moved from on-load click to a manual header button. All documented in `visual-notes.md` and `Frontend/CLAUDE.md`, none in `plan.md`.
- **Fix**: List them in the same plan addendum as F3.
- **Decision**: FIXED (listed in the plan.md addendum)
