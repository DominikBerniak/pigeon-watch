---
date: 2026-10-17T19:51:08+02:00
researcher: Claude (Sonnet 5.5) for Dominik Berniak
git_commit: b7b589e0be281d90b28ac8ea797318e4ccb9b20b
branch: feat/nearby-map-ui-contract
repository: 10xDevs (PigeonWatch)
topic: "Map home toolbar layout shifts (F2, F3) and status over a failed map (F4)"
tags: [research, ui, nearby-map-page, map-view, toolbar, layout-shift, kitchen-sink]
status: partial
last_updated: 2026-10-17
last_updated_by: Claude (Sonnet 5.5)
---

# Research: Map home toolbar layout shifts (F2, F3) and status over a failed map (F4)

**Date**: 2026-10-17T19:51:08+02:00
**Researcher**: Claude (Sonnet 5.5) for Dominik Berniak
**Git Commit**: b7b589e0be281d90b28ac8ea797318e4ccb9b20b (the code under `PigeonWatch/` is byte-identical to squash commit `f0a9c8e` on `main`; see the archived `reviews/archive-sha-repoint.md`)
**Branch**: feat/nearby-map-ui-contract
**Repository**: 10xDevs (PigeonWatch)

Paths are relative to `PigeonWatch/Frontend/src/` unless they start with `context/`. `NM` = `app/features/sightings/nearby-map-page/nearby-map-page`; `MV` = `app/shared/map/map-view/map-view`. "Sink" = the dev-only kitchen sink at `/dev/kitchen-sink`.

## Research Question

From `change.md` (carried from the archived `nearby-map-ui-contract`, `follow-ups/review-fixes.md`): for the map home (`/`) and `app-map-view`, (F2) why the map moves down at 375px after a failed location and which fix options fit; (F3) why the map moves down on a nearby error and which fix options fit; (F4) how the status can report results over a map that failed to load and what signal the page needs. Plus which of the three unrun checks (Tab pass at 375/`md`, screen-reader announcements, the page against the local API) this research could settle.

## Summary

- **All three defects are reproduced in the sink and traced to code.** The fixes touch `NM.html`, `NM.scss`, `NM.ts`, `MV.ts` and `map-view-stub.ts`, plus specs, sink cells and one rule-file bullet. None of the options needs a new token or a new `shared/ui` component; F2's shorter-label option needs one new resx key.
- **F3's size in the archived notes is wrong at 375.** Measured in the sink, the nearby error moves the map **+13 px at 375** (offset 253 vs 240 in "Found"), **+61 px at `md`** (189 vs 128, re-measured now); the archived `lg` figure of 61 was not re-measured. At 375 the toolbar shrinks from 108 to 60 px (the visually hidden status adds 0 height) while the alert row adds 61 (45 + 16 gap).
- **F3 has a zero-shift fix, prototyped by moving DOM nodes in the sink.** With the error `app-alert` placed inside `.nearby-toolbar__slot` and the slot given `min-height: 3rem`, the map offset equals "Found" at 375 (240 vs 240) and at `md` (128 vs 128). It breaks down only when three toolbar buttons are present together (error after a failed location): then the 200 px slot wraps the alert to 66 px and the map moves +18 px at `md`; at 375 the third button wraps to a second row (offset 300, i.e. F2 again).
- **F2's width is overstated by the sink.** The sink cell is 34 px narrower than the real page (cell padding and border, `app/dev/kitchen-sink/state-cell.scss:9-11`; the real `main` has 16 px side padding, `app/app.scss:22-26`). At a 375 px viewport the real toolbar content width is derived as 343 px, and Search (166) + gap (12) + Use my location (168) = 346 px, so it wraps by **3 px**. The two buttons fit on one row from a viewport of about 378 px upward (derived), so 360 and 375 wrap and 390 does not. This has not been seen in the real app.
- **F4 needs one new `app-map-view` output and a page rule.** `MV` keeps the failure in a protected signal (`MV.ts:104`) that no consumer can read; the other two `app-map-view` consumers (`location-picker.html:5`, `sighting-detail-page.html:66`) ignore it. The page's `status()` computed has no input from the map (`NM.ts:136-148`). Two further status texts also sit next to a failed map and cannot be acted on (`zoomToSearch` and `hint`).
- **Not settled:** the Tab pass at 375/`md` and screen-reader announcements (need real assistive technology), and the page against the local API (the `authGuard` route needs login; the API was not run). `status: partial` for that reason; the three defects themselves are answered.

## Detailed Findings

### F2 — "Use my location" wraps to a second toolbar row

- **Mechanism (observed).** The actions container is `.pw-cluster` (`display: flex; flex-wrap: wrap; gap: var(--pw-space-3)`, `styles/_common.scss:14-19`) with `flex: none` (`NM.scss:18-20`), so it never shrinks and the third child wraps when the row is too narrow. "Use my location" is rendered only when `canRetryLocation()` is true (`NM.html:24-34`), i.e. the last location outcome is `denied`, `unavailable` or `timeout` (`NM.ts:56-60,131-135`). Below `md` the toolbar is a column (`NM.scss:7-16`), so the extra row adds to the toolbar height.
- **Measured in the sink at 375 (cell content 309 px).** Search 166 px, Use my location 168 px, gap 12 px (346 px total > 309), toolbar 168 px vs 108 px, map offset 300 vs 240 (+60). At `md` the same cells give toolbar 48 px and map offset 128 (no move); the two buttons are 345 px wide and the slot beside them is 330 px.
- **Real-page width (derived, not rendered).** The sink cell adds 16 px padding and a 1 px border per side (34 px, `app/dev/kitchen-sink/state-cell.scss:9-11`) that the real page does not have, and the real `.app-main` has 16 px side padding (`app/app.scss:22-26`). So the real content width at a 375 px viewport is 309 + 34 = 343 px, which equals 375 − 2×16. The archived notes' "wraps at 310 px" describes the sink, not the page.
- **When it appears.** The button appears after the geolocation outcome, which can be up to 10 s after load (`timeout` is "10 s, always set", `Frontend/CLAUDE.md`, geolocation bullet), so on a wrapping viewport the map moves after the user may already have started panning.
- **Option fit (computed from canvas text widths at the button font, 600 16px system-ui; padding 24+24, border 1+1; not rendered).** Text widths: "Search this area" 116, "Use my location" 118, "My location" 87, "Locate me" 75, "Near me" 63.

  | Option | Needs | Fits on one row at | Notes |
  |---|---|---|---|
  | Shorter label for the map home only ("My location") | new resx key; `map.useMyLocation` is shared with the picker (`location-picker.html:17`) | the real 343 px (166 + 12 + 137 = 315), not the sink's 309 | "Locate me" (303) and "Near me" (291) also fit 309. A longer label in a new culture would break it again (only the neutral `UiLabels.resx` exists today). |
  | Icon button with accessible name | an inline SVG (no icon font is allowed by the CSP rule; the only `<svg>` in `src/` today is in `shared/ui/brand/brand.html`) | 166 + 12 + 48 = 226 px, i.e. from a viewport of about 258 px | New pattern in the repo (no `mat-icon` anywhere). |
  | Text-variant (`mat-button`) for the secondary action | style change | 140 + 12 + 142 = 294 px if both buttons are text-variant (derived: padding 12 instead of 24, no border), so it fits 309 too | Changes the stroked-button look of the toolbar. |
  | Reserve the second row from first render | none | always | The map would start 60 px lower on every 375 load until location resolves, including for users whose location succeeds. Worse than the defect for the common path. |

### F3 — The nearby error adds a row

- **Mechanism (observed).** In the error status the `role="status"` element is visually hidden (`NM.html:41`, `position: absolute`, slot height measured 0), so the toolbar loses the slot row; the `app-alert` is a separate sibling after the toolbar (`NM.html:70-72`) and adds its own row inside the `.pw-stack` gap. `.nearby-status` has `min-height: var(--pw-control-height)` = 3rem (`NM.scss:29-33`; `styles/_tokens.scss:75`); the alert is 45 px high (padding 12+12, line 21 px; `shared/ui/alert/alert.scss:1-8`) and its text is 245 px wide (281 px with padding and border).
- **Measured in the sink (map offset from the page top; "Found" is 240 at 375 and 128 at `md`).**

  | Width | Found | Nearby error (current) | Prototype: alert in slot, `min-height: 3rem` | Prototype plus a third button |
  |---|---|---|---|---|
  | 375 (cell 309 px) | 240 | 253 (+13) | 240 (0) | 300 (+60: the third button wraps) |
  | `md` 768 | 128 | 189 (+61) | 128 (0) | 146 (+18: slot 200 px, alert wraps to 66 px) |

  `lg` was not re-measured; the archived notes give +61 there.
- **Why the archived "+61 at every width" is partial.** It holds at `md` and (per the archived measurement) at `lg`, where the toolbar is one row; at 375 the toolbar's own shrink offsets most of it.
- **Prototype caveat.** The prototype moved DOM nodes by script and added a clone of the existing button; no source was changed and Angular change detection was not exercised, so the screen-reader behaviour of an alert inside the slot is unverified.
- **Constraints the fix must keep.** (1) Try again stays in the toolbar beside Search (decided in the archived plan addendum; asserted at `NM.spec.ts:399-401`). (2) The status element exists from first render and only changes text (`NM.spec.ts:222-236`, one `[role="status"]` in the page). An alert placed inside the slot as a sibling of the status element keeps that; nesting `role="alert"` inside `role="status"` would not. (3) The spec at `NM.spec.ts:394-398` asserts `toolbar.nextElementSibling` is the alert, so it changes with option A. (4) The "Spinners, errors with retry…" bullet in `Frontend/CLAUDE.md` states the "error `app-alert` on its own row under the toolbar" exception and must be edited.
- **Options.**
  - A. Alert inside the slot (prototyped): zero shift in the common case; residual +18 px at `md` when the error follows a failed location. A `min-height` on `.nearby-toolbar__slot` using `--pw-control-height` is needed so a 45 px alert does not shrink the row to 45 (derived: toolbar 105 vs 108).
  - B. Reserve the alert row from first render: no shift, but adds 61 px to every load. Not recommended.
  - C. Accept and keep as is: +13 px at 375, +61 px at `md`.

### F4 — Status over a map that failed to load

- **Mechanism (observed in code).** `MV.load()` clears then sets `loadFailed` (`MV.ts:124-136`); the signal is `protected` and is read only by the template (`MV.html:3-7`). The component's outputs are `mapClick`, `centerChange`, `zoomChange`, `markerSelect`, `pinMove`, `mapReady` (`MV.ts:97-102`); there is no failure output, and `mapReady` fires only on success (`MV.ts:163`). The page searches whenever a position is known (`NM.ts:257-264`), and `status()` depends only on `locating`, `zoomedOut`, `positionUnknown`, `awaitingSearch` and the nearby `state` (`NM.ts:136-148`), so with a known position and a failed map the status is `found`/`empty` ("N open sightings within 10 km"; recorded for the sink cell "Map library failed to load" in the archived `visual-notes.md`, not re-read here).
- **Consumers of `app-map-view`** (inspected: all `*.html` under `src/`): the map home, `location-picker.html:5` (uses `mapReady` to enable its button, `location-picker.html:13,22`) and `sighting-detail-page.html:66`. An added output is additive for the two others. `map-view-stub.ts:12-26` mirrors the outputs and must gain the same one (repo rule: "same selector, inputs and outputs", `Frontend/CLAUDE.md`, Stub the map).
- **Naming constraint.** A component output cannot share a name with the existing `loadFailed` signal member, so the signal is renamed or the output is named differently (for example one boolean output emitted on each state change, true on failure and false when a retry starts or the map is created).
- **Page rule (a choice).** The minimum for F4 is to drop `found` and `empty` while the map is failed. Two other statuses are also unactionable next to a failed map: `zoomToSearch` ("Zoom in to search an area") and `hint` ("Move the map…"), because with an unknown position the map never emits `zoomChange` and the zoom stays at 6 (`NM.ts:97`, initial `neutralMapView.zoom`), so Search stays disabled with that reason. `locating` and `loading` remain true statements. On recovery the markers return on their own because `applyMarkers` reads `ready()` (`MV.ts:175-177`) and `sightings()` was filled in the background.
- **Test surface.** `map-view.spec.ts:284-313` (fail then retry) extends to assert the output; the page spec needs a case that emits it from the stub; the sink cell `map-load-failed` (`cell-definitions.ts:308-316`) already shows the state and needs a marker assertion update in `kitchen-sink.spec.ts`.

### Sink coverage gap

No sink cell combines a failed location with a nearby error or a failed map (`cell-definitions.ts:192-317` lists 13 map cells; the retry-location cells use `nearby: 'items'`). The three-button case, the +18 px residual and the F4 `zoomToSearch` combination can only be reproduced by hand-built DOM today. A plan that fixes F2/F3 should add such a cell.

## Code References

- `app/features/sightings/nearby-map-page/nearby-map-page.html:6-69` — toolbar: actions (`:7-35`), status slot (`:36-68`), hidden status in error (`:41`)
- `app/features/sightings/nearby-map-page/nearby-map-page.html:70-72` — the error `app-alert` as a sibling after the toolbar
- `app/features/sightings/nearby-map-page/nearby-map-page.scss:7-33` — toolbar column/row, slot, status `min-height`
- `app/features/sightings/nearby-map-page/nearby-map-page.ts:56-60,131-148,244-267` — retryable outcomes, `canRetryLocation`, `status()`, `applyLocation`
- `app/shared/map/map-view/map-view.ts:97-102,104,124-136,163` — outputs, protected `loadFailed`, `load()`, `mapReady`
- `app/shared/map/map-view/map-view.html:3-7` — the map's own error and retry
- `app/shared/map/testing/map-view-stub.ts:12-26` — stub outputs
- `app/shared/ui/alert/alert.scss:1-8`, `styles/_common.scss:14-19`, `styles/_tokens.scss:75` — alert box, cluster, control height
- `app/app.scss:22-26`, `app/dev/kitchen-sink/state-cell.scss:9-11` — real vs sink widths
- `app/features/sightings/nearby-map-page/nearby-map-page.spec.ts:222-236,388-416` — specs that pin the current layout
- `app/dev/kitchen-sink/cell-definitions.ts:299-316` — nearby error and map-failed cells

## Architecture Insights

- The status slot is the page's single place for feedback beside the toolbar and has a deliberate 3rem floor (`NM.scss:29-33`); F3 option A extends that rule to the error instead of adding a second place.
- The map home keeps its own error `app-alert` rather than `app-error-retry` because Try again lives in the toolbar (`Frontend/CLAUDE.md`, "one exception"); moving only the alert keeps that exception valid.
- The sink under-reports the real page's width at 375 by 34 px, so any 375-px claim from it about wrapping is conservative; a plan should either widen the sink cell for this check or measure the real page.
- `app-map-view` is the only importer of Leaflet; an output added there stays inside the existing boundary.

## Historical Context (from prior changes)

- `context/archive/2026-10-11-nearby-map-ui-contract/visual-notes.md` F2 "60 px down at 375 … wraps to a second row at 310 px" — partial: 60 px reproduced in the sink; the 310 px is the sink cell width, not the page.
- Same file, F3 "map moves 61 px down at every width" — partial: contradicted at 375 in the sink (+13), supported at `md` (+61 re-measured), `lg` not re-measured.
- Same file, F4 "the page has no signal from `app-map-view` about load failure" — supported (`MV.ts:97-104`).
- Same file, "Not verified: Tab pass at 375 and `md`; screen-reader announcements; the page against the local API" — still open.
- `context/archive/2026-10-11-nearby-map-ui-contract/reviews/impl-review.md` F6 — origin of the queue; accepted deviation "Try again sits in the toolbar" (plan addendum).
- `context/archive/2026-10-11-nearby-map-ui-contract/follow-ups/review-fixes.md` — the three defects and options; its note "Where to do it" is what created this change.

## Related Research

- `context/archive/2026-10-11-nearby-map-ui-contract/research.md` (C4 layout, C5 no-position behaviour)
- `context/archive/2026-10-11-report-sighting-on-map/research.md`

## Open Questions

1. **F2 fix** (choice for `/10x-plan`): shorter map-home label (needs a new resx key), icon button (needs an inline SVG pattern), text-variant secondary button, or accept the 3 px wrap on viewports below about 378 px. Not decidable from code; depends on how much the 60 px matters on 360/375 phones.
2. **F3 fix**: option A (alert in the slot) with its +18 px residual after a failed location, or accept it; and whether the rule-file exception text changes.
3. **F4 status rule**: suppress only `found`/`empty`, or also `zoomToSearch` and `hint`, while the map is failed.
4. **Unverified**: Tab pass at 375/`md`; screen-reader announcements of the status text, the alert (including the in-slot alert) and the focus moves; the page against the local API with real data; the real-page width of the toolbar at 375 (derived 343 px, not rendered); the `lg` figure for F3.
