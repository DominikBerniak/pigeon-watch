<!-- IMPL-REVIEW-REPORT -->
# Implementation Review: Edit Own Sighting With Engagement Lock

- **Plan**: context/changes/edit-own-sighting-with-lock/plan.md
- **Scope**: Full plan
- **Reviewed phases**: 1, 2, 3, 4
- **Date**: 2026-11-15
- **Verdict**: NEEDS ATTENTION
- **Findings**: 0 critical, 3 warnings, 7 observations

## Verdicts

| Dimension | Verdict |
|-----------|---------|
| Plan Adherence | PASS |
| Scope Discipline | PASS |
| Safety & Quality | WARNING |
| Architecture | PASS |
| Pattern Consistency | WARNING |
| Success Criteria | WARNING |

Plan drift check: every planned change is present with its contract, and nothing is missing. The approved Phase 3 deviations are documented in Frontend/CLAUDE.md: no location on the edit page, a disabled description instead of readonly, and the extra `DescriptionRequired` code. The extras are benign: unit-level logging tests, extra fixture requests, `[MaxLength]` on Name, a zero-length photo part treated as no photo, and fixture updates for `editPermissions: null`.

Automated criteria (re-run 2026-11-15):
- `dotnet build`: 0 errors.
- Tests: architecture 44/44, unit 652/652, integration 208/208.
- No pending EF model changes.
- `npm run build`: 546.26 kB initial.
- `npm test`: 1182/1182.
- Playwright: `sighting-edit.spec.ts` 2/2 and the full suite 9/9 at 90ced99. The E2E break-check went red on a disabled lock.

## Findings

### F1 — Keeping or removing the photo can write a stale blob name

- **Severity**: ⚠️ WARNING
- **Impact**: 🔎 MEDIUM — real tradeoff; pause to reason through it
- **Dimension**: Safety & Quality
- **Location**: PigeonWatch/Api/BusinessLogic/Services/SightingService.cs:109-122, PigeonWatch/Api/Data/Repositories/SightingRepository.cs:167
- **Detail**: The service reads `currentBlobName` in a separate query. The repository then writes `entity.PhotoBlobName = photoBlobName` unconditionally, including on a keep. A double submit or two tabs can break the reference:
  1. Request A replaces photo X with Y, commits, and deletes X.
  2. Request B is a keep that read X before A committed, and it writes X back.
  3. The row now points at a deleted blob, so the photo returns 404, and Y is orphaned.

  The remove path likewise deletes a stale X and orphans Y. "Last save wins" was accepted for the fields, not for a dangling photo reference.
- **Fix**: Pass a photo intent to `SightingRepository.UpdateAsync`: Keep, Replace(newName) or Remove. Keep leaves `PhotoBlobName` untouched. The repository returns the previous blob name read from the tracked entity, and the service deletes exactly that.
  - Strength: Removes the separate `GetPhotoBlobNameAsync` read and the race window. The blob that gets deleted is always the one the saved row replaced.
  - Tradeoff: The repository signature changes, and the unit and photo tests need updating.
  - Confidence: HIGH — the tracked-entity pattern already exists in `SightingCommentRepository.UpdateTextAsync`.
  - Blind spot: Two concurrent replaces still race on the final value, but each deletes only the blob it replaced.
- **Decision**: FIXED — SightingPhotoChange (Keep/Replace/Remove) + SightingUpdateOutcome; a keep never writes PHOTO_BLOB_NAME, the replaced blob is read from the tracked entity; break-checked

### F2 — A failed reload after a stale save throws away the user's edits

- **Severity**: ⚠️ WARNING
- **Impact**: 🔎 MEDIUM — real tradeoff; pause to reason through it
- **Dimension**: Safety & Quality
- **Location**: PigeonWatch/Frontend/src/app/features/sightings/edit-sighting-page/edit-sighting-page.ts:276-278, 338-344, 409
- **Detail**: After `descriptionLocked` or `invalidStatusTransition`, the page reloads with `keepEdits: true`.
  - If that GET fails (status 0, 5xx, or a 404/403 race), `apply` sets the state to `error`/`notFound` and the form unmounts.
  - Retry then calls `reloads.next({ keepEdits: false })`, which resets the model to the server copy and loses everything the user typed.
- **Fix**: When a `keepEdits` reload fails, stay in the `editing` state with the current model. Show the failure in the form alert, reporting an unexpected status as usual, so the user can save or retry without losing input.
  - Strength: Keeps the "the reload keeps the other edits" promise in Frontend/CLAUDE.md even when the reload fails.
  - Tradeoff: A new branch in `apply` plus 2–3 spec cases.
  - Confidence: HIGH — the failure path is visible in the code.
  - Blind spot: How a 404 during the reload should read (the sighting was deleted) is a copy decision.
- **Decision**: FIXED — a failed keep-edits reload keeps the form and model and shows the error in the form alert (reported per the usual rules); break-checked

### F3 — No throttle on photo edits

- **Severity**: ⚠️ WARNING
- **Impact**: 🔬 HIGH — architectural stakes; think carefully before deciding
- **Dimension**: Safety & Quality
- **Location**: PigeonWatch/Api/WebApi/Controllers/SightingsController.cs:75-79, PigeonWatch/Api/BusinessLogic/Services/SightingService.cs:127
- **Detail**: Create is bounded by the 60 s submission cooldown. `PUT sightings/{id}` has no cooldown and no `[EnableRateLimiting]`. Each PUT with a photo runs a full SkiaSharp decode (up to about 64 MB for a 16 MP PNG), writes two blobs and deletes two. One authenticated owner can loop this on the F1 plan. The plan accepted "no cooldown on edits" without weighing that it also removes the only throttle on photo processing.
- **Fix A ⭐ Recommended**: Add a per-user rate-limit policy for `PUT sightings/{id}`, in the same style as `clientErrors`. Example: 10 per minute per user, 429 with `Retry-After`.
  - Strength: Bounds CPU and storage churn with the existing limiter infrastructure, and needs no business-rule change.
  - Tradeoff: A new policy, config key and integration test. The SPA needs a 429 message on the edit page.
  - Confidence: MED — the limit value is a guess, and the privacy sweep must not trip it (it sends several PUTs).
  - Blind spot: The sweep's PUT count against the chosen limit has not been checked.
- **Fix B**: Record it as an accepted risk in Api/CLAUDE.md and the test plan.
  - Strength: No code change. The app has a small trusted community, and the owner can only hurt their own sighting.
  - Tradeoff: The resource exposure stays on a cost-capped plan.
  - Confidence: HIGH — documentation only.
  - Blind spot: Abuse by a compromised account.
- **Decision**: FIXED via Fix A — per-user `sightingEdits` policy on PUT sightings/{id} (RateLimiting:SightingEditPermitLimit, default 10/min, 429 + Retry-After; Retry-After now set on every limiter rejection); SPA maps 429 to silent `tooManyEdits`; break-checked

### F4 — Repository WHERE uses the stored reporter id, not the caller's

- **Severity**: 💡 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Safety & Quality
- **Location**: PigeonWatch/Api/BusinessLogic/Services/SightingService.cs:116, 136
- **Detail**: `UpdateAsync` is called with `current.ReporterId`. The service checks `current.ReporterId != trimmed.UserId` first, so the two values are equal today. Passing the stored id means the WHERE clause no longer enforces caller ownership on its own.
- **Fix**: Pass `trimmed.UserId` as `reporterId` in both `UpdateAsync` calls.
- **Decision**: FIXED — UpdateAsync receives the caller id (trimmed.UserId)

### F5 — Extra database round trips on create and update

- **Severity**: 💡 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Safety & Quality
- **Location**: PigeonWatch/Api/WebApi/Controllers/SightingsController.cs:69, PigeonWatch/Api/BusinessLogic/Services/SightingService.cs:289-294
- **Detail**: Create runs the engagement query for a sighting that cannot be engaged yet. An update that changes the description runs the engagement query twice: once for the lock check, and again in `UpdatedAsync`.
- **Fix**: On create, build the permissions as `CanEditDescription = true` with the transitions, and skip the query. On update, reuse the engagement result already computed.
- **Decision**: FIXED — create returns permissions without the engagement query (SightingCreateResult.Permissions); update reuses the lock-check result

### F6 — A stale reload fetches the thumbnail again

- **Severity**: 💡 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Safety & Quality
- **Location**: PigeonWatch/Frontend/src/app/features/sightings/edit-sighting-page/edit-sighting-page.ts:256-261, 364-365
- **Detail**: Every reload that keeps the edits calls `shownSightings.next(sighting)`. That revokes the object URL, shows the loading state and requests the thumbnail again, even when the photo did not change.
- **Fix**: Add `distinctUntilChanged` on `(id, hasPhoto)` before the thumbnail `switchMap`.
- **Decision**: FIXED — distinctUntilChanged on (id, hasPhoto) before the thumbnail switchMap

### F7 — The edit page reaches into the photo picker's DOM

- **Severity**: 💡 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Pattern Consistency
- **Location**: PigeonWatch/Frontend/src/app/features/sightings/edit-sighting-page/edit-sighting-page.ts:286-290
- **Detail**: `querySelector('app-photo-picker button')` focuses the picker's internal button. It is the only `querySelector` under `features/`, and `PhotoPicker` already holds a `chooseButton` `viewChild`.
- **Fix**: Add a public `focus()` to `PhotoPicker`, and call it through `viewChild(PhotoPicker)`.
- **Decision**: FIXED — PhotoPicker.focus() called through viewChild(PhotoPicker)

### F8 — MapPhoto duplicated across request mappers

- **Severity**: 💡 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Pattern Consistency
- **Location**: PigeonWatch/Api/WebApi/Mappers/UpdateSightingRequestMapper.cs:23-29
- **Detail**: This is identical to `CreateSightingRequestMapper.MapPhoto`. Two copies drift when the photo binding changes.
- **Fix**: Leave it. Promote it to a shared `ISightingPhotoPartMapper` only if a third caller appears.
- **Decision**: ACCEPTED — left as is; promote to a shared mapper only when a third caller appears

### F9 — S-09 status disagrees across docs

- **Severity**: 💡 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Success Criteria
- **Location**: PigeonWatch/CLAUDE.md:16, context/foundation/roadmap.md (S-09)
- **Detail**: `PigeonWatch/CLAUDE.md` lists S-09 as built, the roadmap says `in-progress`, and `change.md` says implemented. `/10x-archive` flips the roadmap to `done`. The test-plan freshness ledger (§8) was not touched.
- **Fix**: No action now. `/10x-archive` reconciles the roadmap. Update the §8 ledger at archive time if the test plan expects it.
- **Decision**: FIXED — roadmap S-09 flipped to done (table, Status line, readiness row; updated 2026-11-15) and a test-plan §8 ledger entry added

### F10 — Manual criterion 3.5 describes the superseded location text

- **Severity**: 💡 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Success Criteria
- **Location**: context/changes/edit-own-sighting-with-lock/plan.md (Phase 3 Manual Verification, Progress 3.5)
- **Detail**: The plan still says "Edit page shows location as text with no map tiles", and "Desired End State" still lists a read-only location. The user changed this during Phase 3 to no location at all. 3.5 was verified against the new behaviour, but the plan text was not updated, because phase blocks are read-only during implementation.
- **Fix**: Add a short "Implementation notes" addendum to the plan recording the two Phase 3 UI decisions (no location; disabled description).
- **Decision**: FIXED — Implementation Notes addendum added to plan.md
