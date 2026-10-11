# Report a sighting and see it on the map Implementation Plan

## Overview

Ship roadmap slice S-03 end to end: a logged-in user reports a distressed-pigeon sighting from a phone-usable form where the location is a map pin, sees sightings around them on a map (the home page), sees their own sightings on their profile, and opens a detail page. A 60-second cooldown throttles submissions (FR-019).

The frame (`frame.md`) moved the centre of risk from "which map library" to three contracts, which this plan settles first and builds around: what "around me" means (including when position is unknown), how a map pin becomes a required, phone-usable form value, and which external host the `'self'`-only CSP will admit.

## Current State Analysis

- **Nothing sighting-related exists.** The database has one table, `USER_ACCOUNT` (`PigeonWatch/Api/Data/PigeonWatchDbContext.cs:9,16-27`). The home page is a placeholder reserved for S-03 (`PigeonWatch/Frontend/src/app/features/home/home-page/home-page.html:1-4`).
- **S-03 is the first slice with** a domain table, a persisted enum, numeric geo columns, a FK to `USER_ACCOUNT`, a list endpoint, a 404, a `[FromQuery]` action, a per-user throttle, a map, a custom Signal Forms control, and a GET on the profile page (research §1-§4).
- **Backend rules are test-enforced** (8 classes in `PigeonWatch/Api/ArchitectureTests/`): actions return `Task<ActionResult<T>>` with API models only (no `BusinessObjects` types, `ControllerActionTests.cs:30-47`); static classes are const-only (`StaticClassTests.cs:13-26`); every service/provider/repository/mapper/view-model-creator has an interface and registration; UPPER_SNAKE singular tables, `ID uniqueidentifier` PK and four audit columns (`DatabaseModelTests.cs:16-163`).
- **Business failures** are 400 `ValidationProblemDetails` keyed by error code (`AccountController.cs:37-53,74-80`), mapped to label keys on the SPA (`shared/auth/auth-errors.ts:89-155`). The existing 429 limiter sends no body (`WebApi.Host/Program.cs:37-54`).
- **CSP** is `'self'`-only for images and connections (`PigeonWatch/Frontend/public/staticwebapp.config.json:10`) and protects the `localStorage` refresh token. Initial bundle is about 463 kB against a 500 kB warning budget (`Frontend/angular.json:66-77`).
- **Style guard** scans component `.scss` and two partials, bans `.mat-`/`.mdc-`/`::ng-deep`/`!important` and colour/length literals outside the token files (`Frontend/src/styles/style-token-coverage.spec.ts:16,33-39`). Label coverage does not see library-created DOM (`label-coverage.spec.ts:24-28`).
- **Tests** run Vitest + jsdom with no layout, canvas or `navigator.geolocation`; `auth.guards.spec.ts:92-99` renders `HomePage`, so a map on home would be constructed there.

## Desired End State

A logged-in member opens the app and lands on a map. If the browser grants position, the map centres on it and shows open sightings within 10 km; if not, it opens on a neutral Poland view with a hint, and "Search this area" loads sightings within 10 km of whatever the map is centred on. They can open "Report sighting" from the header menu, drop or drag a pin (or use their location, or place it at the map centre), choose Spotted or Caught and a criticality, describe the bird and its behaviour, and submit. They land on the detail page, which shows the data, the coordinates as text, and an opt-in "Show map" toggle. Their profile lists their own sightings. A second submission inside 60 seconds is refused with a message that says how many seconds remain.

Verified by: architecture and unit tests green; a migration applying cleanly; the production build under budget with Leaflet in lazy chunks only; the live "no CSP violations" check with tiles loading; and desktop plus 375 px screenshots of the four new views.

### Key Discoveries:

- Library choice is a known set of build steps once the three contracts are settled (`frame.md`, Hypothesis 1).
- Status and criticality cross the wire as integers, so the "no BusinessObjects types on models" rule is satisfied with plain `int` fields and a cast in the view model creator (`ControllerActionTests.cs:30-47`).
- The transition table cannot be a static helper; it is a `static readonly` member of a non-static, injected provider (`PigeonWatch/Api/CLAUDE.md` coding rules).
- `FormValueControl` exists in the installed `@angular/forms` 22, so the pin-picker is a real Signal Forms control rather than a workaround.
- Audit columns already record the reporter id (`HttpContextCurrentUserProvider.cs:10-20`), but a typed `REPORTER_ID` FK is still needed for the display-name join and for the cooldown and profile indexes.
- `TimeProvider` is registered as a singleton (`DataServiceCollectionExtensions.cs:20`), so the service can compute the cooldown without new plumbing.
- Any command handed to the user in PowerShell uses `curl.exe` (`context/foundation/lessons.md:5-10`).

## What We're NOT Doing

- Photo attach (S-04), filters by status/criticality and a refined "nearby" (S-05), the urgency-ranked list (S-06), comments (S-07), messaging (S-08), edit/status change by the owner (S-09), admin removal (S-10).
- Changing a sighting's status after creation. The full transition graph is defined and unit-tested, but no endpoint uses it yet.
- Longitude wrap-around at the antimeridian and polar latitudes in the bounding box (the product is Polish-scoped for MVP; recorded as an accepted limit).
- NetTopologySuite, a spatial index, or any server-side geocoding or address search.
- A per-user rate-limiter partition, a generalised cooldown service for comments (S-07 generalises it on second use), or an atomic cooldown (two truly concurrent submissions may both pass; accepted for a 60-second spam guard).
- A full-width map layout. The map keeps the existing 40rem `.app-main` column for S-03; width and polish are left to a later `/10x-ui` pass.
- New kitchen-sink cells or a generalised `StateCell`. The sink only gains the stub needed so existing Profile cells make no HTTP call.
- Remembering the last searched area in `localStorage`, an explicit `Permissions-Policy` header, and any map popups carrying user text.
- Exposing reporter ids or contact details on the wire: the API returns the public display name only.

## Implementation Approach

Backend first, because it is test-enforced and the SPA depends on its contract. Phase 1 lands the domain types, the transition graph, the table and the repository. Phase 2 lands the service rules, the endpoints and the client-config extension. Phase 3 lands everything the views share: Leaflet and its build setup, the CSP change, a thin map adapter, the pin-picker control, a geolocation service, the API client and tokens. Phase 4 builds the four views and wires routes and the header. Phase 5 is verification with the live CSP check, the screenshot gate and documentation, including moving the "nearby" definition from S-05 into S-03 in the roadmap.

Map code sits behind one thin adapter component so jsdom specs and later sinks can replace it, while pin logic, geolocation state mapping and query building stay plain, testable code.

Settled decisions (from the interview and upstream):

| Decision | Choice |
| --- | --- |
| Nearby | Fixed 10 km radius around the map centre; centre from geolocation, else from "Search this area" after panning a neutral Poland default view |
| Library and tiles | Leaflet 1.9.4 raster map, tiles from `tile.openstreetmap.org`; CSP gains only `img-src https://tile.openstreetmap.org` |
| Map placement | Map is the home route; `sightings/new` and `sightings/:id` are new lazy routes |
| Detail page | Coordinates as text plus an opt-in "Show map" toggle |
| Cooldown | 60 s, DB check on the user's latest `CREATE_DATE`, coded 400 with remaining seconds; blocked below 60 s, allowed at exactly 60 s |
| Storage | `double` latitude/longitude, bounding-box SQL prefilter, exact great-circle filter in BusinessLogic |
| Enums | Integers in the DB and on the wire; BusinessLogic owns the criticality rank |
| Form controls | Native selects in `mat-form-field`; first custom `FormValueControl` for the pin |
| Visual gate | Manual desktop and 375 px screenshots; `/10x-ui` pass later |

Derived defaults (flag if you disagree): name max 60, description max 1000, behaviour max 500; nearby returns up to 200 newest open sightings; "mine" returns up to 50 newest sightings of any status; resolved sightings are excluded from nearby; neutral default view is Poland at zoom 6; status integers 1-9 follow the PRD order and criticality is Monitor 1, Needs help soon 2, Urgent 3.

## Critical Implementation Details

- **User text never goes into Leaflet HTML.** Leaflet popups and tooltips parse strings as HTML. Selecting a marker therefore shows an Angular-rendered card below the map (auto-escaped, translatable) instead of a popup, and marker icons use a static class name only.
- **The CSP cannot be verified on `ng serve`,** because the headers come from `staticwebapp.config.json` at the static-web-app edge. The live check runs against a production build served with those headers or against the deployed site.
- **Keep Leaflet out of the initial bundle.** The map code lives under `shared/map/` and is not exported from `shared/ui/index.ts` (the barrel is already restricted for initial-bundle code). Leaflet CSS enters through `angular.json` `styles`; if that pushes `initial` over the 500 kB warning, switch it to an `inject: false` bundle loaded by the adapter on first use.
- **Cooldown remainder travels as a number.** `ModelState` carries only descriptions, so the controller also sets `retryAfterSeconds` on the `ValidationProblemDetails` extensions and a `Retry-After` header; the SPA reads that number into the label placeholder.

## Phase 1: Domain and persistence

### Overview

Add the sighting domain types (including the full PRD transition graph), the `SIGHTING` table with its migration, and the repository, all covered by unit tests on SQLite in-memory.

### Changes Required:

#### 1. Domain types

**File**: `PigeonWatch/Api/BusinessObjects/` (new files: `SightingStatus.cs`, `SightingCriticality.cs`, `Sighting.cs`, `NewSighting.cs`, `SightingRules.cs`, `SightingError.cs`, `SightingErrorCodes.cs`, `SightingCreateResult.cs`)

**Intent**: Define the types every later sighting slice reuses, shaped like the S-02 account types (`ProfileUpdateResult.cs:3-14`, `AccountRules.cs:3-14`).

**Contract**: `SightingStatus` int enum with explicit values Spotted 1, Caught 2, AtTheVet 3, InHomeCare 4, AwaitingAdoption 5, Adopted 6, Released 7, PassedAway 8, NotFound 9. `SightingCriticality` Monitor 1, NeedsHelpSoon 2, Urgent 3. `Sighting` carries id, optional name, description, behaviour, latitude, longitude, status, criticality, reporter display name and created-at (UTC); `NewSighting` carries the reporter id and the creatable fields. `SightingRules` is const-only: name 60, description 1000, behaviour 500, cooldown 60 seconds, nearby radius 10 km, nearby cap 200, mine cap 50. Error codes include `CooldownActive`, `InvalidInitialStatus`, `InvalidCriticality`, `InvalidLocation`, `ReporterNotFound`; the failure result carries the remaining seconds for `CooldownActive`. No type name ends in `Model`.

#### 2. Transition provider

**File**: `PigeonWatch/Api/BusinessLogic/Services/SightingStatusTransitionProvider.cs` and `Interfaces/ISightingStatusTransitionProvider.cs`

**Intent**: Encode the PRD status workflow once, in BusinessLogic, so S-06 and S-09 reuse it. No endpoint calls the transition check yet.

**Contract**: Non-static class holding a `static readonly` transition map copied from `prd.md:135-146`. Members: allowed next statuses for a status, whether a transition is allowed, whether a status is resolved (Adopted, Released, PassedAway, NotFound), whether a status is a valid initial status (Spotted or Caught), and the criticality rank. Registered through the BusinessLogic DI extension.

#### 3. Entity, DbContext and migration

**File**: `PigeonWatch/Api/Data/Entities/SightingEntity.cs`, `Data/PigeonWatchDbContext.cs`, `Data/Migrations/` (generated)

**Intent**: Add the `SIGHTING` table following the existing conventions: inline configuration in `OnModelCreating`, `ToTable("Sighting")`, `AuditableEntity` base, `[MaxLength]` from `SightingRules`.

**Contract**: Columns `REPORTER_ID` (Guid FK to `USER_ACCOUNT.ID`, restrict delete, with a `Reporter` navigation for the display name), `NAME` (nullable), `DESCRIPTION`, `BEHAVIOUR`, `LATITUDE`/`LONGITUDE` (`double`), `STATUS`/`CRITICALITY` (`int`). Two indexes: `(LATITUDE, LONGITUDE)` for the bounding box and `(REPORTER_ID, CREATE_DATE)` for the cooldown lookup and the profile list. Migration `AddSighting` is additive and generated with the command in `Api/CLAUDE.md`. Index and FK names come from the UPPER_SNAKE convention; if `DatabaseModelTests` flags a name, fix the convention, not the test.

#### 4. Repository and mapper

**File**: `PigeonWatch/Api/Data/Repositories/SightingRepository.cs` + `Interfaces/ISightingRepository.cs`, `Data/Mappers/SightingMapper.cs` + interface

**Intent**: Keep entities inside `Data` and return business objects, as `AccountRepository.cs:41-75` does.

**Contract**: Create (returns the stored `Sighting` with display name); get by id (null when missing); list within a latitude/longitude box restricted to a given set of open statuses; list by reporter newest first with a take; latest create-date by reporter (null when none). All async methods take `CancellationToken cancellationToken = default`. Repositories never set audit columns.

#### 5. Registration

**File**: `PigeonWatch/Api/DependencyInjection/{Data,BusinessLogic}ServiceCollectionExtensions.cs`

**Intent**: Register the new repository, mapper and provider as `AddScoped<IX, X>()` like the existing types.

**Contract**: One `AddScoped` line per new type; `ServiceRegistrationTests` passes.

#### 6. Tests

**File**: `PigeonWatch/Api/UnitTests/` (new: transition provider tests, sighting repository tests)

**Intent**: Pin the PRD table and the query behaviour before anything depends on them.

**Contract**: Transition tests assert every row of the PRD table (allowed and one disallowed case per status), the four resolved statuses, and the two initial statuses. Repository tests use the SQLite in-memory fixture pattern from `PigeonWatchUserStoreTests.cs:18-27` and cover: box inclusion at the edge, exclusion of listed statuses, reporter list order (newest first), latest-by-reporter, and null results.

### Success Criteria:

#### Automated Verification:

- Solution builds: `dotnet build PigeonWatch/Api/PigeonWatchApi.slnx`
- Architecture tests pass: `dotnet test PigeonWatch/Api/ArchitectureTests/PigeonWatch.ArchitectureTests.csproj`
- Unit tests pass, including the transition table and repository tests: `dotnet test PigeonWatch/Api/UnitTests/PigeonWatch.UnitTests.csproj`
- No pending model changes: `dotnet ef migrations has-pending-model-changes --project PigeonWatch/Api/Data/PigeonWatch.Data.csproj --startup-project PigeonWatch/Api/WebApi.Host/PigeonWatch.WebApi.Host.csproj`

#### Manual Verification:

- `dotnet ef database update` (command in `Api/CLAUDE.md`) applies the migration to LocalDB and the `SIGHTING` table shows the FK and both indexes

**Implementation Note**: After completing this phase and all automated verification passes, pause here for manual confirmation from the human that the manual testing was successful before proceeding to the next phase. Phase blocks use plain bullets — the corresponding `- [ ]` checkboxes for these items live in the `## Progress` section at the bottom of the plan.

---

## Phase 2: Service and API

### Overview

Add the sighting service (validation, cooldown, radius filter), the `sightings` endpoints, the sighting rules in the client configuration, and the UI label keys. After this phase the whole backend contract is testable without the SPA.

### Changes Required:

#### 1. Geo distance

**File**: `PigeonWatch/Api/BusinessLogic/Services/GeoDistanceCalculator.cs` + interface

**Intent**: Great-circle distance and bounding-box computation as an injected, non-static class (the static-class rule forbids a helper).

**Contract**: Given a centre and a radius in km, returns the latitude/longitude box (longitude delta widened by the latitude's cosine, clamped near the poles); given two points, returns the distance in km (haversine).

#### 2. Sighting service

**File**: `PigeonWatch/Api/BusinessLogic/Services/SightingService.cs` + `Interfaces/ISightingService.cs`

**Intent**: Own the rules: create with validation and cooldown, get by id, nearby, mine. Uses the injected `TimeProvider`.

**Contract**: Create validates initial status is Spotted or Caught, criticality is 1-3, latitude within -90..90 and longitude within -180..180, then reads the reporter's latest create-date; if `now - latest < 60 s` it returns a `CooldownActive` failure with `ceil(60 - elapsed)` remaining seconds (elapsed of exactly 60 s is allowed). Nearby takes a centre, computes the box, asks the repository for non-resolved statuses (resolved set from the transition provider), filters by exact distance at most 10 km, orders newest first and takes 200. Mine returns up to 50, any status, newest first. Get returns null for an unknown id.

#### 3. Client configuration

**File**: `PigeonWatch/Api/BusinessLogic/Services/ClientConfigurationService.cs`, `BusinessObjects` client configuration type, `WebApi/Models/ClientConfigurationModel.cs`, `WebApi/Controllers/ConfigurationController.cs`

**Intent**: Serve the sighting rules the same way account rules are served (`ConfigurationController.cs:19-27`).

**Contract**: A nested rules model with name, description and behaviour max lengths, cooldown seconds and nearby radius km. Still anonymous with the existing cache header.

#### 4. Controller, models, creators, mapper

**File**: `PigeonWatch/Api/WebApi/Controllers/SightingsController.cs`, `WebApi/Models/` (`CreateSightingRequestModel`, `NearbySightingsRequestModel`, `SightingModel`, `SightingListModel`), `WebApi/ViewModelCreators/SightingViewModelCreator.cs` + interface, `WebApi/Mappers/CreateSightingRequestMapper.cs` + interface

**Intent**: Expose the service as an authorized API that returns only API models, following `AccountController.cs:37-80`.

**Contract**: `[Authorize]`, route `sightings`. `POST sightings` creates and returns 201 with `SightingModel`; business failures become 400 `ValidationProblemDetails` keyed by error code, and `CooldownActive` additionally sets `retryAfterSeconds` in the extensions and a `Retry-After` header. `GET sightings/{id:guid}` returns the model or 404. `GET sightings/nearby?latitude=&longitude=` (a `[FromQuery]` request model with range validation) returns `SightingListModel`. `GET sightings/mine` returns `SightingListModel`. Models carry status and criticality as `int`, plus id, name, description, behaviour, latitude, longitude, reporter display name and created-at; no reporter id or email. Reads send `Cache-Control: private, no-store`. Required fields use the same attribute approach as `UpdateProfileRequestModel`, with `SightingRules` lengths. The current user id is read inline as in `AccountController.cs:41-42`.

#### 5. UI labels

**File**: `PigeonWatch/Api/BusinessObjects/Resources/UiLabels.resx`

**Intent**: Add every label the new views need (Phase 4 renders them), and drop the obsolete `home.*` placeholder keys once the home page is replaced there.

**Contract**: Keys follow `<area>.<group>.<name>`: `sighting.form.*` (field labels, hints, placeholders, submit), `sighting.status.<name>` for all nine statuses, `sighting.criticality.<name>` for three levels, `sighting.detail.*`, `sighting.mine.*` (title, empty, error, retry), `sighting.errors.*` (cooldown with `{0}` seconds, generic, notFound), `sighting.unnamed`, `map.*` (title, hint when no position, search this area, use my location, place pin at centre, attribution, selected-card labels), and `nav.reportSighting`. Reuse existing unused `nav.*` keys where they fit.

#### 6. Tests

**File**: `PigeonWatch/Api/UnitTests/` (new: service tests, geo tests, controller tests)

**Intent**: Pin the rules at their boundaries and the HTTP mapping.

**Contract**: Service tests with a hand-written `TimeProvider` subclass (no new package): cooldown at 59 s blocked with 1 s remaining, at 60 s allowed, no prior sighting allowed; initial status 3 rejected; criticality 0 and 4 rejected; nearby includes a point just inside 10 km and excludes one just outside; the bounding box corner case (inside the box, outside the circle) is excluded; resolved statuses excluded; cap and ordering. Geo tests assert a known distance within tolerance. Controller tests follow `AccountControllerTests.cs:196-226` (principal plus `ProblemDetailsFactory` stub): 201 on success, 400 with `retryAfterSeconds` and `Retry-After`, 404 for unknown id, 401 for a missing user id, and that models expose no BusinessObjects types (the architecture test already enforces it).

### Success Criteria:

#### Automated Verification:

- Solution builds: `dotnet build PigeonWatch/Api/PigeonWatchApi.slnx`
- Architecture tests pass: `dotnet test PigeonWatch/Api/ArchitectureTests/PigeonWatch.ArchitectureTests.csproj`
- Unit tests pass, including cooldown boundaries, radius filtering and controller mapping: `dotnet test PigeonWatch/Api/UnitTests/PigeonWatch.UnitTests.csproj`
- No pending model changes: `dotnet ef migrations has-pending-model-changes --project PigeonWatch/Api/Data/PigeonWatch.Data.csproj --startup-project PigeonWatch/Api/WebApi.Host/PigeonWatch.WebApi.Host.csproj`

#### Manual Verification:

- With the API running on LocalDB and a bearer token from `POST auth/login` (use `curl.exe`), a create returns 201, fetching it by id returns 200, an unknown id returns 404, `nearby` returns it for a centre within 10 km and not for a centre 15 km away, and a second create within 60 seconds returns 400 with `retryAfterSeconds`

**Implementation Note**: After completing this phase and all automated verification passes, pause here for manual confirmation from the human that the manual testing was successful before proceeding to the next phase. Phase blocks use plain bullets — the corresponding `- [ ]` checkboxes for these items live in the `## Progress` section at the bottom of the plan.

---

## Phase 3: Frontend foundation

### Overview

Everything the views share: Leaflet and its build setup, the named CSP change, the thin map adapter, the pin-picker form control, a geolocation service, the sightings API client, types, error mapping, tokens and the lazy routes' shared pieces.

### Changes Required:

#### 1. Leaflet dependency and build setup

**File**: `PigeonWatch/Frontend/package.json`, `PigeonWatch/Frontend/angular.json`

**Intent**: Add Leaflet 1.9.4 (`leaflet` and `@types/leaflet`, the 1.x pair) and make the esbuild build accept it, without raising any budget.

**Contract**: Leaflet is CommonJS, so add `allowedCommonJsDependencies: ["leaflet"]` to the build options. Add `node_modules/leaflet/dist/leaflet.css` to `styles` (see Critical Implementation Details for the fallback). No marker image files are used; markers are `divIcon`s.

#### 2. CSP change (named security trade-off)

**File**: `PigeonWatch/Frontend/public/staticwebapp.config.json`

**Intent**: Admit exactly one external host so tiles load, and record that this widens a CSP that protects the refresh token in `localStorage`.

**Contract**: `img-src 'self' data: https://tile.openstreetmap.org`. No other directive changes; `Referrer-Policy: strict-origin-when-cross-origin` already satisfies OSM's Referer requirement. The tile URL, attribution and zoom limits live together in one constant module so a provider swap touches one place and the CSP line.

#### 3. Map adapter

**File**: `PigeonWatch/Frontend/src/app/shared/map/map-view/` (component, template, stylesheet)

**Intent**: A thin standalone OnPush component that wraps Leaflet and is the only place that imports it, so specs can replace it.

**Contract**: Signal inputs for centre, zoom and a list of markers (position, id, criticality class); outputs for map click, centre change (after pan or zoom ends), and marker select; an optional draggable single pin for the picker. Marker icons are `divIcon`s with static class names only. Loads Leaflet via `from(import('leaflet'))` (no `async`/`await` in production code) and destroys the map on teardown. Adds the OSM attribution control with the translated attribution text. Not exported from `shared/ui/index.ts`.

#### 4. Marker and map styling

**File**: `PigeonWatch/Frontend/src/styles/_tokens.scss`, `_common.scss`

**Intent**: Style library-created DOM globally and from tokens only, since `::ng-deep` is banned.

**Contract**: New tokens for the three criticality colours (including an amber middle level) and a map height. `_common.scss` declares `pw-map-marker` plus one modifier per criticality, built from tokens. The style guard keeps passing.

#### 5. Pin-picker control

**File**: `PigeonWatch/Frontend/src/app/shared/map/location-picker/`

**Intent**: The repo's first custom Signal Forms control: a map that yields a required latitude/longitude value, usable on a phone with and without geolocation.

**Contract**: Implements `FormValueControl` with a `value` model of `{ latitude, longitude } | null`. Tap places or moves the pin; the pin is draggable; a "Use my location" button fills it from geolocation; a "Place pin at map centre" button gives a non-pointer path. Shows the chosen coordinates as text. Renders required-field errors with `app-field-errors`, gated by `FormSubmitState` so they appear on submit like the other fields. Opens centred on geolocation when available, else on the neutral Poland view, with no pin until the user acts. Pin and geolocation-result handling is plain code covered by specs; the map itself is replaced by a stub in specs.

#### 6. Geolocation service

**File**: `PigeonWatch/Frontend/src/app/core/geolocation/geolocation.service.ts`

**Intent**: One Observable wrapper over `navigator.geolocation.getCurrentPosition` with a typed outcome, used by the map home and the pin-picker.

**Contract**: Always sets a timeout (10 s). Emits `{ status: 'ok', latitude, longitude }` or `{ status: 'denied' | 'unavailable' | 'timeout' | 'unsupported' }`; never errors. Accepts a high-accuracy flag (true for the picker, false for the map home). Works when `navigator.geolocation` is missing (jsdom).

#### 7. API client, types, error mapping

**File**: `PigeonWatch/Frontend/src/app/features/sightings/` (`sightings-api.ts`, `sighting.types.ts`, `sighting-errors.ts`), `core/configuration/configuration.service.ts`

**Intent**: Mirror `profile-api.ts:11-20` for create, get, nearby and mine; type the wire model; and map error codes to label keys.

**Contract**: `ClientConfiguration` gains the sighting rules (read through `computed` with fallbacks like `profile-page.ts:84-93`). `sighting.types.ts` holds const maps from the integer status and criticality values to label keys (static maps, so label coverage still sees every key) and the lists of selectable initial statuses and criticalities. Error mapping reuses the generic ProblemDetails parsing in `shared/auth/auth-errors.ts`; if that helper is auth-named, move it to a neutral shared file in this change (promote-on-second-use) and keep auth behaviour unchanged. The cooldown error exposes the `retryAfterSeconds` number.

#### 8. Tests

**File**: `PigeonWatch/Frontend/src/app/**/*.spec.ts` (new specs beside each unit)

**Intent**: Cover the logic that does not need a rendered map.

**Contract**: Geolocation service outcomes for each failure mode via a stubbed `navigator.geolocation`; picker value changes from map click, drag, geolocation success and "place at centre", plus the required error appearing only after submit; API client URLs and params via `HttpTestingController`; error mapping including the cooldown number. Style guard and label coverage run as usual.

### Success Criteria:

#### Automated Verification:

- Production build passes with no new budget warnings: `npm run build` (from `PigeonWatch/Frontend`)
- Tests pass, including style guard, label coverage and the new specs: `npm test -- --watch=false` (from `PigeonWatch/Frontend`)
- Dependency audit shows no high or critical findings for production dependencies: `npm audit --omit=dev` (from `PigeonWatch/Frontend`)

#### Manual Verification:

- The build output lists Leaflet only in lazy chunks, and the initial total stays under 500 kB
- The only CSP change in the diff is the single `img-src` tile host

**Implementation Note**: After completing this phase and all automated verification passes, pause here for manual confirmation from the human that the manual testing was successful before proceeding to the next phase. Phase blocks use plain bullets — the corresponding `- [ ]` checkboxes for these items live in the `## Progress` section at the bottom of the plan.

---

## Phase 4: Frontend views

### Overview

Build the four views and wire them in: the map home, the report form, the detail page and the profile list; add the header menu entry; and fix the guard spec that renders the old home page.

### Changes Required:

#### 1. Routes and header

**File**: `PigeonWatch/Frontend/src/app/app.routes.ts`, `app.html`, `core/auth/auth.guards.spec.ts`

**Intent**: Replace the home placeholder with the map and add the two new lazy routes, all behind `authGuard`, before `...devRoutes`.

**Contract**: `''` loads the nearby-map page (the old `features/home` page, its spec and its `home.*` labels are removed); `sightings/new` and `sightings/:id` load the report and detail pages. The header menu gains "Report sighting" above Profile and Log out. `auth.guards.spec.ts:92-99` renders the new page with the map adapter replaced by a stub, so the guard test stays free of Leaflet.

#### 2. Nearby map page

**File**: `PigeonWatch/Frontend/src/app/features/sightings/nearby-map-page/`

**Intent**: The home view: shows open sightings within 10 km of the map centre and handles the unknown-position case.

**Contract**: On load requests position (low accuracy). On `ok`, centres at about zoom 12 and queries nearby once. On any other outcome, opens the neutral Poland view with a hint, makes no query, and waits. A "Search this area" button re-queries using the current map centre at any time. States: loading (spinner), empty (no sightings in this area), error with retry (the warm-up interceptor already covers 503), results. Selecting a marker shows an Angular card below the map with the sighting's name or the unnamed label, status, criticality and a link to the detail page; no popups. A prominent "Report sighting" button sits above the map. Layout stays in the 40rem column.

#### 3. Report form page

**File**: `PigeonWatch/Frontend/src/app/features/sightings/report-sighting-page/`

**Intent**: The phone-usable report form, following the profile page's form pattern (`profile-page.ts:84-130,154-179`).

**Contract**: `providers: [provideFormFieldDefaults()]`; `FormSubmitState.markSubmitted()` first in submit; a `busy` signal with `finalize`. Fields: optional name; required description and behaviour (textareas) with max lengths from the client config; required status (native select, Spotted or Caught, empty first option); required criticality (native select, three levels, empty first option); required location via the pin-picker. Uses `app-page-card`, `app-field-errors`, `app-form-actions` and `app-submit-button`. On success navigates to `sightings/:id`. On a cooldown failure shows an `app-alert` with the remaining seconds; on other coded failures the mapped message. Required errors show on submit, readable at 375 px.

#### 4. Detail page

**File**: `PigeonWatch/Frontend/src/app/features/sightings/sighting-detail-page/`

**Intent**: Show one sighting without an embedded map by default (PRD guardrail, `prd.md:45,121`).

**Contract**: Loads `GET sightings/{id}`. Shows name or unnamed label, status and criticality labels, description, behaviour, reporter display name (never email), reported-at date, and the coordinates as text. A "Show map" toggle renders one map adapter with a single marker; nothing map-related is fetched until it is pressed. States: loading, not found (404), generic error with retry. A back link returns to the map.

#### 5. Profile "My sightings"

**File**: `PigeonWatch/Frontend/src/app/features/sightings/my-sightings/`, `features/profile/profile-page/profile-page.html`, `app/dev/kitchen-sink/`

**Intent**: Add the user's own sightings to the profile page (the placeholder the S-02 plan left, `edit-profile-name/plan-brief.md:21`) as a self-contained component, so the profile page itself gains no data logic.

**Contract**: A component that loads `GET sightings/mine` and renders loading, empty, error with retry, and a list (name or unnamed label, status, criticality, date, link to detail). The profile page includes it below the existing content. The kitchen sink and `profile-page` specs gain only the minimum stub (an empty-result sightings client) so existing Profile cells and markers make no HTTP call and keep passing; no new cells.

#### 6. Specs

**File**: `PigeonWatch/Frontend/src/app/features/sightings/**/*.spec.ts`

**Intent**: Cover view logic with the map adapter stubbed.

**Contract**: Map page: position ok triggers exactly one query at that centre; denied triggers none until "Search this area" is pressed, then queries the stubbed map centre; error and empty states. Report page: required errors only after submit, status select offers only Spotted and Caught, success navigates, cooldown shows the seconds. Detail page: render, 404 and "Show map" toggle. My sightings: four states.

#### 7. Visual gate

**File**: `context/changes/report-sighting-on-map/visual-notes.md`

**Intent**: Record the screenshot evidence, as `profile-ui-contract` did, for the four new views.

**Contract**: Desktop and 375 px screenshots taken in the built-in browser for the map (with pins and with the no-position hint), report form (including required errors), detail (with and without the map), and the profile list. Notes record anything left for a later `/10x-ui` pass.

### Success Criteria:

#### Automated Verification:

- Production build passes with no new budget warnings: `npm run build` (from `PigeonWatch/Frontend`)
- Tests pass, including the new view specs, the updated guard spec and the kitchen-sink spec: `npm test -- --watch=false` (from `PigeonWatch/Frontend`)

#### Manual Verification:

- On the dev server against LocalDB, reporting a sighting via a tapped pin, via "Use my location" and via "Place pin at map centre" each submits and lands on the detail page
- At 375 px, submitting the empty report form shows required errors for description, behaviour, status, criticality and location
- A second submission within 60 seconds shows the message with the remaining seconds
- Map home with location allowed centres on the position and shows pins; with location denied it shows the Poland view and hint, and "Search this area" loads pins
- Detail page shows coordinates without loading tiles, "Show map" then shows the single marker, and an unknown id shows the not-found state
- Profile page shows the user's own sightings, and its empty, loading and error states render
- `visual-notes.md` holds desktop and 375 px screenshots of all four views

**Implementation Note**: After completing this phase and all automated verification passes, pause here for manual confirmation from the human that the manual testing was successful before proceeding to the next phase. Phase blocks use plain bullets — the corresponding `- [ ]` checkboxes for these items live in the `## Progress` section at the bottom of the plan.

---

## Phase 5: Verification and docs

### Overview

Run the full checks once more, prove the CSP live, and update the project documentation, including moving the "nearby" definition into S-03 and narrowing S-05.

### Changes Required:

#### 1. Documentation

**File**: `PigeonWatch/CLAUDE.md`, `PigeonWatch/Api/CLAUDE.md`, `PigeonWatch/Frontend/CLAUDE.md`

**Intent**: Keep the guidance files true to the code, since both already have uncommitted edits in the working tree.

**Contract**: Root product file: S-03 shipped, no longer "no sighting business logic". Api file: the `SIGHTING` table, the four `sightings` endpoints, the cooldown mechanism and its error shape, the integer enum contract, and `retryAfterSeconds`. Frontend file: Leaflet, the `shared/map` module and why it is outside the `shared/ui` barrel, the CSP host and the live-check requirement, the pin-picker control, the geolocation service, how to stub the map in specs, and the minimal sink stub.

#### 2. Roadmap

**File**: `context/foundation/roadmap.md`

**Intent**: Record that S-03 now defines "nearby" so S-05 and S-06 reuse one rule (`roadmap.md:157-158`).

**Contract**: S-03 notes "nearby" as a 10 km radius around the map centre (geolocation or "Search this area"). S-05's open unknown is narrowed to refinements (changing the radius, a named city) and S-06 is noted as inheriting the S-03 rule. Status fields are untouched here; `/10x-implement` and `/10x-archive` own them.

#### 3. Live checks

**File**: none (verification only)

**Intent**: Prove the pieces that unit tests and `ng serve` cannot: the CSP and the end-to-end path on real infrastructure.

**Contract**: Serve the production build with the `staticwebapp.config.json` headers (for example with the Static Web Apps CLI emulator) or use the deployed site after merge, and watch the console and network panel while the map and report form load tiles and submit.

### Success Criteria:

#### Automated Verification:

- Backend builds and both test projects pass: `dotnet test PigeonWatch/Api/PigeonWatchApi.slnx`
- Frontend production build and tests pass: `npm run build` then `npm test -- --watch=false` (from `PigeonWatch/Frontend`)
- Backend dependency audit is clean: `dotnet list PigeonWatch/Api/WebApi.Host/PigeonWatch.WebApi.Host.csproj package --vulnerable --include-transitive`

#### Manual Verification:

- With the CSP headers applied, tiles load from the OSM host and the console shows no CSP violations on the map home and the report form
- On the deployed or header-emulated build, register or log in, report a sighting, see it on the map, open its detail page and see it on the profile
- The three CLAUDE.md files and the roadmap read correctly against the shipped behaviour

**Implementation Note**: After completing this phase and all automated verification passes, pause here for manual confirmation from the human that the manual testing was successful before proceeding. Phase blocks use plain bullets — the corresponding `- [ ]` checkboxes for these items live in the `## Progress` section at the bottom of the plan.

---

## Testing Strategy

### Unit Tests:

- Transition table: every PRD row, resolved set, initial set.
- Service: cooldown at 59 s and 60 s, first-ever sighting, invalid initial status and criticality, bounds validation, radius inside and outside including the box-corner case, resolved excluded, cap and order.
- Repository on SQLite in-memory: box edges, status exclusion, reporter order, latest-by-reporter.
- Controller: 201, 400 with `retryAfterSeconds`, 404, 401.
- SPA: geolocation outcomes, picker value logic, API client, error mapping, each view's states with the map adapter stubbed, updated guard spec, style guard and label coverage.

### Integration Tests:

- Architecture tests cover layering, naming and wire-type rules; `has-pending-model-changes` covers the migration; CI runs both on every PR (`.github/workflows/api-pr-checks.yml:20-69`).

### Manual Testing Steps:

1. Start the API on LocalDB and the SPA with `npm start`; log in.
2. On the map home, allow location and confirm centring; reload with location blocked and confirm the hint and "Search this area".
3. Report a sighting three ways (tap, use my location, place at centre); confirm the detail page and the profile list.
4. Submit twice within 60 seconds and confirm the remaining-seconds message; wait and submit again.
5. At 375 px, repeat the report flow and the empty-submit errors.
6. Apply the CSP headers and confirm no violations.

## Performance Considerations

- The map query is a bounding-box SQL read followed by an exact distance filter in memory, capped at 200 rows, which fits the "small" volume target. A spatial index or NetTopologySuite is the escape hatch if volumes ever outgrow that.
- Each map load may wake the paused serverless database (`deploy-plan.md:147-148`); the existing warm-up interceptor shows the panel on 503. The page queries once on load and only again on an explicit "Search this area", never on every pan.
- Leaflet adds about 42 kB gzipped in a lazy chunk plus about 14 kB raw CSS in the global styles; the initial budget has about 37 kB of headroom.

## Migration Notes

The `AddSighting` migration is additive (new table and indexes only), applied by the CI-built EF bundle with retries on deploy (`deploy-api.yml:38-112`); there is no down-migration in production. The managed identity needs no new permissions because schema changes ship through the bundle.

## References

- Related research: `context/changes/report-sighting-on-map/research.md`
- Frame brief: `context/changes/report-sighting-on-map/frame.md`
- Similar implementation: `PigeonWatch/Api/WebApi/Controllers/AccountController.cs:37-80`, `PigeonWatch/Frontend/src/app/features/profile/profile-page/profile-page.ts:84-179`
- Status and criticality tables: `context/foundation/prd.md:133-156` (PRD v2; it was in the working tree during planning and was committed with Phase 5, fa8b75e, together with the roadmap's PRD v2 alignment and the S-03 status change)
- CSP precedent: `context/archive/2026-10-04-register-and-login/plan.md:513,518,596,775,928`

## Deviations

Recorded after implementation (impl-review F7). These are intended and tested.

- **Marker title carries the sighting name.** `map-view` sets Leaflet's `title` option to the marker label, which can be the user-typed name, as a native hover and accessibility label. Leaflet assigns it as a DOM property, not HTML, so it is not an XSS sink. Popups, tooltips and `divIcon` `html` still never carry user text.
- **`ReporterNotFound` is 401, not a coded 400.** A token for a deleted user is treated like `AccountController`'s `UserNotFound`.
- **The service normalises input.** `SightingService` trims name, description and behaviour, stores a blank name as null, validates before reading the cooldown, and caps `retryAfterSeconds` at 60 so a future-dated `CREATE_DATE` cannot inflate it.
- **The report form has a Cancel button** in `app-form-actions` that returns to the map (`sighting.form.cancel`).
- **Review fixes** (impl-review F1-F4 and F8) added these, after the phases:
  - a Leaflet load-failed alert with retry and a `mapReady` output on `map-view`;
  - a 15 s geolocation backstop;
  - a SQL row cap on the nearby box query (`SightingRules.NearbyBoxMaxRows`);
  - whitespace-only description and behaviour treated as missing;
  - the Leaflet CSS moved to the planned `inject: false` fallback (initial bundle 486 kB).

  See `reviews/impl-review.md`.

## Progress

> Convention: `- [ ]` pending, `- [x]` done. Append ` — <commit sha>` when a step lands. Do not rename step titles. See `references/progress-format.md`.

### Phase 1: Domain and persistence

#### Automated

- [x] 1.1 Solution builds: `dotnet build PigeonWatch/Api/PigeonWatchApi.slnx` — 3ba5058
- [x] 1.2 Architecture tests pass: `dotnet test PigeonWatch/Api/ArchitectureTests/PigeonWatch.ArchitectureTests.csproj` — 3ba5058
- [x] 1.3 Unit tests pass, including the transition table and repository tests: `dotnet test PigeonWatch/Api/UnitTests/PigeonWatch.UnitTests.csproj` — 3ba5058
- [x] 1.4 No pending model changes: `dotnet ef migrations has-pending-model-changes --project PigeonWatch/Api/Data/PigeonWatch.Data.csproj --startup-project PigeonWatch/Api/WebApi.Host/PigeonWatch.WebApi.Host.csproj` — 3ba5058

#### Manual

- [x] 1.5 `dotnet ef database update` (command in `Api/CLAUDE.md`) applies the migration to LocalDB and the `SIGHTING` table shows the FK and both indexes — 3ba5058

### Phase 2: Service and API

#### Automated

- [x] 2.1 Solution builds: `dotnet build PigeonWatch/Api/PigeonWatchApi.slnx` — 3ba5058
- [x] 2.2 Architecture tests pass: `dotnet test PigeonWatch/Api/ArchitectureTests/PigeonWatch.ArchitectureTests.csproj` — 3ba5058
- [x] 2.3 Unit tests pass, including cooldown boundaries, radius filtering and controller mapping: `dotnet test PigeonWatch/Api/UnitTests/PigeonWatch.UnitTests.csproj` — 3ba5058
- [x] 2.4 No pending model changes: `dotnet ef migrations has-pending-model-changes --project PigeonWatch/Api/Data/PigeonWatch.Data.csproj --startup-project PigeonWatch/Api/WebApi.Host/PigeonWatch.WebApi.Host.csproj` — 3ba5058

#### Manual

- [x] 2.5 With the API running on LocalDB and a bearer token from `POST auth/login` (use `curl.exe`), a create returns 201, fetching it by id returns 200, an unknown id returns 404, `nearby` returns it for a centre within 10 km and not for a centre 15 km away, and a second create within 60 seconds returns 400 with `retryAfterSeconds` — 3ba5058

### Phase 3: Frontend foundation

#### Automated

- [x] 3.1 Production build passes with no new budget warnings: `npm run build` (from `PigeonWatch/Frontend`) — 3ba5058
- [x] 3.2 Tests pass, including style guard, label coverage and the new specs: `npm test -- --watch=false` (from `PigeonWatch/Frontend`) — 3ba5058
- [x] 3.3 Dependency audit shows no high or critical findings for production dependencies: `npm audit --omit=dev` (from `PigeonWatch/Frontend`) — 3ba5058

#### Manual

- [x] 3.4 The build output lists Leaflet only in lazy chunks, and the initial total stays under 500 kB — 3ba5058
- [x] 3.5 The only CSP change in the diff is the single `img-src` tile host — 3ba5058

### Phase 4: Frontend views

#### Automated

- [x] 4.1 Production build passes with no new budget warnings: `npm run build` (from `PigeonWatch/Frontend`) — 3ba5058
- [x] 4.2 Tests pass, including the new view specs, the updated guard spec and the kitchen-sink spec: `npm test -- --watch=false` (from `PigeonWatch/Frontend`) — 3ba5058

#### Manual

- [x] 4.3 On the dev server against LocalDB, reporting a sighting via a tapped pin, via "Use my location" and via "Place pin at map centre" each submits and lands on the detail page — 3ba5058
- [x] 4.4 At 375 px, submitting the empty report form shows required errors for description, behaviour, status, criticality and location — 3ba5058
- [x] 4.5 A second submission within 60 seconds shows the message with the remaining seconds — 3ba5058
- [x] 4.6 Map home with location allowed centres on the position and shows pins; with location denied it shows the Poland view and hint, and "Search this area" loads pins — 3ba5058
- [x] 4.7 Detail page shows coordinates without loading tiles, "Show map" then shows the single marker, and an unknown id shows the not-found state — 3ba5058
- [x] 4.8 Profile page shows the user's own sightings, and its empty, loading and error states render — 3ba5058
- [x] 4.9 `visual-notes.md` holds desktop and 375 px screenshots of all four views — 3ba5058 (partial, per impl-review F6: Profile "My sightings" was seen but not committed because it shows the account email; map-selected at 375, report form untouched at 375 and detail not-found on desktop were not captured)

### Phase 5: Verification and docs

#### Automated

- [x] 5.1 Backend builds and both test projects pass: `dotnet test PigeonWatch/Api/PigeonWatchApi.slnx` — 3ba5058
- [x] 5.2 Frontend production build and tests pass: `npm run build` then `npm test -- --watch=false` (from `PigeonWatch/Frontend`) — 3ba5058
- [x] 5.3 Backend dependency audit is clean: `dotnet list PigeonWatch/Api/WebApi.Host/PigeonWatch.WebApi.Host.csproj package --vulnerable --include-transitive` — 3ba5058

#### Manual

- [x] 5.4 With the CSP headers applied, tiles load from the OSM host and the console shows no CSP violations on the map home and the report form — 3ba5058
- [x] 5.5 On the deployed or header-emulated build, register or log in, report a sighting, see it on the map, open its detail page and see it on the profile — 3ba5058
- [x] 5.6 The three CLAUDE.md files and the roadmap read correctly against the shipped behaviour — 3ba5058
