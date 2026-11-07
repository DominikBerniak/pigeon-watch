---
date: 2026-11-04
researcher: Claude (Opus 5.5)
git_commit: 53d7e07
branch: claude/heuristic-chatelet-caa219
change_id: sighting-row-photo-thumbnail
status: complete
---

# Research: real photo thumbnails in sighting list rows

## Question

What has to change so that the rows in the sighting list and in My sightings show a real photo thumbnail? The thumbnail is created at upload time, and a missing one is created on first request (decided by the user).

## Settled decisions (user)

- The thumbnail is created at upload time, stored next to the full photo, and served as-is. Per-request resizing was rejected: the F1 App Service has about 60 CPU-minutes a day, and the responses are `private, no-store`, so nothing can cache them.
- Photos uploaded before this change get their thumbnail on first request, and it is stored then. There is no backfill job.

## Findings: API

- **Create order.** In `Api/BusinessLogic/Services/SightingService.cs`, `CreateAsync` runs validate → cooldown → `photoNormalizationProvider.NormalizeAsync` → `sightingPhotoStore.SaveAsync("<guid>.jpg")` → `InsertAsync` inside `try/finally`. On the paths read here, the `finally` calls `DeleteOrphanedPhotoAsync(blobName)` whenever the insert did not succeed: the insert threw, or it returned `ReporterNotFound`. `DeleteOrphanedPhotoAsync` catches the delete failure and logs it at Error with the exception and the trace id. If the full-photo `SaveAsync` throws, nothing has been stored, so the method exits before the `try`.
- **Read path.** `OpenPhotoAsync` returns null without a store call when `GetPhotoBlobNameAsync` returns null. It logs a Warning with `BlobName` and `SightingId` when `OpenReadAsync` returns null. `SightingsController.GetPhoto` (`[HttpGet("{id:guid}/photo")]`, `[Produces("image/jpeg")]`, `Task<ActionResult>`) sets `Cache-Control: private, no-store` and answers `NotFoundForSighting(id)` on null.
- **Blob name.** `PhotoBlobName` is stored as `<guid>.jpg` (const `photoBlobExtension` in the service). `<guid>-thumb.jpg` can be derived from it, so no column or migration is needed.
- **Normalizer.** `PhotoNormalizationProvider.cs` decodes with `SKCodec` into an Rgba8888 bitmap, applies the EXIF orientation, scales the longest edge to at most 1600 and encodes JPEG at `SightingRules.PhotoJpegQuality` (85). The full photo it writes has no orientation tag and no metadata, so a thumbnail made from it needs no orientation handling.
- **Stores.**
  - `LocalFolderSightingPhotoStore.IsValidBlobName` accepts only `GUID("D").jpg` and throws `ArgumentException` on any other name, so `-thumb.jpg` must be added.
  - `AzureBlobSightingPhotoStore` accepts any name, and its `DeleteAsync` uses `DeleteIfExistsAsync`.
  - `IntegrationTests/InMemorySightingPhotoStore.cs` fails only as a whole (`IsUnavailable`). A test where only the thumbnail save fails needs a failure switch per blob name.
- **Architecture rules.**
  - `ControllerActionTests.IsBinaryImageAction` accepts `Task<ActionResult>` with exactly one `[Produces("image/…")]`.
  - Every service and provider has an `I<Name>` interface in `Interfaces/` and is registered in `DependencyInjection` (`BusinessLogicServiceCollectionExtensions` registers `IPhotoNormalizationProvider` as scoped).
  - Async methods take `CancellationToken cancellationToken = default`.
- **Sweeps.** `IntegrationTests/EndpointFixtureRegistry.cs` maps each route key to fixture requests. `EndpointProtectionTests` and `PrivacySweepTests` iterate the routed endpoints, so a new route without a fixture fails them. The sweeps seed a sighting with `TestPhotos.JpegWithExif()`.

## Findings: SPA (state after 53d7e07, Phase 5 of attach-sighting-photo)

- **Row.**
  - `features/sightings/shared/sighting-row/sighting-row.html` shows the camera glyph and `pw-visually-hidden` `sighting.row.hasPhoto` inside `.sighting-row__name` when `hasPhoto` is true.
  - The component has no injected services today, and its spec provides only `provideRouter([])`.
  - `.sighting-row__link` is a flex column.
- **Detail page pattern to mirror.** `sighting-detail-page.ts` `photoOf` / `objectUrlOf`:
  - `SightingsApi.photo(id)` (blob) is turned into an `Observable<string>` that calls `PhotoEncoder.createPreviewUrl` and revokes in its teardown.
  - The stream runs inside `switchMap` with `takeUntilDestroyed`, so the URL is revoked on change and on destroy.
  - Failures are reported with `ErrorReportingService.report('api-failure', 'sighting.detail.photo', error)`.
- **Hosts of the row.** `my-sightings.ts` and `sighting-list-page.ts`. Their specs stub `SightingsApi` with `{ mine }` and `{ list }` only. No row fixture in those two specs has `hasPhoto: true` (the files containing `hasPhoto: true` are `sighting-row.spec.ts` and `sighting-detail-page.spec.ts`), so they make no thumbnail call.
- **Kitchen sink.**
  - In `dev/kitchen-sink/cell-stubs.ts`, `sinkSightings[2]` and every `sinkListSightings` entry with `index % 4 === 1` carry `hasPhoto`.
  - `CellStubs.sightingsApi` has `photo()` but no thumbnail method.
  - `kitchen-sink.spec.ts` provides a `PhotoEncoder` stub (`blob:sink-photo`) and asserts the camera label on `list-loaded` rows.
  - A list or My sightings cell that renders a photo row would call the new thumbnail method.
- **jsdom** has no `IntersectionObserver` and no `URL.createObjectURL`. That is why the specs stub `PhotoEncoder` and why the row needs a fallback.
- **E2E.** `tests/e2e/sighting-photo.spec.ts` finds the camera icon on the new row in My sightings. Once the thumbnail loads, that icon is replaced, so the spec must look for the thumbnail image instead.

## Gaps

- None blocking. Thumbnail size and quality are a guided choice from the brief (about 160 px, modest quality); the plan fixes 160 px at quality 70.
