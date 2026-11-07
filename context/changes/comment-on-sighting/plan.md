# Comment on a Sighting Implementation Plan

## Overview

Roadmap slice S-07 (FR-009, FR-019): any logged-in member can comment on any sighting, edit or delete their own comments, and read the comments newest first in pages of 20 on the sighting detail page. Comment submissions (posts and edits) are throttled by a 60 s per-user comment cooldown that is separate from the sighting cooldown. Deleted comments stay in the thread as a "Comment deleted" placeholder and stay in the database, so the later S-09 engagement lock can still see them.

## Current State Analysis

- No comment code exists in the API or the SPA (research: grep over `PigeonWatch/Api/**/*.cs` and `PigeonWatch/Frontend/src/**/*.ts` at `e65b780`).
- The only domain table is `SIGHTING`. Its entity, EF config, repository, mapper, business object, service, controller, view model creator and DI registration are the template for a child entity (`Data/Entities/SightingEntity.cs:6-31`, `Data/PigeonWatchDbContext.cs:30-40`, `Data/Repositories/SightingRepository.cs`, `Data/Mappers/SightingMapper.cs`, `BusinessLogic/Services/SightingService.cs`, `WebApi/Controllers/SightingsController.cs`, `WebApi/ViewModelCreators/SightingViewModelCreator.cs`).
- The sighting cooldown is inline in `SightingService.CreateAsync` (`SightingService.cs:40-55`): it compares the reporter's latest `SIGHTING.CREATE_DATE` (`SightingRepository.cs:132-141`) with the injected `TimeProvider` against `SightingRules.CooldownSeconds = 60` (`BusinessObjects/SightingRules.cs:8`). Elapsed below 60 s is blocked, exactly 60 s is allowed, and `retryAfterSeconds = min(ceil(remaining), 60)`. S-03 deferred generalising it to S-07 (`context/archive/2026-10-11-report-sighting-on-map/plan.md:41`).
- `AuditSaveChangesInterceptor` sets `UPDATE_DATE = CREATE_DATE` on insert and bumps `UPDATE_*` on every modification (`Data/Auditing/AuditSaveChangesInterceptor.cs:23-37`). So `UPDATE_DATE` cannot tell "edited" apart from "soft-deleted", and cannot drive the edit cooldown.
- Every public action must return `Task<ActionResult<TModel>>` (`ArchitectureTests/ControllerActionTests.cs`), so DELETE returns a model rather than a bare 204.
- Every routed endpoint needs a fixture in `IntegrationTests/EndpointFixtureRegistry.cs:31-48`, or `EndpointCoverageTests` and `PrivacySweep` fail. `ResponseLeakScanner.NeedlesFor` checks email variants only (`ResponseLeakScanner.cs:10-25`).
- The SPA detail page loads the sighting through an RxJS + signals state union and loads the photo in a separate stream (`sighting-detail-page.ts:68-72`, `:205-227`). The report form is the template for validation, the busy state and the cooldown message (`report-sighting-page.ts:118-207`, `sighting-errors.ts:25-79`).

## Desired End State

- `GET sightings/{sightingId}/comments?page=N` returns `{ items, totalCount }`. It holds 20 comments per page, newest first, with deleted comments as placeholders (no text, no author), an "edited" timestamp, and `isMine` for the caller.
- `POST`, `PUT {commentId}` and `DELETE {commentId}` let a member add, edit or soft-delete comments. Edit and delete are author-only (403 otherwise). An unknown sighting, or an unknown or already-deleted comment, is a 404.
- Posting or editing within 60 s of the caller's last comment post or edit is a 400 `CooldownActive` with `retryAfterSeconds` and `Retry-After`. Reporting a sighting does not block commenting, and commenting does not block reporting.
- The detail page shows a comments section. It has an add form, a fixed-height scrollable list with "Show older comments" inside it, inline edit, and delete with confirmation, plus placeholders, an "edited" marker, empty, loading and error states, all labelled from `UiLabels.resx`.
- Verified by: API unit, architecture and integration tests (endpoint coverage and privacy sweep included), a clean migration check, SPA unit specs and label coverage, and one Playwright spec.

### Key Discoveries:

- Display names reach responses through EF navigations (`SightingMapper.cs:35`). Comments copy this with `Include(Author)`, and the author id never leaves the business layer.
- `FailureLoggingControllerBase.GetValidationProblem` already adds `retryAfterSeconds` and `Retry-After` (`FailureLoggingControllerBase.cs:39-53`). Keep the `CooldownActive` code, because the SPA mapper keys on it (`sighting-errors.ts:25-54`).
- The client rules flow `SightingRules` → `ClientConfigurationService.cs:20` → `ClientConfiguration.cs:14` → `ClientConfigurationViewModelCreator.cs:30` → `SightingRulesModel.cs:11` → `core/configuration/configuration.service.ts:20-29`, with a fallback constant in `sighting.types.ts`.
- `FormSubmitState` is per component (`shared/ui/form-field/form-submit-state.ts:7-13`). The comments component provides its own `provideFormFieldDefaults()`.
- Detail-page states need kitchen-sink cells, stub modes and marker assertions (Frontend `CLAUDE.md`, "Kitchen sink"). New lengths need a `--pw-*` token (`src/styles/_tokens.scss`, enforced by `style-token-coverage.spec.ts`).

## What We're NOT Doing

- No admin removal of comments. FR-017 and S-10 cover sightings only; this is recorded as a known gap, not built here.
- No edit history. Only the latest text and an `editedAt` timestamp are kept.
- No contact-detail filter on comment text. test-plan §7 is re-evaluated and the self-disclosure stance is kept and recorded.
- No notifications, read state or live updates of new comments.
- No restriction by sighting status or by ownership: owners and resolved sightings can be commented on.
- No engagement lock. S-09 decides how comments (including deleted ones) lock a sighting; this slice only keeps the data it needs.
- No Polish label values. Keys go into `UiLabels.resx` (English) only, per the established S-12 default.
- No cursor pagination. Page-based paging with dedupe by id in the SPA is the accepted trade-off.
- No ASP.NET rate-limit policy. The business cooldown is the guard, as for sightings.

## Implementation Approach

Data first, then API, then UI. Phase 1 adds the `SIGHTING_COMMENT` table and pulls the cooldown arithmetic into a shared `SubmissionCooldownProvider`. `SightingService` is refactored onto it without any behaviour change, which its existing tests prove. Phase 2 adds `SightingCommentService` and a separate `[Authorize] SightingCommentsController` routed at `sightings/{sightingId:guid}/comments`, wired into every contract gate. Phase 3 builds the SPA section as a child component of the detail page with its own load stream, so the page never waits on comments. Phase 4 adds the browser test and documentation.

## Critical Implementation Details

- **Dedicated columns, not audit columns.** `EDITED_AT` (set only by an edit) drives the "edited" marker and the comment cooldown. `DELETED_AT` (set only by a delete) drives the placeholder. A soft delete must not set `EDITED_AT`, and the cooldown must not consider `DELETED_AT`.
- **Comment cooldown clock.** The author's latest submission is the max over all their comments of `CREATE_DATE` and `EDITED_AT`, including soft-deleted rows, so delete-and-repost cannot bypass it. The sighting clock stays the reporter's latest `SIGHTING.CREATE_DATE`. The two clocks never read each other's tables.
- **Order of checks on post and edit.** Validate the text, then check that the sighting or comment exists (404), then authorship (403, edit and delete only), then the cooldown (edit and post only). A rejected request writes nothing. Delete is not subject to the cooldown.
- **Stable ordering.** Comments are listed by `CREATE_DATE` descending, then `ID` descending, so pages are deterministic when timestamps collide.
- **No page shift.** The comment list is a fixed-height scroll container (new token), and "Show older comments" sits inside it after the last item, so appending a page grows only the container's scroll height and not the page.

## Phase 1: Comment data and shared cooldown

### Overview

Add the comment table and data access, and move the cooldown computation into a provider that both sightings and comments use. No endpoint changes yet.

### Changes Required:

#### 1. Comment rules

**File**: `PigeonWatch/Api/BusinessObjects/SightingCommentRules.cs` (new, const-only)

**Intent**: Single source for comment limits, read by the request models, the entity and the client configuration.

**Contract**: `TextMaxLength = 500`, `PageSize = 20`. The cooldown duration stays `SightingRules.CooldownSeconds` (one 60 s value, two clocks).

#### 2. Business objects

**File**: `PigeonWatch/Api/BusinessObjects/SightingComment.cs`, `NewSightingComment.cs`, `SightingCommentPage.cs`, `SightingCommentResult.cs`, `SightingCommentPageResult.cs`, `SightingCommentErrorCodes.cs` (new)

**Intent**: Domain and result types for comments, mirroring `Sighting`, `NewSighting`, `SightingPage` and `SightingCreateResult`.

**Contract**:
- `SightingComment(Id, SightingId, AuthorId, AuthorDisplayName, Text, CreatedAt, EditedAt?, DeletedAt?)`. `AuthorId` exists only so the API layer can compute `isMine`; it is never mapped to an API model.
- `SightingCommentResult` carries `Succeeded`, `Errors`, `Comment?`, `RetryAfterSeconds?` with `Success`, `Failure` and `CooldownFailure` factories.
- Error codes (PascalCase consts): `CooldownActive` (same string as `SightingErrorCodes.CooldownActive`), `CommentTextRequired`, `CommentTextTooLong`, `SightingNotFound`, `CommentNotFound`, `NotCommentAuthor`, `AuthorNotFound`, `InvalidPage`.

#### 3. Entity, DbContext, migration

**File**: `PigeonWatch/Api/Data/Entities/SightingCommentEntity.cs` (new, internal), `Data/PigeonWatchDbContext.cs`, `Data/Migrations/<timestamp>_AddSightingComment.cs` (generated)

**Intent**: Persist comments as a child of `SIGHTING` with an author FK, following the sighting entity conventions.

**Contract**:
- Table `SIGHTING_COMMENT`, derives from `AuditableEntity`.
- Columns: `SIGHTING_ID` FK → `SIGHTING.ID` and `AUTHOR_ID` FK → `USER_ACCOUNT.ID` (both `OnDelete(Restrict)`, with `Sighting` and `Author` navigations), `TEXT` `nvarchar(SightingCommentRules.TextMaxLength)` NOT NULL, `EDITED_AT` nullable `datetime2`, `DELETED_AT` nullable `datetime2`.
- Indexes `(SIGHTING_ID, CREATE_DATE)` for listing and `(AUTHOR_ID, CREATE_DATE)` for the cooldown.
- `DbSet` internal, `ToTable("SightingComment")`.
- Generated with the `dotnet ef migrations add AddSightingComment` command from `Api/CLAUDE.md`.

#### 4. Repository and mapper

**File**: `PigeonWatch/Api/Data/Repositories/SightingCommentRepository.cs` + `Interfaces/ISightingCommentRepository.cs`, `Data/Mappers/SightingCommentMapper.cs` + `Interfaces/ISightingCommentMapper.cs` (new)

**Intent**: Data access for comments that maps to business objects, with author display names via `Include(Author)`.

**Contract**:
- `CreateAsync(NewSightingComment)` returns null when the author is missing, like `SightingRepository.CreateAsync`.
- `GetAsync(sightingId, commentId)` reads within that sighting and includes deleted rows.
- `ListPageAsync(sightingId, page, pageSize)` returns `SightingCommentPage(Items, TotalCount)`: count including deleted rows, ordered `CREATE_DATE` desc then `ID` desc, skip and take in SQL.
- `UpdateTextAsync(commentId, text, editedAt)` and `SoftDeleteAsync(commentId, deletedAt)` return the updated comment.
- `GetLatestSubmissionDateByAuthorAsync(authorId)` returns the max of `CREATE_DATE` and `EDITED_AT` over all the author's rows, or null. It uses two queries in the style of `GetLatestCreateDateByReporterAsync` (`OrderByDescending(...).Select((DateTime?)...).FirstOrDefaultAsync()`, `SightingRepository.cs:132-141`), one on `CreateDate` and one on `EditedAt` where it is not null. The two results are combined in C# with `DateTime.SpecifyKind(..., DateTimeKind.Utc)`, because SQLite returns `Unspecified`.
- `SightingExistsAsync` can live on `ISightingRepository` instead if that reads more naturally.
- The mapper's `ToEntity` and `ToComment` set `DateTimeKind.Utc` like `SightingMapper.cs:36`.

#### 5. Shared cooldown provider and SightingService refactor

**File**: `PigeonWatch/Api/BusinessLogic/Services/SubmissionCooldownProvider.cs` + `Interfaces/ISubmissionCooldownProvider.cs` (new), `BusinessLogic/Services/SightingService.cs`

**Intent**: One implementation of "seconds left before the next submission is allowed". `SightingService.CreateAsync` keeps its query and message but delegates the arithmetic.

**Contract**: `int? GetRetryAfterSeconds(DateTime? latestSubmission)` returns null when there is no latest submission or when elapsed ≥ `SightingRules.CooldownSeconds` on the injected `TimeProvider`. Otherwise it returns `min(ceil(remaining), CooldownSeconds)`. `SightingService` behaviour (validate → cooldown → photo) is unchanged.

#### 6. DI registration

**File**: `PigeonWatch/Api/DependencyInjection/DataServiceCollectionExtensions.cs`, `BusinessLogicServiceCollectionExtensions.cs`

**Intent**: Register the new repository, mapper and provider so `ServiceRegistrationTests` passes.

**Contract**: Scoped registrations alongside the sighting ones (`DataServiceCollectionExtensions.cs:60-61`, `BusinessLogicServiceCollectionExtensions.cs:16`).

#### 7. Tests

**File**: `PigeonWatch/Api/UnitTests/SubmissionCooldownProviderTests.cs`, `SightingCommentRepositoryTests.cs` (new), `UnitTests/SightingServiceTests.cs` (adjusted construction only)

**Intent**: Pin the cooldown boundaries in one place and prove the repository's ordering, paging, count and latest-submission semantics on SQLite.

**Contract**:
- Provider: no history gives null; 59.x s gives 1; exactly 60 s gives null; 0 s gives 60.
- Repository: newest first with an id tiebreak; deleted rows counted and returned; latest submission considers `EDITED_AT` and deleted rows; a page past the end is empty with the real count.
- Existing sighting cooldown tests stay green unmodified in their assertions.

### Success Criteria:

#### Automated Verification:

- API builds with style rules: `dotnet build PigeonWatch/Api/PigeonWatchApi.slnx`
- Architecture tests pass: `dotnet test PigeonWatch/Api/ArchitectureTests/PigeonWatch.ArchitectureTests.csproj`
- Unit tests pass, including the new provider and repository tests and the unchanged sighting cooldown tests: `dotnet test PigeonWatch/Api/UnitTests/PigeonWatch.UnitTests.csproj`
- Migration matches the model: `dotnet ef migrations has-pending-model-changes --project PigeonWatch/Api/Data/PigeonWatch.Data.csproj --startup-project PigeonWatch/Api/WebApi.Host/PigeonWatch.WebApi.Host.csproj`
- Integration tests still pass: `dotnet test PigeonWatch/Api/IntegrationTests/PigeonWatch.IntegrationTests.csproj`

#### Manual Verification:

- Generated migration creates `SIGHTING_COMMENT` with the two FKs, both indexes, nullable `EDITED_AT`/`DELETED_AT` and UPPER_SNAKE_CASE names (read the migration file)

**Implementation Note**: After completing this phase and all automated verification passes, pause here for manual confirmation from the human that the manual testing was successful before proceeding to the next phase.

---

## Phase 2: Comment API

### Overview

Expose list, post, edit and delete endpoints with authorship, the cooldown and placeholder semantics, publish comment rules to the SPA, and join every API contract gate.

### Changes Required:

#### 1. Comment service

**File**: `PigeonWatch/Api/BusinessLogic/Services/SightingCommentService.cs` + `Interfaces/ISightingCommentService.cs` (new)

**Intent**: Business rules for comments: trimming, validation, existence, authorship, cooldown and timestamps.

**Contract**:
- `ListAsync(sightingId, page)` gives `SightingNotFound` or `InvalidPage` (page < 1) or the page.
- `CreateAsync(sightingId, authorId, text)`: trim, then `CommentTextRequired`/`CommentTextTooLong`, then `SightingNotFound`, then the cooldown (`CooldownFailure` with message "Wait {n} seconds before posting or editing another comment."), then insert (`AuthorNotFound` if the user row is gone).
- `UpdateAsync(sightingId, commentId, userId, text)`: validate, then `CommentNotFound` (missing, other sighting or deleted), then `NotCommentAuthor`, then the cooldown, then set the text and `EditedAt = now`.
- `DeleteAsync(sightingId, commentId, userId)`: `CommentNotFound` (missing or already deleted), then `NotCommentAuthor`, then set `DeletedAt = now`. No cooldown.
- Time from the injected `TimeProvider`; cooldown via `ISubmissionCooldownProvider` with `GetLatestSubmissionDateByAuthorAsync`.

#### 2. API models, request mapper, view model creator

**File**: `PigeonWatch/Api/WebApi/Models/SightingCommentModel.cs`, `SightingCommentPageModel.cs`, `SightingCommentRequestModel.cs`, `CommentRulesModel.cs` (new); `WebApi/ViewModelCreators/SightingCommentViewModelCreator.cs` + interface (new)

**Intent**: Wire shapes that never carry an author id, email or audit column. Placeholders blank out the content.

**Contract**:
- `SightingCommentModel { id, text?, authorDisplayName?, createdAt, editedAt?, isDeleted, isMine }`. When deleted: `text` and `authorDisplayName` are null, `editedAt` null, `isMine` false.
- `SightingCommentPageModel { items, totalCount }`.
- `SightingCommentRequestModel { text }` with `[Required]` and `[MaxLength(SightingCommentRules.TextMaxLength)]`.
- The view model creator takes the current user id to compute `isMine`.

#### 3. Controller

**File**: `PigeonWatch/Api/WebApi/Controllers/SightingCommentsController.cs` (new), `WebApi/Controllers/FailureLoggingControllerBase.cs`

**Intent**: A separate `[ApiController] [Authorize]` controller routed at `sightings/{sightingId:guid}/comments`, deriving from `FailureLoggingControllerBase`.

**Contract**:
- `GET ?page=1` → 200 page, `Cache-Control: private, no-store`.
- `POST` → 201 `SightingCommentModel` with `Location` pointing at the list.
- `PUT {commentId:guid}` → 200 model.
- `DELETE {commentId:guid}` → 200 placeholder model.
- Mappings: `SightingNotFound`/`CommentNotFound` → 404 (logged at Information like `NotFoundForSighting`); `NotCommentAuthor` → 403 via a new base helper that logs a Warning with the route; `AuthorNotFound` → `UnauthorizedForMissingUser`; a bad user claim → `UnauthorizedForInvalidIdentity`.
- Other codes → `GetValidationProblem` with `retryAfterSeconds` for the cooldown. `InvalidPage` and `CommentTextTooLong` are in `loggedErrorCodes`; `CooldownActive` and `CommentTextRequired` are not logged.

#### 4. Client configuration

**File**: `PigeonWatch/Api/BusinessObjects/ClientConfiguration.cs`, `BusinessLogic/Services/ClientConfigurationService.cs`, `WebApi/Models/ClientConfigurationModel.cs`, `WebApi/ViewModelCreators/ClientConfigurationViewModelCreator.cs`

**Intent**: Publish the comment limits so the SPA never hard-codes them.

**Contract**: New `commentRules: { textMaxLength: 500, cooldownSeconds: 60, pageSize: 20 }` block on `GET configuration/client`, carried by the nested `CommentRulesModel` from §2.

#### 5. DI

**File**: `PigeonWatch/Api/DependencyInjection/BusinessLogicServiceCollectionExtensions.cs`, `WebApiServiceCollectionExtensions.cs`

**Intent**: Register the service and the view model creator.

**Contract**: Scoped, next to the sighting registrations.

#### 6. Integration gates and contract tests

**File**: `PigeonWatch/Api/IntegrationTests/EndpointFixtureRegistry.cs`, `FixtureContext.cs`, `PrivacySweepTests.cs`, `EndpointProtectionTests.cs`, `TestUser.cs`, `SightingCommentContractTests.cs` (new)

**Intent**: Every new endpoint is covered by the coverage, protection and privacy sweeps, and the comment contract is proven end to end.

**Contract**:
- Four fixture entries (`GET`/`POST sightings/{sightingId:guid}/comments`, `PUT`/`DELETE sightings/{sightingId:guid}/comments/{commentId:guid}`). `FixtureContext` gains a `CommentId`, seeded over HTTP as Alice on her sighting through a `TestUser` helper like `CreateSightingAsync`. It is added at both construction sites: `PrivacySweepTests.cs:233-241` and `EndpointProtectionTests.cs:76-79`. The comment cooldown must stay a 400: `PrivacySweep` fails on any 429 (`PrivacySweep.cs:34-35`) and sends repeated valid POSTs.
- Contract tests:
  - Bob can comment on Alice's sighting, and Alice can comment on her own.
  - The list is newest first, 20 per page, page 2 is reachable, `totalCount` is right, and page 0 is a 400 `InvalidPage`.
  - An unknown sighting or comment is a 404.
  - Bob editing or deleting Alice's comment is a 403, and reading back through Alice's client shows it unchanged.
  - Deleting a deleted comment is a 404, and editing a deleted comment is a 404.
  - The placeholder has null text and author and `isDeleted` true, and stays in `totalCount`.
  - `editedAt` is set on edit, not on delete.
  - Cooldown with `ManualTimeProvider`: post then post at +30 s is a 400 `CooldownActive` with `retryAfterSeconds` 30 and `Retry-After`; post then edit at +30 s is blocked; post then delete then post at +30 s is blocked; at +60 s it is allowed; reporting a sighting then commenting at +1 s is allowed, and commenting then reporting at +1 s is allowed.
  - Whitespace-only text is a 400 `CommentTextRequired`; 501 characters is a 400.
  - Explicit assertions that the JSON has no `authorId`, `createUser`, `updateUser` or user-id string.
- Existing `EndpointCoverageTests`, `EndpointProtectionTests` and `PrivacySweepTests` pass with the new fixtures.

#### 7. Unit tests

**File**: `PigeonWatch/Api/UnitTests/SightingCommentServiceTests.cs`, `SightingCommentsControllerTests.cs`, `SightingCommentViewModelCreatorTests.cs` (new), `ConfigurationTests.cs`

**Intent**: Pin the order of checks, the error mapping, the placeholder shaping and the client config block.

**Contract**: The service rejects before writing (no repository write on any failure); validation precedes existence, which precedes authorship, which precedes the cooldown; the controller maps each code to the status above; the view model creator blanks deleted comments and computes `isMine`; the config exposes `commentRules`.

#### 8. Documentation

**File**: `PigeonWatch/Api/CLAUDE.md`, `context/foundation/test-plan.md`

**Intent**: Record the new table, endpoints, cooldown model and test gates; close the §7 re-evaluation.

**Contract**:
- `Api/CLAUDE.md`: `SIGHTING_COMMENT` in the data layer list, the four endpoints, a "Sighting comments" section (separate clocks, edit counts, delete does not, deleted rows count for the clock, placeholder shape, 403/404 rules), and the `SubmissionCooldownProvider` noted under Cooldown.
- `test-plan.md:213`: note that the free-text exclusion was re-evaluated for comments on 2026-11-07 and kept, because authors can edit or delete to retract; the user-id assertion covers the scanner gap for comments.

### Success Criteria:

#### Automated Verification:

- API builds: `dotnet build PigeonWatch/Api/PigeonWatchApi.slnx`
- Architecture tests pass: `dotnet test PigeonWatch/Api/ArchitectureTests/PigeonWatch.ArchitectureTests.csproj`
- Unit tests pass, including the comment service, controller, view model creator and configuration tests: `dotnet test PigeonWatch/Api/UnitTests/PigeonWatch.UnitTests.csproj`
- Integration tests pass, including `SightingCommentContractTests`, endpoint coverage, protection and the privacy sweep: `dotnet test PigeonWatch/Api/IntegrationTests/PigeonWatch.IntegrationTests.csproj`
- No pending model changes: `dotnet ef migrations has-pending-model-changes --project PigeonWatch/Api/Data/PigeonWatch.Data.csproj --startup-project PigeonWatch/Api/WebApi.Host/PigeonWatch.WebApi.Host.csproj`

#### Manual Verification:

- Against the local API (LocalDB), post, list, edit within 60 s (blocked), delete and list as two users with `curl.exe` or Swagger, confirming the placeholder and that no response carries an author id or email

**Implementation Note**: After completing this phase and all automated verification passes, pause here for manual confirmation from the human that the manual testing was successful before proceeding to the next phase.

---

## Phase 3: SPA comments section

### Overview

Add a comments section to the sighting detail page: an add form, a fixed-height newest-first list with "Show older comments", inline edit, delete with confirmation, placeholders and an "edited" marker, all with labels, kitchen-sink cells and specs.

### Changes Required:

#### 1. Wire types, configuration and API client

**File**: `PigeonWatch/Frontend/src/app/features/sightings/sighting.types.ts`, `core/configuration/configuration.service.ts`, `features/sightings/sightings-api.ts`

**Intent**: Typed access to the comment endpoints and rules.

**Contract**:
- `SightingComment` and `SightingCommentPage` interfaces matching the API models; `CommentRules { textMaxLength, cooldownSeconds, pageSize }` on the client config with a `fallbackCommentRules` constant.
- `SightingsApi` gets `comments(sightingId, page)`, `addComment(sightingId, text)`, `editComment(sightingId, commentId, text)` and `deleteComment(sightingId, commentId)`, each with `encodeURIComponent` on ids.

#### 2. Error mapping

**File**: `PigeonWatch/Frontend/src/app/features/sightings/comment-errors.ts` (new) + spec

**Intent**: Map comment failures to UI states and messages, mirroring `sighting-errors.ts`.

**Contract**:
- Union: `cooldown(retryAfterSeconds)`, `textRequired`, `notFound` (404), `forbidden` (403), `unexpected(traceId)`.
- Cooldown is detected by `cooldownactive` on a 400 with `retryAfterSeconds` > 0, otherwise generic.
- Messages: `sighting.comments.errors.cooldown` ("You can post or edit another comment in {0} seconds."), `…notFound` ("This comment no longer exists. Refresh the comments."), `…forbidden`, `…generic`.
- Report unexpected failures and 403s, but not 400/401/404/429. A 403 means the UI offered Edit or Delete on a comment that is not the caller's, which is a client bug, so it shows the forbidden message with `app-error-reference`. Contexts are `sighting.comments.load`, `sighting.comments.add`, `sighting.comments.edit` and `sighting.comments.delete`. The shared `expectedClientStatuses` in `shared/http/problem-details.ts` stays unchanged.

#### 3. Comments component

**File**: `PigeonWatch/Frontend/src/app/features/sightings/sighting-detail-page/sighting-comments/sighting-comments.{ts,html,scss}` (new)

**Intent**: A self-contained section with its own load stream and form state, so the detail page never waits on it.

**Contract**:
- Input `sightingId` (required) and `providers: [provideFormFieldDefaults()]`.
- Heading `sighting.comments.title` with the count.
- Add form above the list: Signal Forms textarea, required, whitespace-only counts as empty, `maxLength` from `commentRules`, hint showing the max, `app-submit-button [busy]`. On success it prepends the returned comment, clears the field and increments the total; on cooldown it shows an `app-alert` with seconds and keeps the text.
- List: a scroll container with `max-height: var(--pw-comments-list-max-height)`, `overflow-y: auto`, `tabindex="0"`, `role="region"` and an `aria-label`. Each item shows the author display name, relative time (`sightingAge`) in a `<time>` with an absolute `title`, and `sighting.comments.edited` when `editedAt` is set; text keeps line breaks and is rendered as plain interpolation, never `innerHTML`.
- Own comments get Edit (inline textarea, Save/Cancel, same validation and cooldown handling) and Delete (`MatDialog` confirm with `sighting.comments.deleteConfirm…` keys; on success the item is replaced in place by the placeholder model).
- Deleted items render `sighting.comments.deleted` with no author or actions.
- "Show older comments" sits inside the scroll container after the last item, shown while `items.length < totalCount`. It loads the next page, appends with dedupe by id, and shows a busy state while loading.
- States: loading (`app-loading-indicator`), empty (muted text, `pw-text-muted`, `sighting.comments.empty`; not an `app-alert`, so the detail page's `app-alert` assertions still refer to the page's own alerts), load error (`app-error-retry` + `app-error-reference`), and an "older" load error inline next to the button with retry.
- The component renders no `dl` and no links, so the detail page's facts-list and link assertions keep their meaning.

#### 4. Detail page integration

**File**: `PigeonWatch/Frontend/src/app/features/sightings/sighting-detail-page/sighting-detail-page.{ts,html}`

**Intent**: Render the section in the loaded state only, at the end of `.sighting-detail__details` (after `sighting-detail-page.html:99`), so it stays in the left column when the map is shown.

**Contract**: `<app-sighting-comments [sightingId]="sighting.id" />`. It does not take focus, and the page heading focus and title behaviour are unchanged.

#### 5. Token and labels

**File**: `PigeonWatch/Frontend/src/styles/_tokens.scss`, `PigeonWatch/Api/BusinessObjects/Resources/UiLabels.resx`

**Intent**: The fixed list height is a token; every visible string is a resx key.

**Contract**:
- `--pw-comments-list-max-height` (about 28rem).
- Keys under `sighting.comments.*`: title/titleWithCount, empty, loading, listLabel, add.label, add.hint ({0}), add.submit, edit, save, cancel, delete, deleteConfirmTitle, deleteConfirmMessage, deleteConfirmAction, deleted, edited, showOlder, olderError, errors.cooldown ({0}), errors.notFound, errors.forbidden, errors.generic, validation.required, validation.maxLength ({0}).

#### 6. Kitchen sink

**File**: `PigeonWatch/Frontend/src/app/dev/kitchen-sink/cell-definitions.ts`, `cell-stubs.ts`, `kitchen-sink.spec.ts`

**Intent**: Visual gate for each comments state, per the detail-page contract.

**Contract**:
- Cells under "Sighting detail" for comments loading, empty, mixed list (own, other, edited, deleted, with "Show older"), load error, cooldown alert, and editing.
- `CellStubs.sightingsApi` (`cell-stubs.ts:289-297`) gains a default `comments` that returns an empty page, plus `addComment`, `editComment` and `deleteComment`. 9 of the 12 detail cells reach the loaded state and render the section, so without these they throw.
- Comment stub modes in `CellStubs`, and marker assertions in `kitchen-sink.spec.ts`. Its existing detail selectors (`dl > div` length 7 and the `app-alert`/`figure` checks at `:457-588`) are scoped to the facts list and photo area, not loosened.
- The sink still never takes focus on load.

#### 7. Specs

**File**: `sighting-comments/sighting-comments.spec.ts`, `sighting-comments-reporting.spec.ts` (new); `sighting-detail-page.spec.ts` (add comment stubs)

**Intent**: Cover behaviour without a browser.

**Contract**:
- Initial load requests page 1.
- "Show older" requests page 2 and appends without duplicates when an item repeats.
- Posting prepends and clears; the cooldown shows seconds and keeps the text; whitespace-only shows the required error and makes no call.
- Edit and Delete appear only on `isMine` items; editing saves and shows the edited marker.
- Delete confirm → placeholder, and cancel makes no call.
- A 404 on edit or delete shows the not-found message.
- Load error → retry.
- Reporting: 500 and 403 reported with context; 400 and 404 silent.
- `sighting-detail-page.spec.ts` (`{ get, photo }` stub at `:55`) and `sighting-detail-page-reporting.spec.ts` (`{ get }` at `:28`) gain a default empty-page `comments` stub.
- Assertions that change because the page now has a section are scoped, not loosened: `dl > div` length 7 (`:95, :546, :562`) to the facts list, `querySelectorAll('a')` count (`:221, :260`) to the card actions, `app-alert` null (`:211`) to the details area, and `report` call counts (`:497-589`) to the sighting contexts.

### Success Criteria:

#### Automated Verification:

- SPA unit tests pass, including the new comment specs, kitchen-sink markers, label coverage and style-token coverage: `npm test -- --watch=false` (in `PigeonWatch/Frontend`)
- Production build passes within the bundle budget: `npm run build` (in `PigeonWatch/Frontend`)
- API resource tests still pass with the new keys: `dotnet test PigeonWatch/Api/UnitTests/PigeonWatch.UnitTests.csproj`

#### Manual Verification:

- On the detail page against the local API: post, edit (blocked within 60 s with the seconds shown, allowed after), delete with confirmation shows the placeholder, and "Show older comments" with more than 20 comments appends inside the box without moving the page
- Keyboard: the list region is focusable and scrolls with the arrow keys, every action is reachable by Tab, and the dialog returns focus on close
- Kitchen sink: the comment cells render at form and wide widths with no layout jump

**Implementation Note**: After completing this phase and all automated verification passes, pause here for manual confirmation from the human that the manual testing was successful before proceeding to the next phase.

---

## Phase 4: E2E and documentation

### Overview

One browser test over the real form and API, and project docs updated for the new surface.

### Changes Required:

#### 1. Playwright spec

**File**: `PigeonWatch/Frontend/tests/e2e/sighting-comments.spec.ts` (new)

**Intent**: Prove the comment flow in the running app, including the cooldown on an immediate edit.

**Contract**: Following `tests/e2e/sighting-photo.spec.ts:12-64`:
1. Register a fresh user with an empty `storageState`, as `sighting-photo.spec.ts:7,19` does, so a rerun or retry is never blocked by an earlier run's comment cooldown. Then report a sighting and open its detail page.
2. Post a comment and see it at the top.
3. Press Edit, change the text, save immediately, and see the cooldown message with seconds.
4. Delete with confirm and see the "Comment deleted" placeholder.

#### 2. Project docs

**File**: `PigeonWatch/CLAUDE.md`, `PigeonWatch/Frontend/CLAUDE.md`

**Intent**: Keep the agent-facing docs accurate.

**Contract**:
- `PigeonWatch/CLAUDE.md`: S-07 built, `SIGHTING_COMMENT` and the four comment endpoints in the surface list, and the separate comment cooldown.
- Frontend `CLAUDE.md`: a short "Sighting comments" section (component, scroll container and token, newest-first paging inside the box, the error contexts, the kitchen-sink cells and the e2e spec, and the new detail card heights per state, which replace the archived visual-notes baseline whose "no form" and "no list" N/A entries no longer hold), and the stale `context/changes/sighting-detail-ui-contract/visual-notes.md` path fixed to `context/archive/2026-10-18-sighting-detail-ui-contract/visual-notes.md`.

### Success Criteria:

#### Automated Verification:

- E2E suite passes, including `sighting-comments.spec.ts`: `npx playwright test` (in `PigeonWatch/Frontend`)
- SPA unit tests still pass: `npm test -- --watch=false` (in `PigeonWatch/Frontend`)

#### Manual Verification:

- Docs read correctly: the endpoint list, cooldown description and fixed visual-notes path match the code

**Implementation Note**: After completing this phase and all automated verification passes, pause here for manual confirmation from the human that the manual testing was successful before proceeding to the next phase.

---

## Testing Strategy

### Unit Tests:

- `SubmissionCooldownProvider` boundaries (59.x s / 60 s / none).
- `SightingCommentService` order of checks, trimming, `EditedAt` versus `DeletedAt`, and no writes on failure.
- Controller status mapping; view model creator placeholder and `isMine`; client config `commentRules`.
- SPA: comment error mapping, component flows (post, edit, delete, page append with dedupe, cooldown, states), and report-or-silent per status.

### Integration Tests:

- `SightingCommentContractTests` over the real pipeline with `ManualTimeProvider`: authorship with read-back, the separate clocks, edit counts, delete does not, delete-and-repost still throttled, placeholder shape, paging, and no author id or audit fields.
- Endpoint coverage, protection (anonymous gets 401) and the privacy sweep, including the four new endpoints.

### Manual Testing Steps:

1. As user A, report a sighting; as user B, open it, post a comment, and try to edit it immediately (cooldown message), then edit after 60 s (marker shown).
2. As A, confirm there are no Edit/Delete buttons on B's comment, then post your own comment.
3. As B, delete your comment and confirm the placeholder stays in place and the count is unchanged.
4. Seed more than 20 comments and press "Show older comments" repeatedly; the page must not move.

## Performance Considerations

The list reads one page of 20 with a count over the `(SIGHTING_ID, CREATE_DATE)` index. The cooldown reads the max over the author's rows via `(AUTHOR_ID, CREATE_DATE)`; `EDITED_AT` is not indexed, which is acceptable at MVP volumes. Comments load in their own request, so the detail page's first render is unaffected.

## Migration Notes

The additive migration `AddSightingComment` creates a new table with no data backfill. Rollback is the generated `Down` (drop table). The cooldown refactor changes no stored data.

## References

- Research: `context/changes/comment-on-sighting/research.md`
- Roadmap slice: `context/foundation/roadmap.md:174-184`
- PRD: `context/foundation/prd.md:80` (FR-006 lock), `:86` (FR-009), `:115` (FR-019), `:160` (access)
- Sighting cooldown: `PigeonWatch/Api/BusinessLogic/Services/SightingService.cs:40-55`
- Sighting entity template: `PigeonWatch/Api/Data/Entities/SightingEntity.cs:6-31`
- Endpoint gates: `PigeonWatch/Api/IntegrationTests/EndpointFixtureRegistry.cs:31-48`, `context/foundation/test-plan.md:184-196`
- Form and cooldown UI template: `PigeonWatch/Frontend/src/app/features/sightings/report-sighting-page/report-sighting-page.ts:118-207`, `sighting-errors.ts:25-79`
- E2E template: `PigeonWatch/Frontend/tests/e2e/sighting-photo.spec.ts:12-64`

## Progress

> Convention: `- [ ]` pending, `- [x]` done. Append ` — <commit sha>` when a step lands. Do not rename step titles. See `references/progress-format.md`.

### Phase 1: Comment data and shared cooldown

#### Automated

- [x] 1.1 API builds with style rules — f101e37
- [x] 1.2 Architecture tests pass — f101e37
- [x] 1.3 Unit tests pass, including the new provider and repository tests and the unchanged sighting cooldown tests — f101e37
- [x] 1.4 Migration matches the model — f101e37
- [x] 1.5 Integration tests still pass — f101e37

#### Manual

- [x] 1.6 Generated migration creates SIGHTING_COMMENT with the two FKs, both indexes, nullable EDITED_AT/DELETED_AT and UPPER_SNAKE_CASE names — f101e37

### Phase 2: Comment API

#### Automated

- [x] 2.1 API builds
- [x] 2.2 Architecture tests pass
- [x] 2.3 Unit tests pass, including the comment service, controller, view model creator and configuration tests
- [x] 2.4 Integration tests pass, including SightingCommentContractTests, endpoint coverage, protection and the privacy sweep
- [x] 2.5 No pending model changes

#### Manual

- [x] 2.6 Against the local API, post, list, edit within 60 s (blocked), delete and list as two users, confirming the placeholder and no author id or email

### Phase 3: SPA comments section

#### Automated

- [ ] 3.1 SPA unit tests pass, including the new comment specs, kitchen-sink markers, label coverage and style-token coverage
- [ ] 3.2 Production build passes within the bundle budget
- [ ] 3.3 API resource tests still pass with the new keys

#### Manual

- [ ] 3.4 On the detail page: post, edit cooldown, delete placeholder, and Show older appends inside the box without moving the page
- [ ] 3.5 Keyboard: the list region scrolls, actions are reachable by Tab, and the dialog returns focus
- [ ] 3.6 Kitchen sink: the comment cells render at form and wide widths with no layout jump

### Phase 4: E2E and documentation

#### Automated

- [ ] 4.1 E2E suite passes, including sighting-comments.spec.ts
- [ ] 4.2 SPA unit tests still pass

#### Manual

- [ ] 4.3 Docs read correctly: endpoint list, cooldown description and fixed visual-notes path match the code
