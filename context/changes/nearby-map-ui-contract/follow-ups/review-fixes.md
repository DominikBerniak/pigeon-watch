# Review fixes: nearby-map-ui-contract

Queued from `reviews/impl-review.md` (2026-10-17). Evidence and measurements: `visual-notes.md`, Keyboard and accessible-name pass, findings F2-F4.

## Map home (`/`) defects deferred in Phase 5

- [ ] **Map moves 60 px down at 375px after a failed location** (visual-notes F2). "Use my location" appears after `denied`, `unavailable` or `timeout` and wraps to a second toolbar row at 310 px; `md` and `lg` do not move. Options: keep both buttons on one row at 375 (shorter label or icon button with an accessible name), or reserve the second row from first render while geolocation can still fail.
- [ ] **Map moves 61 px down at every width on a nearby error** (visual-notes F3). The one-line error `app-alert` takes its own row under the toolbar. Options: reserve the row, or put the error text in the status slot and keep the `alert` role on it. Keep the Phase 5 decision that Try again sits in the toolbar (plan.md addendum).
- [ ] **Status reports search results above a map that failed to load** (visual-notes F4). The page searches on a known position whatever `app-map-view` does, so "N open sightings within 10 km" sits over the map's load error. Needs a load-failure signal from `app-map-view` (for example a `loadFailed` output) that the page uses to suppress the count.

## Checks not done in this change

- [ ] Keyboard Tab pass at 375px and `md` (the toolbar wraps at 375).
- [ ] Screen-reader announcements of the toolbar status text, the error alert, and the focus moves after select, Close, Try again and a successful "Use my location".
- [ ] The map home against the local API with real data.

## Where to do it

The `sighting-detail-ui-contract` follow-up change, or a small change of its own before S-05 (filters) or S-06 (urgency list) build on the map toolbar.
