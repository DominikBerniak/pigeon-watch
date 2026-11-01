# Urgency-Ranked Sighting List (S-06) Implementation Plan

## Overview

Add a list view of nearby sightings ordered by urgency (FR-020, roadmap S-06). The API gets `POST sightings/list`: sightings within the 10 km radius, ordered by criticality (Urgent first), then oldest report first, filtered by status and criticality, with resolved statuses allowed on request, paged in SQL with a `totalCount`. The SPA gets a `/sightings/list` page that the map opens with its search centre and filters, built on a filter component and a sighting row promoted into `features/sightings/shared/` and reused by the map and My sightings.

## Current State Analysis

- `GET sightings/nearby` filters the bounding box in SQL, keeps the 10 km circle in memory with haversine, orders newest first and takes 200 (`PigeonWatch/Api/BusinessLogic/Services/SightingService.cs:56-92`, `Data/Repositories/SightingRepository.cs:35-67`). There is no urgency ordering anywhere.
- `ISightingStatusTransitionProvider.GetCriticalityRank` (Monitor 1, NeedsHelpSoon 2, Urgent 3) exists for S-06 and is unused (`BusinessLogic/Services/SightingStatusTransitionProvider.cs:35-40,62-68`). The enum values are the same numbers (`BusinessObjects/SightingCriticality.cs`).
- The only time on a sighting is `CreatedAt` (audit `CreateDate`); no endpoint changes a status yet, so resolved sightings exist only once S-09 ships.
- Resolved statuses are a 400 `InvalidFilterStatus` on nearby (`SightingService.cs:115-129`); the repository applies whatever status list it gets (`SightingRepository.cs:49-53`).
- No paging exists in the API or SPA (research F1). `SightingListModel` is `Items` only.
- `GeoDistanceCalculator.GetDistanceKilometers` uses `Math.Sin/Cos/Asin/Pow/Sqrt` and `Math.Min` (`BusinessLogic/Services/GeoDistanceCalculator.cs`). Production runs SQL Server; repository and integration tests run SQLite (`Microsoft.EntityFrameworkCore.Sqlite` 10.0.12).
- The SPA has no list of nearby sightings. `app-nearby-map-filters` is page-local, uncontrolled and hard-codes the five open statuses (`Frontend/src/app/features/sightings/nearby-map-page/nearby-map-filters.ts:27-37`). My sightings renders its own row with an absolute `date: 'medium'` (`my-sightings/my-sightings.html:20-36`). The map holds `filters` and `lastSearchCentre` as page state (`nearby-map-page.ts:123,129`), deliberately local (`Frontend/CLAUDE.md`, "Nearby filters").

## Desired End State

- `POST sightings/list` with body `{ latitude, longitude, statuses?, criticalities?, page?, pageSize? }` returns `{ items, totalCount }`: sightings within exactly 10 km (the same haversine rule as the map), ordered criticality descending, `CreateDate` ascending, `Id` ascending; `page` 1-based (default 1), `pageSize` one of 10/20/30 (default 10). Empty `statuses` means the five open statuses; any of the nine statuses is valid. Invalid page, page size, status or criticality values are 400 with error codes; a page past the end is 200 with empty `items` and the real `totalCount`.
- `GET sightings/nearby` and the map behave exactly as before (newest first, 200 cap, resolved statuses rejected).
- The map toolbar has a "List view" link that opens `/sightings/list` carrying the last search centre and filters as router navigation state. The list page uses that centre, or geolocation when opened without it; with no location it shows a message and a link to the map. It shows the filters (all nine statuses), rows with name, status, criticality badge and relative age, and a paginator (10/20/30). A plain link returns to the map.
- My sightings and the list share one row component; My sightings now shows relative age, with the absolute date in the `<time>` element's `datetime` and `title`.
- Verify: the API and SPA test suites, the integration endpoint sweep, the production build and a Playwright test pass; manual checks against LocalDB (SQL Server) confirm the in-SQL radius works there.

### Key Discoveries:

- The haversine membership test needs no `Asin`, `Sqrt`, `Pow` or `Math.Min` in SQL: `2R·asin(√h) ≤ r` is equivalent to `h ≤ sin²(r / 2R)`, so only `Sin` and `Cos` of the row columns appear in the predicate, and the threshold and `cos(centre latitude)` are constants computed in C#.
- `Data` may reference only `BusinessObjects` (`PigeonWatch/Api/CLAUDE.md`, Architecture), so the radius constants travel to the repository as a business object, and the repository cannot call the transition provider; ordering by the stored criticality `int` is only correct while the enum values equal the provider's ranks.
- `EndpointCoverageTests` requires a fixture key per routed endpoint (`IntegrationTests/EndpointFixtureRegistry.cs:30-46`); the registry already has `JsonOf(HttpMethod.Post, …)` and `AddMalformed` for body endpoints.
- `API models expose no BusinessObjects types` (`ArchitectureTests/ControllerActionTests.cs:31`); request and response fields are `int`/`double`/lists of `int`.
- `mat-paginator` ships English text through `MatPaginatorIntl`, which would break the "every UI text is a resx key" rule (`Frontend/CLAUDE.md`, UI text and resources) unless the page provides its own intl.
- `label-coverage.spec.ts` matches only literal keys in `translate('…')` calls and the pipe; keys chosen at runtime (age keys) need their own existence assertion, as `sighting.types.spec.ts` does against `defaultLabels`.
- A new `sightings/list` route must precede `sightings/:id` (`app.routes.ts:41`). The list is a lazy chunk, so the initial bundle (516.3 kB, 3.7 kB under the warning) is not touched as long as nothing under `core/` or `app.ts` changes.

## What We're NOT Doing

- No change to `GET sightings/nearby`, the map's ordering, its 200/400 caps or its resolved-status rejection (research D2, D4). The PRD sentence that urgency is "what a rescuer sees on the map and any sighting list" (`prd.md:131`) is applied to the list only.
- No time weighting, decay or "now" in the ranking (D1). No status-changed timestamp, migration or index.
- No distance in the row and no distance ordering.
- No page, page size, centre or filters in the SPA URL; list paging resets to page 1 on every visit, including Back from a sighting detail. The hand-over survives Back (it is in the history entry) but not a reload in a new tab or a bookmark, which fall back to geolocation.
- The link from the list back to the map is plain: the map keeps its page-local filter state and does not read a hand-over.
- No "Locate me" or "Search this area" on the list page; no map on the list page.
- No client-configuration exposure of page sizes; the SPA mirrors 10/20/30 as a constant, like `filterableSightingStatuses`.
- No tab title or heading-focus arrival behaviour for the list page (only the detail page has it today).
- No promotion to `shared/ui`: the filters and row use `sighting.types` and resource keys, which `shared/ui` forbids (D8).
- No renaming of the existing `map.filters.*` keys used by the shared filters component.

## Implementation Approach

Build bottom-up so each phase is testable on its own: first prove the in-SQL radius on SQLite and write the paged query, then put the endpoint on top, then refactor the SPA's existing filter and row into shared components with no new page, then add the list page, then cover the user flow in Playwright. The map's behaviour is a regression boundary throughout: its tests stay unchanged except for the component rename.

## Critical Implementation Details

- **Radius predicate translation.** Phase 1 starts with a SQLite repository test that places sightings just inside and just outside 10 km and runs the new query; it is the proof that EF Core translates `Math.Sin`/`Math.Cos` and that the bundled SQLite has math functions. If it cannot translate, switch the predicate to the equirectangular form (`dx = Δlng·cos(lat₀)`, `dy = Δlat`, in radians, `dx² + dy² ≤ (r/R)²`, plain arithmetic) and record the deviation in the plan. SQL Server translation is checked manually against LocalDB in Phase 2.
- **Rank equals stored value.** SQL orders by the `CRITICALITY` column, so a unit test pins that ordering `SightingCriticality` values ascending gives the same order as `GetCriticalityRank` ascending; renumbering an enum or a rank then fails a test instead of silently mis-ordering the list.
- **Hand-over state is untrusted input.** `history.state` can hold anything (an older app version, a hand-edited entry), so the list page accepts it only through a type guard (finite latitude in [-90, 90], longitude in [-180, 180], arrays of known status and criticality values) and otherwise behaves as if opened directly.

## Phase 1: API data — in-SQL radius and urgency-ordered paged query

### Overview

Give the repository a query that applies box, exact radius, status and criticality filters in SQL, orders by urgency with a stable tie-break, counts and pages, plus the business objects and geo constants it needs.

### Changes Required:

#### 1. Radius filter business object

**File**: `PigeonWatch/Api/BusinessObjects/GeoRadiusFilter.cs`

**Intent**: Carry everything the repository needs to evaluate "within r km of the centre" in SQL, so the formula's constants are computed once in BusinessLogic and `Data` only plugs them in.

**Contract**: `sealed record GeoRadiusFilter(GeoBoundingBox Box, double CentreLatitudeRadians, double CentreLongitudeRadians, double CosCentreLatitude, double HalfChordThreshold)`, where `HalfChordThreshold = sin²(radiusKm / (2 · earthRadiusKm))`.

#### 2. Page business objects

**File**: `PigeonWatch/Api/BusinessObjects/SightingPage.cs`, `SightingListQuery.cs`, `SightingListResult.cs`

**Intent**: A page of sightings with the total match count; the list query as the service receives it; the service result with success/failure, following `SightingNearbyResult`.

**Contract**: `SightingPage(IReadOnlyList<Sighting> Items, int TotalCount)`; `SightingListQuery(double Latitude, double Longitude, IReadOnlyCollection<SightingStatus> Statuses, IReadOnlyCollection<SightingCriticality> Criticalities, int Page, int PageSize)`; `SightingListResult(bool Succeeded, IReadOnlyList<SightingError> Errors, SightingPage Page)` with `Success(page)` / `Failure(errors)` (failure carries an empty page).

#### 3. Radius constants from the calculator

**File**: `PigeonWatch/Api/BusinessLogic/Services/GeoDistanceCalculator.cs` and `Interfaces/IGeoDistanceCalculator.cs`

**Intent**: Add `GetRadiusFilter(latitude, longitude, radiusKilometers)` that returns the existing bounding box plus the constants above, using the same earth radius as `GetDistanceKilometers`, so list and map share one definition of the circle.

**Contract**: `GeoRadiusFilter GetRadiusFilter(double latitude, double longitude, double radiusKilometers)`. Unit tests: the box equals `GetBoundingBox` for the same input; for a set of points around a centre (including ones at 9.99 and 10.01 km), `h ≤ HalfChordThreshold` agrees with `GetDistanceKilometers(...) <= 10`.

#### 4. Paged repository query

**File**: `PigeonWatch/Api/Data/Repositories/SightingRepository.cs` and `Interfaces/ISightingRepository.cs`

**Intent**: New method that filters by box, exact radius, statuses (always a non-empty list from the service) and criticalities (only when given), counts the matches, then orders and pages them in SQL. `ListInBoxAsync` stays as is for nearby.

**Contract**: `Task<SightingPage> ListPageInRadiusAsync(GeoRadiusFilter radius, IReadOnlyCollection<SightingStatus> statuses, IReadOnlyCollection<SightingCriticality> criticalities, int skip, int take, CancellationToken cancellationToken = default)`. Order: `OrderByDescending(Criticality).ThenBy(CreateDate).ThenBy(Id)`; `CountAsync` over the filtered query, then `Skip(skip).Take(take)` with `Include(Reporter)`. The radius predicate, with the filter's fields copied into locals so EF parameterizes them and `radiansPerDegree = Math.PI / 180`:

```csharp
.Where(e =>
    Math.Sin((e.Latitude * radiansPerDegree - centreLatitude) / 2) * Math.Sin((e.Latitude * radiansPerDegree - centreLatitude) / 2)
    + cosCentreLatitude * Math.Cos(e.Latitude * radiansPerDegree)
        * Math.Sin((e.Longitude * radiansPerDegree - centreLongitude) / 2) * Math.Sin((e.Longitude * radiansPerDegree - centreLongitude) / 2)
    <= halfChordThreshold)
```

#### 5. Repository and rank tests

**File**: `PigeonWatch/Api/UnitTests/SightingRepositoryTests.cs`, `UnitTests/SightingStatusTransitionProviderTests.cs`, `UnitTests/GeoDistanceCalculatorTests.cs` (existing or new, following the folder's naming)

**Intent**: Pin the query on SQLite and the rank/enum alignment.

**Contract**: Repository tests (first one written before the query exists): a sighting ~9.9 km north is returned and one ~10.1 km north is not, and one inside the box corner but outside the circle is not; order is Urgent before NeedsHelpSoon before Monitor, oldest first within a level, `Id` breaking equal `CreateDate`; `TotalCount` counts all matches while `Items` holds one page; `skip` past the end gives empty items with the real count; status and criticality predicates apply before the count. Provider test: enum values ascending equal rank order ascending.

### Success Criteria:

#### Automated Verification:

- The SQLite radius test passes, proving `sin`/`cos` translate on SQLite: `dotnet test PigeonWatch/Api/UnitTests/PigeonWatch.UnitTests.csproj --filter SightingRepositoryTests`
- Unit tests pass: `dotnet test PigeonWatch/Api/UnitTests/PigeonWatch.UnitTests.csproj`
- Architecture tests pass: `dotnet test PigeonWatch/Api/ArchitectureTests/PigeonWatch.ArchitectureTests.csproj`
- Build passes with style rules: `dotnet build PigeonWatch/Api/PigeonWatchApi.slnx`
- No model change: `dotnet ef migrations has-pending-model-changes --project PigeonWatch/Api/Data/PigeonWatch.Data.csproj --startup-project PigeonWatch/Api/WebApi.Host/PigeonWatch.WebApi.Host.csproj` reports no changes

**Implementation Note**: After completing this phase and all automated verification passes, pause here for manual confirmation from the human that the manual testing was successful before proceeding to the next phase.

---

## Phase 2: API endpoint — `POST sightings/list`

### Overview

Expose the paged, urgency-ordered list over HTTP with validation, error codes, models, tests and docs.

### Changes Required:

#### 1. Rules and error codes

**File**: `PigeonWatch/Api/BusinessObjects/SightingRules.cs`, `SightingErrorCodes.cs`

**Intent**: Page sizes and default as constants next to the other limits; two new error codes.

**Contract**: `ListPageSizeSmall = 10`, `ListPageSizeMedium = 20`, `ListPageSizeLarge = 30`, `ListDefaultPageSize = ListPageSizeSmall`; `InvalidPage`, `InvalidPageSize`.

#### 2. Service method

**File**: `PigeonWatch/Api/BusinessLogic/Services/SightingService.cs` and `Interfaces/ISightingService.cs`

**Intent**: Validate the query, resolve an empty status filter to the open statuses, build the radius filter and ask the repository for the page. Nearby validation and ordering stay untouched.

**Contract**: `Task<SightingListResult> GetListPageAsync(SightingListQuery query, CancellationToken cancellationToken = default)`. Validation collects all errors: a status not defined in `SightingStatus` → `InvalidFilterStatus` (resolved values are valid); undefined criticality → `InvalidFilterCriticality`; `Page < 1` → `InvalidPage`; `PageSize` not in the three sizes (a `static readonly` set on the service) → `InvalidPageSize`. Statuses and criticalities are de-duplicated; `skip = (Page - 1) · PageSize`.

#### 3. API models, mapper, view model creator

**File**: `PigeonWatch/Api/WebApi/Models/SightingListRequestModel.cs`, `SightingPageModel.cs`; `WebApi/Mappers/SightingListRequestMapper.cs` (+ interface); `WebApi/ViewModelCreators/SightingViewModelCreator.cs` (+ interface)

**Intent**: JSON body model with range-validated coordinates and defaults; a mapper to `SightingListQuery` that casts ints to enums like `NearbySightingsRequestMapper`; a `CreatePage` method on the existing creator.

**Contract**: Request: `required double Latitude` `[Range(-90, 90)]`, `required double Longitude` `[Range(-180, 180)]`, `List<int> Statuses = []`, `List<int> Criticalities = []`, `int Page = 1`, `int PageSize = SightingRules.ListDefaultPageSize`. Response: `SightingPageModel { required IReadOnlyList<SightingModel> Items; required int TotalCount }`. Mapper registered in `DependencyInjection` with the other WebApi mappers.

#### 4. Controller action

**File**: `PigeonWatch/Api/WebApi/Controllers/SightingsController.cs`

**Intent**: `[HttpPost("list")]` action following `Nearby`: `Cache-Control: private, no-store`, map, call the service, log known invalid-input codes, return a validation problem or `Ok(CreatePage(...))`.

**Contract**: `Task<ActionResult<SightingPageModel>> List([FromBody] SightingListRequestModel request, CancellationToken cancellationToken)`; route `sightings/list`; `InvalidPage` and `InvalidPageSize` join `loggedErrorCodes`.

#### 5. Tests

**File**: `PigeonWatch/Api/UnitTests/SightingServiceTests.cs`, `UnitTests/SightingsControllerTests.cs`, `UnitTests/SightingViewModelCreatorTests.cs` (or the existing creator test file), `UnitTests/SightingListRequestMapperTests.cs`; `IntegrationTests/EndpointFixtureRegistry.cs`; an integration test file for the list (beside the existing sightings integration tests)

**Intent**: Pin validation, defaults and wiring at unit level, and the full pipeline on SQLite at integration level.

**Contract**: Service: each error code alone and combined; `[]` statuses become the five open statuses passed to the repository; `[Adopted]` is accepted and passed through; skip/take from page and size; nearby tests unchanged. Controller: success maps to `SightingPageModel`, failure is a 400 with the codes, `Cache-Control` set. Registry: `new(["POST sightings/list"], BuildList)` with fixtures for default body, page 2 with size 20, resolved status, page 0, page size 15, latitude 999, missing coordinates, non-numeric status, plus `AddMalformed`. Integration: an end-to-end order and paging check (three criticalities, two pages, `totalCount`), a page past the end returning empty items, and a 401 without a token.

#### 6. Docs

**File**: `PigeonWatch/Api/CLAUDE.md`, `PigeonWatch/CLAUDE.md`

**Intent**: Add `POST sightings/list` to the endpoint surface (body, defaults, ordering, paging, past-end behaviour, error codes), update "All four `sightings` endpoints" to five, add a "List" bullet to the Sightings section (in-SQL haversine via `GetRadiusFilter`, order by stored criticality equals rank, no cap) and the page-size rules to the Rules bullet.

**Contract**: Sections "endpoint surface", "Sightings" (Rules, Nearby, new List, Failures) in `Api/CLAUDE.md`; the surface/nearby paragraph in `PigeonWatch/CLAUDE.md`.

### Success Criteria:

#### Automated Verification:

- Build passes with style rules: `dotnet build PigeonWatch/Api/PigeonWatchApi.slnx`
- Architecture tests pass: `dotnet test PigeonWatch/Api/ArchitectureTests/PigeonWatch.ArchitectureTests.csproj`
- Unit tests pass, including unchanged nearby tests: `dotnet test PigeonWatch/Api/UnitTests/PigeonWatch.UnitTests.csproj`
- Integration tests pass, including endpoint coverage and the privacy sweep over the new fixtures: `dotnet test PigeonWatch/Api/IntegrationTests/PigeonWatch.IntegrationTests.csproj`

#### Manual Verification:

- Against the API running on LocalDB (SQL Server) with a few sightings at different criticalities and one ~12 km away, `POST sightings/list` (through Swagger, or `Invoke-RestMethod` / `curl.exe` with a bearer token) returns the in-radius ones in urgency order with the right `totalCount`, proving the trig predicate translates on SQL Server
- `GET sightings/nearby` returns the same sightings as before, newest first

**Implementation Note**: After completing this phase and all automated verification passes, pause here for manual confirmation from the human that the manual testing was successful before proceeding to the next phase.

---

## Phase 3: SPA shared components — filters and sighting row

### Overview

Promote the map's filter row and My sightings' row into `features/sightings/shared/` so the list can reuse them, with My sightings switching to relative age. No new page yet.

### Changes Required:

#### 1. Shared filters component

**File**: `PigeonWatch/Frontend/src/app/features/sightings/shared/sighting-filters/sighting-filters.{ts,html,scss,spec.ts}` (moved from `nearby-map-page/nearby-map-filters.*` with `git mv`)

**Intent**: The same filter row, parameterised by which statuses it offers and what it starts with, so the map keeps five open statuses and the list offers all nine and starts from the hand-over.

**Contract**: Selector `app-sighting-filters`; inputs `statusOptions: readonly SightingStatus[]` (required), `initial: SightingFilters` (default no filters, applied as the starting selection), `disabled`; output `filtersChange: SightingFilters`. Keeps the `map.filters.*` keys. The map page uses it with `filterableSightingStatuses`; its spec and the map page's template and spec follow the rename.

#### 2. Status list

**File**: `PigeonWatch/Frontend/src/app/features/sightings/sighting.types.ts`

**Intent**: Add `allSightingStatuses` (the nine values in workflow order) next to `filterableSightingStatuses`.

**Contract**: `export const allSightingStatuses: readonly SightingStatus[]`.

#### 3. Relative age

**File**: `PigeonWatch/Frontend/src/app/features/sightings/shared/sighting-age.ts` (+ spec); `PigeonWatch/Api/BusinessObjects/Resources/UiLabels.resx`

**Intent**: A pure function that turns `createdAt` and a "now" into a label key and a number, with abbreviated units so no plural syntax is needed.

**Contract**: `sightingAge(createdAt: string, now: Date): { key: string; value: number }`: under 1 minute or in the future → `sighting.age.justNow`; under 60 minutes → `sighting.age.minutes` ("{0} min ago"); under 24 hours → `sighting.age.hours` ("{0} h ago"); otherwise `sighting.age.days` ("{0} d ago"); values floored. The spec covers each boundary and asserts every key exists in `defaultLabels`.

#### 4. Shared sighting row

**File**: `PigeonWatch/Frontend/src/app/features/sightings/shared/sighting-row/sighting-row.{ts,html,scss,spec.ts}`

**Intent**: One row markup for My sightings and the list: name link to the detail page (or `sighting.unnamed`), status label, criticality badge and relative age in a `<time>` whose `datetime` is `createdAt` and whose `title` is the absolute medium date.

**Contract**: Attribute component `li[app-sighting-row]`, OnPush, inputs `sighting: Sighting` (required) and `now: Date` (required, passed by the parent so a list renders against one clock and specs are deterministic). Styles move from `my-sightings.scss` and use tokens only.

#### 5. My sightings uses the row

**File**: `PigeonWatch/Frontend/src/app/features/sightings/my-sightings/my-sightings.{ts,html,scss,spec.ts}`

**Intent**: Replace the inline row with `app-sighting-row`, passing a `now` captured when the list loads; update the spec's date assertion to relative age.

**Contract**: My sightings' states, reporting and reload pipeline are unchanged.

#### 6. Kitchen sink

**File**: `PigeonWatch/Frontend/src/app/dev/kitchen-sink/cell-stubs.ts`, `kitchen-sink.spec.ts`

**Intent**: Keep the Profile "My sightings items" and map filter cells rendering after the rename; give `sinkSightings` varied `createdAt` values so the relative ages differ.

**Contract**: Existing cell ids and marker assertions unchanged.

### Success Criteria:

#### Automated Verification:

- Frontend tests pass, including label coverage and style-token coverage: `npm test -- --watch=false` (in `PigeonWatch/Frontend`)
- Production build passes within budgets, initial bundle not grown: `npm run build` (in `PigeonWatch/Frontend`)

#### Manual Verification:

- On the map, the filters look and behave as before (five statuses, Clear, toggle count)
- Profile → My sightings shows relative ages ("5 min ago") with the full date on hover

**Implementation Note**: After completing this phase and all automated verification passes, pause here for manual confirmation from the human that the manual testing was successful before proceeding to the next phase.

---

## Phase 4: SPA list page

### Overview

Add the `/sightings/list` page, its API client method, the hand-over from the map, paging and the links between the two views.

### Changes Required:

#### 1. Types and API client

**File**: `PigeonWatch/Frontend/src/app/features/sightings/sighting.types.ts`, `sightings-api.ts` (+ spec)

**Intent**: Wire types for the request and page, the mirrored page sizes, and a POST call.

**Contract**: `SightingListRequest { latitude; longitude; statuses; criticalities; page; pageSize }`, `SightingPage { items: Sighting[]; totalCount: number }`, `sightingListPageSizes = [10, 20, 30]`, `defaultSightingListPageSize = 10`; `SightingsApi.list(request: SightingListRequest): Observable<SightingPage>` posting JSON to `${apiUrl}/sightings/list`.

#### 2. Hand-over contract

**File**: `PigeonWatch/Frontend/src/app/features/sightings/sighting-list-page/sighting-list-hand-over.ts` (+ spec)

**Intent**: The navigation-state shape the map sends and a type guard the list uses to accept it (see Critical Implementation Details).

**Contract**: `interface SightingListHandOver { centre: GeoPoint; filters: SightingFilters }`; `readSightingListHandOver(state: unknown): SightingListHandOver | null`. The map may send only open statuses; the guard accepts any of the nine.

#### 3. Paginator labels

**File**: `PigeonWatch/Frontend/src/app/features/sightings/sighting-list-page/sighting-list-paginator-intl.ts`; `UiLabels.resx`

**Intent**: A `MatPaginatorIntl` subclass provided by the list page that reads its texts from `ResourceService`, so no Material English text is shown.

**Contract**: Keys `list.paginator.itemsPerPage`, `list.paginator.firstPage`, `list.paginator.previousPage`, `list.paginator.nextPage`, `list.paginator.lastPage`, `list.paginator.range` ("{0} – {1} of {2}"), `list.paginator.rangeEmpty` ("0 of {0}"); called with literal keys so `label-coverage.spec.ts` sees them.

#### 4. List page

**File**: `PigeonWatch/Frontend/src/app/features/sightings/sighting-list-page/sighting-list-page.{ts,html,scss,spec.ts}`, `sighting-list-page-reporting.spec.ts`; `UiLabels.resx`

**Intent**: An `app-page-card` page that resolves a centre, loads pages through a `switchMap` pipeline (like My sightings), and renders filters, rows and the paginator.

**Contract**:
- Centre: the hand-over from `Router.currentNavigation()?.extras.state` through the guard (it is also restored on Back); otherwise `GeolocationService.currentPosition(false)`. `denied`, `unavailable`, `timeout` or `unsupported` → a "location unavailable" message with a link to the map; no request is sent.
- State: `filters` (from the hand-over or none), `page` (1), `pageSize` (10), all in memory. A filter or page-size change resets to page 1; a page change requests that page. A response with empty `items`, `totalCount > 0` and `page > 1` requests the last page once.
- View states: locating, location unavailable, loading, error (`app-error-retry` with `app-error-reference`; reported with context `sightings.list` unless `isExpectedClientStatus`), empty (`list.page.empty`, or `list.page.noMatch` when a filter is active), loaded (`ul` of `li[app-sighting-row]` and `mat-paginator` with `sightingListPageSizes`, hidden when `totalCount` is 0).
- Card: title `list.page.title` ("Sightings nearby"), subtitle `list.page.subtitle` ("Within {0} km, most urgent first" with `nearbyRadiusKm`), header action "Map view" (`list.page.mapView`, plain `routerLink="/"`), filters (`app-sighting-filters` with `allSightingStatuses` and the hand-over's filters) below the header, always visible.
- Other keys: `list.page.error`, `list.page.retry`, `list.page.locating`, `list.page.locationUnavailable`, `list.page.openMap`.

#### 5. Route and map link

**File**: `PigeonWatch/Frontend/src/app/app.routes.ts`; `features/sightings/nearby-map-page/nearby-map-page.{ts,html,spec.ts}`; `UiLabels.resx`

**Intent**: Declare the lazy, guarded route before `sightings/:id`; add a "List view" link to the map toolbar that passes `{ centre: lastSearchCentre, filters }` as navigation state when a search has run, and no state otherwise.

**Contract**: Route `sightings/list`, `canActivate: [authGuard]`, no layout flag. Map key `map.listView`. Map spec: the link carries the hand-over after a search and none before it. The map's own filter state stays page-local.

#### 6. Kitchen sink

**File**: `PigeonWatch/Frontend/src/app/dev/kitchen-sink/cell-stubs.ts`, `cell-definitions.ts`, `state-cell.html`, `state-cell.ts`, `kitchen-sink.spec.ts`

**Intent**: A "Sighting list" group with cells for locating, location denied, loading, loaded (mixed criticalities and ages), second page, empty, filtered with no match, and error; a `list()` stub mode set (items, paged, empty, failure, pending) with list fixtures that vary criticality and `createdAt`.

**Contract**: New group and modes interface, a `state-cell` case, a marker assertion per cell, and the same `SightingsApi.list` stub shape in the page spec. Sink cells use the geolocation stub (no hand-over).

#### 7. Docs

**File**: `PigeonWatch/Frontend/CLAUDE.md`, `PigeonWatch/CLAUDE.md`

**Intent**: Document `features/sightings/shared/` (feature-shared components that may use `sighting.types` and keys, the exception to the `shared/ui` promotion rule), the list page and its hand-over through navigation state, that the map's filters are still page-local and only handed to the list, the paginator intl, relative age in rows, and the new kitchen-sink group; add the list to the SPA overview.

**Contract**: Sections "UI library, styling and reusable components" (reusable components, kitchen sink), "Maps and geolocation" (Nearby filters paragraph), and the `features/` description.

### Success Criteria:

#### Automated Verification:

- Frontend tests pass, including the list page, reporting, hand-over guard, kitchen-sink and label-coverage specs: `npm test -- --watch=false` (in `PigeonWatch/Frontend`)
- Production build passes within budgets and the initial bundle is not grown: `npm run build` (in `PigeonWatch/Frontend`)

#### Manual Verification:

- From the map after a search with a criticality filter, "List view" opens the list at that centre with the filter preselected, Urgent rows first, oldest first within a level
- Opening `/sightings/list` directly asks for location; with location blocked it shows the message and the map link
- Paging and page size work; changing a filter returns to page 1; opening a sighting and pressing Back shows the list at the same centre and filters, page 1
- "Map view" returns to the map with its default state
- The kitchen sink's "Sighting list" group renders every cell

**Implementation Note**: After completing this phase and all automated verification passes, pause here for manual confirmation from the human that the manual testing was successful before proceeding to the next phase.

---

## Phase 5: E2E — list opened from the map

### Overview

Keep one browser-level test for the user flow that unit tests cannot cover: the map → list hand-over and the real POST against the running API.

### Changes Required:

#### 1. Playwright test

**File**: `PigeonWatch/Frontend/tests/e2e/sighting-list.spec.ts`

**Intent**: Following `nearby-filters.spec.ts` (own registered user to avoid the cooldown, geolocation 52.07/19.48), report an Urgent sighting, search on the map, open "List view", and assert the request body and the row; then choose only a resolved status and assert the no-match message.

**Contract**: `waitForResponse` on `POST …/sightings/list`; the first request body has the map's centre, `page: 1`, `pageSize: 10`; the reported sighting's row shows its name, the Urgent badge and a relative age; the filter change sends `statuses: [6]` (or the chosen resolved value) and `page: 1`, and the page shows `list.page.noMatch`.

### Success Criteria:

#### Automated Verification:

- The new test passes: `npx playwright test tests/e2e/sighting-list.spec.ts` (in `PigeonWatch/Frontend`)
- The full E2E suite still passes: `npx playwright test` (in `PigeonWatch/Frontend`)

**Implementation Note**: After completing this phase and all automated verification passes, the change is ready for review.

---

## Testing Strategy

### Unit Tests:

- API: radius constants vs haversine distance; SQLite repository radius edges, ordering with tie-break, count vs page, past-end page, filter-before-count; enum/rank alignment; service validation per code and combined, empty statuses → open set, resolved accepted; mapper casts; view model creator; controller success/failure/cache header. Nearby tests unchanged.
- SPA: `sightingAge` boundaries and key existence; filters component with `statusOptions` and `initial`; sighting row output; hand-over guard (valid, wrong types, out-of-range, unknown enum values); list page state transitions, page reset rules, past-end recovery, reporting per status; map link state.

### Integration Tests:

- `POST sightings/list` through the real pipeline on SQLite: order and paging across pages, past-end, 401 without a token, endpoint coverage and privacy sweep over the new fixtures.

### Manual Testing Steps:

1. Run the API on LocalDB and the SPA; report sightings at three criticalities near one point and one ~12 km away.
2. Map → search → "List view": check order, counts and that the far one is missing.
3. Choose a resolved status: list shows the no-match message (no resolved sightings exist before S-09).
4. Page size 10 → 20 and page navigation; open a row and press Back.
5. Open `/sightings/list` directly with location blocked; follow the map link.

## Performance Considerations

The list has no row cap: count and page queries scan the box rows that match status and criticality, using the `(LATITUDE, LONGITUDE)` index for the box; the sort by criticality and `CREATE_DATE` has no index and runs over the in-radius rows. This is acceptable at the PRD's small-to-medium volume (`prd.md`, Open Question 2); an index or cap is a later change if needed. Each list load is two queries (count and page).

## Migration Notes

No schema change and no data migration. The SPA and API ship together; an older SPA never calls the new endpoint.

## References

- Related research: `context/changes/urgency-ranked-sighting-list/research.md` (decisions D1-D11; D5 and D7 superseded by this plan: `POST` body instead of `GET` query, router navigation state instead of URL parameters)
- Roadmap item: `context/foundation/roadmap.md` S-06; PRD FR-020
- Nearby query to mirror: `PigeonWatch/Api/BusinessLogic/Services/SightingService.cs:56-92`, `PigeonWatch/Api/Data/Repositories/SightingRepository.cs:35-67`
- Body endpoint fixtures: `PigeonWatch/Api/IntegrationTests/EndpointFixtureRegistry.cs`
- Filters to promote: `PigeonWatch/Frontend/src/app/features/sightings/nearby-map-page/nearby-map-filters.ts`
- Row to promote: `PigeonWatch/Frontend/src/app/features/sightings/my-sightings/my-sightings.html:20-36`
- E2E pattern: `PigeonWatch/Frontend/tests/e2e/nearby-filters.spec.ts`

## Implementation Addendum (2026-11-01, from impl review)

### A1. Map and detail page round trip (supersedes two "What We're NOT Doing" items)

- The map seeds its filters from navigation state (`readNearbyMapFilters`, open statuses only) and keeps them in the history entry via `Location.replaceState`, so Back and "Map view" restore them. The list's "Map view" link and the detail page's "Back to map" carry `{ mapFilters }`.
- List rows carry `{ returnToList: { centre, filters } }`; the detail page reads it through `readSightingDetailReturn` and shows "Back to list", which reopens the list at the same centre and filters, page 1.
- All state passes through the same type guards as the hand-over. Documented in `Frontend/CLAUDE.md`.

### A2. Initial bundle grew (supersedes "initial bundle not grown" in Phase 4)

- Initial total went from 516.31 kB (39d47f4) to 528.18 kB (4c5ce22): Material icon-button +8.15 kB and CDK a11y +4.18 kB pulled into the shared initial chunk by `mat-paginator`, generated UI labels +1.31 kB, global theme overrides and tokens +0.35 kB. The plan missed that resx labels and global theme overrides always land in the initial bundle.
- The `initial` warning budget in `angular.json` was raised from 520 kB to 540 kB; 528 kB is the new baseline.

### A3. List page UI (supersedes parts of Phase 4 items 3 and 4)

- Pager: `mat-paginator` with `hidePageSize` supplies only the first/previous/next/last buttons; a separate `mat-select` chooses the page size, and the page renders its own `list.paginator.range` as "current / last". `SightingListPaginatorIntl.getRangeLabel` returns an empty string and `list.paginator.rangeEmpty` was not added.
- Filters sit behind a toggle like the map's (closed by default), not always visible below the header.
- The title shows the match count once a page has loaded (`list.page.titleWithCount`).
- Route `sightings/list` declares `layout: 'wide'` so the card can grow past the content width on large screens.

## Progress

> Convention: `- [ ]` pending, `- [x]` done. Append ` — <commit sha>` when a step lands. Do not rename step titles. See `references/progress-format.md`.

### Phase 1: API data — in-SQL radius and urgency-ordered paged query

#### Automated

- [x] 1.1 The SQLite radius test passes, proving sin/cos translate on SQLite — 4638951
- [x] 1.2 Unit tests pass — 4638951
- [x] 1.3 Architecture tests pass — 4638951
- [x] 1.4 Build passes with style rules — 4638951
- [x] 1.5 No model change reported by has-pending-model-changes — 4638951

### Phase 2: API endpoint — POST sightings/list

#### Automated

- [x] 2.1 Build passes with style rules — a45e796
- [x] 2.2 Architecture tests pass — a45e796
- [x] 2.3 Unit tests pass, including unchanged nearby tests — a45e796
- [x] 2.4 Integration tests pass, including endpoint coverage and the privacy sweep over the new fixtures — a45e796

#### Manual

- [x] 2.5 POST sightings/list on LocalDB returns in-radius sightings in urgency order with the right totalCount — a45e796
- [x] 2.6 GET sightings/nearby returns the same sightings as before, newest first — a45e796

### Phase 3: SPA shared components — filters and sighting row

#### Automated

- [x] 3.1 Frontend tests pass, including label coverage and style-token coverage — 340ef79
- [x] 3.2 Production build passes within budgets, initial bundle not grown — 340ef79

#### Manual

- [x] 3.3 Map filters look and behave as before — 340ef79
- [x] 3.4 My sightings shows relative ages with the full date on hover — 340ef79

### Phase 4: SPA list page

#### Automated

- [x] 4.1 Frontend tests pass, including the list page, reporting, hand-over guard, kitchen-sink and label-coverage specs — d89abf2
- [x] 4.2 Production build passes within budgets and the initial bundle is not grown — d89abf2 — note: build passes, but the initial bundle grew to 528.18 kB and the budget moved to 540 kB (A2)

#### Manual

- [x] 4.3 List view from the map opens at the map centre with filters preselected, in urgency order — d89abf2
- [x] 4.4 Direct open asks for location; blocked location shows the message and the map link — d89abf2
- [x] 4.5 Paging, page size, filter reset to page 1, and Back from detail keep centre and filters — d89abf2
- [x] 4.6 Map view returns to the map with its default state — d89abf2 — note: Map view restores the list's filters (A1), not the default state
- [x] 4.7 Kitchen sink Sighting list group renders every cell — d89abf2

### Phase 5: E2E — list opened from the map

#### Automated

- [x] 5.1 The new sighting-list Playwright test passes — eec602e
- [x] 5.2 The full E2E suite still passes — eec602e
