# Nearby Map UI Contract Implementation Plan

## Overview

Bring the nearby map home (`/`) and its selected-sighting card onto the design-system contract and fix how it behaves. Markers become selectable from the keyboard and show which one is selected. Criticality gets a shared badge and a legend instead of colour alone. Loading, empty, error and the selected card move to where the user is looking. "Search this area" can no longer run a misleading country-wide search. The page gets a wider desktop layout with the card beside the map. The spinner, error+retry and label/value list become `shared/ui` components and replace every copy in the app. A generalised kitchen sink proves the 7-state matrix, and the change ends with a spec check and a rule that keep the next agent on the contract.

## Current State Analysis

The view already reads the token contract: the hardcoded-value scan finds 0 hits in the 4 view files, the 3 spacing values in `nearby-map-page.scss` read `var(--pw-*)`, and `style-token-coverage.spec.ts` guards the folder (`research.md`, Summary). The drift is behavioural and structural. The audit raised five charges (`research.md`, `## Charges`):

- **C1** Markers are keyboard-focusable but not keyboard-selectable: `applyMarkers()` subscribes only to Leaflet `'click'` (`shared/map/map-view/map-view.ts:165-178`), Leaflet 1.9.4 delivers a key press on a marker as `keypress`, and `MapMarker` has no selected flag (`shared/map/map-marker.ts:5-9`). The accessible name is the sighting name only (`nearby-map-page.ts:76-84`).
- **C2** `--pw-color-criticality-*` (`styles/_tokens.scss:21-23`) is used only by the marker classes (`styles/_common.scss:74-84`); criticality is plain text on the selected card (`nearby-map-page.html:71-73`), the detail page (`sighting-detail-page.html:39`) and My sightings (`my-sightings.html:34-40`), and there is no legend.
- **C3** Three blocks are copied across features: the spinner wrapper (`nearby-map-page.html:26-33`, `sighting-detail-page.html:8-14`, `my-sightings.html:6-12`, each with its own `spinnerDiameter = 32`), the label/value list (`nearby-map-page.html:60-75`, `sighting-detail-page.html:30-75`, `profile-page.html:6-24`) and the error+retry block (`nearby-map-page.html:35-41`, `sighting-detail-page.html:20-25`, `my-sightings.html:15-20`, plus the map's own `map-view.html:3-10` with a differently shaped button).
- **C4** All feedback renders below the map and the "Search this area" button (`nearby-map-page.html:25-86`); entering `loading` empties the markers (`nearby-map-page.ts:70-74`); `select()` only sets a signal (`nearby-map-page.ts:139-141`); the locating line is swapped for the hint alert above the map, so the map jumps (`nearby-map-page.html:6-11`).
- **C5** Without a position the map opens on Poland at zoom 6 (`shared/map/map-config.ts:10-13`) and "Search this area" is always enabled (`nearby-map-page.html:21`), so a press queries 10 km around the centre of the country and reports "No open sightings" (`nearby-map-page.html:45-47`). The h1 still says "Sightings near you".

Constraints from the repo:

- `PigeonWatch/Frontend/CLAUDE.md`: `shared/ui` components are standalone, OnPush, signal inputs, no text of their own, variants are inputs; a pattern found in a second feature is promoted to `shared/ui` in the same change; `app-map-view` is the only file that imports Leaflet and `shared/map` has no barrel; no user text in Leaflet HTML; every UI text is a key in `UiLabels.resx`; `core/` and `app.ts` import `shared/ui` by file path; initial bundle 486.2 kB against a 500 kB budget.
- `app-main` is capped at `--pw-content-max-width` (40rem) in the shell (`app/app.scss:48-52`); there is no per-route layout today.
- The kitchen sink hard-codes `<app-profile-page />` (`dev/kitchen-sink/state-cell.html:10`, `state-cell.ts:34`), its stubs are Profile-specific (`cell-stubs.ts:13-19`), and its spec expects 13 cells that all contain `app-profile-page` (`kitchen-sink.spec.ts:68,72`).

## Desired End State

- Enter or Space on a focused marker selects it; the selected marker is visibly different and exposes `aria-pressed="true"`; each marker's accessible name includes its criticality; Close returns focus to the marker.
- `app-criticality-badge` shows criticality as a token-coloured dot plus text on the map card, the detail page and My sightings; the map page shows a legend of the three levels at all times.
- `app-loading-indicator`, `app-error-retry` and `dl[app-detail-list]` / `div[app-detail-item]` live in `shared/ui` and are the only implementation of those blocks in the app: no feature template uses `mat-progress-spinner` directly, no feature defines a `spinnerDiameter`, and the map view, `app-map-view`, the detail page, My sightings and Profile use them.
- A toolbar directly above the map holds "Search this area" and a persistent status slot (locating, zoom-in reason, hint, loading, found count, empty, error); markers stay on the map while a search reloads; selecting a marker scrolls the card into view and moves focus to it.
- Below zoom 10 "Search this area" is disabled (still focusable) and the status slot says why; without a position the heading reads "Sightings in this area" and a "Use my location" button in the toolbar asks for the position again (shown after `denied`, `unavailable` or `timeout`, not when the browser has no geolocation).
- From `md` the home route uses a 64rem content width; from `lg` the map aside (legend, then the prompt or the selected card) sits beside the map; below `lg` it sits under the map.
- `/dev/kitchen-sink` shows the nearby map in every state of the 7-state matrix (or N/A with a reason) with no API, no login and no map tiles, alongside the existing Profile cells.
- `style-token-coverage.spec.ts` fails when a template outside `shared/ui` uses `<mat-progress-spinner`; `Frontend/CLAUDE.md` names the new components, the `map-view` API additions, the wide route layout and the map kitchen sink.
- `context/changes/nearby-map-ui-contract/visual-notes.md` holds the baseline and after evidence at 375px, `md` and `lg`.

### Key Discoveries:

- No missing-token charge: the only hidden size literal is `spinnerDiameter = 32` in TypeScript (`nearby-map-page.ts:57`), which the stylesheet guard does not scan; it moves into `app-loading-indicator` (`research.md`, C3).
- The runtime seam for map cells is `LEAFLET_LOADER` (`shared/map/map-view/map-view.ts:34-48`): a cell-level loader can return real Leaflet with a no-op `tileLayer`, and a failing loader shows the map's own error state (as `map-view.spec.ts:140-170` does). `MapViewStub` can only be swapped through `TestBed` (`shared/map/testing/map-view-stub.ts:5-21`).
- `map-view` emits `centerChange` on `moveend` (`map-view.ts:138`) but no zoom, so the zoom-10 rule needs a new output.
- At the 24rem map height and latitude 52°, a 10 km radius spans about half the map height at zoom 10 and a dot at zoom 6; the geolocated view opens at `nearbyMapZoom` 12 (`map-config.ts:15`), which stays above the threshold.
- `app-alert` already sets `role="alert"` for errors and `role="status"` otherwise (`shared/ui/alert/alert.ts:11,20`).
- `Frontend/CLAUDE.md:26` points at `context/changes/profile-ui-contract/visual-notes.md`, which now lives under `context/archive/2026-10-11-profile-ui-contract/`.
- `context/foundation/roadmap.md` has no item with this change ID; S-05 (filters) and S-06 (urgency list) will consume the badge and the shared components.

## What We're NOT Doing

- Drawing the 10 km search radius on the map (decided: not wanted).
- Showing "Use my location" when the browser has no geolocation (`unsupported`): a retry cannot succeed there.
- Auto-zooming before a search (decided: disable below zoom 10 instead).
- Widening the app header or any other route; the header stays at 40rem (app-shell navigation and width remain with research D3).
- Overlaying status or the card on the map.
- The warm-up overlay on slow queries (D1), unknown-route "not found" (D2), header navigation and document titles (D3), guest-layout flash (D4), `invalidateSize` (D5, the map container only changes width on viewport resize, which Leaflet tracks itself), a text list of results (D6, S-06), dark mode (D7), a screenshot runner (D8), gradient literals in `_tokens.scss` (D9).
- Kitchen-sink cells for the detail page; it is verified against the local API here and gets cells in its own follow-up change.
- A legend component: the legend is a list of badges in the map page; the badge is the shared part.
- Any API change other than new keys in `UiLabels.resx`.

## Implementation Approach

Follow the `/10x-ui` order: environment (a kitchen sink that can render the map, plus baseline evidence) → library (shared components, adopted everywhere, with no intended layout change apart from the badges) → the shared map component (C1, which also reaches the detail map and the location picker) → this one view (layout, C4, C5) → states, gate and guard. The baseline is captured first so the research's unverified claims (keyboard selection, fold positions at 375px, marker focus outline, the tile-less loader) are settled on screen before anything changes.

Repo conventions throughout: CRLF working copy (convert files written with LF), no code comments, TypeScript layout rules from `Frontend/CLAUDE.md`, map code imported by file path, new `shared/ui` components imported by file path from `shared/map` and from initial-bundle code.

## Critical Implementation Details

- **Tile-less Leaflet in the sink.** The module from `import('leaflet')` is a namespace object; the cell loader must return a new object (for example `{ default: { ...L, tileLayer: noop } }`, which `leafletOf()` in `map-view.ts` unwraps) rather than mutating it. Verify in Phase 1 that markers render and take focus with no request to `tile.openstreetmap.org`. If it cannot work, fall back to real tiles in the sink (dev-only), record that in `visual-notes.md`, and keep the number of map cells on the page small.
- **Live regions.** The toolbar status text is one element that exists from first render with `role="status"`; only its text changes. `app-loading-indicator` owns a live region by default (`live` input) and the map page turns it off inside the slot so the region is not nested. The error state renders `app-error-retry` (whose alert has `role="alert"`) in the same slot instead of the status text, not inside it.
- **Focus on select and close.** Selecting moves focus to the card heading (`tabindex="-1"`) after the card renders, which also scrolls it into view on a phone; at `lg` the card is already visible and focus still moves. Close clears the selection and calls `focusMarker(id)` on `app-map-view`, so a keyboard user lands back on the marker they came from. A new search clears the selection without moving focus.
- **Repeated location requests.** "Use my location" re-subscribes to `GeolocationService.currentPosition(false)`; route the requests through one stream with `switchMap` so a second press cancels the first instead of letting a late answer recentre the map after a newer one. After a browser-level `denied` the request fails immediately until the user changes the site permission, which is why the button stays available in that state.
- **Disabled but discoverable.** "Search this area" uses Material's `disabledInteractive` so it stays in the tab order while disabled, with `aria-describedby` pointing at the status text that explains why.

## Phase 1: Kitchen sink for the map and baseline

### Overview

Make the dev-only kitchen sink able to render the nearby map page with stubbed services and no tiles, add cells for the current view's states, and record "before" evidence. No production view changes in this phase.

### Changes Required:

#### 1. Generalised state cell

**File**: `PigeonWatch/Frontend/src/app/dev/kitchen-sink/cell-definitions.ts`, `state-cell.ts`, `state-cell.html`, `kitchen-sink.ts`, `kitchen-sink.html`, `kitchen-sink.scss`

**Intent**: Let a cell render either the Profile page or the nearby map page, so the sink covers more than one view without a second cell component.

**Contract**: `CellDefinition` gains `group: 'profile' | 'nearbyMap'`; `StateCell` renders the component for its group; the page groups cells under one heading per view and is titled "Kitchen sink" (literal English is allowed in `dev/`). Existing Profile cells keep their ids and behaviour.

#### 2. Stubs and driver

**File**: `PigeonWatch/Frontend/src/app/dev/kitchen-sink/cell-stubs.ts`, `cell-driver.ts`, `state-cell.ts`

**Intent**: Give map cells their own independent data and position, and a way to reach the selected state through the real DOM.

**Contract**: `CellStubs.sightingsApi` gains `nearby()` with modes `items` (fixture with one sighting of each criticality, one unnamed, two sharing a name), `empty`, `failure` and `pending` (never completes); `mine()` stays an empty result. A `GeolocationService` stub returns `ok`, `denied`, `timeout` or `pending` (`NEVER`). The cell provides a tile-less `LEAFLET_LOADER` (see Critical Implementation Details) and, for one cell, a failing loader. `cell-driver.ts` gains a step that clicks an element by selector (used to click a marker). Every stub is chosen per cell, as Profile cells do today.

#### 3. Baseline map cells

**File**: `PigeonWatch/Frontend/src/app/dev/kitchen-sink/cell-definitions.ts`

**Intent**: Show the unchanged view in each state it can reach today.

**Contract**: Cells: locating, position unknown, loading, loaded with markers, selected (driver clicks a marker), empty, nearby error, map library failed.

#### 4. Kitchen-sink spec

**File**: `PigeonWatch/Frontend/src/app/dev/kitchen-sink/kitchen-sink.spec.ts`

**Intent**: Keep the sink from rotting now that it has two groups.

**Contract**: The cell count and the per-cell assertion follow the definitions instead of a hard-coded 13 and `app-profile-page`; map cells swap `MapView` for `MapViewStub` on `NearbyMapPage` (jsdom has no layout), and the selected cell is reached by emitting `markerSelect` from the stub. Each map cell asserts its marker (locating text, hint alert, spinner, stub markers, card, empty text, error alert).

#### 5. Baseline evidence

**File**: `context/changes/nearby-map-ui-contract/visual-notes.md`

**Intent**: Record what the unchanged view looks like and settle the research's unverified claims before changing anything.

**Contract**: A "Baseline" section with one entry per map cell at 375px, `md` and `lg`, and explicit answers to: does Enter or Space on a focused marker select it (C1); is a focus outline visible on a marker; where do the spinner, empty text and card sit relative to the fold at 375px (C4); does the tile-less loader render focusable markers with no tile requests.

### Success Criteria:

#### Automated Verification:

- Unit tests pass, including the updated kitchen-sink spec: `npm test -- --watch=false` (from `PigeonWatch/Frontend`)
- Production build passes within the bundle budgets: `npm run build`
- Production output has no trace of the sink: no file under `dist/` contains `dev/kitchen-sink`
- Hardcoded-value scan returns 0 hits on the 4 nearby map view files and the kitchen-sink files

#### Manual Verification:

- With `npm start`, `/dev/kitchen-sink` renders every Profile cell as before and every baseline map cell, with no API, no login, and no request to `tile.openstreetmap.org` (or the recorded fallback)
- Baseline findings at 375px, `md` and `lg` are recorded in `visual-notes.md`, including the four questions above

**Implementation Note**: After completing this phase and all automated verification passes, pause here for manual confirmation from the human that the manual testing was successful before proceeding to the next phase. Phase blocks use plain bullets — the corresponding `- [ ]` checkboxes for these items live in the `## Progress` section at the bottom of the plan.

---

## Phase 2: Shared components (C2, C3)

### Overview

Add four `shared/ui` components and replace every copy of the blocks they cover. Apart from the criticality badge appearing where criticality was plain text and retry buttons keeping their natural width, the views should look as in the baseline.

### Changes Required:

#### 1. `app-loading-indicator`

**File**: `PigeonWatch/Frontend/src/app/shared/ui/loading-indicator/` (`.ts`, `.html`, `.scss`, `.spec.ts`)

**Intent**: One place owns the spinner, its sizes and its accessible label, removing the three `spinnerDiameter` literals.

**Contract**: Inputs `label` (required, translated text, used as the spinner's `aria-label`), `layout: 'block' | 'inline'` (`block` default: centred; `inline`: smaller spinner beside the label text), `live` (default `true`: host carries `role="status"`). The two diameters are constants in this component only.

#### 2. `app-error-retry`

**File**: `PigeonWatch/Frontend/src/app/shared/ui/error-retry/` (`.ts`, `.html`, `.scss`, `.spec.ts`)

**Intent**: One "something went wrong, try again" block so the two retry buttons on the map page stop disagreeing.

**Contract**: Projected content is the message; input `retryLabel` (required, translated); output `retry`. Renders `app-alert kind="error"` and a stroked button at its natural width, start-aligned.

#### 3. `app-detail-list` and `app-detail-item`

**File**: `PigeonWatch/Frontend/src/app/shared/ui/detail-list/` (`detail-list.*`, `detail-item.*`, specs)

**Intent**: One label/value list for the map card, the detail page and Profile.

**Contract**: `dl[app-detail-list]` with input `layout: 'column' | 'inline'` (column: rows stacked; inline: rows wrap side by side); `div[app-detail-item]` with inputs `label` (required, translated) and `value: 'plain' | 'text' | 'numeric'` (`text` preserves line breaks, `numeric` uses tabular figures), rendering `dt` (muted, small) and `dd` (projected content). Gaps from `--pw-space-*`. Valid `dl` > `div` > `dt`/`dd` markup.

#### 4. `app-criticality-badge`

**File**: `PigeonWatch/Frontend/src/app/shared/ui/criticality-badge/` (`.ts`, `.html`, `.scss`, `.spec.ts`), `PigeonWatch/Frontend/src/styles/_tokens.scss` (only if a dot size token is needed)

**Intent**: Show criticality as colour plus text everywhere it appears.

**Contract**: Input `kind: MapMarkerKind` (type-only import from `shared/map/map-marker.ts`); projected content is the translated label. A decorative dot (`aria-hidden`) filled from `--pw-color-criticality-*` beside the text in the normal text colour. Variant classes are internal to the component.

#### 5. Barrel

**File**: `PigeonWatch/Frontend/src/app/shared/ui/index.ts`

**Intent**: Export the four components for lazy feature pages.

**Contract**: `LoadingIndicator`, `ErrorRetry`, `DetailList`, `DetailItem`, `CriticalityBadge` exported. `shared/map` imports `ErrorRetry` by file path.

#### 6. Adopt everywhere

**File**: `features/sightings/nearby-map-page/nearby-map-page.{html,scss,ts}`, `shared/map/map-view/map-view.{html,scss,ts}`, `features/sightings/sighting-detail-page/sighting-detail-page.{html,scss,ts}`, `features/sightings/my-sightings/my-sightings.{html,scss,ts}`, `features/profile/profile-page/profile-page.{html,scss,ts}` (paths under `PigeonWatch/Frontend/src/app/`)

**Intent**: Replace every copy with the shared component in one go, with no other behavioural change.

**Contract**: Spinner wrappers → `app-loading-indicator` (block); error+retry blocks (including `map-view`'s load-failed block) → `app-error-retry`; the map card (inline), detail page (column, with `text`/`numeric` values) and Profile (column; the Edit button stays projected in the name `dd` with a local flex wrapper) → `app-detail-list`; criticality text on the map card, detail page and My sightings meta line → `app-criticality-badge` (kind from `sightingCriticalityMarker`). Removed: `spinnerDiameter` in all three components, `.nearby-spinner`, `.nearby-selected__details/__row/__value`, `.detail-spinner`, `.detail-list`, `.detail-row`, `.detail-value*`, `.my-sightings__spinner`, `.profile-details`, `.profile-row`. Existing feature specs that query removed classes are updated to query roles or components; their behaviour cases stay green.

### Success Criteria:

#### Automated Verification:

- Unit tests pass, including specs for the four new components: `npm test -- --watch=false`
- No feature or `shared/map` template contains `mat-progress-spinner`, and no `spinnerDiameter` remains under `src/app` (grep returns nothing)
- Production build passes within the bundle budgets: `npm run build`
- Hardcoded-value scan returns 0 hits on the touched view files, the new components and the kitchen-sink files

#### Manual Verification:

- In `/dev/kitchen-sink` the map cells and Profile cells look like the baseline except for the badge on the map card and the natural-width retry buttons
- With `npm start` against the local API, the detail page (loaded, loading, error) and Profile with My sightings items show the shared list, badge, spinner and error+retry with no layout regression at 375px and desktop

**Implementation Note**: After completing this phase and all automated verification passes, pause here for manual confirmation from the human that the manual testing was successful before proceeding to the next phase. Phase blocks use plain bullets — the corresponding `- [ ]` checkboxes for these items live in the `## Progress` section at the bottom of the plan.

---

## Phase 3: Map component (C1)

### Overview

Fix the keyboard and selection defects in `app-map-view`, the shared component, and give it the zoom output and focus method the view needs.

### Changes Required:

#### 1. Keyboard selection and selected state

**File**: `PigeonWatch/Frontend/src/app/shared/map/map-view/map-view.ts`, `map-view.spec.ts`, `PigeonWatch/Frontend/src/styles/_common.scss`, `PigeonWatch/Frontend/src/styles/_tokens.scss`

**Intent**: A keyboard user can open a sighting from its marker, and everyone can see which marker the card describes.

**Contract**: Enter or Space on a focused marker emits `markerSelect` (Space does not scroll the page). New input `selectedId: string | null` (default `null`): the matching marker gets `pw-map-marker--selected` and a raised z-index, every marker carries `aria-pressed` reflecting its state. `_common.scss` declares `.pw-map-marker--selected` (primary-coloured border and a larger size from a new `--pw-map-marker-size-selected` token) and, if the baseline showed no visible outline, `.pw-map-marker:focus-visible` from the `--pw-focus-*` tokens. Spec cases: Enter and Space emit, other keys do not, `selectedId` sets class and `aria-pressed`, changing it moves both.

#### 2. Zoom output and focus method

**File**: `PigeonWatch/Frontend/src/app/shared/map/map-view/map-view.ts`, `map-view.spec.ts`, `PigeonWatch/Frontend/src/app/shared/map/testing/map-view-stub.ts`, `PigeonWatch/Frontend/src/app/shared/map/map-config.ts`

**Intent**: Let the page know the zoom level for the search rule, and return focus to a marker after Close.

**Contract**: Output `zoomChange: number`, emitted on `moveend` together with `centerChange`. Public method `focusMarker(id: string): void` focuses that marker's element if present. `map-config.ts` exports `minSearchZoom = 10`. `MapViewStub` gains `selectedId`, `zoomChange` and a recorded `focusMarker`. The location picker and detail map are unaffected (`selectedId` stays `null`).

#### 3. Marker names with criticality

**File**: `PigeonWatch/Frontend/src/app/features/sightings/nearby-map-page/nearby-map-page.ts`, `nearby-map-page.html`, `nearby-map-page.spec.ts`, `PigeonWatch/Api/BusinessObjects/Resources/UiLabels.resx`

**Intent**: Markers with the same name become distinguishable, and urgency is in the name a screen reader reads.

**Contract**: New key `map.markerLabel` = `{0}, {1}` (name, criticality label); marker `label` uses it; the page binds `[selectedId]` to the current selection. Spec: a marker's label contains its criticality label; the selected id reaches the stub.

### Success Criteria:

#### Automated Verification:

- Unit tests pass, including the new `map-view` keyboard, selected-state and zoom cases: `npm test -- --watch=false`
- Production build passes within the bundle budgets: `npm run build`
- Hardcoded-value scan returns 0 hits on the touched view and map files

#### Manual Verification:

- In `/dev/kitchen-sink`, Tab reaches a marker, its focus is visible, Enter and Space each open the card, and the selected marker is visibly different from the others
- The location picker on `/sightings/new` still places, drags and centres the pin as before

**Implementation Note**: After completing this phase and all automated verification passes, pause here for manual confirmation from the human that the manual testing was successful before proceeding to the next phase. Phase blocks use plain bullets — the corresponding `- [ ]` checkboxes for these items live in the `## Progress` section at the bottom of the plan.

---

## Phase 4: Map view layout (C4, C5)

### Overview

Rebuild the page around a toolbar above the map, a map aside, and a wider desktop layout; stop the misleading country-wide search.

### Changes Required:

#### 1. Wide route layout

**File**: `PigeonWatch/Frontend/src/app/app.routes.ts`, `app.ts`, `app.html`, `app.scss`, `PigeonWatch/Frontend/src/styles/_tokens.scss`

**Intent**: Give the home route more width on desktop without forking the shell for one feature.

**Contract**: The home route declares `data: { layout: 'wide' }`; the shell derives a signal from the active route's data after each navigation and adds `app-main--wide` to `<main>`; from `md` that class uses a new `--pw-content-wide-max-width` (64rem). The header is unchanged. `app.ts` keeps importing nothing from `shared/map` and nothing new from the `shared/ui` barrel.

#### 2. Page structure and aside

**File**: `PigeonWatch/Frontend/src/app/features/sightings/nearby-map-page/nearby-map-page.{html,scss,ts}`, `PigeonWatch/Frontend/src/styles/_tokens.scss`

**Intent**: Put feedback next to the action and the selected card where the user can see it.

**Contract**: Order: header (h1 + Report) → toolbar (Search this area + status slot) → body (map + aside). Body is one column; from `lg` it is two columns, the map flexible and the aside a new `--pw-map-aside-width` (18rem). The aside always shows the legend (heading plus the three badges with their labels as a list), then either the select prompt or the selected card. The card loses `aria-live`; its heading has `tabindex="-1"`.

#### 3. Toolbar, status slot and search rule

**File**: `PigeonWatch/Frontend/src/app/features/sightings/nearby-map-page/nearby-map-page.{html,scss,ts}`, `nearby-map-page.spec.ts`

**Intent**: One place says what the page is doing, the map stops jumping, and a search below zoom 10 cannot happen.

**Contract**: The status text element exists from first render with `role="status"` and a reserved minimum height (`--pw-control-height`). Its content, first match wins: locating → zoom below `minSearchZoom` (`map.zoomToSearch`) → position unknown and no search yet (`map.hint`) → loading (inline `app-loading-indicator`, `live` off) → loaded with results (`map.found`) → loaded empty (`map.empty`). The error state renders `app-error-retry` in the slot instead. The page tracks zoom from `zoomChange` (initially `viewZoom`). "Search this area" is disabled (with `disabledInteractive` and `aria-describedby` on the status text) while zoom is below `minSearchZoom` or a search is loading. The loading state keeps the previous results on the map; a new search still clears the selection. Spec cases: disabled at zoom 9, enabled at 10; status text per state; markers kept during loading; error renders the retry block and retry repeats the last search.

#### 4. Heading, focus and labels

**File**: `PigeonWatch/Frontend/src/app/features/sightings/nearby-map-page/nearby-map-page.{html,ts}`, `nearby-map-page.spec.ts`, `PigeonWatch/Api/BusinessObjects/Resources/UiLabels.resx`

**Intent**: The heading stops claiming to know the user's position, and selection is announced by moving focus.

**Contract**: h1 is `map.title` while locating or with a position, `map.titleArea` once the position is unknown. Selecting moves focus to the card heading after render; Close clears the selection and calls `focusMarker(id)` (see Critical Implementation Details). New keys: `map.titleArea` = "Sightings in this area", `map.zoomToSearch` = "Zoom in to search an area", `map.found` = "{0} open sightings within {1} km", `map.selectPrompt` = "Select a marker to see details", `map.legend.title` = "Criticality". Spec cases: heading switch on `denied`; focus on the card heading after `markerSelect`; Close calls `focusMarker` with the selected id.

#### 5. Use my location

**File**: `PigeonWatch/Frontend/src/app/features/sightings/nearby-map-page/nearby-map-page.{html,ts}`, `nearby-map-page.spec.ts`

**Intent**: A user whose location was denied, unavailable or timed out can try again without reloading the page.

**Contract**: A stroked "Use my location" button (existing key `map.useMyLocation`) sits in the toolbar after "Search this area" when the last geolocation outcome is `denied`, `unavailable` or `timeout`; it is absent for `ok` and `unsupported`, and disabled while a request is in flight. Pressing it sets the locating state and asks for the position again with the same low accuracy as on load. On `ok` the page behaves as on a first successful load: the map moves to the position at `nearbyMapZoom`, the heading returns to `map.title`, the button disappears and a search runs. On failure the page returns to the position-unknown state (status `map.hint`, or `map.zoomToSearch` below zoom 10) and the button stays. Spec cases: button shown for `denied`, `unavailable` and `timeout`, absent for `ok` and `unsupported`; a press that resolves `ok` recentres, switches the heading and searches; a press that fails keeps the button and the hint; a second press while the first is pending does not let the first answer win.

### Success Criteria:

#### Automated Verification:

- Unit tests pass, including the new page and shell cases: `npm test -- --watch=false`
- Production build passes within the bundle budgets, initial bundle still under the 500 kB warning: `npm run build`
- Hardcoded-value scan returns 0 hits on the view files and `app.scss`

#### Manual Verification:

- At 375px: pressing "Search this area" shows loading and the result in the toolbar without scrolling; tapping a marker brings the card into view with focus on its heading; the map does not jump when geolocation resolves
- At `md` the page is wider than other routes with the aside below the map; at `lg` the aside sits beside the map and shows the legend and prompt when nothing is selected
- With location denied: the heading reads "Sightings in this area", the button is disabled at country zoom with the reason in the toolbar, and becomes enabled after zooming in to level 10
- After a location timeout, "Use my location" is in the toolbar; when the retry succeeds the map moves to the position, the heading reads "Sightings near you" and results appear; when it fails the hint returns and the button stays

**Implementation Note**: After completing this phase and all automated verification passes, pause here for manual confirmation from the human that the manual testing was successful before proceeding to the next phase. Phase blocks use plain bullets — the corresponding `- [ ]` checkboxes for these items live in the `## Progress` section at the bottom of the plan.

---

## Phase 5: States, gate and guard

### Overview

Complete the 7-state matrix in the kitchen sink, capture the after evidence, do the keyboard pass, and leave the spec check and the rule.

### Changes Required:

#### 1. Complete the matrix

**File**: `PigeonWatch/Frontend/src/app/dev/kitchen-sink/` (cells, stubs, spec), `context/changes/nearby-map-ui-contract/visual-notes.md`

**Intent**: Make every state of the rebuilt view visible and record which are shown and which are N/A with a reason.

**Contract**: New or updated map cells: zoomed out with search disabled, position unknown at zoom 10 (hint), position unknown with "Use my location" (geolocation stub answers `timeout` first and a configurable outcome on the next request), retry locating, loading with previous markers, found, selected via keyboard, nearby error, map library failed; plus one Profile cell where My sightings has items (badge in the meta line), which needs a `mine()` items mode. Matrix for the map view: default (loaded, aside with legend and prompt); hover (Material state layer on buttons, pointer on markers); focus-visible (buttons, markers, card heading); disabled (Search below zoom 10 and while loading, Use my location while locating); error (nearby failure with retry, map load failure, text plus alert role); empty (found nothing, wording names the 10 km radius); loading (inline indicator in the slot, markers kept, no layout jump). `visual-notes.md` gets an "After" section at 375px, `md` and `lg`, with each delta against the baseline labelled intended or fixed.

#### 2. Keyboard and accessible-name pass

**File**: findings in `context/changes/nearby-map-ui-contract/visual-notes.md`; fixes in the owning component only if the pass finds a defect

**Intent**: Verify the floor on the rebuilt page.

**Contract**: Tab order follows the visual order (Report, Search this area, Use my location when shown, map controls and markers, aside, card actions); every control and marker has a name; Enter and Space select; Close returns focus to the marker. Any defect is fixed where it lives or recorded as deferred with a reason.

#### 3. Spec check

**File**: `PigeonWatch/Frontend/src/styles/style-token-coverage.spec.ts`

**Intent**: Turn "use the shared spinner" into a failing test.

**Contract**: An added assertion over every template under `src/app` outside `src/app/shared/ui/`: no `<mat-progress-spinner` element. Existing checks stay.

#### 4. Agent rule

**File**: `PigeonWatch/Frontend/CLAUDE.md`

**Intent**: Point the next agent at the new components, the map API and the extended sink.

**Contract**: The `shared/ui` bullet lists `app-loading-indicator`, `app-error-retry`, `app-detail-list`/`app-detail-item` and `app-criticality-badge`, and says criticality is never shown as plain text; a note that features use `app-loading-indicator` instead of `mat-progress-spinner` (enforced by the spec); the Maps section names `selectedId`, `zoomChange`, `focusMarker`, keyboard selection and `minSearchZoom`, and the geolocation bullet's "waits for Search this area" reflects the zoom rule and the map home's "Use my location" retry; a sentence on `data: { layout: 'wide' }` and `--pw-content-wide-max-width`; the kitchen-sink bullet covers both groups, the map stubs and the tile-less loader, and its `visual-notes.md` path points at the archived profile notes and this change's notes.

### Success Criteria:

#### Automated Verification:

- Unit tests pass, including the extended `style-token-coverage.spec.ts` and the kitchen-sink spec: `npm test -- --watch=false`
- Production build passes within the bundle budgets: `npm run build`
- Production output has no trace of the sink: no file under `dist/` contains `dev/kitchen-sink`
- Hardcoded-value scan returns 0 hits (no higher than the Phase 1 count) on the view files, the touched features, the shared components and the kitchen-sink files
- `Frontend/CLAUDE.md` names `app-loading-indicator`, `app-error-retry`, `app-criticality-badge`, `selectedId` and `minSearchZoom`, and no longer references `context/changes/profile-ui-contract/` (grep)

#### Manual Verification:

- Every cell of the 7-state matrix in `visual-notes.md` is shown on screen or marked N/A with a reason, at 375px, `md` and `lg`
- Keyboard pass: every control and marker shows a visible focus ring, has an accessible name, tabs in visual order, and Close returns focus to the marker
- After screenshots compared with the baseline; every visual delta in `visual-notes.md` is labelled intended or fixed
- The next step, `/10x-impl-review`, is run before archive; UI findings are triaged by user impact, not skipped as cosmetic

**Implementation Note**: After completing this phase and all automated verification passes, pause here for manual confirmation from the human that the manual testing was successful before proceeding to the next phase. Phase blocks use plain bullets — the corresponding `- [ ]` checkboxes for these items live in the `## Progress` section at the bottom of the plan.

---

## Testing Strategy

### Unit Tests:

- New `shared/ui` components: indicator layouts and `live`, error-retry projection and `retry` output, detail-list layouts and value variants, badge kind and decorative dot.
- `app-map-view`: Enter/Space selection, other keys ignored, `selectedId` class and `aria-pressed`, `zoomChange`, `focusMarker`; existing click, pin and load-failure cases stay green.
- `NearbyMapPage`: zoom rule, "Use my location" visibility per outcome and its success, failure and repeated-press paths, status text per state, markers kept while loading, heading switch, focus on select, Close returns focus, marker labels with criticality.
- Shell: `app-main--wide` only on the home route.
- Detail page, My sightings, Profile: existing behaviour cases stay green after the swap.
- Kitchen-sink spec and the extended `style-token-coverage.spec.ts`.

### Integration Tests:

- None added. The repo has no browser test runner and this plan does not install one; the kitchen sink plus browser-pane screenshots is the visual gate.

### Manual Testing Steps:

1. `npm start`, open `/dev/kitchen-sink`, go through every map and Profile cell at 375px, `md` and `lg` (browser pane viewport emulation).
2. Tab through a loaded map cell: reach a marker, press Enter and Space, check the card takes focus, press Close and check focus returns to the marker.
3. In a position-unknown cell, zoom from the country view to level 10 and watch the button and status text change; in the "Use my location" cell press the button with the next outcome set to `ok` and then to `timeout`.
4. Against the local API, open `/`, a sighting's detail page and `/profile` to confirm the shared components in real data.
5. Run `npm run build` and confirm the sink is absent from `dist/` and the initial bundle stays under budget.

## Performance Considerations

The shell change adds a route-data read to `app.ts`, which is in the initial bundle (486.2 kB, warning at 500 kB); it imports nothing from `shared/map` and nothing new from the `shared/ui` barrel, and `npm run build` runs in every phase. Keeping markers during loading reuses the same marker layer. The sink's tile-less loader keeps dev map cells off the OpenStreetMap tile servers.

## Migration Notes

None: no data or API contract changes. `UiLabels.resx` gains six keys (`map.markerLabel`, `map.titleArea`, `map.zoomToSearch`, `map.found`, `map.selectPrompt`, `map.legend.title`); no existing key changes.

## References

- Research and charges: `context/changes/nearby-map-ui-contract/research.md`
- Prior UI contract change (phase order, sink, guard): `context/archive/2026-10-11-profile-ui-contract/plan.md`
- Map deferrals this change picks up: `context/archive/2026-10-11-report-sighting-on-map/plan.md:42-43`, `visual-notes.md:30-37`
- Design-system contract and UI rules: `PigeonWatch/Frontend/CLAUDE.md`, `.claude/skills/10x-ui/SKILL.md`
- Token source: `PigeonWatch/Frontend/src/styles/_tokens.scss`; common classes: `_common.scss`; guard: `style-token-coverage.spec.ts`
- Shared map component: `PigeonWatch/Frontend/src/app/shared/map/map-view/map-view.ts`
- Follow-up: `sighting-detail-ui-contract` (detail page kitchen-sink cells and any remaining layout work)

## Progress

> Convention: `- [ ]` pending, `- [x]` done. Append ` — <commit sha>` when a step lands. Do not rename step titles. See `references/progress-format.md`.

### Phase 1: Kitchen sink for the map and baseline

#### Automated

- [x] 1.1 Unit tests pass, including the updated kitchen-sink spec: `npm test -- --watch=false` (from `PigeonWatch/Frontend`) — 049cc88
- [x] 1.2 Production build passes within the bundle budgets: `npm run build` — 049cc88
- [x] 1.3 Production output has no trace of the sink: no file under `dist/` contains `dev/kitchen-sink` — 049cc88
- [x] 1.4 Hardcoded-value scan returns 0 hits on the 4 nearby map view files and the kitchen-sink files — 049cc88

#### Manual

- [x] 1.5 With `npm start`, `/dev/kitchen-sink` renders every Profile cell as before and every baseline map cell, with no API, no login, and no request to `tile.openstreetmap.org` (or the recorded fallback) — 049cc88
- [x] 1.6 Baseline findings at 375px, `md` and `lg` are recorded in `visual-notes.md`, including the four questions above — 049cc88

### Phase 2: Shared components (C2, C3)

#### Automated

- [x] 2.1 Unit tests pass, including specs for the four new components: `npm test -- --watch=false` — 7f6cc22
- [x] 2.2 No feature or `shared/map` template contains `mat-progress-spinner`, and no `spinnerDiameter` remains under `src/app` (grep returns nothing) — 7f6cc22
- [x] 2.3 Production build passes within the bundle budgets: `npm run build` — 7f6cc22
- [x] 2.4 Hardcoded-value scan returns 0 hits on the touched view files, the new components and the kitchen-sink files — 7f6cc22

#### Manual

- [x] 2.5 In `/dev/kitchen-sink` the map cells and Profile cells look like the baseline except for the badge on the map card and the natural-width retry buttons — 7f6cc22
- [x] 2.6 With `npm start` against the local API, the detail page (loaded, loading, error) and Profile with My sightings items show the shared list, badge, spinner and error+retry with no layout regression at 375px and desktop — 7f6cc22

### Phase 3: Map component (C1)

#### Automated

- [x] 3.1 Unit tests pass, including the new `map-view` keyboard, selected-state and zoom cases: `npm test -- --watch=false` — 0b6e9a6
- [x] 3.2 Production build passes within the bundle budgets: `npm run build` — 0b6e9a6
- [x] 3.3 Hardcoded-value scan returns 0 hits on the touched view and map files — 0b6e9a6

#### Manual

- [x] 3.4 In `/dev/kitchen-sink`, Tab reaches a marker, its focus is visible, Enter and Space each open the card, and the selected marker is visibly different from the others — 0b6e9a6
- [x] 3.5 The location picker on `/sightings/new` still places, drags and centres the pin as before — 0b6e9a6

### Phase 4: Map view layout (C4, C5)

#### Automated

- [x] 4.1 Unit tests pass, including the new page and shell cases: `npm test -- --watch=false`
- [x] 4.2 Production build passes within the bundle budgets, initial bundle still under the 500 kB warning: `npm run build`
- [x] 4.3 Hardcoded-value scan returns 0 hits on the view files and `app.scss`

#### Manual

- [x] 4.4 At 375px: pressing "Search this area" shows loading and the result in the toolbar without scrolling; tapping a marker brings the card into view with focus on its heading; the map does not jump when geolocation resolves
- [x] 4.5 At `md` the page is wider than other routes with the aside below the map; at `lg` the aside sits beside the map and shows the legend and prompt when nothing is selected
- [x] 4.6 With location denied: the heading reads "Sightings in this area", the button is disabled at country zoom with the reason in the toolbar, and becomes enabled after zooming in to level 10
- [x] 4.7 After a location timeout, "Use my location" is in the toolbar; when the retry succeeds the map moves to the position, the heading reads "Sightings near you" and results appear; when it fails the hint returns and the button stays

### Phase 5: States, gate and guard

#### Automated

- [ ] 5.1 Unit tests pass, including the extended `style-token-coverage.spec.ts` and the kitchen-sink spec: `npm test -- --watch=false`
- [ ] 5.2 Production build passes within the bundle budgets: `npm run build`
- [ ] 5.3 Production output has no trace of the sink: no file under `dist/` contains `dev/kitchen-sink`
- [ ] 5.4 Hardcoded-value scan returns 0 hits (no higher than the Phase 1 count) on the view files, the touched features, the shared components and the kitchen-sink files
- [ ] 5.5 `Frontend/CLAUDE.md` names `app-loading-indicator`, `app-error-retry`, `app-criticality-badge`, `selectedId` and `minSearchZoom`, and no longer references `context/changes/profile-ui-contract/` (grep)

#### Manual

- [ ] 5.6 Every cell of the 7-state matrix in `visual-notes.md` is shown on screen or marked N/A with a reason, at 375px, `md` and `lg`
- [ ] 5.7 Keyboard pass: every control and marker shows a visible focus ring, has an accessible name, tabs in visual order, and Close returns focus to the marker
- [ ] 5.8 After screenshots compared with the baseline; every visual delta in `visual-notes.md` is labelled intended or fixed
- [ ] 5.9 The next step, `/10x-impl-review`, is run before archive; UI findings are triaged by user impact, not skipped as cosmetic
