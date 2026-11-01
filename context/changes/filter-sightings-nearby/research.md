---
date: 2026-11-01T13:10:00+01:00
researcher: Dominik Berniak
git_commit: c56a3aa20ed3fdbbf09bf126d6361496d0fc2793
branch: main
repository: 10xDevs
topic: "What exists today that S-05 (filter the nearby map by status and criticality) builds on, and what it would touch"
tags: [research, codebase, sightings, nearby, map, filters, S-05]
status: complete
last_updated: 2026-11-01
last_updated_by: Dominik Berniak
---

# Research: S-05 filter-sightings-nearby

**Date**: 2026-11-01T13:10:00+01:00
**Researcher**: Dominik Berniak
**Git Commit**: c56a3aa20ed3fdbbf09bf126d6361496d0fc2793
**Branch**: main
**Repository**: 10xDevs

## Research Question

S-05 (roadmap `context/foundation/roadmap.md:148-159`, PRD FR-004) lets the user see the map scoped to the nearby area by default and filter sightings by status and criticality. What does the current API and SPA provide for this, what conventions and tests constrain a change, and what is still undecided?

## Summary

- Observed: no status or criticality filtering exists anywhere. The nearby endpoint takes only latitude and longitude (`PigeonWatch/Api/WebApi/Models/NearbySightingsRequestModel.cs:6-15`), and the SPA sends only those two (`PigeonWatch/Frontend/src/app/features/sightings/sightings-api.ts:20-24`).
- Observed: the "nearby default" half of FR-004 is already delivered by S-03 (10 km radius, `SightingRules.NearbyRadiusKilometers`, `PigeonWatch/Api/BusinessObjects/SightingRules.cs:9`). S-05 is the filters plus any refinement of that rule.
- Observed: the server already hard-codes a status filter. `SightingService.GetNearbyAsync` passes the non-resolved statuses (Spotted, Caught, AtTheVet, InHomeCare, AwaitingAdoption) to the repository (`PigeonWatch/Api/BusinessLogic/Services/SightingService.cs:59-70`), and the repository already accepts a status list (`PigeonWatch/Api/Data/Repositories/SightingRepository.cs:48-52`). There is no criticality predicate on the query path.
- Observed: the map page keeps no filter state; the page loads through one `searches` pipeline keyed on the centre (`nearby-map-page.ts:192-203`).
- Inference: filtering can be done server-side (new optional query parameters, reuses the repository's status list) or client-side over the up-to-200 loaded items. The server-side route changes the 200/400 caps' meaning (they would apply after the filter); the client-side route does not. This is a planning decision, not settled by any document.
- Correction to an earlier note: the roadmap's S-02 risk text cites a 500 kB initial budget (`roadmap.md:121`); `angular.json:74-85` sets the production initial budget to warning 520kB, error 1MB.

## Detailed Findings

### API: nearby query path (this inspected path only)

- Controller `GET sightings/nearby` binds `NearbySightingsRequestModel` and calls `GetNearbyAsync(latitude, longitude, ct)`; `[Authorize]`, `Cache-Control: private, no-store` (`SightingsController.cs:69-77`, `:15`, `:22`).
- Request model has exactly two fields, both `required double` with `[BindRequired]` and `[Range]` (`NearbySightingsRequestModel.cs:6-15`). Invalid input yields the `[ApiController]` automatic 400.
- Service: bounding box, then repository, then exact great-circle filter, `OrderByDescending(CreatedAt)`, `Take(NearbyMaxResults)` (`SightingService.cs:56-77`). Caps: `NearbyMaxResults=200`, `NearbyBoxMaxRows=400` (`SightingRules.cs:10-11`).
- Cap interaction: the repository takes the newest 400 rows in the box before the circle filter, then the service trims to 200 (`SightingRepository.cs:35-58`, `SightingService.cs:63-75`). A criticality predicate pushed into the SQL query would apply before these caps; one applied after would not.
- Indexes: only `(Latitude, Longitude)` and `(ReporterId, CreateDate)` (`PigeonWatchDbContext.cs:38-39`). No index on status or criticality.
- Resolved-status set: Adopted, Released, PassedAway, NotFound (`SightingStatusTransitionProvider.cs:21-27`); `IsResolved` at `:52`. Resolved sightings are therefore never returned today, so filtering by a resolved status cannot return anything without relaxing that rule (FR-020 for the list view says resolved are "excluded by default but can be included on request", `prd.md:74`; FR-004 does not say this for the map).

### API: types and wire format

- `SightingStatus` 1-9 and `SightingCriticality` Monitor=1, NeedsHelpSoon=2, Urgent=3 (`BusinessObjects/SightingStatus.cs:3-14`, `SightingCriticality.cs:3-8`). Stored as `int` columns, no converter (`Data/Migrations/20261011135944_AddSighting.cs:25-26`).
- JSON uses plain ints for both, in responses and the create request (`WebApi/Models/SightingModel.cs:17,19`, `CreateSightingRequestModel.cs:23,25`); no `JsonStringEnumConverter` found in the inspected WebApi, WebApi.Host and DependencyInjection projects (`Program.cs:18`).
- Validation exists only on create (`SightingService.cs:84-98`); nothing validates status or criticality on read. An architecture test forbids business or data types in API models (`ArchitectureTests/ControllerActionTests.cs:31`), so a filter request model would use ints, as `CreateSightingRequestModel` does.
- `ISightingStatusTransitionProvider.GetCriticalityRank` exists (`SightingStatusTransitionProvider.cs:62`) but nothing in the nearby path uses it.

### API: client configuration

- Only `NearbyRadiusKm` reaches the SPA; the 200 and 400 caps do not (`ClientConfigurationViewModelCreator.cs:25-32`, `SightingRulesModel.cs:13`). `ConfigurationTests.cs:36-47` asserts each rule field, so a new rule field needs a line there.

### API: tests a change touches

- `SightingServiceTests.cs:211` asserts the exact `ListInBoxAsync` argument list; any signature change breaks it and the `CandidatesInBox` helper (~:296). Further nearby tests at `:231`, `:243`, `:259`.
- `SightingsControllerTests.cs:156-171` stubs `GetNearbyAsync(52.1, 21.1, ...)` with literals and builds the request model with two properties; new required members or parameters break it.
- `SightingRepositoryTests.cs` calls `ListInBoxAsync(...)` at `:99`, `:121`, `:139`, `:140` with a static `openStatuses` array (`:17-18`).
- `IntegrationTests/EndpointFixtureRegistry.cs:41` registers `GET sightings/nearby`; `BuildNearby` (`:199-209`) has four fixture requests. A new query parameter on the same route needs no new key; filter fixtures would go in `BuildNearby`. `OwnershipContractTests.cs:17` and `TestUser.cs:29-34` (status and criticality as int parameters) support seeding filter cases.
- Documentation to update if the contract changes: `PigeonWatch/Api/CLAUDE.md:25,37,40` and `PigeonWatch/CLAUDE.md:16` ("up to 200 newest open sightings within 10 km").

### SPA: nearby map page

- Standalone OnPush page, lazy route (`nearby-map-page.ts:73-91`, `app.routes.ts:12`). State: `NearbyState = idle | loading | error | loaded{items}` (`:48-52`); `sightings` holds the last loaded list and is overwritten only on success (`:120`, `applyState` `:261-265`).
- One `searches` Subject feeds `switchMap(centre => sightingsApi.nearby(centre)...)` (`:192-203`); `search(centre)` stores `lastSearchCentre` (`:308-313`) and `retry()` replays it (`:239-244`). Geolocation and "Search this area" both go through `search()`.
- `status` computed decides `found` versus `empty` from `state.items.length` (`:148-166`, `:163`); the found text uses `sightings().length` (`nearby-map-page.html:60-62`), empty text uses the radius (`:63-65`). With a filter, "empty" would need to distinguish "nothing nearby" from "nothing matches the filter" (inference; no existing label covers it).
- Markers come from the `markers` computed over `sightings()` (`:170-184`); `selected` looks up `selectedId` in `sightings()` (`:185-189`), so a filter that hides the selected sighting needs a selection rule (inference).
- The legend is a read-only list of criticality badges (`nearby-map-page.html:90-101`, `legendCriticalities` `:65-69`). A review note says S-05 builds on the map toolbar (`context/archive/2026-10-11-nearby-map-ui-contract/follow-ups/review-fixes.md:19`, as reported by a worker; that file was not opened by this research, though its archive folder exists).

### SPA: types, labels and reusable controls

- `sighting.types.ts`: `SightingStatus` const object 1-9 (`:4-16`), `SightingCriticality` (`:18-24`), label maps `sightingStatusLabels` (`:53-63`) and `sightingCriticalityLabels` (`:65-69`), `selectableSightingCriticalities` (`:102-106`), `initialSightingStatuses` (`:97-100`). There is no exported list of the five open statuses.
- Labels live in `PigeonWatch/Api/BusinessObjects/Resources/UiLabels.resx` (status keys `:300-324`, criticality `:327-333`, map keys `:408-480`) and are generated into per-culture JSON by `scripts/generate-resources.mjs` on `prestart`, `prebuild`, `pretest` (`package.json:6-11`). `core/resources/label-coverage.spec.ts` fails on a used key missing from the English snapshot and on literal text in templates, so new UI text needs resx keys.
- No chip, checkbox-group or toggle-group component exists in `shared/ui` (`shared/ui/index.ts`); the only Material select is in the report form (`report-sighting-page.html:53-92`, `mat-select` with `pw-select-panel`, `provideFormFieldDefaults()`). `styles/_material-theme.scss:72` has select overrides only; no chips, checkbox or button-toggle overrides were found by grep.
- Frontend rules from `PigeonWatch/Frontend/CLAUDE.md` (as reported): tokens only (`--pw-*`), no `.mat-*` selectors or `::ng-deep`, variants as component inputs with Material tweaks in `_material-theme.scss`, patterns used in a second feature promoted to `shared/ui`, `provideFormFieldDefaults()` deliberately not in `app.config.ts`.
- Budgets: `anyComponentStyle` warning 4kB, error 8kB (`angular.json:74-85`); `nearby-map-page.scss` is 71 lines. The page is lazy, so Material imports added there do not count toward the initial bundle (inference from `app.routes.ts:12`). No build was run; current sizes are unknown.

### SPA: test conventions

- `nearby-map-page.spec.ts` (718 lines) uses Vitest with stubs for `GeolocationService` and `SightingsApi` and a `MapViewStub` (`shared/map/testing/map-view-stub.ts`); existing tests that a filter would affect: result-count status (`:380`), empty state (`:386`), keep-markers-while-loading and clear selection (`:349`), legend (`:428`), selected card (`:443`).
- Kitchen sink (`dev/kitchen-sink`): `nearbyMapCells` at `cell-definitions.ts:193-340`; a new page state needs a cell plus a marker assertion in `kitchen-sink.spec.ts`, and a new data dependency needs a stub mode in `cell-stubs.ts` (`NearbyMode` at `:24`, `CellStubs.nearby` at `:183`) and the same stub in the page spec (per `Frontend/CLAUDE.md:29`, as reported).
- Playwright: `tests/e2e/seed.spec.ts` covers reporting and finding the marker only; it has no filter coverage. `error-reporting.spec.ts` forces a 500 on `sightings/nearby`.

## Code References

- `PigeonWatch/Api/WebApi/Controllers/SightingsController.cs:69-77` - nearby action
- `PigeonWatch/Api/WebApi/Models/NearbySightingsRequestModel.cs:6-15` - two-field request model
- `PigeonWatch/Api/BusinessLogic/Services/SightingService.cs:56-77` - nearby query, hard-coded open statuses
- `PigeonWatch/Api/Data/Repositories/SightingRepository.cs:35-58` - box query with status list
- `PigeonWatch/Api/BusinessObjects/SightingRules.cs:3-13` - radius and caps
- `PigeonWatch/Api/BusinessLogic/Services/SightingStatusTransitionProvider.cs:21-27,52,62` - resolved set, `IsResolved`, `GetCriticalityRank`
- `PigeonWatch/Frontend/src/app/features/sightings/sightings-api.ts:20-24` - nearby call
- `PigeonWatch/Frontend/src/app/features/sightings/nearby-map-page/nearby-map-page.ts:119-203,261-313` - state, search pipeline
- `PigeonWatch/Frontend/src/app/features/sightings/sighting.types.ts:4-106` - types, labels, lists
- `PigeonWatch/Frontend/src/app/features/sightings/report-sighting-page/report-sighting-page.html:53-92` - existing `mat-select` pattern
- `PigeonWatch/Frontend/angular.json:74-85` - budgets

## Architecture Insights

- "Nearby" is defined once on the server (`SightingRules`) and the SPA reads only the radius from client configuration; S-06 will reuse the same rule and the same filters (`roadmap.md:158,171`), which argues for the filter contract being shared rather than page-local (inference).
- Enums cross the wire as ints and API models avoid business enums; label keys are the only place names appear in the SPA.
- The page is a single state machine over one search pipeline; filters add a second input to that pipeline (server-side) or a derived view over `sightings()` (client-side).

## Historical Context (from prior changes)

- `context/archive/2026-10-11-report-sighting-on-map/frame.md:34` - the S-05 open unknown (radius vs named city) was effectively answered in S-03 as "around the user's position"; S-05 is filters plus any refinement. Supported by `roadmap.md:157-158`.
- `context/archive/2026-10-11-report-sighting-on-map/plan.md:57-67` - S-03 fixed the radius at 10 km, resolved statuses excluded from nearby, cap 200 newest open sightings, storage as `double` lat/lng with a bounding-box prefilter. The original `plan.md:471` notes a spatial index as the escape hatch only if volumes outgrow the in-memory filter.
- `context/archive/2026-10-11-nearby-map-ui-contract/` and `context/archive/2026-10-17-nearby-map-review-fixes/` exist (folder names only inspected); their contents were not read beyond the review note cited above.

## Related Research

- `context/archive/2026-10-11-report-sighting-on-map/research.md` - map provider, storage and nearby-scope exploration for S-03.

## Open Questions

1. Server-side or client-side filtering? Server-side changes the nearby contract (new optional query parameters), the `ListInBoxAsync` signature and its tests; client-side changes only the SPA but filters within at most 200 loaded items.
2. Default filter state: all five open statuses and all three criticalities (current behaviour), or something narrower? FR-004 says only "filterable by status and criticality".
3. Should the map offer resolved statuses at all? FR-020 allows including resolved sightings on request for the list; FR-004 is silent for the map, and the nearby API never returns them today.
4. Roadmap unknown still open (non-blocking): a user-changeable radius or a named city (`roadmap.md:157`).
5. Whether the filter controls should be a shared component now (S-06 needs the same filters) or page-local first; `Frontend/CLAUDE.md` promotes patterns on their second use.
6. Not verified by this research: `Sighting.cs`, `NewSighting` helper in `SightingRepositoryTests.cs`, the automatic-400 body format, `PrivacySweepTests` and `EnumerationContractTests` behaviour on new query parameters, current initial bundle size, and the contents of the two nearby-map archive folders.
