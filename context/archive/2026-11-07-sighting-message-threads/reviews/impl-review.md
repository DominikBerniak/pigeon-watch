<!-- IMPL-REVIEW-REPORT -->
# Implementation Review: Sighting Message Threads (S-08)

- **Plan**: context/changes/sighting-message-threads/plan.md
- **Scope**: Full plan
- **Reviewed phases**: 1, 2, 3, 4, 5, 6, 7
- **Date**: 2026-11-08
- **Verdict**: NEEDS ATTENTION
- **Findings**: 0 critical, 5 warnings, 5 observations

## Verdicts

| Dimension | Verdict |
|-----------|---------|
| Plan Adherence | PASS |
| Scope Discipline | WARNING |
| Safety & Quality | WARNING |
| Architecture | PASS |
| Pattern Consistency | WARNING |
| Success Criteria | PASS |

Success criteria evidence (2026-11-08): `dotnet build` succeeded; `dotnet ef migrations has-pending-model-changes` reports no changes; `dotnet test` 567 unit + 44 architecture + 177 integration passed; `npm test -- --watch=false` 1104 passed; `npm run build` initial total 544.69 kB (under the 550 kB warning); `npx playwright test` 8 passed. All manual items are checked, each confirmed by the user during its phase.

## Findings

### F1 — Reply draft and in-flight send leak across thread switches

- **Severity**: ⚠️ WARNING
- **Impact**: 🔎 MEDIUM — real tradeoff; pause to reason through it
- **Dimension**: Safety & Quality
- **Location**: PigeonWatch/Frontend/src/app/features/messages/message-thread/message-thread.ts:274-345
- **Detail**: The messages page reuses one `app-message-thread` instance and only changes its `sightingId`/`threadId` inputs. `resetThread()` clears the items, paging and errors, but not `replyModel`, the reply form or `submitState`. A draft typed to person A is still in the box after switching to person B, and Enter sends it to B. Also, `send()`'s `next` handler adds the returned message to whatever thread is loaded when the response arrives, so a reply sent to A can appear in B's list and bump B's `totalCount`.
- **Fix**: In `resetThread()`, reset `replyModel`, the reply form and `submitState`, and cancel or ignore an in-flight send: keep its subscription and unsubscribe it, or compare the thread key captured at send time inside `next`. Add a spec that switches threads with a draft typed and with a send pending.
  - Strength: Closes a real wrong-recipient path in a private-messaging feature; the change stays inside one component.
  - Tradeoff: Cancelling an in-flight send means the user may not see their message until they reopen thread A. The server still stores it.
  - Confidence: HIGH — confirmed in the code: `resetThread` has no reply reset, and the `next` handler checks only `state().kind`.
  - Blind spot: Whether the full-screen `MessageThreadDialog` has the same reuse pattern.
- **Decision**: FIXED — `resetThread(key)` clears the draft, form and submit state and unsubscribes a pending send only when the thread key changes (same-thread reloads keep the draft); the send subscription is also released on destroy. Three specs added in message-thread.spec.ts; the draft and in-flight specs went red on the old component.

### F2 — Read marker uses server time, not the newest message seen

- **Severity**: ⚠️ WARNING
- **Impact**: 🔎 MEDIUM — real tradeoff; pause to reason through it
- **Dimension**: Safety & Quality
- **Location**: PigeonWatch/Api/Data/Repositories/SightingThreadRepository.cs:113,163; PigeonWatch/Frontend/src/app/features/messages/message-thread/message-thread.ts:355
- **Detail**: `MarkReadAsync` sets the caller's marker to `now`. The SPA sends the PUT after page 1 arrives, so a message from the other side created between that GET and the PUT counts as read but was never shown. A reply also moves the replier's marker to `now`, which silently marks messages that arrived since the last load as read.
- **Fix A ⭐ Recommended**: Send the newest seen message's `createdAt` with `PUT .../read`, store `max(existing, seenAt)` capped at `now`, and on reply move the marker only up to the reply time when the client had already seen the latest message.
  - Strength: The unread badge stays honest. That is the point of the Phase 6 amendment.
  - Tradeoff: Touches the API contract (a request body on a 204 endpoint), the service, the contract tests and the SPA.
  - Confidence: MED — the window is small (milliseconds to seconds) at MVP traffic.
  - Blind spot: How the inbox sync merges a thread that becomes unread again after it was marked.
- **Fix B**: Accept and document it as a known limitation in Api/CLAUDE.md "Read state".
  - Strength: No code churn; the race needs two people typing within seconds of each other.
  - Tradeoff: A message can occasionally be missed without any signal.
  - Confidence: HIGH — trivial to do.
  - Blind spot: How often both users have the chat open at once.
- **Decision**: FIXED (Fix A, refined) — `PUT .../read` takes `{ seenAt }` (`SightingThreadReadRequestModel`, missing value is a 400); the marker becomes max(existing, min(seenAt, now)); sending no longer moves the sender's marker (own messages are already excluded from unread), so a reply never silently reads the other side's messages. SPA sends the newest shown message's `createdAt`. Repository, service, controller, contract and SPA specs updated; the break-check (marker back to `now`) turned 5 tests red. Docs updated in Api/CLAUDE.md and Frontend/CLAUDE.md.

### F3 — Unread dot can stay hidden after a later message in a thread opened earlier

- **Severity**: ⚠️ WARNING
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Safety & Quality
- **Location**: PigeonWatch/Frontend/src/app/features/messages/messages-page/messages-page.ts:287-292
- **Detail**: `withReadMarks` forces `hasUnread = false` for every thread in `readSinceInboxRequest`, even when a background sync that runs after the mark-read returns `hasUnread: true`. Open thread X, the other person writes again, the header badge goes up and the sync refetches the inbox, but X's dot stays hidden until a manual Refresh.
- **Fix**: Record when each thread was marked read and override only rows whose `lastMessageAt` is not newer than that time, or clear the set entry once a sync started after the mark-read has merged.
- **Decision**: FIXED — inbox requests are numbered; a local read mark is dropped once a response from a request started after the mark arrives, so later server state wins and a stale response still cannot undo the mark. Two specs added; the existing sync spec's assertion that the open thread stays read was updated to the new requirement (server wins). Break-check: two specs red on the old code.

### F4 — Inbox list and unread count cannot seek by user on the owner side

- **Severity**: ⚠️ WARNING
- **Impact**: 🔬 HIGH — architectural stakes; think carefully before deciding
- **Dimension**: Safety & Quality
- **Location**: PigeonWatch/Api/Data/Repositories/SightingThreadRepository.cs:129-135,176-187
- **Detail**: `ListThreadsForUserAsync` filters `ParticipantId == userId || Sighting.ReporterId == userId`, an OR across a join. Only the participant side has an index. `CountUnreadMessagesAsync` starts from `SIGHTING_MESSAGE` with no predicate that can seek by user, so its cost grows with the total number of messages, and it runs at sign-in, after every mark-read and on throttled route changes. The plan accepted the owner-side join for the inbox at MVP volume, but not a count that scans every message.
- **Fix A ⭐ Recommended**: Rewrite both queries as two indexed paths (`ParticipantId == userId` via `(PARTICIPANT_ID, LAST_MESSAGE_AT)`, plus threads on the caller's sightings via `SIGHTING.REPORTER_ID`), combined with `Concat`, with the count joined to messages on `(THREAD_ID, CREATE_DATE)`.
  - Strength: No schema change; the existing repository tests on SQLite pin the behaviour.
  - Tradeoff: More complex LINQ, and the generated SQL needs checking on SQL Server.
  - Confidence: MED — EF's `Concat` translation to `UNION ALL` with ordering and paging needs verifying.
  - Blind spot: No query plans have been captured on LocalDB.
- **Fix B**: Denormalise `OWNER_ID` onto `SIGHTING_THREAD` with an `(OWNER_ID, LAST_MESSAGE_AT)` index (additive migration, backfill from `SIGHTING.REPORTER_ID`).
  - Strength: Simple, indexable predicates on both sides.
  - Tradeoff: Duplicates the owner the plan deliberately derives from the sighting, so the "owner never stored" rule in Api/CLAUDE.md changes.
  - Confidence: MED — straightforward, but it changes a stated design invariant.
  - Blind spot: S-11 (admin threads) may want a different shape.
- **Decision**: FIXED (Fix A) — inbox and unread count use two indexed paths: participant threads (`PARTICIPANT_ID = @user`) and threads on the caller's sightings (`SIGHTING.REPORTER_ID = @user` joined on `SIGHTING_ID`, excluding `PARTICIPANT_ID = @user`), combined with `Concat` (UNION ALL) for the inbox and as two `CountAsync` queries joined to `SIGHTING_MESSAGE` on `THREAD_ID` for the count. SQL Server translation checked with `ToQueryString`; execution plans not captured. Two repository tests added (paging across both roles, count across both roles).

### F5 — Unplanned UI scope: header redesign, tooltip directive, full-screen dialog, raised budget

- **Severity**: ⚠️ WARNING
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Scope Discipline
- **Location**: PigeonWatch/Frontend/src/app/app.html; shared/ui/tooltip/tooltip.ts; features/messages/message-thread-dialog/; angular.json:77
- **Detail**: No plan amendment covers these: Report sighting and the account menu became header icon buttons, a new `appTooltip` directive replaces `title` attributes in unrelated features (sighting row, comments), `MessageThreadDialog` adds a full-screen chat, and the bundle warning budget rose from 540 kB to 550 kB. All of them are documented in Frontend/CLAUDE.md and were accepted during the phases' manual checks, but the plan of record does not mention them.
- **Fix**: Add a dated addendum to plan.md listing these four additions so the archived plan matches what shipped.
- **Decision**: FIXED — dated addendum "UI additions accepted during Phases 5-6" added to plan.md before Testing Strategy.

### F6 — PRD FR-013 says "unread-thread count"; the badge counts messages

- **Severity**: 💡 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Plan Adherence
- **Location**: context/foundation/prd.md:98
- **Detail**: The amended FR-013 wording predates the Phase 6 amendment that switched the badge to unread messages.
- **Fix**: Change "an unread-thread count" to "an unread-message count".
- **Decision**: FIXED — FR-013 now reads "an unread-message count".

### F7 — A sighting removed mid-request answers 401 instead of 404

- **Severity**: 💡 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Safety & Quality
- **Location**: PigeonWatch/Api/Data/Repositories/SightingThreadRepository.cs:61; BusinessLogic/Services/SightingThreadService.cs:26
- **Detail**: `StartOrAppendAsync` returns null both when the participant row is missing and when the sighting is missing, and the service maps null to `SenderNotFound` (401). The service checks the sighting first, so only a delete in between hits this path, and no delete path exists today.
- **Fix**: Return a result that tells the two cases apart, and map a missing sighting to `SightingNotFound`.
- **Decision**: FIXED — when `StartOrAppendAsync` returns null the service checks the sighting again: gone → `SightingNotFound` (404), otherwise `SenderNotFound` (401). Service test added; break-check red.

### F8 — ThreadsController.List builds its failure inline; MarkRead declares a placeholder model

- **Severity**: 💡 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Pattern Consistency
- **Location**: PigeonWatch/Api/WebApi/Controllers/ThreadsController.cs:37-43; SightingThreadsController.cs (MarkRead); WebApi/Models/SightingThreadReadModel.cs
- **Detail**: `List` turns every failure into a 400 inline instead of using the shared `GetFailure` mapping its siblings use, so a future not-found or sender code would become a 400. `MarkRead` returns `ActionResult<SightingThreadReadModel>` but always returns 204; the empty model exists to satisfy the action-shape architecture test.
- **Fix**: Route `List` through the same failure mapping as `SightingThreadsController`; keep the placeholder model, since `ControllerActionTests` requires `ActionResult<T>`.
- **Decision**: FIXED — `ThreadsController.List` goes through a private `GetFailure` (SenderNotFound → 401, logged codes → Warning, else 400) like its sibling; unit test for the 401 added. Placeholder `SightingThreadReadModel` kept for `ControllerActionTests`.

### F9 — Type and build hygiene: MessageContext union and EF1002 warning

- **Severity**: 💡 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Pattern Consistency
- **Location**: PigeonWatch/Frontend/src/app/features/sightings/message-errors.ts:16-22; PigeonWatch/Api/UnitTests/SightingThreadRepositoryTests.cs:630
- **Detail**: `MessageContext` lacks `messages.inbox.sync` and `messages.unread.count`, which are reported as separate literals. The repository test helper `CountAsync` uses `SqlQueryRaw` with an interpolated table name, the only new build warning on the branch (EF1002); the table names are constants.
- **Fix**: Add the two contexts to the union and use them at the call sites; replace the helper with typed `db.Set<…>().CountAsync()` calls or a `switch` over constant SQL.
- **Decision**: FIXED — `MessageContext` now includes `messages.inbox.sync` and `messages.unread.count` (the messages page uses `satisfies MessageContext`; `core/` keeps its literal because core must not import features); the raw-SQL `CountAsync` helper replaced by typed `DbSet.CountAsync`, so the EF1002 warning is gone.

### F10 — Loading messages makes five database round trips

- **Severity**: 💡 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Safety & Quality
- **Location**: PigeonWatch/Api/BusinessLogic/Services/SightingThreadService.cs:84-112; Data/Repositories/SightingThreadRepository.cs:37
- **Detail**: Loading messages runs a sighting-exists check, then `GetThreadForUserAsync` (includes plus the unread subquery), then a count, then the page. The thread projection is thrown away, and the repository's `ListMessagesAsync` already scopes by participant or owner.
- **Fix**: Use a cheap `AnyAsync` participant-or-owner check for the authorisation step instead of the full thread projection.
- **Decision**: FIXED — `ListMessagesAsync` uses the new `IsThreadVisibleToUserAsync` (`AnyAsync`, same scoping) instead of loading the thread projection. Repository and service tests added; break-check red.
