# Nearby map review fixes — Plan Brief

> Full plan: `context/changes/nearby-map-review-fixes/plan.md`
> Research: `context/changes/nearby-map-review-fixes/research.md`

## What & Why

Fix three review findings on the map home and `app-map-view`, carried from the archived `nearby-map-ui-contract`. The map moves down when "Use my location" appears at 375 px (F2) and when a nearby error appears (F3), and the status says "N open sightings within 10 km" over a map that failed to load (F4). Do it before S-05 (filters) and S-06 (urgency list) build on the map toolbar.

## Starting Point

The toolbar's actions container never shrinks, so a third button wraps (by 3 px on the real page at 375 px). The error `app-alert` is a separate row under the toolbar. `app-map-view` keeps its load failure in a protected signal that nothing can read, so the page's `status()` has no input from the map. All three are reproduced in the kitchen sink and traced in the research.

## Desired End State

After a failed location, "Search this area" and "Locate me" share one row at 360 and 375 px and the map does not move. A nearby error replaces the status text inside the toolbar slot, Try again stays beside Search, and the map does not move at 375 px or `md`. While the map failed to load, the page shows no count, empty message, zoom prompt or hint and Search is disabled; those texts return when the map recovers. Measurements and the manual keyboard and screen-reader checks are recorded in `visual-notes.md`.

## Key Decisions Made

| Decision | Choice | Why (1 sentence) | Source |
| --- | --- | --- | --- |
| F2 fix | Map-home-only label `map.locateMe` = "Locate me" | Two stroked buttons fit 343 px (303 px total), no new pattern or style change | Plan |
| F2 alternatives | Not icon button, text variant, reserved row or accepting the wrap | Each adds a new pattern, changes the toolbar look, or costs 60 px on every load | Research / Plan |
| F3 fix | Error `app-alert` inside `.nearby-toolbar__slot`, slot `min-height: var(--pw-control-height)` | Prototyped at zero shift at 375 px and `md`, keeps one status element | Research / Plan |
| Try again | Stays in the toolbar beside Search | Earlier decision in the archived plan addendum | Research |
| F4 signal | New `loadFailed` output on `app-map-view` (`true` on failure, `false` when retry starts); internal signal renamed `failed` | An output cannot share a name with the existing member; the other two consumers ignore it | Research / Plan |
| F4 status rule | Drop `found`, `empty`, `zoomToSearch`, `hint` while the map is failed; Search disabled; `locating`, `loading`, `error` stay | None of the four can be acted on next to a failed map; the map's own alert carries the message | Plan |
| Verification | Two new sink cells, specs, manual Tab and screen-reader checks recorded in `visual-notes.md` | The combinations had no cell; jsdom has no layout; local-API run left out of scope | Plan |

## Scope

**In scope:** `app-map-view` output and stub; the page's status, Search and toolbar markup, styles and specs; one resx key; two sink cells; `Frontend/CLAUDE.md` rule edits; `visual-notes.md`.

**Out of scope:** keeping three buttons on one row at 375 px after a failed location plus a nearby error (about +18 px at `md` and a wrapped third button at 375 px remain, recorded); reserving rows from first render; the picker and detail page; a new status text for a failed map; icon buttons or new tokens; running against the local API; another culture.

## Architecture / Approach

`MapView` renames its protected signal and emits `loadFailed(boolean)`. The page binds it to a `mapFailed` signal that feeds `status()` and `searchDisabled()`; the Search button's `aria-describedby` is set only when a status text exists. In the template the error alert moves from after the toolbar into the slot as a sibling of the status element, and the slot gets the 3rem floor. The map home uses its own short label key; the shared label stays for the picker.

## Phases at a Glance

| Phase | What it delivers | Key risk |
| --- | --- | --- |
| 1. Map load-failure signal (F4) | `loadFailed` output, stub, page rule, specs, one sink cell, rule-file lines | The sink spec runs over the stub, so the real failed-map status is covered only by the page spec |
| 2. Toolbar layout (F2, F3) | `map.locateMe`, alert in the slot, spec and sink renames, combined-case cell, rule-file edits | Layout cannot be asserted in jsdom; residuals for the three-button case are accepted |
| 3. Measure and record | `visual-notes.md` with offsets at 375 px, `md`, `lg`, Tab and screen-reader results | Screen-reader checks need a human with assistive technology |

**Prerequisites:** the archived `nearby-map-ui-contract` code is on this branch (it matches squash commit `f0a9c8e` on `main`); `npm install` done in `PigeonWatch/Frontend`.
**Estimated effort:** about 2 sessions across 3 phases.

## Open Risks & Assumptions

- The real-page width at 375 px (343 px) is derived, not rendered; the sink cell is 34 px narrower, so sink checks are conservative.
- The in-slot alert was prototyped by moving DOM nodes, so its screen-reader behaviour is unverified until Phase 3.
- A longer label in a future culture could wrap the toolbar again.
- The page against the local API with real data stays unverified.

## Success Criteria (Summary)

- At 360 and 375 px the map does not move when "Locate me" appears or when the nearby error appears after a successful location, as measured in the sink.
- No result count, empty message, zoom prompt or hint appears over a map that failed to load, and they return on recovery.
- Specs and the production build pass, and `visual-notes.md` records the measurements and the manual accessibility checks.
