# Filter sightings by nearby area, status and criticality Implementation Plan

## Overview

S-05 (roadmap `context/foundation/roadmap.md:148-159`, PRD FR-004) lets the user narrow the nearby map by status and criticality. The nearby area itself is already scoped by S-03 (10 km radius), so this change adds two optional filters: query parameters on `GET sightings/nearby`, applied in the repository query, and two multi-select controls in the map toolbar. An empty selection means "all", nothing is remembered between visits, and a filter change runs a new search and clears the selection.

## Current State Analysis

- `GET sightings/nearby` takes only latitude and longitude (`PigeonWatch/Api/WebApi/Models/NearbySightingsRequestModel.cs:6-15`); the SPA sends only those two (`PigeonWatch/Frontend/src/app/features/sightings/sightings-api.ts:20-24`).
- The service already hard-codes a status filter: all statuses for which `IsResolved` is false (Spotted, Caught, AtTheVet, InHomeCare, AwaitingAdoption) go to `ListInBoxAsync` (`SightingService.cs:59-70`, resolved set at `SightingStatusTransitionProvider.cs:21-27`). The repository already takes a status list (`SightingRepository.cs:48-52`). No criticality predicate exists on this path.
- The repository takes the newest `NearbyBoxMaxRows` (400) rows in the box before the circle filter, then the service trims to `NearbyMaxResults` (200) (`SightingRepository.cs:35-58`, `SightingService.cs:63-75`, `SightingRules.cs:10-11`).
- The map page has one `searches` pipeline keyed on the centre (`nearby-map-page.ts:192-203`), a `status` computed that picks found or empty from `items.length` (`:148-166`), and a `search()` that already clears `selectedId` (`:308-313`).
- No chip, checkbox or toggle component exists in `shared/ui`; the report form's `mat-select` is the only Material select (`report-sighting-page.html:53-92`).
- Research: `context/changes/filter-sightings-nearby/research.md`.

## Desired End State

On the nearby map the user sees two filter controls (status, criticality) in the toolbar. With nothing selected the map shows exactly what S-03 shows. Selecting values narrows the markers through a new search; the result count and the empty message reflect the filter; the selection card closes on every filter change. Verified by API tests for each filter combination and invalid input, SPA specs for the control and the pipeline, kitchen-sink cells, and one Playwright test.

### Key Discoveries:

- The repository already filters by a status list, so the status filter is an intersection of the requested statuses with the open set (`SightingService.cs:59-61`); only criticality needs a new predicate.
- Filtering server-side is the choice that keeps the 400/200 caps meaningful: they must apply after the filter, not before it.
- `ListInBoxAsync` is pinned by `SightingServiceTests.cs:211`, `SightingRepositoryTests.cs:99,121,139,140` and the `CandidatesInBox` helper; any signature change moves all three.
- API models cannot use business enums (`ArchitectureTests/ControllerActionTests.cs:31`), so the filter lists are `int` lists like `CreateSightingRequestModel`.
- Label keys come from `Api/BusinessObjects/Resources/UiLabels.resx` and `label-coverage.spec.ts` fails on a missing key or literal template text.
- The initial bundle is 512.1 kB against a 520 kB warning (`Frontend/CLAUDE.md:31`), but the map page is lazy (`app.routes.ts:12`), so Material select added there does not count toward it; this is confirmed by `npm run build` in Phase 3.

## What We're NOT Doing

- Resolved statuses (Adopted, Released, Passed away, Not found) on the map; they stay excluded, and the status filter offers only the five open statuses. "Include resolved on request" belongs to the S-06 list (FR-020).
- A user-changeable radius or a named city (the non-blocking S-05 unknown in the roadmap).
- Remembering the filter choice (no `localStorage`) or putting it in the URL.
- Moving the filter components into `shared/ui`; they stay local to the map page until S-06 reuses them.
- A database index on status or criticality; the box query remains the volume strategy.
- Changing the urgency order or adding a list view (S-06).

## Implementation Approach

Contract first, then the data layer, then the page. Phase 1 makes the API contract testable alone. Phase 2 changes the SPA's API wrapper, types and labels without touching the page UI. Phase 3 adds the controls and the page behaviour. Phase 4 adds the browser-level test and the documentation.

Decisions carried in from planning:

- Filtering runs on the server through repeatable query parameters `statuses` and `criticalities` (ints). No parameter means no filter, so existing callers and tests behave as before.
- An undefined enum value, or a resolved status in `statuses`, returns 400 with an error code following the existing `Create` validation pattern.
- Empty selection means all; both controls start empty; state resets on reload.
- A filter change triggers a new search at the last centre and clears the selection, as "Search this area" does.
- Controls are two multi-select `mat-select` fields on the existing form-field defaults.

## Phase 1: API filter contract

### Overview

`GET sightings/nearby` accepts optional status and criticality lists, validates them, and filters in the repository query.

### Changes Required:

#### 1. Request model and controller

**File**: `PigeonWatch/Api/WebApi/Models/NearbySightingsRequestModel.cs`, `PigeonWatch/Api/WebApi/Controllers/SightingsController.cs`

**Intent**: Add the two optional filter lists to the request and pass them to the service; invalid input produces a 400 through the same mechanism `Create` uses, not a silent drop.

**Contract**: `statuses` and `criticalities` are optional `int` lists bound from repeated query parameters (`?statuses=1&statuses=2&criticalities=3`); omitted or empty means no filter. The latitude and longitude rules are unchanged, and the action keeps its `Task<ActionResult<...>>` return type.

#### 2. Service

**File**: `PigeonWatch/Api/BusinessLogic/Services/Interfaces/ISightingService.cs`, `PigeonWatch/Api/BusinessLogic/Services/SightingService.cs`, `PigeonWatch/Api/BusinessObjects/SightingErrorCodes.cs`

**Intent**: Validate the requested values and narrow the repository query. Requested statuses are checked against the open set and requested criticalities against `SightingCriticality`; the status list sent to the repository is the requested statuses (or the whole open set when none are requested).

**Contract**: `GetNearbyAsync(latitude, longitude, statuses, criticalities, ct)` with the new lists as business types. Unknown or resolved status values and unknown criticality values are rejected with new error codes that follow `InvalidInitialStatus` / `InvalidCriticality` naming. Ordering by `CreatedAt` descending, the 200-row cap and the circle filter are unchanged.

#### 3. Repository

**File**: `PigeonWatch/Api/Data/Repositories/Interfaces/ISightingRepository.cs`, `PigeonWatch/Api/Data/Repositories/SightingRepository.cs`

**Intent**: Add a criticality predicate to the box query so the `NearbyBoxMaxRows` cap applies to matching rows only.

**Contract**: `ListInBoxAsync` gains an optional criticality collection; null or empty means no predicate. The existing status list parameter stays.

#### 4. Tests

**File**: `PigeonWatch/Api/UnitTests/SightingServiceTests.cs`, `PigeonWatch/Api/UnitTests/SightingRepositoryTests.cs`, `PigeonWatch/Api/UnitTests/SightingsControllerTests.cs`, `PigeonWatch/Api/IntegrationTests/EndpointFixtureRegistry.cs`, `PigeonWatch/Api/IntegrationTests/OwnershipContractTests.cs`

**Intent**: Update the tests pinned to the old signatures and add the new behaviour, following the existing `Subject_behaviour_in_snake_case` naming.

**Contract**: Service tests for status-only, criticality-only, combined and empty filters, and for each rejected value (undefined status, resolved status, undefined criticality); the existing exact-argument test at `SightingServiceTests.cs:211` keeps asserting the open set when no filter is given. Repository tests for the criticality predicate with the cap applied after it. Controller test passes the lists through and returns 400 for a rejected value. Integration fixtures in `BuildNearby` for a valid filter and an invalid filter value; a seeded case through `TestUser.CreateSightingAsync` proves a filter returns only matching sightings and that coordinates still do not appear in request logs.

#### 5. API documentation

**File**: `PigeonWatch/Api/CLAUDE.md`, `PigeonWatch/CLAUDE.md`

**Intent**: Update the nearby description ("up to 200 newest open sightings within 10 km") to state the optional filters and that no parameters keeps the old result.

**Contract**: Edit the lines that currently describe the nearby contract (`Api/CLAUDE.md:25,37,40`, `PigeonWatch/CLAUDE.md:16`).

### Success Criteria:

#### Automated Verification:

- The API solution builds with the `.editorconfig` style rules: `dotnet build PigeonWatch/Api/PigeonWatchApi.slnx`
- Architecture tests pass: `dotnet test PigeonWatch/Api/ArchitectureTests/PigeonWatch.ArchitectureTests.csproj`
- Unit tests pass, including the new filter and validation cases: `dotnet test PigeonWatch/Api/UnitTests/PigeonWatch.UnitTests.csproj`
- Integration tests pass, including the endpoint coverage and privacy sweeps with the new fixtures: `dotnet test PigeonWatch/Api/IntegrationTests/PigeonWatch.IntegrationTests.csproj`

#### Manual Verification:

- With the API running on LocalDB and a token from `POST auth/login` (use `curl.exe`), `sightings/nearby` with `criticalities=3` returns only Urgent sightings, with `statuses=1` only Spotted ones, and with neither returns the same set as before
- `statuses=6` (Adopted) and `criticalities=9` each return 400

**Implementation Note**: After completing this phase and all automated verification passes, pause here for manual confirmation from the human that the manual testing was successful before proceeding to the next phase. Phase blocks use plain bullets — the corresponding `- [ ]` checkboxes for these items live in the `## Progress` section at the bottom of the plan.

---

## Phase 2: SPA data layer and labels

### Overview

The SPA can ask for filtered results and has the types and labels the controls need; the page UI is unchanged.

### Changes Required:

#### 1. API wrapper

**File**: `PigeonWatch/Frontend/src/app/features/sightings/sightings-api.ts`, `PigeonWatch/Frontend/src/app/features/sightings/sightings-api.spec.ts`

**Intent**: Let `nearby` take the two filter lists and send them as repeated query parameters; with no filters the request stays identical to today's.

**Contract**: `nearby(centre, filters?)` where `filters` carries `statuses` and `criticalities` as arrays of the existing numeric types; empty or missing arrays add no parameters.

#### 2. Types

**File**: `PigeonWatch/Frontend/src/app/features/sightings/sighting.types.ts`

**Intent**: Export the list of the five open statuses (in workflow order) next to `selectableSightingCriticalities`, and a filter type shared by the wrapper and the page.

**Contract**: `filterableSightingStatuses` (Spotted, Caught, AtTheVet, InHomeCare, AwaitingAdoption) and a `SightingFilters` type. Existing label maps are reused for option text.

#### 3. Labels

**File**: `PigeonWatch/Api/BusinessObjects/Resources/UiLabels.resx`

**Intent**: Add the label keys the filters need: the two field labels, the "all" placeholder shown when nothing is selected, a group label for the filter area, and the no-match message with the radius. The generated per-culture JSON is rebuilt by the existing `prebuild`/`pretest` step.

**Contract**: New keys under the existing `map.*` prefix, English values only (Polish is S-12's work); every key is used by a template or `translate(...)` call so `label-coverage.spec.ts` passes.

### Success Criteria:

#### Automated Verification:

- Resource snapshots regenerate and the frontend builds within budget: `npm run build` in `PigeonWatch/Frontend`
- Specs pass, including the wrapper spec for the new parameters and the label coverage spec: `npm test -- --watch=false` in `PigeonWatch/Frontend`

---

## Phase 3: Map page filters

### Overview

Two multi-selects in the map toolbar drive the search pipeline; the page reports the filter in its status text and closes the selection on every change.

### Changes Required:

#### 1. Filter controls

**File**: `PigeonWatch/Frontend/src/app/features/sightings/nearby-map-page/` (new `nearby-map-filters` component with `.ts`, `.html`, `.scss`) and `PigeonWatch/Frontend/src/styles/_material-theme.scss` only if the multi-select needs an override

**Intent**: A standalone component holding two multi-select `mat-select` fields (status, criticality) built on `provideFormFieldDefaults()` and the report form's `mat-select` markup pattern, emitting the current `SightingFilters` as an output. Empty selection shows the "all" placeholder; options use the existing label maps; criticality options may use the existing badge for the dot.

**Contract**: Input: none beyond disabled-while-locating; output: `filtersChange` with the two arrays. Styles use only `var(--pw-*)` and `pw-*` classes, no `.mat-*` selectors or `::ng-deep` (`style-token-coverage.spec.ts`); any variant is a component input; per-component style stays under the 4 kB budget.

#### 2. Page state and pipeline

**File**: `PigeonWatch/Frontend/src/app/features/sightings/nearby-map-page/nearby-map-page.ts`, `nearby-map-page.html`

**Intent**: Hold the active filters in a signal, pass them to `sightingsApi.nearby(centre, filters)`, and on a filter change re-run the last search centre through the existing `search()` path so selection clears and previous markers stay while loading. With no search centre yet (idle, waiting for position) a filter change only stores the filters.

**Contract**: The `found` and `empty` statuses stay driven by `items.length`; a new empty variant is chosen when the result is empty and any filter is active, showing the no-match label instead of the radius-only empty label. The filters component sits in the toolbar next to "Search this area". Every `if`/`return` follows the repo's blank-line and no-brace rules (`Frontend/CLAUDE.md:83-84`).

#### 3. Specs

**File**: `PigeonWatch/Frontend/src/app/features/sightings/nearby-map-page/nearby-map-page.spec.ts`, new `nearby-map-filters.spec.ts`

**Intent**: Cover the control and the page behaviour: filters are passed to `SightingsApi.nearby`, a change re-runs the last centre and clears the selection, no centre yet stores the filters without a request, the no-match message appears only with an active filter, and an unfiltered empty result still shows the radius message. The existing tests at `:349`, `:380`, `:386`, `:428`, `:443` keep passing.

**Contract**: Reuse the page spec's stubs for `SightingsApi` and `GeolocationService` and the `MapViewStub`; the control spec asserts options, the "all" placeholder and the emitted arrays.

#### 4. Kitchen sink

**File**: `PigeonWatch/Frontend/src/app/dev/kitchen-sink/cell-definitions.ts`, `cell-stubs.ts`, `kitchen-sink.spec.ts`

**Intent**: Register the new page states (filters applied with results, filter with no match) as cells with a stub mode, and add the marker assertions, per `Frontend/CLAUDE.md:29`.

**Contract**: A new `NearbyMode` for a filtered result in `cell-stubs.ts`, new ids following the `map-*` pattern in `nearbyMapCells`, and matching assertions in `kitchen-sink.spec.ts`; the "every cell renders once" test (`:115`) still passes.

### Success Criteria:

#### Automated Verification:

- The frontend builds and stays within the initial-bundle budget (initial warning 520 kB, 512.1 kB measured before this change) and the 4 kB component style budget: `npm run build` in `PigeonWatch/Frontend`
- Specs pass, including the page, filter and kitchen-sink specs and the style-token and label coverage specs: `npm test -- --watch=false` in `PigeonWatch/Frontend`

#### Manual Verification:

- On a phone-sized viewport the two controls fit the toolbar without horizontal scroll and are usable by touch and keyboard
- Choosing Urgent in the criticality control shows only Urgent markers, the count text matches, the detail card closes, and clearing the control restores the original markers
- A filter with no match shows the no-match message, not the radius-only message
- No new CSP violations appear in the browser console

---

## Phase 4: E2E and docs wrap-up

### Overview

One browser-level test keeps the feature working end to end, and the project documentation states the new contract.

### Changes Required:

#### 1. Playwright test

**File**: `PigeonWatch/Frontend/tests/e2e/nearby-filters.spec.ts`

**Intent**: Using the seed's mocked geolocation and a sighting reported through the UI as in `seed.spec.ts`, assert that a matching criticality filter keeps its marker and a non-matching one removes it, using `getByRole('combobox')` and `getByRole('option')`.

**Contract**: One test; reuses the `auth.setup.ts` session and the seed's coordinates (52.07, 19.48); the marker is found by its accessible name `${birdName}, Urgent`.

#### 2. Frontend and roadmap documentation

**File**: `PigeonWatch/Frontend/CLAUDE.md`, `context/foundation/roadmap.md`

**Intent**: Note the filter behaviour and the local-to-map-page decision in the frontend guide, and record in the roadmap that S-05 shipped filters only (the radius refinement stays an open non-blocking unknown). Status fields are left to `/10x-implement` and `/10x-archive`.

**Contract**: Edit the nearby-map section of `Frontend/CLAUDE.md`; in `roadmap.md` update S-05's Unknowns text only.

### Success Criteria:

#### Automated Verification:

- The new Playwright test passes: `npx playwright test tests/e2e/nearby-filters.spec.ts` in `PigeonWatch/Frontend`
- The existing E2E tests still pass: `npx playwright test` in `PigeonWatch/Frontend`

#### Manual Verification:

- The documentation edits read correctly and no other file's status fields changed

---

## Testing Strategy

### Unit Tests:

- Service: each filter alone and combined, empty lists, rejected values (undefined status, resolved status, undefined criticality), unchanged open-set query when no filter is given
- Repository: criticality predicate, cap applied after the predicate
- Controller: list pass-through and 400 on rejection
- SPA: wrapper query string, control options and emitted values, page re-search and selection clearing, no-match versus empty message

### Integration Tests:

- `sightings/nearby` with filter fixtures through the real pipeline (SQLite in-memory), the endpoint coverage and privacy sweeps, and a seeded filter case

### Manual Testing Steps:

1. Run the API and SPA locally, report one Urgent and one Monitor sighting near the mocked position
2. Filter by Urgent, then by Monitor, then clear, and watch markers and count text
3. Select a marker, change a filter, and confirm the card closes
4. Pick a combination with no match and read the message

## Performance Considerations

The criticality predicate runs inside the existing bounding-box query with the same lat/lon index; no new index is added and volume stays the S-03 "small" target (`context/archive/2026-10-11-report-sighting-on-map/plan.md:471`). Each filter change is one extra request, same cost as "Search this area".

## Migration Notes

None. No schema or data change; requests without the new parameters behave as before.

## References

- Related research: `context/changes/filter-sightings-nearby/research.md`
- Roadmap item: `context/foundation/roadmap.md:148-159`
- Nearby query: `PigeonWatch/Api/BusinessLogic/Services/SightingService.cs:56-77`
- Map page pipeline: `PigeonWatch/Frontend/src/app/features/sightings/nearby-map-page/nearby-map-page.ts:192-203`
- Select pattern to follow: `PigeonWatch/Frontend/src/app/features/sightings/report-sighting-page/report-sighting-page.html:53-92`

## Progress

> Convention: `- [ ]` pending, `- [x]` done. Append ` — <commit sha>` when a step lands. Do not rename step titles. See `references/progress-format.md`.

### Phase 1: API filter contract

#### Automated

- [x] 1.1 The API solution builds with the `.editorconfig` style rules: `dotnet build PigeonWatch/Api/PigeonWatchApi.slnx` — 720dbb8
- [x] 1.2 Architecture tests pass: `dotnet test PigeonWatch/Api/ArchitectureTests/PigeonWatch.ArchitectureTests.csproj` — 720dbb8
- [x] 1.3 Unit tests pass, including the new filter and validation cases: `dotnet test PigeonWatch/Api/UnitTests/PigeonWatch.UnitTests.csproj` — 720dbb8
- [x] 1.4 Integration tests pass, including the endpoint coverage and privacy sweeps with the new fixtures: `dotnet test PigeonWatch/Api/IntegrationTests/PigeonWatch.IntegrationTests.csproj` — 720dbb8

#### Manual

- [x] 1.5 With the API running on LocalDB and a token from `POST auth/login` (use `curl.exe`), `sightings/nearby` with `criticalities=3` returns only Urgent sightings, with `statuses=1` only Spotted ones, and with neither returns the same set as before — 720dbb8
- [x] 1.6 `statuses=6` (Adopted) and `criticalities=9` each return 400 — 720dbb8

### Phase 2: SPA data layer and labels

#### Automated

- [x] 2.1 Resource snapshots regenerate and the frontend builds within budget: `npm run build` in `PigeonWatch/Frontend`
- [x] 2.2 Specs pass, including the wrapper spec for the new parameters and the label coverage spec: `npm test -- --watch=false` in `PigeonWatch/Frontend`

### Phase 3: Map page filters

#### Automated

- [ ] 3.1 The frontend builds and stays within the initial-bundle budget (initial warning 520 kB, 512.1 kB measured before this change) and the 4 kB component style budget: `npm run build` in `PigeonWatch/Frontend`
- [ ] 3.2 Specs pass, including the page, filter and kitchen-sink specs and the style-token and label coverage specs: `npm test -- --watch=false` in `PigeonWatch/Frontend`

#### Manual

- [ ] 3.3 On a phone-sized viewport the two controls fit the toolbar without horizontal scroll and are usable by touch and keyboard
- [ ] 3.4 Choosing Urgent in the criticality control shows only Urgent markers, the count text matches, the detail card closes, and clearing the control restores the original markers
- [ ] 3.5 A filter with no match shows the no-match message, not the radius-only message
- [ ] 3.6 No new CSP violations appear in the browser console

### Phase 4: E2E and docs wrap-up

#### Automated

- [ ] 4.1 The new Playwright test passes: `npx playwright test tests/e2e/nearby-filters.spec.ts` in `PigeonWatch/Frontend`
- [ ] 4.2 The existing E2E tests still pass: `npx playwright test` in `PigeonWatch/Frontend`

#### Manual

- [ ] 4.3 The documentation edits read correctly and no other file's status fields changed
