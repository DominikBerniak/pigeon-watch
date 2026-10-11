# Report a sighting and see it on the map — Plan Brief

> Full plan: `context/changes/report-sighting-on-map/plan.md`
> Frame brief: `context/changes/report-sighting-on-map/frame.md`
> Research: `context/changes/report-sighting-on-map/research.md`

## What & Why

Ship roadmap slice S-03: a logged-in user reports a distressed-pigeon sighting, sees sightings around them on a map, sees their own on their profile, and opens a detail page, with a cooldown against spam.

> **The actual problem to plan around is**: S-03's map rests on three undecided contracts — what "around me" means when the map opens (including when position is unknown), how a map pin becomes a required, phone-usable form value, and which external host the `'self'`-only CSP will admit — not on which map library to use.

## Starting Point

Nothing sighting-related exists: the database has only `USER_ACCOUNT`, and the home page is a placeholder reserved for S-03. The frame found the library and build integration to be known, low-risk steps; the risk sat in the three contracts above, which this plan settles first.

## Desired End State

A member lands on a map centred on their position (or a neutral Poland view with a "Search this area" button if location is unavailable) showing open sightings within 10 km. They report a sighting by dropping a pin, choosing status and criticality, and describing the bird, and land on its detail page. Their profile lists their sightings, and a second report inside 60 seconds is refused with the seconds remaining.

## Key Decisions Made

| Decision | Choice | Why | Source |
| --- | --- | --- | --- |
| "Around me" | 10 km radius around the map centre | Fixed, simple, and reused by S-05 and S-06; the roadmap's S-05 unknown moves here | Frame + Plan |
| Unknown position | Neutral Poland view, no query until "Search this area" | One query shape for every case, and it serves report-from-memory users | Plan |
| Map and tiles | Leaflet 1.9.4 with OSM raster tiles | Smallest lazy chunk, no key, one new CSP host | Research + Plan |
| CSP | `img-src` gains only `https://tile.openstreetmap.org`, with a live no-violations check | Named security trade-off for a policy protecting the refresh token | Frame + Plan |
| Pin as form value | First custom `FormValueControl`, with tap/drag, "use my location" and "place at centre" | Frame found location capture was under-framed; "place at centre" gives a non-pointer path | Frame + Plan |
| Map placement | Map is the home route; two new lazy routes | The placeholder was reserved for it; guard spec stubs the map | Plan |
| Detail page | Coordinates as text plus opt-in "Show map" toggle | Honours the no-embedded-map-by-default guardrail while allowing in-app context | Plan |
| Cooldown | 60 s, DB check on latest `CREATE_DATE`, coded 400 with `retryAfterSeconds` | Survives F1 restarts and gives the "clear message" FR-019 needs | Plan |
| Geo storage | `double` columns, bounding box then exact distance in code | No new package; SQLite repository tests keep working | Plan |
| Enums | Integers in the DB and on the wire; rank owned by BusinessLogic | Compact and sortable for S-06; satisfies the no-BusinessObjects-on-models rule | Plan |
| Form controls | Native selects in `mat-form-field` | OS picker on phones, reuses existing error plumbing | Plan |
| Visual gate | Manual desktop and 375 px screenshots | First-time views go through the ordinary chain; `/10x-ui` follows | Plan |

## Scope

**In scope:** `SIGHTING` table and migration, status/criticality types with the full PRD transition graph, four `sightings` endpoints, sighting rules in client config, Leaflet map home, pin-picker, report form, detail page, profile "My sightings", header menu entry, CSP change, docs and roadmap update.

**Out of scope:** photos, filters and refined "nearby", urgency list, comments, messaging, owner edits and status changes, admin tools, antimeridian handling, NetTopologySuite, a full-width map layout, new kitchen-sink cells, remembering the last area, and a generalised cooldown service.

## Architecture / Approach

Backend first, since it is test-enforced and the SPA depends on its contract: domain and persistence, then service and API. The frontend then adds a thin Leaflet adapter (the only importer of Leaflet, outside the `shared/ui` barrel so it stays out of the initial bundle), the pin-picker, a geolocation service and the API client, and finally the four views. User text never enters Leaflet HTML: selecting a marker shows an Angular-rendered card instead of a popup.

## Phases at a Glance

| Phase | What it delivers | Key risk |
| --- | --- | --- |
| 1. Domain and persistence | Types, transition graph, `SIGHTING` table, repository | Naming and audit rules enforced by architecture tests |
| 2. Service and API | Cooldown, radius filter, endpoints, client config, labels | First 404, first `[FromQuery]`, `retryAfterSeconds` plumbing |
| 3. Frontend foundation | Leaflet build setup, CSP change, map adapter, pin-picker, geolocation, API client | Budget headroom (about 37 kB) and first custom Signal Forms control |
| 4. Frontend views | Map home, report form, detail, profile list, header entry | jsdom cannot render a map; Profile sink and guard spec need stubs |
| 5. Verification and docs | Live CSP check, screenshots, CLAUDE.md and roadmap updates | CSP cannot be checked on `ng serve` |

**Prerequisites:** S-01 and S-02 shipped (done); LocalDB with the `PigeonWatch` database for manual checks.
**Estimated effort:** about 5 sessions across 5 phases, with Phase 3 and Phase 4 the largest.

## Open Risks & Assumptions

- OSM standard tiles are best-effort with no SLA and a usage policy (attribution, no bulk use); the tile URL is one constant, so a swap touches that file and the CSP line.
- The cooldown is not atomic: two truly concurrent submissions can both pass. Accepted for a 60-second spam guard.
- The initial bundle has about 37 kB of headroom; Leaflet CSS in global styles uses about 14 kB raw, with an `inject: false` fallback if the warning trips.
- The plan keeps the map in the existing 40rem column; a wider layout is left to a later `/10x-ui` pass (this narrows the earlier phase outline, which mentioned a layout variant).
- Defaults chosen without asking: field lengths 60/1000/500, nearby cap 200, mine cap 50, nearby excludes resolved statuses, Poland default view at zoom 6, integer values 1-9 for status and 1-3 for criticality.

## Success Criteria (Summary)

- A member can report a sighting from a phone, see it on the map, on their profile, and on its detail page.
- A second report within 60 seconds is refused with a message that says how long to wait.
- The map loads tiles from the one allowed host with no CSP violations, and the production build stays under the 500 kB initial budget with Leaflet in lazy chunks only.
