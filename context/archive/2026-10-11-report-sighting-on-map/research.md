---
date: 2026-10-11T15:17:13+02:00
researcher: Claude (claude-opus-5-5)
git_commit: 1b75b8f6d89b46559eabc43cf145801404adf6c8
branch: main
repository: DominikBerniak/pigeon-watch
topic: "S-03 report-sighting-on-map: what the codebase provides, what it constrains, and what must be decided before planning"
tags: [research, codebase, sighting, map, leaflet, maplibre, ef-core, rate-limiting, angular, csp]
status: complete
last_updated: 2026-10-11
last_updated_by: Claude (claude-opus-5-5)
---

# Research: S-03 Report a sighting and see it on the map

**Date**: 2026-10-11T15:17:13+02:00
**Researcher**: Claude (claude-opus-5-5)
**Git Commit**: 1b75b8f6d89b46559eabc43cf145801404adf6c8 (working tree has uncommitted edits to `prd.md`, `roadmap.md` and both `PigeonWatch` CLAUDE.md files; anchors into those files are local)
**Branch**: main
**Repository**: DominikBerniak/pigeon-watch

## Research Question

For roadmap slice S-03 (`context/foundation/roadmap.md:124-134`): a logged-in user reports a sighting (location, description, behaviour, optional name, initial status Spotted or Caught, criticality Monitor / Needs help soon / Urgent), sees it on the nearby map and on their own profile, and opens its detail page; submissions are throttled by the FR-019 cooldown. What does the current codebase already provide, which conventions and guards will a new sighting feature have to satisfy, what does the first map integration require under the existing CSP, bundle budget and test setup, and which decisions are still open for `/10x-plan`?

Paths are relative to the repo root unless noted. `Api/` = `PigeonWatch/Api/`, `Frontend/` = `PigeonWatch/Frontend/`.

## Summary

- **Nothing sighting-related exists.** The database has one table, `USER_ACCOUNT` (`Api/Data/PigeonWatchDbContext.cs:9,16-27`), and the home page is a placeholder reserved for S-03 (`Frontend/src/app/features/home/home-page/home-page.html:1-4`, `Api/BusinessObjects/Resources/UiLabels.resx:84-86`, `context/archive/2026-10-04-register-and-login/plan.md:711`). S-03 is the first slice to add a domain table, a persisted enum, numeric/geo columns, a FK to `USER_ACCOUNT`, a list endpoint, a 404 and a per-user throttle. The code has no precedent for any of these (inspected: `Api/**` excluding `bin/obj`; worker sweep, see Detailed Findings §1-3).
- **The backend chain is well defined and test-enforced.** Controller → service → repository → entity → mapper → view model creator, each with an interface in an `Interfaces/` folder and DI registration, guarded by 8 architecture test classes (`Api/ArchitectureTests/`). Three of these rules shape the sighting design directly. (a) API models may not expose any `BusinessObjects` type (`Api/ArchitectureTests/ControllerActionTests.cs:30-47`), so status and criticality enums need a wire representation of their own. (b) A static class may hold only `const`s (`Api/ArchitectureTests/StaticClassTests.cs:13-26`), so the transition table cannot be a static helper. (c) Every class named `…Provider`/`…Service`/`…Repository`/`…Mapper`/`…ViewModelCreator` needs an `I<Name>` interface and registration (`ServiceRegistrationTests.cs`, per worker).
- **Business failures return 400 `ValidationProblemDetails` keyed by error code** (`Api/WebApi/Controllers/AccountController.cs:37-53,74-80`), and the SPA maps codes to label keys (`Frontend/src/app/shared/auth/auth-errors.ts:89-155`, per worker). The FR-019 acceptance criterion requires "a clear message" (`context/foundation/prd.md:58`), which fits a coded service-level error better than the existing per-IP 429 limiter. That limiter sends no body (`Api/WebApi.Host/Program.cs:37-54`).
- **The first map must stay out of the initial bundle and needs a CSP change.** The CSP allows images and connections only from `'self'`, `data:` and the API host (`Frontend/public/staticwebapp.config.json:10`). The initial bundle is measured at 462.5-463.2 kB against a 500 kB warning budget (`Frontend/angular.json:66-77`, `context/archive/2026-10-11-profile-ui-contract/reviews/impl-review.md:24`). Leaflet 1.9.4 is about 42 kB gz of JS. MapLibre 6.13 is about 285 kB gz plus a 143 kB gz worker. Both are fine as a lazy route chunk. They differ in CSP surface, build gotchas and testability (Detailed Findings §5).
- **No project document chooses a map provider, tile source, location-capture method, cooldown duration, lat/lng storage type or "nearby" scope** (searched `context/**` except `credentials.local.md`). These are the open choices for planning (see Open Questions). The roadmap assigns the "nearby" definition to S-05 (`context/foundation/roadmap.md:157-158`).

## Detailed Findings

### 1. Backend: the pattern a sighting feature follows

- **S-02 is the latest authorized feature.** It added no table and reused `USER_ACCOUNT` (latest migration `Api/Data/Migrations/20261004115814_AddUserAccount.cs`). Its chain:
  - controller `Api/WebApi/Controllers/AccountController.cs:37-72`;
  - request/response models `WebApi/Models/UpdateProfileRequestModel.cs`, `UpdatedProfileModel.cs` (`sealed class`, `required … { get; init; }`);
  - view model creator `WebApi/ViewModelCreators/UpdatedProfileViewModelCreator.cs:6-16`;
  - service `BusinessLogic/Services/AccountService.cs:24-39`;
  - repository `Data/Repositories/AccountRepository.cs:41-75`;
  - result record `BusinessObjects/ProfileUpdateResult.cs:3-14` with `Success`/`Failure` factories;
  - DI `AddScoped<IX, X>()` in `DependencyInjection/{WebApi,BusinessLogic,Data}ServiceCollectionExtensions.cs`.
  
  Request mappers exist (`WebApi/Mappers/RegisterRequestMapper.cs:6-12`); S-02 did not use one.
- **Current user id.** Each controller action reads it inline: `Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out Guid userId)` else `Unauthorized()` (`AccountController.cs:41-42`; also `:60`, `ConfigurationController.cs:35`). There is no helper or base class.
- **Error shape.** Failures carry `AccountError(Code, Description)` with codes in `BusinessObjects/AccountErrorCodes.cs`. A `UserNotFound` code becomes 401; any other failure goes through `GetValidationProblem` → `ModelState.AddModelError(code, description)` → 400 `ValidationProblemDetails` (`AccountController.cs:46-50,74-80`). Both types are account-named, so sightings need their own error/result types or a generalized one. No controller returns 404, 409 or 429 today (worker sweep of `WebApi/Controllers/`).
- **Entities and DbContext.**
  - Configuration is inline in `OnModelCreating`, with no `IEntityTypeConfiguration` classes (`Data/PigeonWatchDbContext.cs:16-27`).
  - `ToTable` takes a PascalCase singular name, and `UpperSnakeCaseNamingConvention` turns it into `UPPER_SNAKE` (`PigeonWatchDbContext.cs:11-14`, `Data/Conventions/UpperSnakeCaseNamingConvention.cs`).
  - Entities are `internal`, derive from `AuditableEntity` (`Data/Entities/AuditableEntity.cs:5-16`) and get a sequential-GUID `Id` from `Entity` (`Data/Entities/Entity.cs:3-6`).
  - Lengths come from `[MaxLength(...)]` attributes using `BusinessObjects` consts (`Data/Entities/UserAccountEntity.cs:20,23`).
  - The `USER_ACCOUNT` key is `ID uniqueidentifier` (`AddUserAccount.cs:18,38`), so a reporter FK would be a `Guid` property. No FK or navigation exists yet.
  - Inside `Api/` (worker grep) there are no persisted enums, no `HasConversion`, no `decimal`/`double`/`HasPrecision` columns and no NetTopologySuite package. The only EF packages are SqlServer and Sqlite 10.0.12 (`Api/Data/PigeonWatch.Data.csproj:16`).
- **Audit columns already record the reporter.** `CREATE_USER` holds the user id string (`Data/Auditing/HttpContextCurrentUserProvider.cs:10-20`), and `CREATE_DATE` is a UTC `datetime2` set by `AuditSaveChangesInterceptor` from `TimeProvider` (`Data/Auditing/AuditSaveChangesInterceptor.cs:27-52`). A typed FK column is still needed for joins to the display name and for an index.
- **Client validation rules.**
  - `GET configuration/client` serves the consts in `BusinessObjects/AccountRules.cs:3-14` through `ClientConfiguration` → `ClientConfigurationModel` with nested rule models (`WebApi/Models/ClientConfigurationModel.cs:3-8`, `ConfigurationController.cs:19-27`).
  - On the SPA side, the `ClientConfiguration` type is `{passwordRules, displayNameRules}` (`Frontend/src/app/core/configuration/configuration.service.ts:20-23`), read through `computed` with fallbacks (`Frontend/src/app/features/profile/profile-page/profile-page.ts:84-93`).
  - Sighting rules (description and behaviour max lengths, name max length, possibly the cooldown seconds) would extend both sides the same way.
- **No list or query precedent.** No `[FromQuery]`, `Skip`/`Take` or collection response exists in `Api/WebApi`. The only parameterized GET is `ResourcesController.cs:16-25`.

### 2. Backend guards a new feature must satisfy

The 8 architecture test classes are in `Api/ArchitectureTests/`. These are the rules that matter for S-03:

| Rule | Source | Consequence for S-03 |
|---|---|---|
| Actions return exactly `Task<ActionResult<T>>`, `T` in `PigeonWatch.WebApi.Models` | `ControllerActionTests.cs:10-28` | A list endpoint returns a wrapper model or `List<XModel>` (generic arguments are inspected recursively, `:30-47`) |
| API models expose no BusinessObjects or Data type, recursively | `ControllerActionTests.cs:30-47` | `SightingStatus`/`Criticality` enums in BusinessObjects cannot appear on models; use strings or WebApi-local enums mapped in the view model creator |
| Static classes are const-only or DI extensions | `StaticClassTests.cs:13-26` | The allowed-transitions table lives as a `static readonly` member of a non-static class (Api CLAUDE.md coding rules) |
| `Interfaces/` folders hold only interfaces (source scan) | `InterfaceLocationTests.cs` | Enums go elsewhere, e.g. `BusinessObjects/` root |
| Types ending in `Model` must be in `PigeonWatch.WebApi.Models` | `TypeLocationTests.cs:9-30` | Avoid `…Model` names in BusinessObjects |
| UPPER_SNAKE identifiers, singular tables, single `ID uniqueidentifier` PK, 4 NOT NULL audit columns | `DatabaseModelTests.cs:16-163` | A `SIGHTING` table passes the singular check (worker, Humanizer last-word check `:68-104`) |
| Every awaitable has a defaulted `CancellationToken`; actions have one with no default | `CancellationTokenTests.cs:10-42` | — |
| `…Provider/Service/Repository/Mapper/ViewModelCreator` has `I<Name>` and is registered | `ServiceRegistrationTests.cs:15-72` | Applies to any cooldown or transition provider |

- PR CI runs build, architecture tests, unit tests and `dotnet ef migrations has-pending-model-changes` (`.github/workflows/api-pr-checks.yml:20-46`). A model change without a migration fails the PR.
- Deploy builds a linux-x64 EF bundle and applies it with 5 retries, 30 s apart (`.github/workflows/deploy-api.yml:38-112`). Migrations must be additive and backward compatible, and there is no automatic down-migration (`context/deployment/deploy-plan.md:109-117`).
- The managed identity has only `db_datareader`/`db_datawriter` (`deploy-plan.md:74`). On the inspected deploy path (`deploy-api.yml:38-112`), schema changes reach production through the CI migration bundle, so a spatial index, if chosen, would also ship as a migration.

### 3. Cooldown (FR-019) and rate limiting

- **Existing limiter.** The single `auth` policy is a fixed window of 10 requests per minute partitioned by `RemoteIpAddress`, with 429 and no body (`Api/WebApi.Host/Program.cs:37-54`, name in `Api/WebApi/RateLimiting/RateLimitPolicyNames.cs:3-6`). Pipeline order is `UseAuthentication` (`Program.cs:72`) then `UseRateLimiter` (`:74`), so a policy could partition by `NameIdentifier`. A per-user partition was already listed as a later option in `context/archive/2026-10-10-edit-profile-name/follow-ups/review-fixes.md:5-11`.
- **Persisted per-account throttle precedent.** The only one is Identity lockout (5 attempts / 5 minutes, `Api/DependencyInjection/AccountServiceCollectionExtensions.cs:20-22`).
- **In-memory limiter on F1.** On App Service F1, an in-memory limiter loses its counters whenever the process restarts or sleeps on idle (`deploy-plan.md:153-155`, `infrastructure.md:57-61`). A check against the user's latest sighting `CREATE_DATE` survives restarts and can return a coded error with the remaining wait. It costs one indexed query per submission. Which approach to take is a planning choice.
- **Requirements.** FR-019 requires a cooldown "short enough that genuine back-to-back reports during an outing aren't blocked" and also applies it to comments (`context/foundation/prd.md:115`). No duration is set anywhere in `context/**` (worker search). The S-07 comment slice will reuse whatever mechanism S-03 picks.
- **Time source.** `TimeProvider` is registered as a singleton (`Api/DependencyInjection/DataServiceCollectionExtensions.cs:20`), but `AuditSaveChangesInterceptor` is its only consumer. Unit tests use `TimeProvider.System`, and there is no fake-time precedent (worker sweep of `Api/UnitTests/`).

### 4. Frontend: where S-03 plugs in

- **Routes.** Every route is lazy `loadComponent`. `''` (authGuard) → `HomePage`, then `login`/`register` (guestGuard), `profile` (authGuard), `...devRoutes`, and `**` → `unknownRouteRedirect` (`Frontend/src/app/app.routes.ts:5-35`). New `sightings/new` and `sightings/:id` routes go before `...devRoutes`. The S-01 guard test already uses `/sightings/1` as its example deep link (`context/archive/2026-10-04-register-and-login/plan.md:612`).
- **Header and layout.**
  - The header is in the root shell. A `mat-menu` has two items, Profile and Logout (`Frontend/src/app/app.html:3-20`), and there is no nav bar. Unused `nav.*` labels already exist (`UiLabels.resx:24-35`, per worker).
  - `.app-main` is capped at `--pw-content-max-width` (40rem) (`Frontend/src/app/app.scss:20-24`). A full-width or tall map page needs a layout variant, and there is no map-height token (`Frontend/src/styles/_tokens.scss:67-73`, per worker).
- **Profile page.** The page is a single `app-page-card` (`Frontend/src/app/features/profile/profile-page/profile-page.html:1-77`). It loads no data on init: it reads `session.currentUser` and makes only a PUT (`profile-page.ts:78-79,162-178`). A "my sightings" section adds the page's first GET with loading, empty and error states. The S-02 plan expected this ("gives S-03 a place to add sightings", `context/archive/2026-10-10-edit-profile-name/plan-brief.md:21`).
- **Feature pattern.**
  - Services: a `providedIn: 'root'` service builds `${environment.apiUrl}/...` URLs and returns Observables (`Frontend/src/app/features/profile/profile-api.ts:11-20`).
  - Components: a `busy` signal with `finalize`, `FormSubmitState.markSubmitted()` first in submit, Signal Forms validators fed from `clientConfig`, and `provideFormFieldDefaults()` on the page (Frontend CLAUDE.md; `profile-page.ts:84-130,154-179`).
  - Interceptors handle the bearer token with one refresh retry, and show the warm-up panel on 503/0 (`Frontend/src/app/app.config.ts:19`).
  - Error mapping lives in auth-named `shared/auth/auth-errors.ts`. Per the "promote on second use" rule (Frontend CLAUDE.md), S-03 either adds `sighting-errors.ts` or generalizes it.
- **shared/ui.**
  - It exports `Alert`, `Brand`, `FieldErrors`, `FormActions`, `PageCard`, `SubmitButton` and the form-field defaults (`Frontend/src/app/shared/ui/index.ts:1-12`).
  - It has nothing for select, radio or button-toggle (status, criticality), list or card items, empty or loading states, or a map.
  - There is no amber/warning colour token for a middle criticality level (`_tokens.scss:2-20`, per worker).
- **Labels.** Every UI text is a key in `UiLabels.resx`. `label-coverage.spec.ts` checks that every literal `'key' | translate` / `translate('key'` exists and that templates contain no literal text (per worker, `:21-22,97-115`). A dynamic `translate('sighting.status.' + x)` would escape that check, so a static enum → key map keeps coverage.
- **Style guard.** `src/styles/style-token-coverage.spec.ts` scans `.scss` under `src/app` plus `_base.scss`/`_common.scss`, and every `.html` under `src/app`. It does not scan `.css` files, `node_modules` or `src/styles.scss` (per worker, `:33-39`). Vendor map CSS must therefore come in via `angular.json` `styles` or an `@use` of the package path, never by copying it into a component `.scss`. Library-created DOM is outside component encapsulation and `::ng-deep` is banned, so any marker or popup styling must be global and token-based. Marker colours passed as JS options would bypass the token rule's intent.
- **Kitchen sink.** It is Profile-only: `StateCell` hard-wires `ProfilePage` and stubs Profile services (`Frontend/src/app/dev/kitchen-sink/state-cell.ts:33-38`, `cell-definitions.ts:143-146`, per worker). New sighting views need a generalized cell or a parallel sink if the visual gate from `context/archive/2026-10-11-profile-ui-contract/plan.md:29,46,61` is to apply to them.
- **Tests.** The tests use Vitest + jsdom through `@angular/build:unit-test` with no setup file and no `ResizeObserver`/`matchMedia` shims (`Frontend/angular.json:118-120`, per worker). jsdom has no layout, canvas, WebGL or `navigator.geolocation`.

### 5. Map integration constraints (external sources checked 2026-10-11)

**Current CSP** (`Frontend/public/staticwebapp.config.json:10`):

`default-src 'self'; script-src 'self'; style-src 'self' 'unsafe-inline'; img-src 'self' data:; font-src 'self'; connect-src 'self' https://pigeonwatch-api.azurewebsites.net; …`

There is no `worker-src`, so workers fall back to `script-src 'self'`. Earlier plans treat the CSP as the mitigation for the refresh token kept in `localStorage` (`context/archive/2026-10-04-register-and-login/plan-brief.md:96`). Widening it is a recorded security trade-off and needs a matching live "no CSP violations" check (`register-and-login/plan.md:775,928`).

**Budgets** (`Frontend/angular.json:66-77`, production): `initial` warning at 500 kB, error at 1 MB; `anyComponentStyle` warning at 4 kB, error at 8 kB. Global `styles` contains only `src/styles.scss` (`angular.json:55-57`), and anything added there lands in the initial bundle. Prior plans say to lazy-load rather than raise the budget (`register-and-login/plan.md:597`).

| | Leaflet 1.9.4 | MapLibre GL JS 6.13.0 |
|---|---|---|
| Size (measured from npm tarballs, gzip -9) | JS 42.4 kB gz, CSS 3.5 kB gz | JS 285 kB gz + worker 143 kB gz, CSS 10.5 kB gz |
| Release status | 1.9.4 from 2023-05-18. 2.0 is `2.0.0-alpha.1` (2025-08-16): ESM-only, no global `L`, no bundled types, `@types/leaflet` covers 1.x only | Released almost weekly. v6 (July 2026): ESM-only, built-in types, WebGL2 required |
| Angular esbuild | UMD entry with no `module`/`exports`, so it is CommonJS. Needs `allowedCommonJsDependencies` (none set today) or the `leaflet-src.esm.js` path | Native ESM, but the worker must be copied via `assets` and set with `setWorkerUrl()`; otherwise no tiles render |
| Known gotcha | Default marker icon path breaks once the builder hashes CSS `url()`s. Copy `dist/images` and set icon URLs, or use a `divIcon` | Needs style JSON, glyphs and sprites over `connect-src` |
| CSP additions | `img-src https://<tile-host>` | `worker-src 'self'`, `img-src blob:`, `connect-src` for tile/style/glyph hosts |
| Tiles | Raster: OSM, Stadia, Thunderforest, Azure Maps | Vector: OpenFreeMap (no key), MapTiler, Stadia |
| jsdom | Constructs, but its size is 0×0; usually mocked | Throws without WebGL2; must be mocked |
| Angular wrapper | `@bluehalo/ngx-leaflet` 22.0.0 (peers Angular 22, Leaflet 1) | `@maplibre/ngx-maplibre-gl` 22.2.0 (peers Angular ≥22, maplibre ≥6) |

Sources: npm registry pages for each package; https://leafletjs.com/2025/05/18/leaflet-2.0.0-alpha.html; https://maplibre.org/maplibre-gl-js/docs/; https://maplibre.org/maplibre-gl-js/docs/guides/v5-to-v6-migration-guide/; https://github.com/bluehalo/ngx-leaflet.

**Tile providers** (no API keys are stored in the repo today):

- **OSM standard tiles** (https://operations.osmfoundation.org/policies/tiles/): free and best-effort with no SLA. Requires visible attribution and a valid Referer, so the site must not send `Referrer-Policy: no-referrer`. Bulk or offline prefetch is forbidden, and access may be withdrawn. The policy sets no explicit size threshold.
- **OpenFreeMap** (https://openfreemap.org/): vector tiles with no key and no limits, donation-funded, no SLA.
- **MapTiler** (https://www.maptiler.com/cloud/pricing/): 5,000 map loads per month, origin-restricted keys, pauses at the cap.
- **Stadia Maps** (https://stadiamaps.com/pricing/): 200k credits per month, domain auth with no key in the browser, non-commercial.
- **Thunderforest** (https://www.thunderforest.com/pricing/): Hobby plan, 150k tiles per month, with a key.
- **Azure Maps Gen2** (https://azure.microsoft.com/en-us/pricing/details/azure-maps/): 5,000 free tile transactions per month (15 tiles = 1 transaction). Only SAS tokens with `maxRatePerSecond` give a hard cap. A shared key would be exposed in the SPA.

**Detail-page guardrail.** The PRD forbids an embedded interactive map by default on the detail page (`context/foundation/prd.md:45,121`; `PigeonWatch/CLAUDE.md:38`). The detail page therefore shows coordinates or text, optionally with an opt-in map or an external "open in maps" link.

**Geolocation** (MDN `getCurrentPosition`, Permissions-Policy `geolocation`):

- Requires HTTPS (localhost counts) and a permission prompt.
- `timeout` defaults to `Infinity`, so always set it.
- No `Permissions-Policy` header is set today (CSP line only, `staticwebapp.config.json:10`), so the default `self` allowlist applies.
- iOS Safari re-prompts often, and `navigator.permissions.query` is unreliable there (WebKit bugs 275950, 275479).
- Since May 2026, Chrome Android lets users grant only approximate location.
- Planning implication: a manual pick on the map is needed as a fallback whatever capture method is chosen.

**Lat/lng storage.**

- **NetTopologySuite.** `Microsoft.EntityFrameworkCore.SqlServer.NetTopologySuite` 10.0.12 maps to `geography`, with X = longitude. Distance functions are translated (https://learn.microsoft.com/en-us/ef/core/providers/sql-server/spatial). Azure SQL lists spatial support without a tier caveat (https://learn.microsoft.com/en-us/azure/azure-sql/database/features-comparison). Downside: the SQLite in-memory unit tests (`Api/UnitTests/PigeonWatchUserStoreTests.cs:18-27`) cannot use it without SpatiaLite.
- **Plain columns.** Two `double` or `decimal(9,6)` columns with a bounding-box query need no package. EF's SQLite provider cannot translate comparisons or ORDER BY on `decimal`, so `double` keeps SQLite repository tests working (worker note; not run).

## Code References

- `Api/WebApi/Controllers/AccountController.cs:37-80` - authorized action pattern, inline user id, `GetValidationProblem`
- `Api/BusinessObjects/ProfileUpdateResult.cs:3-14` - result record with `Success`/`Failure` factories
- `Api/BusinessObjects/AccountRules.cs:3-14` - const rules served to the SPA
- `Api/Data/PigeonWatchDbContext.cs:9-27` - DbSet and inline entity configuration
- `Api/Data/Entities/AuditableEntity.cs:5-16`, `Entity.cs:3-6` - base entities
- `Api/Data/Auditing/AuditSaveChangesInterceptor.cs:27-52` - audit fill from `ICurrentUserProvider` + `TimeProvider`
- `Api/WebApi.Host/Program.cs:37-54,72-74` - rate limiter and pipeline order
- `Api/ArchitectureTests/ControllerActionTests.cs:10-47` - action return type and wire-type rules
- `Api/ArchitectureTests/StaticClassTests.cs:13-26` - static class rule
- `Api/ArchitectureTests/DatabaseModelTests.cs:16-163` - SQL naming, PK and audit column checks
- `Api/UnitTests/PigeonWatchUserStoreTests.cs:18-27,244-254` - SQLite in-memory fixture pattern
- `Api/UnitTests/AccountControllerTests.cs:196-226` - controller test wiring with a principal and a `ProblemDetailsFactory` stub
- `Frontend/src/app/app.routes.ts:5-35` - route table
- `Frontend/src/app/app.html:3-20` - header and user menu
- `Frontend/src/app/app.scss:20-24` - content max-width
- `Frontend/src/app/features/home/home-page/home-page.html:1-4` - placeholder for S-03
- `Frontend/src/app/features/profile/profile-page/profile-page.html:1-77` - profile card
- `Frontend/src/app/core/configuration/configuration.service.ts:20-23,45-51` - client config type and load
- `Frontend/src/app/shared/ui/index.ts:1-12` - shared components
- `Frontend/public/staticwebapp.config.json:10` - CSP
- `Frontend/angular.json:55-57,66-77,118-120` - global styles, budgets, test target
- `.github/workflows/api-pr-checks.yml:20-69` - PR checks
- `.github/workflows/deploy-api.yml:38-112` - migration bundle

## Architecture Insights

- **Layering keeps enums off the wire.** Status and criticality are domain types in `BusinessObjects`, while the API exposes its own representation. This is the first time a view model creator has to translate an enum. Choosing string or integer wire values decides what the SPA stores and how labels map.
- **The transition graph belongs in BusinessLogic, defined in S-03.** The roadmap says S-03 should define the full status/criticality types and transition graph even though only the initial status is settable now (`context/foundation/roadmap.md:133`). The PRD table is the source (`context/foundation/prd.md:133-156`). Under the static-class rule it becomes an injected provider or a `static readonly` set on a non-static class.
- **Database wake-ups cost free-tier quota.** The DB auto-pauses on the free tier, about 55 h online per month (`deploy-plan.md:147-148`). A map load while the DB is paused wakes it, and the paused DB answers 503, which shows the warm-up panel (`Frontend/src/app/core/warmup/warmup.interceptor.ts:13-81`). A bounded, viewport- or area-scoped query keeps payloads small.
- **Map library behind a thin adapter.** jsdom cannot render either library, and the kitchen sink stubs services per cell. A thin map component or adapter that can be replaced in specs and sink cells fits both existing patterns. The pin logic (lat/lng from a tap or from geolocation) stays as plain testable code.

## Historical Context (from prior changes)

- `context/archive/2026-10-03-persistence-wiring-smoke/plan.md:46,52,79-82` - each slice adds its own tables and migrations, with no generic repository and the naming, audit and GUID-key conventions. Still valid: confirmed by `DatabaseModelTests.cs`.
- `context/archive/2026-10-04-register-and-login/plan.md:597` - keep the 500 kB budget and lazy-load instead. Still valid: the budget is unchanged in `angular.json:66-77`.
- `context/archive/2026-10-04-register-and-login/plan.md:518,596` - the CSP value and the practice of naming CSP changes explicitly in plans. The value matches `staticwebapp.config.json:10` today.
- `context/archive/2026-10-04-register-and-login/plan.md:711` - the home page body is the placeholder for S-03. Still valid: `home-page.html:1-4`.
- `context/archive/2026-10-10-edit-profile-name/plan-brief.md:21` - the profile page is where S-03 adds the user's own sightings.
- `context/archive/2026-10-10-edit-profile-name/reviews/impl-review.md:83-91,158-166` - `core/` must not import `features/`, and the initial bundle was 462.52 kB. Partly superseded: 463.20 kB after profile-ui-contract (`context/archive/2026-10-11-profile-ui-contract/reviews/impl-review.md:24`).
- `context/archive/2026-10-10-edit-profile-name/follow-ups/review-fixes.md:5-11` - IP-only rate limiting was accepted for MVP, and partitioning by user id was named as a later option.
- `context/archive/2026-10-11-profile-ui-contract/plan.md:29,46,61` - the visual gate uses the dev-only kitchen sink with desktop and 375 px screenshots in the built-in browser, and there is no screenshot runner.
- `context/deployment/deploy-plan.md:77` - Blob Storage wiring belongs to S-04, not S-03.
- `context/foundation/lessons.md:5-10` - any command for the user must use `curl.exe` in PowerShell. This applies to the plan's manual verification steps.

## Related Research

- `context/archive/2026-10-04-register-and-login/research.md` - auth and SPA foundations
- `context/archive/2026-10-04-register-and-login/external-research.md` - earlier external research (auth)
- `context/archive/2026-10-11-profile-ui-contract/research.md` - design-system contract and visual-gate method

## Open Questions

These are decisions for `/10x-plan` or the user. No project source settles any of them.

1. **Map library and tile source.**
   - Leaflet 1.9.4 with raster tiles (small, CommonJS, frozen 1.x line), or MapLibre 6 with vector tiles (large lazy chunk, worker setup, wider CSP).
   - Which tile host: OSM, OpenFreeMap, or a keyed free tier.
   - Whether to use the Angular wrapper or the library directly.
   - Each choice fixes the exact CSP additions.
2. **Location capture.** A tap or drag pin on the map, "use my location" via geolocation, or both. A manual fallback is needed regardless (§5).
3. **Lat/lng storage.** Plain `double` columns with a bounding-box query, or NetTopologySuite `geography`. The choice affects SQLite unit tests and later S-05 radius queries.
4. **"Nearby" in S-03.** S-05 owns the definition (`roadmap.md:157-158`). S-03 still has to choose its first query shape: the current map viewport bounds, a fixed radius around the user, or all sightings with a cap.
5. **Cooldown.**
   - Duration (not set anywhere).
   - Mechanism: a DB check on the latest `CREATE_DATE` returning a coded 400 with the remaining time, or a per-user rate-limiter partition returning 429. Restart behaviour on F1 and the "clear message" requirement both matter here.
   - Whether S-03 builds it so S-07 comments can reuse it.
6. **Enum wire format and persistence.** String or integer in the database (`HasConversion<string>()` with a max length, or int), and string values or a WebApi enum on the wire.
7. **Detail-page location display.** Coordinates text, an opt-in "show map" toggle, or an external maps link. Any of these satisfies the guardrail (`prd.md:45,121`).
8. **Home vs `/map`.** Whether the map replaces the home placeholder or gets its own route, and whether the header gains navigation ("Report sighting", "Map") beyond the user menu.
9. **Visual gate scope.** Whether S-03 generalizes the Profile-only kitchen sink to cover the new views' states, or defers that to a later `/10x-ui` pass on these views (the root `CLAUDE.md` routes restyles of existing views through `/10x-ui`).

Unverified items, which do not block planning:

- The current initial bundle size (no build was run).
- Whether `CREATE SPATIAL INDEX` works on the serverless free-limit database.
- The exact Angular builder steps for the MapLibre worker (inferred from MapLibre's bundler docs).
- Whether a Leaflet CSS file imported into a component stylesheet would exceed `anyComponentStyle`. It is about 14 kB raw against an 8 kB error threshold (per worker), so treat it as likely.
