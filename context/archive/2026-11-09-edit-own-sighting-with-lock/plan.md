# Edit Own Sighting With Engagement Lock Implementation Plan

## Overview

Roadmap S-09 / PRD FR-006. The owner of a sighting can edit it from a new edit page. They can change the name, behaviour, criticality, the description (until another user engages), the status (along the strict PRD workflow) and the photo (add, replace or remove). The location is fixed from the moment the sighting is reported and is never editable.

## Current State Analysis

- **No update path exists.** `SightingsController` has Create, Get, GetPhoto, GetPhotoThumbnail, Nearby, List and Mine (`PigeonWatch/Api/WebApi/Controllers/SightingsController.cs:41-158`). `ISightingRepository` has no update method (`PigeonWatch/Api/Data/Repositories/Interfaces/ISightingRepository.cs:5-14`), and `SightingsApi` on the SPA has no update call (`PigeonWatch/Frontend/src/app/features/sightings/sightings-api.ts:21-119`).
- **The transition graph is encoded and unit-tested, but nothing calls `IsTransitionAllowed`** (`PigeonWatch/Api/BusinessLogic/Services/SightingStatusTransitionProvider.cs:7-19`, :42-47). Api/CLAUDE.md and PigeonWatch/CLAUDE.md both state "no endpoint changes a status yet".
- **The engagement signals already exist:**
  - `SIGHTING_COMMENT` rows carry `AUTHOR_ID` and `DELETED_AT`, and the owner may comment on their own sighting.
  - `SIGHTING_THREAD` rows are created only by a non-owner; the owner gets 400 `OwnSightingContact` (`PigeonWatch/Api/BusinessLogic/Services/SightingThreadService.cs:21-22`). There is no thread delete.
  - Both tables are indexed on `SIGHTING_ID` (`PigeonWatch/Api/Data/PigeonWatchDbContext.cs:60,76`).
- **`SightingModel` exposes ownership only as `isMine`.** It is also the item type of the nearby, list and mine responses (`PigeonWatch/Api/WebApi/ViewModelCreators/SightingViewModelCreator.cs:8-40`), so per-sighting flags that need a query must not be computed for list items.
- **Create is multipart, with a photo pipeline to reuse:** normalise, create the thumbnail, store both blobs, insert, and delete the blobs on failure (`PigeonWatch/Api/BusinessLogic/Services/SightingService.cs:26-89`). `ISightingPhotoStore.DeleteAsync` exists.
- **The endpoint coverage registry fails for any unregistered route** (`PigeonWatch/Api/IntegrationTests/EndpointFixtureRegistry.cs:32-60`). The privacy-sweep world already has Bob's thread on Alice's sighting, so that sighting's description is locked.

## Desired End State

- **`PUT sightings/{id:guid}`** (multipart, `[Authorize]`) updates name, behaviour, criticality, description, status and photo for the owner and returns 200 with `SightingModel`.
  - The request has no location fields. Forged `latitude`/`longitude` parts are ignored, so the stored location never changes.
  - Non-owners get 403 `NotSightingOwner`; a missing sighting is a 404.
  - A status that differs from the current one must be an allowed transition, or the request gets 400 `InvalidStatusTransition`. An unchanged status is accepted.
  - A trimmed description that differs from the stored one while the sighting is engaged gets 400 `DescriptionLocked`.
  - **Engaged** means at least one comment with `DELETED_AT IS NULL` by a user other than the reporter, or at least one message thread.
- **`SightingModel.editPermissions`** is `{ canEditDescription, allowedNextStatuses }`. It is non-null only on the single-sighting responses (create, get and update) when the caller is the owner. It is `null` in the nearby, list and mine items and for everyone else.
- **SPA:**
  - The detail page shows an Edit button to the owner, which opens `/sightings/:id/edit`.
  - The edit page shows the location as read-only text.
  - The description becomes read-only with an explanation while engaged.
  - The status select offers the current status plus `allowedNextStatuses`.
  - The photo can be kept, replaced or removed.
  - Saving returns to the detail page.
- **Verification:** the full API test suites, the SPA build and specs, and a two-user Playwright test all pass:
  1. Bob comments, so Alice's description is read-only, but her status change still saves.
  2. Bob deletes the comment, so the description is editable again.

### Key Discoveries:

- `SightingCommentRepository.UpdateTextAsync` is the tracked-update pattern, with the ownership predicate in the WHERE (`PigeonWatch/Api/Data/Repositories/SightingCommentRepository.cs:53-68`).
- The order of checks and status mapping to mirror are in `SightingCommentsController.GetFailure` (`PigeonWatch/Api/WebApi/Controllers/SightingCommentsController.cs:87-107`) and the "Order of checks" in Api/CLAUDE.md.
- The 403 helper `ForbiddenForNonAuthor` mentions "author" (`PigeonWatch/Api/WebApi/Controllers/FailureLoggingControllerBase.cs:53`), so the owner case needs a sibling helper.
- `comment-errors.ts:10-53` maps a 403 to a reported `forbidden`. That is the template for the edit failures on the SPA.
- The two-user Playwright pattern (fresh contexts, freshly registered users) is in `PigeonWatch/Frontend/tests/e2e/sighting-messages.spec.ts:3-71`.

## What We're NOT Doing

- **Changing the location.** It is immutable after creation for everyone, which tightens FR-006. There is no location field in the update request and no location picker on the edit page.
- **Any status-changed timestamp column or transition history.** The audit `UPDATE_DATE` stays the only change time; FR-008 history stays parked.
- **A cooldown on edits, and a concurrency token or rowversion.** The engagement check runs in the same request just before the write. A comment created in the same instant may slip past (accepted race). The owner's two tabs: last save wins.
- **Making name, behaviour, criticality or the photo read-only after engagement.** Only the description is affected.
- **Changing whether people can comment on or message resolved sightings.**
- **Admin edits.** Those belong to S-10 and S-11.
- **Showing "edited" on the detail page.**

## Implementation Approach

Build API-first, test-first:
1. Read-side permissions, so the SPA can render the right edit state.
2. The write endpoint, with every rule enforced on the server.
3. The SPA edit page.
4. The two-user browser test and the PRD, roadmap and test-plan updates.

The server stays the single authority:
- The SPA never computes the lock or the transitions; it renders `editPermissions`.
- A stale form, for example one opened before Bob commented, is caught by the API's `DescriptionLocked` or `InvalidStatusTransition`. The SPA then reloads the sighting.

## Critical Implementation Details

- **Order of checks in the update** (a rejected request writes nothing and touches no storage):
  1. Field validation: criticality defined, status defined, description required after trimming, and `PhotoConflict` when a new photo and `removePhoto=true` arrive together.
  2. 404 `SightingNotFound`.
  3. 403 `NotSightingOwner`.
  4. `InvalidStatusTransition`.
  5. `DescriptionLocked`.
  6. Photo normalisation (`PhotoTooLarge`, `PhotoUnsupportedType`, `PhotoUnreadable`).
  7. Store the new blobs.
  8. Tracked update.
  9. Only after the row is saved, delete the old photo and thumbnail blobs. A failed delete is logged at Error with its cause and does not fail the request. If the update fails, delete the newly stored blobs, as create does.
- **Description comparison** uses the trimmed request value against the stored value. Re-sending the unchanged description of an engaged sighting is accepted. This is what lets a status-only save from a locked form succeed.

## Phase 1: API — edit permissions on the sighting response

### Overview

Compute whether a sighting is engaged and which statuses can follow, and return both to the owner on single-sighting responses. Write the failing tests first.

### Changes Required:

#### 1. Engagement query

**File**: `PigeonWatch/Api/Data/Repositories/Interfaces/ISightingRepository.cs`, `PigeonWatch/Api/Data/Repositories/SightingRepository.cs`

**Intent**: Answer "has anyone other than the reporter engaged with this sighting" in one round trip, using the existing `SIGHTING_ID` indexes.

**Contract**: `Task<bool> HasOtherUserEngagementAsync(Guid sightingId, Guid reporterId, CancellationToken cancellationToken = default)`. It returns true when a `SightingComments` row exists with that sighting, `AuthorId != reporterId` and `DeletedAt == null`, or any `SightingThreads` row exists for the sighting.

#### 2. Permissions business object and service method

**File**: `PigeonWatch/Api/BusinessObjects/SightingEditPermissions.cs` (new), `PigeonWatch/Api/BusinessLogic/Services/SightingService.cs`, `PigeonWatch/Api/BusinessLogic/Services/Interfaces/ISightingService.cs`

**Intent**: Give the owner the editable state of their sighting, and nobody else.

**Contract**: `record SightingEditPermissions(bool CanEditDescription, IReadOnlyList<SightingStatus> AllowedNextStatuses)`. `Task<SightingEditPermissions?> GetEditPermissionsAsync(Sighting sighting, Guid userId, CancellationToken cancellationToken = default)`:
- returns null when `sighting.ReporterId != userId`;
- otherwise `CanEditDescription = !engaged`, and `AllowedNextStatuses` comes from `ISightingStatusTransitionProvider.GetAllowedNextStatuses`.

#### 3. Wire shape

**File**: `PigeonWatch/Api/WebApi/Models/SightingEditPermissionsModel.cs` (new), `PigeonWatch/Api/WebApi/Models/SightingModel.cs`, `PigeonWatch/Api/WebApi/ViewModelCreators/SightingViewModelCreator.cs` (+ interface), `PigeonWatch/Api/WebApi/Controllers/SightingsController.cs`

**Intent**: Carry the permissions only where they are cheap and meaningful.

**Contract**:
- `SightingModel.EditPermissions` is a nullable `SightingEditPermissionsModel { bool CanEditDescription; IReadOnlyList<int> AllowedNextStatuses }`.
- `Create(Sighting, Guid currentUserId, SightingEditPermissions? permissions)` fills it. List and page creation pass null.
- `Get` and `Create` call `GetEditPermissionsAsync`. A newly created sighting has no engagement.

#### 4. Tests (written first, red before the change)

**File**: `PigeonWatch/Api/UnitTests/SightingRepositoryTests.cs`, `PigeonWatch/Api/UnitTests/SightingServiceTests.cs`, `PigeonWatch/Api/IntegrationTests/SightingEditContractTests.cs` (new)

**Intent**: Pin the lock rule and the visibility of `editPermissions`.

**Contract**: The SQLite repository tests cover these cases:
- no engagement: false;
- the owner's own comment: false;
- Bob's live comment: true;
- Bob's soft-deleted comment only: false;
- any thread: true;
- a comment on another sighting: false.

Integration tests (Alice is the owner, Bob a participant, Carol an outsider):
- Alice's GET has `editPermissions` with `canEditDescription: true` and the transitions for Spotted (Caught, Not found).
- After Bob comments it is false; after Bob deletes the comment it is true again; after Bob starts a thread it is false.
- Bob's and Carol's GETs have `editPermissions: null`.
- The nearby, list and mine items have `editPermissions: null`.

### Success Criteria:

#### Automated Verification:

- API builds with the style rules: `dotnet build PigeonWatch/Api/PigeonWatchApi.slnx`
- Architecture tests pass: `dotnet test PigeonWatch/Api/ArchitectureTests/PigeonWatch.ArchitectureTests.csproj`
- Unit tests pass, including the new engagement repository cases: `dotnet test PigeonWatch/Api/UnitTests/PigeonWatch.UnitTests.csproj`
- Integration tests pass, including the `editPermissions` visibility tests in `SightingEditContractTests`: `dotnet test PigeonWatch/Api/IntegrationTests/PigeonWatch.IntegrationTests.csproj`

#### Manual Verification:

- In Swagger, `GET sightings/{id}` for your own sighting shows `editPermissions`, and another user's sighting shows `editPermissions: null`

**Implementation Note**: After completing this phase and all automated verification passes, pause here for manual confirmation from the human that the manual testing was successful before proceeding to the next phase.

---

## Phase 2: API — `PUT sightings/{id}`

### Overview

Add the owner-only update with the transition, description-lock and photo replace/remove rules. Register it with the coverage, ownership and privacy guards, and document it.

### Changes Required:

#### 1. Error codes and update business object

**File**: `PigeonWatch/Api/BusinessObjects/SightingErrorCodes.cs`, `PigeonWatch/Api/BusinessObjects/SightingUpdate.cs` (new), `PigeonWatch/Api/BusinessObjects/SightingUpdateResult.cs` (new)

**Intent**: Name the new failures and carry the update input without a location.

**Contract**:
- New codes: `SightingNotFound`, `NotSightingOwner`, `InvalidStatus`, `InvalidStatusTransition`, `DescriptionLocked`, `PhotoConflict`.
- `SightingUpdate { SightingId, UserId, Name?, Description, Behaviour, Status, Criticality, NewSightingPhoto? Photo, bool RemovePhoto }`.
- The result follows `SightingCreateResult` (`Succeeded`, `Errors`, `Sighting`, `Permissions`).

#### 2. Tracked update in the repository

**File**: `PigeonWatch/Api/Data/Repositories/Interfaces/ISightingRepository.cs`, `PigeonWatch/Api/Data/Repositories/SightingRepository.cs`

**Intent**: Write only the editable columns, scoped to the owner, following `SightingCommentRepository.UpdateTextAsync`.

**Contract**: `Task<Sighting?> UpdateAsync(Guid sightingId, Guid reporterId, SightingUpdate update, string? photoBlobName, CancellationToken cancellationToken = default)`.
- It sets name, description, behaviour, status, criticality and `PhotoBlobName`.
- It never touches latitude or longitude.
- It returns null when no row matches the id and reporter.

#### 3. Service rules

**File**: `PigeonWatch/Api/BusinessLogic/Services/SightingService.cs`, `PigeonWatch/Api/BusinessLogic/Services/Interfaces/ISightingService.cs`

**Intent**: Enforce the order of checks in "Critical Implementation Details" and reuse the create photo pipeline. Extract the shared normalise-and-store step instead of duplicating it.

**Contract**: `Task<SightingUpdateResult> UpdateAsync(SightingUpdate update, CancellationToken cancellationToken = default)`.
- An unchanged status skips the transition check.
- `removePhoto` on a sighting without a photo is a no-op.
- A new photo replaces the existing one, or is added when there was none.
- After a successful row update, the old photo and thumbnail blobs are deleted, and a failed delete is logged at Error with its cause.
- On a failed update, the new blobs are deleted.
- The result includes the recomputed permissions.

#### 4. Request model, mapper and action

**File**: `PigeonWatch/Api/WebApi/Models/UpdateSightingRequestModel.cs` (new), `PigeonWatch/Api/WebApi/Mappers/UpdateSightingRequestMapper.cs` (+ interface, new), `PigeonWatch/Api/WebApi/Controllers/SightingsController.cs`, `PigeonWatch/Api/WebApi/Controllers/FailureLoggingControllerBase.cs`, `PigeonWatch/Api/DependencyInjection/*`

**Intent**: Expose the update as multipart with the same limits as create.

**Contract**:
- `[HttpPut("{id:guid}")]`, `[Consumes("multipart/form-data")]` and the create action's size limits.
- The request model fields are `Name?`, `Description` (`[Required]`, `[MaxLength]`), `Behaviour`, `Status`, `Criticality` (`[BindRequired]`), `IFormFile? Photo` and `bool RemovePhoto`. There are no latitude or longitude properties.
- Status mapping:
  - `SightingNotFound`: 404 via `NotFoundForSighting`.
  - `NotSightingOwner`: 403 via a new `ForbiddenForNonOwner` helper, logged at Warning.
  - Every other code: 400 `ValidationProblemDetails`.
- `InvalidStatus`, `InvalidCriticality`, `InvalidStatusTransition`, `PhotoConflict` and the photo codes are added to `loggedErrorCodes`. `DescriptionLocked` is not logged, because it is an expected race outcome.
- Register the mapper in DependencyInjection.

#### 5. Guards and contract tests (written first)

**File**: `PigeonWatch/Api/UnitTests/SightingServiceTests.cs`, `PigeonWatch/Api/UnitTests/SightingsControllerTests.cs`, `PigeonWatch/Api/IntegrationTests/SightingEditContractTests.cs`, `PigeonWatch/Api/IntegrationTests/EndpointFixtureRegistry.cs`, `PigeonWatch/Api/IntegrationTests/OwnershipContractTests.cs`, `PigeonWatch/Api/IntegrationTests/ControllerFailureLoggingTests.cs`, `PigeonWatch/Api/IntegrationTests/TestUser.cs`

**Intent**: Prove every rule over the real pipeline and keep the cross-user guards complete.

**Contract**: The integration cases are:

| Case | Expected |
| --- | --- |
| Alice edits name, behaviour, criticality and description | 200; read back the new values with the location unchanged |
| Forged `latitude`/`longitude` parts | ignored |
| Spotted → Caught | 200 |
| Spotted → At the vet | 400 `InvalidStatusTransition`, nothing written |
| End state → anything | 400 |
| Same status | 200 |
| After Bob's comment: changed description | 400 `DescriptionLocked` |
| After Bob's comment: unchanged description with status Caught | 200 |
| After Bob deletes the comment: changed description | 200 |
| After a thread exists: changed description | 400 |
| Bob or Carol edits | 403, Alice's read-back unchanged |
| Unknown id | 404 |
| New photo | 200, the old blob names deleted from `InMemorySightingPhotoStore` |
| `removePhoto` | `hasPhoto: false`, blobs deleted |
| Photo plus `removePhoto` | 400 `PhotoConflict` |
| Undecodable photo | 400 `PhotoUnreadable`, the stored photo kept |
| Photo store unavailable | 503, row unchanged |

No response carries `reporterId`, audit fields or an email. `EndpointFixtureRegistry` gets `PUT sightings/{id:guid}`, sending the unchanged description, because the sweep sighting is engaged. `OwnershipContractTests` gets Bob's forged update of Alice's sighting. `ControllerFailureLoggingTests` asserts Warning on the 403.

#### 6. Docs

**File**: `PigeonWatch/Api/CLAUDE.md`, `PigeonWatch/CLAUDE.md`

**Intent**: Keep the surface and rules documentation true.

**Contract**:
- Add the endpoint to the surface lists, and the new codes, the engagement rule and `editPermissions` to "Sightings".
- Replace "No endpoint changes a status yet" with the update rules, and state that location is immutable.

### Success Criteria:

#### Automated Verification:

- API builds with the style rules: `dotnet build PigeonWatch/Api/PigeonWatchApi.slnx`
- Architecture tests pass (new mapper interface and registration): `dotnet test PigeonWatch/Api/ArchitectureTests/PigeonWatch.ArchitectureTests.csproj`
- Unit tests pass, including the update service and controller tests: `dotnet test PigeonWatch/Api/UnitTests/PigeonWatch.UnitTests.csproj`
- Integration tests pass, including the update contract, endpoint coverage, ownership, privacy sweep and failure-logging tests: `dotnet test PigeonWatch/Api/IntegrationTests/PigeonWatch.IntegrationTests.csproj`
- No pending EF model changes: `dotnet ef migrations has-pending-model-changes --project PigeonWatch/Api/Data/PigeonWatch.Data.csproj --startup-project PigeonWatch/Api/WebApi.Host/PigeonWatch.WebApi.Host.csproj`

#### Manual Verification:

- In Swagger against LocalDB, a status change Spotted → Caught succeeds, Spotted → At the vet is rejected, and replacing a photo leaves no old blob in `LocalPhotos`

**Implementation Note**: After completing this phase and all automated verification passes, pause here for manual confirmation from the human that the manual testing was successful before proceeding to the next phase.

---

## Phase 3: SPA — edit page

### Overview

Add the owner's Edit entry point and the edit page that renders the server's permissions and handles the update failures.

### Changes Required:

#### 1. Types, API client and error mapping

**File**: `PigeonWatch/Frontend/src/app/features/sightings/sighting.types.ts`, `.../sightings-api.ts`, `.../sighting-errors.ts` (+ their specs)

**Intent**: Model `editPermissions` and the update call, and map the new failures.

**Contract**:
- `Sighting.editPermissions: { canEditDescription: boolean; allowedNextStatuses: SightingStatus[] } | null`.
- `SightingsApi.update(id, SightingUpdate): Observable<Sighting>` sends multipart `PUT sightings/{id}` with `removePhoto`.
- Failure kinds:

| Kind | Reported? | Behaviour |
| --- | --- | --- |
| `forbidden` (403) | yes, client bug | — |
| `notFound` (404) | no | — |
| `descriptionLocked` | no | reloads the sighting |
| `invalidStatusTransition` | no | reloads the sighting |
| `photoConflict` | yes, client bug | — |

  The existing photo and generic kinds are reused.

#### 2. Edit page and route

**File**: `PigeonWatch/Frontend/src/app/features/sightings/edit-sighting-page/` (new: component, template, styles, specs), `PigeonWatch/Frontend/src/app/app.routes.ts`

**Intent**: A Signal Forms page in the report-form style that edits only what the server allows.

**Contract**:
- The route is `sightings/:id/edit` (`authGuard`, `layout: 'wide'`), declared before `sightings/:id`.
- The page loads through `SightingsApi.get`. When `editPermissions` is null (not the owner), it shows a "cannot edit" alert with a Back link.
- **Fields:**
  - Name, behaviour and criticality are editable.
  - The description is `readonly()` with a hint when `canEditDescription` is false.
  - Status options are the current status plus `allowedNextStatuses`.
  - The location is shown as read-only text (no map).
  - Photo: the current thumbnail with a Remove action, plus `app-photo-picker` for a new photo. The photo state is keep, replace or remove.
- Submit calls `update` and navigates to `/sightings/{id}`. Unexpected failures are reported under `sighting.edit`. Cancel goes back to the detail page.

#### 3. Detail page entry point

**File**: `PigeonWatch/Frontend/src/app/features/sightings/sighting-detail-page/sighting-detail-page.html`, `.ts`, spec

**Intent**: Let the owner reach the edit page.

**Contract**: an Edit link button in the `headerActions` slot, rendered only when `sighting.isMine`, pointing at `/sightings/{id}/edit`.

#### 4. Labels, kitchen sink, docs

**File**: `PigeonWatch/Api/BusinessObjects/Resources/UiLabels.resx`, `PigeonWatch/Frontend/src/app/dev/kitchen-sink/cell-definitions.ts`, `cell-stubs.ts`, `kitchen-sink.spec.ts`, `PigeonWatch/Frontend/CLAUDE.md`

**Intent**: No literal UI text. Each new page state is visible in the kitchen sink. The docs describe the page.

**Contract**:
- The `sighting.edit.*` keys cover the title, the save and cancel buttons, the locked-description hint, the read-only location label, the remove-photo button, the cannot-edit alert and each error message.
- The detail page gets the edit button label.
- The kitchen-sink cells are: edit page editable, description locked, and not owner.

### Success Criteria:

#### Automated Verification:

- SPA production build passes within the bundle budget: `npm run build` (in `PigeonWatch/Frontend`)
- SPA unit tests pass, including the edit page, detail page, API client, error mapping and kitchen-sink specs: `npm test -- --watch=false` (in `PigeonWatch/Frontend`)
- API still builds after the resx change: `dotnet build PigeonWatch/Api/PigeonWatchApi.slnx`

#### Manual Verification:

- As the owner, open your sighting, click Edit, change the criticality and the status to Caught, and save; the detail page shows the new values
- The edit page shows the location as text with no map tiles loaded
- After another user comments, the description field is read-only with the hint, while the status still saves
- Replacing and removing the photo is reflected on the detail page and on list thumbnails

**Implementation Note**: After completing this phase and all automated verification passes, pause here for manual confirmation from the human that the manual testing was successful before proceeding to the next phase.

---

## Phase 4: E2E and product docs

### Overview

Keep a browser-level test of the two-user lock flow, and bring the PRD, roadmap and test plan in line with the decisions.

### Changes Required:

#### 1. Two-user Playwright spec

**File**: `PigeonWatch/Frontend/tests/e2e/sighting-edit.spec.ts` (new)

**Intent**: Reproduce the lock in the running app, following the `sighting-messages.spec.ts` pattern.

**Contract**:
1. Alice reports a sighting, edits its description and saves.
2. Bob comments.
3. Alice's edit page shows the description read-only and saves a status change to Caught.
4. Bob deletes his comment.
5. Alice's description is editable again.
6. Bob's view of the sighting has no Edit button.

#### 2. Product docs

**File**: `context/foundation/prd.md`, `context/foundation/roadmap.md`, `context/foundation/test-plan.md`

**Intent**: Record the decisions made during planning.

**Contract**:
- **FR-006:** the location never changes after creation. The description is read-only while a visible comment from another user or any message thread exists. Other fields, the status (strict workflow) and the photo stay editable.
- **Roadmap:** S-09 outcome and unknowns wording.
- **Test plan:** close the "no sighting edit endpoint" accepted gap (`test-plan.md:217`), and note that the risk #3 modify gate now covers `PUT sightings/{id}` (`test-plan.md:63`).

### Success Criteria:

#### Automated Verification:

- The new edit spec passes: `npx playwright test tests/e2e/sighting-edit.spec.ts` (in `PigeonWatch/Frontend`)
- The full Playwright suite still passes, including the `sighting-list.spec.ts` resolved-status filter now that resolved sightings can exist: `npx playwright test` (in `PigeonWatch/Frontend`)

#### Manual Verification:

- The PRD FR-006, roadmap S-09 and test-plan wording match the decisions in this plan

**Implementation Note**: After completing this phase and all automated verification passes, pause here for manual confirmation from the human that the manual testing was successful before proceeding to the next phase.

---

## Testing Strategy

### Unit Tests:

- Engagement query cases: owner comment, live and deleted non-owner comments, thread, other sighting.
- Service order of checks, with nothing written on rejection.
- Same-status no-op, the end-state transitions, and the description comparison after trimming.
- Photo replace and remove blob handling, including a failed old-blob delete being logged and not failing the request.
- SPA: the error mapping kinds, the edit page's read-only description, its status options and photo state, and the detail page's Edit visibility.

### Integration Tests:

- `SightingEditContractTests` with Alice, Bob and Carol, plus the coverage, ownership, privacy-sweep and failure-logging guards.

### Manual Testing Steps:

1. Report a sighting, edit every editable field, and confirm the location is unchanged.
2. From a second account, comment. The owner's description becomes read-only, and a status change still saves.
3. Delete that comment. The description is editable again.
4. Message the owner from the second account. The description is read-only for good.
5. Move a sighting to Released. It leaves the nearby map and the default list, and the edit page offers no further status.

## Performance Considerations

- The engagement check is two indexed existence subqueries, run only for single-sighting responses to the owner and during an update. Lists are unaffected.

## Migration Notes

- No migration: no new columns. Existing sightings become editable immediately; their engagement is computed from existing comment and thread rows.

## References

- Related research: `context/changes/edit-own-sighting-with-lock/research.md`
- Tracked update with ownership predicate: `PigeonWatch/Api/Data/Repositories/SightingCommentRepository.cs:53-68`
- Failure mapping precedent: `PigeonWatch/Api/WebApi/Controllers/SightingCommentsController.cs:87-107`
- Photo pipeline: `PigeonWatch/Api/BusinessLogic/Services/SightingService.cs:26-89`
- Two-user E2E pattern: `PigeonWatch/Frontend/tests/e2e/sighting-messages.spec.ts:3-71`

## Implementation Notes

Decisions taken during implementation that supersede the phase text above. The phase blocks and the Progress titles stay as written.

- **Phase 3, location (user decision):** the edit page shows no location at all, neither as text nor as a map. The `sighting.edit.locationLabel` key was dropped. "Desired End State" ("shows the location as read-only text") and manual criterion 3.5 ("shows location as text") were verified as "no location and no map tiles".
- **Phase 3, locked description (user decision):** a locked description is `disabled()` (greyed out, cannot be focused) instead of `readonly()`. The locked form still sends the stored description, so a status-only save works. The E2E spec asserts `toBeDisabled()`.
- **Implementation review (`reviews/impl-review.md`):** the photo update passes a Keep/Replace/Remove intent to the repository, and it deletes exactly the blob the saved row replaced. `PUT sightings/{id}` gets a per-user rate limit (429). Smaller fixes are recorded in the review report.

## Progress

> Convention: `- [ ]` pending, `- [x]` done. Append ` — <commit sha>` when a step lands. Do not rename step titles. See `references/progress-format.md`.

### Phase 1: API — edit permissions on the sighting response

#### Automated

- [x] 1.1 API builds with the style rules — 172968d
- [x] 1.2 Architecture tests pass — 172968d
- [x] 1.3 Unit tests pass, including the new engagement repository cases — 172968d
- [x] 1.4 Integration tests pass, including the editPermissions visibility tests in SightingEditContractTests — 172968d

#### Manual

- [x] 1.5 Swagger shows editPermissions for the owner and null for another user — 172968d

### Phase 2: API — PUT sightings/{id}

#### Automated

- [x] 2.1 API builds with the style rules — 172968d
- [x] 2.2 Architecture tests pass (new mapper interface and registration) — 172968d
- [x] 2.3 Unit tests pass, including the update service and controller tests — 172968d
- [x] 2.4 Integration tests pass, including update contract, coverage, ownership, privacy sweep and failure logging — 172968d
- [x] 2.5 No pending EF model changes — 172968d

#### Manual

- [x] 2.6 Swagger: Spotted to Caught succeeds, Spotted to At the vet rejected, replaced photo leaves no old blob — 172968d

### Phase 3: SPA — edit page

#### Automated

- [x] 3.1 SPA production build passes within the bundle budget — 172968d
- [x] 3.2 SPA unit tests pass, including edit page, detail page, API client, error mapping and kitchen-sink specs — 172968d
- [x] 3.3 API still builds after the resx change — 172968d

#### Manual

- [x] 3.4 Owner edits criticality and status to Caught and sees the new values — 172968d
- [x] 3.5 Edit page shows location as text with no map tiles — 172968d
- [x] 3.6 After another user's comment the description is read-only while status still saves — 172968d
- [x] 3.7 Photo replace and remove reflected on detail page and list thumbnails — 172968d

### Phase 4: E2E and product docs

#### Automated

- [x] 4.1 The new edit spec passes — 172968d
- [x] 4.2 The full Playwright suite still passes — 172968d

#### Manual

- [x] 4.3 PRD FR-006, roadmap S-09 and test-plan wording match the plan decisions — 172968d
