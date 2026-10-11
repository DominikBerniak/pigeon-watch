---
date: 2026-10-11T21:26:17+02:00
researcher: Claude (Opus 5.5) for Dominik Berniak
git_commit: 00f328293bf7aec85b5cef55646b79ae55ed547a
branch: main
repository: 10xDevs (PigeonWatch)
topic: "UI audit of the nearby map home view (/) against the design-system contract"
tags: [research, ui, 10x-ui, nearby-map-page, map-view, shared-ui, kitchen-sink]
status: complete
last_updated: 2026-10-11
last_updated_by: Claude (Opus 5.5)
---

# Research: UI audit of the nearby map home view (`/`)

**Date**: 2026-10-11T21:26:17+02:00
**Researcher**: Claude (Opus 5.5) for Dominik Berniak
**Git Commit**: 00f328293bf7aec85b5cef55646b79ae55ed547a
**Branch**: main
**Repository**: 10xDevs (PigeonWatch)

Paths below are relative to `PigeonWatch/Frontend/src/` unless they start with `context/`, `PigeonWatch/` or `node_modules/`. `NM` = `app/features/sightings/nearby-map-page/nearby-map-page`.

## Research Question

`/10x-ui` two-way audit of the nearby map home view (`NM.html`, `NM.ts`, `NM.scss`, plus the selected-sighting card it renders), per `change.md`: (1) source → views: which tokens, `pw-*` classes, `shared/ui` and `shared/map` components the view reads, and which repeated patterns should be shared; (2) view → source: for each local class or duplicated block, which token or component should cover it; (3) entry points: what a user sees logged out, without a position, with no results, while loading or failing, from a link; (4) what the kitchen sink needs to show this view's states. Output: 3–5 charges with file, line and user effect.

## Summary

The view is already on the token contract: the hardcoded-value scan over the 4 view files finds 0 hits, all 3 spacing values in the view stylesheet read `var(--pw-*)` (`NM.scss:15,25,32`), and `style-token-coverage.spec.ts` covers this folder (`styles/style-token-coverage.spec.ts:23-39,101-108`). **No missing-token charge is raised.** The one hidden size literal is `spinnerDiameter = 32` in TypeScript (`NM.ts:57`), which the guard does not scan; it is folded into C3.

The charges are in the other two categories:

| # | Category | Charge (short) | Anchor |
|---|---|---|---|
| C1 | Accidental architecture | Markers can be tabbed to but not selected from the keyboard; no selected-marker state | `app/shared/map/map-view/map-view.ts:165-178` |
| C2 | Missing shared component | Criticality is colour on the map and plain text everywhere else; no legend | `NM.html:71-73`, `styles/_common.scss:74-84` |
| C3 | Missing shared component | Spinner wrapper, label/value list and error+retry block copied across 3 features | `NM.html:26-41,60-75`, `NM.scss:5-8,22-37`, `NM.ts:57` |
| C4 | Accidental architecture | All feedback (loading, empty, error, selected card) is appended below the map; on a phone a tap or search appears to do nothing | `NM.html:25-86`, `NM.ts:70-74,139-141` |
| C5 | Accidental architecture | Without a position the page searches 10 km around the centre of Poland at country zoom and reports "no sightings" | `NM.ts:53,110-116,131-133`, `app/shared/map/map-config.ts:10-13` |

The app has no dark theme (`styles/_material-theme.scss:4,9`), so "both themes" is N/A. The kitchen sink renders only the Profile page (`app/dev/kitchen-sink/state-cell.html:10`) and needs a generalised cell plus `nearby`/geolocation stubs and a tile-less Leaflet loader to show this view's states. Roadmap slice S-05 (filters on this page) is the nearest collision.

## Charges

Each charge: file:line evidence, the token/component that should cover it, the effect on the user.

### C1 — Markers are keyboard-focusable but not keyboard-selectable (accidental architecture)

- **Evidence.** `applyMarkers()` creates each marker with `keyboard: true` and `title: marker.label` and subscribes only to Leaflet `'click'` (`app/shared/map/map-view/map-view.ts:165-178`, the `.on('click', …)` at `:177`). With `keyboard: true`, Leaflet 1.9.4 sets `tabindex=0` and `role="button"` on the icon (`node_modules/leaflet/src/layer/marker/Marker.js:236-238`). A key press on a focused marker reaches the layer as a `keypress` event, not `click` (`node_modules/leaflet/src/map/Map.js:1322,1443`); the only Leaflet handler that acts on Enter is the popup's `_onKeyPress` (`node_modules/leaflet/src/layer/Popup.js:501-504`), and no popup is bound here (`Frontend/CLAUDE.md` rule: selection shows an Angular card, not a popup). `map-view.spec.ts:91-99` only tests a programmatic `.click()`.
- **Also.** The accessible name is the sighting name or "Unnamed bird" (`NM.ts:76-84`), so several identical names are possible and criticality is not in the name; `MapMarker` has no selected flag (`app/shared/map/map-marker.ts:5-9`), so the map does not show which marker the card describes; `autoPanOnFocus` (Leaflet default, `Marker.js:80-85`) pans the map as markers take focus, which fires `centerChange` (`map-view.ts:138`, `NM.ts:127-129`) and silently moves the centre that "Search this area" will query.
- **Should be covered by.** `app-map-view` (the shared component): emit `markerSelect` on Enter/Space as well as click, carry a selected marker state (a `selected` id input and a class from `_common.scss` built on the focus/primary tokens), and give the marker a name that includes criticality.
- **Effect on the user.** A keyboard or screen-reader user can reach every marker on the home page but cannot open any sighting from it, and nobody can tell which marker the card below belongs to.
- **Verification status.** Derived from the Leaflet 1.9.4 source and `map-view.ts`; not yet observed in a browser.

### C2 — Criticality has no shared visual outside the map, and no legend (missing shared component)

- **Evidence.** The `--pw-color-criticality-*` tokens (`styles/_tokens.scss:21-23`) are used in exactly one place, the marker classes `.pw-map-marker--monitor/--needs-help-soon/--urgent` (`styles/_common.scss:74-84`; repo grep by the source→views pass). The selected card shows criticality as plain text (`NM.html:71-73`); so do the detail page (`app/features/sightings/sighting-detail-page/sighting-detail-page.html:39`) and My sightings (`app/features/sightings/my-sightings/my-sightings.html:34-40`). A grep for "legend" finds only the location-picker `<legend>` (`app/shared/map/location-picker/location-picker.html:2`); nothing on the page explains the three marker colours.
- **Should be covered by.** A new `shared/ui` component (working name `app-criticality-badge`: token dot + translated label, `kind` input reusing `MapMarkerKind` from `map-marker.ts`), used on the selected card and as the basis of a legend next to the map. The detail page and My sightings adopt it in their own changes (`sighting-detail-ui-contract` follows this one).
- **Effect on the user.** Urgency is communicated by colour alone on the map and then disappears on the card the user opens; a user who cannot tell green/orange/red apart, or who has not guessed the mapping, cannot find the urgent birds.

### C3 — Three blocks of this view are copied in other features instead of shared (missing shared component)

- **Spinner wrapper.** `NM.html:26-33` + `.nearby-spinner` (`NM.scss:5-8`) + `spinnerDiameter = 32` (`NM.ts:57`); identical copies in `sighting-detail-page.html:8-14` / `.scss:1-4` / `.ts:54` and `my-sightings.html:6-12` / `.scss:5-8` / `.ts:38`. The diameter is a size literal in TypeScript that `style-token-coverage.spec.ts` does not scan (it reads `.scss` and `.html` only, `styles/style-token-coverage.spec.ts:23-39`).
- **Label/value list.** `NM.html:60-75` + `.nearby-selected__details/__row/__value` (`NM.scss:22-37`, wrapping row); same structure as `.detail-list/.detail-row/.detail-value` (`sighting-detail-page.html:30-75`, `.scss:6-30`, column with `--text`/`--numeric` variants) and `.profile-details/.profile-row/.profile-value` (`app/features/profile/profile-page/profile-page.html:6-24`, `.scss:7-31`). In all three inspected copies `dt` is `pw-text-muted pw-text-small`, the row gap is `--pw-space-1` and `dd` has `margin: 0`.
- **Error + retry.** `NM.html:35-41`; same block in `sighting-detail-page.html:20-25` and `my-sightings.html:15-20`. Inside `.pw-stack` (flex column, default `align-items: stretch`, `styles/_common.scss:8-12`) the retry button stretches to full width, while the shared `map-view` error block (`app/shared/map/map-view/map-view.html:3-10`) keeps its retry button at natural width — two "Try again" buttons with two shapes can be on screen together on this page.
- **Should be covered by.** `shared/ui` components, per the repo rule "when a pattern shows up in a second feature, promote it to `shared/ui`" (`PigeonWatch/Frontend/CLAUDE.md:24`): a loading indicator (label input, diameter owned in one place), a label/value list (`layout` input: `column` | `inline`; value variants), and an error-with-retry (or an action slot on `app-alert`). This change adopts them in the map view; the other two features adopt them in their own changes or in this one if the plan chooses (see Open Questions).
- **Effect on the user.** The same information (status, criticality, "something went wrong, try again", "loading") looks and behaves differently on each screen, and on this screen the two retry buttons disagree; each new view (S-05, S-06) would copy the blocks again.

### C4 — Every piece of feedback lands below the map (accidental architecture)

- **Evidence.** The loading spinner, error, empty text and selected card are conditional blocks appended under the map and the "Search this area" button (`NM.html:25-86`). Entering `loading` empties `sightings()` (`NM.ts:70-74`), so all markers disappear while the spinner renders below. `select()` only sets a signal (`NM.ts:139-141`): focus is not moved, nothing scrolls into view, and `aria-live="polite"` sits on the `mat-card` that `@if` creates together with its content (`NM.html:51-52`), which screen readers announce unreliably. Above the map, the "locating" line (`NM.html:6-8`) is replaced by the hint alert (`NM.html:9-11`), so the map shifts when geolocation resolves. The map is `--pw-map-height` = 24rem (`styles/_tokens.scss:77`) inside the 40rem content column (`app/app.scss:20-24`, `styles/_tokens.scss:70`).
- **Inferred (not measured).** With the sticky header, the title row (which wraps at 375 px, report-sighting V6) and a 24rem map, the region below the map starts roughly 600 px down the page, so on a phone the spinner, the empty text and the selected card are below the fold.
- **Also.** "Search this area" is never disabled (`NM.html:21`); repeated presses restart the spinner and clear markers and selection (`NM.ts:147-150`), though `switchMap` prevents duplicate results (`NM.ts:95`). A query slower than 3 s raises the global full-screen warm-up overlay over the page's own spinner (`app/core/warmup/warmup.interceptor.ts:8-9,33`, `warmup-panel.scss:1-10`) — see deferred D1.
- **Should be covered by.** A layout for this view that puts status where the user is looking: loading/empty/error feedback on or directly attached to the map region (not after the button), the selected card brought into view (or focus moved to it) on selection, a persistent live region for announcements, and a reserved slot so the map does not jump. Built from `pw-*` layout classes, existing tokens and the C3 components.
- **Effect on the user.** On a phone, tapping a marker or pressing "Search this area" appears to do nothing — the map goes blank or stays the same and the result is off-screen.

### C5 — No position means a misleading country-wide "nothing here" (accidental architecture)

- **Evidence.** Without a position the map opens on the neutral view, centre of Poland at zoom 6 (`app/shared/map/map-config.ts:10-13`, `NM.ts:58-59`), and `mapCentre` starts at that centre (`NM.ts:53`). On `denied`/`unavailable`/`timeout`/`unsupported` the page sets `positionUnknown` and does not search (`NM.ts:110-116`); the hint says to move the map and press "Search this area" (`NM.html:10`, label `map.hint`). "Search this area" is enabled from the first render, including while still locating (`NM.html:21`, `NM.ts:131-133`), and queries the API's 10 km radius around the map centre (`PigeonWatch/CLAUDE.md`, nearby = 10 km). At zoom 6 the visible area is the whole country, so a press without zooming in returns "No open sightings within 10 km of this area." (`NM.html:45-47`, label `map.empty`). The radius is never drawn on the map. There is no "Use my location" retry on this page (the label is used only by the location picker, `app/shared/map/location-picker/location-picker.html:17`). The h1 still reads "Sightings near you" (`NM.html:3`).
- **Should be covered by.** An entry-point fix in the view, not a colour: either keep the search action unavailable (with the reason shown) until the map is zoomed to a level where 10 km is meaningful, or move the map to an appropriate zoom before searching; show the search radius on the map; offer a retry of geolocation. All with existing tokens and `app-alert`.
- **Effect on the user.** A user who denied location (or whose browser timed out) presses the one button the page tells them to press and is told there are no sightings, while looking at a map of the entire country — the most likely conclusion is "the app is empty".

### Deferred (not addressed by this change)

| # | Finding | Anchor | Reason deferred |
|---|---|---|---|
| D1 | Any API call pending >3 s shows the full-screen warm-up overlay, including a slow (not cold) nearby query | `app/core/warmup/warmup.interceptor.ts:8-9,33,47-64` | Global interceptor in `core/`, shared by every view; one view per change |
| D2 | Logged-in user on an unknown route is redirected to `/` with no "not found" message | `app/core/auth/auth.guards.ts:26-41` | Routing-wide, not this view |
| D3 | Header has no navigation back to the map (`nav.home` / `nav.mainNavigation` labels exist but are unused); no per-route document title | `app/app.html:3-21`, `index.html:5` | App shell, not this view; candidate for its own change |
| D4 | Possible flash of the guest layout before the session restores | `app/app.config.ts:20-21`, `app/app.html:3,23-27` | Inferred only; app shell |
| D5 | `MapView` never calls `invalidateSize()` on resize (report-sighting V1) | `app/shared/map/map-view/map-view.ts` | Not observed as a defect on this view yet; revisit if the layout work in C4 changes the map container size — then it becomes in scope |
| D6 | No text alternative/list of the loaded results (results exist only as markers) | `NM.html:43-49` | S-06 (urgency list) is the slice that adds a list; C1 makes markers operable meanwhile |
| D7 | Dark mode / both-theme contrast | `styles/_material-theme.scss:4,9` | No second theme exists — N/A (same as profile-ui-contract D2) |
| D8 | Screenshot-test runner | — | None in the repo; do not install one (profile-ui-contract D3); the kitchen sink is the gate |
| D9 | Gradient literals in `_tokens.scss` | `styles/_tokens.scss:25-29` | Inherited deferral (profile-ui-contract D1); token source, not the view |

## Detailed Findings

### Source → views

- Imports: the view uses `Alert` from `shared/ui` and `MapView`, `map-config`, `map-marker` from `shared/map` (`NM.ts:13-16`), plus Material button, card and spinner modules (`NM.ts:34-42`). `app-page-card` is not used, and its host width `--pw-form-max-width` (`app/shared/ui/page-card/page-card.scss:6`) would be too narrow for a map; there is no shared full-width page header with an action slot, so the `h1` + action row is hand-written (`NM.html:2-5`, `NM.scss:1-3`) and its `h1` uses the fixed `pw-title` size where `app-page-card` uses responsive sizes (`page-card.scss:29-31,49-51`).
- `pw-*` classes read: `pw-stack`, `pw-cluster`, `pw-title`, `pw-subtitle`, `pw-text-muted`, `pw-text-small` (`NM.html`). Local classes: `.nearby-header`, `.nearby-spinner`, `.nearby-empty`, `.nearby-selected__*` (`NM.scss:1-37`), all either pure layout or one of the C3 duplicates.
- The selected card is `mat-card appearance="outlined"`, themed from tokens via `mat.card-overrides` (`styles/_material-theme.scss:53-59`); it does not carry `app-page-card`'s `--pw-shadow-lg`. The same bare outlined card + `h2.pw-subtitle` pattern also appears in My sightings (`my-sightings.html:1-3`).
- Focus: Material controls use `mat.strong-focus-indicators` with `--pw-color-focus` (`styles/_material-theme.scss:84-90`); other elements, including Leaflet markers, fall to the global `:focus-visible` outline (`styles/_base.scss:50-53`). No rule targets `.pw-map-marker:focus-visible`; whether the global outline is visible on a round marker is unverified.

### Entry points

- Logged out: `authGuard` sends `/` to `/login?returnUrl=/` after `session.whenReady()` (`app/core/auth/auth.guards.ts:7-13,44-46`); login returns through `safeReturnUrl` (`app/features/auth/login-page/login-page.ts:59,99`, `app/core/auth/safe-return-url.ts:1-6`). No charge — this entry is correct.
- No position, slow API, unknown route: see C5, D1, D2.
- Map library failing to load: `map-view` shows its own error alert + retry under a grey 24rem box (`map-view.html:3-10`, `map-view.scss:5-11`); before Leaflet loads the box is empty with no loading indicator. Covered by C4's layout work only insofar as the page-level feedback and the map-level feedback must not both appear as two unrelated "Try again" blocks (C3).

### Kitchen sink (for the plan)

- Cells are `CellDefinition`s (`app/dev/kitchen-sink/cell-definitions.ts:9-14,143-146`) driven by `cell-driver.ts` steps (`press`, `fill`, `submitForm`, `waitFor`; no generic "click a selector" step, `cell-driver.ts:26-57`). `StateCell` hard-codes `<app-profile-page />` (`state-cell.html:10`, `state-cell.ts:34`), and `CellStubs` modes are Profile-specific (`cell-stubs.ts:13-19`), so map cells need a generalised cell (component per group) or a parallel map cell component.
- Stubs needed: `SightingsApi.nearby` (today only `mine`, `cell-stubs.ts:51-53`) with items / empty / failure / pending modes; `GeolocationService.currentPosition` with ok / denied / pending (`NEVER`) modes; existing `ConfigurationService` stub suffices (`radiusKm` falls back, `NM.ts:65-69`); `ResourceService` stays real.
- Map rendering: `MapViewStub` can only be swapped via `TestBed` (`app/shared/map/testing/map-view-stub.ts:5-21`), not at runtime. The runtime seam is the `LEAFLET_LOADER` token (`app/shared/map/map-view/map-view.ts:34-48`): a cell-level loader that wraps real Leaflet with a no-op `tileLayer` would give real, focusable markers without tile requests (untried), and a failing loader gives the map's own error state (as `map-view.spec.ts:140-170` does). Real tiles in each cell would hit `tile.openstreetmap.org` (`app/shared/map/map-config.ts:4`, `map-view.ts:132-134`) against the cost guardrail (`PigeonWatch/CLAUDE.md`, Notes for future work).
- Spec impact: `kitchen-sink.spec.ts:68` hard-codes 13 cells and `:72` asserts each cell contains `app-profile-page`; `kitchen-sink.html:3` titles the page "Kitchen sink: Profile".

## Code References

- `app/features/sightings/nearby-map-page/nearby-map-page.html:1-87` — the view
- `app/features/sightings/nearby-map-page/nearby-map-page.ts:53-151` — state, geolocation, search, selection
- `app/features/sightings/nearby-map-page/nearby-map-page.scss:1-37` — local classes
- `app/shared/map/map-view/map-view.ts:34-48,108-178` — Leaflet loader token, creation, markers
- `app/shared/map/map-view/map-view.html:3-10` — map load error + retry
- `app/shared/map/map-marker.ts:5-17` — marker type and class names
- `app/shared/map/map-config.ts:3-15` — tiles, neutral view, nearby zoom
- `styles/_tokens.scss:21-23,70,77` — criticality colours, content width, map height
- `styles/_common.scss:8-19,64-92` — `pw-stack`/`pw-cluster`, marker classes
- `styles/_material-theme.scss:53-59,84-90` — card theme, focus indicators
- `styles/style-token-coverage.spec.ts:23-39,80-157` — the guard's scope and rules
- `app/dev/kitchen-sink/state-cell.ts:34-65`, `cell-stubs.ts:13-85`, `cell-driver.ts:10-57` — kitchen sink internals
- `node_modules/leaflet/src/layer/marker/Marker.js:80-85,236-238`, `node_modules/leaflet/src/layer/Popup.js:501-504` — Leaflet keyboard behaviour

## Architecture Insights

- The contract's two halves already exist and the view reads them; the drift here is behavioural and structural (states, keyboard, layout), not literal values. This matches the profile-ui-contract pre-audit outcome.
- `shared/map/map-view` is the right home for C1: the defect and the missing selected state are in the shared component, so fixing it there also covers the detail page and the location picker. It must stay the only file that imports Leaflet (`Frontend/CLAUDE.md`, Maps section).
- New shared components from C2/C3 go in `shared/ui` and the barrel; the map page is a lazy feature, so barrel imports are fine. Nothing from this change may enter `core/` or `app.ts` imports (initial bundle 486.2 kB vs 500 kB budget, `Frontend/CLAUDE.md:28`).
- Any new UI text (legend, radius hint, location retry) needs keys in `PigeonWatch/Api/BusinessObjects/Resources/UiLabels.resx` (`Frontend/CLAUDE.md`, UI text section).
- Roadmap: S-05 `filter-sightings-nearby` adds status/criticality filters to this page (`context/foundation/roadmap.md:49,148-158`); S-06 adds an urgency list reusing those filters (`:50,161-171`). The C2 badge and C3 components are what those slices should consume, which argues for landing them before S-05.

## Historical Context (from prior changes)

- `context/archive/2026-10-11-report-sighting-on-map/plan.md:42-43` — full-width map layout and kitchen-sink cells for the map were explicitly deferred to a later `/10x-ui` (supported: both are still absent).
- `context/archive/2026-10-11-report-sighting-on-map/visual-notes.md:30-37` — open items V1 (no `invalidateSize`), V2 (map column 40rem vs 28rem cards), V3 (selected card cramped, "View details" vs "Close" weights), V6 (header wraps at 375 px). V1 → D5; V2/V3 feed C4; V6 is the shell header (D3 territory). V3's "two-column grid" wording is partial: the current SCSS is a wrapping flex row (`NM.scss:22-27`), unchanged since `3ba5058`.
- `context/archive/2026-10-11-report-sighting-on-map/plan.md:487-503` — accepted deviations: marker `title` carries the user-typed name; `map-view` gained `loadFailed` + retry and `mapReady`; 15 s geolocation backstop; Leaflet CSS not injected.
- `context/archive/2026-10-11-profile-ui-contract/` — phase order (kitchen sink + baseline → shared components → view → states, gate, guard), cells driven through the real DOM, literal-English labels allowed only in `dev/`, evidence in a `visual-notes.md` with "seen"/"measured" entries at the pane's desktop width and 375 px, keyboard pass with F/D findings; deferred D1–D3 carried over as D7–D9 above.
- `PigeonWatch/Frontend/CLAUDE.md:26` points at `context/changes/profile-ui-contract/visual-notes.md`, which now lives under `context/archive/2026-10-11-profile-ui-contract/` — the rule-file update in this change's guard phase should fix the path.

## Related Research

- `context/archive/2026-10-11-profile-ui-contract/research.md`
- `context/archive/2026-10-11-report-sighting-on-map/research.md`

## Open Questions

1. **Scope of C3 adoption** (product/scope choice for `/10x-plan`): promote the three shared components and adopt them only in the map view (detail and My sightings adopt later), or replace all copies in this change? The repo rule says "promote in the same change"; the `/10x-ui` rule says one view per change.
2. **Map width** (product choice): keep the map in the 40rem column (`app/app.scss:20-24`) or give this view a wider layout? Deferred twice already (report-sighting V2, `plan.md:42`).
3. **C5 behaviour** (product choice): block search at low zoom, or auto-zoom before searching; whether to draw the 10 km radius.
4. **Unverified in a browser** (to settle in the plan's baseline phase): C1 keyboard selection, C4 fold positions at 375 px, the focus outline on a marker, and whether a tile-less `LEAFLET_LOADER` works in a kitchen-sink cell.
