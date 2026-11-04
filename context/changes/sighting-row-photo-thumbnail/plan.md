# Sighting row photo thumbnail Implementation Plan

## Overview

Show a real photo thumbnail in sighting list rows (the sighting list page and My sightings) instead of only the camera icon. Each thumbnail is created once, at upload, and stored next to the full photo. A photo uploaded before this change gets its thumbnail on first request, and that thumbnail is stored.

## Current State Analysis

See `research.md`. In short:

- `SightingService.CreateAsync` runs validate → cooldown → normalize → store `<guid>.jpg` → insert, and deletes the blob in a `finally` when the insert fails.
- `GET sightings/{id}/photo` streams the full photo.
- The row (`li[app-sighting-row]`) shows an inline camera glyph and the visually hidden `sighting.row.hasPhoto` label.
- The local-folder store accepts only `GUID.jpg` names.
- The in-memory store can only fail as a whole.

## Desired End State

- A new upload stores both `<guid>.jpg` and `<guid>-thumb.jpg`. The thumbnail is a square centre crop of at most 160 px (never upscaled), encoded as JPEG at quality 70.
- `GET sightings/{id:guid}/photo/thumbnail` streams the stored thumbnail. When the thumbnail is missing, the endpoint creates it from the full photo, stores it and returns it. When the stored save fails, the failure is logged with its cause and the bytes are still returned.
- Rows with a photo show a 3rem square at the row start. The square holds the camera icon until the thumbnail loads, keeps it if loading fails, and then shows the image. The thumbnail loads only when the row scrolls into view.

### Key Discoveries:

- `Api/BusinessLogic/Services/SightingService.cs`: `CreateAsync` and `DeleteOrphanedPhotoAsync` hold the compensation pattern to extend.
- `Api/Data/Storage/LocalFolderSightingPhotoStore.cs` `IsValidBlobName` rejects `-thumb.jpg`.
- `Frontend/src/app/features/sightings/sighting-detail-page/sighting-detail-page.ts` (`photoOf`/`objectUrlOf`) is the object-URL stream pattern to mirror.
- `Frontend/src/app/dev/kitchen-sink/kitchen-sink.spec.ts` already stubs `PhotoEncoder`; `CellStubs.sightingsApi` lacks a thumbnail method.
- `Frontend/tests/e2e/sighting-photo.spec.ts` looks for the camera icon on the row, which the loaded thumbnail replaces.

## What We're NOT Doing

- No backfill job and no migration: the thumbnail name is derived from `PhotoBlobName`.
- No per-request resizing of stored thumbnails, and no HTTP caching (the responses stay `private, no-store`).
- No thumbnail on the nearby map or on the detail page (the detail page keeps its 200 px crop of the full photo).
- No square reserved on rows without a photo.

## Implementation Approach

The work is test-first at the provider, then the API, then the SPA. The thumbnail is produced in the normalize step, before any storage call, so a rejected or undecodable photo still never touches storage. The full photo is saved first. The thumbnail save and the insert then run inside the existing compensation scope, so any failure there deletes both blobs.

## Critical Implementation Details

- **Compensation scope.** The thumbnail save must sit inside the `try` whose `finally` deletes blobs. Otherwise a failed thumbnail save leaves the full photo orphaned. Each blob delete is attempted and logged on its own. `DeleteAsync` of a blob that was never written is a no-op in every store.
- **Create-on-miss save failure.** In `OpenPhotoThumbnailAsync`, a failed store of the regenerated thumbnail is logged at Error with the exception, and the bytes are still returned. Cancellation is not logged as a failure.
- **jsdom fallback.** When `IntersectionObserver` is undefined, the row treats itself as visible immediately. Specs therefore exercise the load without an observer polyfill.

## Phase 1: Thumbnail provider (test-first)

### Overview

A SkiaSharp sibling of `PhotoNormalizationProvider` that turns a normalized JPEG into a square thumbnail.

### Changes Required:

#### 1. Rules

**File**: `PigeonWatch/Api/BusinessObjects/SightingRules.cs`

**Intent**: Hold the thumbnail size, quality and blob-name suffix next to the other photo rules.

**Contract**: `PhotoThumbnailSize = 160`, `PhotoThumbnailJpegQuality = 70`, `PhotoBlobExtension = ".jpg"`, `PhotoThumbnailBlobSuffix = "-thumb"`.

#### 2. Provider

**File**: `PigeonWatch/Api/BusinessLogic/Services/PhotoThumbnailProvider.cs` and `Interfaces/IPhotoThumbnailProvider.cs`, registered scoped in `BusinessLogicServiceCollectionExtensions`

**Intent**: Decode the JPEG, take the centred square of side equal to the shorter edge, scale it to `min(PhotoThumbnailSize, shorter edge)` and encode it as JPEG at `PhotoThumbnailJpegQuality`. Undecodable input throws `InvalidOperationException`, because the input is always our own normalized JPEG.

**Contract**: `byte[] CreateThumbnail(byte[] jpeg)`.

#### 3. Tests first

**File**: `PigeonWatch/Api/UnitTests/PhotoThumbnailProviderTests.cs`

**Intent**: Pin the behaviour before writing the provider: a landscape image comes out as a 160×160 JPEG whose content is the centre (side bands cropped away); a portrait image is also 160×160 and centre-cropped; a 100×50 image comes out 50×50 (no upscaling); garbage bytes throw.

### Success Criteria:

#### Automated Verification:

- Thumbnail provider tests written first and passing: `dotnet test PigeonWatch/Api/UnitTests/PigeonWatch.UnitTests.csproj`
- Architecture tests pass: `dotnet test PigeonWatch/Api/ArchitectureTests/PigeonWatch.ArchitectureTests.csproj`

---

## Phase 2: API create and thumbnail endpoint

### Overview

Store the thumbnail at upload and serve it, creating a missing one on first request.

### Changes Required:

#### 1. Service

**File**: `PigeonWatch/Api/BusinessLogic/Services/SightingService.cs` (+ `ISightingService`)

**Intent**:
- `CreateAsync` creates the thumbnail right after a successful normalize, saves the full photo, then saves the thumbnail and inserts inside the `try`.
- The `finally` deletes both blobs when the insert did not succeed. Each failed delete is logged at Error with its cause.
- New `OpenPhotoThumbnailAsync`:
  - returns null when the sighting has no photo;
  - returns the stored thumbnail stream when present;
  - otherwise opens the full photo; a missing full photo logs a Warning and returns null;
  - otherwise creates the thumbnail, tries to save it (a failure is logged at Error with its cause) and returns a stream over the bytes.

**Contract**: `Task<Stream?> OpenPhotoThumbnailAsync(Guid sightingId, CancellationToken cancellationToken = default)`. The thumbnail name is `<stem>-thumb.jpg`, where `<stem>` is `PhotoBlobName` without `.jpg`.

#### 2. Endpoint

**File**: `PigeonWatch/Api/WebApi/Controllers/SightingsController.cs`

**Intent**: Add `GetPhotoThumbnail`, a mirror of `GetPhoto`.

**Contract**: `[HttpGet("{id:guid}/photo/thumbnail")]`, `[Produces("image/jpeg")]`, `Task<ActionResult>`, `Cache-Control: private, no-store`, `NotFoundForSighting(id)` on null. `[Authorize]` is inherited from the controller.

#### 3. Stores

**Files**: `Api/Data/Storage/LocalFolderSightingPhotoStore.cs`, `Api/IntegrationTests/InMemorySightingPhotoStore.cs`, `Api/UnitTests/LocalFolderSightingPhotoStoreTests.cs`

**Intent**:
- The local-folder name check accepts `GUID.jpg` and `GUID-thumb.jpg` and keeps rejecting everything else (test both).
- The in-memory store gets a per-name save-failure predicate and records the names it has saved, so tests can count thumbnail saves.

#### 4. Tests

- **Unit** (`SightingServicePhotoTests`):
  - the upload saves the full photo and the thumbnail and inserts;
  - a failed thumbnail save rethrows `PhotoStorageUnavailableException`, does not insert and deletes the full photo;
  - a failed insert and `ReporterNotFound` delete both blobs;
  - a failed delete is logged with its cause;
  - the thumbnail read returns null without store calls when the sighting has no photo;
  - a stored thumbnail is returned without calling the provider;
  - a missing thumbnail is created from the full photo, saved and returned;
  - a failed save of the created thumbnail is logged at Error and the bytes are still returned;
  - a missing full photo logs a Warning and returns null.
- **Integration** (`SightingPhotoContractTests`):
  - an upload stores both blobs and the thumbnail streams back as a ≤160 px JPEG with `private, no-store`;
  - a failed thumbnail save returns 503 `PhotoStorageUnavailable`, keeps no blob and inserts no row;
  - a sighting without a photo returns 404;
  - a missing thumbnail is created and stored once (a second request does not save again);
  - a missing full blob returns 404 with a Warning;
  - an anonymous request returns 401.
- **Fixtures**: add a `GET sightings/{id:guid}/photo/thumbnail` entry to `EndpointFixtureRegistry` so the protection and privacy sweeps cover it.

#### 5. Docs

**File**: `PigeonWatch/Api/CLAUDE.md`

**Intent**: Add the endpoint to the surface list (seven `sightings` endpoints), and describe the thumbnail rules, the compensation and the create-on-miss behaviour under "Sighting photos".

### Success Criteria:

#### Automated Verification:

- Solution builds with style enforcement: `dotnet build PigeonWatch/Api/PigeonWatchApi.slnx`
- Architecture tests pass, including the binary-action rule
- Unit tests pass
- Integration tests pass, including the sweeps

---

## Phase 3: SPA lazy row thumbnail

### Overview

Rows with a photo load and show the thumbnail when they scroll into view.

### Changes Required:

#### 1. API client

**File**: `Frontend/src/app/features/sightings/sightings-api.ts` (+ spec)

**Contract**: `photoThumbnail(id: string): Observable<Blob>` → `GET {apiUrl}/sightings/{id}/photo/thumbnail` with `responseType: 'blob'`.

#### 2. Row

**Files**: `Frontend/src/app/features/sightings/shared/sighting-row/sighting-row.{ts,html,scss,spec.ts}`, `Frontend/src/styles/_tokens.scss`

**Intent**:
- A row with `hasPhoto` renders a `--pw-row-thumb-size` (3rem) square at the start of the link, beside a text column holding the name and meta.
- The square shows the camera glyph until a thumbnail URL exists, and shows `<img alt="">` with `object-fit: cover` once it does.
- `sighting.row.hasPhoto` stays visually hidden in the name.
- After the first render, an `IntersectionObserver` on the host marks the row visible once and disconnects. Without `IntersectionObserver`, the row is visible immediately.
- When the row is visible and the sighting has a photo, the row loads `photoThumbnail(id)` into an object URL through `PhotoEncoder.createPreviewUrl`. The URL is revoked on change and on destroy.
- A failure reports `api-failure` under `sighting.row.photo` and keeps the icon.
- Tokens only.

**Spec**:
- no request for a row without a photo;
- one request and an image with the object URL;
- the icon stays while pending and after an error, and the error is reported;
- revoke on destroy;
- no request until the observer reports an intersection (stub observer);
- the label is still present.

#### 3. Kitchen sink

**Files**: `Frontend/src/app/dev/kitchen-sink/cell-stubs.ts`, `cell-definitions.ts`, `kitchen-sink.spec.ts`

**Intent**:
- `CellStubs.sightingsApi.photoThumbnail` returns the sink photo by default. An optional `thumbnail` mode on list cells (`image`, `pending`, `failure`) selects the result.
- Add the cells `list-thumbnails-pending` and `list-thumbnails-error`.
- Markers:
  - `list-loaded` photo rows show an `img` with the stub URL;
  - in the pending and error cells, the photo rows show the camera icon and no `img`;
  - My sightings photo rows show the `img`.

#### 4. E2E

**File**: `Frontend/tests/e2e/sighting-photo.spec.ts`

**Intent**: In My sightings, the new row shows a thumbnail image with a non-zero natural width, instead of the camera icon.

#### 5. Docs

**File**: `PigeonWatch/Frontend/CLAUDE.md`

**Intent**: Update the row description, the kitchen-sink stub and cell lists, the "Sighting photos" section and the measured initial bundle.

### Success Criteria:

#### Automated Verification:

- Frontend specs pass: `npm test` in `PigeonWatch/Frontend`
- Build passes within the initial-bundle budget: `npm run build`

#### Manual Verification:

- List and My sightings rows show thumbnails at phone width, with no layout shift while they load
- Scrolling a long list requests thumbnails only for rows that come into view

## Testing Strategy

### Unit Tests:

- Provider crop, size and no-upscale behaviour; service create compensation and the thumbnail read paths.

### Integration Tests:

- Both blobs stored at upload, 503 cleanup, endpoint 200/404/401, create-on-miss stored once, sweeps.

### Manual Testing Steps:

1. Report a sighting with a photo, open the list and My sightings, and see the thumbnail.
2. Delete a `-thumb.jpg` from `LocalPhotos` and reload: the thumbnail comes back and the file reappears.

## Performance Considerations

- One extra small encode per upload, and at most one regeneration per pre-existing photo. Serving a stored thumbnail costs no CPU on the F1 plan.
- Rows request thumbnails only when visible, so a 30-row page does not fetch every thumbnail at once.

## Migration Notes

- None. Existing photos get their thumbnail lazily, through the create-on-miss path.

## References

- Research: `context/changes/sighting-row-photo-thumbnail/research.md`
- Pattern: `PigeonWatch/Frontend/src/app/features/sightings/sighting-detail-page/sighting-detail-page.ts` (`photoOf`)
- Pattern: `PigeonWatch/Api/BusinessLogic/Services/PhotoNormalizationProvider.cs`

## Progress

> Convention: `- [ ]` pending, `- [x]` done. Append ` — <commit sha>` when a step lands. Do not rename step titles. See `references/progress-format.md`.

### Phase 1: Thumbnail provider (test-first)

#### Automated

- [x] 1.1 Thumbnail provider tests written first and passing — 3579f15
- [x] 1.2 Architecture tests pass — 3579f15

### Phase 2: API create and thumbnail endpoint

#### Automated

- [x] 2.1 Solution builds with style enforcement
- [x] 2.2 Architecture tests pass, including the binary-action rule
- [x] 2.3 Unit tests pass
- [x] 2.4 Integration tests pass, including the sweeps

### Phase 3: SPA lazy row thumbnail

#### Automated

- [ ] 3.1 Frontend specs pass
- [ ] 3.2 Build passes within the initial-bundle budget

#### Manual

- [ ] 3.3 List and My sightings rows show thumbnails at phone width, with no layout shift while they load
- [ ] 3.4 Scrolling a long list requests thumbnails only for rows that come into view
