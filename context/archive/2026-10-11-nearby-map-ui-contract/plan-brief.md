# Nearby Map UI Contract — Plan Brief

> Full plan: `context/changes/nearby-map-ui-contract/plan.md`
> Research: `context/changes/nearby-map-ui-contract/research.md`

## What & Why

The nearby map home (`/`) already uses only tokens, but it fails users in how it behaves. Keyboard users can reach markers but cannot open them. Urgency is shown by colour alone. Every result lands below the map, off-screen on a phone. A user without location can run a country-wide search that reports "no sightings". This change fixes those five charges (C1–C5) on one view and moves the blocks it shares with other screens into `shared/ui`.

## Starting Point

There are 0 hardcoded values and the token guard already covers the view. The spinner, error+retry block and label/value list are copied across the map, the detail page, My sightings and Profile. `app-map-view` listens only for clicks and has no selected state or zoom output. The kitchen sink renders only Profile.

## Desired End State

- Enter or Space on a marker opens the card, and the selected marker stands out.
- A legend and a criticality badge show urgency as colour plus text.
- A toolbar above the map holds "Search this area" and a status line for every state. The map keeps its markers while reloading.
- Below zoom 10 the search is disabled, and the toolbar says why. Without a position a "Use my location" button lets the user try again.
- On desktop the page is wider, and from 1024px the card sits beside the map.
- The kitchen sink shows every state with no API and no tiles.

## Key Decisions Made

| Decision | Choice | Why (1 sentence) | Source |
| --- | --- | --- | --- |
| Token phase | None; a token is added only where a component needs one | The scan found 0 literals | Research |
| C3 adoption | Replace every copy (map, map-view, detail, My sightings, Profile) | Removes the duplication now instead of over several follow-ups | Plan |
| Criticality badge | Used wherever criticality appears, including detail and My sightings | Follows from replacing every copy; no plain-text criticality left | Plan |
| C5 search rule | "Search this area" disabled below zoom 10, reason shown | The user picks their own area; no misleading country-wide search | Plan |
| C5 extras | Neutral heading "Sightings in this area" and a "Use my location" retry without a position; no radius circle | A user whose location failed can recover without reloading; the circle was not wanted | Plan (retry added after first draft) |
| Retry visibility | Shown after `denied`, `unavailable` or `timeout`; hidden when the browser has no geolocation | After `denied` it works once the user unblocks the site permission; `unsupported` can never succeed | Plan |
| C4 layout | Toolbar above the map with a persistent status slot; markers kept while loading; focus moves to the card on select | Action and feedback sit together, and the map stops jumping | Plan |
| Map width | 64rem from `md` for this route only; card beside the map from `lg` | The map is never narrower than today | Plan |
| Empty side column | Legend always shown, plus "Select a marker to see details" | The aside is never an empty box | Plan |
| Wide-layout mechanism | Route `data: { layout: 'wide' }` read by the shell | Explicit, with no feature selector in the shell | Plan |
| Guard | Spec fails on `<mat-progress-spinner` outside `shared/ui`, plus a CLAUDE.md rule | The next agent cannot reintroduce the copy | Plan |

## Scope

**In scope:**
- `app-map-view`: keyboard selection, `selectedId`, `zoomChange` and `focusMarker`.
- Four new `shared/ui` components, adopted everywhere.
- The map page layout and states, the wide route layout, and six resx keys.
- Kitchen-sink map cells, the spec guard and the CLAUDE.md rule.

**Out of scope:**
- Drawing the search radius, auto-zoom and map overlays.
- Header width and navigation, and the warm-up overlay.
- `invalidateSize`, a results list (S-06), dark mode and a screenshot runner.
- Detail-page sink cells; these move to `sighting-detail-ui-contract`.

## Architecture / Approach

Order follows `/10x-ui`: the sink and baseline first, then shared components, then the map component, then the view, then states and the guard. The map work stays in `app-map-view`, the only Leaflet importer, so the location picker and the detail map get the keyboard fix too. The sink renders the real page with stubbed services and a Leaflet loader that skips tiles.

## Phases at a Glance

| Phase | What it delivers | Key risk |
| --- | --- | --- |
| 1. Kitchen sink for the map and baseline | Generalised cells, map stubs, a tile-less loader, before evidence | The tile-less loader may not work; fallback is dev-only real tiles |
| 2. Shared components (C2, C3) | Loading indicator, error+retry, detail list, badge in 5 places | Regressions in 4 views at once; detail page has no sink |
| 3. Map component (C1) | Keyboard selection, selected state, zoom output, focus method | Leaflet keyboard events; must not break the picker |
| 4. Map view layout (C4, C5) | Toolbar, status slot, zoom rule, Use my location, aside, wide layout, heading | Shell change in the initial bundle (about 14 kB headroom) |
| 5. States, gate and guard | 7-state matrix, after evidence, keyboard pass, spec, rule | Matrix needs 3 widths (375px, md, lg) |

**Prerequisites:** none; `report-sighting-on-map` and `profile-ui-contract` are archived. A local API is needed only for the Phase 2 detail-page and Profile check.
**Estimated effort:** about 4–5 sessions across 5 phases.

## Open Risks & Assumptions

- Assumes Leaflet 1.9.4 delivers Enter and Space on a marker as a keyboard event the marker can listen to (from the research's source reading). Phase 1 confirms it in a browser.
- The shell's route-data read must stay small enough for the 500 kB initial budget.
- The detail page is checked by hand against the local API, not in the sink, until its follow-up change.

## Success Criteria (Summary)

- A keyboard-only user can find an urgent bird on the map, open it, and get back to the marker.
- On a phone, every search and selection shows its result without scrolling to look for it.
- No view in the app shows criticality as colour alone or carries its own copy of the spinner, error+retry or label/value list.
