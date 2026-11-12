# Edit Own Sighting With Engagement Lock — Plan Brief

> Full plan: `context/changes/edit-own-sighting-with-lock/plan.md`
> Research: `context/changes/edit-own-sighting-with-lock/research.md`

## What & Why

Owners can fix their sighting and move it through the rescue workflow, which is roadmap S-09 / PRD FR-006. Two safeguards stop details a rescuer relies on from changing under them:
- The location can never change after the sighting is reported.
- The description becomes read-only while someone else is engaged (a visible comment from another user, or any message thread).

## Starting Point

There is no update endpoint, repository method, SPA call or edit route. The PRD status graph is encoded and unit-tested in `SightingStatusTransitionProvider` but nothing calls it. Comments (`AUTHOR_ID`, `DELETED_AT`) and threads (created only by non-owners) already hold every signal the lock needs.

## Desired End State

The owner sees an Edit button on their sighting. It opens a page where they can change:
- the name, behaviour and criticality;
- the description, unless it's locked;
- the status, from the allowed next steps;
- the photo (add, replace or remove).

The location is shown as fixed text. The API enforces every rule itself: 403 for non-owners, 400 for an invalid transition or a changed locked description.

## Key Decisions Made

| Decision | Choice | Why (1 sentence) | Source |
| --- | --- | --- | --- |
| Location editing | Never editable after creation, for anyone | Rescuers navigate by the pin; tightens FR-006 | Plan |
| Read-only after engagement | Description only | Literal FR-006 minus location; criticality can still rise | Plan |
| Engagement trigger | A visible comment by a non-owner, or any thread; a deleted comment no longer counts | Forgiving of a comment deleted by mistake | Plan |
| Owner's own comments | Never trigger the lock | Owners may comment on their own sighting | Research |
| Photo | Add, replace or remove, always editable | A better photo helps rescuers | Plan |
| Endpoint | One multipart `PUT sightings/{id}` with status | One route and one form; an unchanged status is a no-op | Plan |
| Non-owner edit | 403 `NotSightingOwner` | Sightings are readable by all members, so a 404 hides nothing; same as comments | Research |
| Lock/transition data on wire | Nullable `editPermissions` on single-sighting responses for the owner only | No per-row engagement queries in lists; no ids exposed | Plan |
| Status timestamp | None (audit `UPDATE_DATE` only) | Keep the slice small; history is a non-goal | Plan |
| Cooldown / concurrency | Neither; the race with a simultaneous comment is accepted | Same stance as the non-atomic create cooldown | Plan |

## Scope

**In scope:**
- The `PUT sightings/{id}` rules, including blob cleanup when a photo is replaced or removed.
- The `editPermissions` field.
- The edit page and the detail page's Edit button.
- The new labels and kitchen-sink cells.
- A two-user Playwright test.
- Updates to the PRD FR-006, roadmap S-09, test-plan and CLAUDE.md wording.

**Out of scope:**
- Moving the location.
- A status-changed timestamp or history.
- An edit cooldown and a rowversion.
- Making other fields read-only.
- Rules for commenting on or messaging resolved sightings.
- Admin edits (S-10, S-11).
- Showing an "edited" marker.

## Architecture / Approach

- **Repository:** an engagement existence query and a tracked update scoped by id and reporter.
- **Service:** checks run in this order:
  1. Validate the fields.
  2. 404 if the sighting doesn't exist.
  3. 403 if the caller isn't the owner.
  4. Check the status transition.
  5. Check the description lock.
  6. Normalise the new photo.
  7. Store the new blobs.
  8. Update the row.
  9. Delete the old blobs.
- **Controller:** maps the error codes the way `SightingCommentsController` does.
- **SPA:** renders the server's `editPermissions`. If the form is stale (`DescriptionLocked`, `InvalidStatusTransition`), it reloads the sighting.

## Phases at a Glance

| Phase | What it delivers | Key risk |
| --- | --- | --- |
| 1. API: edit permissions | Engagement query and `editPermissions` on create/get | Accidentally computing it for list items |
| 2. API: PUT | Owner update with transition, lock and photo rules, plus guards and docs | Blob cleanup ordering on replace/failure |
| 3. SPA: edit page | Route, form, Edit button, error mapping, labels, kitchen sink | Status select and read-only state drifting from the server |
| 4. E2E and docs | Two-user Playwright lock test; PRD, roadmap and test-plan updates | Existing E2E specs that assumed no resolved sightings |

**Prerequisites:** S-07 and S-08 are merged (done). LocalDB and the API run for the manual checks and Playwright.
**Estimated effort:** about 3-4 sessions across 4 phases.

## Open Risks & Assumptions

- **A comment posted at the same moment as a description edit can slip past the lock** (accepted race).
- **If the owner saves from two tabs, the last save silently wins.**
- **A crash between storing a new photo and updating the row leaves orphaned files,** the same accepted limit as create.

## Success Criteria (Summary)

- The owner can change their sighting's details, status and photo, and can never move its location.
- Once another user comments (visibly) or messages them, the description is read-only, but status changes still work. Deleting that comment unlocks the description again.
- Non-owners can't edit: there's no Edit button, and the API returns 403.
