# Attach Sighting Photo Implementation Plan

## Overview

Roadmap slice S-04 (FR-005). When reporting a sighting, a logged-in rescuer can attach one optional photo. The browser shrinks it and re-encodes it as JPEG. The server re-encodes it again with SkiaSharp, which removes EXIF data (GPS, device, owner) and applies the orientation. The photo is stored in the private `sighting-photos` Blob container and streamed through the API only to logged-in members. The detail page shows the photo. List and My-sightings rows show a camera icon when a sighting has one. Reporting without a photo works exactly as today.

## Current State Analysis

See `context/changes/attach-sighting-photo/research.md` for the full map. In short:

- **No photo code exists.** The API has no `IFormFile`, blob code or `Azure.*` package. The SPA has no file input, `<img>` or `FormData`.
- **Create is JSON.** `POST /sightings` binds `CreateSightingRequestModel` from a JSON body (`PigeonWatch/Api/WebApi/Controllers/SightingsController.cs:37-60`). `SightingService.CreateAsync` validates the fields, enforces the 60 s cooldown and inserts the row (`PigeonWatch/Api/BusinessLogic/Services/SightingService.cs:19-56`).
- **Storage is already provisioned.** Account `pigeonwatchstorage`, private container `sighting-photos`, and the API's managed identity holds "Storage Blob Data Contributor". The app settings `BlobStorage__ServiceUri` and `BlobStorage__ContainerName` are already set. The open build task is managed-identity access through `BlobServiceClient` with no key (`context/deployment/deploy-plan.md:63-77`).
- **No configuration-options class exists yet.** The only configuration read is `GetConnectionString("Default")` (`PigeonWatch/Api/DependencyInjection/DataServiceCollectionExtensions.cs:31`).
- **Photos are blocked from display today.** The SPA's security policy (CSP) allows images only from `'self' data: https://tile.openstreetmap.org` (`PigeonWatch/Frontend/public/staticwebapp.config.json:10`), and `<img src>` carries no bearer token.

## Desired End State

- `POST /sightings` accepts `multipart/form-data` with the existing fields plus an optional `photo` part.
  - Without a photo it behaves exactly as today.
  - With a valid photo (JPEG, PNG or WebP, at most 2 MB, real type checked from the file's bytes) it stores a normalized JPEG (no metadata, longest edge at most 1600 px) and creates the sighting.
  - An invalid photo returns 400 with `photoTooLarge`, `photoUnsupportedType` or `photoUnreadable`.
  - A storage outage returns 503 with `photoStorageUnavailable` and `Retry-After`, and nothing is saved.
  - If the database insert fails after the photo was stored, the stored photo is deleted.
- `GET /sightings/{id}/photo` is `[Authorize]`. It streams `image/jpeg` with `Cache-Control: private, no-store`, and returns 404 when the sighting or its photo does not exist.
- Every `SightingModel` response carries `hasPhoto`. `/configuration/client` serves the photo limits inside `sightingRules`.
- The report form has a photo picker. It re-encodes the picked file in the browser, shows a preview, and lets the user remove the photo. A storage failure keeps the form filled and tells the user to retry or remove the photo.
- The detail page loads the photo through `HttpClient` and shows it from a `blob:` URL, with loading and error states. Rows show a camera icon with a translated label.
- In production, photos go to Blob Storage using the managed identity. In Development they go to a local folder. Integration tests use an in-memory fake. Production refuses to start if the storage settings are missing.

Verify with: the architecture, unit and integration test suites, `npm test -- --watch=false`, `npm run build`, one Playwright spec, and the Phase 6 live checks.

### Key Discoveries:

- `ServiceRegistrationTests` requires every `*Service`, `*Provider`, `*Repository`, `*ViewModelCreator` and `*Mapper` class to be registered against its exact implementation type (`PigeonWatch/Api/ArchitectureTests/ServiceRegistrationTests.cs:7,44-72`). The two store implementations are chosen by configuration, so they are named `*PhotoStore` and sit outside that rule.
- `ControllerActionTests` requires `Task<ActionResult<T>>` with `T` in `WebApi.Models` (`PigeonWatch/Api/ArchitectureTests/ControllerActionTests.cs:10-28`), which a binary endpoint cannot satisfy. The plan adds one explicit exception for actions marked `[Produces("image/jpeg")]`.
- `PigeonWatchServices` builds the architecture-test container from an in-memory configuration holding only `ConnectionStrings:Default` (`PigeonWatch/Api/ArchitectureTests/PigeonWatchServices.cs:12-25`). It needs the `BlobStorage:*` keys added.
- BusinessLogic references Data (`PigeonWatch/Api/BusinessLogic/PigeonWatch.BusinessLogic.csproj:12`), so `SightingService` can depend on a store interface that lives in Data.
- `warmupInterceptor` resends after a 0 or 503 status for up to 120 s (`PigeonWatch/Frontend/src/app/core/warmup/warmup.interceptor.ts:46-71`). A storage-outage 503 is not a cold start, so it must be excluded by its error code.
- The API runs on Linux App Service F1 (`context/deployment/deploy-plan.md:54`). SkiaSharp therefore needs `SkiaSharp.NativeAssets.Linux.NoDependencies`.
- The app uses no icon font, and the CSP allows fonts only from `'self'`. The camera icon is an inline SVG.
- The custom form control to copy is `PigeonWatch/Frontend/src/app/shared/map/location-picker/location-picker.ts`, bound with `[formField]` in `report-sighting-page.html:101-103`.

## What We're NOT Doing

- More than one photo per sighting, or a gallery.
- Thumbnails, or photos inside list rows. Rows get only the camera icon.
- Adding, replacing or deleting a photo after the sighting is created. Editing belongs to S-09, and admin removal (including deleting the blob) belongs to S-10.
- Moderation or content scanning of photos.
- SAS links or a public container. The browser never talks to Blob Storage directly.
- Azurite, or any CI service container.
- Saving the sighting without its photo when storage fails. The whole create fails instead.
- Profile photos, which stay out of scope per `prd.md:66`.
- Cleaning up blobs left behind when the compensating delete fails. That case is logged, not retried.

## Implementation Approach

Build from the storage seam upward, so every layer can be tested against the one below:

1. The storage interface and its implementations, plus start-up validation and the outage handler.
2. Normalization, written test-first, because its rules (type check, size and pixel limits, orientation, metadata removal) are pure and easy to test.
3. The API wiring: create in the order validate → cooldown → normalize → store → insert, with a compensating delete.
4. The SPA reporting form.
5. The SPA display.
6. Live verification on Azure. That is the only place the real Blob client, the managed identity, SkiaSharp's native library and the CSP all run together.

The JSON contract of `POST /sightings` is replaced by multipart in one step. The SPA is the only client, and both ship from the same merge to `main`.

## Critical Implementation Details

- **Create ordering.** The service first runs the existing field validation and the cooldown check, so a rejected request never touches storage. Then it normalizes the photo, stores the blob under a new GUID name, and inserts the row with that name.
  - If the insert throws, or returns `ReporterNotFound`, the service deletes the blob and then rethrows or returns the original outcome.
  - If the delete fails, it is logged at Error with the blob name, trace context and the delete exception as cause, and it never replaces the original failure.
- **Storage outage contract.** Store implementations wrap `Azure.RequestFailedException`, `Azure.Identity.AuthenticationFailedException` and timeouts in `PhotoStorageUnavailableException`, keeping the inner exception. A new `IExceptionHandler` maps it to 503 ProblemDetails with `errors.photoStorageUnavailable`, `Retry-After: 10`, and an Error log entry carrying the cause.
  - `warmupInterceptor` must not resend a 503 whose ProblemDetails carries that code. Otherwise the user would see the "server waking up" panel for 120 s during a storage outage.
- **Decompression bombs.** Read the image dimensions from the codec header before decoding. If width × height exceeds 40 megapixels, reject with `photoUnreadable` without decoding.
- **Request size.** `IFormFile.Length > SightingRules.PhotoMaxBytes` returns 400 `photoTooLarge`. A request body above `PhotoMaxBytes + 512 KB` is cut off by `[RequestSizeLimit]` / `[RequestFormLimits]` with a 413, which the SPA also maps to the "photo too large" message.
- **Browser re-encode.** Use `createImageBitmap(file, { imageOrientation: 'from-image' })`, draw to a canvas scaled so the longest edge is at most `photoMaxDimension`, then `toBlob('image/jpeg', 0.85)`. If decoding fails (for example HEIC in Chrome), show "unsupported type" without calling the API. Canvas output carries no EXIF data.

## Phase 1: Photo storage seam

### Overview

Add the storage interface, the managed-identity Blob implementation and the Development-only local-folder implementation. Also add the first configuration-options class with start-up validation, and the storage-outage exception with its 503 handler. Nothing calls the seam yet.

### Changes Required:

#### 1. Packages

**File**: `PigeonWatch/Api/Data/PigeonWatch.Data.csproj`

**Intent**: Reference the Azure Blob SDK and Azure Identity in the Data layer, which already holds the infrastructure code.

**Contract**: Add `Azure.Storage.Blobs` and `Azure.Identity` with inline, pinned versions. No central package management.

#### 2. Store interface and outage exception

**File**: `PigeonWatch/Api/Data/Storage/Interfaces/ISightingPhotoStore.cs`, `PigeonWatch/Api/BusinessObjects/PhotoStorageUnavailableException.cs`

**Intent**: One low-level seam that saves, opens and deletes photo bytes by blob name. The outage exception lives in BusinessObjects so BusinessLogic can recognize it without referencing Azure.

**Contract**:
- `Task SaveAsync(string blobName, Stream jpeg, CancellationToken cancellationToken = default)`
- `Task<Stream?> OpenReadAsync(string blobName, CancellationToken cancellationToken = default)`, which returns null when the blob is missing
- `Task DeleteAsync(string blobName, CancellationToken cancellationToken = default)`, which is idempotent
- `PhotoStorageUnavailableException(string message, Exception inner)`

#### 3. Implementations

**File**: `PigeonWatch/Api/Data/Storage/AzureBlobSightingPhotoStore.cs`, `PigeonWatch/Api/Data/Storage/LocalFolderSightingPhotoStore.cs`

**Intent**:
- The Azure store builds a `BlobContainerClient` from `ServiceUri` + `ContainerName` with `DefaultAzureCredential`, never a key. It uploads with content type `image/jpeg` and wraps transport and auth failures in `PhotoStorageUnavailableException`.
- The local store writes under `LocalDirectory`. It validates that the blob name is a GUID-based `*.jpg` name, so no path traversal is possible.

**Contract**: Both implement `ISightingPhotoStore` and are `internal sealed`. Their names end in `PhotoStore`, which is not a role suffix.

#### 4. Options and registration

**File**: `PigeonWatch/Api/Data/Storage/BlobStorageOptions.cs`, `PigeonWatch/Api/DependencyInjection/DataServiceCollectionExtensions.cs`, `PigeonWatch/Api/WebApi.Host/appsettings.Development.json`, `.gitignore`

**Intent**: Bind `BlobStorage` with `ValidateOnStart`, using an `IValidateOptions` that reads `IHostEnvironment`.

**Contract**:
- `BlobStorageOptions { Uri? ServiceUri; string? ContainerName; string? LocalDirectory }`.
- Validation:
  - In Development, either `LocalDirectory` or `ServiceUri` + `ContainerName` must be set.
  - In any other environment, `ServiceUri` + `ContainerName` are required and `LocalDirectory` must be empty.
- `ISightingPhotoStore` is registered as a singleton factory: `LocalFolderSightingPhotoStore` when `LocalDirectory` is set, otherwise `AzureBlobSightingPhotoStore`.
- Development config points `LocalDirectory` at a gitignored folder.
- If `appsettings.Development.json` is not writable for the agent (it was permission-denied during research), the user adds the key.

#### 5. Outage handler

**File**: `PigeonWatch/Api/Data/Diagnostics/PhotoStorageUnavailableExceptionHandler.cs`, `DataServiceCollectionExtensions.cs`, `PigeonWatch/Api/BusinessObjects/SightingErrorCodes.cs`

**Intent**: Map a storage outage to 503, following the pattern of `DatabaseUnavailableExceptionHandler`.

**Contract**:
- `SightingErrorCodes.PhotoStorageUnavailable = "photoStorageUnavailable"`.
- The response is 503 `ValidationProblemDetails` with that error key and `Retry-After: 10`.
- The handler logs at Error with the exception as cause, the user id and the trace id, never the email.

#### 6. Architecture-test configuration

**File**: `PigeonWatch/Api/ArchitectureTests/PigeonWatchServices.cs`

**Intent**: Keep `AddPigeonWatch` registration testable now that `BlobStorage` is bound.

**Contract**: The in-memory configuration adds `BlobStorage:ServiceUri` and `BlobStorage:ContainerName` test values.

### Success Criteria:

#### Automated Verification:

- Solution builds with style enforcement: `dotnet build PigeonWatch/Api/PigeonWatchApi.slnx`
- Architecture tests pass: `dotnet test PigeonWatch/Api/ArchitectureTests/PigeonWatch.ArchitectureTests.csproj`
- Unit tests cover the local-folder store (save/open/delete round trip, open of a missing blob returns null, a non-GUID name is rejected), the options validator (Development vs Production matrix) and the 503 handler (status, Retry-After, error key, Error log carrying the cause): `dotnet test PigeonWatch/Api/UnitTests/PigeonWatch.UnitTests.csproj`
- No vulnerable packages: `dotnet list PigeonWatch/Api/WebApi.Host/PigeonWatch.WebApi.Host.csproj package --vulnerable --include-transitive`

#### Manual Verification:

- `dotnet run` in Development starts with `LocalDirectory` set, and fails at start-up with a clear options-validation message when `BlobStorage` is removed and `ASPNETCORE_ENVIRONMENT=Production`

**Implementation Note**: After automated verification passes, pause for manual confirmation before Phase 2.

---

## Phase 2: Server-side photo normalization (test-first)

### Overview

A provider turns an uploaded stream into a normalized JPEG or a typed error. It is written test-first, against small image fixtures generated inside the tests.

### Changes Required:

#### 1. Rules and error codes

**File**: `PigeonWatch/Api/BusinessObjects/SightingRules.cs`, `SightingErrorCodes.cs`, `PhotoNormalizationResult.cs`

**Intent**: Give the photo limits a single source of truth, shared with the SPA later through configuration.

**Contract**:
- `PhotoMaxBytes = 2 * 1024 * 1024`
- `PhotoMaxDimension = 1600`
- `PhotoMaxPixels = 40_000_000`
- `PhotoJpegQuality = 85`
- `PhotoAllowedContentTypes = ["image/jpeg", "image/png", "image/webp"]`
- New error codes: `photoTooLarge`, `photoUnsupportedType`, `photoUnreadable`
- `PhotoNormalizationResult` holds either the normalized JPEG bytes or an error code

#### 2. Normalization provider

**File**: `PigeonWatch/Api/BusinessLogic/Services/PhotoNormalizationProvider.cs`, `Services/Interfaces/IPhotoNormalizationProvider.cs`, `PigeonWatch/Api/BusinessLogic/PigeonWatch.BusinessLogic.csproj`, `BusinessLogicServiceCollectionExtensions.cs`

**Intent**: Use SkiaSharp to detect the format from the file's bytes (never from the client's content type), and reject anything other than JPEG, PNG or WebP. Then:
1. Reject images above `PhotoMaxPixels` before decoding.
2. Decode the image.
3. Apply the EXIF orientation from the codec's encoded origin.
4. Scale down so the longest edge is at most `PhotoMaxDimension` (never scale up).
5. Encode a JPEG at `PhotoJpegQuality`.

The output carries no metadata.

**Contract**:
- `Task<PhotoNormalizationResult> NormalizeAsync(Stream input, long length, CancellationToken cancellationToken = default)`.
- Packages: `SkiaSharp` and `SkiaSharp.NativeAssets.Linux.NoDependencies`, pinned to the same version.
- Registered as scoped.

### Success Criteria:

#### Automated Verification:

- Unit tests, written first and failing before the provider exists, cover these cases: `dotnet test PigeonWatch/Api/UnitTests/PigeonWatch.UnitTests.csproj`
  - a JPEG with EXIF GPS comes out with no EXIF segment;
  - a 4000×3000 JPEG comes out at 1600×1200;
  - a 800×600 PNG comes out as an 800×600 JPEG;
  - an orientation-6 JPEG comes out rotated (width and height swapped);
  - a WebP is accepted;
  - a GIF, text renamed to `.jpg`, a truncated JPEG and an oversized length are each rejected with the right code;
  - a header claiming more than 40 MP is rejected without decoding.
- Architecture tests pass (provider registered, interface placed correctly): `dotnet test PigeonWatch/Api/ArchitectureTests/PigeonWatch.ArchitectureTests.csproj`

**Implementation Note**: Pause for confirmation before Phase 3.

---

## Phase 3: API create and read

### Overview

Wire the photo into the sighting: persist the blob name, accept multipart on create, add the photo download endpoint, expose `hasPhoto` and the photo limits, and cover all of it with integration tests using an in-memory store.

### Changes Required:

#### 1. Data model and migration

**File**: `PigeonWatch/Api/Data/Entities/SightingEntity.cs`, `Data/Mappers/SightingMapper.cs`, `Data/Repositories/SightingRepository.cs` (+ interface), `Data/Migrations/<timestamp>_AddSightingPhoto.cs`

**Intent**: Store the blob name, nullable, so the migration is additive and safe against the previous build. Add the reads that the photo endpoint and `hasPhoto` need.

**Contract**:
- `string? PhotoBlobName`, max length 64, column `PHOTO_BLOB_NAME`.
- `NewSighting` and the repository create path carry `PhotoBlobName`.
- The `Sighting` business object gains `bool HasPhoto`, never the blob name.
- `ISightingRepository.GetPhotoBlobNameAsync(Guid sightingId, CancellationToken cancellationToken = default)` returns `string?`.
- The migration is created with `dotnet ef migrations add AddSightingPhoto ...`.

#### 2. Service orchestration

**File**: `PigeonWatch/Api/BusinessLogic/Services/SightingService.cs` (+ interface), `BusinessObjects/NewSightingPhoto.cs`

**Intent**: Add the optional photo to `CreateAsync` in the order described under Critical Implementation Details, and add `OpenPhotoAsync`.

**Contract**:
- `NewSighting` gains `NewSightingPhoto? Photo` (`Stream Content`, `long Length`).
- `Task<Stream?> OpenPhotoAsync(Guid sightingId, CancellationToken cancellationToken = default)` returns null when the sighting has no photo or the blob is missing; a missing blob is logged at Warning.
- Normalization errors join the existing validation-failure result.
- Error codes `photoTooLarge`, `photoUnsupportedType` and `photoUnreadable` are added to the controller's `loggedErrorCodes` at Warning, next to the other invalid-input codes.

#### 3. Controller and request model

**File**: `PigeonWatch/Api/WebApi/Controllers/SightingsController.cs`, `WebApi/Models/CreateSightingRequestModel.cs`, `WebApi/Mappers/CreateSightingRequestMapper.cs`, `WebApi/Models/SightingModel.cs`, `WebApi/ViewModelCreators/SightingViewModelCreator.cs`

**Intent**: Switch create to form binding with an optional file. Add the streaming photo action. Surface `HasPhoto` on all four read responses.

**Contract**:
- `Create([FromForm] CreateSightingRequestModel request, ...)` with `[Consumes("multipart/form-data")]`, `[RequestSizeLimit(PhotoMaxBytes + 512 * 1024)]` and a matching `[RequestFormLimits(MultipartBodyLengthLimit = ...)]`.
- The request model gains `IFormFile? Photo`.
- New action `[HttpGet("{id:guid}/photo")] [Produces("image/jpeg")]` returns `Task<ActionResult>`: a `File(stream, "image/jpeg")` with `Cache-Control: private, no-store`, or 404 using the existing not-found logging.
- `SightingModel.HasPhoto` (bool).

#### 4. Architecture-rule exception for binary actions

**File**: `PigeonWatch/Api/ArchitectureTests/ControllerActionTests.cs`

**Intent**: The requirement changed: one endpoint now returns bytes, not a JSON model. Allow exactly that case and keep the rule strict for everything else.

**Contract**:
- An action may return `Task<ActionResult>` only when it carries `[Produces]` with a single `image/*` content type.
- A new test asserts that such actions exist only with that attribute.

#### 5. Client configuration

**File**: `PigeonWatch/Api/BusinessLogic/Services/ClientConfigurationService.cs` and its model in `WebApi/Models`

**Intent**: Serve the photo limits to the SPA through the existing `sightingRules`.

**Contract**: `sightingRules` gains `photoMaxBytes`, `photoMaxDimension` and `photoAllowedContentTypes`.

#### 6. Test harness and fixtures

**File**: `PigeonWatch/Api/IntegrationTests/PigeonWatchApiFactory.cs`, `IntegrationTests/InMemorySightingPhotoStore.cs`, `EndpointFixtureRegistry.cs`, `ControllerFailureLoggingTests.cs`, `TestUser.cs`, `OwnershipContractTests.cs`, and any other `PostAsJsonAsync("/sightings"...)` caller

**Intent**: Swap in an in-memory store that can be switched to fail. Supply `BlobStorage` settings so start-up validation passes. Move every create caller to multipart. Register fixtures for the photo endpoint so the coverage, protection and privacy sweeps include it.

**Contract**:
- A shared multipart helper replaces `JsonOf` for create.
- `AddMalformed` gets a multipart variant.
- A fixture for `GET sightings/{id}/photo` is seeded with a photo.

### Success Criteria:

#### Automated Verification:

- Build passes: `dotnet build PigeonWatch/Api/PigeonWatchApi.slnx`
- Migration matches the model: `dotnet ef migrations has-pending-model-changes --project PigeonWatch/Api/Data/PigeonWatch.Data.csproj --startup-project PigeonWatch/Api/WebApi.Host/PigeonWatch.WebApi.Host.csproj`
- Architecture tests pass, including the new binary-action rule: `dotnet test PigeonWatch/Api/ArchitectureTests/PigeonWatch.ArchitectureTests.csproj`
- Unit tests cover these service cases: `dotnet test PigeonWatch/Api/UnitTests/PigeonWatch.UnitTests.csproj`
  - no photo → no store calls;
  - cooldown or validation failure → no store calls;
  - normalization error → no store calls;
  - store throws → no insert;
  - insert throws → blob deleted and original exception rethrown;
  - `ReporterNotFound` → blob deleted;
  - delete failure is logged with cause and the original outcome kept.
- Integration tests pass: `dotnet test PigeonWatch/Api/IntegrationTests/PigeonWatch.IntegrationTests.csproj`. They cover:
  - multipart create without a photo → 201 with `hasPhoto:false`;
  - with a photo → 201 with `hasPhoto:true`, and `GET .../photo` returns a JPEG with no EXIF segment and `Cache-Control: private, no-store`;
  - anonymous `GET .../photo` → 401;
  - unknown id or no photo → 404;
  - oversized photo → 400 `photoTooLarge`;
  - GIF → 400 `photoUnsupportedType`;
  - body over the request limit → 413;
  - failing store → 503 with `photoStorageUnavailable` and `Retry-After`, no row saved, Error log present;
  - list, nearby and mine responses include `hasPhoto`;
  - `/configuration/client` includes the photo limits;
  - endpoint coverage, protection and privacy sweeps green.

#### Manual Verification:

- With `dotnet run` (Development), a multipart create from Swagger or `curl.exe -F` stores a file in the local folder, and `GET /sightings/{id}/photo` with a bearer token returns it

**Implementation Note**: Pause for confirmation before Phase 4.

---

## Phase 4: SPA reporting

### Overview

Add the photo picker to the report form, re-encode the photo in the browser, send the create request as FormData, map the new failures, and allow `blob:` images in the CSP.

### Changes Required:

#### 1. Types, configuration and API client

**File**: `PigeonWatch/Frontend/src/app/features/sightings/sighting.types.ts`, `sightings-api.ts`, `sightings-api.spec.ts`

**Intent**: Carry the optional photo and the new rules. Send the create request as FormData without setting `Content-Type`.

**Contract**:
- `NewSighting.photo: Blob | null`.
- `Sighting.hasPhoto: boolean`.
- `SightingRules` and `fallbackSightingRules` gain `photoMaxBytes`, `photoMaxDimension` and `photoAllowedContentTypes`.
- `create()` builds FormData; the `photo` part is named `photo.jpg`.
- New `photo(id): Observable<Blob>` uses `responseType: 'blob'`.

#### 2. Photo re-encoding helper and picker control

**File**: `PigeonWatch/Frontend/src/app/features/sightings/report-sighting-page/photo-picker/` (component, helper, specs)

**Intent**: A custom Signal Forms control, modelled on `LocationPicker`, holding a `Blob | null`. It offers a visible file button (`accept="image/jpeg,image/png,image/webp"`), re-encodes as described under Critical Implementation Details, previews through an object URL (revoked on change and on destroy), and has a Remove button. A client-side failure sets a field error without any API call.

**Contract**: The re-encode helper sits behind an injectable seam so specs can stub the canvas work.

#### 3. Report page and error mapping

**File**: `report-sighting-page.ts`/`.html`/`.spec.ts`, `report-sighting-page-reporting.spec.ts`, `sighting-errors.ts`

**Intent**:
- Add the photo field to the form model.
- Map the server photo codes, and a 413, to field errors on the photo control.
- Map `photoStorageUnavailable` to a form-level alert that says "retry or remove the photo", keeps the form filled, and reports through `ErrorReportingService` with `<app-error-reference>`.

**Contract**: `SightingFailure` gains `photoTooLarge`, `photoUnsupportedType`, `photoUnreadable` and `photoStorageUnavailable`. The expected client statuses (400/413) are not reported. The 503 storage case is reported.

#### 4. Warm-up exclusion

**File**: `PigeonWatch/Frontend/src/app/core/warmup/warmup.interceptor.ts` (+ spec)

**Intent**: Never treat a storage-outage 503 as a cold start.

**Contract**: A 503 whose ProblemDetails `errors` contains `photoStorageUnavailable` passes straight through. Every other 503 or status-0 response keeps today's behaviour.

#### 5. Labels and CSP

**File**: `PigeonWatch/Api/BusinessObjects/Resources/UiLabels.resx`, `PigeonWatch/Frontend/public/staticwebapp.config.json`, `src/app/shared/map/map-config.spec.ts`

**Intent**:
- Add `sighting.form.photo*` labels: field label, hint, remove, the three errors and the storage-unavailable message.
- Add `blob:` to `img-src`. This is a deliberate security trade-off: it only admits object URLs created by our own scripts. It is verified live in Phase 6.
- Update the spec that asserts the `img-src` line, because the requirement changed.

**Contract**: `img-src 'self' data: blob: https://tile.openstreetmap.org`.

### Success Criteria:

#### Automated Verification:

- Frontend specs pass: `npm test -- --watch=false` (run in `PigeonWatch/Frontend`). They cover:
  - FormData with and without a photo;
  - the picker choosing, previewing and removing a photo, plus the decode-failure field error;
  - server photo codes and 413 → photo field error, not reported;
  - storage 503 → alert, reported, form keeps its values;
  - warm-up passes the storage 503 through and still retries a plain 503;
  - label coverage;
  - the CSP spec.
- Build passes within the initial-bundle budget: `npm run build`

#### Manual Verification:

- On a phone-width viewport against the local API: pick a large phone photo, see the preview, submit, and land on the detail page; the stored file in the local folder is at most 1600 px and has no EXIF data
- Submitting with storage made to fail (local folder made read-only) keeps the form filled and shows the retry-or-remove message

**Implementation Note**: Pause for confirmation before Phase 5.

---

## Phase 5: SPA display

### Overview

Show the photo on the detail page and the camera icon in rows. Add kitchen-sink cells and one Playwright test.

### Changes Required:

#### 1. Detail page photo

**File**: `PigeonWatch/Frontend/src/app/features/sightings/sighting-detail-page/` (ts, html, scss, specs)

**Intent**: When `hasPhoto` is true, load the photo with `sightingsApi.photo(id)`, show it as a figure before the details list with translated alt text, and revoke the object URL on navigation and on destroy.

- While the photo loads, show a fixed-ratio placeholder.
- If loading fails, show an inline message and report through `ErrorReportingService` under `'sighting.detail.photo'`.
- The rest of the page never waits for the photo.

**Contract**: Layout uses tokens only (`style-token-coverage.spec.ts`). The image is capped at the column width and keeps its aspect ratio.

#### 2. Row icon

**File**: `PigeonWatch/Frontend/src/app/features/sightings/shared/sighting-row/` (ts, html, scss, spec)

**Intent**: When `hasPhoto` is true, show an inline SVG camera glyph with a translated accessible label.

**Contract**: Label key `sighting.row.hasPhoto`. The SVG is `aria-hidden`, and the label is visually hidden.

#### 3. Kitchen sink

**File**: `src/app/dev/kitchen-sink/cell-definitions.ts`, `cell-stubs.ts`, `kitchen-sink.spec.ts`

**Intent**: Add cells `detail-photo-loaded`, `detail-photo-loading` and `detail-photo-error`, a `photo()` stub mode (a generated image, pending, or failure), and `hasPhoto` on some list fixtures. Each new cell gets a marker assertion, per Frontend CLAUDE.md.

#### 4. Playwright

**File**: `PigeonWatch/Frontend/tests/e2e/sighting-photo.spec.ts`, plus a small JPEG fixture under `tests/e2e/fixtures/`

**Intent**: Report a sighting with a photo through the real form and API, land on the detail page and see the image rendered with a non-zero natural width. Then find the camera icon on that sighting's row in My sightings.

#### 5. Docs

**File**: `PigeonWatch/Api/CLAUDE.md`, `PigeonWatch/Frontend/CLAUDE.md`

**Intent**: Record the new conventions:
- the photo store seam and the `BlobStorage` settings;
- the binary-action exception to the controller rule;
- the multipart create contract;
- the warm-up exclusion for storage outages;
- the `blob:` CSP entry;
- the new kitchen-sink cells;
- the new initial-bundle measurement.

### Success Criteria:

#### Automated Verification:

- Frontend specs pass, including the detail page (photo loaded, loading, error reported, no request when `hasPhoto` is false, URL revoked on destroy), the row icon, the kitchen-sink markers and style-token coverage: `npm test -- --watch=false`
- Build passes within the initial-bundle budget: `npm run build`
- Playwright photo spec passes against the local API: `npx playwright test tests/e2e/sighting-photo.spec.ts`

#### Manual Verification:

- Kitchen-sink photo cells look right at narrow and wide widths and in dark mode
- Detail page with a portrait and a landscape photo: no layout shift of the details list, readable at phone width

**Implementation Note**: Pause for confirmation before Phase 6.

---

## Phase 6: Live verification after deploy

### Overview

After the merge deploys, check on Azure the parts that only exist there: the managed identity reaching Blob Storage, SkiaSharp's native library on Linux F1, and the CSP served by Static Web Apps. Then close the open deploy-plan task.

### Changes Required:

#### 1. Deploy plan

**File**: `context/deployment/deploy-plan.md`

**Intent**: Tick the "Build task, Blob half (slice S-04)" item with the verification evidence. Record whether `allowBlobPublicAccess` is false on `pigeonwatchstorage`. Mark the storage-key wording in `infrastructure.md:79,103` as superseded by managed identity.

### Success Criteria:

#### Manual Verification:

- Deploy workflow green, including the `/configuration/client` probe, which now returns the photo limits
- On the deployed SPA, report a sighting with a phone photo and see it on the detail page with no CSP violation in the browser console
- `az storage blob list --account-name pigeonwatchstorage --container-name sighting-photos --auth-mode login` shows the new blob, and the downloaded file has no EXIF data
- `az storage account show --name pigeonwatchstorage --query allowBlobPublicAccess` returns `false`, or it is set to false
- `curl.exe -i https://pigeonwatch-api.azurewebsites.net/sightings/<id>/photo` without a token returns 401
- App Service logs show no SkiaSharp `DllNotFoundException`

---

## Testing Strategy

### Unit Tests:

- Store adapters (local folder), the options validator, and the 503 handler.
- Normalization: type checked from the file's bytes, size and pixel limits, orientation, downscaling, metadata removal.
- Service orchestration: the call order and compensating delete, so no store call happens on any rejection path.

### Integration Tests:

- The real pipeline over multipart: create with and without a photo, photo download (authorized, anonymous, missing), every error code, the 413 limit, the 503 storage outage with no row saved, `hasPhoto` on every read, the client configuration, and the coverage, protection and privacy sweeps.

### Manual Testing Steps:

1. Phone-width report with a large HEIC- or JPEG-origin photo → preview → submit → detail shows the photo.
2. Inspect the stored file: at most 1600 px, no EXIF data.
3. Force a storage failure → the form keeps its values and shows the retry-or-remove message with a reference id.
4. On production: the CSP is clean, an anonymous photo request gets 401, and no native-library errors appear in the logs.

## Performance Considerations

- The browser re-encode keeps uploads to roughly 300–600 KB, which bounds bandwidth through the F1 API and the CPU spent on the server re-encode.
- Images are rejected by header dimensions before decoding, so a crafted file can't exhaust memory.
- No thumbnails, and only one photo request per detail view.
- The photo UI stays in lazy feature chunks. Check `npm run build` against the 540 kB warning limit.

## Migration Notes

- `AddSightingPhoto` only adds a nullable column, so the previously deployed build keeps working (`deploy-plan.md:109`).
- Existing rows get `hasPhoto:false`.
- Rollback: revert the deploy. The column can stay; dropping it follows the rollback runbook at `deploy-plan.md:115`.

## References

- Research: `context/changes/attach-sighting-photo/research.md`
- Roadmap item: `context/foundation/roadmap.md:136-146`
- Storage provisioning and open task: `context/deployment/deploy-plan.md:63-77`
- Outage handler pattern: `PigeonWatch/Api/Data/Diagnostics/DatabaseUnavailableExceptionHandler.cs:11-77`
- Custom form-control pattern: `PigeonWatch/Frontend/src/app/shared/map/location-picker/location-picker.ts`
- Lessons: `context/foundation/lessons.md` (use `curl.exe` in PowerShell commands)

## Progress

> Convention: `- [ ]` pending, `- [x]` done. Append ` — <commit sha>` when a step lands. Do not rename step titles. See `references/progress-format.md`.

### Phase 1: Photo storage seam

#### Automated

- [x] 1.1 Solution builds with style enforcement — 75cba79
- [x] 1.2 Architecture tests pass — 75cba79
- [x] 1.3 Unit tests cover local-folder store, options validator and 503 handler — 75cba79
- [x] 1.4 No vulnerable packages — 75cba79

#### Manual

- [x] 1.5 Development starts with LocalDirectory; Production without BlobStorage fails at start-up — 75cba79

### Phase 2: Server-side photo normalization (test-first)

#### Automated

- [x] 2.1 Normalization unit tests written first and passing — 3381194
- [x] 2.2 Architecture tests pass — 3381194

### Phase 3: API create and read

#### Automated

- [x] 3.1 Build passes — 2d25b9a
- [x] 3.2 Migration matches the model — 2d25b9a
- [x] 3.3 Architecture tests pass, including the binary-action rule — 2d25b9a
- [x] 3.4 Service orchestration unit tests pass — 2d25b9a
- [x] 3.5 Integration tests pass — 2d25b9a

#### Manual

- [x] 3.6 Local multipart create stores a file and GET photo returns it — 2d25b9a

### Phase 4: SPA reporting

#### Automated

- [x] 4.1 Frontend specs pass — 0ed0136
- [x] 4.2 Build passes within the initial-bundle budget — 0ed0136

#### Manual

- [x] 4.3 Phone-width report with a large photo; stored file is at most 1600 px with no EXIF — 0ed0136
- [x] 4.4 Storage failure keeps the form filled with the retry-or-remove message — 0ed0136

### Phase 5: SPA display

#### Automated

- [x] 5.1 Frontend specs pass (detail photo, row icon, kitchen sink, tokens) — 53d7e07
- [x] 5.2 Build passes within the initial-bundle budget — 53d7e07
- [x] 5.3 Playwright photo spec passes — 53d7e07

#### Manual

- [x] 5.4 Kitchen-sink photo cells look right at both widths and in dark mode — 53d7e07
- [x] 5.5 Portrait and landscape photos render without layout shift — 53d7e07

### Phase 6: Live verification after deploy

#### Manual

- [x] 6.1 Deploy workflow green and probe returns photo limits — 818a8ab
- [x] 6.2 Deployed report with photo shows it with no CSP violation — 818a8ab
- [x] 6.3 Blob present in sighting-photos and has no EXIF — 818a8ab
- [x] 6.4 allowBlobPublicAccess is false — 818a8ab
- [x] 6.5 Anonymous photo request returns 401 — 818a8ab
- [x] 6.6 No SkiaSharp native-library errors in App Service logs — 818a8ab
