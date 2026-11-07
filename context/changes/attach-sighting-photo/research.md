---
date: 2026-11-03T09:35:19+01:00
researcher: Claude (Opus 5.5) for Dominik Berniak
git_commit: 7e36512
branch: main
repository: 10xDevs (PigeonWatch)
topic: "S-04 attach-sighting-photo: what exists, what constrains it, what must be decided"
tags: [research, sightings, photo, blob-storage, upload, csp, privacy, angular, aspnetcore]
status: complete
last_updated: 2026-11-03
last_updated_by: Claude (Opus 5.5)
---

# Research: S-04 attach-sighting-photo

**Date**: 2026-11-03T09:35:19+01:00
**Researcher**: Claude (Opus 5.5) for Dominik Berniak
**Git Commit**: 7e36512
**Branch**: main
**Repository**: 10xDevs (PigeonWatch)

## Research Question

Roadmap slice S-04 (`context/foundation/roadmap.md:136-146`): "user can attach an optional photo to a sighting they report" (FR-005). It is the first use of file storage, and reporting must keep working without a photo. What does the current code and the recorded context give us, what constrains the change, and which decisions are still open for `/10x-plan`?

## Summary

- **No photo code exists anywhere.** In the API there is no `IFormFile`, `[FromForm]`, blob or Azure package (grep over `PigeonWatch/Api` `*.cs`/`*.csproj`). In the SPA there is no file input, `<img>`, `FormData` or multipart call (grep over `PigeonWatch/Frontend/src` and `tests`).
- **Storage is already decided and provisioned.** Azure Blob Storage account `pigeonwatchstorage` with a private container `sighting-photos` (`context/deployment/deploy-plan.md:63-64`). The API's managed identity has "Storage Blob Data Contributor" (`deploy-plan.md:72`). App settings `BlobStorage__ServiceUri` and `BlobStorage__ContainerName` are set in App Service (`deploy-plan.md:75`). One open task belongs to S-04: use `BlobServiceClient` with `DefaultAzureCredential`/`ManagedIdentityCredential`, never a key-based connection string (`deploy-plan.md:77`).
- **Create is JSON today.** `POST /sightings` binds `CreateSightingRequestModel` from a JSON body (`PigeonWatch/Api/WebApi/Controllers/SightingsController.cs:37-60`, `WebApi/Models/CreateSightingRequestModel.cs:6-26`). Adding a photo means either switching create to multipart or adding a second upload call.
- **Three hard blockers for showing a photo:**
  1. **Access rule.** Unauthenticated visitors may not view sightings (`context/foundation/prd.md:160`), so a public blob URL is not allowed.
  2. **Login token.** `<img src>` does not carry the bearer token added by `authInterceptor` (`PigeonWatch/Frontend/src/app/core/auth/auth.interceptor.ts:56`).
  3. **CSP.** It allows images only from `'self' data: https://tile.openstreetmap.org` (`PigeonWatch/Frontend/public/staticwebapp.config.json:10`), so blob-host URLs and `blob:` object URLs are both blocked in production.
- **Architecture tests decide where the code goes.**
  - `IFormFile` must stop at WebApi, because BusinessLogic may not depend on `Microsoft.AspNetCore` (`ArchitectureTests/LayerDependencyTests.cs:13-70`).
  - A type ending in `Service` must live in `BusinessLogic.Services` (`ArchitectureTests/TypeLocationTests.cs:9-15`).
  - The Azure SDK client therefore fits in Data behind an interface, for example a `*Repository` or `*Provider`. The registration test then requires an `I<Name>` interface registered by `AddPigeonWatch` (`ArchitectureTests/ServiceRegistrationTests.cs:7,44-72`).
- **Undecided product and technical points** (no recorded decision in any inspected doc):
  - size and type limits;
  - removing EXIF/GPS data;
  - how photos are served;
  - thumbnails;
  - number of photos per sighting;
  - whether the photo is sent with the create request or uploaded separately;
  - whether the 60 s cooldown applies to the photo;
  - status codes for storage failures;
  - retention and cleanup when a sighting is removed (S-10).

  These are listed under Open Questions.

## Detailed Findings

### Backend: create and read paths

- `SightingsController.Create` (`PigeonWatch/Api/WebApi/Controllers/SightingsController.cs:37-60`):
  - reads the user id from `ClaimTypes.NameIdentifier` (:40);
  - returns 401 when the reporter is not found (:46);
  - returns a 400 ValidationProblem for business errors through `GetValidationProblem` (:54);
  - returns `CreatedAtAction(nameof(Get))` with a `SightingModel` on success (:59).
  - The class is `[ApiController]`, `[Authorize]` (:13-15) and derives from `FailureLoggingControllerBase`.
- Request flow:
  - `CreateSightingRequestModel` has DataAnnotations limits taken from `BusinessObjects/SightingRules.cs:5-16`.
  - `CreateSightingRequestMapper` (`WebApi/Mappers/CreateSightingRequestMapper.cs:8-19`) maps it to the `NewSighting` business record (`BusinessObjects/NewSighting.cs:3-11`).
  - `SightingService.CreateAsync` (`BusinessLogic/Services/SightingService.cs:19-56`) trims the fields, validates them (:131-145), enforces the 60 s cooldown with `TimeProvider` (:33-48), and calls `ISightingRepository.CreateAsync` (:50). A null result maps to `ReporterNotFound` (:52-53).
  - Error codes are defined in `BusinessObjects/SightingErrorCodes.cs:5-13`.
- Persistence:
  - `SightingRepository.CreateAsync` (`Data/Repositories/SightingRepository.cs:10-23`) and `SightingMapper` (`Data/Mappers/SightingMapper.cs:8-36`).
  - `SightingEntity` (`Data/Entities/SightingEntity.cs:6-28`) derives from `AuditableEntity`.
  - It is configured in `Data/PigeonWatchDbContext.cs:30-40`, with the UPPER_SNAKE convention set at :15.
  - The latest migration is `Data/Migrations/20261011135944_AddSighting.cs`.
- Read surfaces:
  - All four read endpoints return `SightingModel` or lists of it, built by `WebApi/ViewModelCreators/SightingViewModelCreator.cs:8-33`:
    - `GET sightings/{id}` (:62-73);
    - `GET sightings/nearby` (:75-92);
    - `POST sightings/list` (:94-111);
    - `GET sightings/mine` (:113-124).
  - Adding a field to `SightingModel` (`WebApi/Models/SightingModel.cs:3-24`) therefore changes all four responses.
  - Reads set `Cache-Control: private, no-store`.
- Limits and abuse guards on create:
  - No request size limit applies to sightings. The only `[RequestSizeLimit]` is 2048 bytes on `ClientErrorsController.cs:27`. No Kestrel or `FormOptions` configuration was found in `WebApi.Host/Program.cs`, so the ASP.NET Core defaults apply (about 30 MB per body). That is framework behaviour, not something the repo sets.
  - No rate-limit policy applies to sightings. `WebApi/RateLimiting/RateLimitPolicyNames.cs:5-7` defines only `auth` and `clientErrors`, configured at `Program.cs:41-85`. The only guard on create is the business cooldown.
  - There is no anti-forgery protection, because auth uses bearer tokens and no cookies.

### Backend: configuration, DI and architecture rules

- `WebApi.Host/appsettings.json:1-13` contains only Logging and AllowedHosts, with no `BlobStorage` section. `appsettings.Development.json` could not be read (permission denied), so its content is unknown.
- **No options pattern.** There is no `IOptions<>`, `Configure<>` or `ValidateOnStart` anywhere. The only configuration read is `GetConnectionString("Default")` at `DependencyInjection/DataServiceCollectionExtensions.cs:31`. Binding `BlobStorage:*` would be the first options class in the project.
- **DI structure.** `AddPigeonWatch(IConfiguration)` (`DependencyInjection/PigeonWatchServiceCollectionExtensions.cs:8-16`) calls `AddData(configuration)`, `AddBusinessLogic()`, `AddWebApi()` and `AddAccounts()`. Only `AddData` receives configuration.
- **Architecture tests build the container from a test configuration.** `ArchitectureTests/PigeonWatchServices.cs:12-25` uses an in-memory configuration holding only `ConnectionStrings:Default`. A new required setting that fails at registration would break these tests unless that configuration is extended.
- **Package management.** There is no central package management (no `Directory.Packages.props`). Versions are set inline per csproj, and no `Azure.*` package is referenced yet.
- **Rules a storage seam must satisfy:**
  - `LayerDependencyTests.cs:13-70`: BusinessObjects and BusinessLogic cannot reference `Microsoft.AspNetCore` or EF; WebApi cannot reference Data; Host cannot reference Data or BusinessLogic.
  - `LayerDependencyTests.cs:98-116`: only DependencyInjection may use `IServiceCollection`.
  - `ServiceRegistrationTests.cs:7`: classes with the suffixes Service, Provider, Repository, ViewModelCreator or Mapper need an interface plus a registration.
  - `TypeLocationTests.cs:9-15`: these suffixes are tied to namespaces: Repository in Data.Repositories, Service in BusinessLogic.Services, ViewModelCreator in WebApi.ViewModelCreators, Model in WebApi.Models.
  - `DatabaseModelTests.cs`: a new table must have a singular name, derive from `AuditableEntity` and use an `ID uniqueidentifier` key.
  - `CancellationTokenTests.cs`: async methods take `CancellationToken cancellationToken = default` (per Api CLAUDE.md; the test itself was not opened).
  - `ControllerActionTests.cs:11-47`: actions return `Task<ActionResult<T>>` with T in `WebApi.Models`.

### Backend: tests and failure conventions

- **Integration host.** `IntegrationTests/PigeonWatchApiFactory.cs:19-206` is a `WebApplicationFactory<Program>` on in-memory SQLite. It swaps the DbContext (:142-148) and the `TimeProvider` (:152-156), and collects logs with `FakeLogCollector` (:138). A blob-storage fake would be swapped in the same `ConfigureServices` hook. No Azurite or blob fake exists, and the CI workflows declare no `services:`.
- **Endpoint fixtures.** `IntegrationTests/EndpointCoverageTests.cs:9,25` requires a fixture in `EndpointFixtureRegistry.cs` for every routed endpoint. The create fixtures (`EndpointFixtureRegistry.cs:156-171`) are JSON-only (`JsonOf`, `AddMalformed`) and feed `PrivacySweep.cs`, `ResponseLeakScanner.cs` and `EndpointProtectionTests.cs`. A multipart create or a new upload or download endpoint needs new fixtures.
- **Unit tests.** NSubstitute and xunit v3. Examples: `UnitTests/SightingServiceTests.cs:16-42` (substituted `ISightingRepository`, `ManualTimeProvider`) and `UnitTests/SightingsControllerTests.cs:24` (`Create_returns_201_with_the_created_sighting`).
- **Failure logging.**
  - `FailureLoggingControllerBase.cs:9-61` logs Warning or Information with `{Route}`, `{ErrorCode}` and `{SightingId}`, but only for codes in `loggedErrorCodes` (`SightingsController.cs:26-35`). Cooldown is deliberately not logged (`ControllerFailureLoggingTests.cs:57-71`).
  - Unhandled exceptions become 500 responses through `UseExceptionHandler` (`Program.cs:123`). The only `IExceptionHandler` maps database-unavailable errors to 503 with `Retry-After: 10` (`Data/Diagnostics/DatabaseUnavailableExceptionHandler.cs:11-77`). There is no handling for storage outages.

### Frontend: report form, API client, display

- **Report page** (`PigeonWatch/Frontend/src/app/features/sightings/report-sighting-page/report-sighting-page.ts`):
  - It uses Angular signal forms (`form(this.model, …)` at :109-120) over a plain-object signal (:51-67, :99).
  - Location is a custom control bound with `[formField]` (`report-sighting-page.html:101-103`); that is the pattern to copy for a photo control.
  - Submit (:151-169) builds a `NewSighting` (:190-206) and calls `sightingsApi.create`, then navigates to `/sightings/:id`. Failures go to `handleFailure` (:171-179), which reports only unexpected generic failures and shows `<app-alert>` plus `<app-error-reference>` (`html:104-110`).
- **API client.** `SightingsApi.create` posts JSON to `${environment.apiUrl}/sightings` (`src/app/features/sightings/sightings-api.ts:20-22`). `Sighting` (`sighting.types.ts:23-34`) has no photo field.
  - No interceptor sets `Content-Type`; `authInterceptor` only sets `Authorization` (`auth.interceptor.ts:56`). A `FormData` body will therefore get its multipart boundary from the browser.
- **Retries and status mapping:**
  - `warmupInterceptor` (`src/app/core/warmup/warmup.interceptor.ts:46-71`) resends the same request after a status 0 or 503 for up to 120 s. An upload could be resent, and create is not idempotent. Registration has the same known issue (`PigeonWatch/Frontend/CLAUDE.md`, "Registration during a cold start").
  - `sighting-errors.ts:18-30` maps errors to `cooldown`, `notFound` or `generic`, so a 413 or 415 would show as generic and be reported.
  - `problem-details.ts:31-35` treats only 400, 401, 404 and 429 as expected client statuses.
- **Display points:**
  - Detail page: a `<dl app-detail-list>` inside `.sighting-detail__details` (`sighting-detail-page.html:43-70`).
  - Sighting row: link with name and meta (`shared/sighting-row/sighting-row.html:1-15`), reused by the list page and My sightings (`my-sightings.html:20-24`).
- **UI rules:**
  - All UI text must be keys in `PigeonWatch/Api/BusinessObjects/Resources/UiLabels.resx`, generated into `src/app/core/resources/generated/ui-labels.en.json` and checked by `label-coverage.spec.ts`.
  - Styles must use the tokens in `src/styles/_tokens.scss`, checked by `style-token-coverage.spec.ts`.
  - A new detail-page state needs a kitchen-sink cell, a marker assertion and a stub mode (`src/app/dev/kitchen-sink/cell-definitions.ts:383-455`, `cell-stubs.ts:266`, per Frontend CLAUDE.md).
  - The shared UI barrel (`src/app/shared/ui/index.ts`) has no file-input or image component.
- **Client configuration.** `GET /configuration/client` returns `sightingRules` (`src/app/core/configuration/configuration.service.ts:20-32,54-60`) with a fallback in `sighting.types.ts:148-154`. This is the established route for serving photo limits from `SightingRules`.
- **Tests.**
  - `report-sighting-page.spec.ts` stubs `SightingsApi.create` (:47-61) and asserts the `NewSighting` payload (:269-292).
  - `sightings-api.spec.ts:44-63` asserts a JSON body and will need changing for FormData.
  - Playwright specs fill the report form in `tests/e2e/seed.spec.ts:8-33`, `nearby-filters.spec.ts:24-37` and `sighting-list.spec.ts:41-54`. None uploads a file.

### Deployment and infrastructure

- **Hosting.** The API runs on App Service F1: 60 CPU-minutes per day, no Always-On, cold starts (`context/foundation/infrastructure.md:57-61`). Every byte uploaded through the API, and any server-side resizing, uses that budget. This is an inference; no doc quantifies it.
- **Regions.** API, SQL and Storage are in swedencentral; Static Web Apps is in eastus2 (`deploy-plan.md:38`).
- **Configuration and infrastructure.** No infrastructure as code exists; resources were created with az CLI, recorded in deploy-plan.md. Role grants and repo secrets must be done by the user (`deploy-plan.md:72, :84-85`).
- **Migrations.** Must be additive and work with the previously deployed build (`deploy-plan.md:109`). CI runs test, migrate (EF bundle), deploy, then a probe of `/configuration/client` (`.github/workflows/deploy-api.yml`); there is no storage step.
- **CORS.** CORS for the API is set on the platform (`deploy-plan.md:126, :134`). Blob-service CORS is not configured anywhere; it would only matter if the browser talks to Blob Storage directly.
- **Stale guidance.** `infrastructure.md:79, :103` still mention storage keys as app settings. That is superseded by managed identity (`deploy-plan.md:69`).
- **Not verified live.** Whether public blob access is disabled on the account, and whether lifecycle rules exist.

### Privacy, testing and observability constraints

- **Privacy rule.** No reporter id or email on the wire (`PigeonWatch/CLAUDE.md:9`). Photo EXIF data can carry GPS, device and owner name, and `ResponseLeakScanner` checks only for email strings, so it would not catch EXIF (`context/foundation/test-plan.md:183, :220`).
- **Test plan.** Risk #2 (contact leak through new surfaces) names S-04 (`test-plan.md:45, :62, :213`). Every new endpoint follows §6.4: a fixture, protected or explicitly anonymous, the privacy sweep, and an ownership test where it acts on a user's data (`test-plan.md:171-186`). Required gates are build with style rules, unit and architecture tests, migrations matching the model, and API integration tests (`test-plan.md:123-133`).
- **Observability.** Server failures are logged before they become a response, with user id and trace id only (`context/audits/observability/2026-10-25_auth-sightings.md:70, :123-124`). In the SPA, unexpected failures go through `ErrorReportingService.report` and show `<app-error-reference>` (`PigeonWatch/Frontend/CLAUDE.md:54`). The CLAUDE.md hard rule forbids empty catches and dropped causes.
- **CSP changes.** Must be called out as a security trade-off and verified live, because `ng serve` does not apply SWA headers (`PigeonWatch/Frontend/CLAUDE.md:43`; S-03 `plan.md:72, :244, :305`). `src/app/shared/map/map-config.spec.ts:22` asserts the current `img-src` line.

## Code References

- `PigeonWatch/Api/WebApi/Controllers/SightingsController.cs:37-60` — create action (JSON body)
- `PigeonWatch/Api/WebApi/Models/CreateSightingRequestModel.cs:6-26` — request model
- `PigeonWatch/Api/BusinessLogic/Services/SightingService.cs:19-56` — create logic, cooldown at :33-48
- `PigeonWatch/Api/BusinessObjects/SightingRules.cs:5-16` — limits shared with the SPA
- `PigeonWatch/Api/Data/Entities/SightingEntity.cs:6-28` — entity; latest migration `20261011135944_AddSighting`
- `PigeonWatch/Api/WebApi/Models/SightingModel.cs:3-24` — response model used by all four read endpoints
- `PigeonWatch/Api/DependencyInjection/DataServiceCollectionExtensions.cs:18-43` — where infrastructure is registered
- `PigeonWatch/Api/ArchitectureTests/TypeLocationTests.cs:9-15`, `ServiceRegistrationTests.cs:7`, `PigeonWatchServices.cs:12-25` — placement and registration rules
- `PigeonWatch/Api/IntegrationTests/PigeonWatchApiFactory.cs:138-169` — service swaps for tests
- `PigeonWatch/Api/IntegrationTests/EndpointFixtureRegistry.cs:156-171` — create fixtures (JSON only)
- `PigeonWatch/Frontend/src/app/features/sightings/report-sighting-page/report-sighting-page.ts:109-206` — form and submit
- `PigeonWatch/Frontend/src/app/features/sightings/sightings-api.ts:20-22` — `create` call
- `PigeonWatch/Frontend/src/app/core/warmup/warmup.interceptor.ts:46-71` — resend after 0 or 503
- `PigeonWatch/Frontend/public/staticwebapp.config.json:10` — CSP `img-src`
- `context/deployment/deploy-plan.md:63-77` — storage account, container, MI role, app settings, open S-04 build task

## Architecture Insights

- Every layer seam is enforced by NetArchTest, so the storage seam's name and namespace are not free choices:
  - a Data-layer `*Repository` or `*Provider` with an interface in `Interfaces/`;
  - registered in `AddData` with the `IConfiguration` it already receives;
  - with the WebApi turning `IFormFile` into a stream plus metadata before calling BusinessLogic.
- `SightingRules` plus `GET /configuration/client` is the single source for limits shared by the API and the SPA. Photo size and type limits fit there.
- Test doubles are swapped in `PigeonWatchApiFactory.ConfigureServices`. An in-memory blob store fits that pattern without adding Azurite to CI.
- Failures are typed error codes in `ValidationProblemDetails`. A bad photo would add codes to `SightingErrorCodes`, and the SPA can map them in `sighting-errors.ts`.

## Historical Context (from prior changes)

- `context/archive/2026-10-11-report-sighting-on-map/plan.md:37` and `plan-brief.md:42` — S-03 deferred the photo to S-04 (supported).
- `context/archive/2026-10-03-persistence-wiring-smoke/plan.md:50` — Blob wiring deferred (supported; still not done).
- `context/foundation/prd.md:78-79` — photo is optional but encouraged; a required photo was rejected (supported).
- `context/foundation/infrastructure.md:79, :103` — storage keys as app settings (contradicted by `deploy-plan.md:69`, which moved to managed identity).
- `context/archive/2026-10-11-report-sighting-on-map/plan.md:15` — initial bundle about 463 kB against the 500 kB warning budget, at S-03 time. That figure is from S-03 and was not re-measured; photo UI should stay in lazy chunks.

## Related Research

- `context/archive/2026-10-11-report-sighting-on-map/research.md`
- `context/archive/2026-10-18-testing-api-privacy-authorization-contract/research.md`
- `context/archive/2026-10-25-server-side-observability-logging/` (plan-brief)

## Open Questions

Decisions for `/10x-plan` (none recorded in any inspected doc):

1. **How photos are served.** Options:
   - (a) an authenticated API endpoint that streams the blob, with the SPA fetching it through `HttpClient` and showing a `blob:` URL (CSP needs `img-src blob:`);
   - (b) short-lived read SAS URLs generated by the API with a user-delegation key under managed identity (CSP needs the blob host; the URL works for anyone holding it until it expires);
   - (c) public container, which `prd.md:160` rules out.
2. **When the photo is uploaded.** Options:
   - (a) one multipart create request with the photo;
   - (b) create first, then `PUT /sightings/{id}/photo` (owner-only; needs an ownership test).

   Option (a) is resent by the warm-up interceptor; option (b) separates failures, so a sighting can save even when the photo fails.
3. **Limits.** Maximum size (stay well under the about 30 MB framework default and mind the F1 CPU budget), allowed types (JPEG/PNG/WebP? HEIC from iPhones?), and one photo per sighting or several.
4. **EXIF/GPS.** Strip on the server (needs an image library, which costs CPU on F1), re-encode in the browser with a canvas before upload (also shrinks phone photos), or accept the leak risk.
5. **Thumbnails.** Whether list rows show one.
6. **Cooldown.** Whether the 60 s cooldown applies to a separate photo upload.
7. **Storage failures.** What the API returns when Blob Storage fails or is slow (503 like the database handler, or a 500 with logging), and what the SPA shows.
8. **Retention.** Orphan blobs when create fails after the upload, or when S-10 removes a sighting.
9. **Live checks** (user-run, `az` with `curl.exe` per lessons.md): `allowBlobPublicAccess` on `pigeonwatchstorage`, and that the managed identity can write to `sighting-photos`.
10. **Local development.** Azurite or a filesystem/in-memory store for `dotnet run` and E2E, since there are no blob credentials locally.
