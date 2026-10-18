# Sighting Detail UI Contract Implementation Plan

## Overview

Bring `/sightings/:id` up to the same design-system contract as the Profile and nearby-map views. The audit found no missing tokens or components (0 hardcoded values, 6 `shared/ui` components already read), so the work is structural and behavioural: a display-only marker on the one-sighting map, a truthful arrival (tab title, heading focus, Back link inside the card), a kitchen-sink group that renders every state of the page, and a rule that keeps the next agent on the contract. The cost guardrail (no map and no tile until "Show map" is pressed) is kept and re-asserted.

## Current State Analysis

The detail page is a lazy route behind `authGuard` with no `title` and no `data` (`PigeonWatch/Frontend/src/app/app.routes.ts:39-46`). Its template wraps the card in a bare `div.pw-stack`, puts the "Back to map" link in another bare `div` above a 28rem card inside the 40rem `<main>`, and wraps the "Show map" toggle in a third bare `div` (`sighting-detail-page.html:1-4,48-62`). The only `h1` is rendered inside `app-page-card`, so the page cannot focus it. Nothing sets the tab title (`index.html` ships `<title>PigeonWatch</title>`) or moves focus on arrival; the only focus-on-arrival precedent is the nearby page's per-view `afterNextRender` heading focus (`nearby-map-page.ts:236-241`).

The shared `app-map-view` creates every marker with `keyboard: true`, binds Enter/Space to `markerSelect`, and writes `aria-pressed` on every marker (`map-view.ts:199-202,211-230`). The detail page binds no `(markerSelect)`, so its single marker is a Tab stop whose activation does nothing and which reads as an unpressed toggle, named by the sighting name only, inside a region labelled "Map of sightings".

The kitchen sink at `/dev/kitchen-sink` has `profile` and `nearbyMap` groups only. `CellStubs.sightingsApi` has `mine` and `nearby` and no `get` (`cell-stubs.ts:159-162`), `state-cell.html:27-34` switches on two groups, and `leafletLoader` only fails for the `nearbyMap` group (`cell-stubs.ts:183-192`).

## Desired End State

- `/sightings/:id` renders with `app-page-card` as the template root; the Back link sits in the card's `actions` footer and still goes to `/`; no bare single-control wrapper `div` remains.
- On arrival by navigation, the tab title reads "<name or Sighting> – PigeonWatch" and focus is on the page heading once the sighting, the not-found state or the error state is showing; leaving the page resets the title to "PigeonWatch".
- "Show map" reveals a map whose marker is display-only: not a Tab stop, no `role="button"`, no `aria-pressed`, hover title with the criticality, region labelled for one sighting. The nearby map and the report pin-picker behave exactly as before.
- The kitchen sink has a "Sighting detail" group rendering loaded, unnamed, long name, map shown, not found, error, loading, retry-that-succeeds and map-library-failed, each asserted in `kitchen-sink.spec.ts`.
- `visual-notes.md` in the change folder records the 7-state matrix, offsets at 375 px, `md` and `lg`, and the Tab pass; `PigeonWatch/Frontend/CLAUDE.md` documents the map-view input, the sink group and the arrival pattern; the deferred charges are marked in `research.md`.

Verified by `npm test -- --watch=false` and `npm run build` from `PigeonWatch/Frontend`, the hardcoded-value scan returning 0 hits, and the manual checks listed per phase.

### Key Discoveries:

- Missing-token and missing-component charges are empty: 0 scan hits and `style=`, `::ng-deep`, `!important` absent in the three view files (`research.md`, Summary).
- `PigeonWatch/Frontend/CLAUDE.md` already says the detail map "leaves `selectedId` `null`", so a display-only marker is the documented intent, not a new behaviour.
- Secondary navigation links already live in the card's `actions` slot on login and register (`login-page.html:33-36`, `register-page.html:57-60`); the profile page uses the same slot for its logout button (`profile-page.html:63-67`).
- `style-token-coverage.spec.ts` scans every `.html` and `.scss` under `app/` including `dev/`, so new sink markup is guarded without a per-view list (`styles/style-token-coverage.spec.ts:103,127-177`).
- The sink never moves focus on load: a scenario that moves focus runs from a `manual` button in the cell header (`PigeonWatch/Frontend/CLAUDE.md`, kitchen sink bullet).
- `CellDefinition` stub modes are a discriminated union keyed by `group` (`cell-stubs.ts:21-44`); `leafletLoader` reads the `leaflet` mode of one group only (`cell-stubs.ts:183-192`).

## What We're NOT Doing

- **Charge 4 (non-404 failures share one error; 503/offline spinner up to 120 s).** Deferred: `GET /sightings/{id}` answers 200 or 404 (401 is handled by the auth interceptor), so every other failure that reaches the page is a transient one and a retry is correct; the 120 s wait belongs to `warmup.interceptor.ts` and the 401 flash to `auth.interceptor.ts`. `sightingFailure` and the error state are unchanged.
- **Preserving the nearby map's centre, zoom and selection when the user goes Back.** Deferred: reaches into the nearby page. The link stays a fixed `/` labelled "Back to map" from every entry point.
- **An origin-aware Back label ("Back to profile").** Declined: would edit three other views' links and add keys.
- **App-wide route titles and focus-on-navigation in `app.ts`.** Deferred: changes every view in a one-view change and touches the initial bundle (486.2 kB of the 500 kB budget). Other routes keep the static tab title and no focus move.
- **Material state-layer hover styling, the date format locale, and the unread label keys behind `statusLabel()` and `criticalityLabel()`.** Deferred: no page-specific code behind them.
- **A new shared Back-link component or width-matching `pw-*` class.** Declined: the "promote on second use" rule has not triggered (one template).
- No new design tokens, no new UI library, no `ng add`.

## Implementation Approach

Fix the shared contract before the view: first the `app-map-view` input (Phase 1), then the page that uses it together with the `PageCard` focus hook (Phase 2), then the kitchen-sink group and the 7-state evidence (Phase 3), then the rule and the deferrals (Phase 4). Each phase ends green on `npm test -- --watch=false` and `npm run build`.

New behaviour follows existing patterns: signal inputs and OnPush on shared components, a `viewChild` plus a public method for focus (as the nearby page focuses its heading), resx keys for every string, stub modes and a `CellStubs` method for every new data dependency. Visible strings come from `UiLabels.resx`; the kitchen sink may use literal English. The working copy is CRLF (`.gitattributes`), so convert files written with LF before finishing.

## Critical Implementation Details

- **Timing & lifecycle.** The arrival focus must not run in the kitchen sink on load, because the sink never takes focus or scrolls on load. Apply the first-arrival focus only when the page was created by a router navigation (read `Router.currentNavigation()` once in the constructor), and apply the focus after a retry or an id change unconditionally. If `currentNavigation()` turns out to be null during activation in the real app or in the router harness, gate on the route carrying an `id` param instead (the sink's `ActivatedRoute` has none). The retry-then-loaded sink cell runs its retry from a `manual` button for the same reason.
- **State sequencing.** Focus the heading when the state leaves `loading` (loaded, not found or error), not while it is loading: the heading text changes from "Sighting" to the name on load, and a focus set earlier would announce the wrong name. Set the focus after render (`afterNextRender`), as `nearby-map-page.ts:236-241` does.
- **Timing & lifecycle (title).** Reset the title to the app name (`common.appName`) on destroy, not to a value captured at construction. In the sink several detail cells coexist and a captured value would restore another cell's title; the static `index.html` title equals the app name, which is the value every other route shows.
- **State sequencing (map-view).** The display-only input must be read inside the effect that builds markers, so toggling it rebuilds them; `applySelection` must skip `aria-pressed` when markers are display-only.

## Phase 1: Display-only markers in the shared map

### Overview

`app-map-view` gains one input that turns markers from interactive controls into plain visual markers. The default keeps today's behaviour for the nearby map and the location picker. No page uses the new value yet.

### Changes Required:

#### 1. Map view component

**File**: `PigeonWatch/Frontend/src/app/shared/map/map-view/map-view.ts`

**Intent**: Let a page ask for markers that are not controls, so the one-sighting map stops offering a Tab stop that does nothing.

**Contract**: New signal input `interactiveMarkers` (boolean, default `true`). When `false`: markers are created with `keyboard: false` (so no `tabindex` and no `role="button"` from Leaflet), no keydown handler is attached, `aria-pressed` is not written by `applySelection`, and `markerSelect` is not emitted by click or key. Marker icon class names, `title`, the selected class and z-index handling are unchanged. The input is read inside `applyMarkers` so a change rebuilds the markers.

#### 2. Map view stub

**File**: `PigeonWatch/Frontend/src/app/shared/map/testing/map-view-stub.ts`

**Intent**: Keep the stub's selector, inputs and outputs identical to the real component so page specs can read the new input.

**Contract**: Same `interactiveMarkers` signal input with the same default.

#### 3. Map view spec

**File**: `PigeonWatch/Frontend/src/app/shared/map/map-view/map-view.spec.ts`

**Intent**: Pin both modes: unchanged interactive behaviour and the new display-only behaviour.

**Contract**: New cases asserting that with `interactiveMarkers` false a marker icon has no `tabindex`, no `role="button"`, no `aria-pressed`, and Enter/Space and click emit nothing; existing cases (Tab stop, Enter/Space and click emit `markerSelect`, `aria-pressed` follows `selectedId`) keep passing with the default.

### Success Criteria:

#### Automated Verification:

- Frontend unit tests pass: `npm test -- --watch=false` (run from `PigeonWatch/Frontend`)
- Map-view spec asserts display-only markers have no tab stop, no `role="button"`, no `aria-pressed` and emit no `markerSelect`, and interactive markers behave as before
- Production build passes within the 500 kB initial budget: `npm run build` (run from `PigeonWatch/Frontend`)

#### Manual Verification:

- On the nearby map (dev server, logged in), markers still receive focus with Tab and Enter selects one

**Implementation Note**: After completing this phase and all automated verification passes, pause here for manual confirmation from the human that the manual testing was successful before proceeding to the next phase. Phase blocks use plain bullets — the corresponding `- [ ]` checkboxes for these items live in the `## Progress` section at the bottom of the plan.

---

## Phase 2: Detail view contract

### Overview

The detail page gets its card-rooted layout, a truthful arrival, a display-only map and the specs that were missing. This phase maps to charges 1, 2, 3 and the spec half of charge 5.

### Changes Required:

#### 1. Page card heading focus

**File**: `PigeonWatch/Frontend/src/app/shared/ui/page-card/page-card.ts` and `page-card.html`

**Intent**: The heading lives inside the shared card, so the card must expose a way to focus it; a page cannot reach it otherwise.

**Contract**: The `h1` becomes programmatically focusable (`tabindex="-1"`, not a Tab stop) and `PageCard` gains a public `focusHeading()` method, backed by a `viewChild` on the `h1`. No input, no style change; the footer still collapses when nothing is projected.

#### 2. Page card spec

**File**: `PigeonWatch/Frontend/src/app/shared/ui/page-card/page-card.spec.ts`

**Intent**: Pin the new hook.

**Contract**: A case asserting `focusHeading()` moves `document.activeElement` to the `h1` and the `h1` is not in the Tab order.

#### 3. Detail page template

**File**: `PigeonWatch/Frontend/src/app/features/sightings/sighting-detail-page/sighting-detail-page.html`

**Intent**: Make the page look like the other card pages: `app-page-card` as the root, the Back link in the card's actions footer, no bare wrapper `div`s holding a single control, and a map that is display-only and labelled for one sighting.

**Contract**: Root element is `app-page-card`. The Back link is an `a mat-button routerLink="/"` projected with the `actions` attribute, same label key as today (`sighting.detail.back`). The "Show map" button's bare wrapper becomes a `pw-cluster` row (the documented layout class) so the button keeps its natural width; `aria-controls` and `aria-expanded` are unchanged. `app-map-view` receives `[interactiveMarkers]="false"` and a new region label key. The cost guardrail is untouched: `app-map-view` is still rendered only after the toggle and only when the position is known.

#### 4. Detail page component

**File**: `PigeonWatch/Frontend/src/app/features/sightings/sighting-detail-page/sighting-detail-page.ts`

**Intent**: Announce the page on arrival (tab title and focus) and give the single marker the same accessible name as on the nearby map.

**Contract**: Inject `Title`. An effect sets the tab title from `title()` through a new resx key `sighting.detail.pageTitle` (`{0} – {1}`: title, then `common.appName`); `DestroyRef.onDestroy` sets it to the `common.appName` value. A `viewChild` on `PageCard` calls `focusHeading()` through `afterNextRender` when the state leaves `loading` (first arrival only when the page was created by a navigation; after a retry or an id change always) — see Critical Implementation Details. The marker `label` is built with `map.markerLabel` (name, then the translated criticality label), as `nearby-map-page.ts:160-174` does. `mapShown` still resets on every id/reload emission. `sightingFailure` mapping is unchanged.

#### 5. UI labels

**File**: `PigeonWatch/Api/BusinessObjects/Resources/UiLabels.resx`

**Intent**: Every visible string is a resx key; this change adds two.

**Contract**: New keys next to the other `sighting.detail.*` entries: `sighting.detail.mapLabel` (region label for the one-sighting map, for example "Map showing where this sighting was reported") and `sighting.detail.pageTitle` (`{0} – {1}`). The `map.ariaLabel` and `map.markerLabel` keys are unchanged.

#### 6. Detail page spec

**File**: `PigeonWatch/Frontend/src/app/features/sightings/sighting-detail-page/sighting-detail-page.spec.ts`

**Intent**: Cover what the audit found uncovered: loading, arrival, the map's semantics, the retry reset, and the Back link's new home.

**Contract**: New or updated named cases: the Back link renders inside the card's actions footer and still has `href="/"`; the loading state shows the "Sighting" heading and a loading indicator; the tab title is "Limpy – PigeonWatch" when loaded, "Sighting – PigeonWatch" while loading and in the not-found and error states, and "PigeonWatch" after the component is destroyed; the heading holds focus after the sighting loads by navigation and again after a retry that succeeds; the map stub receives `interactiveMarkers` false, the `sighting.detail.mapLabel` text, and a marker `label` that includes the criticality; pressing "Try again" hides an already shown map (the `mapShown` reset); `app-map-view` is still absent before "Show map" is pressed.

### Success Criteria:

#### Automated Verification:

- Frontend unit tests pass: `npm test -- --watch=false` (run from `PigeonWatch/Frontend`)
- Production build passes within the 500 kB initial budget: `npm run build` (run from `PigeonWatch/Frontend`)
- UI resource tests pass with the two new keys: `dotnet test PigeonWatch/Api/UnitTests --filter UiResourceTests`
- Hardcoded-value scan on the detail page files returns 0 hits: `grep -nE '#[0-9a-fA-F]{3,8}\b|rgba?\(|hsla?\(|oklch\(' PigeonWatch/Frontend/src/app/features/sightings/sighting-detail-page/sighting-detail-page.html PigeonWatch/Frontend/src/app/features/sightings/sighting-detail-page/sighting-detail-page.ts`
- Detail spec asserts the Back link is inside the card actions footer with `href="/"`, and the loading state shows the heading "Sighting" and a loading indicator
- Detail spec asserts the tab title in the loaded, loading, not-found and error states and its reset to "PigeonWatch" on destroy
- Detail spec asserts the heading takes focus after a navigation-entered load and after a successful retry
- Detail spec asserts the map stub gets `interactiveMarkers` false, the new region label and a marker label with the criticality, that a retry hides a shown map, and that no `app-map-view` exists before "Show map"
- Page-card spec asserts `focusHeading()` focuses the `h1` and the `h1` is not a Tab stop

#### Manual Verification:

- With the API running and logged in, "View details" on the nearby map opens the page with focus on the heading and the tab title showing the sighting name; Tab goes to "Show map" and then to the Back link
- After "Show map", Tab does not stop on the marker and hovering the marker shows its name and criticality
- The browser network panel shows no `tile.openstreetmap.org` request until "Show map" is pressed
- Leaving the page with the Back link returns the tab title to "PigeonWatch"

**Implementation Note**: After completing this phase and all automated verification passes, pause here for manual confirmation from the human that the manual testing was successful before proceeding to the next phase. Phase blocks use plain bullets — the corresponding `- [ ]` checkboxes for these items live in the `## Progress` section at the bottom of the plan.

---

## Phase 3: States and visual gate

### Overview

The detail page gets its own group in the kitchen sink so every state is rendered side by side, and the 7-state matrix is recorded with measurements. This phase maps to the visual-gate half of charge 5 and verifies the layout stability named in charge 2.

### Changes Required:

#### 1. Cell stubs

**File**: `PigeonWatch/Frontend/src/app/dev/kitchen-sink/cell-stubs.ts`

**Intent**: Give the sink a `SightingsApi.get` so the detail page can render without an API.

**Contract**: New `SightingDetailModes` (`group: 'sightingDetail'`, a `detail` mode of `loaded`, `loadedUnnamed`, `loadedLongName`, `notFound`, `failure`, `pending` or `failureThenLoaded`, and a `leaflet` mode of `tileless` or `failure`) added to the `StubModes` union. `sightingsApi.get(id)` ignores the id and answers per mode (404 `HttpErrorResponse` for `notFound`, 500 for `failure`, a first failing request then a loaded one for `failureThenLoaded`, modelled on the `nearbyRequests` counter). `leafletLoader` fails for any group whose `leaflet` mode is `failure`, not only `nearbyMap`. Existing groups' behaviour is unchanged.

#### 2. Cell definitions

**File**: `PigeonWatch/Frontend/src/app/dev/kitchen-sink/cell-definitions.ts`

**Intent**: Define the detail cells and register their group.

**Contract**: A `SightingDetailCell` type and a `detailCells` list with ids and English labels for: loaded (default), loaded unnamed, loaded with a long name, map shown (steps press the `sighting.detail.showMap` label and wait for the map container), not found, error, loading (request never completes), error then loaded (the retry press runs from a `manual` scenario because the retry moves focus), and map library failed after "Show map". A new group `{ title: 'Sighting detail', wide: false, cells: detailCells }` appended to `cellGroups`.

#### 3. State cell

**File**: `PigeonWatch/Frontend/src/app/dev/kitchen-sink/state-cell.ts` and `state-cell.html`

**Intent**: Render the detail page in a cell.

**Contract**: `SightingDetailPage` added to the `StateCell` imports and a `@case ('sightingDetail')` rendering `<app-sighting-detail-page />` in `state-cell.html`. No change to the providers: `SightingsApi` and `LEAFLET_LOADER` are already stubbed per cell.

#### 4. Kitchen-sink spec

**File**: `PigeonWatch/Frontend/src/app/dev/kitchen-sink/kitchen-sink.spec.ts`

**Intent**: Assert every detail cell, as the other groups do.

**Contract**: `TestBed.overrideComponent(SightingDetailPage, …MapViewStub…)` next to the existing override; the `pageOf` record gains `sightingDetail: 'app-sighting-detail-page'` and the `app-profile-page, app-nearby-map-page` selector in the per-cell count includes it. One named case per state: loaded rows and heading, unnamed heading, long-name heading present, map shown renders one stub map with `interactiveMarkers` false and one marker, not found shows an info alert and no Try again, error shows an error alert and Try again, loading shows the loading indicator and the "Sighting" heading, error-then-loaded recovers after the retry, and the map-failed cell renders the page with the toggle available.

#### 5. Visual notes

**File**: `context/changes/sighting-detail-ui-contract/visual-notes.md`

**Intent**: Leave the evidence the earlier UI changes left, following `context/archive/2026-10-17-nearby-map-review-fixes/visual-notes.md`.

**Contract**: Measurements from `npm start` at `/dev/kitchen-sink` at 375 px, `md` and `lg` (heading wrap and card height before and after load, Back link inside the card, map-open shift), the 7-state matrix (default, hover, focus-visible, error, loading shown with cell ids; disabled and empty marked N/A with the reason), and the Tab pass (Show map, then Back link; visible focus ring on Back, the toggle and Try again; no marker stop).

### Success Criteria:

#### Automated Verification:

- Frontend unit tests pass, including the kitchen-sink spec with the detail group: `npm test -- --watch=false` (run from `PigeonWatch/Frontend`)
- Production build passes within the 500 kB initial budget: `npm run build` (run from `PigeonWatch/Frontend`)
- Sink spec renders each detail cell once with its label and the detail page, and asserts the loaded, unnamed, long-name, map-shown, not-found, error, loading and retry states
- Sink spec asserts the map-shown cell's map has `interactiveMarkers` false and one marker, and the error cell offers Try again while the not-found cell does not

#### Manual Verification:

- `npm start`, open `/dev/kitchen-sink`: the "Sighting detail" group renders every cell, and the page does not scroll or take focus on load
- visual-notes.md records heading wrap, card height and Back-link placement at 375 px, `md` and `lg`, and a desktop and a 375 px screenshot of the group were reviewed
- visual-notes.md records the 7-state matrix with cell ids and N/A reasons for disabled and empty
- visual-notes.md records the Tab pass and the manual retry cell moving focus to the heading

**Implementation Note**: After completing this phase and all automated verification passes, pause here for manual confirmation from the human that the manual testing was successful before proceeding to the next phase. Phase blocks use plain bullets — the corresponding `- [ ]` checkboxes for these items live in the `## Progress` section at the bottom of the plan.

---

## Phase 4: Rule and guard

### Overview

Leave the rule that keeps the next agent on the contract, and mark the deferred charges so nothing is silently dropped.

### Changes Required:

#### 1. Frontend rules file

**File**: `PigeonWatch/Frontend/CLAUDE.md`

**Intent**: Record what this change established so the next UI change reads it first.

**Contract**: Extend existing bullets, do not create a second block: the `app-map-view` API sentence gains `interactiveMarkers` (default true; the detail map sets it false, so its marker is not a Tab stop and has no `aria-pressed`); the kitchen-sink bullet lists the **Sighting detail** group, its stub modes and its visual-notes path; a short bullet states the arrival pattern (a routed view that needs one sets its own tab title through `Title` and focuses its `app-page-card` heading with `focusHeading()` once on arrival, resets the title on destroy, and no route sets `title` yet) and that a card page's secondary navigation link goes in the card's `actions` slot. The `PageCard` description gains `focusHeading()`.

#### 2. Deferred charges

**File**: `context/changes/sighting-detail-ui-contract/research.md`

**Intent**: Charges the plan does not address stay in `## Charges`, marked deferred with a reason.

**Contract**: Under `## Charges`, charge 4 and each deferred candidate (Back to the map losing its state, app-wide title and focus, Material hover, date locale, label keys) get a one-line **Deferred** marker with the reason from "What We're NOT Doing". Charges 1, 2, 3 and 5 get a one-line pointer to the phase that addresses them.

### Success Criteria:

#### Automated Verification:

- Frontend unit tests pass: `npm test -- --watch=false` (run from `PigeonWatch/Frontend`)
- Production build passes within the 500 kB initial budget: `npm run build` (run from `PigeonWatch/Frontend`)
- Hardcoded-value scan on the detail page, page-card and kitchen-sink files returns 0 new hits: `grep -nE '#[0-9a-fA-F]{3,8}\b|rgba?\(|hsla?\(|oklch\(' PigeonWatch/Frontend/src/app/features/sightings/sighting-detail-page/sighting-detail-page.html PigeonWatch/Frontend/src/app/shared/ui/page-card/page-card.html PigeonWatch/Frontend/src/app/dev/kitchen-sink/state-cell.html`
- Frontend rules file names the new input and the sink group: `grep -nE 'interactiveMarkers|Sighting detail' PigeonWatch/Frontend/CLAUDE.md`

#### Manual Verification:

- PigeonWatch/Frontend/CLAUDE.md reads correctly: the map-view, kitchen-sink and arrival-pattern entries match what was built and nothing contradicts them
- research.md `## Charges` marks charge 4 and every deferred candidate as deferred with a reason, and points charges 1, 2, 3 and 5 to their phases

**Implementation Note**: After completing this phase and all automated verification passes, pause here for manual confirmation from the human that the manual testing was successful before proceeding. Phase blocks use plain bullets — the corresponding `- [ ]` checkboxes for these items live in the `## Progress` section at the bottom of the plan.

---

## Testing Strategy

### Unit Tests:

- `map-view.spec.ts`: display-only vs interactive markers (tab stop, role, `aria-pressed`, emissions).
- `page-card.spec.ts`: `focusHeading()` focuses a non-Tab-stop `h1`.
- `sighting-detail-page.spec.ts`: Back link home, loading state, tab title lifecycle, heading focus after load and retry, map semantics and label, `mapShown` reset on retry, cost guardrail.
- `kitchen-sink.spec.ts`: one assertion per detail cell.
- `style-token-coverage.spec.ts` runs unchanged over the new markup.

### Integration Tests:

- Only the kitchen-sink spec renders the page together with stubbed services; no e2e layer is added.

### Manual Testing Steps:

1. With the API running and a registered user, open a sighting from the nearby map: check focus on the heading, tab title, Tab order, and that no tile is requested before "Show map".
2. Press "Show map": the marker is not a Tab stop; hover shows its name and criticality.
3. Open `/dev/kitchen-sink` and review the "Sighting detail" group at desktop and 375 px.
4. Press the Back link and confirm the tab title returns to "PigeonWatch".

## Performance Considerations

The initial bundle is 486.2 kB against a 500 kB budget. All changes are in lazy chunks (detail page, `shared/ui` page-card imported by lazy pages, map-view) or in the dev-only sink, and `app.ts` is not touched; `npm run build` is a gate in every phase. The cost guardrail is a hard constraint: no map and no tile before "Show map".

## Migration Notes

None. No data, API or route change; two new UI-label keys are served by the existing resources endpoint.

## References

- Related research: `context/changes/sighting-detail-ui-contract/research.md`
- Origin: `context/archive/2026-10-11-nearby-map-ui-contract/change.md` ("Follow-up change")
- Method for sink measurements and the Tab pass: `context/archive/2026-10-17-nearby-map-review-fixes/visual-notes.md`
- Focus-on-arrival precedent: `PigeonWatch/Frontend/src/app/features/sightings/nearby-map-page/nearby-map-page.ts:236-241`
- Marker label precedent: `PigeonWatch/Frontend/src/app/features/sightings/nearby-map-page/nearby-map-page.ts:160-174`
- Card `actions` slot precedent: `PigeonWatch/Frontend/src/app/features/auth/login-page/login-page.html:33-36`

## Progress

> Convention: `- [ ]` pending, `- [x]` done. Append ` — <commit sha>` when a step lands. Do not rename step titles. See `references/progress-format.md`.

### Phase 1: Display-only markers in the shared map

#### Automated

- [x] 1.1 Frontend unit tests pass: `npm test -- --watch=false` (run from `PigeonWatch/Frontend`) — 010700c
- [x] 1.2 Map-view spec asserts display-only markers have no tab stop, no `role="button"`, no `aria-pressed` and emit no `markerSelect`, and interactive markers behave as before — 010700c
- [x] 1.3 Production build passes within the 500 kB initial budget: `npm run build` (run from `PigeonWatch/Frontend`) — 010700c

#### Manual

- [x] 1.4 On the nearby map (dev server, logged in), markers still receive focus with Tab and Enter selects one — 010700c

### Phase 2: Detail view contract

#### Automated

- [x] 2.1 Frontend unit tests pass: `npm test -- --watch=false` (run from `PigeonWatch/Frontend`) — 00b08ac
- [x] 2.2 Production build passes within the 500 kB initial budget: `npm run build` (run from `PigeonWatch/Frontend`) — 00b08ac
- [x] 2.3 UI resource tests pass with the two new keys: `dotnet test PigeonWatch/Api/UnitTests --filter UiResourceTests` — 00b08ac
- [x] 2.4 Hardcoded-value scan on the detail page files returns 0 hits: `grep -nE '#[0-9a-fA-F]{3,8}\b|rgba?\(|hsla?\(|oklch\(' PigeonWatch/Frontend/src/app/features/sightings/sighting-detail-page/sighting-detail-page.html PigeonWatch/Frontend/src/app/features/sightings/sighting-detail-page/sighting-detail-page.ts` — 00b08ac
- [x] 2.5 Detail spec asserts the Back link is inside the card actions footer with `href="/"`, and the loading state shows the heading "Sighting" and a loading indicator — 00b08ac
- [x] 2.6 Detail spec asserts the tab title in the loaded, loading, not-found and error states and its reset to "PigeonWatch" on destroy — 00b08ac
- [x] 2.7 Detail spec asserts the heading takes focus after a navigation-entered load and after a successful retry — 00b08ac
- [x] 2.8 Detail spec asserts the map stub gets `interactiveMarkers` false, the new region label and a marker label with the criticality, that a retry hides a shown map, and that no `app-map-view` exists before "Show map" — 00b08ac
- [x] 2.9 Page-card spec asserts `focusHeading()` focuses the `h1` and the `h1` is not a Tab stop — 00b08ac

#### Manual

- [x] 2.10 With the API running and logged in, "View details" on the nearby map opens the page with focus on the heading and the tab title showing the sighting name; Tab goes to "Show map" and then to the Back link — 00b08ac
- [x] 2.11 After "Show map", Tab does not stop on the marker and hovering the marker shows its name and criticality — 00b08ac
- [x] 2.12 The browser network panel shows no `tile.openstreetmap.org` request until "Show map" is pressed — 00b08ac
- [x] 2.13 Leaving the page with the Back link returns the tab title to "PigeonWatch" — 00b08ac

### Phase 3: States and visual gate

#### Automated

- [x] 3.1 Frontend unit tests pass, including the kitchen-sink spec with the detail group: `npm test -- --watch=false` (run from `PigeonWatch/Frontend`)
- [x] 3.2 Production build passes within the 500 kB initial budget: `npm run build` (run from `PigeonWatch/Frontend`)
- [x] 3.3 Sink spec renders each detail cell once with its label and the detail page, and asserts the loaded, unnamed, long-name, map-shown, not-found, error, loading and retry states
- [x] 3.4 Sink spec asserts the map-shown cell's map has `interactiveMarkers` false and one marker, and the error cell offers Try again while the not-found cell does not

#### Manual

- [x] 3.5 `npm start`, open `/dev/kitchen-sink`: the "Sighting detail" group renders every cell, and the page does not scroll or take focus on load
- [x] 3.6 visual-notes.md records heading wrap, card height and Back-link placement at 375 px, `md` and `lg`, and a desktop and a 375 px screenshot of the group were reviewed
- [x] 3.7 visual-notes.md records the 7-state matrix with cell ids and N/A reasons for disabled and empty
- [x] 3.8 visual-notes.md records the Tab pass and the manual retry cell moving focus to the heading

### Phase 4: Rule and guard

#### Automated

- [ ] 4.1 Frontend unit tests pass: `npm test -- --watch=false` (run from `PigeonWatch/Frontend`)
- [ ] 4.2 Production build passes within the 500 kB initial budget: `npm run build` (run from `PigeonWatch/Frontend`)
- [ ] 4.3 Hardcoded-value scan on the detail page, page-card and kitchen-sink files returns 0 new hits: `grep -nE '#[0-9a-fA-F]{3,8}\b|rgba?\(|hsla?\(|oklch\(' PigeonWatch/Frontend/src/app/features/sightings/sighting-detail-page/sighting-detail-page.html PigeonWatch/Frontend/src/app/shared/ui/page-card/page-card.html PigeonWatch/Frontend/src/app/dev/kitchen-sink/state-cell.html`
- [ ] 4.4 Frontend rules file names the new input and the sink group: `grep -nE 'interactiveMarkers|Sighting detail' PigeonWatch/Frontend/CLAUDE.md`

#### Manual

- [ ] 4.5 PigeonWatch/Frontend/CLAUDE.md reads correctly: the map-view, kitchen-sink and arrival-pattern entries match what was built and nothing contradicts them
- [ ] 4.6 research.md `## Charges` marks charge 4 and every deferred candidate as deferred with a reason, and points charges 1, 2, 3 and 5 to their phases
