# Nearby map review fixes Implementation Plan

## Overview

Fix three review findings on the map home (`/`) and `app-map-view`, carried from the archived `nearby-map-ui-contract`: the map moving 60 px down at 375 px after a failed location (F2), the map moving down on a nearby error (F3), and the status reporting search results over a map that failed to load (F4). One view, existing design system, no new tokens and no new `shared/ui` component. Do it before S-05 (filters) and S-06 (urgency list) build on the map toolbar.

## Current State Analysis

All three defects are reproduced in the kitchen sink and traced to code in `context/changes/nearby-map-review-fixes/research.md`.

- **F2.** The actions container is `.pw-cluster` with `flex: none` (`nearby-map-page.scss:18-20`), so the third child wraps when the row is too narrow. "Use my location" renders only when the last location outcome is `denied`, `unavailable` or `timeout` (`nearby-map-page.html:24-34`, `nearby-map-page.ts:56-60,131-135`). Search (166 px) + gap (12) + Use my location (168) = 346 px against a derived real content width of 343 px at a 375 px viewport (`app.scss:22-26`: 16 px side padding), so the page wraps by 3 px; the sink cell is 34 px narrower (309 px) and shows the same wrap. Below `md` the toolbar is a column, so the extra row moves the map down 60 px.
- **F3.** In the error status the `role="status"` element is visually hidden (`nearby-map-page.html:41`) and the error `app-alert` is a sibling after the toolbar (`nearby-map-page.html:70-72`). Measured in the sink: the map moves +13 px at 375 and +61 px at `md`. A prototype with the alert inside `.nearby-toolbar__slot` and `min-height: 3rem` on the slot gave zero shift at both widths.
- **F4.** `MapView` keeps the load failure in a `protected` signal (`map-view.ts:104`) with no output; `mapReady` fires only on success (`map-view.ts:163`). The page searches whenever a position is known (`nearby-map-page.ts:257-264`) and `status()` has no input from the map (`nearby-map-page.ts:136-148`), so "N open sightings within 10 km" sits over the map's load error, and with an unknown position "Zoom in to search an area" sits over a map that cannot zoom.
- **Consumers of `app-map-view`:** the map home, `location-picker.html:5` and `sighting-detail-page.html:66`. An added output is additive for the other two. `MapViewStub` mirrors the outputs and must gain the same one.

## Desired End State

- At 360 and 375 px, "Search this area" and "Locate me" sit on one row after a failed location; the map does not move when the button appears.
- A nearby error shows its message inside the toolbar's status slot beside the buttons, with Try again still in the toolbar; the map does not move between "Found" and the nearby error at 375 px and `md`, for the common case of a known position.
- While the map failed to load, the page shows no result count, no "empty" message, no "Zoom in to search an area" and no "Move the map…" hint, and Search is disabled; "Finding your location…", the loading indicator and the nearby error remain true statements and stay. When the map recovers, the status texts return.
- The sink has cells for the two combinations that had none (failed location plus nearby error, failed map with unknown position), and a `visual-notes.md` records the measured offsets and the manual keyboard and screen-reader checks.

### Key Discoveries:

- The actions container never shrinks and the third button wraps (`nearby-map-page.scss:18-20`, `styles/_common.scss:14-19`).
- The slot already carries a 3rem floor through `.nearby-status` (`nearby-map-page.scss:29-33`); F3 extends that rule to the error instead of adding a second place for feedback.
- A component output cannot share a name with the existing `loadFailed` signal member (`map-view.ts:104`); the signal is renamed so the output can be `loadFailed`.
- `map.useMyLocation` is shared with the location picker (`location-picker.html:17`); the map home needs its own key.
- The kitchen-sink spec swaps `MapView` for `MapViewStub` on `NearbyMapPage` (`Frontend/CLAUDE.md`, Kitchen sink), so the sink spec cannot observe the real load failure; F4 behaviour is asserted in the page spec by emitting from the stub, and the sink cells are visual evidence.
- Spec constraints that change with the fix: `nearby-map-page.spec.ts:394-398` asserts `toolbar.nextElementSibling` is the alert, and the page spec and sink spec name the button "Use my location".

## What We're NOT Doing

- Not keeping three buttons on one row at 375 px. After a failed location *and* a nearby error the toolbar has Search, Try again and Locate me; the third button still wraps at 375 px (F2 again) and the map still moves about 18 px at `md` (the 200 px slot wraps the alert). This rare combination is accepted and recorded in `visual-notes.md`; it was reproduced only on hand-built DOM and is now reproducible in a sink cell.
- Not reserving the second toolbar row or the alert row from first render (it would add 60 px or 61 px to every load, the common path).
- Not changing the shared `map.useMyLocation` label, the location picker or the detail page.
- Not adding an icon button, a text-variant button, a new token or a new `shared/ui` component.
- Not adding a new status text for a failed map; the map's own error and Retry carry the message.
- Not running the page against the local API with real data (the `authGuard` route needs login); it stays listed as not verified in `visual-notes.md`.
- Not touching `sighting-detail-ui-contract`; one view per change.
- Not adding a Polish (or any other) culture; a longer future label could wrap again and would need its own check.

## Implementation Approach

Three phases. F4 first because it touches the shared map component and is independent of the toolbar layout. Then F2 and F3 together, because both edit the same toolbar markup, spec and sink cells. Last, a measurement and manual-check gate that records evidence, because jsdom has no layout and the shifts can only be verified in a real browser.

## Phase 1: Map load-failure signal (F4)

### Overview

`app-map-view` reports a load failure through a new `loadFailed` output; the page uses it to suppress the four status texts that cannot be acted on next to a failed map and to disable Search.

### Changes Required:

#### 1. The map component

**File**: `PigeonWatch/Frontend/src/app/shared/map/map-view/map-view.ts`, `PigeonWatch/Frontend/src/app/shared/map/map-view/map-view.html`

**Intent**: Expose the load failure so a page can react. Rename the internal `protected loadFailed` signal to `failed` (template uses `failed()`), and add an output named `loadFailed`.

**Contract**: `readonly loadFailed = output<boolean>()`. Emits `true` each time a load fails, and `false` when a retry starts after a failure. It does not emit on a first successful load, so the location picker and the detail page, which ignore it, see no change.

#### 2. The stub

**File**: `PigeonWatch/Frontend/src/app/shared/map/testing/map-view-stub.ts`

**Intent**: Keep the stub's outputs the same as the real component's (repo rule: same selector, inputs and outputs).

**Contract**: add `readonly loadFailed = output<boolean>()` beside `mapReady`.

#### 3. The page rule

**File**: `PigeonWatch/Frontend/src/app/features/sightings/nearby-map-page/nearby-map-page.ts`, `PigeonWatch/Frontend/src/app/features/sightings/nearby-map-page/nearby-map-page.html`

**Intent**: Track whether the map failed and stop reporting what the user cannot act on. Bind `(loadFailed)` on `app-map-view` to a `mapFailed` signal.

**Contract**:
- `status()`: `locating` stays first. Then, while `mapFailed()`, the result is the nearby state's `loading` or `error` when that is the state, otherwise `none`. This drops `found`, `empty`, `zoomToSearch` and `hint` only while the map is failed.
- `searchDisabled()` is also true while `mapFailed()`.
- The Search button's `aria-describedby` points at the status id only when it is disabled and the status text exists (`status() !== 'none'`), so a disabled Search never describes an empty element.
- On `loadFailed(false)` the signal clears and the normal statuses return; markers return on their own because `applyMarkers` reads `ready()` and `sightings()` was filled in the background.

#### 4. Specs

**File**: `PigeonWatch/Frontend/src/app/shared/map/map-view/map-view.spec.ts`, `PigeonWatch/Frontend/src/app/features/sightings/nearby-map-page/nearby-map-page.spec.ts`

**Intent**: Pin the new output and the page rule.

**Contract**:
- `map-view.spec.ts` (the "fail then retry" case): record `loadFailed` emissions; expect `[true, false]` after the failed load and the retry, and no emission on a first successful load.
- `nearby-map-page.spec.ts`: with a known position and the stub emitting `loadFailed(true)`, the status text is empty and does not contain the count, Search is disabled; with an unknown position and zoom below 10, "Zoom in to search an area" is absent; the hint is absent at zoom 10; `loadFailed(false)` brings each text back; the nearby `loading` indicator and the `error` alert remain while the map is failed.

#### 5. Sink cell and rule file

**File**: `PigeonWatch/Frontend/src/app/dev/kitchen-sink/cell-definitions.ts`, `PigeonWatch/Frontend/src/app/dev/kitchen-sink/kitchen-sink.spec.ts`, `PigeonWatch/Frontend/CLAUDE.md`

**Intent**: Give the combination "map failed, position unknown" a cell, and record the new output in the rule file.

**Contract**:
- New cell `map-load-failed-unknown-position` in the Nearby map group: `geolocation: 'denied'`, `nearby: 'items'`, `leaflet: 'failure'`, no steps. `kitchen-sink.spec.ts` asserts the cell exists and that its `LEAFLET_LOADER` fails, like the existing `map-load-failed` check. The sink spec runs the page over `MapViewStub`, so it does not assert the suppressed status; that is the page spec's job.
- `Frontend/CLAUDE.md`: in "`app-map-view` API" add `loadFailed` to the outputs with its `true`/`false` contract, and in the Geolocation bullet add one sentence that a failed map drops the result, hint and zoom statuses and disables Search. In the Kitchen sink bullet add the new cell to the Nearby map list.

### Success Criteria:

#### Automated Verification:

- Frontend unit tests pass: `npm test -- --watch=false` (from `PigeonWatch/Frontend`)
- Production build passes within the bundle budget: `npm run build` (from `PigeonWatch/Frontend`)
- `map-view.spec.ts` asserts `loadFailed` emits `true` on a failed load, `false` when the retry starts, and does not emit on a first successful load
- `nearby-map-page.spec.ts` asserts that while the map is failed the count, empty, zoom and hint texts are absent, Search is disabled and `aria-describedby` is not set on an empty status, and that each text returns when the map recovers

#### Manual Verification:

- In `npm start` at `/dev/kitchen-sink`, the "Map library failed to load" cell shows no "N open sightings" text over the map's error
- The new "map failed, position unknown" cell shows no "Zoom in to search an area" over the map's error and keeps Search disabled

**Implementation Note**: After completing this phase and all automated verification passes, pause here for manual confirmation from the human that the manual testing was successful before proceeding to the next phase. Phase blocks use plain bullets — the corresponding `- [ ]` checkboxes for these items live in the `## Progress` section at the bottom of the plan.

---

## Phase 2: Toolbar layout (F2, F3)

### Overview

"Use my location" gets a shorter label on the map home so two buttons fit one row at 343 px, and the nearby error moves from its own row into the toolbar's status slot so it replaces the status text in place.

### Changes Required:

#### 1. The label

**File**: `PigeonWatch/Api/BusinessObjects/Resources/UiLabels.resx`, `PigeonWatch/Frontend/src/app/features/sightings/nearby-map-page/nearby-map-page.html`

**Intent**: A map-home-only label that fits beside "Search this area" in 343 px; the shared label and the picker stay as they are.

**Contract**: new key `map.locateMe` with value `Locate me`, next to `map.useMyLocation`. The page button uses `map.locateMe`; `location-picker.html` keeps `map.useMyLocation`. Measured text width is 75 px, so Search 166 + gap 12 + button 125 = 303 px, which also fits the 309 px sink cell.

#### 2. The error in the slot

**File**: `PigeonWatch/Frontend/src/app/features/sightings/nearby-map-page/nearby-map-page.html`, `PigeonWatch/Frontend/src/app/features/sightings/nearby-map-page/nearby-map-page.scss`

**Intent**: Place the error `app-alert` inside `.nearby-toolbar__slot` as a sibling of the status element (not nested in the `role="status"` element), so the toolbar keeps its height and the status element still exists from the first render. Give the slot the control-height floor so a 45 px alert does not shrink the row below the 3rem the status text reserves.

**Contract**: the `@if (status() === 'error')` alert block moves from after `.nearby-toolbar` to inside `.nearby-toolbar__slot`; `.nearby-toolbar__slot` gets `min-height: var(--pw-control-height)`. Try again stays in `.nearby-toolbar__actions` beside Search (earlier decision, archived plan addendum). Tokens only, no literals.

#### 3. Page and sink specs

**File**: `PigeonWatch/Frontend/src/app/features/sightings/nearby-map-page/nearby-map-page.spec.ts`, `PigeonWatch/Frontend/src/app/dev/kitchen-sink/kitchen-sink.spec.ts`, `PigeonWatch/Frontend/src/app/dev/kitchen-sink/cell-definitions.ts`, `PigeonWatch/Frontend/src/app/dev/kitchen-sink/state-cell.html`

**Intent**: Follow the renamed button and the moved alert.

**Contract**:
- Every "Use my location" button lookup on the map home becomes "Locate me" (page spec, sink spec `buttonOf(..., 'map.locateMe')`, the cell `press('map.locateMe')` step, and the labels/text in `cell-definitions.ts` and `state-cell.html`). `location-picker.spec.ts` keeps "Use my location".
- The error-case page spec (`:394-416`) asserts the alert is inside `.nearby-toolbar__slot`, is not the toolbar's next sibling, Try again is in `.nearby-toolbar__actions`, the page still has exactly one `[role="status"]` element, and the existing retry, focus and recovery assertions keep passing.

#### 4. Sink cell for the combined case

**File**: `PigeonWatch/Frontend/src/app/dev/kitchen-sink/cell-definitions.ts`, `PigeonWatch/Frontend/src/app/dev/kitchen-sink/kitchen-sink.spec.ts`

**Intent**: Make the three-button case reproducible without hand-built DOM.

**Contract**: new cell `map-nearby-error-after-denied`: `geolocation: 'denied'`, `nearby: 'failure'`, `leaflet: 'tileless'`, steps zoom in four times, `press('map.searchArea')`, wait for the alert. The sink spec asserts the cell shows Search, Try again and Locate me together and the alert inside the slot.

#### 5. Rule file

**File**: `PigeonWatch/Frontend/CLAUDE.md`

**Intent**: Keep the rules true.

**Contract**: in "Spinners, errors with retry…" replace "its error `app-alert` on its own row under the toolbar" with the error `app-alert` inside the toolbar's status slot (Try again stays in the toolbar). In the Kitchen sink bullet add the new cell to the Nearby map list, rename "Use my location" to "Locate me" for the map home, and replace the stale `context/changes/nearby-map-ui-contract/visual-notes.md` pointer with `context/archive/2026-10-11-nearby-map-ui-contract/visual-notes.md`.

### Success Criteria:

#### Automated Verification:

- Frontend unit tests pass: `npm test -- --watch=false` (from `PigeonWatch/Frontend`)
- Production build passes within the bundle budget: `npm run build` (from `PigeonWatch/Frontend`)
- API builds with the new resource key: `dotnet build PigeonWatch/Api/PigeonWatchApi.slnx` (from the repo root)
- The page spec finds "Locate me" (not "Use my location") after denied, unavailable and timeout and still not after unsupported; the picker spec keeps "Use my location"
- The page spec asserts the error alert is inside `.nearby-toolbar__slot`, Try again is in `.nearby-toolbar__actions` and there is exactly one `[role="status"]` element
- The sink spec asserts the `map-nearby-error-after-denied` cell shows Search, Try again and Locate me together with the alert in the slot

#### Manual Verification:

- At 375 px in the sink, the "Location timed out" cell keeps Search and Locate me on one row and the map starts at the same offset as the "Found" cell
- At 375 px and `md` in the sink, the "Nearby error" cell starts the map at the same offset as the "Found" cell
- The new `map-nearby-error-after-denied` cell shows no worse than the recorded residuals (third button wraps at 375 px, about +18 px at `md`)

**Implementation Note**: After completing this phase and all automated verification passes, pause here for manual confirmation from the human that the manual testing was successful before proceeding to the next phase. Phase blocks use plain bullets — the corresponding `- [ ]` checkboxes for these items live in the `## Progress` section at the bottom of the plan.

---

## Phase 3: Measure and record

### Overview

jsdom has no layout, so the shifts and the two accessibility checks the archived change skipped are verified in a real browser against the sink and written down.

### Changes Required:

#### 1. Visual notes

**File**: `context/changes/nearby-map-review-fixes/visual-notes.md`

**Intent**: One record of evidence, in the style of the archived `visual-notes.md`, so the next agent and the reviewer can see what was measured and what was not.

**Contract**: a table of map offsets from the page top at 375 px, `md` and `lg` for the "Found", "Nearby error", "Location timed out" and combined cells (the F3 `lg` figure the research did not re-measure); the F2 and F3 results against the "Desired End State"; the accepted residuals; the Tab-order and screen-reader findings; and a "Not verified" list that keeps the local-API run and the real-page width at 375 px (derived 343 px, not rendered).

#### 2. Manual checks

**File**: none (browser at `npm start`, `/dev/kitchen-sink`, using the Browser pane for offsets)

**Intent**: Run the checks the archived change did not do, now that the toolbar is stable.

**Contract**: keyboard Tab pass through the toolbar at 375 px and `md` (Search, Try again, Locate me, map, marker, details); screen-reader announcements of the status text, the in-slot error alert, and the focus moves after select, Close, Try again and a successful "Locate me". Assistive technology is a human step; the agent supplies the cells and the checklist.

### Success Criteria:

#### Automated Verification:

- Frontend unit tests pass: `npm test -- --watch=false` (from `PigeonWatch/Frontend`)
- Production build passes within the bundle budget: `npm run build` (from `PigeonWatch/Frontend`)

#### Manual Verification:

- `visual-notes.md` records the map offsets at 375 px, `md` and `lg` for "Found", "Nearby error" and "Location timed out"; the "Nearby error" and "Location timed out" cells equal "Found" at each width
- The keyboard Tab pass at 375 px and `md` reaches Search, Try again, Locate me and the map in a sensible order, with a visible focus ring each time
- A screen reader announces the status text changes and the in-slot error alert once each, and focus lands where the page code puts it after select, Close, Try again and a successful "Locate me"
- The sink at 375 px and `md` shows no overlap or clipped text in the new cells

**Implementation Note**: After completing this phase and all automated verification passes, pause here for manual confirmation from the human that the manual testing was successful before proceeding. Phase blocks use plain bullets — the corresponding `- [ ]` checkboxes for these items live in the `## Progress` section at the bottom of the plan.

---

## Testing Strategy

### Unit Tests:

- `map-view.spec.ts`: `loadFailed` emits `true` on failure, `false` on retry start, nothing on a first successful load.
- `nearby-map-page.spec.ts`: the F4 status matrix with the stub emitting `loadFailed` (known position, unknown position at zoom below 10 and at 10, recovery, nearby `loading` and `error` kept); "Locate me" after each retryable outcome; the alert inside the slot with Try again in the actions container; one `[role="status"]`.
- `kitchen-sink.spec.ts`: the two new cells exist and render their parts (loader fails; three buttons plus alert in the slot).

### Integration Tests:

- None added; there is no layout in jsdom. Layout is verified in the sink in Phase 3.

### Manual Testing Steps:

1. `npm start` in `PigeonWatch/Frontend`, open `/dev/kitchen-sink`, set the Browser pane to 375 px and to `md`, `lg`.
2. Read the map's offset from the page top in the "Found", "Nearby error", "Location timed out" and the two new cells; compare with "Found".
3. Tab through the toolbar and run a screen reader over the status, the error alert and the focus moves.

## Performance Considerations

None. One output, one signal and one computed on the page; no new request, no new bundle code. Check `npm run build` against the 500 kB initial-bundle budget as the repo rule requires; nothing in this change lands in the initial bundle.

## Migration Notes

None. A new resx key; the neutral English file is the only culture today, and a missing key in another culture falls back to English per key.

## References

- Related research: `context/changes/nearby-map-review-fixes/research.md`
- Origin: `context/archive/2026-10-11-nearby-map-ui-contract/follow-ups/review-fixes.md`, `visual-notes.md` (F2-F4) and `reviews/impl-review.md` F6
- Earlier decision that Try again stays in the toolbar: the plan addendum in the archived `nearby-map-ui-contract/plan.md`
- Toolbar and slot: `PigeonWatch/Frontend/src/app/features/sightings/nearby-map-page/nearby-map-page.html:6-72`, `nearby-map-page.scss:7-33`
- Load failure: `PigeonWatch/Frontend/src/app/shared/map/map-view/map-view.ts:104,124-136`, `map-view.html:3-7`
- Status computed: `PigeonWatch/Frontend/src/app/features/sightings/nearby-map-page/nearby-map-page.ts:136-148`
- Sink cells: `PigeonWatch/Frontend/src/app/dev/kitchen-sink/cell-definitions.ts:299-316`

## Progress

> Convention: `- [ ]` pending, `- [x]` done. Append ` — <commit sha>` when a step lands. Do not rename step titles. See `references/progress-format.md`.

### Phase 1: Map load-failure signal (F4)

#### Automated

- [x] 1.1 Frontend unit tests pass: `npm test -- --watch=false` (from `PigeonWatch/Frontend`)
- [x] 1.2 Production build passes within the bundle budget: `npm run build` (from `PigeonWatch/Frontend`)
- [x] 1.3 `map-view.spec.ts` asserts `loadFailed` emits `true` on a failed load, `false` when the retry starts, and does not emit on a first successful load
- [x] 1.4 `nearby-map-page.spec.ts` asserts that while the map is failed the count, empty, zoom and hint texts are absent, Search is disabled and `aria-describedby` is not set on an empty status, and that each text returns when the map recovers

#### Manual

- [x] 1.5 In `npm start` at `/dev/kitchen-sink`, the "Map library failed to load" cell shows no "N open sightings" text over the map's error
- [x] 1.6 The new "map failed, position unknown" cell shows no "Zoom in to search an area" over the map's error and keeps Search disabled

### Phase 2: Toolbar layout (F2, F3)

#### Automated

- [ ] 2.1 Frontend unit tests pass: `npm test -- --watch=false` (from `PigeonWatch/Frontend`)
- [ ] 2.2 Production build passes within the bundle budget: `npm run build` (from `PigeonWatch/Frontend`)
- [ ] 2.3 API builds with the new resource key: `dotnet build PigeonWatch/Api/PigeonWatchApi.slnx` (from the repo root)
- [ ] 2.4 The page spec finds "Locate me" (not "Use my location") after denied, unavailable and timeout and still not after unsupported; the picker spec keeps "Use my location"
- [ ] 2.5 The page spec asserts the error alert is inside `.nearby-toolbar__slot`, Try again is in `.nearby-toolbar__actions` and there is exactly one `[role="status"]` element
- [ ] 2.6 The sink spec asserts the `map-nearby-error-after-denied` cell shows Search, Try again and Locate me together with the alert in the slot

#### Manual

- [ ] 2.7 At 375 px in the sink, the "Location timed out" cell keeps Search and Locate me on one row and the map starts at the same offset as the "Found" cell
- [ ] 2.8 At 375 px and `md` in the sink, the "Nearby error" cell starts the map at the same offset as the "Found" cell
- [ ] 2.9 The new `map-nearby-error-after-denied` cell shows no worse than the recorded residuals (third button wraps at 375 px, about +18 px at `md`)

### Phase 3: Measure and record

#### Automated

- [ ] 3.1 Frontend unit tests pass: `npm test -- --watch=false` (from `PigeonWatch/Frontend`)
- [ ] 3.2 Production build passes within the bundle budget: `npm run build` (from `PigeonWatch/Frontend`)

#### Manual

- [ ] 3.3 `visual-notes.md` records the map offsets at 375 px, `md` and `lg` for "Found", "Nearby error" and "Location timed out"; the "Nearby error" and "Location timed out" cells equal "Found" at each width
- [ ] 3.4 The keyboard Tab pass at 375 px and `md` reaches Search, Try again, Locate me and the map in a sensible order, with a visible focus ring each time
- [ ] 3.5 A screen reader announces the status text changes and the in-slot error alert once each, and focus lands where the page code puts it after select, Close, Try again and a successful "Locate me"
- [ ] 3.6 The sink at 375 px and `md` shows no overlap or clipped text in the new cells
