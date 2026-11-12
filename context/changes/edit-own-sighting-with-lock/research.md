---
date: 2026-11-09T11:05:51+01:00
researcher: Dominik Berniak (with Claude Code)
git_commit: 419ef4faedd0630540bd9f7e166ad245ada6a6ed
branch: main
repository: 10xDevs (PigeonWatch)
topic: "S-09 edit-own-sighting-with-lock: what exists today for editing a sighting, changing its status and computing the engagement lock"
tags: [research, codebase, sightings, status-workflow, comments, threads, authorization, angular, aspnet-core]
status: complete
last_updated: 2026-11-09
last_updated_by: Dominik Berniak (with Claude Code)
---

# Research: S-09 edit own sighting with engagement lock

**Date**: 2026-11-09T11:05:51+01:00
**Researcher**: Dominik Berniak (with Claude Code)
**Git Commit**: 419ef4faedd0630540bd9f7e166ad245ada6a6ed
**Branch**: main
**Repository**: 10xDevs (PigeonWatch)

## Research Question

Roadmap S-09 / PRD FR-006 (`context/foundation/prd.md:80`, `context/foundation/roadmap.md:198-208`): an owner can edit their own sighting and change its status along the strict workflow. Once another user has commented on the sighting or contacted the reporter, location and description lock; status stays changeable. What does the codebase already provide, what must be added, and which decisions are still open?

## Summary

- **No edit or status-change path exists in the inspected API or SPA.** `SightingsController` has Create, Get, GetPhoto, GetPhotoThumbnail, Nearby, List and Mine (`PigeonWatch/Api/WebApi/Controllers/SightingsController.cs:41-158`). `ISightingRepository` has no update method (`Data/Repositories/Interfaces/ISightingRepository.cs:5-14`). `SightingsApi` on the SPA has no update method (`Frontend/src/app/features/sightings/sightings-api.ts:21-119`).
- **The transition graph is built, unit-tested and unused.** `SightingStatusTransitionProvider` encodes exactly the PRD table (`BusinessLogic/Services/SightingStatusTransitionProvider.cs:7-19`) and exposes `GetAllowedNextStatuses` and `IsTransitionAllowed`. No caller of `IsTransitionAllowed` was found in the API. Same-status is not an edge in the graph, so an edit that leaves the status unchanged needs explicit handling.
- **The lock is computable from existing tables with two existence checks.**
  - **Contact:** the owner cannot start a thread; that returns 400 `OwnSightingContact` (`BusinessLogic/Services/SightingThreadService.cs:21-22`). So any `SightingThreads` row for the sighting means a non-owner made contact. S-08 recorded this reading: `context/archive/2026-11-07-sighting-message-threads/plan-brief.md:61`.
  - **Comment:** owners can comment on their own sighting (`context/archive/2026-11-07-comment-on-sighting/plan.md:39`). The comment check must therefore be `AuthorId != ReporterId`. Both tables are indexed on `SIGHTING_ID` (`Data/PigeonWatchDbContext.cs:60,76`).
- **Ownership is exposed only as `isMine`** (`WebApi/Models/SightingModel.cs:27`, computed at `WebApi/ViewModelCreators/SightingViewModelCreator.cs:23`). The response has no lock flag and no allowed-next-statuses list. The SPA cannot derive the lock itself, so the API has to send it.
- **No concurrency token or status/edit timestamp exists on SIGHTING.** The only concurrency token is `UserAccount.ConcurrencyStamp` (`Data/PigeonWatchDbContext.cs:30`). `UPDATE_DATE` is bumped on every modification by the audit interceptor, which is why S-07 and S-08 added dedicated `EDITED_AT` and `LAST_MESSAGE_AT` columns.
- **Test-plan gates activate with S-09.** A new route must be registered in `EndpointFixtureRegistry` (`IntegrationTests/EndpointFixtureRegistry.cs:40-55`) and be `[Authorize]`. Risk #3 (cross-user modify) and the accepted "no sighting edit endpoint" gap are tied to S-09 (`context/foundation/test-plan.md:46,63,217`). One existing E2E assertion is known to break once statuses can change (`context/archive/2026-11-01-urgency-ranked-sighting-list/reviews/impl-review.md:135`).

## Detailed Findings

### Data model (API)

- `SightingEntity` fields are `ReporterId`, `Name?` (60), `Description` (1000), `Behaviour` (500), `Latitude`, `Longitude`, `Status`, `Criticality` and `PhotoBlobName?`. It also inherits the audit columns `CreateUser/CreateDate/UpdateUser/UpdateDate` (`Data/Entities/SightingEntity.cs:6-31`, `Data/Entities/AuditableEntity.cs:5-16`). Limits come from `BusinessObjects/SightingRules.cs` (Api/CLAUDE.md:54).
- The `Sighting` business object carries `ReporterId`, `ReporterDisplayName`, `CreatedAt` (from `CreateDate`) and `HasPhoto`. It has no updated or status-changed time (`BusinessObjects/Sighting.cs:3-15`, `Data/Mappers/SightingMapper.cs:8-39`).
- `SightingRepository.GetByIdAsync` uses AsNoTracking with `Include(Reporter)` (`Data/Repositories/SightingRepository.cs:25-33`).
- **Tracked-update pattern to reuse:** `SightingCommentRepository.UpdateTextAsync` puts the ownership predicate in the WHERE, mutates the row, saves, and returns null when no row matched (`Data/Repositories/SightingCommentRepository.cs:53-68`).
- **Status timestamps:** the PRD says each transition is timestamped (`prd.md:131`). Change-log history (FR-008) is nice-to-have, and full audit history is a Non-Goal (`prd.md:84-85`). Today only `CreatedAt` exists (`context/archive/2026-11-01-urgency-ranked-sighting-list/research.md:31`).

### Status workflow

- **Provider methods:** `GetAllowedNextStatuses` (:42), `IsTransitionAllowed` (:47), `IsResolved` (:52), `IsValidInitialStatus` (:57) and `GetCriticalityRank` (:62). The resolved set is Adopted, Released, PassedAway and NotFound (:21-27). It is registered scoped at `DependencyInjection/BusinessLogicServiceCollectionExtensions.cs:18`, and its tests are in `UnitTests/SightingStatusTransitionProviderTests.cs`.
- **Current consumer:** `SightingService` uses `IsValidInitialStatus` (`SightingService.cs:280`) and `IsResolved` (:147, :185).
- **SPA status data:** `SightingStatus` is 1-9 (`sighting.types.ts:4-16`). The client has the label helpers (:122-158) and the lists `initialSightingStatuses`, `filterableSightingStatuses` and `allSightingStatuses` (:166-195). It has no transition table.

### Engagement lock signals

- **Comments** (`Data/Entities/SightingCommentEntity.cs:6-22`): `SightingId`, `AuthorId`, `Text`, `EditedAt?` and `DeletedAt?`. Deletion is soft and the row is kept so that S-09 can see it (`context/archive/2026-11-07-comment-on-sighting/plan.md:5,40`). `SightingCommentService.CreateAsync` has no owner check, so the owner can comment.
- **Threads** (`Data/Entities/SightingThreadEntity.cs:3-20`): `SightingId` and `ParticipantId`, which is always the non-owner. There is a unique index on `(SIGHTING_ID, PARTICIPANT_ID)` (`PigeonWatchDbContext.cs:76`). A thread is created together with its first message via `StartOrAppendAsync` (`SightingThreadService.cs:24`).
- **Existing existence queries:** `SightingExistsAsync` (`SightingCommentRepository.cs:105-110`), `IsThreadVisibleToUserAsync` (`SightingThreadRepository.cs:50`) and `GetSightingOwnerAsync` (`SightingThreadRepository.cs:16-24`). None of them answers "engaged by someone other than the owner".
- **Inferred lock predicate:** `Comments.Any(c => c.SightingId == id && c.AuthorId != reporterId) || Threads.Any(t => t.SightingId == id)`. Whether soft-deleted comments count is an open decision.

### Authorization and error conventions

- **Identity and base helpers:** the user id comes from `ClaimTypes.NameIdentifier`, falling back to `UnauthorizedForInvalidIdentity()` (`SightingsController.cs:47-48`). `FailureLoggingControllerBase` provides these helpers (`WebApi/Controllers/FailureLoggingControllerBase.cs:9-82`):
  - `NotFoundForSighting` (:32), a 404 logged at Information.
  - `ForbiddenForNonAuthor` (:53), a 403 logged at Warning. Its message says "author".
  - `LogInvalidInput` (:27), Warning.
  - `GetValidationProblem` (:60-74), which adds `retryAfterSeconds` and `Retry-After`.
- **Precedents:**
  - **Comments:** validate, then exists (404), then authorship (403), then cooldown, then write; a rejected request writes nothing (Api/CLAUDE.md:68; `SightingCommentsController.cs:87-107`).
  - **Threads:** 404 for outsiders so that ids cannot be probed (`context/archive/2026-11-07-sighting-message-threads/plan-brief.md:29`).
  - **Sightings:** sightings are readable by every logged-in user (`test-plan.md:180`), so hiding a non-owner edit behind a 404 would not hide anything. This inference points to 403, as for comments, but nothing records it as a decision.
- **Error codes:** `SightingErrorCodes.cs:5-17` has no code for not-owner, invalid transition or locked field. `SightingCommentErrorCodes.SightingNotFound` exists (:8).
- **Request models:** they use DataAnnotations with `[MaxLength(SightingRules.*)]` and `[BindRequired]`, with status and criticality sent as int (`WebApi/Models/CreateSightingRequestModel.cs:8-34`). There is no FluentValidation.
- **Cooldown:** the sighting cooldown measures from SIGHTING `CreateDate` only (`SightingService.cs:40-48`). Whether edits are throttled is undecided.
- **Logging:** in the inspected `SightingService`, logging covers infrastructure faults only, at Error with the exception (`SightingService.cs:226-231`). Controller log assertions live in `IntegrationTests/ControllerFailureLoggingTests.cs:24-73`.

### SPA: detail page and report form

- **Routes:** `sightings/new` (`app.routes.ts:32-40`), `sightings/list` (:41-49) and `sightings/:id` (:50-58). There is no edit route.
- **Detail page:** state comes from `SightingsApi.get` through `combineLatest(paramMap, reloads)` (`sighting-detail-page.ts:234-249`).
  - The `headerActions` slot holds the Show map toggle (`sighting-detail-page.html:25-40`); the card `actions` slot holds the Back links (:2-10).
  - Today the only owner-dependent UI is that the contact button is hidden for the owner (html:82) and the thread lookup is skipped (ts:314-315).
  - Comments load inside `<app-sighting-comments>` (html:126), so the page itself cannot see other users' comments.
- **Report form** (`report-sighting-page.ts`):
  - It uses a Signal Forms model (54-62, 110, 121-139). Status options are `initialSightingStatuses` (114).
  - Submit calls `SightingsApi.create` with multipart (173-191; `sightings-api.ts:143-158`). Failures are mapped through `sightingFailure` (`sighting-errors.ts:34-54`) and reported as `api-failure` under `sighting.report`.
  - **Reuse needs adaptation:**
    - Status options must come from the allowed transitions.
    - Locked fields need `disabled()`/`readonly()` rules.
    - The labels are report-specific.
    - The location picker opens on the neutral Poland view and recentres only when it has no value (`shared/map/location-picker/location-picker.ts:46-47,67-75`), so a pre-filled pin would sit off-screen.
    - The photo picker has no notion of an existing server photo (`photo-picker.ts:34,49,72-86`).
- **Errors:** `comment-errors.ts:10-53` maps 403 to a reported `forbidden`, the template for owner-only failures. Errors render inline (`app-alert`, `app-error-reference`); no `MatSnackBar` usage was found in the inspected SPA sources.
- **Kitchen sink:** a new detail-page state needs a cell, a stub and a marker assertion (Frontend/CLAUDE.md:31).

### Tests

- **API integration:**
  - The factory is `PigeonWatchApiFactory(TimeProvider?)`, and `CreateUserAsync` returns a `TestUser` (`IntegrationTests/PigeonWatchApiFactory.cs:22,69`).
  - `TestUser` helpers: `CreateSightingAsync`, `CreateCommentAsync`, `StartThreadAsync` and `ReplyAsync` (`TestUser.cs:29,49,62,75`).
  - The Alice, Bob and Carol world plus `ManualTimeProvider` is in `SightingThreadContractTests.cs:10-26,52,559-575`. The forged-ownership shape is in `OwnershipContractTests.cs:45-58`.
  - The privacy-sweep world already seeds Bob's thread on Alice's sighting, so that sighting is locked.
- **Architecture:** actions return `Task<ActionResult<T>>` with WebApi models only (`ArchitectureTests/ControllerActionTests.cs:12,53`). Every service needs an `I<Name>` interface plus a DI registration. Async methods take a `CancellationToken`. Columns are UPPER_SNAKE_CASE (`DatabaseModelTests.cs:17-136`).
- **Unit:** `SightingServiceTests.cs:18-34` (NSubstitute, `ManualTimeProvider`), `SightingsControllerTests.cs`, and SQLite repository tests (`SightingRepositoryTests.cs:23,447`).
- **E2E:** the two-user pattern is in `Frontend/tests/e2e/sighting-messages.spec.ts:3-30,44-71`. It uses fresh contexts and registers fresh users, which also avoids cooldown leftovers. It fits "Bob comments or messages, then Alice's edit shows location and description locked".

## Code References

- `PigeonWatch/Api/BusinessLogic/Services/SightingStatusTransitionProvider.cs:7-19` - transition graph matching the PRD
- `PigeonWatch/Api/BusinessLogic/Services/SightingThreadService.cs:21-22` - owner cannot start a thread
- `PigeonWatch/Api/BusinessLogic/Services/SightingService.cs:26-89,276-290` - create flow and validation to mirror
- `PigeonWatch/Api/WebApi/Controllers/SightingsController.cs:27-68` - logged codes and create action
- `PigeonWatch/Api/WebApi/Controllers/FailureLoggingControllerBase.cs:32,53` - 404 and 403 helpers
- `PigeonWatch/Api/WebApi/Controllers/SightingCommentsController.cs:87-107` - failure-to-HTTP mapping precedent
- `PigeonWatch/Api/Data/Repositories/SightingCommentRepository.cs:53-68` - tracked update with ownership predicate
- `PigeonWatch/Api/Data/PigeonWatchDbContext.cs:30,36-46,60,76` - concurrency token (users only), SIGHTING mapping, engagement indexes
- `PigeonWatch/Api/WebApi/ViewModelCreators/SightingViewModelCreator.cs:23` - `IsMine`
- `PigeonWatch/Api/IntegrationTests/EndpointFixtureRegistry.cs:40-55` - route coverage registry
- `PigeonWatch/Frontend/src/app/features/sightings/report-sighting-page/report-sighting-page.ts:54-235` - form to adapt
- `PigeonWatch/Frontend/src/app/features/sightings/sighting-detail-page/sighting-detail-page.html:25-40,82` - owner UI hooks
- `PigeonWatch/Frontend/src/app/shared/map/location-picker/location-picker.ts:46-75` - initial centring gap
- `PigeonWatch/Frontend/tests/e2e/sighting-messages.spec.ts:3-71` - two-user E2E pattern

## Architecture Insights

- Server-computed flags rather than raw ids is the established privacy pattern (`isMine`, thread `isOwner`), and the reporter id never crosses the wire (PigeonWatch/CLAUDE.md). Lock state and allowed next statuses fit the same pattern as new fields on `SightingModel`.
- The order of checks in the two inspected mutating flows (comments, Api/CLAUDE.md:68, and threads, `sighting-message-threads/plan.md:24`) is: validate, then exists, then ownership, then cooldown, then write. A rejected request writes nothing.
- The audit `UPDATE_DATE` cannot double as a domain timestamp. Each slice that needed one added a dedicated column.

## Historical Context (from prior changes)

- `context/archive/2026-10-11-report-sighting-on-map/plan.md:37,94-98` - edit and status change deferred to S-09; the provider was built for S-06 and S-09 to reuse.
- `context/archive/2026-11-03-attach-sighting-photo/plan.md:48,71-73` - adding, replacing or removing a photo after creation is deferred to S-09. Blob cleanup on failure is logged at Error without masking the original failure.
- `context/archive/2026-11-07-comment-on-sighting/plan-brief.md:71` and `research.md:32,147` - whether deleted or owner comments trigger the lock was left to S-09.
- `context/archive/2026-11-07-sighting-message-threads/plan-brief.md:61` - "thread exists" means "contact made". Verdict: supported by `SightingThreadService.cs:21-22`.
- `context/archive/2026-11-07-sighting-message-threads/research.md:121` - messaging on resolved sightings matters once S-09 lands. No decision was recorded.
- `context/foundation/shape-notes.md:102` says "status remains freely editable". Verdict: superseded by the strict no-undo workflow (`prd.md:146`, `roadmap.md:206`).
- `context/foundation/roadmap.md:171` - resolved sightings appear only once S-09 lets owners change status. Test-plan risk #4 (open vs resolved separation, `test-plan.md:64`) becomes live.
- `context/archive/2026-11-01-urgency-ranked-sighting-list/reviews/impl-review.md:135` and `plan.md:404` - an Adopted no-match E2E assertion and a "no resolved sightings before S-09" manual step will break.

## Related Research

- `context/archive/2026-11-07-comment-on-sighting/research.md`
- `context/archive/2026-11-07-sighting-message-threads/research.md`
- `context/archive/2026-10-11-report-sighting-on-map/research.md`
- `context/archive/2026-10-18-sighting-detail-ui-contract/research.md`

## Open Questions

Product decisions for `/10x-plan`. None of these is recorded anywhere inspected.

1. **What triggers the lock:** does a non-owner comment that was later soft-deleted still lock the sighting? (Owner comments must not lock; threads always do.)
2. **Locked set:** FR-006 locks only location and description. Do name, behaviour, criticality and the photo stay editable after engagement?
3. **Photo editing:** is replacing or removing the photo in scope for S-09 (deferred here by S-04), or split out?
4. **Wire shape:**
   - One update endpoint (PUT or PATCH) or a separate status-change endpoint?
   - Is a changed locked field rejected with a 400 code, or ignored?
   - Are lock state and allowed next statuses returned on `SightingModel`?
5. **Non-owner edit response:** 403, as for comments (inferred, since sightings are publicly readable), or 404?
6. **Status timestamp:** add a single status-changed column, or nothing beyond the audit columns, given that history is a Non-Goal?
7. **Concurrency:** accept the race between the lock check and a concurrent comment or thread, and lost updates, as the non-atomic cooldown was accepted? Or add a rowversion?
8. **Cooldown:** do edits count against the 60 s cooldown?
9. **Resolved sightings:** should commenting and messaging stay allowed once a sighting reaches an end state?
