# Urgency-Ranked Sighting List (S-06) — Plan Brief

> Full plan: `context/changes/urgency-ranked-sighting-list/plan.md`
> Research: `context/changes/urgency-ranked-sighting-list/research.md`

## What & Why

Rescuers can see nearby sightings only as map markers, newest first. S-06 (FR-020) adds a list of nearby sightings ordered by urgency, with the map's status and criticality filters, resolved sightings hidden unless the user asks for them, so the most critical birds are at the top.

## Starting Point

`GET sightings/nearby` filters a box in SQL, applies the 10 km haversine radius in memory and returns up to 200 newest sightings; resolved statuses are rejected. The criticality rank exists in `SightingStatusTransitionProvider` but nothing uses it. The SPA has no list view; the filter row is local to the map page and My sightings has its own row markup. No paging exists anywhere.

## Desired End State

A "List view" link on the map opens `/sightings/list` at the map's search centre with its filters. The list shows Urgent first, then Needs help soon, then Monitor, oldest report first within each level, with name, status, criticality badge and relative age ("3 h ago"), 10/20/30 per page. Users can include resolved statuses through the filter. Opened directly, the list uses the device location. The map is unchanged.

## Key Decisions Made

| Decision | Choice | Why (1 sentence) | Source |
| --- | --- | --- | --- |
| Urgency order | Criticality desc, then oldest `CreatedAt`, then `Id` | Simple, explainable, no tuning constant; `Id` keeps pages stable | Research D1 / Plan |
| Where ranking runs | API, list only; map stays newest first | Business rule owned by the API without changing the map | Research D2 |
| Resolved statuses | All nine valid in the list's filter; empty = five open statuses; map still rejects them | "Hidden unless requested" without touching the map contract | Research D4, D10 |
| Endpoint | `POST sightings/list` with a JSON body, returns `{ items, totalCount }` | User's choice: filters and paging in a body, not a query string (supersedes D5's GET) | Plan |
| Size | Paged in SQL, no cap; page 1-based, size 10/20/30 (default 10) | Every in-radius sighting reachable | Research D11 / Plan |
| Past last page | 200 with empty items and real `totalCount`; SPA steps to the last page | No error when sightings vanish between requests | Plan |
| Radius in SQL | Haversine as `h ≤ sin²(r/2R)` using only `Sin`/`Cos`; SQLite proof first, equirectangular fallback | List and map agree exactly on the 10 km edge | Plan |
| SPA placement | Separate lazy route `/sightings/list` with links both ways | List and map stay simple, separate pages | Research D6 |
| List state | Centre and filters via router navigation state; page in memory; no URL params | User's choice: clean URLs (supersedes D7's URL hand-over) | Plan |
| Back to map | Plain link; map state stays page-local | No change to the map's state model | Plan |
| Shared UI | `features/sightings/shared/` filters and row, used by map, list, My sightings | Second use of both; `shared/ui` forbids keys and feature types | Research D8 |
| Row time | Relative age everywhere, absolute date in `<time>`/`title` | One row without a variant input | Research D9 / Plan |

## Scope

**In scope:**
- `POST sightings/list` (validation, error codes `InvalidPage`, `InvalidPageSize`), in-SQL radius, paged query, tests, fixtures, API docs
- Shared `app-sighting-filters` and `li[app-sighting-row]`, relative age labels; map and My sightings switched over
- `/sightings/list` page with hand-over, geolocation fallback, filters, `mat-paginator` with resx labels, kitchen-sink group, specs, docs
- One Playwright test for map → list

**Out of scope:**
- Any change to `GET sightings/nearby` or the map's order and caps
- Time weighting in the rank, status-changed timestamps, migrations, indexes
- Distance in rows, URL state for the list, map restoring list state, tab title/focus on the list

## Architecture / Approach

`SightingsController.List` → `SightingService.GetListPageAsync` (validation, open-status default, `GeoDistanceCalculator.GetRadiusFilter`) → `SightingRepository.ListPageInRadiusAsync` (box + haversine + status/criticality predicates, `Count`, order, `Skip`/`Take` in SQL). The SPA list page resolves a centre (navigation state from the map, else geolocation), keeps filters and page in memory, and calls `SightingsApi.list` through a `switchMap` pipeline.

## Phases at a Glance

| Phase | What it delivers | Key risk |
| --- | --- | --- |
| 1. API data | In-SQL radius + urgency-ordered paged repository query | SQLite or SQL Server not translating `sin`/`cos` |
| 2. API endpoint | `POST sightings/list` with validation, tests, fixtures, docs | Fixture/privacy sweep gaps for a body endpoint |
| 3. SPA shared components | Shared filters and row; My sightings shows relative age | Regressing the map filters during the move |
| 4. SPA list page | Route, page, hand-over, paginator, links, kitchen sink | Untrusted navigation state; paginator English text |
| 5. E2E | Playwright map → list test | Cooldown and geolocation set-up in the browser |

**Prerequisites:** S-03 and S-05 shipped (they have); LocalDB for the SQL Server manual check.
**Estimated effort:** ~3-4 sessions across 5 phases.

## Open Risks & Assumptions

- SQLite math functions are assumed present in the bundled build; Phase 1's first test decides, with the equirectangular predicate as the fallback.
- SQL Server translation is verified manually only (tests run on SQLite).
- Ordering by the stored criticality value assumes it equals the provider's rank; a unit test guards it.
- Before S-09 no sighting is resolved, so choosing a resolved status shows no extra results.
- No cap: list queries scan in-box rows without an index on criticality/date; fine at the PRD's volume.

## Success Criteria (Summary)

- From the map, a user sees nearby sightings as a list with the most urgent and longest-waiting first, filterable like the map, including resolved statuses on request.
- Every in-radius sighting is reachable through paging, and list and map agree on what is within 10 km.
- The map and its API behave exactly as before.
