---
date: 2026-10-18T12:23:15+02:00
researcher: Claude (Sonnet 5.5) for Dominik Berniak
git_commit: fcebe524a3a3a059cf39190e0a2ca252b7d154ae
branch: main
repository: 10xDevs (PigeonWatch)
topic: "Sighting detail page (/sightings/:id): two-way design-system audit and charges"
tags: [research, ui, sighting-detail-page, map-view, kitchen-sink, charges]
status: partial
last_updated: 2026-10-18
last_updated_by: Claude (Sonnet 5.5)
---

# Research: Sighting detail page (/sightings/:id): two-way design-system audit and charges

**Date**: 2026-10-18T12:23:15+02:00
**Researcher**: Claude (Sonnet 5.5) for Dominik Berniak
**Git Commit**: fcebe524a3a3a059cf39190e0a2ca252b7d154ae
**Branch**: main
**Repository**: 10xDevs (PigeonWatch)

Paths are relative to `PigeonWatch/Frontend/src/` unless they start with `context/` or `PigeonWatch/`. `DETAIL` = `app/features/sightings/sighting-detail-page/sighting-detail-page` (`.html`, `.ts`, `.spec.ts`). `NM` = `app/features/sightings/nearby-map-page/nearby-map-page`. "Sink" = the dev-only kitchen sink at `/dev/kitchen-sink`. Nothing was rendered or measured for this research: layout and focus statements are read from code and marked "inferred".

## Research Question

From `change.md` and the `/10x-ui` audit brief: for the one view `/sightings/:id`, audit in both directions (source to views, view to source), read the agent rules for UI instructions, run the hardcoded-value scan, ask what happens when the view is reached logged out, with no data or straight from a link, and write 3 to 5 charges (file, line, user impact), with the charges the plan will not address marked deferred.

## Summary

- **Missing-token and missing-component charges are empty.** The hardcoded-value scan returns 0 hits in the 3 view files (`DETAIL` `.html`, `.ts`, `.spec.ts`), and `style=`, `::ng-deep` and `!important` also return 0. The view has no stylesheet of its own. It uses 6 `shared/ui` components (`page-card`, `alert`, `error-retry`, `loading-indicator`, `detail-list`/`detail-item`, `criticality-badge`). No rule in `PigeonWatch/Frontend/CLAUDE.md` or `PigeonWatch/CLAUDE.md` invites one-off values, and the detail page violates none of the UI rules as written.
- **The audit finds five charges, all in the "accidental architecture" and states categories:** how the user arrives and leaves (page title, focus, Back link), the layout structure around the card, the semantics of the single-marker map, the collapsed error states, and the missing visual gate.
- **The kitchen sink cannot render the detail page today.** `CellStubs.sightingsApi` has `mine` and `nearby` and no `get` (`app/dev/kitchen-sink/cell-stubs.ts:159-162`), there is no detail group, and `state-cell.html` only switches on `profile` and `nearbyMap`. Adding the cells is a known set of five extension points.
- **Not settled (status partial):** rendered layout, focus order and screen-reader behaviour were not observed, and the API's behaviour for a malformed id is inferred from a route constraint, not run.

## Detailed Findings

### Source to views (which views read the contract)

Counted in `.html` templates outside `shared/ui` and `dev/` (per view: one count per element in the file).

| shared/ui component | detail | nearby | profile | report | login | register |
|---|---|---|---|---|---|---|
| `app-page-card` | 1 | 0 | 1 | 1 | 1 | 1 |
| `app-alert` | 1 | 1 | 2 | 1 | 2 | 1 |
| `app-error-retry` | 1 | 0 | 0 | 0 | 0 | 0 |
| `app-loading-indicator` | 1 | 1 | 0 | 0 | 0 | 0 |
| `dl[app-detail-list]` | 1 | 1 | 1 | 0 | 0 | 0 |
| `div[app-detail-item]` | 7 | 2 | 2 | 0 | 0 | 0 |
| `app-criticality-badge` | 1 | 2 | 0 | 0 | 0 | 0 |

- The detail page reads 6 distinct shared components; the nearby page 4, the profile page 6 (a different 6). Evidence: `DETAIL.html:5,8,11,14,21,22-44,26`; `NM.html:54,69,92,108-113`; `profile-page.html:1,4,6,8,11,43`.
- Tokens and common classes that no template reads (outside `_tokens.scss`): the tokens `--pw-color-accent-teal` (only inside the gradient tokens), `--pw-duration-fast`, `--pw-shadow-sm`, `--pw-space-8`; the classes `pw-center-screen` (no template) and `pw-container` (only `app.html`). Not charges for this view.
- The detail page's own template uses one common class, `pw-stack` (`DETAIL.html:1,20`), declared in `styles/_common.scss`.

### View to source (what the template does by hand)

- **Two bare wrapper `<div>`s hold a single control each.** `DETAIL.html:2-4` wraps the Back link and `DETAIL.html:48-62` wraps the "Show map" button, both so the flex-column parent does not stretch the control. The retry button solves the same problem inside its component with `align-self: flex-start` (`app/shared/ui/error-retry/error-retry.scss`), not with a wrapper. No shared component or `pw-*` class covers "a link or button in a column".
- **The Back link sits outside the card.** `DETAIL.html:1-4` makes the page's only routed template that has a wrapper around `app-page-card`: the profile, report and login templates have `app-page-card` as the template root (`profile-page.html:1`, `report-sighting-page.html:1`, `login-page.html:1`). The page card is capped at `--pw-form-max-width` (28rem, `styles/_tokens.scss:74`) and centred (`app/shared/ui/page-card/page-card.scss:3-8`), while `<main>` is capped at `--pw-content-max-width` (40rem, `app/app.scss:20-24`; the detail route declares no `data.layout`, `app/app.routes.ts:42-49`). The Back link row is therefore full width and left-aligned beside a centred 28rem card at widths above 28rem plus the main padding (inferred, not rendered).
- **The same sighting is presented differently in two views.** The nearby selected card shows a name heading, status and criticality in an inline `detail-list` (`NM.html:104-117`); the detail page shows seven rows in a column list (`DETAIL.html:21-47`). Both use `app-criticality-badge`, so the criticality rule holds in both.
- **Headings.** One `h1`, rendered by `app-page-card` (`app/shared/ui/page-card/page-card.html:4`). It reads "Sighting" while loading and in the not-found and error states, and the sighting name (or "Unnamed bird") once loaded (`DETAIL.ts:75-81`). There is no `h2` on the page.
- **UI text.** Every visible string in `DETAIL.html` goes through the `translate` pipe and the keys exist in `PigeonWatch/Api/BusinessObjects/Resources/UiLabels.resx` (for example `sighting.detail.back`, `sighting.errors.notFound`, `map.ariaLabel`). The status and criticality label keys come from `sighting.types.ts`, which was not read.
- **Dates.** `date: 'medium'` inside `<time datetime>` (`DETAIL.html:39-43`) is the same expression as the My sightings list (`app/features/sightings/my-sightings/my-sightings.html:31-33`). How the Angular locale is configured was not checked.

### Entry point: logged out, no data, straight from a link

- **Logged out.** `sightings/:id` has `canActivate: [authGuard]` and no `title` or `data` (`app/app.routes.ts:42-49`). The guard redirects to `/login?returnUrl=<state.url>` (`app/core/auth/auth.guards.ts:7-14`), `login-page.ts:52,92` navigates back through `safeReturnUrl`, so the return URL is kept. The detail component never instantiates for a logged-out user and no API call is made.
- **Expired token while on the page.** The auth interceptor calls `session.expire(router.url)` on an unrecoverable 401 (`app/core/auth/auth.interceptor.ts:72,79`), which navigates to `/login?returnUrl=<current url>` (`app/core/auth/session.service.ts:130-133`). The original error still reaches the page, so the generic error state shows until the navigation completes (inferred from both happening in one tick).
- **Failure mapping.** `sightingFailure` returns `notFound` only for HTTP 404; every other failure, including 400, 401, 403, 5xx and status 0, becomes `generic` and the page's `error` state (`app/features/sightings/sighting-errors.ts:264-274`, `DETAIL.ts:220-222`). The API route is `[HttpGet("{id:guid}")]` behind `[Authorize]` and returns an empty `NotFound()` for an unknown id (`PigeonWatch/Api/WebApi/Controllers/SightingsController.cs:15,44-52`). A non-GUID id matches no endpoint, so the page most likely shows the not-found state (inferred; the host's status-code-page setup was only read in part).
- **503 and offline.** The warm-up interceptor retries status 0 and 503 until a 120 s budget (`app/core/warmup/warmup.interceptor.ts:57-64,83-91`), so on those failures the page stays in the loading state for up to 120 s before the generic error.
- **Back to map.** `DETAIL.html:3` is `routerLink="/"` with the fixed label `sighting.detail.back`, whatever the origin. The detail page is reached from three places: the nearby card's "View details" (`NM.html:119`), the My sightings list (`my-sightings.html:22`), and the redirect after reporting (`report-sighting-page.ts:149`). The nearby page keeps its centre, zoom, selection and results in component signals only (`NM.ts:106-116`; no route params, no storage found), so returning to `/` starts a new page that locates again.
- **Title and focus on arrival.** `app/` contains no `Title` service use and no `document.title` assignment (grep for `Title`/`document.title` in non-spec `.ts` found none), no route sets `title`, and `index.html:5` has the static `<title>PigeonWatch</title>`. `app/app.ts` only toggles the wide layout after `NavigationEnd`, and `provideRouter(routes)` (`app/app.config.ts:18`) has no features. Nothing moves focus to the new page or announces it (inferred for focus: Angular's router does not move focus by itself).

### Map block

- The map host `<div id="sighting-detail-map">` always exists, so `aria-controls` on the toggle is valid (`DETAIL.html:49-55,63-74`). The toggle both flips its label ("Show map" / "Hide map") and binds `aria-expanded` (`:52-60`).
- Cost guardrail holds: `app-map-view` is rendered only after the toggle and only when `position()` is non-null (`DETAIL.html:64-72`); the spec asserts no `app-map-view` before the press (`DETAIL.spec.ts:109-135`).
- The single marker is a Leaflet `keyboard: true` marker (`app/shared/map/map-view/map-view.ts:206`). Enter and Space emit `markerSelect` (`map-view.ts:219-227`), and the detail page binds no `(markerSelect)` (`DETAIL.html:66-71`), so activating it does nothing. `applySelection` sets `aria-pressed` to `"false"` on every marker, including this one, because `selectedId` stays `null` (`map-view.ts:229-238`, line 227). The marker's accessible name is the sighting name only (`DETAIL.ts:184-190`, `label: this.title()`), whereas the nearby page uses the `map.markerLabel` key that includes the criticality (`NM.ts:160-172`). The region label is `map.ariaLabel`, "Map of sightings", for a map that shows one sighting.
- Pressing "Show map" or "Hide map" moves focus nowhere; focus stays on the toggle (the page has no `.focus()` call, `DETAIL.ts` and `.html`). A Leaflet load failure renders the map's own `app-error-retry` inside the map block and the page ignores `loadFailed` (`app/shared/map/map-view/map-view.html:3-7`).
- `mapShown` is reset to `false` on every emission of the id/reload stream (`DETAIL.ts:198`), which includes a retry and a route-param change. No spec covers that reset (spec cases: load, unnamed, map toggle, 404, 500 retry, back link).

### Loading and layout stability

- While loading, `page-card` shows the `h1` "Sighting" and one `app-loading-indicator` (block layout, 32 px spinner, no reserved height); `app/shared/ui/loading-indicator/loading-indicator.scss`. The indicator is not given `[live]`, so it has no `role="status"` of its own here (inferred from the template, `live` default not read).
- When the sighting arrives the spinner is replaced by seven rows, the toggle and the map host, and the `h1` text changes to the sighting name, which can wrap (`page-card.scss` sets no truncation). There is no `min-height` on the card body or the map host. This is inferred from structure; no offset was measured. Map open adds the `--pw-map-height` (24rem, `styles/_tokens.scss:78`) canvas below the toggle, a user-initiated shift.

### Kitchen sink and the 7 states

- **Why the sink cannot show this view now:** `CellDefinition` groups are `profile` and `nearbyMap` only (`app/dev/kitchen-sink/cell-stubs.ts:21-44`, `cell-definitions.ts:14-35,342-345`); `state-cell.html:27-34` switches on those two groups; the `SightingsApi` stub has `mine` and `nearby` and no `get` (`cell-stubs.ts:159-162`), so a detail cell would throw a `TypeError` on `sightingsApi.get`.
- **Extension points to render the page (read from code, not run):** a new `SightingDetailModes` in the `StubModes` union with a `detail` mode (`loaded`, `loadedUnnamed`, `notFound`, `failure`, `pending`, optionally `failureThenLoaded` for a retry that succeeds, modelled on the `nearbyRequests` counter at `cell-stubs.ts:144`); a `get` on the stub; a `detailCells` group in `cell-definitions.ts` with `wide: false`; `SightingDetailPage` in the `StateCell` imports and a `@case` in `state-cell.html`; in `kitchen-sink.spec.ts` a `TestBed.overrideComponent(SightingDetailPage, …MapViewStub…)`, and updates to `pageOf` (lines 112-115) and the `app-profile-page, app-nearby-map-page` selector (line 124). The cell's `ActivatedRoute` has no `id` param, so `get('')` is called; the stub can ignore its argument.
- **Existing tooling needs no change:** the tile-less `LEAFLET_LOADER` wrapper applies to every group (`state-cell.ts:51-54`); `press`, `click` and `waitFor` steps need no driver change (`cell-driver.ts`); `style-token-coverage.spec.ts` already scans every `.html` and `.scss` under `app/` (including the dev folder) and has no per-view list (`styles/style-token-coverage.spec.ts:103,127-177`).
- **7-state matrix for this view:**

  | State | Applies | Notes | Test today |
  |---|---|---|---|
  | default | yes | loaded sighting, seven rows, Show map | `DETAIL.spec.ts:84-101` |
  | hover | yes | Back, Show/Hide map, Try again (Material state layer); markers once the map is shown | none (visual) |
  | focus-visible | yes | Back, toggle, Try again, then map region and marker after the toggle | none |
  | disabled | N/A | no disabled control (no form, toggle and retry always enabled) | n/a |
  | error | yes (page-level) | `notFound` (info alert, no retry) and `error` (alert plus Try again); field error N/A | `DETAIL.spec.ts:137-167` |
  | empty | N/A | no list; an unnamed sighting is a loaded variant | unnamed title asserted (`:101`) |
  | loading | yes | spinner on first load, on retry and on id change | none |

## Charges

Each charge: where, effect on the user. Category in brackets.

1. **[Accidental architecture] Arrival and way back: the page title never changes, nothing announces or focuses the new page, and "Back to map" is a fixed link to `/`.**
   - Evidence: `app/app.routes.ts:42-49` (no `title`), `index.html:5` (static `<title>PigeonWatch</title>`), `app/app.ts` (no focus on navigation), `DETAIL.html:3` (`routerLink="/"`), `NM.ts:106-116` (state lives in component signals).
   - User impact: a screen-reader or keyboard user opening a sighting hears no new page and keeps the same tab title as every other page; a user who came from the profile list or from the report redirect still sees "Back to map" and lands on a map that re-locates and has dropped the selected sighting.
   - **Status:** title and focus addressed in plan Phase 2 (per view: tab title from the name, heading focus after load, title reset on destroy). **Deferred** with a reason: keeping Back as a fixed link to `/` and the map losing its state on return (preserving state reaches into the nearby page; an origin-aware label would edit three other views' links); app-wide route titles and focus on navigation (changes every view and touches the initial bundle).
2. **[Accidental architecture, missing shared answer] The layout around the card is built from two bare wrapper `<div>`s and a Back link outside a 28rem card inside a 40rem main.**
   - Evidence: `DETAIL.html:1-4,48-62`, `page-card.scss:3-8`, `app/app.scss:20-24`; `profile-page.html:1`, `report-sighting-page.html:1` and `login-page.html:1` have no wrapper.
   - User impact: at widths above about 28rem plus the main padding the link row and the centred card do not share an edge (inferred, not rendered), and a loaded sighting with a long name or the arrival of the data changes the card height with no reserved space.
   - **Status:** wrapper divs and the Back link addressed in plan Phase 2 (page root is `app-page-card`, Back link in the card's `actions` footer, the toggle's wrapper is a `pw-cluster` row). Measured in plan Phase 3 (`visual-notes.md`): the no-reserved-height jump on load (193 to 680 px at 375) is **deferred**: accepted for a first load. The 28rem card on large screens is **deferred** to the separate change `app-wide-large-screen-layout`.
3. **[States, accessibility] The map block gives a focusable single marker with a no-op Enter/Space, `aria-pressed="false"`, a name without criticality, and a plural region label; the toggle both flips its label and sets `aria-expanded`.**
   - Evidence: `app/shared/map/map-view/map-view.ts:206,219-238`, `DETAIL.html:49-60,66-71`, `DETAIL.ts:184-190`, resx `map.ariaLabel` / `map.markerLabel`.
   - User impact: a keyboard user can focus and activate a marker that does nothing and is announced as an unpressed toggle without its urgency; a screen reader reads "Hide map, expanded" for one control.
   - **Status:** addressed in plan Phases 1 and 2 (`interactiveMarkers` input on `app-map-view`, display-only marker, singular region label, criticality in the marker title). The toggle's label flip plus `aria-expanded` is **deferred**: left as is, with `aria-controls` valid.
4. **[States] All non-404 failures share one generic error with a retry, and 503 or offline shows a spinner for up to 120 s first.**
   - Evidence: `app/features/sightings/sighting-errors.ts:264-274`, `DETAIL.ts:220-222`, `app/core/warmup/warmup.interceptor.ts:57-64,83-91`.
   - User impact: a permanent 4xx offers a retry that will fail again, and an offline user waits up to two minutes with a spinner. The interceptor part belongs to the warm-up and auth layers, not to this view.
   - **Status: deferred.** `GET /sightings/{id}` answers 200 or 404 (401 is handled by the auth interceptor), so every other failure reaching the page is transient and a retry is correct; the 120 s wait belongs to `warmup.interceptor.ts` and the 401 flash to `auth.interceptor.ts`. `sightingFailure` and the error state are unchanged.
5. **[Visual gate] The sink has no detail cell and the loading state, the Show map toggle's focus and the map-library failure have no spec.**
   - Evidence: `app/dev/kitchen-sink/cell-stubs.ts:21-44,159-162`, `cell-definitions.ts:342-345`, `state-cell.html:27-34`; `DETAIL.spec.ts` (no loading, no focus, no `loadFailed` case).
   - User impact: none today, but no state of this page is ever rendered side by side, so a regression in `disabled`-like states (error, loading, not found) would go unseen.
   - **Status:** addressed in plan Phases 2 and 3 (specs for loading, title, focus, map semantics and the retry reset; a nine-cell "Sighting detail" kitchen-sink group; `visual-notes.md`).

**Deferred (decided in `/10x-plan`, with reasons):** the auth-interceptor 401 flash and the 120 s warm-up wait in charge 4 (cross-cutting, outside one view); Material state-layer hover styling (no page-specific code; a hover tint was not visible in the browser pane, see `visual-notes.md`); the date format locale (no page-specific code); the unread label keys behind `statusLabel()` and `criticalityLabel()` (no page-specific code).

## Code References

- `app/features/sightings/sighting-detail-page/sighting-detail-page.html:1-4,48-62` - wrapper divs and Back link
- `app/features/sightings/sighting-detail-page/sighting-detail-page.html:49-74` - map toggle, host, map-view binding
- `app/features/sightings/sighting-detail-page/sighting-detail-page.ts:75-81,184-190,198,220-222` - title, marker label, `mapShown` reset, failure mapping
- `app/app.routes.ts:42-49` - route, guard, no title
- `app/shared/map/map-view/map-view.ts:206,219-238` - marker keyboard, `aria-pressed`
- `app/shared/ui/page-card/page-card.scss:3-8`, `app/app.scss:20-24` - card and main widths
- `app/dev/kitchen-sink/cell-stubs.ts:21-44,144,159-162`, `state-cell.html:27-34` - sink extension points
- `PigeonWatch/Api/WebApi/Controllers/SightingsController.cs:15,44-52` - id constraint and 404

## Architecture Insights

- The contract is intact at the template level (0 literals, 6 shared components); what is left is the structure around the card and the behaviour of controls, which the style guard cannot see (it scans `.scss` and `.html` for literals, undeclared `pw-*` classes, inline styles and `mat-progress-spinner` only; `styles/style-token-coverage.spec.ts:127-177`).
- The "Back link" and "button in a column" patterns have no shared answer; the "promote on the second use" rule (`PigeonWatch/Frontend/CLAUDE.md`, shared/ui bullet) has not triggered because the Back link appears in one template.
- Route-level behaviour (title, focus on navigation) is an app-shell concern; fixing it for one route either adds a per-route `title` or touches `app.ts`, which affects every view.

## Historical Context (from prior changes)

- `context/archive/2026-10-11-nearby-map-ui-contract/change.md` ("Follow-up change") - the reason this change exists; it deferred the detail page and asked it to reuse what became shared.
- `context/archive/2026-10-17-nearby-map-review-fixes/visual-notes.md` - the method for sink measurements (offsets from the page top at 375 px, `md` and `lg`) and the Tab-pass format to follow.
- `context/archive/2026-10-11-profile-ui-contract/` - the earlier single-view contract change; cells and a rule-file entry were its outputs.

## Related Research

- `context/archive/2026-10-17-nearby-map-review-fixes/research.md` - toolbar and map-view findings; the `loadFailed` output exists on `app-map-view` and the detail page does not read it.

## Open Questions

1. **Back link:** keep a fixed `/` link, make it history-aware, or preserve map state through the route (decision for `/10x-plan`; the third reaches into the nearby page).
2. **Page title:** a static route `title` ("Sighting") or one set from the loaded name; either one is app-wide pattern work.
3. **Focus on arrival:** a per-view `h1` focus (as the nearby page does for its card, `NM.ts:238`) or an app-shell rule.
4. **Marker semantics:** drop the marker's interactivity on the detail map, or give it a purpose and the criticality in its name.
5. **Not observed:** rendered layout at 375 px and `md`, focus order, screen-reader output, and the host's response to a non-GUID id.
