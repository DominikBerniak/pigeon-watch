<!-- IMPL-REVIEW-REPORT -->
# Implementation Review: Sighting Detail UI Contract

- **Plan**: context/changes/sighting-detail-ui-contract/plan.md
- **Scope**: Full plan (4 of 4 phases)
- **Reviewed phases**: 1, 2, 3, 4
- **Date**: 2026-10-18
- **Verdict**: NEEDS ATTENTION
- **Findings**: 0 critical, 3 warnings, 7 observations

## Verdicts

| Dimension | Verdict |
|-----------|---------|
| Plan Adherence | PASS |
| Scope Discipline | PASS |
| Safety & Quality | PASS |
| Architecture | PASS |
| Pattern Consistency | PASS |
| Success Criteria | WARNING |

Automated criteria re-run on the committed tree: 507 tests pass, build 488.06 kB (budget 500 kB), UiResourceTests 9 pass, hardcoded-value scan 0 hits, rules-file grep matches. Progress: 31 of 31 rows `[x]`, each criterion maps one to one to its row.

## Findings

### F1 — map-view spec never exercises the input's default

- **Severity**: ⚠️ WARNING
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Success Criteria
- **Location**: PigeonWatch/Frontend/src/app/shared/map/map-view/map-view.spec.ts (render helper, "makes interactive markers a labelled tab stop" case)
- **Detail**: `render()` always calls `setInput('interactiveMarkers', …)` with an explicit value, so flipping the input's default to `false` would pass every spec, although the nearby map and the location picker rely on the default `true` and their specs run on the stub. The case name says "labelled" but never asserts the marker `title`.
- **Fix**: Set the input only when the argument is given (or add one case that renders without it), and assert `title` or drop "labelled" from the name.
- **Decision**: FIXED — render only sets the input when given; default and title now asserted

### F2 — no spec for a route-param change between two sightings

- **Severity**: ⚠️ WARNING
- **Impact**: 🔎 MEDIUM — real tradeoff; pause to reason through it
- **Dimension**: Success Criteria
- **Location**: PigeonWatch/Frontend/src/app/features/sightings/sighting-detail-page/sighting-detail-page.spec.ts
- **Detail**: Title reset, `mapShown` reset, heading focus re-run and the cancellation of an in-flight load when the id changes are untested; the `loadsStarted > 0` branch of `applyState` is only exercised for non-navigation pages. The plan named the id change as an always-focus case.
- **Fix**: Add one spec that navigates `/sightings/a` then `/sightings/b` with a Subject for the first request, and asserts the final title, the heading focus and a hidden map.
  - Strength: Covers the only path where `focusOnSettle`, `mapShown` and `switchMap` interact; the router harness already exists in the file.
  - Tradeoff: One more spec with a Subject to drive; low maintenance.
  - Confidence: HIGH — same harness and helpers as the retry spec.
  - Blind spot: Whether the harness keeps the component instance between the two navigations.
- **Decision**: FIXED — added two id-change specs (a to b restart, slow answer ignored); break-check: replacing switchMap turned the slow-answer spec red

### F3 — "sink takes no focus on load" is guarded only by one manual check

- **Severity**: ⚠️ WARNING
- **Impact**: 🔎 MEDIUM — real tradeoff; pause to reason through it
- **Dimension**: Success Criteria
- **Location**: PigeonWatch/Frontend/src/app/dev/kitchen-sink/kitchen-sink.spec.ts (retry case, ~441-454)
- **Detail**: The sink spec has no router navigation, so `currentNavigation()` is always null there and the real dev route is not covered; `document.activeElement` is asserted only after the manual retry. The browser check in visual-notes.md covered a fresh load only, not entering the sink by in-app navigation.
- **Fix**: In the retry case, assert `document.activeElement` is not the cell heading before the header button is pressed; record the in-app-navigation entry as an unverified item in visual-notes.md.
  - Strength: Turns the invariant into a regression test without a router-driven sink spec.
  - Tradeoff: It cannot prove the real route; the gap stays recorded, not closed.
  - Confidence: MED — the check proves the stubbed path only.
  - Blind spot: Router timing when the sink is reached by an in-app link, not a reload.
- **Decision**: FIXED — sink retry case asserts no detail heading holds focus before the press; in-app-navigation entry listed as unverified in visual-notes.md

### F4 — display-only markers keep a pointer cursor

- **Severity**: 💡 OBSERVATION
- **Impact**: 🔎 MEDIUM — real tradeoff; pause to reason through it
- **Dimension**: Safety & Quality
- **Location**: PigeonWatch/Frontend/src/app/shared/map/map-view/map-view.ts:198-203
- **Detail**: With `keyboard: false` the marker stays a Leaflet interactive layer, so `leaflet.css` gives it `cursor: pointer`: a click affordance with no action. `interactive: false` would remove the cursor but also sets `pointer-events: none`, which kills the hover `title` the plan promised.
- **Fix A ⭐ Recommended**: Add a static modifier class to display-only marker icons and set `cursor: default` for it in `_common.scss`.
  - Strength: Keeps the hover title and removes the false affordance, using the existing marker-class mechanism.
  - Tradeoff: One new `pw-map-marker--static` class in `_common.scss` and `map-marker.ts`, so the style guard needs it declared.
  - Confidence: MED — the cursor override is expected to win over Leaflet's rule but was not tried.
  - Blind spot: Cascade order between `leaflet.css` (lazy, injected) and the global stylesheet.
- **Fix B**: Accept the pointer cursor and note it in visual-notes.md.
  - Strength: No code change.
  - Tradeoff: A pointer cursor over a non-clickable marker.
  - Confidence: HIGH — nothing else depends on it.
  - Blind spot: Users may try to click it.
- **Decision**: FIXED via Fix A — pw-map-marker--static class on display-only icons, cursor: default in _common.scss, asserted in map-view.spec.ts

### F5 — two deferrals live only in research.md

- **Severity**: 💡 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Scope Discipline
- **Location**: context/changes/sighting-detail-ui-contract/research.md (charge 2 and 3 status lines)
- **Detail**: The 28rem card on large screens (deferred to `app-wide-large-screen-layout`) and the Show map/Hide map label flip with `aria-expanded` are marked deferred in research.md but are not in the plan's "What We're NOT Doing", which a later reader treats as the scope record.
- **Fix**: Add both lines to "What We're NOT Doing" in plan.md as a short addendum.
- **Decision**: FIXED — addendum added to What We are NOT Doing in plan.md

### F6 — spec names and weak assertions overclaim

- **Severity**: 💡 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Success Criteria
- **Location**: sighting-detail-page.spec.ts:232, :202-216, :289
- **Detail**: "focuses the heading in the not-found and error states" only covers a 404. The not-found and error title cases expect the same string as the loading case, so they would pass if the effect never re-ran after settling. "hides a shown map when Try again reloads" is driven by emitting loaded then error on one Subject; in the real UI Try again exists only in the error state, so the reset is reachable only through an id change.
- **Fix**: Rename or extend the focus case to a 500, and keep the retry-reset case under a name that says it is the reload reset.
- **Decision**: FIXED — focus case split into not-found and error; reload-reset case renamed and now also asserts the title follows

### F7 — visual-notes.md is thinner than the Phase 3 contract

- **Severity**: 💡 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Plan Adherence
- **Location**: context/changes/sighting-detail-ui-contract/visual-notes.md
- **Detail**: The plan asked for a visible ring on Back, the toggle and Try again; rings are recorded for Back, the map region, the zoom controls and the attribution link only. The Tab pass started from the map-shown cell rather than "Show map then Back"; hover and focus-visible rows have no cell ids; no full-group desktop or 375 px screenshot is recorded.
- **Fix**: Add the toggle and Try again ring checks and cell ids to the matrix, or list them under "Not verified".
- **Decision**: FIXED — cell ids added and the missing ring, Tab-start, in-app navigation and screenshot gaps listed under Not verified (no new browser run)

### F8 — sighting-detail-page.ts departs from sibling field conventions

- **Severity**: 💡 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Pattern Consistency
- **Location**: PigeonWatch/Frontend/src/app/features/sightings/sighting-detail-page/sighting-detail-page.ts:78,146-148
- **Detail**: `Router` is injected inline in a field initializer and `DestroyRef` inline in the constructor, where siblings keep `private readonly` fields; the `focusOnSettle` plus `loadsStarted` pair works but is hard to read.
- **Fix**: Store `router` and `destroyRef` as private readonly fields and fold the two flags into one set in the loading branch.
- **Decision**: FIXED — router and destroyRef are private readonly fields; one focusOnSettle flag, previous state decides

### F9 — nine sink cells share one document title

- **Severity**: 💡 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Safety & Quality
- **Location**: PigeonWatch/Frontend/src/app/dev/kitchen-sink/state-cell.ts
- **Detail**: Every detail cell sets the tab title and resets it on destroy, so the dev tab title is whichever cell ran last.
- **Fix**: Accept as dev-only (the plan did), or stub `Title` in `CellStubs`.
- **Decision**: SKIPPED — accepted as dev-only, as the plan did

### F10 — test hygiene in two specs

- **Severity**: 💡 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Pattern Consistency
- **Location**: page-card.spec.ts:51-66, kitchen-sink.spec.ts:395-400
- **Detail**: The page-card case appends the fixture root to `document.body` and removes it manually, which TestBed already does, and asserts `tabIndex` and the `tabindex` attribute twice over. The long-name case asserts that fixture data contains a space instead of behaviour.
- **Fix**: Drop the manual attach/remove and one duplicate assertion; assert the heading text, not the fixture.
- **Decision**: FIXED — manual attach/remove and duplicate assertion dropped; fixture assertion removed
