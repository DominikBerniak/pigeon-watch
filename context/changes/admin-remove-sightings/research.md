---
date: 2026-11-17T12:49:57+01:00
researcher: Dominik Berniak (with Claude Code)
git_commit: b8435046be858edf9d745542c5d62034354a4083
branch: main
repository: 10xDevs (PigeonWatch)
topic: "S-10 admin-remove-sightings: what exists for an admin role and for removing a sighting, what removal touches, and which decisions are open"
tags: [research, codebase, admin, authorization, identity, sightings, soft-delete, comments, threads, angular, aspnet-core]
status: complete
last_updated: 2026-11-17
last_updated_by: Dominik Berniak (with Claude Code)
---

# Research: S-10 admin removes sightings

**Date**: 2026-11-17T12:49:57+01:00
**Researcher**: Dominik Berniak (with Claude Code)
**Git Commit**: b8435046be858edf9d745542c5d62034354a4083
**Branch**: main
**Repository**: 10xDevs (PigeonWatch)

## Research Question

Roadmap S-10 / PRD FR-017 (`context/foundation/roadmap.md:210-221`, `context/foundation/prd.md:106-107`): "as the admin, user can remove a sighting from the map and lists." This is the first introduction of the single admin role, and S-11 (`roadmap.md:223-233`) reuses it. What does the codebase provide today for identifying an admin and for removing a sighting, what does removing a sighting touch across the API and SPA, and which decisions are still open?

## Summary

- **No admin concept exists in the inspected API.** Identity is registered with `AddIdentityApiEndpoints<ApplicationUser>` and a custom user store (`PigeonWatch/Api/DependencyInjection/AccountServiceCollectionExtensions.cs:12,29`). There is no `AddRoles`. The store implements exactly four Identity store interfaces (password, email, security stamp, lockout; `PigeonWatch/Api/Data/Identity/PigeonWatchUserStore.cs:17-20`), so no role or extra claim reaches the bearer ticket. `UserAccountEntity` has no role or admin column (grep for `role|isadmin|is_admin` in `Data/Entities/*.cs` returned no match). No `AddAuthorization`/`AddPolicy` call and no `[Authorize(Roles=…)]` were found in the API.
- **The role channel to the SPA already exists and is always empty.** `ConfigurationController` reads `ClaimTypes.Role` claims into the response (`PigeonWatch/Api/WebApi/Controllers/ConfigurationController.cs:45`). The SPA type `CurrentUser` has `roles: string[]` (`PigeonWatch/Frontend/src/app/core/configuration/configuration.service.ts:50-55`). No SPA code branches on `roles`. The register-and-login plan deferred this explicitly: "No role tables or role store … always empty until S-10" (`context/archive/2026-10-04-register-and-login/plan.md:72`).
- **No sighting delete path exists, and the schema blocks a plain hard delete.** `SightingsController` has 8 routed actions: create, update, get, photo, thumbnail, nearby, list and mine (`PigeonWatch/Api/WebApi/Controllers/SightingsController.cs:47,76,107,125,139,153,175,197`). There is no delete action. All 7 `OnDelete` calls in `PigeonWatchDbContext` are `DeleteBehavior.Restrict` (`PigeonWatch/Api/Data/PigeonWatchDbContext.cs:43,55,59,71,75,87,91`). A `DELETE` of a sighting row that has comments or threads would therefore fail unless those rows are deleted first. `SightingEntity` has no `DeletedAt`/removed column. Its photo is a nullable `PhotoBlobName` column (`Data/Entities/SightingEntity.cs:29-30`).
- **The repo's established deletion pattern is soft delete with hand-written filters.** Comments carry `DeletedAt` and are soft-deleted (`Data/Repositories/SightingCommentRepository.cs:69-82`). No `HasQueryFilter` exists anywhere under `Data/`, so every query that must hide removed rows filters by hand. Message threads are append-only by decision, kept as a moderation trail (`context/archive/2026-11-07-sighting-message-threads/plan-brief.md:26`).
- **A removed sighting has to be filtered in many places.** The inspected set is 11 read paths and 7 write paths. Three repository methods are choke points that cover most of them: `SightingRepository.GetByIdAsync` (`:25`), `SightingCommentRepository.SightingExistsAsync` (`:105-107`) and `SightingThreadRepository.GetSightingOwnerAsync` (`:16`). These need separate filters: the two list queries (`ListInBoxAsync` `:35`, `ListPageInRadiusAsync` `:69`, including its count), `ListByReporterAsync` (`:119`), `GetPhotoBlobNameAsync` (`:143`), `StartOrAppendAsync` (`SightingThreadRepository.cs:65`), the thread inbox and the unread count (`SightingThreadRepository.cs:135,180-220`), and the comment by-id queries (`SightingCommentRepository.cs:25-30,57,73`). The comment by-id queries do not check the sighting.
- **The SPA has the patterns needed but no admin awareness.** The detail page gates owner actions on the server flag `isMine` (`features/sightings/sighting-detail-page/sighting-detail-page.html:25`). `Sighting` carries `isMine` and `editPermissions` (`features/sightings/sighting.types.ts:43-44`). The comment delete confirmation dialog is the nearest precedent for a destructive action. The detail page already shows a "no longer available" info alert on 404.
- **Open product decisions.** The roadmap names one unknown, admin assignment (`roadmap.md:219`). The research surfaces five more that nothing in `context/` decides: soft or hard removal, what the reporter sees, what happens to the removed sighting's threads, unread count and comments, whether a removal records who removed it and when, and the status code a non-admin gets. These are listed under Open Questions.

## Detailed Findings

### 1. Authentication and the (missing) admin role

- Identity setup: `PigeonWatch/Api/DependencyInjection/AccountServiceCollectionExtensions.cs:12` (`AddIdentityApiEndpoints<ApplicationUser>`), `:29` (`AddUserStore<PigeonWatchUserStore>`). There is no `IdentityDbContext` and there are no stock Identity tables, so no AspNetRoles table exists to reuse.
- Tokens: Identity bearer tokens protected by Data Protection, not signed JWTs (`context/archive/2026-10-04-register-and-login/research.md:142`, D1). The SPA cannot decode them, and learns about the user only through `GET /configuration/general` (`core/configuration/configuration.service.ts:80`, per the frontend worker) → `SessionService.currentUser` (`core/auth/session.service.ts:55`).
- Claims: only the user-store interfaces at `PigeonWatchUserStore.cs:17-20` are implemented. A role claim needs one of three changes: `IUserRoleStore` plus `AddRoles` with a role table, a custom `IUserClaimsPrincipalFactory`, or a claims transformation. Inference from framework behaviour, not run.
- Current user id pattern in controllers: `Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), …)`, then the id is passed to services (e.g. `SightingsController.cs:53,83`, per the backend auth worker).
- Pipeline: `UseAuthentication` → log scope → `UseRateLimiter` → `UseAuthorization` → controllers (`PigeonWatch/Api/WebApi.Host/Program.cs:160-176`, per worker). No authorization policies are registered. Program.cs configures CORS and the rate-limiter policies.
- Configuration that could designate an admin: none exists. `appsettings.json` holds Logging and AllowedHosts only. The single options class is `BlobStorageOptions`, bound with `AddOptions<T>().Bind(…)` plus an `IValidateOptions` validator (`DependencyInjection/DataServiceCollectionExtensions.cs:31-35`, per worker). That is the in-repo precedent for a config-bound setting. `appsettings.Development.json` could not be read in this session (permission deny), so its contents are not verified here.
- No data seeding exists (no `HasData`), so no admin user exists in any environment today.
- Claim freshness (inference, not tested): a claim baked into the bearer ticket changes only at the next login or refresh. A per-request check (database or config lookup in the service) avoids that delay. Only S-11 depends on this, because a ban must take effect without waiting for a refresh.

### 2. Ownership and authorization conventions to mirror

- Owner checks live in the service and return a typed error code, which the controller maps to HTTP. On the inspected update path, `SightingService.UpdateAsync` returns `SightingErrorCodes.NotSightingOwner` when `current.ReporterId != trimmed.UserId` (`BusinessLogic/Services/SightingService.cs:93-94`, per worker). The controller maps `SightingNotFound` to 404 and `NotSightingOwner` to 403 (`SightingsController.cs:90-94`, per worker), using helpers in `WebApi/Controllers/FailureLoggingControllerBase.cs:32-81`.
- Threads use 404 for non-participants so thread ids cannot be probed (`context/archive/2026-11-07-sighting-message-threads/plan-brief.md:29`). Sighting writes use 403 because sightings are readable by every member, so a 404 hides nothing (`context/archive/2026-11-09-edit-own-sighting-with-lock/plan-brief.md:36`). No document sets a rule for an admin-only endpoint called by a non-admin.
- Architecture tests that bind a new endpoint and service (per worker, `PigeonWatch/Api/ArchitectureTests/`): only DependencyInjection registers services. New services need an `I<Name>` interface in `Interfaces/` (`ServiceRegistrationTests.cs:30,44`). Controller actions return `Task<ActionResult<T>>` with `T` from WebApi.Models. Every async method takes a `CancellationToken`. The layer matrix forbids WebApi → Data (`LayerDependencyTests.cs:82`). Entities derive from `AuditableEntity` and use UPPER_SNAKE singular table names (`DatabaseModelTests.cs:135`). No architecture test mentions authorization. "Host calls only AddPigeonWatch" means `AddAuthorization`, if used, belongs in DependencyInjection.

### 3. Data model and what removal touches

- Entities related to a sighting: `SightingCommentEntity` (FK `SightingId`, `DeletedAt`), `SightingThreadEntity` (FK `SightingId`, unique `(SightingId, ParticipantId)`), `SightingMessageEntity` (FK `ThreadId`). All derive from `AuditableEntity` (`CreateUser/CreateDate/UpdateUser/UpdateDate`).
- Delete behaviour: all 7 configured FKs are Restrict (`PigeonWatchDbContext.cs:43,55,59,71,75,87,91`). The worker reports 8 Restrict FKs in the model snapshot (`Data/Migrations/PigeonWatchDbContextModelSnapshot.cs:393-455`). The DbContext count above is the one verified here.
- Auditing: `AuditSaveChangesInterceptor` stamps `UpdateUser/UpdateDate` on every Modified entry (`Data/Auditing/AuditSaveChangesInterceptor.cs:27-52`, per worker). A soft-delete update would therefore record the removing admin's id in `UPDATE_USER` with no extra code. That stamp is overwritten by any later update to the row.
- Latest migration: `20261108193238_AddSightingThreadReadMarkers`. A new column needs a new migration. Production runs SQL Server (`DependencyInjection/DataServiceCollectionExtensions.cs:51`). Integration and repository tests run SQLite (`IntegrationTests/PigeonWatchApiFactory.cs:204`).
- Photos: two blobs per sighting (original and thumbnail; `SightingService.cs:450`, per worker). Whole-sighting blob deletion does not exist. Blobs are deleted only after a photo replace or remove, or for orphans on a failed save, using a log-and-continue pattern (`SightingService.cs:341,347`, per worker). A soft delete leaves both blobs in storage. They stop being reachable only if `GetPhotoBlobNameAsync` filters removed sightings.
- Cooldown: `GetLatestCreateDateByReporterAsync` (`SightingRepository.cs:132`) does not filter. Left unfiltered, removing a sighting does not reset the reporter's submission cooldown.

### 4. Read paths that would need a removed-sighting filter (API)

| Endpoint | Repository query | Filter point |
|---|---|---|
| `GET sightings/nearby` (`SightingsController.cs:153`) | `ListInBoxAsync` (`SightingRepository.cs:35-67`) | add to `Where` |
| `POST sightings/list` (`:175`) | `ListPageInRadiusAsync` (`:69-117`), items and `CountAsync` | add to `Where` before count |
| `GET sightings/mine` (`:197`) | `ListByReporterAsync` (`:119`) | filter, or show "removed" (open) |
| `GET sightings/{id}` (`:107`) | `GetByIdAsync` (`:25`) | choke point. Null gives 404. |
| `GET sightings/{id}/photo` and `/thumbnail` (`:125,139`) | `GetPhotoBlobNameAsync` (`:143`) | filter. Null gives 404. |
| `GET sightings/{id}/comments` | `SightingExistsAsync` (`SightingCommentRepository.cs:105-107`) | choke point |
| `GET sightings/{id}/threads`, `…/threads/{tid}/messages` | `GetSightingOwnerAsync` (`SightingThreadRepository.cs:16`) | choke point |
| `GET threads` (inbox) | `ListThreadsForUserAsync` (`:135`) via `ParticipantThreads` (`:188-193`, no sighting join) and `OwnerThreads` (`:195-202`, joins `db.Sightings`) | both builders (open decision) |
| `GET threads/unread-count` | `CountUnreadMessagesAsync` (`:180-186`) via the same two builders (`:204-220`) | same builders |

The table lists 11 endpoints, counting photo and thumbnail separately and the two thread reads separately. The worker's list of controllers covered `SightingsController`, `SightingCommentsController`, `SightingThreadsController` and `ThreadsController`.

### 5. Write paths that target a sighting

- `PUT sightings/{id}` (`SightingsController.cs:76`): goes through `GetByIdAsync`, so filtering it gives 404. The repository update re-fetches by id and reporter (`SightingRepository.cs:153-184`) and needs the same filter.
- `POST sightings/{id}/comments`: `SightingExistsAsync` gives 404.
- `PUT`/`DELETE sightings/{id}/comments/{cid}`: `GetAsync` (`SightingCommentRepository.cs:25-30`), `UpdateTextAsync` (`:57`) and `SoftDeleteAsync` (`:73`) match on comment id and sighting id with no check on the sighting row. Comments under a removed sighting would stay editable unless these are filtered or the service checks the sighting first.
- `POST sightings/{id}/threads`: `GetSightingOwnerAsync`, then `StartOrAppendAsync`, which reads `db.Sightings` directly (`SightingThreadRepository.cs:65`).
- `POST threads/{tid}/messages` and `PUT threads/{tid}/read`: `GetSightingOwnerAsync` is checked first (per worker).
- Rate limiting: `SightingEdits` is the only per-user write policy and applies only to `PUT sightings/{id}` (`WebApi.Host/Program.cs:92`, per worker). An admin remove endpoint needs its own policy or none. It is not a submission, so the cooldown does not apply.

### 6. SPA surfaces

- Detail page: loads via `sightingsApi.get(id)` and maps 404 to `notFound` (not reported). It shows the label `sighting.errors.notFound`, "This sighting doesn't exist or is no longer available." (`PigeonWatch/Api/BusinessObjects/Resources/UiLabels.resx:732`, per worker). Owner actions sit under `@if (sighting()?.isMine)` (`sighting-detail-page.html:25`). The message button sits under `!sighting.isMine` (`:87`). An "is admin" signal could come from a server flag on `Sighting`, following the `isMine`/`editPermissions` pattern (`sighting.types.ts:43-44`), or from `currentUser.roles`.
- Destructive-action precedent: `sighting-comments/delete-comment-dialog/` is a dumb `MatDialog` with Cancel `[mat-dialog-close]="false"` and Confirm `true`. The caller pipes `afterClosed()` → filter → `switchMap(delete)` with a `finalize` busy signal and a failure mapper (per worker, `sighting-comments.ts` ~L342-376).
- Failure mapping: `sightingFailure` (`features/sightings/sighting-errors.ts:35`) has no 403 case. `sightingUpdateFailure` adds `forbidden` (`:107-110`). A remove call needs its own mapper or an extension. `isReportablePhotoFailure` (`:73`) reports a photo 404 as an error, so a photo requested after removal would raise a false report.
- Message thread page: per worker, `loadFailure` in `features/messages/message-thread/message-thread.ts` (~L415-421) collapses every load error, 404 included, into a generic retryable error. If threads of a removed sighting 404, the participant sees a generic error with retry, not a "no longer available" state.
- Lists and map: plain `HttpClient` with no cache. The map, the list and My sightings (`profile-page.html:77`) refetch on navigation. An already-open page does not update itself (per worker).
- Guards: `authGuard`, `guestGuard` and `unknownRouteRedirect` only (`core/auth/auth.guards.ts`). There is no role guard.
- Labels: a .NET resx (`PigeonWatch/Api/BusinessObjects/Resources/UiLabels.resx`) generates `core/resources/generated/ui-labels.en.json` on `prestart`. `label-coverage.spec.ts` exists. New strings go into the resx.

### 7. Tests

- Integration harness: `PigeonWatchApiFactory` (SQLite, `InMemorySightingPhotoStore`). `CreateUserAsync` registers and logs in a `TestUser`. The factory also runs direct SQL helpers such as `DeleteUserAccount`, `ExpireLockout` and `ReadPhotoBlobName` (per worker). No admin helper exists. A config-based admin list would be easy to set from `WebApplicationFactory` configuration overrides. A database flag would need a SQL helper (inference).
- Three guards fail until a new endpoint is registered: `EndpointCoverageTests`, `EndpointProtectionTests`, and the `PrivacySweepTests.Sweep_as_*` tests (`context/foundation/test-plan.md:174-181`). An ownership-style test should read state back through another user's client (`test-plan.md:182`). Expected values come from the documented contract, never from production constants (`test-plan.md:186`).
- Nearest contract tests to model on (per worker): `SightingEditContractTests` (403 for Bob and Carol, 404 for unknown, blob deletion), `SightingCommentContractTests` (deleted-comment placeholder), `SightingThreadContractTests` (404 for non-participant).
- E2E: `auth.setup.ts` logs in one user from `E2E_USERNAME`/`E2E_PASSWORD` (`PigeonWatch/Frontend/tests/e2e/auth.setup.ts:6-9`). Multi-user specs register fresh users in a new browser context (`sighting-messages.spec.ts`, per worker). An admin E2E scenario cannot self-register an admin, so it needs whatever assignment mechanism S-10 picks to be reachable from the E2E environment.

## Code References

- `PigeonWatch/Api/DependencyInjection/AccountServiceCollectionExtensions.cs:12,29` - Identity registration, custom user store, no roles
- `PigeonWatch/Api/Data/Identity/PigeonWatchUserStore.cs:17-20` - implemented store interfaces (no role or claim store)
- `PigeonWatch/Api/WebApi/Controllers/ConfigurationController.cs:45` - role claims copied into `currentUser.roles`
- `PigeonWatch/Frontend/src/app/core/configuration/configuration.service.ts:50-55` - SPA `CurrentUser.roles`
- `PigeonWatch/Api/Data/PigeonWatchDbContext.cs:43-91` - all FKs Restrict
- `PigeonWatch/Api/Data/Entities/SightingEntity.cs:29-30` - `PhotoBlobName`, no removal column
- `PigeonWatch/Api/Data/Repositories/SightingRepository.cs:25,35,69,119,132,143,153` - sighting queries needing a filter (or deliberately not)
- `PigeonWatch/Api/Data/Repositories/SightingCommentRepository.cs:25-30,57,69-82,105-107` - comment by-id queries without sighting check; soft-delete precedent; existence choke point
- `PigeonWatch/Api/Data/Repositories/SightingThreadRepository.cs:16,65,135,180-220` - owner choke point, direct sighting read, inbox and unread builders
- `PigeonWatch/Api/WebApi/Controllers/SightingsController.cs:47-197` - 8 routed actions, no delete
- `PigeonWatch/Frontend/src/app/features/sightings/sighting-detail-page/sighting-detail-page.html:25,87` - owner/non-owner gating
- `PigeonWatch/Frontend/src/app/features/sightings/sighting-errors.ts:35,73,107-110` - failure mappers
- `PigeonWatch/Frontend/tests/e2e/auth.setup.ts:6-9` - single-user E2E login

## Architecture Insights

- **Server-computed permission flags.** The SPA never compares user ids. It renders from `isMine` and `editPermissions`. An admin "can remove" signal fits as either another server flag on `SightingModel` or `currentUser.roles`. The flag is consistent with existing per-sighting decisions. `roles` is the existing app-wide channel S-11 will also need.
- **Domain error codes, then controller mapping.** Authorization outcomes are service-level error codes, not ASP.NET policies (`context/archive/2026-10-18-testing-api-privacy-authorization-contract/research.md:170`). An admin check can follow the same shape, a service check returning a `NotAdmin`-style code, or use a framework policy. Nothing in the repo uses policies yet.
- **Soft delete without global filters.** The comment precedent filters `DeletedAt` per query. A removed-sighting flag follows the same discipline across the paths in section 4. A `HasQueryFilter` would cover them all at once, but it is not used anywhere and would also hide rows from the engagement-lock and cooldown queries unless those call `IgnoreQueryFilters` (inference).
- **Moderation trail bias.** Restrict FKs, append-only messages and soft-deleted comments all keep history in the database. A hard delete of a sighting would go against all three.

## Historical Context (from prior changes)

- `context/archive/2026-10-04-register-and-login/plan.md:72`: "No role tables or role store … always empty until S-10." Supported (verified against `PigeonWatchUserStore.cs:17-20`).
- `context/archive/2026-10-04-register-and-login/research.md:144`: the research stage recommended "registering roles now" without seeding an admin. Contradicted by the plan that shipped (`plan.md:72`), which deferred roles. The "do not seed an admin in S-01" part holds.
- `context/archive/2026-11-07-comment-on-sighting/plan.md:5,92`: deleted comments stay as placeholders, and FKs are Restrict. Both supported by current code.
- `context/archive/2026-11-07-sighting-message-threads/plan-brief.md:26` and `plan.md:35`: messages are append-only, and "no admin removal of messages (S-10 gap, as for comments)". Admin removal of comments and messages is therefore an acknowledged gap. FR-017 names sightings and flocks only (`prd.md:106`).
- `context/archive/2026-11-09-edit-own-sighting-with-lock/plan-brief.md:36`: sighting writes use 403 for non-owners because sightings are readable by all members.
- `context/foundation/prd.md:107`: removal without a stated reason or appeal path was accepted for MVP ("small, trusted community and a single admin").
- `context/foundation/prd.md:167`: full audit/change-log history is a Non-Goal.
- `context/foundation/test-plan.md:46,63,217`: S-10/S-11 admin endpoints are anticipated. The admin side is to be re-evaluated when they land.
- Flocks (FR-011/FR-016) are parked (`roadmap.md:282-283`), so S-10 covers sightings only.

## Related Research

- `context/archive/2026-11-09-edit-own-sighting-with-lock/research.md` - sighting write path, ownership 403, engagement-lock queries
- `context/archive/2026-11-07-sighting-message-threads/research.md` - thread visibility and 404 rule; notes the missing admin concept (`:48`)
- `context/archive/2026-10-18-testing-api-privacy-authorization-contract/research.md` - endpoint guard and sweep harness
- `context/archive/2026-10-04-register-and-login/research.md` - Identity and bearer-token decisions

## Open Questions

Product or design decisions for `/10x-plan`. None is decided in `context/`.

1. **How is the admin designated?** This is the roadmap unknown (`roadmap.md:219`). Options observed in the code:
   - (a) A config-bound list of admin user ids or emails (App Service setting, user-secrets locally), following the `BlobStorageOptions` precedent. No migration is needed, tests set it through factory config, and it is a deploy-time change.
   - (b) An `IS_ADMIN` column on `USER_ACCOUNT`, set by SQL. It needs a migration and a test SQL helper.
   - (c) A full Identity role store (role tables, `IUserRoleStore`, `AddRoles`). It has the most surface and matches the existing `roles` claim channel.

   For any of these, decide whether admin status travels as a role claim (fills `currentUser.roles`, but is stale until refresh) or is checked per request in the service.
2. **Soft or hard removal?** Hard delete has to delete comments, threads, messages and both photo blobs, because every FK is Restrict. Soft delete (a removed-at column, plus optionally a removed-by column) keeps the moderation trail and leaves blobs stored. Should a removal be reversible by the admin, or recorded beyond the overwritable `UPDATE_USER`?
3. **What does the reporter see?** Does the removed sighting vanish from `GET sightings/mine` and the detail page for its own reporter (404, "no longer available"), or show a "removed by admin" state? FR-017 specifies no notice or reason (`prd.md:107`).
4. **Threads, unread count and comments of a removed sighting.** Do the participants' threads disappear from the inbox and unread badge, stay listed but read-only, or stay fully usable? Today `ParticipantThreads` does not join sightings (`SightingThreadRepository.cs:188-193`), and the SPA thread page shows a generic error on 404. Comment edit and delete by id (`SightingCommentRepository.cs:25-30,57,73`) also need a decision.
5. **Status code for a non-admin calling remove:** 403 (consistent with `NotSightingOwner`) or 404. Also, may an admin remove an already removed or unknown sighting idempotently, or does that give 404?
6. **Can the admin still see removed sightings?** An admin-only "removed" view, and whether an admin can still open the detail page after removal, are not covered by FR-017 and could be scoped out.
