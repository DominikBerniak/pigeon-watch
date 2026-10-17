# Visual notes: nearby map review fixes

## Method

Captured 2026-10-17 on `/dev/kitchen-sink` from the `ng serve` already running on port 4200 (serves the working tree after Phase 2: commit `21ceaaf`), in the Claude browser pane with viewport emulation at 375×812, 768×1024 (`md`) and 1280×900 (`lg`). "Measured" means read from the DOM (`getBoundingClientRect`, computed styles, `document.activeElement`) in a fresh page load; Tab and Return were real key presses in the pane. Offsets are px from the top of `app-nearby-map-page`, as in the archived `visual-notes.md`. The map height is fixed (24rem), so a changed offset is a layout shift.

Limits: the sink cell is 34 px narrower than the real page at 375 (cell padding and border, `state-cell.scss`), so a 375 claim from the sink is conservative. No screen reader was run (see "Not verified").

## Map offsets

| Cell | 375 | `md` 768 | `lg` 1280 |
| --- | --- | --- | --- |
| Found (`map-loaded`) | 240 | 128 | 128 |
| Nearby error (`map-nearby-error`) | 240 | 128 | 128 |
| Location timed out (`map-use-my-location`) | 240 | 128 | 128 |
| Map failed, position unknown (`map-load-failed-unknown-position`) | 240 | 128 | 128 |
| Nearby error after a denied location (`map-nearby-error-after-denied`) | 300 | 146 | 128 |

Before the change (research, same sink): "Nearby error" 253 at 375 (+13) and 189 at `md` (+61); "Location timed out" 300 at 375 (+60). The archived `lg` figure for the nearby error was +61; it is now 0.

## Results against the Desired End State

- **F2 (Locate me wraps).** Fixed. After `denied`, `unavailable` or `timeout` the toolbar has "Search this area" (166 px) and "Locate me" (125 px) on one row at 375 (both at top 116), and the map stays at 240. At `md` and `lg` both sit on the same row as before.
- **F3 (nearby error adds a row).** Fixed for a known position. The error `app-alert` is inside `.nearby-toolbar__slot` (45 px high at 375), the slot is 48 px, and the map equals "Found" at 375, `md` and `lg`.
- **F4 (status over a failed map).** The two sink cells over a failed map show no result count and no "Zoom in to search an area"; Search is disabled. The sink runs the page over `MapViewStub`, so the unit spec of the page (emitting `loadFailed` from the stub) is the assertion; the cells are visual evidence only.

## Accepted residuals

- **Three buttons after a failed location and a nearby error** (`map-nearby-error-after-denied`: Search, Try again and Locate me). At 375 the third button wraps to a second row (Locate me at top 176) and the map starts at 300 (+60). At `md` the 200 px slot wraps the alert to 66 px (slot 66) and the map starts at 146 (+18). At `lg` the map starts at 128, no shift. This matches what the plan recorded; it is now reproducible in a cell.
- **Real-page width at 375** is derived (343 px), not rendered. The sink cell is 309 px, so the sink measurement is the harsher case for wrapping.

## Keyboard Tab pass

Real Tab presses, measured `document.activeElement` each step.

- **375, `map-nearby-error-after-denied`:** Search this area → Try again → Locate me → map canvas (region "Map of sightings") → Zoom in → Zoom out → attribution link. A visible ring on every stop: Material's `.mat-focus-indicator::before` 2 px solid primary on the three buttons (the button's own `outline` is `none`; seen in a screenshot on "Try again"), and a 2 px solid primary outline on the canvas, the zoom controls and the attribution link.
- **`md`, same cell:** the same order and the same rings.
- **`md`, `map-loaded` (Found):** Search this area → map canvas → markers (focus ring 2 px solid primary on each) → Zoom in → Zoom out. Return on a marker moves focus to the card heading (H2), then Tab → View details → Close. Return on Close returns focus to the marker.
- Order is sensible and follows the visual order at both widths.

## Overlap and clipping

Scanned the toolbar buttons and alert for overlap and the page for horizontal overflow: none in any of the 15 map cells at `md`, and none in "Location timed out", "Nearby error", the combined cell and the failed-map cell at 375 (the document has no horizontal scroll at 375). The only scan hits were Material's progress-spinner internals, which are expected clipping.

## Screen reader checklist (human step)

Run in a real screen reader (NVDA or VoiceOver) on `/dev/kitchen-sink` at 375 px and `md`:

1. "Found" cell: press Search; "Loading…" then the result count is announced once each.
2. "Nearby error" cell: the error text is announced once as an alert, not also as part of the status text.
3. "Nearby error after a denied location": focus order and announcements as in the cell above, with "Locate me" read as a button.
4. Select a marker (Return): focus lands on the card heading and the name is read; Close returns focus to the marker.
5. "Location timed out" cell with the next answer set to "Position found": press "Locate me"; the status changes are announced and focus lands where the page code puts it.
6. "Try again" in the error cell: the status returns to "Loading…" and the result is announced.

## Not verified

- Screen-reader announcements (checklist above).
- The map home against the local API with real data (the `authGuard` route needs login).
- The real page at 375 px (width derived as 343 px, not rendered).
- The Polish culture: only the neutral English `UiLabels.resx` exists today, and a longer label could wrap again.
