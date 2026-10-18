# Sighting Detail UI Contract — Plan Brief

> Full plan: `context/changes/sighting-detail-ui-contract/plan.md`
> Research: `context/changes/sighting-detail-ui-contract/research.md`

## What & Why

Bring the sighting detail page (`/sightings/:id`) up to the same design-system contract as the Profile and nearby-map views. The audit found no missing tokens or components, so the work is about how the page is entered and left, the structure around its card, the behaviour of its single-marker map, and the missing visual gate. It is the follow-up the nearby-map UI contract deliberately left out (one view per change).

## Starting Point

The page already reads 6 shared components and has 0 hardcoded values. It sets no tab title, moves no focus on arrival, sits in a bare wrapper with its Back link outside a narrow card, and shows a focusable marker whose Enter/Space does nothing and which reads as an unpressed toggle. The kitchen sink has Profile and Nearby map groups only, so no state of this page is ever rendered side by side.

## Desired End State

Opening a sighting by navigation puts focus on its heading and shows "<name> – PigeonWatch" in the tab; the Back link sits in the card's footer and still goes to the map; "Show map" reveals a map whose marker is display-only. The kitchen sink renders every state of the page, `visual-notes.md` records the 7-state matrix and the Tab pass, and the Frontend rules file tells the next agent where to look.

## Key Decisions Made

| Decision | Choice | Why (1 sentence) | Source |
| --- | --- | --- | --- |
| Missing tokens / components | None needed; no environment or token phase | The scan returns 0 hits and 6 shared components are already read | Research |
| Back link target | Fixed link to `/`, "Back to map", from every entry point | The label is always true and no other view is touched; losing the map state is deferred | Plan |
| Back link placement | In the card's `actions` footer; page root becomes `app-page-card` | Login and register already use that slot; removes a wrapper and the width mismatch with no new shared code | Plan |
| Arrival | Per view: tab title from the name, heading focus once the state leaves loading | Fixes this view with a pattern the nearby page already uses, without touching every route or the initial bundle | Plan |
| Heading focus mechanism | `PageCard.focusHeading()`; first-arrival focus only when entered by navigation | The `h1` lives inside the shared card, and the sink must never take focus on load | Plan |
| Title lifecycle | Reset to the app name on destroy | A captured value would restore another sink cell's title | Plan |
| Marker semantics | `app-map-view` input `interactiveMarkers` (default true); detail map sets false | The marker has no action, and the rules file already says the detail map leaves selection unset | Plan |
| Charge 4 (non-404 errors) | Deferred; one generic error kept | The endpoint answers only 200 or 404, so other failures are transient and a retry is right | Plan |
| Visual gate | Kitchen-sink "Sighting detail" group, 9 cells | No state of the page is rendered today; the sink is the repo's existing gate | Research |

## Scope

**In scope:** display-only markers in `app-map-view` (and its stub); the detail page's layout, tab title, heading focus, map label and marker name; two resx keys; `PageCard.focusHeading()`; the missing detail specs; a sink group with stubs, cells and spec; `visual-notes.md`; the Frontend rules-file update; marking deferred charges.

**Out of scope:** charge 4, preserving map state on Back, an origin-aware Back label, app-wide route titles and focus, Material hover, date locale, the unread label keys, a shared Back-link component, new tokens or libraries.

## Architecture / Approach

Fix the shared contract first, then the view, then the gate, then the rule. `app-map-view` gets one input read inside the effect that builds markers. `PageCard` exposes a heading-focus method, and the detail page calls it after render when its state leaves loading, sets the title through `Title` from resx keys, and drops its wrapper divs. The sink gains a `sightingDetail` stub group (a `get` stub with seven modes) and cells that reach their states through the real DOM; the one scenario that moves focus runs from a manual button.

## Phases at a Glance

| Phase | What it delivers | Key risk |
| --- | --- | --- |
| 1. Display-only markers in the shared map | `interactiveMarkers` input, stub and spec | The nearby map and pin-picker must keep today's behaviour by default |
| 2. Detail view contract | Card-rooted layout, title, heading focus, map semantics, resx keys, missing specs | Navigation-gated focus may not be detectable at construction (fallback planned) |
| 3. States and visual gate | Sink group, sink spec, `visual-notes.md` with the 7-state matrix | The sink must not scroll or take focus on load |
| 4. Rule and guard | Frontend rules-file update, deferred charges marked | None significant |

**Prerequisites:** the API running with a registered user for the Phase 2 manual checks; `npm install` done in `PigeonWatch/Frontend`.
**Estimated effort:** ~3-4 sessions across 4 phases.

## Open Risks & Assumptions

- Assumes `Router.currentNavigation()` is set while the routed component is created; if not, the plan's fallback is to gate on the route's `id` param.
- Rendered layout, focus order and screen-reader output were never observed in the research; Phase 3 measures them and may surface a charge the audit inferred wrongly.
- The page title is set per view; other routes keep the static title until an app-wide change.

## Success Criteria (Summary)

- A keyboard or screen-reader user arriving at a sighting lands on its heading, sees its name in the tab, and meets no dead marker stop on the map.
- No map or tile request happens before "Show map" is pressed.
- Every state of the page (loaded, unnamed, long name, map shown, not found, error, loading, retry, map failed) is visible in the kitchen sink, and the next agent is pointed at the tokens, components and sink by the rules file.
