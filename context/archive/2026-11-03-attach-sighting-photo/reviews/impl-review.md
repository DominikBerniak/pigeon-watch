<!-- IMPL-REVIEW-REPORT -->
# Implementation Review: Attach Sighting Photo

- **Plan**: context/changes/attach-sighting-photo/plan.md
- **Scope**: Full plan
- **Reviewed phases**: 1, 2, 3, 4, 5, 6
- **Date**: 2026-11-07
- **Verdict**: NEEDS ATTENTION
- **Findings**: 0 critical, 4 warnings, 5 observations

Phases 1–5 were reviewed as committed at `53d7e07`, and Phase 6 at `818a8ab`. They were squash-merged as `6c1715f` (PR #31). The later `sighting-row-photo-thumbnail` change touches some of the same files and is out of scope. The automated criteria were run on `main` at `078548a`, which includes that later change:
- build: 0 errors;
- migrations: no pending model changes;
- tests: architecture 44/44, unit 376/376, integration 150/150, frontend 853/853;
- `npm run build`: 529.87 kB;
- no vulnerable packages.

## Verdicts

| Dimension | Verdict |
|-----------|---------|
| Plan Adherence | PASS |
| Scope Discipline | WARNING |
| Safety & Quality | WARNING |
| Architecture | PASS |
| Pattern Consistency | WARNING |
| Success Criteria | WARNING |

## Findings

### F1 — Full-size decode up to 40 MP can exhaust F1 memory

- **Severity**: ⚠️ WARNING
- **Impact**: 🔎 MEDIUM — real tradeoff; pause to reason through it
- **Dimension**: Safety & Quality
- **Location**: PigeonWatch/Api/BusinessLogic/Services/PhotoNormalizationProvider.cs:67-72, PigeonWatch/Api/BusinessObjects/SightingRules.cs:19
- **Detail**:
  - The header check rejects files above `PhotoMaxPixels = 40_000_000`. Anything below that is decoded at full size into an Rgba8888 `SKBitmap`, which can reach about 160 MB, with possibly another copy in `SKImage.FromBitmap`.
  - A flat 40 MP JPEG fits under the 2 MB byte cap. The SPA re-encodes to 1600 px first, but a direct API caller does not.
  - The App Service plan is F1 (about 1 GB), the work is synchronous, and nothing caps concurrency, so two such uploads at once could take the worker down.
- **Fix A ⭐ Recommended**: Decode at a reduced scale (`codec.GetScaledDimensions` or a sample size close to the 1600 px target) and lower `PhotoMaxPixels` to about 16 MP.
  - Strength: removes the memory peak at its source while keeping full quality for normal phone photos.
  - Tradeoff: needs new tests for scaled decoding, so the orientation and dimension tests must still hold.
  - Confidence: MED — SkiaSharp's scaled JPEG decode is supported, but this repo has not measured it.
  - Blind spot: the memory usage on the F1 plan has not been measured.
- **Fix B**: Keep full-size decoding and add a `SemaphoreSlim` (1–2) around normalization, with a lower `PhotoMaxPixels`.
  - Strength: a small change that bounds peak memory.
  - Tradeoff: uploads queue up when several arrive at once, and each one still needs up to about 64 MB at 16 MP.
  - Confidence: HIGH — a straightforward guard.
  - Blind spot: the queue delay under bursts.
- **Decision**: FIXED (Fix A). `PhotoMaxPixels` is now 16 MP. The provider decodes at the smallest eighth-step scale whose longest edge is still at least 1600 px: JPEG and WebP 4000×3000 decode at 2000×1500, checked with a probe, while PNG decodes at full size, bounded by the cap. The 16 MP rejection test was written first (red, then green). A large orientation-6 guard test was added, and `Api/CLAUDE.md` is updated.

### F2 — The "body too large" warning drops the cause and over-matches

- **Severity**: ⚠️ WARNING
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Pattern Consistency
- **Location**: PigeonWatch/Api/DependencyInjection/WebApiServiceCollectionExtensions.cs:45-58
- **Detail**:
  - The replacement `InvalidModelStateResponseFactory` logs `Request rejected with {ErrorCode} on {Route}` without the ModelState error that triggered it. That goes against the repo hard rule against dropping error causes.
  - It also maps any empty-key form error on a body above the limit to `PhotoTooLarge`, so a different form failure would be mislabelled and leave no evidence behind.
  - `ModelError.Exception` is null at runtime, as observed during Phase 3, so the cause that is available is the error message.
- **Fix**: Add the ModelState error message as a structured field to the warning (for example `{FormError}`).
- **Decision**: FIXED. The warning now carries `{FormError}`, the empty-key ModelState error message, and `Body_above_the_request_limit_is_400_photo_too_large_and_logged_at_warning` asserts it. The test was red before the change and is green after.

### F3 — Manual check 4.4 cannot pass as written with the local store

- **Severity**: ⚠️ WARNING
- **Impact**: 🔎 MEDIUM — real tradeoff; pause to reason through it
- **Dimension**: Success Criteria
- **Location**: PigeonWatch/Api/Data/Storage/LocalFolderSightingPhotoStore.cs:9-37, plan Progress 4.4
- **Detail**:
  - Check 4.4 says to make the local folder read-only and then expect the "retry or remove" message.
  - `LocalFolderSightingPhotoStore` does not wrap `IOException` or `UnauthorizedAccessException` in `PhotoStorageUnavailableException`, so that scenario reaches the generic 500 and alert, not the 503 path.
  - This caveat was raised when 4.4 was handed over, and 4.4 was ticked anyway. It is unclear how it was verified.
  - Production uses the Azure store, which does wrap these failures, and the 503 path is covered by integration tests with the in-memory failing store. So the gap is in the evidence, not in production behaviour.
- **Fix A ⭐ Recommended**: Wrap `IOException` and `UnauthorizedAccessException` in `PhotoStorageUnavailableException` in the local store, keeping the inner exception, so the manual check behaves as the plan describes.
  - Strength: keeps Development and Production behaving the same for storage outages, and makes 4.4 reproducible.
  - Tradeoff: a local disk problem now looks like a "retry" outage in Development.
  - Confidence: HIGH — mirrors `AzureBlobSightingPhotoStore`.
  - Blind spot: none significant.
- **Fix B**: Record in the plan that 4.4 was verified through the in-memory failing store and the integration test, and reword the check.
  - Strength: no code change.
  - Tradeoff: the Development behaviour stays different from Production.
  - Confidence: MED — depends on how 4.4 was actually checked.
  - Blind spot: how the user verified 4.4.
- **Decision**: FIXED (Fix A). `LocalFolderSightingPhotoStore` wraps `IOException` and `UnauthorizedAccessException` from save, read and delete in `PhotoStorageUnavailableException`, keeping the inner exception. A missing file on read still returns null. Two unit tests were written first and were red, then green: a save with the root path blocked by a file, and a read of a blob locked with `FileShare.None`. `Api/CLAUDE.md` "Storage outage" is updated.

### F4 — Undocumented additions beyond the plan

- **Severity**: ⚠️ WARNING
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Scope Discipline
- **Location**: PigeonWatch/Api/WebApi/Models/CreateSightingRequestModel.cs:21-31; PigeonWatch/Frontend/src/app/features/sightings/report-sighting-page/photo-picker/photo-picker.ts:102-106, 117-121, 143; PigeonWatch/Api/Data/Storage/AzureBlobSightingPhotoStore.cs:71-77
- **Detail**: Each of these is defensible, but none is recorded in the plan or in `change.md`:
  - `[BindRequired]` on `Latitude`, `Longitude`, `Status` and `Criticality`, because form binding would otherwise turn a missing field into 0;
  - a client-side type check before decoding, and a size check after encoding, in the picker;
  - encode failures reported under `sighting.photo.encode`;
  - the Azure store also wrapping `AggregateException` and an `OperationCanceledException` the caller did not request.
- **Fix**: Add a short addendum to the Notes in `change.md` listing these four additions.
- **Decision**: FIXED. An addendum in the Notes of `change.md` lists the four additions.

### F5 — Any `RequestFailedException` becomes a "retry later" 503

- **Severity**: 💡 OBSERVATION
- **Impact**: 🔎 MEDIUM — real tradeoff; pause to reason through it
- **Dimension**: Safety & Quality
- **Location**: PigeonWatch/Api/Data/Storage/AzureBlobSightingPhotoStore.cs:71-77
- **Detail**:
  - The store treats every `RequestFailedException` as an outage. That includes 403 (a missing role), 404 `ContainerNotFound` and 409, which are configuration faults a retry never fixes.
  - The user is told to retry, while the operator sees only an Error log, with the cause kept.
  - A request the caller aborted is correctly not wrapped.
- **Fix**: Keep 503 for 408, 429, 5xx and transport failures, and let 4xx configuration failures become a 500, logged with their cause.
- **Decision**: FIXED. `AzureBlobSightingPhotoStore.IsUnavailable` (now `internal static`) wraps a `RequestFailedException` only for status 0, 408, 429 or 5xx, so other 4xx answers reach the generic 500 with their cause. The blob-missing 404 on read is still caught earlier and returns null. The new `AzureBlobSightingPhotoStoreTests` were red on 400, 403, 404 and 409, and are now green. `Api/CLAUDE.md` "Storage outage" is updated.

### F6 — The detail page reports every photo failure, including expected statuses

- **Severity**: 💡 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Pattern Consistency
- **Location**: PigeonWatch/Frontend/src/app/features/sightings/sighting-detail-page/sighting-detail-page.ts:265-269
- **Detail**:
  - The page's main stream reports only failures that `isReportableSightingFailure` accepts. The photo stream reports every error.
  - That includes 401 and 429, which Frontend CLAUDE.md ("Reporting errors") treats as expected.
  - A 404 here, where `hasPhoto` is true but the blob is missing, is unexpected, so reporting it is right.
- **Fix**: Skip the report when `isExpectedClientStatus(error)` is true and the status is not 404.
- **Decision**: FIXED. The photo stream on the detail page reports a failure only when it is a 404 (a missing blob) or not an expected client status, so 400, 401 and 429 now show the inline message without a report. New specs (`it.each([400, 401, 429])` silent, and 404 reported) were red, then green. Frontend 857/857. `Frontend/CLAUDE.md` "Detail photo" is updated.

### F7 — The detail page imports `PhotoEncoder` from the report page's folder

- **Severity**: 💡 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Pattern Consistency
- **Location**: PigeonWatch/Frontend/src/app/features/sightings/sighting-detail-page/sighting-detail-page.ts:34
- **Detail**:
  - The object-URL helpers sit in `report-sighting-page/photo-picker/photo-encoder.ts`, but the detail page uses them too, and so do the rows in the later thumbnail change.
  - Frontend CLAUDE.md puts components and helpers that several sightings views share under `features/sightings/shared/`.
- **Fix**: Move `photo-encoder.ts` (and its spec) to `features/sightings/shared/` and update the imports.
- **Decision**: FIXED. `photo-encoder.ts` and its spec were moved with `git mv` to `features/sightings/shared/`, and the 8 imports were updated, including the photo picker and the kitchen-sink spec. Frontend 857/857 pass, and `npm run build` is unchanged at 529.87 kB. `Frontend/CLAUDE.md` paths are updated.

### F8 — Browser decode failures all show as "unsupported type"

- **Severity**: 💡 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Safety & Quality
- **Location**: PigeonWatch/Frontend/src/app/features/sightings/report-sighting-page/photo-picker/photo-encoder.ts:43-45, photo-picker.ts:137-141
- **Detail**:
  - Every `createImageBitmap` rejection becomes `PhotoDecodeError`, which the picker shows as "unsupported type" and does not report.
  - That fits HEIC, but an out-of-memory rejection on a very large image is also shown silently as "unsupported type".
  - A bitmap that resolves after the subscription is cancelled is never `close()`d.
- **Fix**: Pass `resizeWidth`/`resizeHeight` to `createImageBitmap`, and close a bitmap that arrives after cancellation.
- **Decision**: FIXED differently (narrower). `PhotoEncoder.decode` now closes a bitmap that resolves after the encoding was cancelled. A new spec was red, then green, and Frontend passes 858/858. The `resizeWidth`/`resizeHeight` part was dropped, because the orientation is unknown before decoding, so a single target edge cannot keep the 1600 px longest-edge bound. HEIC and out-of-memory both still show "unsupported type" without a report, which is accepted and documented in `Frontend/CLAUDE.md` under "PhotoEncoder seam".

### F9 — Orphan blobs and streaming failures are accepted, not documented

- **Severity**: 💡 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Safety & Quality
- **Location**: PigeonWatch/Api/BusinessLogic/Services/SightingService.cs:72-88; PigeonWatch/Api/Data/Storage/LocalFolderSightingPhotoStore.cs:13-14; PigeonWatch/Api/WebApi/Controllers/SightingsController.cs:94
- **Detail**:
  - A process crash between storing the blob and inserting the row leaves an orphan blob, and nothing sweeps it up. The plan accepted orphans only when the compensating delete fails.
  - The local store leaves a partial file when a save is cancelled.
  - A transport failure while streaming the photo, after the headers are sent, truncates the response instead of returning a 503.
- **Fix**: Record these in the API CLAUDE.md "Sighting photos" section as accepted limitations.
- **Decision**: FIXED. An "Accepted limitations" bullet in `Api/CLAUDE.md` "Sighting photos" records the orphan blobs left by a crash before the insert, the partial local file left by a cancelled save, and the truncated stream on a mid-stream storage failure.
