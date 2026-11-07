<!-- IMPL-REVIEW-REPORT -->
# Implementation Review: Sighting row photo thumbnail

- **Plan**: context/changes/sighting-row-photo-thumbnail/plan.md
- **Scope**: Full plan
- **Reviewed phases**: 1, 2, 3
- **Date**: 2026-11-07
- **Verdict**: NEEDS ATTENTION
- **Findings**: 0 critical, 2 warnings, 4 observations

The phases were reviewed as committed at `3579f15`, `06c9213` and `cd05f24`, which PR #31 squash-merged as `6c1715f`. Files were read at `main` (`479bef8`), which includes the later attach-sighting-photo review fixes (`b1b5a90`).

Automated criteria, run on `main`:
- build: 0 errors;
- architecture tests: 44/44;
- unit tests: 390/390;
- integration tests: 150/150;
- frontend tests: 858/858;
- `npm run build`: 529.87 kB.

Manual checks 3.3 and 3.4 were confirmed by the user, as recorded in `50bae00`.

The unplanned files are `PigeonWatch/CLAUDE.md` (a docs sync), `IntegrationTests/TestPhotos.cs` (a `Dimensions` helper) and `UnitTests/SightingServiceTests.cs` (the new constructor dependency). All three follow directly from the plan, so Scope Discipline is PASS.

Two observations from the review were left out:
- `ReadPhotoAsync` reads the full photo into memory without a cap. Our own normalized JPEG of at most 1600 px bounds it.
- A corrupt stored photo gives a 500. The input is always our own JPEG, and the behaviour is documented.

## Verdicts

| Dimension | Verdict |
|-----------|---------|
| Plan Adherence | WARNING |
| Scope Discipline | PASS |
| Safety & Quality | WARNING |
| Architecture | PASS |
| Pattern Consistency | WARNING |
| Success Criteria | PASS |

## Findings

### F1 — Row thumbnails report expected statuses

- **Severity**: ⚠️ WARNING
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Pattern Consistency
- **Location**: PigeonWatch/Frontend/src/app/features/sightings/shared/sighting-row/sighting-row.ts:110-112
- **Detail**:
  - `thumbnailOf` reports every failure under `sighting.row.photo`, including 400, 401 and 429, which Frontend CLAUDE.md ("Reporting errors") treats as expected.
  - The detail page reports its photo stream only for a 404 (a missing blob) or a status that is not expected, a fix from attach-sighting-photo F6.
  - An expired session on a list page uses up one of the 20 reports a page load may send.
- **Fix**: Use the detail page's rule (report a 404 or a status that is not expected). Add row specs for 401 and 429 not reported and 404 reported, and update the "Row thumbnails" docs.
- **Decision**: FIXED. A new `isReportablePhotoFailure` in `sighting-errors.ts` reports a failure only when it is a 404 or not an expected status. The row and the detail page both use it, replacing the detail page's local helper. Row specs were red, then green: 400, 401 and 429 are silent, and 404 is reported. Frontend passes 862/862. `Frontend/CLAUDE.md` "Detail photo" and "Row thumbnails" are updated.

### F2 — Thumbnail provider decodes the full photo at full size

- **Severity**: ⚠️ WARNING
- **Impact**: 🔎 MEDIUM — real tradeoff; pause to reason through it
- **Dimension**: Safety & Quality
- **Location**: PigeonWatch/Api/BusinessLogic/Services/PhotoThumbnailProvider.cs:17, 31
- **Detail**:
  - `SKBitmap.Decode(codec)` decodes the normalized JPEG at full size: about 10 MB for a 1600 px square, and `SKImage.FromBitmap` copies it, so the peak is about 20 MB for a 160 px output.
  - This runs on every upload and on every create-on-miss. The first visit to a list of older photos can start many of these at once on the F1 plan.
  - `PhotoNormalizationProvider` already decodes at an eighth-step scale. For a JPEG, 1/8 of 1600 is 200, which is still at least 160.
- **Fix**: Decode at the smallest eighth-step scale whose shorter edge is still at least `PhotoThumbnailSize`, reusing the normalization provider's scaled decode, with full size as the fallback.
  - Strength: removes the memory peak at its source and reuses a pattern this repo has already tested.
  - Tradeoff: the crop now works on a downscaled source; the existing crop tests must still pass on the new path.
  - Confidence: HIGH — the same SkiaSharp API already runs in `PhotoNormalizationProvider`.
  - Blind spot: the memory use on the F1 plan has not been measured.
- **Decision**: FIXED. `PhotoThumbnailProvider` decodes through a new `DecodeSize`: the smallest eighth-step scale whose shorter edge still covers `min(PhotoThumbnailSize, shorter edge)`, falling back to full size on `InvalidScale`. This mirrors the normalization provider; for a 1600×1200 photo that is 400×300 instead of the full 1600×1200. `SetImmutable` stops `SKImage.FromBitmap` from copying the pixels. The scaled decode is mirrored rather than shared, because a shared helper would need its own interface and registration. `BusinessLogic` now exposes its internals to `PigeonWatch.UnitTests` so `DecodeSize` can be tested. A new theory (1600×1200, 1200×1600, 1200×400, and 150×50 staying at full size) was red on the three scalable sizes, then green. A new 1600×1200 crop test was added, and the existing crop tests pass on the scaled path. API results: unit 395/395, architecture 44/44, integration 150/150. `Api/CLAUDE.md` "Thumbnails" is updated.

### F3 — The save catch in create-on-miss is broader than storage

- **Severity**: 💡 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Safety & Quality
- **Location**: PigeonWatch/Api/BusinessLogic/Services/SightingService.cs:264
- **Detail**:
  - `SaveCreatedThumbnailAsync` catches any exception except the caller's cancellation.
  - So a programming fault, such as the `ArgumentException` that `LocalFolder` throws for a bad name, is logged at Error and the bytes are still served.
  - Nothing is hidden, because the cause is logged, but a bug reads like a storage outage.
- **Fix**: Narrow the catch to `PhotoStorageUnavailableException`, so other faults fail the request.
- **Decision**: FIXED. `SaveCreatedThumbnailAsync` now catches only `PhotoStorageUnavailableException`, so any other fault from the save fails the request; a caller cancellation already propagates. A new unit test, where an `ArgumentException` from the save propagates and nothing is logged, was red, then green. The existing Error-log test for a storage outage still passes. API results: unit 396/396, architecture 44/44, integration 150/150. `Api/CLAUDE.md` "Thumbnails" is updated.

### F4 — A partial local thumbnail is never replaced

- **Severity**: 💡 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Safety & Quality
- **Location**: PigeonWatch/Api/Data/Storage/LocalFolderSightingPhotoStore.cs (SaveAsync); PigeonWatch/Api/CLAUDE.md "Accepted limitations"
- **Detail**:
  - In Development, a cancelled create-on-miss leaves a zero-byte or partial `-thumb.jpg`, which is served from then on and never created again.
  - Two concurrent first requests can also hit `FileShare.None` and log an `IOException`.
  - Azure overwrites safely, so this only affects Development.
- **Fix**: Add "a partial local thumbnail is served and never replaced" to the accepted limitations in `Api/CLAUDE.md`.
- **Decision**: FIXED. The "Accepted limitations" bullet in `Api/CLAUDE.md` now records two Development-only cases: a partial local `-thumb.jpg` is served and never created again (remove it by hand), and concurrent first requests can collide on the exclusive file lock.

### F5 — Two regression tests are missing

- **Severity**: 💡 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Safety & Quality
- **Location**: PigeonWatch/Api/UnitTests/SightingServicePhotoTests.cs
- **Detail**:
  - The code is correct today: the cleanup uses `CancellationToken.None`.
  - But no test pins that a cancelled create still deletes both blobs.
  - No test pins that a storage outage while reading a stored thumbnail surfaces as `PhotoStorageUnavailableException` (503).
- **Fix**: Add the two unit tests.
- **Decision**: FIXED. Two regression tests were added to `SightingServicePhotoTests`. One checks that a create cancelled during the thumbnail save still deletes both blobs with an uncancelled token. The other checks that a storage outage while reading the stored thumbnail is rethrown as `PhotoStorageUnavailableException`, with no create and no save. Both guard existing behaviour, so they passed on the first run, with no red step. A mutation check (switching the cleanup to the request token) made the cancellation test fail, and the change was reverted. Unit 398/398.

### F6 — The plan names the pending kitchen-sink cell differently

- **Severity**: 💡 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Plan Adherence
- **Location**: context/changes/sighting-row-photo-thumbnail/plan.md:205
- **Detail**: The plan says `list-thumbnails-pending`. The code (`cell-definitions.ts:527`), the spec and Frontend CLAUDE.md all use `list-thumbnails-loading`.
- **Fix**: Note the name in an addendum to the Notes in `change.md`. The plan text stays as approved.
- **Decision**: FIXED. A note in `change.md` records the cell name `list-thumbnails-loading`. The plan text is unchanged.
