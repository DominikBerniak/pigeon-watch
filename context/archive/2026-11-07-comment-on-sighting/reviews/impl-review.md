<!-- IMPL-REVIEW-REPORT -->
# Implementation Review: Comment on a Sighting

- **Plan**: context/changes/comment-on-sighting/plan.md
- **Scope**: Full plan
- **Reviewed phases**: 1, 2, 3, 4
- **Date**: 2026-11-07
- **Verdict**: NEEDS ATTENTION
- **Findings**: 0 critical, 3 warnings, 4 observations

## Verdicts

| Dimension | Verdict |
|-----------|---------|
| Plan Adherence | WARNING |
| Scope Discipline | PASS |
| Safety & Quality | WARNING |
| Architecture | PASS |
| Pattern Consistency | WARNING |
| Success Criteria | PASS |

Success criteria re-run on HEAD (6f72f1a): API build OK, architecture 44/44, unit 461/461, integration 163/163, no pending model changes; SPA 923/923 and E2E 7/7 at the Phase 4 gate. All 22 Progress rows carry a SHA; manual rows were confirmed by the user in-session.

User-approved additions beyond the plan (not findings): collapsible comments section hidden by default, textarea max-height, `form[appFormSubmitScope]` + `FormSubmitState.reset()`, `DeleteCommentDialog`, 6 extra resx keys (incl. `collapse`/`expand`), `[Required(AllowEmptyStrings = true)]`, `NotFoundForComment`/`ForbiddenForNonAuthor`, `int? page`, `SightingExistsAsync` on the comment repository, `SightingService` taking `ISubmissionCooldownProvider`. Benign unplanned extra: `ListAsync` page-overflow guard (`SightingCommentService.cs:16-17`, tested).

## Findings

### F1 — Comment writes are not guarded by sighting, author or deleted state

- **Severity**: ⚠️ WARNING
- **Impact**: 🔎 MEDIUM — real tradeoff; pause to reason through it
- **Dimension**: Safety & Quality
- **Location**: PigeonWatch/Api/Data/Repositories/SightingCommentRepository.cs:53-82
- **Detail**: `UpdateTextAsync` and `SoftDeleteAsync` re-load by `commentId` only. The service checks sighting, deleted state and authorship first via `GetAsync`, so there is no IDOR today, but it is a check-then-write: an edit racing a delete (two tabs) can set `TEXT`/`EDITED_AT` on an already soft-deleted row, and any future caller that skips the service checks gets an unscoped write.
- **Fix**: Pass `sightingId` and `authorId` into both methods and load with `e.Id == commentId && e.SightingId == sightingId && e.AuthorId == authorId && e.DeletedAt == null`, returning null (→ `CommentNotFound`) when nothing matches; add a repository test for each guard.
  - Strength: Makes the write itself enforce the invariants the service checks; closes the edit-after-delete overwrite.
  - Tradeoff: Interface change touches the service, its unit-test substitutes and the repository tests.
  - Confidence: HIGH — same filter shape `GetAsync` already uses.
  - Blind spot: Still not atomic under true concurrency (two SaveChanges can interleave); acceptable at MVP like the documented cooldown race.
- **Decision**: FIXED — write queries scoped by sighting, author and DeletedAt == null; two repository guard tests (red before, green after)

### F2 — Detail-page and kitchen-sink assertions were not scoped as the plan required

- **Severity**: ⚠️ WARNING
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Plan Adherence
- **Location**: PigeonWatch/Frontend/src/app/features/sightings/sighting-detail-page/sighting-detail-page.spec.ts:200,214,223,233,272,509,589; PigeonWatch/Frontend/src/app/dev/kitchen-sink/kitchen-sink.spec.ts:~461-594
- **Detail**: Phase 3 §6–7 say the `dl > div` length 7, `querySelectorAll('a')` count, `app-alert` null and `report` count assertions are "scoped, not loosened". They were left global and pass only because the comments stub returns an empty page and the component renders no `dl`, link or alert when empty. `.sighting-detail__details app-alert` now also covers the comments section. A later comments change could make these assertions match comment elements.
- **Fix**: Scope each selector to its original area (facts `dl`, card actions, the details area outside `app-sighting-comments`, sighting report contexts) without changing expected values.
- **Decision**: FIXED — facts rows, page alerts, page links and report expectations scoped to the sighting areas and contexts via helpers; expected values unchanged

### F3 — Braces kept on single-line bodies in new TS

- **Severity**: ⚠️ WARNING
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Pattern Consistency
- **Location**: PigeonWatch/Frontend/src/app/features/sightings/comment-errors.ts:56-58; PigeonWatch/Frontend/src/app/features/sightings/sighting-detail-page/sighting-comments/sighting-comments.ts:417-419
- **Detail**: Frontend CLAUDE.md "TypeScript coding rules": a single-statement body on one line has no braces. Both new bodies keep them (copied from siblings that drift the same way).
- **Fix**: Drop the braces in the two new files.
- **Decision**: DISMISSED — false positive: both one-line forms exceed printWidth 100 (102 and 105 chars), so Prettier wraps them and the rule keeps braces on a wrapped single statement

### F4 — `CommentTextTooLong` and its Warning log are unreachable over HTTP

- **Severity**: 💡 OBSERVATION
- **Impact**: 🔎 MEDIUM — real tradeoff; pause to reason through it
- **Dimension**: Safety & Quality
- **Location**: PigeonWatch/Api/WebApi/Models/SightingCommentRequestModel.cs:9; PigeonWatch/Api/WebApi/Controllers/SightingCommentsController.cs:22-26
- **Detail**: `[MaxLength(500)]` runs at model binding on untrimmed text, so >500 characters is an unlogged model-binding 400 keyed `Text` and the service's trimmed `CommentTextTooLong` branch (in `loggedErrorCodes`) never runs from HTTP; text padded past 500 with whitespace is rejected even though it fits after trimming. The plan asked for both, and Api/CLAUDE.md documents the model-binding 400, so this is a plan inconsistency, not drift.
- **Fix A ⭐ Recommended**: Remove `CommentTextTooLong` from `loggedErrorCodes` and keep `[MaxLength]`
  - Strength: Matches the documented contract and the SPA (`maxLength` counts untrimmed text); one-line change.
  - Tradeoff: The service check stays as defence for non-HTTP callers only.
  - Confidence: HIGH — Api/CLAUDE.md already describes this behaviour.
  - Blind spot: None significant.
- **Fix B**: Drop `[MaxLength]` from the request model so the trimmed service check (and its log) applies
  - Strength: One source of truth; padded-but-valid text accepted.
  - Tradeoff: Changes the 501-character response from a `Text`-keyed 400 to `CommentTextTooLong`; contract tests and Api/CLAUDE.md must change; unbounded body reaches the service.
  - Confidence: MED — request size is still capped by Kestrel defaults.
  - Blind spot: Haven't checked whether the SPA maps a `Text`-keyed 400 anywhere.
- **Decision**: FIXED via Fix A — CommentTextTooLong removed from loggedErrorCodes; Api/CLAUDE.md status-mapping note updated

### F5 — No reporting test for the `sighting.comments.delete` context

- **Severity**: 💡 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Success Criteria
- **Location**: PigeonWatch/Frontend/src/app/features/sightings/sighting-detail-page/sighting-comments/sighting-comments-reporting.spec.ts
- **Detail**: Load, add and edit contexts are asserted report-or-silent; delete (500 reported, 404 silent) is not.
- **Fix**: Add a delete 500 → reported with `sighting.comments.delete` and a delete 404 → silent case.
- **Decision**: FIXED — delete 500 reported under sighting.comments.delete and delete 404 silent added to sighting-comments-reporting.spec.ts (break-check: red with the wrong context)

### F6 — A post while the thread is in the error state is invisible until retry

- **Severity**: 💡 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Safety & Quality
- **Location**: PigeonWatch/Frontend/src/app/features/sightings/sighting-detail-page/sighting-comments/sighting-comments.ts:249-273
- **Detail**: The add form stays usable when the first page failed; a successful post updates `items`/`totalCount` but the list stays hidden behind the error until Try again reloads from the server (which then corrects the state).
- **Fix**: On a successful post while the thread is not loaded, trigger a reload.
- **Decision**: FIXED — a successful post while the thread is not loaded reloads page 1 instead of prepending; regression spec red before, green after

### F7 — Saving unchanged text consumes the cooldown and sets the edited marker

- **Severity**: 💡 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Safety & Quality
- **Location**: PigeonWatch/Api/BusinessLogic/Services/SightingCommentService.cs:~60-75
- **Detail**: An edit whose trimmed text equals the stored text still writes `EDITED_AT`, shows "edited" and starts the 60 s clock. The immediate-edit cooldown itself is a deliberate, documented rule.
- **Fix**: Treat an unchanged-text update as a no-op success (return the current comment without writing or checking the cooldown), or leave as is.
- **Decision**: FIXED — unchanged trimmed text returns the comment after the authorship check, with no write, no EDITED_AT and no cooldown check; two service tests (red before, green after); Api/CLAUDE.md order-of-checks updated
