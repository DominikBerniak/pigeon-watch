# Visual notes: S-03 sighting views

Captured on 2026-10-11 in the built-in browser pane, against `ng serve` (development build) and the API on LocalDB, logged in as the developer account. The code is commit `ccf30ff` (Phases 1-3) plus the Phase 4 working tree.

How to read the screenshots:

- "Desktop" is the pane's own width, about 800 px. The layout is capped at the 40rem `.app-main` column, so a 1280 px window shows the same layout.
- "375" is the pane's 375x812 mobile preset.
- The pane denies geolocation. Every "no position" capture is therefore the real denied path.
- The "with pins" captures use a script stub of `navigator.geolocation` that returns a point in Kraków's Old Town, installed before an in-app navigation to `/`. This is a test aid only and does not change the page.

Screenshots are in `screenshots/`.

## Per-view evidence

| View | Desktop | 375 px | Seen / measured |
| --- | --- | --- | --- |
| Map home, no position | `map-no-position-desktop.jpg` | `map-no-position-375.jpg` | Hint alert above the map, neutral Poland view at zoom 6, "Search this area" below the map, no query until it is pressed. |
| Map home, with pins | `map-pins-desktop.jpg` | `map-pins-375.jpg` | Centred on the position at zoom 12. Four `pw-map-marker` divIcons in three criticality colours, counted from the DOM. No popups. |
| Map home, marker selected | `map-selected-desktop.jpg` | not captured | An Angular card below the map shows the name, status, criticality, "View details" and "Close". |
| Report form, pristine | `report-desktop.jpg` | not captured separately | Picker centred on the position, with no pin until the user acts. Hints are under every field. |
| Report form, empty submit | `report-errors-desktop.jpg` | `report-errors-375-top.jpg`, `report-errors-375-bottom.jpg` | Required errors on description, behaviour, status, criticality, and "Choose a location on the map." on the picker. No horizontal scroll at 375: the document width measured 375. At 375 the actions stack with "Report sighting" first, as the shared `app-form-actions` rule says. |
| Detail | `detail-desktop.jpg` | `detail-375.jpg` | Name, status, criticality, description, behaviour, reporter display name, reported-at date and coordinates as text. Zero `tile.openstreetmap.org` requests before "Show map", read from the Resource Timing entries. |
| Detail, map shown | `detail-map-desktop.jpg` | `detail-map-375.jpg` | One marker at the sighting, 9 tile requests after the toggle, and the button reads "Hide map". |
| Detail, not found | not captured | `detail-not-found-375.jpg` | Title "Sighting" and the not-found alert. The back link is still present. |
| Profile, My sightings | seen, not kept | seen, not kept | The list sits below the profile card, with a name link to the detail page and the status, criticality and date line. The images were taken but left out of the repo because they show the account email. |

Not captured as screenshots: the loading, empty and error states of the map home and My sightings. They are covered by the view specs and were confirmed manually by the user (Progress 4.6 and 4.8).

## Left for a later `/10x-ui` pass

- **V1. The map does not recalculate its size after a viewport resize.** After switching the pane from desktop to 375 on the report form, the lower part of the picker map stayed grey (`report-errors-375-bottom.jpg`). Leaflet only reads the container size on creation, and `MapView` never calls `invalidateSize()` on resize. On a phone this shows up on rotation. The fix belongs in `shared/map/map-view` (a `ResizeObserver` calling `invalidateSize`), not in the views.
- **V2. Column widths do not match.** The profile card and the detail card are capped at 28rem. The My sightings card, the map page and the detail page's "Back to map" link use the full 40rem column, so on desktop the My sightings card is wider than the profile card above it, and the back link sits left of the detail card's edge.
- **V3. The selected-marker card is cramped.** The status and criticality labels sit in a tight two-column grid, and "View details" and "Close" have very different weights.
- **V4. The My sightings title sits directly above the first link** with no space between them, and its heading style does not match the "Profile" page-card title.
- **V5. Dates use the en-US `medium` format** (for example "Oct 11, 2026, 7:40:52 PM"). The app has no culture-aware date formatting yet.
- **V6. At 375 the header wraps** the user-menu button under the logo. This predates S-03 and is unchanged.
