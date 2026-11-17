# Admin removes sightings (S-10) Implementation Plan

## Overview

Introduce the single Admin role (PRD Access Control, `context/foundation/prd.md:160`) through a real ASP.NET Identity role store, assigned at startup from configuration. Let the admin soft-remove and restore a sighting (FR-017, `prd.md:106`). A removed sighting leaves the map and lists for everyone except the admin. It stays visible to its reporter (with a "Removed" state) and to the admin. Its message threads stay readable as read-only history. S-11 (`admin-moderate-users`) reuses the role.

## Current State Analysis

Full evidence: `context/changes/admin-remove-sightings/research.md`.

- **No role anywhere.** Identity uses `AddIdentityApiEndpoints<ApplicationUser>().AddUserStore<PigeonWatchUserStore>()` (`PigeonWatch/Api/DependencyInjection/AccountServiceCollectionExtensions.cs:12,29`) with no `IdentityDbContext`. The store implements exactly password, email, security stamp and lockout (`PigeonWatch/Api/Data/Identity/PigeonWatchUserStore.cs:17-20`). `ConfigurationController` already copies `ClaimTypes.Role` claims into `currentUser.roles` (`PigeonWatch/Api/WebApi/Controllers/ConfigurationController.cs:45`). That value is always empty today, and the SPA type carries it (`PigeonWatch/Frontend/src/app/core/configuration/configuration.service.ts:50-55`).
- **No delete path, and hard delete is blocked.** `SightingsController` has 8 routed actions and none deletes (`PigeonWatch/Api/WebApi/Controllers/SightingsController.cs:47-197`). All 7 FKs are `Restrict` (`PigeonWatch/Api/Data/PigeonWatchDbContext.cs:43-91`).
- **Soft delete is the house pattern.** Comments use `DeletedAt` with filters written per query (`PigeonWatch/Api/Data/Repositories/SightingCommentRepository.cs:69-82`). There is no `HasQueryFilter`. Messages are append-only.
- **One model for every sighting endpoint.** `SightingModel` (`PigeonWatch/Api/WebApi/Models/SightingModel.cs:3-30`) is used by create, get, update, nearby, list rows and mine. `IsMine` and `EditPermissions` are computed in `SightingViewModelCreator.Create` (`PigeonWatch/Api/WebApi/ViewModelCreators/SightingViewModelCreator.cs:8-32`). The SPA has one `Sighting` type (`PigeonWatch/Frontend/src/app/features/sightings/sighting.types.ts:31-45`).
- **Nothing runs against the database at startup.** Migrations are applied by the `migrate` job in `.github/workflows/deploy-api.yml`. The integration factory starts the host before `EnsureCreated` (`PigeonWatch/Api/IntegrationTests/PigeonWatchApiFactory.cs:225-229`).

## Desired End State

- An email listed in `Moderation:AdminEmails` belongs to a registered user. After the next API start and that user's next login or token refresh, `GET /configuration/general` returns `currentUser.roles == ["Admin"]`.
- The admin sees `Remove` on any sighting's detail page and confirms in a dialog. The sighting then shows a "Removed" alert and a `Restore` button.
  - **Admin:** keeps seeing the sighting on the map (removed marker variant) and in the list (Removed badge).
  - **Reporter:** sees it in My sightings with a Removed badge and can open its detail page read-only. Cannot edit it, comment on it or start a thread on it.
  - **Everyone else:** it is gone from the map and list. Its URL shows the existing "no longer available" alert.
  - **Thread participants:** the inbox still lists the thread and the messages stay readable. The reply form is replaced by a "sighting was removed" notice.
- Restore puts the sighting back for everyone.
- Verified by the unit, integration and architecture tests named per phase, plus one Playwright scenario (Phase 5).

### Key Discoveries:

- `AddRoles<TRole>()` swaps in `UserClaimsPrincipalFactory<TUser,TRole>`, which emits `ClaimTypes.Role` when the user store implements `IUserRoleStore`. `auth/refresh` rebuilds the principal, so roles are re-read on every refresh. This is ASP.NET Core framework behaviour, verified in Phase 1 by an integration test.
- `DatabaseModelTests` require every entity to have a single `ID uniqueidentifier` key and the four audit columns (`PigeonWatch/Api/ArchitectureTests/DatabaseModelTests.cs:106-163`). A composite-key join table is not allowed.
- `ServiceRegistrationTests` require an `I<Name>` interface for types ending in `Service`, `Provider`, `Repository`, `ViewModelCreator` or `Mapper` (`PigeonWatch/Api/ArchitectureTests/ServiceRegistrationTests.cs:8,39-77`).
- Every public controller action must return `Task<ActionResult<T>>` with `T` in WebApi.Models (`PigeonWatch/Api/ArchitectureTests/ControllerActionTests.cs:11-29`). The comment `DELETE` returns its model with 200 (`PigeonWatch/Api/WebApi/Controllers/SightingCommentsController.cs:73-85`).
- `HttpContextCurrentUserProvider` returns `"SYSTEM"` when no user is signed in (`PigeonWatch/Api/Data/Auditing/HttpContextCurrentUserProvider.cs:13-18`), which the privacy sweep accepts.
- `GetValidationProblem` maps any error code that has no explicit branch to a 400 `ValidationProblemDetails` keyed by code (`PigeonWatch/Api/WebApi/Controllers/FailureLoggingControllerBase.cs:133-147`). `DescriptionLocked` uses it. The comment and thread controllers' `GetFailure` helpers fall through to it too.
- The privacy sweep and the endpoint guards iterate `EndpointFixtureRegistry` as anonymous, Bob and Alice. None of them is an admin (`PigeonWatch/Api/IntegrationTests/PrivacySweepTests.cs:49-85`).
- `label-coverage.spec.ts` fails on any template text node not routed through `translate` and on any used key missing from the generated labels.

## What We're NOT Doing

- No admin UI to grant or revoke the role. Assignment is configuration plus restart. The bootstrap never removes a role, so revocation needs SQL.
- No removal reason, notification or appeal (accepted in `prd.md:107`).
- No admin removal of individual comments or messages. That gap stays as recorded in `context/archive/2026-11-07-sighting-message-threads/plan.md:35`.
- No hard delete or blob cleanup. Photos of removed sightings stay in storage.
- No flocks (FR-011/FR-016 are parked).
- No admin-only list page of removed sightings. The admin finds them on the map and list.
- No change to the reporter's submission cooldown. Removed sightings still count.
- No rate-limit policy on remove and restore, because they are admin-only.
- No Polish labels. Only `UiLabels.resx` exists.
- No S-11 warn or ban behaviour.

## Implementation Approach

Bottom-up, every API phase green on its own tests before the SPA starts.

- Phase 1 builds the role so a test can mint an admin token.
- Phase 2 adds the removal columns and the two admin endpoints.
- Phase 3 applies one visibility rule on every read and write path.
- Phase 4 adds the SPA. Phase 5 proves the flow in a browser and updates docs.

Visibility is one business rule defined once in BusinessLogic: a removed sighting is visible to its reporter and to an admin. Services receive the caller's id and an `isAdmin` flag, read by the controller from `User.IsInRole(RoleNames.Admin)`.

Authorization for remove and restore is a service-level check returning a `NotAdmin` code, mapped to a logged 403. This follows the `NotSightingOwner` precedent rather than a policy attribute, whose 403 skips the controller's Warning log.

## Critical Implementation Details

**Timing & lifecycle.** The bootstrap must not block startup or crash the host. Azure SQL can be cold, and migrations are applied by CI, not at startup. Run it as a `BackgroundService`. Log a failure at Error with its exception and keep the API serving. Log a listed email with no registered user at Warning.

**Test-host ordering.** `PigeonWatchApiFactory` starts the host before `EnsureCreated`, so the worker would query missing tables. The factory removes the worker registration. Tests call the bootstrapper's public `RunAsync` after `CreateUserAsync` has registered the admin, then log the admin in again so the token carries the role.

**Claims staleness.** A granted role reaches a session only at its next login or `auth/refresh`. The access token lifetime defaults to 1 hour. This is accepted for S-10 and recorded in `Api/CLAUDE.md` for S-11.

## Phase 1: Admin role foundation (API)

### Overview

Store roles, issue the role claim, and grant Admin from configuration. No sighting behaviour changes.

### Changes Required:

#### 1. Role entities and migration

**File**: `PigeonWatch/Api/Data/Entities/RoleEntity.cs`, `PigeonWatch/Api/Data/Entities/UserRoleEntity.cs`, `PigeonWatch/Api/Data/PigeonWatchDbContext.cs`, new migration `AddRoles`

**Intent**: Add the two tables Identity roles need, shaped to pass the architecture tests.

**Contract**:
- `ROLE` table: `ID`, `NAME`, `NORMALIZED_NAME` (unique index), `CONCURRENCY_STAMP`, audit columns.
- `USER_ROLE` table: `ID`, `USER_ID` → `USER_ACCOUNT`, `ROLE_ID` → `ROLE`, unique index on `(USER_ID, ROLE_ID)`, audit columns.
- Both FKs are `Restrict`, and both entities are internal and derive from `AuditableEntity`.
- Generate the migration with the `dotnet ef migrations add` command from `PigeonWatch/Api/CLAUDE.md`. It does not seed the role, because the bootstrap creates it.

#### 2. Role type, role store and user-role store

**File**: `PigeonWatch/Api/Data/Identity/ApplicationRole.cs`, `PigeonWatch/Api/Data/Identity/PigeonWatchRoleStore.cs`, `PigeonWatch/Api/Data/Identity/PigeonWatchUserStore.cs`, a role mapper under `PigeonWatch/Api/Data/Mappers/` with its interface

**Intent**: Give Identity a role store and let the user store answer role questions, so `UserManager.AddToRoleAsync` and the claims factory work.

**Contract**:
- `ApplicationRole` is a POCO with `Id`, `Name`, `NormalizedName` and `ConcurrencyStamp`.
- `PigeonWatchRoleStore : IRoleStore<ApplicationRole>` follows the user store's patterns: `AsNoTracking` finds, a concurrency failure returns `IdentityResult.Failed`, and it never sets audit columns.
- `PigeonWatchUserStore` additionally implements `IUserRoleStore<ApplicationUser>`.
- `AddToRoleAsync` throws `InvalidOperationException` for an unknown role.
- `UserManager.AddToRoleAsync` must persist the `USER_ROLE` row. It ends in `UpdateAsync`'s `SaveChangesAsync` on the same scoped context, and a unit test proves it.

#### 3. Identity registration and role names

**File**: `PigeonWatch/Api/DependencyInjection/AccountServiceCollectionExtensions.cs`, `PigeonWatch/Api/BusinessObjects/RoleNames.cs`

**Intent**: Wire the role store into the existing Identity builder, and give every layer one constant for the role name.

**Contract**:
- The existing builder chain gains `.AddRoles<ApplicationRole>().AddRoleStore<PigeonWatchRoleStore>()`.
- `RoleNames.Admin = "Admin"` is a const-only static class, which `StaticClassTests` allow.

#### 4. Moderation options and the bootstrap

**File**: `PigeonWatch/Api/Data/Identity/ModerationOptions.cs`, `PigeonWatch/Api/Data/Identity/AdminRoleBootstrapper.cs`, a `BackgroundService` wrapper (for example `AdminRoleBootstrapWorker`) registered in `PigeonWatch/Api/DependencyInjection/`

**Intent**: Grant Admin to each configured, registered email at every API start, idempotently.

**Contract**:
- `ModerationOptions` has `SectionName = "Moderation"` and `AdminEmails`, a list that defaults to empty, so the architecture-test container stays valid. It is bound like `BlobStorageOptions` (`PigeonWatch/Api/DependencyInjection/DataServiceCollectionExtensions.cs:31-34`).
- `AdminRoleBootstrapper.RunAsync(CancellationToken)` is public. It opens a scope and uses `RoleManager` and `UserManager`.
  - It creates the Admin role if it is missing.
  - It adds each listed email's user if not already in the role.
  - It logs a missing user at Warning and never removes a role.
- The worker calls `RunAsync` once and logs any exception at Error.
- Type names avoid the suffixes that trigger the `I<Name>` rule. Only DependencyInjection registers them.

#### 5. Test support

**File**: `PigeonWatch/Api/IntegrationTests/PigeonWatchApiFactory.cs`, `PigeonWatch/Api/IntegrationTests/TestUser.cs` (if a helper fits there)

**Intent**: Let a test create an admin user with a token that carries the role.

**Contract**:
- The factory accepts optional admin emails, added to its in-memory configuration as `Moderation:AdminEmails:N`.
- The factory removes the bootstrap worker registration.
- New helper `CreateAdminAsync(...)`: register the user, run `AdminRoleBootstrapper.RunAsync`, log in again, and return a `TestUser` whose token carries the role.

#### 6. Tests

**File**: `PigeonWatch/Api/UnitTests/PigeonWatchUserStoreTests.cs` (and a role-store test class beside it), `PigeonWatch/Api/UnitTests/AdminRoleBootstrapperTests.cs`, `PigeonWatch/Api/IntegrationTests/AdminRoleContractTests.cs`

**Intent**: Prove the store, the bootstrap and the claim end to end.

**Contract**:
- Store tests on SQLite: add to role then get roles, is in role, unknown role throws, and the row persists through `UserManager.AddToRoleAsync`.
- Bootstrap tests:
  - It creates the role.
  - It grants a listed registered email.
  - A second run is a no-op.
  - An unlisted user is untouched.
  - An unregistered email is logged at Warning and does not throw.
- Contract tests:
  - `Admin_listed_in_configuration_gets_the_admin_role_in_general_configuration`.
  - `Member_not_listed_has_no_roles`.
  - `Role_granted_after_login_appears_after_token_refresh`.

### Success Criteria:

#### Automated Verification:

- Solution builds with style rules: `dotnet build PigeonWatch/Api/PigeonWatchApi.slnx`
- Architecture tests pass, including the ID key, audit columns, naming and registration rules for the new types: `dotnet test PigeonWatch/Api/ArchitectureTests/PigeonWatch.ArchitectureTests.csproj`
- No pending model changes after the migration: `dotnet ef migrations has-pending-model-changes --project PigeonWatch/Api/Data/PigeonWatch.Data.csproj --startup-project PigeonWatch/Api/WebApi.Host/PigeonWatch.WebApi.Host.csproj`
- Unit tests pass, including the new store and bootstrapper tests: `dotnet test PigeonWatch/Api/UnitTests/PigeonWatch.UnitTests.csproj`
- Integration tests pass, including `AdminRoleContractTests` and the unchanged privacy sweep: `dotnet test PigeonWatch/Api/IntegrationTests/PigeonWatch.IntegrationTests.csproj`

#### Manual Verification:

- Against LocalDB: apply the migration, set `Moderation:AdminEmails:0` in user-secrets to a registered account, and restart the API. Logging in as that account, `GET /configuration/general` (via `curl.exe` or the SPA network tab) shows `roles: ["Admin"]`. Another account shows `roles: []`.
- Start the API with the database unreachable. The log shows one Error from the bootstrap and the API still serves requests.

**Implementation Note**: After automated verification passes, pause for manual confirmation before Phase 2.

---

## Phase 2: Remove and restore endpoints (API)

### Overview

Record removal on the sighting and expose the two admin actions. Visibility is not changed yet. Phase 3 does that.

### Changes Required:

#### 1. Removal columns

**File**: `PigeonWatch/Api/Data/Entities/SightingEntity.cs`, `PigeonWatch/Api/Data/PigeonWatchDbContext.cs`, new migration `AddSightingRemoval`

**Intent**: Store when and by whom a sighting was removed. The remover's id must survive later `UPDATE_USER` overwrites.

**Contract**:
- `REMOVED_AT datetime2 NULL`.
- `REMOVED_BY uniqueidentifier NULL`, FK → `USER_ACCOUNT` with `Restrict`.
- Both are null for every existing row.

#### 2. Business object, mapper and API model

**File**: `PigeonWatch/Api/BusinessObjects/Sighting.cs`, `PigeonWatch/Api/Data/Mappers/SightingMapper.cs`, `PigeonWatch/Api/WebApi/Models/SightingModel.cs`, `PigeonWatch/Api/WebApi/ViewModelCreators/SightingViewModelCreator.cs`

**Intent**: Carry the removal time to every sighting response. Never expose who removed it.

**Contract**:
- `Sighting` gains a trailing optional `DateTime? RemovedAt = null`. The mapper sets it with `DateTimeKind.Utc`, as it does `CreatedAt`.
- `SightingModel` gains `RemovedAt` (`DateTime?`, required property). No `RemovedBy` appears in any model.

#### 3. Repository writes

**File**: `PigeonWatch/Api/Data/Repositories/SightingRepository.cs` and its interface

**Intent**: Set and clear the removal fields idempotently.

**Contract**:
- `RemoveAsync(Guid sightingId, Guid removedBy, DateTime removedAt, ct) → Sighting?`
  - Returns null for an unknown id.
  - An already-removed row keeps its original `RemovedAt` and `RemovedBy`.
- `RestoreAsync(Guid sightingId, ct) → Sighting?`
  - Clears both fields.
  - A sighting that is not removed is returned unchanged.

#### 4. Service and error codes

**File**: `PigeonWatch/Api/BusinessLogic/Services/SightingService.cs` and its interface, `PigeonWatch/Api/BusinessObjects/SightingErrorCodes.cs`, a result type in BusinessObjects mirroring `SightingUpdateResult`

**Intent**: Admin-only remove and restore, with the time taken from the injected `TimeProvider`.

**Contract**:
- `RemoveAsync(Guid sightingId, Guid userId, bool isAdmin, ct)` and `RestoreAsync(...)`.
- Check order: not admin gives `NotAdmin`, then an unknown id gives `SightingNotFound`, then success.
- Add `NotAdmin` and `SightingRemoved` to `SightingErrorCodes`. `SightingRemoved` is used in Phase 3.

#### 5. Controller actions

**File**: `PigeonWatch/Api/WebApi/Controllers/SightingsController.cs`, `PigeonWatch/Api/WebApi/Controllers/FailureLoggingControllerBase.cs`

**Intent**: Route the two admin actions with the house error mapping.

**Contract**:
- `[HttpDelete("{id:guid}")] Remove` and `[HttpPost("{id:guid}/restore")] Restore`, both `Task<ActionResult<SightingModel>>` returning 200 with the model.
- `isAdmin` comes from `User.IsInRole(RoleNames.Admin)`.
- `NotAdmin` maps to a new `ForbiddenForNonAdmin()` (403, Warning log, like `ForbiddenForNonOwner`). `SightingNotFound` maps to `NotFoundForSighting`.

#### 6. Fixtures and tests

**File**: `PigeonWatch/Api/IntegrationTests/EndpointFixtureRegistry.cs`, `PigeonWatch/Api/IntegrationTests/SightingRemovalContractTests.cs`, `PigeonWatch/Api/UnitTests/SightingServiceTests.cs`, `PigeonWatch/Api/UnitTests/SightingRepositoryTests.cs`, `PigeonWatch/Api/UnitTests/SightingsControllerTests.cs`, `PigeonWatch/Api/UnitTests/ControllerFailureLoggingTests.cs`

**Intent**: Satisfy the endpoint guards, and prove the authorization and idempotency contract.

**Contract**:
- Fixtures keyed `DELETE sightings/{id:guid}` and `POST sightings/{id:guid}/restore`, each with an unknown-id and a seeded-sighting request. In the sweep they answer 403 for Bob and Alice, which is acceptable because the sweep fails only on 401, 429, 5xx or a leaked email.
- Contract tests:
  - `Member_cannot_remove_a_sighting` (403, and Alice reads it back unchanged).
  - `Admin_removes_a_sighting_and_the_response_carries_removed_at`.
  - `Removing_twice_keeps_the_first_removal`.
  - `Admin_restores_a_removed_sighting`.
  - `Unknown_sighting_is_404_for_the_admin`.
  - `Removal_records_the_admin_in_removed_by`, read by SQL helper.
- Unit tests cover the check order and the 403 Warning log.

### Success Criteria:

#### Automated Verification:

- Solution builds: `dotnet build PigeonWatch/Api/PigeonWatchApi.slnx`
- Architecture tests pass, including the controller return-type rule for the two new actions: `dotnet test PigeonWatch/Api/ArchitectureTests/PigeonWatch.ArchitectureTests.csproj`
- No pending model changes: `dotnet ef migrations has-pending-model-changes --project PigeonWatch/Api/Data/PigeonWatch.Data.csproj --startup-project PigeonWatch/Api/WebApi.Host/PigeonWatch.WebApi.Host.csproj`
- Unit tests pass: `dotnet test PigeonWatch/Api/UnitTests/PigeonWatch.UnitTests.csproj`
- Integration tests pass, including `SightingRemovalContractTests`, `EndpointCoverageTests`, `EndpointProtectionTests` and `PrivacySweepTests`: `dotnet test PigeonWatch/Api/IntegrationTests/PigeonWatch.IntegrationTests.csproj`

#### Manual Verification:

- Against LocalDB with the admin from Phase 1: `curl.exe -X DELETE` on a sighting as the admin returns 200 with `removedAt` set. As a member it returns 403 and the API log shows one Warning. `POST …/restore` clears `removedAt`.

**Implementation Note**: After automated verification passes, pause for manual confirmation before Phase 3.

---

## Phase 3: Visibility rules on every read and write path (API)

### Overview

Apply the rule "a removed sighting is visible to its reporter and to an admin" everywhere. Writes return 400 `SightingRemoved` where the caller can see the sighting and 404 where they cannot. Threads stay readable.

### Changes Required:

#### 1. The rule

**File**: `PigeonWatch/Api/BusinessLogic/Services/SightingVisibilityProvider.cs` and its interface, `PigeonWatch/Api/BusinessObjects/SightingAccess.cs`

**Intent**: Define the visibility decision once, so the three services cannot drift.

**Contract**:
- `SightingAccess(Guid ReporterId, DateTime? RemovedAt)` is a business object.
- `CanView(SightingAccess, Guid viewerId, bool viewerIsAdmin)` returns `RemovedAt is null || viewerId == ReporterId || viewerIsAdmin`. `IsRemoved` is a convenience.
- Registered in DependencyInjection.

#### 2. Sighting reads

**File**: `PigeonWatch/Api/Data/Repositories/SightingRepository.cs` and interface, `PigeonWatch/Api/BusinessLogic/Services/SightingService.cs` and interface, `PigeonWatch/Api/WebApi/Controllers/SightingsController.cs`

**Intent**: Hide removed sightings from non-admins on the map and list. Restrict detail and photo to reporter and admin. Keep My sightings unfiltered.

**Contract**:
- `ListInBoxAsync` and `ListPageInRadiusAsync` gain `bool includeRemoved`. When false they add `RemovedAt == null` before both the item query and `CountAsync`.
- `GetNearbyAsync` and `GetListPageAsync` pass `isAdmin`. For the list it may travel in `SightingListQuery`.
- `GetAsync`, `OpenPhotoAsync` and `OpenPhotoThumbnailAsync` take `userId` and `isAdmin`, and return null (404) when `CanView` is false. The photo paths need the reporter and removal state, so `GetPhotoBlobNameAsync` is extended or paired with an access lookup.
- `GetPhoto` and `GetPhotoThumbnail` start parsing the user id, with the `UnauthorizedForInvalidIdentity` branch.
- `ListByReporterAsync` is unchanged: removed sightings appear with `removedAt`.

#### 3. Sighting writes

**File**: `PigeonWatch/Api/BusinessLogic/Services/SightingService.cs`, `PigeonWatch/Api/WebApi/Controllers/SightingsController.cs`

**Intent**: Freeze a removed sighting for its owner.

**Contract**:
- `GetEditPermissionsAsync` returns null when the sighting is removed, so `editPermissions` is null in every response.
- `UpdateAsync` order:
  1. Validation.
  2. Not found or `CanView` false gives `SightingNotFound` (404).
  3. Not owner gives `NotSightingOwner` (403).
  4. Removed gives `SightingRemoved` (400 via `GetValidationProblem`, added to `loggedErrorCodes`).
  5. The existing transition, lock and photo steps.
- No blob is written on the rejected path.

#### 4. Comments

**File**: `PigeonWatch/Api/Data/Repositories/SightingCommentRepository.cs` and interface, `PigeonWatch/Api/BusinessLogic/Services/SightingCommentService.cs` and interface, `PigeonWatch/Api/WebApi/Controllers/SightingCommentsController.cs`, `PigeonWatch/Api/BusinessObjects/SightingCommentErrorCodes.cs`

**Intent**: Comments of a removed sighting are readable only by those who can see it, and writable by nobody.

**Contract**:
- `GetSightingAccessAsync(sightingId) → SightingAccess?` replaces the `SightingExistsAsync` call sites.
- `ListAsync`, `CreateAsync`, `UpdateAsync` and `DeleteAsync` take `userId` and `isAdmin`.
- Check order for every method:
  1. The existing validation.
  2. The access lookup: an unknown sighting, or `CanView` false, gives `SightingNotFound` (404).
  3. The existing comment-not-found and author checks (404 / 403).
  4. On a removed sighting, the writes give `SightingRemoved` (400, an alias of the sighting code), before the cooldown check.
- Because of step 2, the comment by-id paths no longer act on a comment whose sighting the caller cannot see.

#### 5. Threads

**File**: `PigeonWatch/Api/Data/Repositories/SightingThreadRepository.cs` and interface, `PigeonWatch/Api/BusinessLogic/Services/SightingThreadService.cs`, `PigeonWatch/Api/BusinessObjects/SightingThread.cs`, `PigeonWatch/Api/Data/Mappers/SightingThreadMapper.cs`, `PigeonWatch/Api/WebApi/Models/SightingThreadModel.cs`, `PigeonWatch/Api/WebApi/ViewModelCreators/SightingThreadViewModelCreator.cs`, `PigeonWatch/Api/BusinessObjects/SightingMessageErrorCodes.cs`, `PigeonWatch/Api/WebApi/Controllers/SightingThreadsController.cs`

**Intent**: Keep conversations readable as history, but stop new messages on a removed sighting.

**Contract**:
- `GetSightingOwnerAsync` becomes, or is joined by, an access lookup returning `SightingAccess?`. `StartOrAppendAsync`'s direct `db.Sightings` read is covered by the service check before it.
- `StartAsync`:
  1. Unknown sighting, or `CanView` false, gives `SightingNotFound` (404).
  2. The existing own-sighting check.
  3. Removed gives `SightingRemoved` (400).
- `ReplyAsync`: the existing `ThreadNotFound` check stays first, so a non-participant still gets 404. A removed sighting then gives `SightingRemoved` (400).
- These stay unfiltered: `ListForSightingAsync`, `ListMessagesAsync`, `MarkReadAsync`, the inbox and the unread count.
- `SightingThread` and `SightingThreadModel` gain `IsSightingRemoved`, set by `SightingThreadMapper.ToThread` from `entity.Sighting.RemovedAt`. Every `ToThread` call site must load `Sighting`.

#### 6. Tests and fixtures

**File**: `PigeonWatch/Api/UnitTests/SightingVisibilityProviderTests.cs`, `SightingServiceTests.cs`, `SightingServicePhotoTests.cs`, `SightingCommentServiceTests.cs`, `SightingThreadServiceTests.cs`, `SightingRepositoryTests.cs`, the creator tests; `PigeonWatch/Api/IntegrationTests/SightingRemovalVisibilityContractTests.cs`; `EndpointFixtureRegistry.cs` only if a fixture needs a new error variant

**Intent**: Prove each row of the visibility table through the real pipeline, with expected values taken from this plan's decisions.

**Contract**: Integration tests, as Alice (reporter), Bob (member, thread participant), Carol (member) and an admin, after the admin removes Alice's sighting:

| Path | Alice | Bob/Carol | Admin |
|---|---|---|---|
| nearby, list (including `totalCount`) | absent | absent | present with `removedAt` |
| mine (Alice) | present with `removedAt` | – | – |
| `GET sightings/{id}`, photo, thumbnail | 200, `editPermissions` null | 404 | 200 |
| `GET …/comments` | 200 | 404 | 200 |
| `POST …/comments` | 400 `SightingRemoved` | 404 | 400 |
| `PUT sightings/{id}` | 400 `SightingRemoved` | 404 | 403 |
| threads inbox, messages, mark-read (Bob's thread) | 200, `isSightingRemoved` true | Bob 200; Carol 404 on the thread as today | – |
| reply in Bob's thread | 400 | 400 | – |
| start thread | – | 404 | 400 |

- After restore, every row returns to today's behaviour. One test proves this.

### Success Criteria:

#### Automated Verification:

- Solution builds: `dotnet build PigeonWatch/Api/PigeonWatchApi.slnx`
- Architecture tests pass: `dotnet test PigeonWatch/Api/ArchitectureTests/PigeonWatch.ArchitectureTests.csproj`
- Unit tests pass, including the visibility provider and the service check-order tests: `dotnet test PigeonWatch/Api/UnitTests/PigeonWatch.UnitTests.csproj`
- Integration tests pass, including `SightingRemovalVisibilityContractTests` and the existing edit, comment, thread, list and photo contract tests unchanged: `dotnet test PigeonWatch/Api/IntegrationTests/PigeonWatch.IntegrationTests.csproj`

#### Manual Verification:

- Against LocalDB, remove a sighting as the admin. In the SPA (still without moderation UI), a second member no longer sees it on the map and gets "no longer available" on its URL. The reporter still finds it in My sightings.

**Implementation Note**: After automated verification passes, pause for manual confirmation before Phase 4.

---

## Phase 4: SPA moderation UI

### Overview

Show Remove and Restore to the admin. Render the removed state for reporter, admin and thread participants. Every visible string goes through the resx.

### Changes Required:

#### 1. Session, types and API

**File**: `PigeonWatch/Frontend/src/app/core/auth/session.service.ts`, `PigeonWatch/Frontend/src/app/features/sightings/sighting.types.ts`, `PigeonWatch/Frontend/src/app/features/sightings/sightings-api.ts`, `PigeonWatch/Frontend/src/app/features/sightings/sighting-errors.ts`

**Intent**: Expose admin status and the new API surface.

**Contract**:
- `SessionService.isAdmin = computed(...)` is true when `currentUser.roles` includes `'Admin'`.
- `Sighting.removedAt: string | null`. `SightingThread.isSightingRemoved: boolean`.
- `SightingsApi.remove(id)` is `DELETE` and `restore(id)` is `POST …/restore`. Both return `Observable<Sighting>`.
- New `sightingModerationFailure` mapper: 403 gives forbidden, 404 gives notFound, anything else is unexpected. Forbidden and unexpected are reported through `ErrorReportingService`.
- `SightingRemoved` (400) on any write is mapped to a "removed" failure kind with its own message.

#### 2. Detail page

**File**: `PigeonWatch/Frontend/src/app/features/sightings/sighting-detail-page/sighting-detail-page.{ts,html}`, new `sighting-detail-page/remove-sighting-dialog/`

**Intent**: Admin actions and the read-only removed state.

**Contract**:
- `headerActions` gains `Remove` (admin, not removed), which opens `RemoveSightingDialog`, modelled on `delete-comment-dialog`. It also gains `Restore` (admin, removed), which needs no dialog.
- A busy state disables the button. Success goes through `applyState` with the returned sighting and focuses the heading. Failure shows an `app-alert kind="error"` with an error reference.
- When `removedAt` is set:
  - An `app-alert kind="info"` shows "This sighting was removed by an admin."
  - The edit link is hidden, because `editPermissions` is null and the condition gains `!removedAt`.
  - The contact-reporter button is hidden. The open-conversation link stays.
  - `app-sighting-comments` receives `readOnly`.

#### 3. Comments read-only

**File**: `PigeonWatch/Frontend/src/app/features/sightings/sighting-detail-page/sighting-comments/sighting-comments.{ts,html}`

**Intent**: List stays, writing stops.

**Contract**: New `readOnly = input(false)` hides the add form and the per-comment edit and delete controls.

#### 4. Map, list and My sightings

**File**: `PigeonWatch/Frontend/src/app/shared/map/map-marker.ts`, `PigeonWatch/Frontend/src/app/shared/map/map-view/map-view.ts`, `PigeonWatch/Frontend/src/styles/_common.scss` and `_tokens.scss`, `PigeonWatch/Frontend/src/app/features/sightings/nearby-map-page/nearby-map-page.{ts,html}`, `PigeonWatch/Frontend/src/app/features/sightings/shared/sighting-row/sighting-row.{ts,html,scss}`

**Intent**: Make a removed sighting recognisable wherever the API returns it. That is the admin's map and list and the reporter's My sightings.

**Contract**:
- `MapMarker.removed?: boolean`. `applyMarkers` appends `pw-map-marker--removed`, styled from a new token (muted, dashed outline) in both themes.
- Nearby markers use a new `map.markerLabelRemoved` label. The selected-sighting card shows a Removed badge.
- `sighting-row` renders a "Removed" badge in its meta line when `removedAt` is set, which covers the list and My sightings.

#### 5. Message thread read-only

**File**: `PigeonWatch/Frontend/src/app/features/messages/message-thread/message-thread.{ts,html}`

**Intent**: History stays readable, and new messages are not offered.

**Contract**:
- When `thread()?.isSightingRemoved`, the reply form is replaced by an `app-alert kind="info"` notice.
- A send that still races into 400 `SightingRemoved` shows the same notice instead of a generic error.

#### 6. Labels, kitchen sink, specs

**File**: `PigeonWatch/Api/BusinessObjects/Resources/UiLabels.resx`, regenerated `core/resources/generated/*`, `PigeonWatch/Frontend/src/app/dev/kitchen-sink/*`, specs beside each changed component

**Intent**: Keep label coverage and the kitchen-sink state matrix complete.

**Contract**:
- New keys under `sighting.detail.*`, `sighting.remove.*`, `sighting.restore.*`, `sighting.removed.*`, `map.markerLabelRemoved` and `sighting.messages.*`, plus the error messages.
- Kitchen-sink cells and marker assertions for: detail as admin (removable), detail removed (as admin and as reporter), removed row badge, removed map marker, read-only thread.
- Specs that newly inject `SessionService` provide a stub with `isAdmin`.
- Specs to add or extend:
  - Remove dialog: confirm calls `remove`, cancel does nothing, 403 is reported, 404 shows notFound.
  - Restore button.
  - Hidden edit and contact controls when removed.
  - Comments read-only.
  - Row badge and removed marker class.
  - Thread read-only.
  - `sightings-api` remove and restore.

### Success Criteria:

#### Automated Verification:

- Labels regenerate and the app and spec typechecks pass: `npm run build` and `npx tsc -p tsconfig.spec.json --noEmit` (run in `PigeonWatch/Frontend`)
- All frontend specs pass, including `label-coverage.spec.ts`, the kitchen-sink spec and the new and extended component specs: `npm test -- --watch=false` (run in `PigeonWatch/Frontend`)

#### Manual Verification:

- Against the local API with an admin account:
  - Remove a sighting from its detail page: the dialog confirms, the removed alert and Restore appear, and the marker turns muted on the map.
  - Restore it: the sighting is back to normal.
- As the reporter: My sightings shows the Removed badge, and the detail page has no edit, comment or contact controls.
- As a thread participant: the thread shows the notice and no reply form.
- Check all of the above in light and dark theme.

**Implementation Note**: After automated verification passes, pause for manual confirmation before Phase 5.

---

## Phase 5: E2E scenario and docs

### Overview

Protect the end-to-end moderation flow in a real browser and record the new contracts.

### Changes Required:

#### 1. E2E spec

**File**: `PigeonWatch/Frontend/tests/e2e/admin-remove-sighting.spec.ts`, `PigeonWatch/Frontend/.env` (local, gitignored), `context/foundation/test-stack.md` (E2E credentials line)

**Intent**: A reporter's sighting removed by the admin leaves the map for a member, shows as Removed to the reporter, and comes back on restore.

**Contract**:
- The admin logs in inside the test in a fresh context with `E2E_ADMIN_USERNAME` / `E2E_ADMIN_PASSWORD`. A missing variable fails with a message, as `auth.setup.ts` does.
- The local API lists that email in user-secrets `Moderation:AdminEmails:0` and has been restarted once after the account was registered.
- Flow:
  1. The reporter registers and reports a sighting.
  2. The admin removes it through the dialog.
  3. The reporter sees the Removed badge in My sightings.
  4. A third user registers and gets "no longer available" on the sighting's URL.
  5. The admin restores it.
  6. The third user sees it on the map.
- Role- and label-based selectors only.

#### 2. Documentation

**File**: `PigeonWatch/Api/CLAUDE.md`, `PigeonWatch/Frontend/CLAUDE.md`, `context/foundation/test-plan.md`

**Intent**: The next agent (S-11) finds the role mechanism, the visibility rule and the test helpers without rediscovery.

**Contract**:
- `Api/CLAUDE.md`:
  - Roles and bootstrap, including `Moderation:AdminEmails` and claims staleness.
  - Removal columns and the visibility rule with its status table.
  - The `CreateAdminAsync` helper.
- `Frontend/CLAUDE.md`: `isAdmin`, the removed states, the new kitchen-sink cells and the E2E admin variables.
- `test-plan.md`: re-evaluate the admin side as `test-plan.md:217` asks. Record that the sweep covers the admin endpoints only as non-admin, and that admin success paths live in the removal contract tests.

### Success Criteria:

#### Automated Verification:

- The new E2E spec passes against the local API and dev server: `npx playwright test tests/e2e/admin-remove-sighting.spec.ts` (run in `PigeonWatch/Frontend`)
- The full E2E suite still passes: `npx playwright test` (run in `PigeonWatch/Frontend`)

#### Manual Verification:

- Read `Api/CLAUDE.md` and `Frontend/CLAUDE.md`. Each states the role mechanism, the visibility rule and the admin test setup accurately.
- Set `Moderation__AdminEmails__0` in the production App Service configuration for the real admin account before or with the deploy. Confirm `roles: ["Admin"]` after that admin's next login.

---

## Testing Strategy

### Unit Tests:

- Role store and user-role store on SQLite, including persistence through `UserManager.AddToRoleAsync`.
- Bootstrapper: idempotent grant, unknown email logged, never revokes.
- Visibility provider: every combination of removed, reporter and admin.
- Service check order for remove, restore, update, comment writes, thread start and reply. Validation comes first, then 404, then 403, then 400 `SightingRemoved`.
- Repository filters: `includeRemoved` on box and radius queries, including the count. Remove and restore are idempotent.
- Controller mappings and Warning logs for `NotAdmin` and `SightingRemoved`.

### Integration Tests:

- `AdminRoleContractTests`, `SightingRemovalContractTests`, `SightingRemovalVisibilityContractTests` (the Phase 3 table).
- Endpoint coverage, protection and privacy sweep with the two new fixtures.

### Manual Testing Steps:

1. Configure an admin email locally, restart the API, log in as that admin, and confirm `roles`.
2. Remove a sighting from the detail page. Check the map marker, the list badge and that Restore is available.
3. As the reporter, check My sightings and the read-only detail page.
4. As a thread participant, check the read-only thread.
5. Restore, and check that everything is back for a member.

## Performance Considerations

The `RemovedAt == null` predicate is added to the box and radius queries. Their selectivity is unchanged, because almost all rows are not removed. No new index is needed for an MVP-sized table. Remove and restore are single-row updates.

## Migration Notes

- Two migrations, `AddRoles` and `AddSightingRemoval`. Both are additive: new tables and two nullable columns. They are applied by the existing CI `migrate` job.
- Existing sightings stay visible because `REMOVED_AT` is null.
- Rollback is the EF down migration. Removed sightings would become visible again.
- Production needs the App Service setting `Moderation__AdminEmails__0`. Without it the API runs normally with no admin.

## References

- Research: `context/changes/admin-remove-sightings/research.md`
- Roadmap slice: `context/foundation/roadmap.md:210-221`; PRD FR-017 `context/foundation/prd.md:106-107`
- Ownership 403 precedent: `context/archive/2026-11-09-edit-own-sighting-with-lock/plan-brief.md:36`
- Soft-delete precedent: `PigeonWatch/Api/Data/Repositories/SightingCommentRepository.cs:69-82`
- Destructive dialog precedent: `PigeonWatch/Frontend/src/app/features/sightings/sighting-detail-page/sighting-comments/delete-comment-dialog/`
- Endpoint test guide: `context/foundation/test-plan.md:172-186`

## Progress

> Convention: `- [ ]` pending, `- [x]` done. Append ` — <commit sha>` when a step lands. Do not rename step titles. See `references/progress-format.md`.

### Phase 1: Admin role foundation (API)

#### Automated

- [x] 1.1 Solution builds with style rules — df5fd76
- [x] 1.2 Architecture tests pass, including the ID key, audit columns, naming and registration rules for the new types — df5fd76
- [x] 1.3 No pending model changes after the migration — df5fd76
- [x] 1.4 Unit tests pass, including the new store and bootstrapper tests — df5fd76
- [x] 1.5 Integration tests pass, including AdminRoleContractTests and the unchanged privacy sweep — df5fd76

#### Manual

- [x] 1.6 Configured admin sees roles ["Admin"] in general configuration against LocalDB; another account sees [] — df5fd76
- [x] 1.7 API keeps serving with one logged Error when the bootstrap cannot reach the database — df5fd76

### Phase 2: Remove and restore endpoints (API)

#### Automated

- [x] 2.1 Solution builds — 7110820
- [x] 2.2 Architecture tests pass, including the controller return-type rule for the two new actions — 7110820
- [x] 2.3 No pending model changes — 7110820
- [x] 2.4 Unit tests pass — 7110820
- [x] 2.5 Integration tests pass, including SightingRemovalContractTests, EndpointCoverageTests, EndpointProtectionTests and PrivacySweepTests — 7110820

#### Manual

- [x] 2.6 DELETE as admin returns 200 with removedAt, as member 403 with one Warning; restore clears removedAt — 7110820

### Phase 3: Visibility rules on every read and write path (API)

#### Automated

- [x] 3.1 Solution builds
- [x] 3.2 Architecture tests pass
- [x] 3.3 Unit tests pass, including the visibility provider and the service check-order tests
- [x] 3.4 Integration tests pass, including SightingRemovalVisibilityContractTests and the existing contract tests unchanged

#### Manual

- [x] 3.5 Removed sighting is gone for a second member, "no longer available" on its URL, still in the reporter's My sightings

### Phase 4: SPA moderation UI

#### Automated

- [ ] 4.1 Labels regenerate and the app and spec typechecks pass
- [ ] 4.2 All frontend specs pass, including label coverage, the kitchen sink and the new component specs

#### Manual

- [ ] 4.3 Admin remove and restore flow works on detail page and map
- [ ] 4.4 Reporter sees the Removed badge and a read-only detail page
- [ ] 4.5 Thread participant sees a read-only thread with the notice
- [ ] 4.6 Removed states render correctly in light and dark theme

### Phase 5: E2E scenario and docs

#### Automated

- [ ] 5.1 The new E2E spec passes against the local API and dev server
- [ ] 5.2 The full E2E suite still passes

#### Manual

- [ ] 5.3 Api and Frontend CLAUDE.md describe the role mechanism, visibility rule and admin test setup accurately
- [ ] 5.4 Production App Service admin setting configured and the admin sees roles ["Admin"] after login
