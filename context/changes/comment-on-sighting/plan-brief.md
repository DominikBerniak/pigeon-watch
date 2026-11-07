# Comment on a Sighting — Plan Brief

> Full plan: `context/changes/comment-on-sighting/plan.md`
> Research: `context/changes/comment-on-sighting/research.md`

## What & Why

Roadmap slice S-07 (FR-009, FR-019) lets any logged-in member comment on any sighting, and edit or delete their own comments, from the sighting detail page. A per-user comment cooldown is the only spam guard until the admin moderation slices land. The slice also stores the data the later S-09 engagement lock needs.

## Starting Point

There is no comment code. The sighting stack (entity, repository, service, controller, view model creator) is the template. The 60 s sighting cooldown sits inline in `SightingService.CreateAsync`, and S-03 left it for this slice to generalise. The detail page already loads its photo in a separate stream, which is the pattern a comments section follows.

## Desired End State

On a sighting's detail page, a member sees comments newest first in a fixed-height scrollable box, with "Show older comments" inside the box so the page never moves. They can post a comment, edit or delete their own, and see "edited" markers and "Comment deleted" placeholders. Posting or editing again within 60 s shows a clear cooldown message. Reporting a sighting and commenting do not block each other.

## Key Decisions Made

| Decision | Choice | Why (1 sentence) | Source |
| --- | --- | --- | --- |
| Cooldown model | Separate clocks (sighting, comment), 60 s each, one shared provider | Lets a rescuer report and immediately add detail, while still capping each kind at 1/min | Plan |
| Edits and cooldown | An edit counts as a comment submission; a delete does not | Stops rapid edit spam; a typo fix right after posting waits up to 60 s | Plan |
| Delete-and-repost | Deleted comments still count for the comment clock | Prevents bypassing the cooldown | Plan (derived) |
| Comment length | 500 characters after trimming | Matches the existing behaviour-text cap | Plan |
| Listing | Newest first, 20 per page, "Show older" inside a fixed-height scroll box | Your choice; the fixed box keeps the page from shifting on repeated loads | Plan |
| Edit/delete | Authors can edit and delete their own; others get 403 | Your choice of full author control | Plan |
| Delete semantics | Soft delete; placeholder with no text/author stays in the list and count | Thread stays coherent, and S-09/moderation keep the record | Plan |
| Edited marker | Dedicated `EDITED_AT` column, no history | Audit `UPDATE_DATE` also changes on delete, so it can't mean "edited" | Research + Plan |
| Who/where | Owners can comment; any status, including resolved | Matches PRD flat permissions; no status coupling | Plan |
| Free-text contact rule | Keep the self-disclosure stance; record it in test-plan §7 | Consistent with sighting text; edit/delete lets authors retract | Plan |
| Author identity on the wire | Display name plus server-computed `isMine`; no author id | Contact-info and id privacy hard rule | Research |
| 403 in the SPA | Reported, with the error reference (shared expected-status set unchanged) | Edit/Delete only show on own comments, so a 403 is a client bug | Plan review |
| Endpoint shape | Separate `SightingCommentsController` at `sightings/{id}/comments` | `SightingModel` is shared by every list; sub-resource precedent from photos | Research |

## Scope

**In scope:**
- `SIGHTING_COMMENT` table and migration, plus the shared `SubmissionCooldownProvider` (sighting behaviour unchanged)
- `GET` (paged), `POST`, `PUT`, `DELETE` comment endpoints with 403/404/cooldown semantics
- `commentRules` in the client config; endpoint fixtures, privacy sweep, contract tests
- Detail-page comments component, labels, token, kitchen-sink cells, specs
- One Playwright spec; updates to the API, PigeonWatch and Frontend `CLAUDE.md` files and test-plan §7

**Out of scope:**
- Admin removal of comments (a FR-017/S-10 gap), edit history, notifications, live updates
- The S-09 engagement lock itself; Polish label values; cursor pagination; contact-detail filtering

## Architecture / Approach

SPA `sighting-comments` component → `SightingsApi` → `SightingCommentsController` → `SightingCommentService` (validate → exists → author → cooldown) → `SightingCommentRepository` (EF, `Include(Author)`) → `SIGHTING_COMMENT`. Both `SightingService` and `SightingCommentService` ask `ISubmissionCooldownProvider` for the seconds left, each from its own "latest submission" query. Errors reuse `ValidationProblemDetails` with `CooldownActive` + `retryAfterSeconds`.

## Phases at a Glance

| Phase | What it delivers | Key risk |
| --- | --- | --- |
| 1. Comment data + shared cooldown | Table, repository, provider; SightingService refactor | Regressing the sighting cooldown (guarded by existing tests) |
| 2. Comment API | 4 endpoints, client config, contract tests, API docs | Missing a contract gate or leaking an id in a placeholder |
| 3. SPA comments section | Component with scroll box, edit/delete, labels, sink, specs | Layout shift and keyboard access in the scroll box |
| 4. E2E + docs | Playwright flow incl. cooldown on edit; CLAUDE.md updates | E2E timing around the 60 s cooldown |

**Prerequisites:** S-03 done (it is). Local LocalDB and the `dotnet-ef` tool restored for the migration.
**Estimated effort:** ~3–4 sessions across 4 phases.

## Open Risks & Assumptions

- Edits count against the cooldown, so a typo fix right after posting waits up to 60 s. Accepted; the E2E checks the message.
- Page-based paging can repeat a row when a new comment arrives between loads; the SPA dedupes by id. No older row is ever skipped, because new comments only push rows down and soft delete removes none.
- The cooldown check is not atomic (same accepted race as sightings).
- No admin can remove a comment until a moderation slice covers it; FR-009's spam answer leans on that.
- S-09 must still decide whether a deleted comment, or the owner's own comment, triggers the lock; the data is kept for it.

## Success Criteria (Summary)

- A member can post, edit, delete and page through comments on any sighting from its detail page, without the page jumping.
- Rapid posting or editing is blocked with a clear seconds-left message, without blocking a sighting report.
- No comment response exposes an author id, email or audit field; all API and SPA gates stay green.
