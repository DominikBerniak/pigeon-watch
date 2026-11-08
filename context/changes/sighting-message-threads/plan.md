# Sighting Message Threads (S-08) Implementation Plan

## Overview

A signed-in user who does not own a sighting can contact its owner. The first message opens a private two-person thread tied to that sighting; both participants read it and reply with plain text, and nobody else can see it. Each user finds their threads on a Messages page reached from the header user menu, and opens a thread on its sighting's detail page. The other person is only ever shown by display name (FR-012, FR-013, PRD contact-privacy NFR).

## Current State Analysis

- No messaging code exists (`research.md`, Summary). S-07 comments is the layer-by-layer template: controller → view-model creator → service → repository → mapper → entity, const-only `*Rules`, client configuration, SPA failure union, privacy fixtures.
- The sighting owner is `SightingEntity.ReporterId` (`PigeonWatch/Api/Data/Entities/SightingEntity.cs:8`). The `Sighting` business object and `SightingModel` (`PigeonWatch/Api/WebApi/Models/SightingModel.cs:3-26`) carry only `ReporterDisplayName`, so neither the API layer nor the SPA can tell whether a sighting is the caller's own.
- `ISightingViewModelCreator.Create(Sighting)` feeds five actions: create, get, list, page and mine (`PigeonWatch/Api/WebApi/Controllers/SightingsController.cs:65,80,127,146,159`). `Get` does not resolve the current user today.
- The header user menu (`PigeonWatch/Frontend/src/app/app.html:22-28`) holds Report sighting, Profile and Log out. The initial bundle is 533.52 kB against a 540 kB warning (`PigeonWatch/Frontend/CLAUDE.md:33`).
- Privacy tests require a fixture per routed endpoint (`EndpointFixtureRegistry.cs:47-50` pattern, `EndpointCoverageTests`) and the sweep world is seeded by `SeedWorldAsync` (`PigeonWatch/Api/IntegrationTests/PrivacySweepTests.cs:233-240`) with Alice content only.

## Desired End State

- Bob opens Alice's sighting, sees "Contact {Alice's display name}", sends a message, and sees his thread on that page. Alice sees Bob's thread on her sighting and on `/messages`, replies, and Bob sees the reply after reloading. Carol cannot read or post to that thread (404).
- Alice does not see a contact action on her own sighting; the API rejects an owner starting a thread on their own sighting (400).
- No response carries another user's id or email; the privacy sweep, coverage guard and a dedicated contract test prove it.
- Verified by the unit, integration, SPA and Playwright suites listed per phase, and by the manual two-browser walk-through in Testing Strategy.

### Key Discoveries:

- Order of checks and logging follow `Api/CLAUDE.md` and `SightingCommentService.cs:30-38`: validate → resource exists → participant → write; 404 logged at Information, 403/abnormal at Warning, validation not logged.
- `UserName` equals the email (`AccountRepository.cs:48`) and `BusinessObjects.CurrentUser` carries the email (`CurrentUser.cs:3`); neither may describe a counterpart.
- The privacy scanner checks emails only, never user ids (`test-plan.md:183`); `SightingCommentContractTests.cs:277` is the no-id template.
- `AuditSaveChangesInterceptor` bumps `UPDATE_*` on every modification, so "last activity" needs its own column, not `UPDATE_DATE` (comment plan, `context/archive/2026-11-07-comment-on-sighting/plan.md:12`).

## What We're NOT Doing

- No notifications or live delivery (FR-013, `prd.md:165`). *(Amended 2026-11-08: read/unread state and an unread count are now in scope, Phase 6; FR-013 amended.)*
- No message edit or delete; messages are append-only.
- No cooldown on messages (FR-019 names sightings and comments only); spam control waits for S-10/S-11 moderation.
- No owner-initiated threads and no way to address a user directly; only a non-owner starts a thread.
- No admin messaging (S-11), no admin removal of messages (S-10 gap, as for comments).
- No engagement lock (S-09); S-08 only stores the data S-09 will query.
- No Polish label values (S-12 default).
- No contact-detail scanning of message text (test-plan §7 decision recorded in Phase 3).

## Implementation Approach

Mirror S-07 at every layer, adding two concepts it lacks: a **participant check** on every thread read and write, and a **derived recipient** (the owner always comes from the sighting, never from the request). Data first, API second, contract tests third so the privacy guarantees are locked before any UI exists; then the detail-page panel, the inbox page, and finally the two-user E2E plus docs.

Thread identity: one `SIGHTING_THREAD` row per (sighting, contacting user), unique on that pair; the owner is implied by the sighting. This gives S-09 "has a non-owner made contact on sighting X" as an existence query and leaves S-11 free to add a different thread kind later.

## Critical Implementation Details

- **Start-or-append race.** `POST sightings/{id}/threads` creates the thread if the caller has none on that sighting, else appends to it. Two concurrent first posts by the same user hit the unique `(SIGHTING_ID, PARTICIPANT_ID)` index; on that unique violation re-read the existing thread and append, instead of surfacing a 500. Do not catch broader exceptions.
- **404, not 403, for non-participants.** A caller who is not the participant or the sighting's owner gets `ThreadNotFound` → 404 for both reads and replies, so thread ids cannot be probed. Every repository read and write is scoped by thread id, sighting id and participant-or-owner, not by thread id alone (S-07 impl-review F1).
- **Deep link.** `/messages` links to `/sightings/:id?thread=<threadId>`; the owner's panel preselects that thread, a non-owner ignores the parameter. *(Superseded 2026-11-07: threads open at `/messages/:sightingId/:threadId`; see the Phase 4 and 5 amendments.)*

## Phase 1: Thread and message data model

### Overview

Tables, entities, mapping and repository for threads and messages, tested on in-memory SQLite.

### Changes Required:

#### 1. Business objects and rules

**File**: `PigeonWatch/Api/BusinessObjects/` (new `SightingThread.cs`, `SightingMessage.cs`, `NewSightingMessage.cs`, `SightingThreadPage.cs`, `SightingMessagePage.cs`, `SightingMessageRules.cs`)

**Intent**: Plain records for the two concepts plus const-only rules.

**Contract**:
- `SightingThread(Id, SightingId, SightingName?, OwnerId, OwnerDisplayName, ParticipantId, ParticipantDisplayName, LastMessageAt)`; ids exist only for the API layer to compute flags and are never mapped to API models.
- `SightingMessage(Id, ThreadId, SenderId, SenderDisplayName, Text, CreatedAt)`.
- `SightingThreadPage(Items, TotalCount)`, `SightingMessagePage(Items, TotalCount)`.
- `SightingMessageRules { TextMaxLength = 1000; PageSize = 20; }`.

#### 2. Entities, DbContext and migration

**File**: `PigeonWatch/Api/Data/Entities/SightingThreadEntity.cs`, `SightingMessageEntity.cs`, `PigeonWatch/Api/Data/PigeonWatchDbContext.cs`, `PigeonWatch/Api/Data/Migrations/<timestamp>_AddSightingMessageThreads.cs`

**Intent**: Two internal `AuditableEntity` tables following the comment configuration (`PigeonWatchDbContext.cs:44-58`).

**Contract**:
- `SIGHTING_THREAD`: `SIGHTING_ID` FK → `SIGHTING`, `PARTICIPANT_ID` FK → `USER_ACCOUNT`, `LAST_MESSAGE_AT` (UTC); unique index `(SIGHTING_ID, PARTICIPANT_ID)`, index `(PARTICIPANT_ID, LAST_MESSAGE_AT)`.
- `SIGHTING_MESSAGE`: `THREAD_ID` FK → `SIGHTING_THREAD`, `SENDER_ID` FK → `USER_ACCOUNT`, `TEXT` max `SightingMessageRules.TextMaxLength`; index `(THREAD_ID, CREATE_DATE)`.
- All FKs `DeleteBehavior.Restrict`. Generated with the `dotnet ef migrations add AddSightingMessageThreads` command from `Api/CLAUDE.md`.

#### 3. Mapper and repository

**File**: `PigeonWatch/Api/Data/Mappers/SightingThreadMapper.cs` (+ interface), `PigeonWatch/Api/Data/Repositories/SightingThreadRepository.cs` (+ interface), `PigeonWatch/Api/DependencyInjection/DataServiceCollectionExtensions.cs`

**Intent**: Persistence operations the service needs, every read scoped by its invariants.

**Contract** (`ISightingThreadRepository`):
- `GetSightingOwnerAsync(sightingId)` → owner id or null when the sighting does not exist.
- `FindThreadAsync(sightingId, participantId)` → thread or null.
- `GetThreadForUserAsync(sightingId, threadId, userId)` → thread only when `userId` is the participant or the sighting's reporter, else null.
- `StartOrAppendAsync(sightingId, participantId, text, now)` → `(thread, message)`; inserts the thread when missing, recovers from the unique-index race by re-reading, sets `LAST_MESSAGE_AT = now`. Returns null when the participant user row is gone.
- `AppendAsync(threadId, senderId, text, now)` → message; updates `LAST_MESSAGE_AT`. Null when the sender row is gone.
- `ListThreadsForSightingAsync(sightingId, userId, page, pageSize)` → all threads when `userId` is the owner, else the caller's own (0 or 1); ordered `LAST_MESSAGE_AT desc, ID desc`.
- `ListThreadsForUserAsync(userId, page, pageSize)` → threads where the user is participant or sighting reporter, same order.
- `ListMessagesAsync(threadId, page, pageSize)` → `CREATE_DATE desc, ID desc`, count + skip/take in SQL.
- Display names come from `Include` of `USER_ACCOUNT.DisplayName`; never project `UserName` or `Email`. Dates via `DateTime.SpecifyKind(..., Utc)`.

#### 4. Repository tests

**File**: `PigeonWatch/Api/UnitTests/SightingThreadRepositoryTests.cs`

**Intent**: Prove scoping and ordering on in-memory SQLite, following `SightingCommentRepositoryTests.cs`.

**Contract**: covers owner/participant/outsider visibility for `GetThreadForUserAsync` and both list methods, start creating vs reusing a thread, the unique index rejecting a second thread for the same pair, `LAST_MESSAGE_AT` ordering, message paging and overflow-safe skip.

### Success Criteria:

#### Automated Verification:

- API builds with style rules: `dotnet build PigeonWatch/Api/PigeonWatchApi.slnx`
- Architecture tests pass, including table naming and service registration: `dotnet test PigeonWatch/Api/ArchitectureTests/PigeonWatch.ArchitectureTests.csproj`
- Unit tests pass, including `SightingThreadRepositoryTests`: `dotnet test PigeonWatch/Api/UnitTests/PigeonWatch.UnitTests.csproj`
- Migration matches the model: `dotnet ef migrations has-pending-model-changes --project PigeonWatch/Api/Data/PigeonWatch.Data.csproj --startup-project PigeonWatch/Api/WebApi.Host/PigeonWatch.WebApi.Host.csproj`

#### Manual Verification:

- Migration applied to LocalDB creates `SIGHTING_THREAD` and `SIGHTING_MESSAGE` with the unique index and Restrict FKs

**Implementation Note**: After completing this phase and all automated verification passes, pause here for manual confirmation from the human that the manual testing was successful before proceeding to the next phase.

---

## Phase 2: Messaging API

### Overview

Service, controllers, models, client configuration and the `isMine` flag on sightings.

### Changes Required:

#### 1. Error codes and results

**File**: `PigeonWatch/Api/BusinessObjects/SightingMessageErrorCodes.cs`, `SightingThreadResult.cs`, `SightingThreadPageResult.cs`, `SightingMessagePageResult.cs`

**Intent**: Result types in the S-07 style without a cooldown variant.

**Contract**: codes `SightingNotFound` (reused), `ThreadNotFound`, `OwnSightingContact`, `MessageTextRequired`, `MessageTextTooLong`, `SenderNotFound`, `InvalidPage` (reused).

#### 2. Service

**File**: `PigeonWatch/Api/BusinessLogic/Services/SightingThreadService.cs` (+ interface), `PigeonWatch/Api/DependencyInjection/BusinessLogicServiceCollectionExtensions.cs`

**Intent**: Business rules in S-07 check order; time from `TimeProvider`.

**Contract**:
- `StartAsync(sightingId, userId, text)`: trim → required/too long → owner lookup (`SightingNotFound`) → caller is owner (`OwnSightingContact`) → start-or-append (`SenderNotFound` when null).
- `ReplyAsync(sightingId, threadId, userId, text)`: validate → `SightingNotFound` → `GetThreadForUserAsync` (`ThreadNotFound`) → append.
- `ListForSightingAsync(sightingId, userId, page)`, `ListForUserAsync(userId, page)`, `ListMessagesAsync(sightingId, threadId, userId, page)`: page < 1 or offset overflow → `InvalidPage`; sighting/thread checks as above.

#### 3. API models and view-model creator

**File**: `PigeonWatch/Api/WebApi/Models/` (`SightingThreadModel.cs`, `SightingThreadPageModel.cs`, `SightingMessageModel.cs`, `SightingMessagePageModel.cs`, `SightingThreadStartModel.cs`, `SightingMessageRequestModel.cs`, `MessageRulesModel.cs`), `PigeonWatch/Api/WebApi/ViewModelCreators/SightingThreadViewModelCreator.cs` (+ interface)

**Intent**: Wire shapes carrying display names and server-computed flags only.

**Contract**:
- `SightingThreadModel { id, sightingId, sightingName?, counterpartDisplayName, isOwner, lastMessageAt }`; `counterpartDisplayName` is the participant's name when the caller owns the sighting, else the owner's.
- `SightingMessageModel { id, text, senderDisplayName, createdAt, isMine }`.
- `SightingThreadStartModel { thread, message }`; page models `{ items, totalCount }`.
- `SightingMessageRequestModel { text }` with `[Required(AllowEmptyStrings = true)]` and `[MaxLength(SightingMessageRules.TextMaxLength)]`.

#### 4. Controllers

**File**: `PigeonWatch/Api/WebApi/Controllers/SightingThreadsController.cs`, `ThreadsController.cs`, `PigeonWatch/Api/WebApi/Controllers/FailureLoggingControllerBase.cs`

**Intent**: Two `[Authorize]` controllers deriving from `FailureLoggingControllerBase`, current user from the `NameIdentifier` claim inline as elsewhere.

**Contract**:
- `GET sightings/{sightingId:guid}/threads?page=` → 200 `SightingThreadPageModel`.
- `POST sightings/{sightingId:guid}/threads` → 201 `SightingThreadStartModel`, `Location` → the thread's messages.
- `GET sightings/{sightingId:guid}/threads/{threadId:guid}/messages?page=` → 200 `SightingMessagePageModel`.
- `POST sightings/{sightingId:guid}/threads/{threadId:guid}/messages` → 201 `SightingMessageModel`.
- `GET threads?page=` → 200 `SightingThreadPageModel`.
- GETs set `Cache-Control: private, no-store`. `SightingNotFound`/`ThreadNotFound` → 404 (Information), `SenderNotFound` → 401, `OwnSightingContact` and `InvalidPage` → 400 logged at Warning, text validation → 400 unlogged. Add a `NotFoundForThread` helper to the base class if the existing helpers do not fit.

#### 5. `isMine` on sightings

**File**: `PigeonWatch/Api/BusinessObjects/Sighting.cs`, `PigeonWatch/Api/Data/Mappers/SightingMapper.cs`, `PigeonWatch/Api/WebApi/Models/SightingModel.cs`, `PigeonWatch/Api/WebApi/ViewModelCreators/SightingViewModelCreator.cs` (+ interface), `PigeonWatch/Api/WebApi/Controllers/SightingsController.cs`

**Intent**: Let the SPA hide "Contact owner" on the caller's own sighting and choose the owner view, without exposing the reporter id.

**Contract**: `Sighting` gains `ReporterId`; `SightingModel` gains `required bool IsMine`; `Create`, `CreateList` and `CreatePage` take `currentUserId`; all five actions resolve the user (`Get` and the list/page actions gain the claim parse and `UnauthorizedForInvalidIdentity()` path).

#### 6. Client configuration

**File**: `PigeonWatch/Api/BusinessObjects/ClientConfiguration.cs`, `PigeonWatch/Api/BusinessLogic/Services/ClientConfigurationService.cs`, `PigeonWatch/Api/WebApi/Models/ClientConfigurationModel.cs`, `PigeonWatch/Api/WebApi/ViewModelCreators/ClientConfigurationViewModelCreator.cs`

**Intent**: Serve `messageRules { textMaxLength, pageSize }` through `GET configuration/client`, as `commentRules` does.

#### 7. Unit tests

**File**: `PigeonWatch/Api/UnitTests/SightingThreadServiceTests.cs`, `SightingThreadsControllerTests.cs`, `ThreadsControllerTests.cs`, `SightingThreadViewModelCreatorTests.cs`, updated `SightingViewModelCreatorTests`/`SightingsControllerTests`, `ConfigurationTests`

**Intent**: Check order, status mapping, logging levels, counterpart selection, `isMine`/`isOwner`, and no id/email/audit fields on any model.

### Success Criteria:

#### Automated Verification:

- API builds with style rules: `dotnet build PigeonWatch/Api/PigeonWatchApi.slnx`
- Architecture tests pass, including controller action shape: `dotnet test PigeonWatch/Api/ArchitectureTests/PigeonWatch.ArchitectureTests.csproj`
- Unit tests pass, including the new service, controller and view-model creator tests: `dotnet test PigeonWatch/Api/UnitTests/PigeonWatch.UnitTests.csproj`

#### Manual Verification:

- With the API running, `curl.exe` as Bob starts a thread on Alice's sighting (201) and Alice lists it via `GET threads` with Bob's display name as counterpart

**Implementation Note**: After completing this phase and all automated verification passes, pause here for manual confirmation from the human that the manual testing was successful before proceeding to the next phase.

---

## Phase 3: Privacy and access contract tests

### Overview

Lock the privacy and participant guarantees on the real HTTP pipeline before any UI exists.

### Changes Required:

#### 1. Fixtures and sweep world

**File**: `PigeonWatch/Api/IntegrationTests/EndpointFixtureRegistry.cs`, `FixtureContext.cs`, `PrivacySweepTests.cs`, `TestUser.cs`, `PigeonWatchApiFactory.cs`

**Intent**: Every new route gets fixtures; the sweep sees foreign message content from both sides.

**Contract**: `FixtureContext` gains `ThreadId`; `SeedWorldAsync` adds Bob starting a thread on Alice's sighting and Alice replying; `TestUser` gains `StartThreadAsync`/`ReplyAsync`; the factory gains audit readers for `SIGHTING_THREAD` and `SIGHTING_MESSAGE`, used by the audit-column test (`PrivacySweepTests.cs:129-160`).

#### 2. Contract tests

**File**: `PigeonWatch/Api/IntegrationTests/SightingThreadContractTests.cs`

**Intent**: Alice (owner), Bob (participant), Carol (outsider) scenarios.

**Contract**: covers 201 + `Location`; second start by Bob reuses his thread; Alice starting on her own sighting → 400 `OwnSightingContact`; Carol reading, replying or listing Bob's thread → 404 and nothing written; Carol's `GET sightings/{id}/threads` is empty; forged `senderId`/`participantId`/`email` body fields ignored; unknown sighting → 404; whitespace text → 400; 1001 chars → 400; paging newest first with `InvalidPage` on 0; `isMine` on `GET sightings/{id}` true for Alice, false for Bob; no response contains `senderId`, `participantId`, `ownerId`, `createUser`, `updateUser`, `email`, either user's Guid (D and N formats) or either email.

#### 3. Test-plan §7

**File**: `context/foundation/test-plan.md`

**Intent**: Record the re-evaluation that §7 (`test-plan.md:213`) asked S-08 to make.

**Contract**: §7 free-text entry states that message text is also excluded: it reaches exactly one chosen recipient, so contact info in it is deliberate self-disclosure even though messages cannot be edited or deleted.

### Success Criteria:

#### Automated Verification:

- Integration tests pass, including coverage guard, anonymous 401 sweep, privacy sweep, audit columns and `SightingThreadContractTests`: `dotnet test PigeonWatch/Api/IntegrationTests/PigeonWatch.IntegrationTests.csproj`
- Unit tests still pass: `dotnet test PigeonWatch/Api/UnitTests/PigeonWatch.UnitTests.csproj`

#### Manual Verification:

- Review the §7 wording in `test-plan.md` and agree it states the product decision

**Implementation Note**: After completing this phase and all automated verification passes, pause here for manual confirmation from the human that the manual testing was successful before proceeding to the next phase.

---

## Phase 4: Sighting-detail messages panel (SPA)

### Overview

The contact action, the owner's thread list and the shared thread view on the sighting detail page.

> **Amendment 2026-11-07 (user decision during implementation, supersedes Changes Required 2–4 below):** conversations are not shown on the sighting detail page. Item 1 (types, API client, `message-errors.ts`) stands. The detail page gets only a "Message {owner}" button for non-owners (none on the caller's own sighting) that opens a `MatDialog` popup with a single text field validated from `messageRules`; sending calls `POST sightings/{id}/threads` (starts or appends to the caller's thread), closes the popup and navigates to `/messages/:sightingId/:threadId`. The in-page `sighting-messages` panel and its owner thread list are removed; the `message-thread` component moves to `features/messages/` for Phase 5. Labels, kitchen-sink cells and specs follow the popup (states: closed, open, sending, validation, failure). Progress 4.3 is verified as "Bob sends from the popup and lands in the conversation; no button on Alice's own sighting" (Alice's reply moves to 5.3); 4.4 as "popup usable at phone width".

### Changes Required:

#### 1. Types, API client and failure mapping

**File**: `PigeonWatch/Frontend/src/app/features/sightings/sighting.types.ts`, `sightings-api.ts`, new `message-errors.ts` (+ spec), `PigeonWatch/Frontend/src/app/core/configuration/configuration.service.ts`

**Intent**: Typed calls for the four sighting-scoped endpoints, `isMine` on `Sighting`, `messageRules` with a local fallback, and a failure union like `comment-errors.ts`.

**Contract**: failure kinds `notFound`, `ownSighting`, `textRequired`, `unexpected`; reportable = `ownSighting` (UI should prevent it) and unexpected non-expected statuses; contexts `sighting.threads.load`, `sighting.messages.load`, `sighting.messages.send`, `sighting.threads.start`.

#### 2. Components

**File**: `PigeonWatch/Frontend/src/app/features/sightings/sighting-detail-page/sighting-messages/` (panel) and `.../sighting-messages/message-thread/` (thread view), `sighting-detail-page.html`/`.ts`

**Intent**: A child panel with its own load stream, so the detail page never waits on messages.

**Contract**:
- Non-owner: no thread → "Contact {owner}" compose box; on send shows the thread. Existing thread → the thread view.
- Owner: list of threads on this sighting (counterpart name, last activity, "Show more" paging); selecting one shows the thread view; `?thread=<id>` preselects; no threads → empty state.
- Thread view: newest first, 20 per page, "Show older", reply box at the top with signal-form validation from `messageRules`, local prepend on send, id dedupe, `app-error-reference` for reportable failures, plain interpolation only.

#### 3. Labels and kitchen sink

**File**: `PigeonWatch/Api/BusinessObjects/Resources/UiLabels.resx`, kitchen-sink page

**Intent**: English keys under `sighting.messages.*`; kitchen-sink cells for empty, loading, error, owner list and thread states.

#### 4. Specs

**File**: specs next to each new component and `message-errors.spec.ts`; scoped updates to `sighting-detail-page` specs

**Intent**: Owner vs non-owner rendering, start/reply flows, paging, validation, reporting only for reportable failures (pattern: `sighting-comments-reporting.spec.ts`).

### Success Criteria:

#### Automated Verification:

- SPA unit tests pass, including the new specs, label coverage and style-token coverage: `npm test -- --watch=false` (in `PigeonWatch/Frontend`)
- Production build passes within the bundle budget: `npm run build` (in `PigeonWatch/Frontend`)

#### Manual Verification:

- As Bob on Alice's sighting: contact, send, see the message; as Alice on her sighting: see Bob's thread and reply; no contact action on Alice's own sighting
- Panel layout is usable at phone width

**Implementation Note**: After completing this phase and all automated verification passes, pause here for manual confirmation from the human that the manual testing was successful before proceeding to the next phase.

---

## Phase 5: Messages inbox page (SPA)

### Overview

The `/messages` page and its user-menu entry.

> **Amendment 2026-11-07 (user decision during implementation, supersedes item 1's Intent below):** `/messages` is a dedicated chat page. A left sidebar lists all the caller's threads from `GET threads` (sighting name with label fallback, counterpart display name, last activity, most recent first, "Show more" paging). Selecting one opens it in the main pane at `/messages/:sightingId/:threadId` (deep-linkable, no API change). The pane is a chat: messages as colored bubbles, the caller's (`isMine`) on the right in one color, the counterpart's on the left in another, in chat order (oldest at top, newest at the bottom next to the reply box; the API stays newest-first and the SPA reverses for display; "Show older" at the top loads the next 20). The pane header shows the sighting name linking to `/sightings/:id` and the counterpart name. No thread selected → empty-pane prompt. At phone width the sidebar and pane stack: list first, then the chat with a back control. Bubble colors are new design tokens. Progress 5.3 is verified as "Alice opens Bob's thread from the sidebar and replies; Bob sees it on reload".

### Changes Required:

> **Amendment 2026-11-08 (user decision during manual verification):** (a) On a sighting where the caller already has a thread, the email icon on the detail page opens `/messages/:sightingId/:threadId` instead of the popup; the detail page loads the caller's own thread with `GET sightings/{id}/threads` in its own stream (non-owners only), and a failed lookup keeps the popup path (start-or-append is safe) and is reported per `message-errors.ts`. (b) The messages page gets a Refresh button that reloads the sidebar from page 1 and the open thread's first page without reloading the page. (c) The Messages user-menu item is removed; an icon button with the same email icon sits next to the user menu in the header, linking to `/messages` with `header.messages` as its accessible name (the unread count is Phase 6). The email icon is shared, so it is promoted to one component and imported by file path from the initial bundle. Progress 5.4 is verified as "the header email button sits well beside the user menu and works from every page". The page card uses the new `full` width so the sidebar and chat sit side by side below `xl`.

#### 1. Route, menu and page

**File**: `PigeonWatch/Frontend/src/app/app.routes.ts`, `PigeonWatch/Frontend/src/app/app.html`, new `PigeonWatch/Frontend/src/app/features/messages/messages-page/`, `sightings-api.ts` (or a messages API file)

**Intent**: A lazy, `authGuard`-protected `/messages` page listing `GET threads` results most recent first, 20 per page; each row shows sighting name (label fallback when unnamed), counterpart name, whether the caller owns the sighting, last activity, and links to `/sightings/:id?thread=<threadId>`.

**Contract**: menu item `<a mat-menu-item routerLink="/messages">` with key `header.messages`; context `messages.inbox.load`; empty state when there are no threads; initial-bundle imports by file path only.

#### 2. Specs, labels and docs number

**File**: page spec, `UiLabels.resx`, `PigeonWatch/Frontend/CLAUDE.md`

**Intent**: Rendering, paging, empty and error states; record the new initial bundle size in `Frontend/CLAUDE.md:33`.

### Success Criteria:

#### Automated Verification:

- SPA unit tests pass: `npm test -- --watch=false` (in `PigeonWatch/Frontend`)
- Production build passes within the bundle budget: `npm run build` (in `PigeonWatch/Frontend`)

#### Manual Verification:

- Alice and Bob each see the thread on `/messages`; clicking it opens the sighting with the thread shown
- Menu item reads well next to Profile and works from every page

**Implementation Note**: After completing this phase and all automated verification passes, pause here for manual confirmation from the human that the manual testing was successful before proceeding to the next phase.

---

## Phase 6: Read state and unread badge

> Added 2026-11-08 by user decision during Phase 5 manual verification; amends FR-013 ("no read/unread tracking") for S-08.

> **Amendment 2026-11-08 (user decision during Phase 6 manual verification):** the header badge counts unread **messages**, not threads: `GET threads/unread-count` returns the number of messages from the other side newer than the caller's marker, across all the caller's threads (`CountUnreadMessagesAsync`). `hasUnread` per thread stays. The messages page keeps its sidebar in step with the count: whenever the unread count changes, it re-fetches the first inbox page in the background (no loading state, selection and paging kept) and merges each returned thread's `hasUnread`, `lastMessageAt` and order into the list.

### Overview

Each participant has a per-thread read marker. A thread is unread for a user when it holds a message from the other person newer than that user's marker. The sidebar marks unread threads and the header email button shows the number of unread threads.

### Changes Required:

#### 1. Data model

**File**: `SightingThreadEntity.cs`, `PigeonWatchDbContext.cs`, new migration `AddSightingThreadReadMarkers`, `SightingThread.cs`, `SightingThreadMapper.cs`, `SightingThreadRepository.cs` (+ interface)

**Contract**: `SIGHTING_THREAD` gains nullable `OWNER_LAST_READ_AT` and `PARTICIPANT_LAST_READ_AT` (UTC). Sending a message sets the sender's own marker to the message time, so your own messages never make a thread unread for you. `MarkReadAsync(sightingId, threadId, userId, now)` sets the caller's side, scoped like `GetThreadForUserAsync`. `CountUnreadThreadsAsync(userId)` counts threads where the caller is owner or participant and the latest message from the other side is newer than the caller's marker (null marker = never read). Thread reads carry `HasUnread` for the caller.

#### 2. API

**File**: service, view-model creator, `SightingThreadModel.cs`, `SightingThreadsController.cs`, `ThreadsController.cs`

**Contract**: `SightingThreadModel` gains `hasUnread`. `PUT sightings/{sightingId:guid}/threads/{threadId:guid}/read` returns 204; non-participant or unknown returns 404 (Information), same scoping as replies. `GET threads/unread-count` returns 200 `{ count }` with `Cache-Control: private, no-store`. Unit tests for service, controllers and repository (on SQLite) cover own-message-not-unread, marker scoping, the outsider 404 and the count.

#### 3. Contract tests

**File**: `EndpointFixtureRegistry.cs`, `SightingThreadContractTests.cs`

**Contract**: fixtures for both new routes (coverage guard, anonymous 401, privacy sweep); Bob's message is unread for Alice until she marks it read; Carol cannot mark Bob's thread (404, nothing written); the count never includes another user's threads.

#### 4. SPA

**File**: `sightings-api.ts`, `sighting.types.ts`, `messages-page`, `message-thread`, `app.html`/`app.ts`, a root unread-count service under `core/` (or `features/messages`, imported by file path)

**Contract**: opening a thread marks it read after its first page loads (PUT), then clears the sidebar mark and refreshes the count. Unread rows show a dot plus bold text and a visually hidden "unread" label (never colour alone). The header email button shows the count as a badge when it is above 0 and nothing at 0, with an accessible name that includes the count. The count is fetched after sign-in, on route changes (throttled), after a thread is marked read and on Refresh; no timer. Failures report under `messages.unread.count` and `messages.thread.read` and leave the badge as it was. Labels, kitchen-sink cells (unread rows, badge with a count) and specs follow. PRD FR-013 is amended to allow read/unread tracking.

### Success Criteria:

#### Automated Verification:

- API builds with style rules: `dotnet build PigeonWatch/Api/PigeonWatchApi.slnx`
- Migration matches the model: `dotnet ef migrations has-pending-model-changes --project PigeonWatch/Api/Data/PigeonWatch.Data.csproj --startup-project PigeonWatch/Api/WebApi.Host/PigeonWatch.WebApi.Host.csproj`
- All .NET tests pass, including architecture, unit and integration: `dotnet test PigeonWatch/Api/PigeonWatchApi.slnx`
- SPA unit tests pass: `npm test -- --watch=false` (in `PigeonWatch/Frontend`)
- Production build passes within the bundle budget: `npm run build` (in `PigeonWatch/Frontend`)

#### Manual Verification:

- Bob messages Alice; Alice sees the badge and the unread mark, opens the thread, and both clear
- No badge when nothing is unread

**Implementation Note**: After completing this phase and all automated verification passes, pause here for manual confirmation from the human that the manual testing was successful before proceeding to the next phase.

---

## Phase 7: Two-user E2E and docs

### Overview

One browser-level proof of the two-person flow, and the rules files updated.

### Changes Required:

#### 1. Playwright spec

**File**: `PigeonWatch/Frontend/tests/e2e/sighting-messages.spec.ts`

**Intent**: Two fresh users in two browser contexts with empty `storageState` (pattern: `sighting-comments.spec.ts:3-7`): owner reports a sighting, contacter sends a message, owner finds it on `/messages` (header badge shows 1, thread marked unread), opens it and replies (badge clears), contacter reloads and sees the reply marked unread.

#### 2. Docs

**File**: `PigeonWatch/CLAUDE.md`, `PigeonWatch/Api/CLAUDE.md`, `PigeonWatch/Frontend/CLAUDE.md`

**Intent**: Remove "messaging does not exist yet" (`PigeonWatch/CLAUDE.md:9`), add the thread endpoints and participant rule to the API surface notes, and note the messages panel and inbox conventions.

### Success Criteria:

#### Automated Verification:

- E2E suite passes, including `sighting-messages.spec.ts`: `npx playwright test` (in `PigeonWatch/Frontend`)
- SPA unit tests still pass: `npm test -- --watch=false` (in `PigeonWatch/Frontend`)
- All .NET tests still pass: `dotnet test PigeonWatch/Api/PigeonWatchApi.slnx`

#### Manual Verification:

- Docs read correctly and no longer claim messaging is missing

**Implementation Note**: After completing this phase and all automated verification passes, pause here for manual confirmation from the human that the manual testing was successful before proceeding to the next phase.

---

## Addendum 2026-11-08: UI additions accepted during Phases 5-6

Recorded after the full implementation review (F5). Each item shipped and was accepted during that phase's manual verification, but was not in a phase block or an amendment:

- **Header icon buttons**: Report sighting moved out of the account menu into a header icon button beside Messages, and the account trigger became an icon button with the display name as the menu's first, non-interactive row (`app.html`, `_material-theme.scss`, `_tokens.scss`).
- **`appTooltip` directive** (`shared/ui/tooltip/`): the app's one tooltip, a body-level popover because the header clips overflow and `MatTooltip` would add about 16 kB to the initial bundle; it also replaced `title` attributes on sighting rows and comments so every tooltip is identical.
- **`MessageThreadDialog`** (`features/messages/message-thread-dialog/`): a full-screen popup of the thread view, opened from the messages page.
- **Bundle warning budget** raised from 540 kB to 550 kB in `angular.json` for the header buttons and the tooltip (measured 544.69 kB after Phase 6).

---

## Testing Strategy

### Unit Tests:

- Repository scoping (owner/participant/outsider), ordering, unique pair, paging.
- Service check order and error codes; controller status codes and log levels; counterpart and flag computation.

### Integration Tests:

- Alice/Bob/Carol contract tests, privacy sweep with a seeded two-user thread, coverage and anonymous-401 guards, audit columns for both new tables.

### Manual Testing Steps:

1. Two browsers: Alice reports a sighting; Bob opens it and contacts Alice.
2. Alice opens `/messages`, opens the thread and replies; Bob reloads the sighting and sees the reply.
3. Carol opens the same sighting and sees only her own empty contact box, not Bob's thread.
4. Alice's own sighting shows the thread list, never a contact box.

## Performance Considerations

Inbox and per-sighting lists are indexed on `(PARTICIPANT_ID, LAST_MESSAGE_AT)` and `(SIGHTING_ID, PARTICIPANT_ID)`; the owner side of `ListThreadsForUserAsync` joins `SIGHTING.REPORTER_ID`, acceptable at MVP volume. The initial bundle gains only a menu item and a lazy route; Phase 5 measures it.

## Migration Notes

Additive migration only (two new tables); no data backfill. `SightingModel.isMine` is a new required field; the SPA is deployed with the API.

## References

- Related research: `context/changes/sighting-message-threads/research.md`
- Similar implementation: `PigeonWatch/Api/WebApi/Controllers/SightingCommentsController.cs:12-14`, `PigeonWatch/Api/BusinessLogic/Services/SightingCommentService.cs:30-38`
- Prior plan: `context/archive/2026-11-07-comment-on-sighting/plan.md`
- Requirements: `context/foundation/prd.md:96-99`, `context/foundation/roadmap.md:186-196`

## Progress

> Convention: `- [ ]` pending, `- [x]` done. Append ` — <commit sha>` when a step lands. Do not rename step titles. See `references/progress-format.md`.

### Phase 1: Thread and message data model

#### Automated

- [x] 1.1 API builds with style rules — da04df4
- [x] 1.2 Architecture tests pass, including table naming and service registration — da04df4
- [x] 1.3 Unit tests pass, including SightingThreadRepositoryTests — da04df4
- [x] 1.4 Migration matches the model — da04df4

#### Manual

- [x] 1.5 Migration applied to LocalDB creates SIGHTING_THREAD and SIGHTING_MESSAGE with the unique index and Restrict FKs — da04df4

### Phase 2: Messaging API

#### Automated

- [x] 2.1 API builds with style rules — 3a7cf0f
- [x] 2.2 Architecture tests pass, including controller action shape — 3a7cf0f
- [x] 2.3 Unit tests pass, including the new service, controller and view-model creator tests — 3a7cf0f

#### Manual

- [x] 2.4 Bob starts a thread on Alice's sighting via curl.exe and Alice lists it via GET threads — 3a7cf0f

### Phase 3: Privacy and access contract tests

#### Automated

- [x] 3.1 Integration tests pass, including coverage guard, anonymous 401 sweep, privacy sweep, audit columns and SightingThreadContractTests — 8ac4806
- [x] 3.2 Unit tests still pass — 8ac4806

#### Manual

- [x] 3.3 Review the §7 wording in test-plan.md — 8ac4806

### Phase 4: Sighting-detail messages panel (SPA)

#### Automated

- [x] 4.1 SPA unit tests pass, including the new specs, label coverage and style-token coverage — 512b599
- [x] 4.2 Production build passes within the bundle budget — 512b599

#### Manual

- [x] 4.3 Bob contacts Alice and Alice replies on the detail page; no contact action on own sighting — 512b599
- [x] 4.4 Panel layout is usable at phone width — 512b599

### Phase 5: Messages inbox page (SPA)

#### Automated

- [x] 5.1 SPA unit tests pass — 6af9f69
- [x] 5.2 Production build passes within the bundle budget — 6af9f69

#### Manual

- [x] 5.3 Both users see the thread on /messages and it opens on the sighting — 6af9f69
- [x] 5.4 Menu item reads well next to Profile and works from every page — 6af9f69

### Phase 6: Read state and unread badge

#### Automated

- [x] 6.1 API builds with style rules — bb72edf
- [x] 6.2 Migration matches the model — bb72edf
- [x] 6.3 All .NET tests pass, including architecture, unit and integration — bb72edf
- [x] 6.4 SPA unit tests pass — bb72edf
- [x] 6.5 Production build passes within the bundle budget — bb72edf

#### Manual

- [x] 6.6 Bob messages Alice; badge and unread mark appear and clear when Alice opens the thread — bb72edf
- [x] 6.7 No badge when nothing is unread — bb72edf

### Phase 7: Two-user E2E and docs

#### Automated

- [x] 7.1 E2E suite passes, including sighting-messages.spec.ts — 8ae5364
- [x] 7.2 SPA unit tests still pass — 8ae5364
- [x] 7.3 All .NET tests still pass — 8ae5364

#### Manual

- [x] 7.4 Docs read correctly and no longer claim messaging is missing — 8ae5364
