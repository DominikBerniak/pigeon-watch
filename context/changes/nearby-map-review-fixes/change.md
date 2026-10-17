---
change_id: nearby-map-review-fixes
title: Nearby map review fixes
status: impl_reviewed
created: 2026-10-17
updated: 2026-10-17
archived_at: null
---

## Notes

Carried over from the archived `nearby-map-ui-contract` (`context/archive/2026-10-11-nearby-map-ui-contract/follow-ups/review-fixes.md`; evidence and measurements in its `visual-notes.md`, "Keyboard and accessible-name pass", findings F2-F4; origin: `reviews/impl-review.md` F6). One view only: the nearby map home (`/`, `PigeonWatch/Frontend/src/app/features/sightings/nearby-map-page`) and `app-map-view`. Same contract variant as the archived change: existing design system, no new palette, no UI library. Do it before S-05 (filters) or S-06 (urgency list) build on the map toolbar.

**Defects**

- **F2, map moves 60 px down at 375px after a failed location.** "Use my location" appears after `denied`, `unavailable` or `timeout` and wraps to a second toolbar row at 310 px; `md` and `lg` do not move. Options: keep both buttons on one row at 375 (shorter label, or an icon button with an accessible name), or reserve the second row from first render while geolocation can still fail.
- **F3, map moves 61 px down at every width on a nearby error.** The one-line error `app-alert` takes its own row under the toolbar. Options: reserve the row, or put the error text in the status slot and keep the `alert` role on it. Keep the earlier decision that Try again sits in the toolbar (the plan.md addendum in the archive).
- **F4, status reports search results above a map that failed to load.** The page searches on a known position whatever `app-map-view` does, so "N open sightings within 10 km" sits over the map's load error. Needs a load-failure signal from `app-map-view` (for example a `loadFailed` output) that the page uses to suppress the count.

**Checks not done in the archived change**

- Keyboard Tab pass at 375px and `md` (the toolbar wraps at 375).
- Screen-reader announcements of the toolbar status text, the error alert, and the focus moves after select, Close, Try again and a successful "Use my location".
- The map home against the local API with real data.

**Not carried here:** `sighting-detail-ui-contract` (`/sightings/:id`) stays its own change, one view per change.
