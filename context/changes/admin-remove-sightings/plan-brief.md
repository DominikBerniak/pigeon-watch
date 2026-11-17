# Admin removes sightings (S-10) — Plan Brief

> Full plan: `context/changes/admin-remove-sightings/plan.md`
> Research: `context/changes/admin-remove-sightings/research.md`

## What & Why

PRD FR-017 needs a single admin to remove bad-faith or duplicate sightings. The admin is also the planned answer to spam on open comments (`prd.md:87`). This change introduces the Admin role, which S-11 reuses. It lets the admin soft-remove and restore a sighting, and applies one visibility rule to a removed sighting across the API and SPA.

## Starting Point

There is no role anywhere. The custom Identity user store has no role support, and `currentUser.roles` reaches the SPA but is always empty. There is no sighting delete path, and every foreign key is `Restrict`. The house pattern is soft delete with filters written per query (comments) and append-only history (messages).

## Desired End State

An email listed in `Moderation:AdminEmails` gets the Admin role at API start. That user sees Remove (with confirmation) and Restore on any sighting.

A removed sighting:
- **Everyone else:** it disappears from the map and list.
- **Admin:** still sees it on the map and list, marked Removed.
- **Reporter:** sees it, marked Removed and read-only, in My sightings and on its detail page.
- **Thread participants:** their conversations stay readable, but no new messages are possible.

Restore brings everything back.

## Key Decisions Made

| Decision | Choice | Why (1 sentence) | Source |
|---|---|---|---|
| Admin designation | Full Identity role store (`ROLE`, `USER_ROLE`, `IUserRoleStore`, `AddRoles`) | Standard roles fill the existing `currentUser.roles` claim channel and serve S-11. | Plan |
| Role assignment | Startup bootstrap from `Moderation:AdminEmails`, idempotent, never revokes | One mechanism for dev, E2E and prod with no manual SQL. | Plan |
| Removal semantics | Soft delete with `REMOVED_AT` and `REMOVED_BY` | Keeps the moderation trail like comments and messages, and the remover survives later audit overwrites. | Plan |
| Reporter's view | Sees the sighting marked Removed, read-only | Transparent to the affected user. | Plan |
| Threads and comments | Threads readable as history, all writes blocked | Participants keep their conversation. | Plan |
| Admin after removal | Admin keeps seeing removed sightings and can restore | Mistakes are recoverable in-app. | Plan |
| Finding removed sightings | Admin's map and list include them with a Removed marker | No separate admin page needed. | Plan |
| Non-admin on remove/restore | 403 via a service `NotAdmin` code with a Warning log | Same shape as `NotSightingOwner`. A policy attribute would skip the controller log. | Plan (derived) |
| Writes on a removed sighting | 400 `SightingRemoved` if the caller can see it, else 404 | Follows the `DescriptionLocked` validation pattern and the "invisible means 404" rule. | Plan (derived) |
| Remove/restore repeat | Idempotent, 200 with the model, first removal kept | Two admin tabs cannot conflict. | Plan (derived) |
| Bootstrap runtime | `BackgroundService`, Error log on failure, API keeps serving | A cold Azure SQL must not block or crash startup. | Plan (derived) |

## Scope

**In scope:**
- Role tables and stores, the configuration-based bootstrap, admin test helpers.
- The removal columns, `DELETE sightings/{id}` and `POST sightings/{id}/restore`.
- The visibility rule on 11 read and 7 write paths, and `IsSightingRemoved` on threads.
- SPA: Remove dialog and Restore, removed alert, removed marker and badge, read-only comments and thread, labels, kitchen-sink cells.
- One E2E scenario, and updates to the CLAUDE.md files and the test plan.

**Out of scope:**
- An admin UI to grant roles. Revoking a role needs SQL.
- A removal reason or notification.
- Admin removal of individual comments or messages.
- Hard delete and blob cleanup.
- Flocks, Polish labels, and S-11 warn or ban.

## Architecture / Approach

Identity gets a role store, so the role claim lands in the bearer ticket and is re-read on every refresh. Controllers read `User.IsInRole("Admin")` and pass `isAdmin` and the user id to services. A single `SightingVisibilityProvider.CanView(access, viewer, isAdmin)` decides visibility for the sighting, comment and thread services. Repositories filter `REMOVED_AT` only where the rule says so: the map and list for non-admins. The SPA reads `removedAt`, `isSightingRemoved` and `SessionService.isAdmin`.

## Phases at a Glance

| Phase | What it delivers | Key risk |
|---|---|---|
| 1. Admin role foundation (API) | Roles stored and issued, config bootstrap, `CreateAdminAsync` test helper | Custom store and `USER_ROLE` save semantics; test-host startup ordering |
| 2. Remove and restore endpoints (API) | Removal columns, two admin endpoints, fixtures, contract tests | Endpoint guards and sweep need correct fixtures |
| 3. Visibility rules (API) | One rule applied on every read and write path, read-only threads | Missing one path leaks a removed sighting; guarded by the contract table test |
| 4. SPA moderation UI | Admin actions, removed states on detail, map, list, My sightings and thread | Specs and kitchen-sink stubs that now need `SessionService.isAdmin` |
| 5. E2E and docs | Browser-proven remove and restore flow, documented contracts for S-11 | Local admin setup (user-secrets plus restart) for E2E |

**Prerequisites:** S-03 done (it is). LocalDB and user-secrets for manual checks. An `E2E_ADMIN_*` account registered locally.
**Estimated effort:** about 4–5 sessions. Phases 1 and 3 are the largest.

## Open Risks & Assumptions

- That Identity's claims factory and the refresh path behave as expected (roles re-read on refresh) is framework knowledge, proven by a Phase 1 integration test before anything builds on it.
- A granted role is seen only after the next login or refresh (access token about 1 hour). This is accepted for S-10, and S-11's ban must not rely on it.
- The production admin needs the App Service setting `Moderation__AdminEmails__0`. Without it no one is admin, but the API runs normally.

## Success Criteria (Summary)

- The configured admin can remove a sighting from its detail page and restore it later from the map or list.
- After removal, members no longer see the sighting, and the reporter sees it marked Removed and frozen. Thread participants can still read the conversation.
- The unit, integration, architecture, frontend and E2E suites are green, including the per-role visibility table test.
