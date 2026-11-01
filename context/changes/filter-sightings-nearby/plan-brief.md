# Filter sightings by nearby area, status and criticality — Plan Brief

> Full plan: `context/changes/filter-sightings-nearby/plan.md`
> Research: `context/changes/filter-sightings-nearby/research.md`

## What & Why

S-05 (PRD FR-004) lets the user narrow the nearby map by sighting status and criticality. Rescuers want to see the birds that need help first, and the map today shows every open sighting within 10 km with no way to narrow it.

## Starting Point

The nearby endpoint takes only latitude and longitude and always returns the five open statuses; the repository already accepts a status list but nothing accepts a criticality. The map page has one search pipeline and no filter state, and the SPA has no filter control.

## Desired End State

The map toolbar has two multi-select filters (status, criticality). With nothing selected the map is identical to today. Choosing values narrows the markers through a new search, the count and empty text reflect the filter, and the detail card closes on every change.

## Key Decisions Made

| Decision | Choice | Why (1 sentence) | Source |
| --- | --- | --- | --- |
| Where filtering runs | Server-side optional `statuses` and `criticalities` query parameters | The 200-row cap then applies to matching sightings, and S-06 can reuse the contract. | Plan |
| Resolved statuses on the map | Stay excluded; status filter offers the five open statuses | Keeps the S-03 rule; "include resolved" belongs to the S-06 list (FR-020). | Plan |
| Invalid filter values | Undefined values and resolved statuses return 400 | Fails loudly instead of silently ignoring input. | Plan |
| Selection on filter change | Clear it, via the existing `search()` path | One rule with "Search this area" and no stale card. | Plan |
| Control shape | Two multi-select `mat-select` fields | Reuses the report form's pattern and theme with the least new styling. | Plan |
| Empty selection and memory | Empty means all; not remembered | First view equals S-03 and the API default of no parameters. | Plan |
| Shared component | Local to the map page until S-06 reuses it | `Frontend/CLAUDE.md` promotes a pattern on its second use. | Plan |
| Radius refinement | Not part of S-05 | Non-blocking roadmap unknown stays open. | Research |

## Scope

**In scope:** filter query parameters, validation and repository predicate; SPA wrapper, types and labels; filter controls, page behaviour, kitchen-sink cells; one Playwright test; documentation updates.

**Out of scope:** resolved statuses on the map, a changeable radius or named city, persisting filters, a shared filter component, new database indexes, the list view and urgency order.

## Architecture / Approach

Contract first: the API gains optional filter lists that narrow the existing box query, and the service intersects the requested statuses with the open set. The SPA wrapper sends them as repeated query parameters; a local filters component feeds the page's existing search pipeline, so a filter change is just another search at the last centre.

## Phases at a Glance

| Phase | What it delivers | Key risk |
| --- | --- | --- |
| 1. API filter contract | Validated filter parameters, service and repository filtering, updated tests and docs | `ListInBoxAsync` signature change breaks several pinned tests |
| 2. SPA data layer and labels | Wrapper, open-status list, label keys | Label coverage spec fails on any unused or missing key |
| 3. Map page filters | Two multi-selects, page behaviour, specs, kitchen-sink cells | Toolbar fit on phones; initial bundle is 512.1 kB of a 520 kB warning |
| 4. E2E and docs wrap-up | Playwright filter test, CLAUDE.md and roadmap notes | E2E depends on the seeded session and mocked geolocation |

**Prerequisites:** S-03 done (it is); API and SPA run locally with LocalDB.
**Estimated effort:** ~2-3 sessions across 4 phases.

## Open Risks & Assumptions

- Assumes `mat-select` multiple mode needs no new theme overrides; if it does, they go in `_material-theme.scss`.
- Assumes the filters component adds nothing to the initial bundle because the page is lazy; Phase 3 confirms this with a build.
- Error codes for rejected filter values follow the existing create-validation naming; exact names are the implementer's call.

## Success Criteria (Summary)

- A user can narrow the map by status and criticality, clear the filters, and the first view without filters is unchanged.
- Invalid filter values are rejected with 400, and the API, SPA and E2E suites pass.
- The initial bundle stays within budget and no CSP violations appear.
