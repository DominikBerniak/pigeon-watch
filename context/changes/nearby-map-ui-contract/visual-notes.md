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
