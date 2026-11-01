---
date: 2026-11-01T16:37:58+01:00
researcher: Dominik Berniak
git_commit: 39d47f449393bbbfa0f759880d8d05fc11975835
branch: main
repository: 10xDevs
topic: "What exists today that S-06 (urgency-ranked nearby sighting list) builds on, and what it would touch"
tags: [research, codebase, sightings, nearby, urgency, list, filters, S-06]
status: complete
last_updated: 2026-11-01
last_updated_by: Dominik Berniak
last_updated_note: "User answered open questions 1-9 (Decisions D1-D11); follow-up findings on paging and the in-SQL radius filter"
---

# Research: S-06 urgency-ranked-sighting-list

**Date**: 2026-11-01T16:37:58+01:00
**Researcher**: Dominik Berniak
**Git Commit**: 39d47f449393bbbfa0f759880d8d05fc11975835
**Branch**: main
**Repository**: 10xDevs

## Research Question

S-06 (roadmap `context/foundation/roadmap.md:161-172`, PRD FR-020 `context/foundation/prd.md:74`) lets the user browse nearby sightings as a list ordered by urgency, with the same status and criticality filters as the map, resolved sightings hidden unless requested. What does the API and SPA provide after S-05 (commit `0bc71b1`), what conventions and tests constrain the change, and what is still undecided?

## Summary

- Observed: no urgency ordering exists. `GET sightings/nearby` sorts the in-radius candidates by `CreatedAt` descending and takes 200 (`PigeonWatch/Api/BusinessLogic/Services/SightingService.cs:85-89`). The repository also sorts by `CreateDate` descending before its 400-row cap (`PigeonWatch/Api/Data/Repositories/SightingRepository.cs:61-64`).
- Observed: the criticality rank is ready but unused. `ISightingStatusTransitionProvider.GetCriticalityRank` returns Monitor 1, NeedsHelpSoon 2, Urgent 3 (`BusinessLogic/Services/SightingStatusTransitionProvider.cs:35-40,62-68`), and no production code calls it (worker grep, this inspected Api tree). `PigeonWatch/Api/CLAUDE.md:9,39` names the API as owner of the urgency ranking and says to reuse the provider.
- Observed: the only time available for "time since reported" is `CreatedAt` (from the audit `CreateDate`, `Data/Mappers/SightingMapper.cs:35`). There is no status-changed timestamp on `SightingEntity` (`Data/Entities/SightingEntity.cs:6-28`) and no status-history table in the one SIGHTING migration (`Data/Migrations/20261011135944_AddSighting.cs:14-49`). No endpoint changes a status yet (`PigeonWatch/CLAUDE.md`, "no endpoint changes a status yet"), so every sighting is still in its initial status.
- Observed: "include resolved" is impossible today. With an empty status filter the service uses the five open statuses (`SightingService.cs:63-65,72`), and any resolved status value (6-9) in `statuses` is a 400 `InvalidFilterStatus` (`SightingService.cs:115-129`). The repository needs no change for it, because it applies whatever status list it receives (`SightingRepository.cs:49-53`).
- Observed: the S-05 filter contract (`statuses`, `criticalities` repeatable ints, applied in SQL before both caps) is reusable as is; the SPA wrapper is `nearby(centre, filters?)` (`Frontend/src/app/features/sightings/sightings-api.ts:20-32`).
- Observed: the SPA has no list view of nearby sightings. Results exist only as map markers; the map home is `path: ''` (`Frontend/src/app/app.routes.ts:7`). The one existing list of sightings is the profile's `my-sightings` component (row: name link, status, `app-criticality-badge`, `createdAt` date; `my-sightings.html:20-36`).
- Observed: the S-05 filter component (`nearby-map-page/nearby-map-filters.ts`) is page-local and uncontrolled (input `disabled` only, output `filtersChange`, `:27-28`), and the map page keeps filters and the last search centre as page state, "deliberately local … resets on every visit" (`Frontend/CLAUDE.md:45`).
- Inference: S-06 is the second user of both the filters and the sighting-row pattern, so `Frontend/CLAUDE.md:24` ("When a pattern shows up in a second feature, promote it to `shared/ui` in the same change") applies to both. The filters component uses resource keys and `sighting.types`, which the `shared/ui` key-free rule (`Frontend/CLAUDE.md:24`) forbids, so promotion needs a decision (key-free in `shared/ui`, or a shared component inside `features/sightings`).
- Inference: ordering by urgency only after the newest-400 SQL cap could drop old Urgent sightings in a dense box. At current volumes (PRD: "small-to-medium", `prd.md` Open Question 2) this is a documented limit rather than a blocker, but the plan must state where the ranking runs relative to the caps.

## Detailed Findings

### API: nearby path after S-05 (this inspected path only)

- Controller `GET sightings/nearby` (`WebApi/Controllers/SightingsController.cs:72-89`): `Cache-Control: private, no-store` (`:75`), maps filters with `NearbySightingsRequestMapper.MapFilters` (`:77`), calls the 5-argument `GetNearbyAsync(lat, lng, statuses, criticalities, ct)` (`:78`), returns a validation problem on failure (`:80-86`) and `Ok(CreateList(...))` on success (`:88`).
- Request model: `Latitude`, `Longitude` (`[BindRequired][Range]`), `List<int> Statuses = []`, `List<int> Criticalities = []` (`WebApi/Models/NearbySightingsRequestModel.cs:6-19`). There is no flag for resolved sightings.
- Service (`SightingService.cs:56-92`): open set = statuses where `!IsResolved` (`:63-65`); validation rejects any status outside the open set and any undefined criticality, collecting both errors (`:66-69`, `:115-129`; codes in `BusinessObjects/SightingErrorCodes.cs:9-10`); empty status filter becomes the open set (`:72`); repository call with `NearbyBoxMaxRows` (`:75-83`); haversine filter, `OrderByDescending(CreatedAt)`, `Take(NearbyMaxResults)` (`:85-89`).
- Repository (`SightingRepository.cs:35-67`): inclusive box, `statusList.Contains(e.Status)` always (`:49-53`), criticality predicate only when given (`:55-59`), `OrderByDescending(CreateDate).Take(take)` (`:61-64`).
- Rules: radius 10 km, `NearbyMaxResults` 200, `NearbyBoxMaxRows` = 2 × 200 (`BusinessObjects/SightingRules.cs:9-11`).
- Indexes: `(Latitude, Longitude)` and `(ReporterId, CreateDate)` only (`Data/PigeonWatchDbContext.cs:39`, as reported).

### API: what an item carries and what time exists

- `SightingModel` has Id, Name, Description, Behaviour, Latitude, Longitude, Status (int), Criticality (int), ReporterDisplayName, `CreatedAt` (UTC) (`WebApi/Models/SightingModel.cs:3-24`), inside `SightingListModel.Items` (`SightingListModel.cs:3-6`). No distance, urgency score or status-changed time. The service computes distance (`SightingService.cs:86`) and discards it.
- `TimeProvider` is registered (`DependencyInjection/DataServiceCollectionExtensions.cs:20`) and already injected into `SightingService` (`SightingService.cs:10`, used for the cooldown at `:31`). Unit tests fix "now" through `UnitTests/ManualTimeProvider.cs` (`SightingServiceTests.cs:14,17`); integration tests have their own `ManualTimeProvider`. A time-dependent urgency score is therefore testable with the existing seam.

### API: the transition provider

- `IsResolved` (resolved set exactly Adopted, Released, PassedAway, NotFound, `SightingStatusTransitionProvider.cs:21-27,52-55`) is called in production only at `SightingService.cs:64` (worker grep).
- `GetCriticalityRank` throws `ArgumentOutOfRangeException` for an undefined value (`:62-68`); unit tests pin ranks 1/2/3 and the throw (`UnitTests/SightingStatusTransitionProviderTests.cs:98-113`). `SightingServiceTests` uses the real provider, not a mock (`:435-438`).

### API: client configuration

- The SPA receives `NearbyRadiusKm` only, not the 200/400 caps (`WebApi/ViewModelCreators/ClientConfigurationViewModelCreator.cs:25-32`, `WebApi/Models/SightingRulesModel.cs:3-14`). Exposing a new rule (for example a time weight) touches `BusinessObjects/ClientConfiguration.cs`, `ClientConfigurationService.cs:9-21`, the creator, the model and `UnitTests/ConfigurationTests.cs:35-47`.

### API: tests a change touches

- Ordering: `SightingServiceTests.cs:259-276` (`Nearby_returns_at_most_200_sightings_newest_first`, all candidates share the default criticality) and `SightingRepositoryTests.cs:128-144` (newest-first plus take); `:193-216` pins take after the criticality predicate.
- Resolved handling: `SightingServiceTests.cs:365-385` (Theory 0, 6, 7, 8, 9, 42 rejected), `:405-419` (`[Adopted]` rejected), `:210-229` and `:315-339` (empty filter means open statuses); `SightingRepositoryTests.cs:107-125`.
- Signature: the 5-argument `GetNearbyAsync` appears at `SightingServiceTests.cs:218,238,252,267,284,296,322,346,375,394,409`, in NSubstitute matchers at `SightingsControllerTests.cs:160,183,198,209`, and `UnitTests/ControllerFailureLoggingTests.cs:186` builds the controller.
- Integration: `IntegrationTests/EndpointFixtureRegistry.cs:41` registers `GET sightings/nearby` with `BuildNearby` (`:199-213`, 7 fixtures including `statuses=6&criticalities=9` at `:210`); `EndpointCoverageTests.cs:8-49` requires one fixture key per routed endpoint, so a new route needs a new `Build*` entry; `PrivacySweep.cs:24-26` sweeps every fixture; `OwnershipContractTests.cs:202-235` compares the unfiltered result order-insensitively, `:238-255` pins the 400 for `statuses=6&criticalities=9`; `RequestLoggingTests.cs:49-58,74` pins the `sightings/nearby` route template.
- Architecture rules: actions return `Task<ActionResult<ApiModel>>` and API models expose no BusinessObjects or Data types (`ArchitectureTests/ControllerActionTests.cs:11,31`), so a request flag is `bool` or `int`; `TypeLocationTests.cs:9-19`, `CancellationTokenTests.cs:11`, `StaticClassTests.cs:14` also apply.
- Docs describing the contract: `PigeonWatch/Api/CLAUDE.md:25,37,39,40` and `PigeonWatch/CLAUDE.md` ("nearby" paragraph).

### SPA: routes, navigation and existing list UI

- Routes: `''` (map home, `authGuard`, `layout: 'wide'`, lazy) at `app.routes.ts:7`; `sightings/new` at `:32`; `sightings/:id` at `:41`. A new `sightings/<something>` route must be declared before `sightings/:id` or `:id` captures it.
- Header (`src/app/app.html`): brand link to `/` and a user menu with "Report sighting", Profile, Logout (as reported, `:6-8`, `:22-27`). No top-level nav bar; `nav.home` and `nav.mainNavigation` labels exist in resx but appear unused (as reported).
- `my-sightings` (`features/sightings/my-sightings/`): state union loading/error/loaded (`my-sightings.ts:19-22`), `reloads` Subject → `switchMap` pipeline (`:55-69`), error reporting with context `sightings.mine` (`:75-83`), row markup `my-sightings.html:20-36`, styles `my-sightings.scss:8-33`; embedded in the profile page (`profile-page.html:77`).

### SPA: the map page and the S-05 filters

- `nearby-map-filters.ts`: inputs `disabled` (`:27`), output `filtersChange: SightingFilters` (`:28`), internal signals `statuses`/`criticalities`/`active` (`:35-37`), `clear()` (`:41-47`), uses `provideFormFieldDefaults()` (`:21`) and `map.filters.*` keys. No input for initial values.
- Map page (`nearby-map-page.ts`): `searches` Subject of `{centre, filters}` (`:117`), `filters` signal (`:123`), plain `lastSearchCentre` field (`:129`), pipeline `switchMap → sightingsApi.nearby(centre, filters)` (`:219-234`), `changeFilters` (`:281-287`, skips the search before the first centre, when zoomed out, or when the map failed), geolocation through `locationRequests` → `GeolocationService.currentPosition(false)` (`:236-242`), `applyLocation` (`:320-343`), and a `status` computed coupled to map state (`zoomedOut`, `mapFailed`) (`:171-193`). Template wiring of the filters: `nearby-map-page.html:95-101`.
- The list has no map, so it has no map centre, zoom or "Search this area" (inference). Its centre must come from geolocation or be handed over from the map; with page-local state, switching between two routes loses both filters and centre (inference from `Frontend/CLAUDE.md:45`).

### SPA: types and labels

- `Sighting` (`sighting.types.ts:26-37`) has `createdAt: string` and no distance or urgency field; `SightingList` (`:39-41`). Enum values are 1-9 for status and 1-3 for criticality (`:4-22`, as reported), label maps and helpers (`:53-95`, as reported), `filterableSightingStatuses` = the five open statuses (`:108-114`), `SightingFilters` (`:116-119`). No list of resolved statuses and no rank or ordering helper.
- Labels: single `Api/BusinessObjects/Resources/UiLabels.resx`; `map.*` at `:408-481`, `map.filters.*` at `:483-508` (as reported). `core/resources/label-coverage.spec.ts` fails on a used key missing from the English snapshot and on literal text in templates outside `src/app/dev/` (only `·` allowed). Keys reached through `sighting.types` label maps are not matched by its regex (as reported).

### SPA: tests and budgets

- Page specs stub `GeolocationService` and `SightingsApi` with `vi.fn` and swap `MapView` for `MapViewStub` (`nearby-map-page.spec.ts:57-95`); `*-reporting.spec.ts` files assert error reporting per status (as reported).
- Kitchen sink: `cell-stubs.ts` (`NearbyMode` incl. `'filtered'`, `sinkSightings` at `:93-110`, all `status: spotted` with the same `createdAt`, so an ordering cell needs varied fixtures), `cell-definitions.ts` (`map-filtered`, `map-filtered-no-match` at `:294-322`, `cellGroups` at `:456-460`), `state-cell.html:27-36` switches on the group. `Frontend/CLAUDE.md:29` lists groups Profile, Nearby map, Sighting detail; a new page means a new group, modes interface, `state-cell` case and stub in its spec (as reported).
- Playwright: `tests/e2e/nearby-filters.spec.ts` registers its own user (avoids the per-user report cooldown), geolocates to 52.07/19.48, reports an Urgent sighting and asserts filter results by `waitForResponse`.
- Budgets: initial warning 520 kB, error 1 MB; `anyComponentStyle` 4 kB/8 kB (`angular.json:74-85`). The S-05 impl review measured the initial bundle at 516.31 kB (`context/archive/2026-11-01-filter-sightings-nearby/reviews/impl-review.md:22`), so about 3.7 kB of headroom; a lazy route does not add to it, a header link in `app.html` might.

## Code References

- `PigeonWatch/Api/BusinessLogic/Services/SightingService.cs:56-92,115-129` - nearby query, open set, filter validation, newest-first order
- `PigeonWatch/Api/Data/Repositories/SightingRepository.cs:35-67` - box query, status/criticality predicates, newest-first row cap
- `PigeonWatch/Api/BusinessObjects/SightingRules.cs:9-11` - radius and caps
- `PigeonWatch/Api/BusinessLogic/Services/SightingStatusTransitionProvider.cs:21-27,35-40,52-55,62-68` - resolved set, criticality rank
- `PigeonWatch/Api/WebApi/Models/NearbySightingsRequestModel.cs:6-19` - request model
- `PigeonWatch/Api/WebApi/Models/SightingModel.cs:3-24` - item fields
- `PigeonWatch/Api/IntegrationTests/EndpointFixtureRegistry.cs:41,199-213` - nearby fixtures
- `PigeonWatch/Frontend/src/app/app.routes.ts:7,32,41` - routes
- `PigeonWatch/Frontend/src/app/features/sightings/sightings-api.ts:20-32` - nearby wrapper
- `PigeonWatch/Frontend/src/app/features/sightings/sighting.types.ts:26-41,108-119` - types, open statuses, filters
- `PigeonWatch/Frontend/src/app/features/sightings/nearby-map-page/nearby-map-filters.ts:21-61` - filters component
- `PigeonWatch/Frontend/src/app/features/sightings/nearby-map-page/nearby-map-page.ts:117-129,219-287,320-343` - search state and pipeline
- `PigeonWatch/Frontend/src/app/features/sightings/my-sightings/my-sightings.html:20-36` - existing sighting row

## Architecture Insights

- Business rules live in the API (`Api/CLAUDE.md:9`: the API owns urgency-ranking logic); the SPA reads only the radius from client configuration. Ranking in the service keeps one rule for map and list, and the PRD says the urgency ordering is "what a rescuer sees on the map and any sighting list" (`prd.md:131`).
- Enums cross the wire as ints; API models avoid business types; a new request flag is a `bool`/`int`.
- `TimeProvider` is the time seam; a ranking that uses "now" fits it without new abstractions.
- The SPA promotes a pattern to `shared/ui` on its second use, and shared components are key-free; S-06 is that second use for the filters and for the sighting row.

## Historical Context (from prior changes)

- `context/archive/2026-10-11-report-sighting-on-map/plan-brief.md:34` - enums stored as ints, "compact and sortable for S-06", rank owned by BusinessLogic. Supported: the rank exists in the provider and is unused.
- `context/archive/2026-10-11-report-sighting-on-map/plan.md:96-98` - the provider was built so S-06 and S-09 reuse it. Supported.
- `context/archive/2026-10-11-report-sighting-on-map/plan-brief.md:25` - "nearby" = 10 km around the map centre, reused by S-05 and S-06. Supported (`SightingRules.cs:9`); roadmap S-06 risk repeats it (`roadmap.md:171`).
- `context/archive/2026-11-01-filter-sightings-nearby/plan-brief.md:22-23,28` - server-side filters "so S-06 can reuse the contract"; resolved statuses stay off the map and "include resolved" belongs to the S-06 list; filters stay local "until S-06 reuses it". Supported on the first and third points; the second is still a decision S-06 must make.
- `context/archive/2026-11-01-filter-sightings-nearby/reviews/impl-review.md` F1 - the collapsible filter panel, toggle and Clear button were added in implementation, recorded as a plan addendum, and the review notes S-06 is "expected to reuse these components"; F2 - filter changes skip the search while zoomed out or when the map failed (map-only conditions).
- `context/archive/2026-10-11-nearby-map-ui-contract/research.md:93` (D6) - the absence of a text list of results was deferred to S-06; the criticality badge and shared blocks were made for S-05/S-06 to consume (`:142`).
- Roadmap S-06 risk (`roadmap.md:171`): resolved sightings appear only once owners can change status in S-09, so the first version ranks on the reported status. Supported: no endpoint changes a status, so an "include resolved" option would return nothing extra until S-09.

## Related Research

- `context/archive/2026-11-01-filter-sightings-nearby/research.md` - S-05 filter contract and map page state.
- `context/archive/2026-10-11-report-sighting-on-map/research.md` - S-03 nearby scope and storage.
- `context/archive/2026-10-11-nearby-map-ui-contract/research.md` - map toolbar, badge and shared components.

## Open Questions

Questions 1-9 were answered by the user on 2026-11-01; the answers are in "Decisions (follow-up 2026-11-01)" below. They are kept here with their verdict.

1. ~~**Urgency formula**~~ - Decided D1: criticality, then oldest.
2. ~~**Where ranking runs and whether the map changes too**~~ - Decided D2: API, list only; the map stays newest-first.
3. ~~**Cap interaction**~~ - Decided D3 and D11: the list orders and pages in SQL with no row cap; the map's newest-400 cap is unchanged.
4. ~~**"Include resolved" contract**~~ - Decided D4: resolved values allowed in the list's `statuses`; the map keeps the S-05 400.
5. ~~**Endpoint shape**~~ - Decided D5: new `GET sightings/list`.
6. ~~**SPA placement**~~ - Decided D6: separate route with links both ways.
7. ~~**List centre without a map**~~ - Decided D7: centre and filters handed over in the URL from the map, otherwise geolocation.
8. ~~**Promotion**~~ - Decided D8: shared components inside `features/sightings`, used by the map, the list and `my-sightings`.
9. ~~**Row content**~~ - Decided D9: relative age, client-side.
10. Not verified by this research: `app.html` line numbers and unused `nav.*` labels, `label-coverage.spec.ts` internals, the rest of `nearby-map-page.spec.ts`, and current bundle size (no build run).
11. Open, for the plan: whether the EF Core 10 SQLite provider translates `Math.Sin`/`Math.Asin`/`Math.Pow` for an in-SQL haversine (not verified; see F2), or the radius predicate uses plain arithmetic.
12. Open, for the plan: page numbering (0- or 1-based), the response for a page past the end (empty page with `totalCount`, or 400), and the tie-break key for a stable order (D1 plus `Id`).
13. Open, for the plan: whether the list URL also carries `page` and `pageSize`, so back navigation from a detail page restores the position.

## Decisions (follow-up 2026-11-01)

The user answered the open questions in this session. Decisions, not findings; each names what it supersedes.

| # | Question | Decision |
| --- | --- | --- |
| D1 | Urgency formula (`roadmap.md:170`) | Criticality descending (Urgent, Needs help soon, Monitor), then oldest `CreatedAt` first within a level. No "now" and no tuning constant. |
| D2 | Where ranking runs | In the API, for the list only. `GET sightings/nearby` and the map keep newest-first (`SightingService.cs:85-89`) and its tests. Supersedes the Architecture Insights inference that one service rule should order both map and list; the PRD sentence that the order is "what a rescuer sees on the map and any sighting list" (`prd.md:131`) is applied to the list only, a scope narrowing to record in the plan. |
| D3 | Row cap before ranking | The list's SQL orders by the D1 key. Refined by D11: the list has no row cap at all. |
| D4 | Including resolved sightings | Resolved values (6-9) are valid in the list's `statuses`. The map's `GET sightings/nearby` keeps the S-05 400 `InvalidFilterStatus` for them (`SightingService.cs:115-129`), so its tests stay. |
| D5 | Endpoint shape | New `GET sightings/list` with its own request model. Needs a new `EndpointFixtureRegistry` entry (`EndpointCoverageTests.cs:8-49`) and privacy-sweep fixtures. |
| D6 | SPA placement | Separate lazy route (for example `/sightings/list`, declared before `sightings/:id`, `app.routes.ts:41`), with a "List view" link from the map page and a link back. |
| D7 | List centre | Opened from the map, the last search centre and the filters travel as URL query parameters; opened directly, the list uses `GeolocationService`, and with location denied it shows a message with a link to the map. `Frontend/CLAUDE.md:45` ("deliberately local") needs an update for the hand-over. |
| D8 | Promotion | The filters component and the sighting row move to a shared folder inside `features/sightings` (they use `sighting.types` and label keys, which `shared/ui` forbids, `Frontend/CLAUDE.md:24`). The map, the list and `my-sightings` use them. |
| D9 | Row content | Name, status, criticality badge and relative age ("reported 3 h ago") formatted in the SPA from `createdAt`; no API field. Needs new labels. |
| D10 | List status filter | The list's status filter offers all nine statuses; an empty selection means the five open statuses; choosing a resolved status includes it. The map's filter keeps the five open statuses (`sighting.types.ts:108-114`). |
| D11 | List size | Paginated server-side: `page` and `pageSize` query parameters, `pageSize` one of 10, 20, 30 (default 10), anything else a 400; the response carries the items and a `totalCount`. No total cap: ordering, radius filter, count and `Skip`/`Take` all run in SQL. Supersedes "200, same as map" and the newest-400 cap for the list. |

## Follow-up Findings (2026-11-01)

### F1: no paging exists in the codebase

- A search for `paginat`, `pageSize`, `PageIndex`, `TotalCount`, `MatPaginator` and `.Skip(` over `PigeonWatch/Api` and `PigeonWatch/Frontend/src` (`*.cs`, `*.ts`, `*.html`, excluding `bin`, `obj`, `node_modules`, `dist`) found no match. S-06 introduces the first paging contract on both sides: a paged response model (no existing one to reuse; `SightingListModel` is `Items` only, `SightingListModel.cs:3-6`), its validation and error code, and the SPA's first paginator (`mat-paginator` is not used anywhere in `Frontend/src`).
- Existing result caps are constants in `SightingRules` (`NearbyMaxResults` 200, `NearbyBoxMaxRows` 400, `MineMaxResults` 50, `BusinessObjects/SightingRules.cs:10-12`). The allowed page sizes would sit there too (inference from the convention).

### F2: the radius filter has to move into SQL for uncapped paging

- Today the box is filtered in SQL and the 10 km circle in memory with haversine (`SightingService.cs:85-86`, `BusinessLogic/Services/GeoDistanceCalculator.cs`, `GetDistanceKilometers` with `Math.Sin`, `Math.Cos`, `Math.Asin`, `Math.Pow`, `Math.Sqrt`). With D11 the count and `Skip`/`Take` must run over the circle, so the circle predicate must be translatable to SQL; otherwise `totalCount` and page boundaries include box corners outside 10 km.
- Providers: production uses SQL Server (`DependencyInjection/DataServiceCollectionExtensions.cs:30`, `Microsoft.EntityFrameworkCore.SqlServer` 10.0.12); integration and repository tests use SQLite (`IntegrationTests/PigeonWatchApiFactory.cs:148`, `UnitTests/SightingRepositoryTests.cs:326`, `Microsoft.EntityFrameworkCore.Sqlite` 10.0.12). Both must translate the predicate.
- Options (inference, not verified by running either provider):
  - Haversine in the LINQ predicate: relies on both providers translating the trig functions; SQLite translation also depends on the bundled SQLite build having math functions. Not verified (open question 11).
  - Equirectangular approximation with constants computed in C# from the centre (`dy = (lat - lat0) * kLat`, `dx = (lng - lng0) * kLng` with `kLng = kLat * cos(lat0)`, keep `dx*dx + dy*dy <= r*r`): plain arithmetic, so both providers can translate it. At a 10 km radius it can differ from haversine only near the circle edge, so a sighting right at 10 km could be on the map but not in the list, or the reverse.
- The bounding box predicate stays as the index-friendly prefilter (`(Latitude, Longitude)` index, `PigeonWatchDbContext.cs:39`). No index covers criticality or `CreateDate` within the box, so the D1 sort runs over the box rows (acceptable at the PRD's small-to-medium volumes, `prd.md` Open Question 2; inference).
- A stable page order needs a unique tie-break after D1 (for example `Id`), otherwise rows with equal criticality and `CreatedAt` can repeat or vanish between pages (open question 12).

### F3: tests and docs the decisions add or leave alone

- Unchanged by D2 and D4: the nearby ordering, cap and resolved-rejection tests listed under "API: tests a change touches".
- New: service and repository tests for the D1 order, D10 status handling and D11 paging (with the `ManualTimeProvider` seam not needed for D1, since it uses no "now"); controller tests; an `EndpointFixtureRegistry` builder for `GET sightings/list` with fixtures for paging and resolved statuses; `RequestLoggingTests` coverage if the new route is logged like nearby.
- SPA: list page spec with `GeolocationService` and `SightingsApi` stubs, a kitchen-sink group for the new page (`Frontend/CLAUDE.md:29`), and varied `createdAt`/criticality fixtures in `cell-stubs.ts` (all `sinkSightings` share one `createdAt` today); a Playwright test for the list.
- Docs: `PigeonWatch/Api/CLAUDE.md` (surface list and nearby section), `PigeonWatch/CLAUDE.md` (surface list), `Frontend/CLAUDE.md:45` (filter state and the shared components).
