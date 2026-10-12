# Visual notes: nearby map UI contract

## Baseline

Captured 2026-10-11 on `/dev/kitchen-sink` (`ng serve` on port 4300, Phase 1 sink, production view unchanged), in the Claude browser pane with viewport emulation at 375×812, 768×1024 (`md`) and 1280×900 (`lg`). Screenshots of the pane timed out, so layout was measured from the DOM (`getBoundingClientRect`, computed styles). Offsets are in px from the top of `app-nearby-map-page`.

In the real app the page starts below the sticky header (`--pw-brand-mark-size-lg` + 2 × `--pw-space-3` + border ≈ 73px) and the `app-main` top margin (`--pw-space-6`, 24px). The page top is therefore about 97px from the viewport top, and at 375×812 the fold falls at about 715px of page offset. A phone's browser chrome lowers this further. The sink cell (310px wide at 375) is narrower than `app-main` in the app (343px). The map height is a fixed 24rem, so vertical offsets differ only where text wraps.

### Environment

- 21 cells render: 13 Profile cells (all ids unchanged, each renders `app-profile-page`) and 8 map cells.
- The tile-less loader works. Over the whole session there were 0 requests to `tile.openstreetmap.org` and 0 `img.leaflet-tile` elements. The two cells with markers draw 4 markers each, every one `role="button"` and `tabindex="0"`.
- No cell calls the API. The app shell's own startup probe (`/configuration/client`, `/resources/en` on `localhost:5285`) still fails with `ERR_CONNECTION_REFUSED` when the API is not running. This is shell behaviour that the Profile-only sink already had ("Waking up PigeonWatch" panel), not something a map cell does.
- Port 4200 was taken by an Azure Static Web Apps CLI emulator serving an older build, so the sink ran from a second `ng serve` on 4300.

### Cells

| Cell | 375 | `md` (768) | `lg` (1280) |
| --- | --- | --- | --- |
| Locating | Status line "Finding your location…" at 116–137, map 153–537, Search button 553–601 | status line above the map, map 101–485 | as `md` (page capped at 40rem, 575px inside the cell) |
| Position unknown | Hint alert 116–182 above the map, map 198–582, Search 598–646, no markers | hint 64–109, map 125–509 | as `md` |
| Loading | map 116–500, Search 516–564, spinner 580–612 below the button, no markers | map 64–448, spinner 528–560 | as `md` |
| Loaded with markers | 4 markers, map 116–500, nothing below the button | map 64–448 | as `md` |
| Selected | card 580–760 below the button | card 528–708 | card 528–708, full width under the map |
| Empty | "No open sightings within 10 km of this area." 580–628 | 528–552 | as `md` |
| Nearby error | error alert 580–625 plus retry button, page height 689 | alert 528–573 | as `md` |
| Map library failed | map-view's own alert and retry inside the map box (map box 116–646), Search below it | map box 64–573 | as `md` |

At 375 the h1 and Report button wrap onto two lines, so the map starts at 116 rather than 64.

### Answers to the research questions

1. **Does Enter or Space on a focused marker select it (C1)?** No. With the urgent marker focused in "Loaded with markers", a real Enter and then a real Space left the cell without a card. Focus stayed on the marker and the page did not scroll. C1 is confirmed.
2. **Is a focus outline visible on a marker?** Yes. When the marker is reached with Tab, `:focus-visible` matches and the computed outline is `solid 2px` in the primary colour (rgb 59 91 219) with a 2px offset, from the global focus rule. Phase 3 does not need `.pw-map-marker:focus-visible`. A programmatic `focus()` shows no outline, as expected.
3. **Where do the spinner, empty text and card sit relative to the fold at 375px (C4)?** All of them sit below the map and the Search button, starting at page offset 580 (about 677px from the viewport top in the app). Against an 812px viewport, the spinner (to 612), the empty text (to 628) and the error alert (to 625) end just above the fold. The retry button ends at 689, near the fold. The selected card (580–760) runs past the fold by about 45px. On a real phone, with browser chrome, all of these sit at or below the bottom edge, and nothing moves them into view. C4 is confirmed.
4. **Does the tile-less loader render focusable markers with no tile requests?** Yes, see Environment. No fallback to real tiles is needed.

### Other baseline observations

- The map jumps when geolocation resolves. At 375 the map top moves from 153 (locating line) to 116 on success (37px up), and to 198 when the location is denied (45px down, because the hint alert is taller than the locating line). At `md` the moves are 37px up and 24px down. This confirms the C4 "map jumps" claim.
- In the "Loading" cell the markers are gone while the request runs (0 markers). This confirms that entering `loading` empties the map.
- Marker accessible names are the sighting name only. Both "Limpy" markers read "Limpy" with no criticality, and no marker has `aria-pressed` (C1).
- `md` and `lg` are identical: the content is capped at `--pw-content-max-width` (40rem), and at `lg` the card sits under the map with empty space on both sides.

## After (Phase 5)

State of the code: commit `ea06fbc` (Phases 1-4) plus the Phase 5 working tree (sink cells, stubs and driver, the spinner check in `style-token-coverage.spec.ts`, the rule in `Frontend/CLAUDE.md`). No production view file changed in this phase.

Captured 2026-10-11 on `/dev/kitchen-sink` from the `ng serve` already running on port 4200 (it serves the working tree with hot reload), in the Claude browser pane with viewport emulation at 375×812, 768×1024 (`md`) and 1280×900 (`lg`). "Measured" means read from the DOM (`getBoundingClientRect`, computed styles, `document.activeElement`); "seen" means looked at in a screenshot. Offsets are in px from the top of `app-nearby-map-page`, as in the Baseline. Method limits:

- The pane was hidden for the whole session. Screenshots worked but the first capture after a scroll was sometimes stale (taken again after a short wait). In a hidden pane `requestAnimationFrame` never fires, which is why the sink's map has no zoom animation (see Environment).
- Real key presses (Tab, Shift+Tab, Return, Space) and a real mouse hover were used for the keyboard pass and the hover state, at `lg` only.

### Environment

- 27 cells: 14 Profile cells (13 unchanged ids plus `read-only-my-sightings`) and 13 map cells.
- 0 requests to `tile.openstreetmap.org` and 0 `img.leaflet-tile` elements over the session (measured), so the tile-less loader still holds.
- The sink route now declares `data: { layout: 'wide' }` (dev-only), so map cells render at the map home's width: the map page is 959 px wide at `lg` inside a 1024 px `main` (the cell's padding and border take the rest), 687 px at `md`, 310 px at 375. The Profile group is capped at `--pw-content-max-width` (measured 640 px), so Profile cells keep the width they had in the Profile notes.
- The tile-less loader also turns off Leaflet's zoom animation (dev-only, `cell-stubs.ts`). Without it the "zoom to level 10" step never completed in the hidden pane, because Leaflet starts an animated zoom from `requestAnimationFrame`.
- The two selected cells are reached from a header button ("Click a marker", "Press Enter on a marker") instead of on load, because selecting moves focus to the card heading. Measured after a reload: `scrollY` 0 and focus on `body`, so no cell takes focus or scrolls the sink on load.
- The "Use my location" cells have a "Next "Use my location" answer" select in the cell header (Position found, Timed out, Never answers). The first location request always answers `timeout`.

### Cells

| Cell | 375 (measured) | `md` 768 (measured) | `lg` 1280 (measured) |
| --- | --- | --- | --- |
| Locating | Report wraps under the h1; toolbar 116-224 (Search 116-164, status "Finding your location…" 176-224); map 240-624; aside 640-734 | toolbar one row 64-112 (status beside Search, 510 px wide); map 128-512, 687 wide; aside under the map 528-622 | toolbar 64-112; map 128-512, 655 wide; aside beside the map at x 824, 288 wide, 128-258 |
| Position unknown at country zoom (denied) | Search and Use my location on two rows (116-164, 176-224); status "Zoom in to search an area" 236-284; map 300-684; heading "Sightings in this area" | one row; status beside the two buttons (331 px wide); map 128-512. Seen. | as `md`, map 128-512, aside beside |
| Position unknown, zoom 10 (new) | as above; status shows the hint in 2 lines inside the 48 px slot; Search enabled. Seen. | as above, hint in the slot. Seen. | as above |
| Location timed out, "Use my location" (new) | as "country zoom" | as "country zoom" | as "country zoom"; with "Position found" the next press recentres, heading "Sightings near you", status "4 open sightings within 10 km", 4 markers, button gone |
| Retrying location (new) | as "country zoom"; status "Finding your location…"; Use my location `disabled` | as above | as above |
| Loading, first search | status "Loading…" with the inline spinner 176-224; map 240-624; no markers | map 128-512 | map 128-512 |
| Loading again, markers kept (new) | as first search, 4 markers stay on the map, Search `aria-disabled="true"`. Seen. | as above | as above |
| Found (default) | status "4 open sightings within 10 km"; map 240-624; aside 640-734 with the legend (three badges) and "Select a marker to see details" (the same aside seen in "Loading again") | aside under the map | legend and prompt beside the map. Seen. |
| Selected by pointer | after "Click a marker": card in the aside at 710-891, scrolled into the viewport (352-533 of 812), focus on the card heading; selected marker larger with a primary border, `aria-pressed="true"`. Seen. | not opened | not opened |
| Selected with the keyboard (new) | not opened | not opened | after "Press Enter on a marker": card in the aside beside the map, focus on its heading, "Limpy, Urgent" selected |
| Empty | status "No open sightings within 10 km of this area." 176-224; map 240-624 | map 128-512 | map 128-512 |
| Nearby error | Revised after manual review: Try again in the toolbar row beside Search this area (same row at 375 and `lg`); the error `alert` on its own one-line row under the toolbar (45 px); status element kept, visually hidden. Seen at 375 and `lg`. | as `lg` | Search and Try again both at the toolbar row; alert 45 px under the 48 px toolbar; map 61 px lower than in found |
| Map library failed | map box 240-624; the map's own error and Try again under it 640-770; aside 786-880; status "4 open sightings within 10 km" | error 528-637 under the map; aside 653-747 | error under the map 528-637; aside beside |
| Profile, My sightings with items (new) | not measured | not measured | 4 items, each meta line "Spotted · ● Urgent · date" with one `app-criticality-badge`, 22 px high (one line). Seen. |

No horizontal scroll at any width (document width equals the viewport minus the scrollbar).

### The 7-state matrix

| State | Nearby map view |
| --- | --- |
| default | Shown: "Found" cell, built from `shared/ui` components and tokens: toolbar with Search and the status count, map, aside with the legend (`app-criticality-badge` × 3) and the select prompt. Seen at `lg` (and the same aside at 375 in "Loading again"), measured at all three widths. |
| hover | Shown at `lg` with the real mouse: Search this area gets Material's state layer (opacity 0.08 of the primary colour, measured). Markers carry `leaflet-interactive` and `cursor: pointer` (measured). 375 and `md`: N/A, a touch viewport has no hover, and the hover rules have no breakpoint. |
| focus-visible | Shown at `lg` with real Tab presses: Report, Search (enabled and `aria-disabled`), Use my location and the card's Close button show Material's 2 px ring (`::before` `display: block`, measured); the map region, every marker, the zoom controls and the attribution link show the global 2 px solid outline; the card heading shows the 2 px outline in the focus colour after a keyboard selection (seen). 375 and `md` were not tabbed again: none of these focus styles depends on a breakpoint. |
| disabled | Shown: Search below zoom 10 ("country zoom", "timed out", "retrying" cells) and while a search loads ("Loading" cells); it stays focusable with `aria-disabled="true"`, `aria-describedby` on the status text, label and border at 38 % and 12 % ink (measured). Use my location while locating ("Retrying location" cell): native `disabled`, out of the tab order. |
| error | Shown: "Nearby error" (text in an `alert` on its own row under the toolbar, Try again beside Search this area; markers cleared) and "Map library failed" (the map's own `alert` plus Try again under the map box). Text, not colour alone. Not shown in a cell: an error while zoomed out below 10, where the zoom reason takes precedence over the error (Phase 4 rule). |
| empty | Shown: "Empty" cell, "No open sightings within 10 km of this area." names the radius. A position-unknown page before any search is not empty: it shows the zoom reason or the hint. |
| loading | Shown: "Loading, first search" and "Loading again" (inline 20 px spinner with "Loading…" in the status slot, Search disabled, markers kept on a repeated search). No layout jump: the slot has a 48 px minimum height and the map top stays at 240 (375) and 128 (`md`, `lg`) across locating, loading, found and empty. The jumps that remain are F2 and F3 below. |

Also shown, outside the seven states: selected by pointer and by keyboard (card, focus, selected marker), and My sightings with criticality badges on the Profile page.

### State-by-state delta against the Baseline

Labels: **intended** = the plan asked for it; **fixed** = a baseline finding that is now gone; **unchanged**; **deferred** = recorded, not changed.

| Cell | Baseline | After | Label |
| --- | --- | --- | --- |
| Locating | "Finding your location…" line above the map at 116-137 (375), map 153 | Status in the toolbar slot, map at 240 (375), 128 (`md`, `lg`) | intended (C4) |
| Map jump when the position is found | 37 px up at 375 and `md` | 0: map at 240 / 128 while locating and after success | fixed (C4) |
| Map jump when the position is denied | 45 px down at 375, 24 px at `md` | 60 px down at 375 (Use my location wraps to a second row), 0 at `md` and `lg` | fixed at `md`/`lg`; 375 deferred (F2) |
| Position unknown | Hint alert above the map; Search enabled at zoom 6 and searched 10 km around central Poland; heading "Sightings near you" | Heading "Sightings in this area"; Search disabled with "Zoom in to search an area"; Use my location offered; hint once zoomed to 10 | intended (C5) |
| Loading | Spinner below Search at 580-612 (375), markers cleared | Inline indicator in the toolbar at 176-224, above the map; markers kept on a repeated search; Search disabled | fixed (C4) |
| Loaded with markers | Nothing below the button, no count, no legend | Count in the status, legend and prompt in the aside | intended (C2, C4) |
| Selected | Card at 580-760 under the button, past the fold at 375; no focus move; no selected marker | Card in the aside, scrolled into view with focus on its heading; at `lg` beside the map; selected marker larger with a primary border and `aria-pressed`; criticality as a badge | fixed (C4, C1); badge intended (C2) |
| Keyboard selection | Enter and Space on a focused marker did nothing | Both select (real keys); Space does not scroll | fixed (C1) |
| Marker names | "Limpy" twice, no criticality | "Limpy, Urgent", "Limpy, Needs help soon", "Fluffy, Monitor", "Unnamed bird, Monitor" | fixed (C1) |
| Empty | Text at 580-628 below the button | Text in the status slot at 176-224 | fixed (C4) |
| Nearby error | Alert and full-width retry at 580-689 | Try again beside Search in the toolbar, one-line error alert under it; the map moves 61 px down | intended (C3, C4; layout revised in manual review); jump deferred (F3) |
| Map library failed | Alert and retry inside the map box | The map's `app-error-retry` under the canvas, same shape as the page's | intended (C3); status count with no map deferred (F4) |
| `md` vs `lg` | Identical, 40rem column, empty space on both sides at `lg` | `md`: 64rem column, aside under the map; `lg`: aside beside the map | intended (wide layout) |
| Profile cells | 13 cells at 40rem | Same 13 cells at 40rem, plus My sightings with items showing a badge per meta line | intended (C2) |

### Keyboard and accessible-name pass

Real Tab, Shift+Tab, Return and Space presses at `lg` (1280×900); `document.activeElement`, `:focus-visible` and computed styles read after each press.

Tab order in "Found", from the cell heading: Report sighting → Search this area → the map region "Map of sightings" (Leaflet's container, `tabindex="0"`) → the four markers in fixture order (Limpy, Urgent; Limpy, Needs help soon; Fluffy, Monitor; Unnamed bird, Monitor) → Zoom in → Zoom out → the attribution link → the next cell. The aside has no focusable element while nothing is selected. With a card open: card heading (focused by the page, `tabindex="-1"`) → View details → Close. In the zoomed-out cell: Report → Search (disabled but focusable) → Use my location. This follows the visual order of the plan (Report, Search, Use my location, map controls and markers, aside, card actions). Within the map, markers come before the zoom controls and follow fixture order rather than screen position; that is Leaflet's DOM order (panes before the control corner) and was not changed.

Accessible names (read from the DOM and the accessibility tree): every control and marker has one. Markers are `button`s named "name, criticality" with `aria-pressed`; the map is a `region` named "Map of sightings"; zoom controls "Zoom in" / "Zoom out"; buttons and links by their text; the status line is `role="status"`; both errors are `role="alert"`. The sink's own select is named by its wrapping label (dev only).

Selection: Enter on the focused "Limpy, Needs help soon" opened its card, moved focus to the heading (`:focus-visible` true, 2 px solid outline in the focus colour) and set `aria-pressed="true"` on that marker only. Tab, Tab, Return on Close removed the card, restored the prompt, cleared every `aria-pressed`, and put focus back on "Limpy, Needs help soon" with a visible outline. Space on that marker opened the card again without moving `scrollY`.

Findings:

- **F1. "Use my location" drops keyboard focus to `body` (deferred).** With the next answer set to "Never answers", Return on the focused button left `document.activeElement` on `body`: the button turns natively `disabled` while locating. With "Position found", the button is removed after success and focus is again on `body` (both measured). A synchronous failure keeps focus on the button. A keyboard or screen-reader user loses their place right after the one action the page offers them. Not fixed here: Phase 4's contract says the button is natively disabled while a request is in flight and absent after success, so the fix is a behaviour decision. Options: `disabledInteractive` with `aria-disabled` while locating, and on success move focus to Search this area (or the h1). Triage in `/10x-impl-review`.
- **F2. At 375 a failed geolocation moves the map 60 px down (deferred).** Use my location appears after `denied` or `timeout` and wraps to a second toolbar row at 310 px. `md` and `lg` do not move. Fixing it needs a layout choice (both buttons on one row, or space reserved for the second button), not a contract change.
- **F3. The nearby error moves the map 61 px down at every width (deferred).** The one-line error alert (45 px) takes its own row under the toolbar; Try again sits in the toolbar row, so the toolbar keeps its 48 px. It follows a user action (a search) and announces itself as an alert, so the shift is visible and explained; recorded, not changed.
- **F4. With the map library failed, the status still reports the search result (deferred).** The page searches on a known position whatever the map does, so "4 open sightings within 10 km" sits above a map that shows only its load error. The page has no signal from `app-map-view` about load failure; low impact, since the error and Try again are next to it.
- Focus on the card heading after a pointer selection shows no outline (`:focus-visible` false after a mouse click). That is the browser's focus-visible heuristic, not a defect.

Not verified: the Tab pass at 375 and `md` (focus styles have no breakpoint, but the toolbar wraps at 375); screen-reader announcements of the status text; the page against the local API.
