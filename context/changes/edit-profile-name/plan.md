# Edit Profile Name (and Change Password) Implementation Plan

## Overview

S-02 gives a logged-in user a profile page where they can see their email, edit their display name, and change their password. It also replaces the header's "Logged in as {name}" text and Log out button with a user menu (name button, dropdown with Profile and Log out). Name editing is FR-002 (name only for MVP). Password change is a requirement added during planning on 2026-10-10; it has no FR in PRD v1 and is folded into S-02 by decision (see Open Roadmap Question to be added in Phase 4).

## Current State Analysis

- The data model already supports renaming. `DisplayName` (3-30 chars, no `@`) is a column on the user table with a unique index on its normalized form, so no migration is needed. `PigeonWatchUserStore.UpdateAsync` already normalizes the name, rejects a name taken by another user (own id excluded, so a case-only change is allowed), handles the unique-index race and the optimistic concurrency stamp.
- There is no endpoint to change a profile or password. The API exposes `POST account/register`, Identity's `auth/login` and `auth/refresh` (all other Identity routes are blocked), and `GET configuration/general` (authorized, returns the current user from the database).
- The SPA reads the user from `GET /configuration/general` into `ConfigurationService`/`SessionService.currentUser`. The name is shown only in the header (`app.html`). There is no profile route, page, or API service.
- Display-name and password validation exists only inside the register page (`register-page.ts`). This is the second feature that needs it, so the frontend promotion rule applies.
- Password policy (min 8, digit, upper, lower, non-alphanumeric) is enforced by Identity and exposed to the SPA through `GET /configuration/client`.
- No Polish resource file exists, and a unit test requires `pl` to resolve to English, so this change adds English labels only.
- There is no API integration test project (an S-01 decision). Backend verification is unit tests plus a manual `curl.exe` pass.

## Desired End State

A logged-in user opens the user menu in the header, chooses Profile, and lands on `/profile`. The page has two cards:

1. **Profile:** the email as read-only text and an editable display name with a Save button. A successful save keeps the user on the page, shows a success notice, and the header name updates immediately. Invalid or taken names show an inline error.
2. **Password:** current password, new password, confirm new password, and a Save button. A successful change keeps this tab signed in (silent re-login with the new password) and shows a success notice. Sessions on other devices stop working when their access token expires and refresh fails. A wrong current password or a policy violation shows an inline error.

Verify with the unit and architecture test suites, the Angular build and tests, and the manual checks in each phase.

### Key Discoveries:

- `PigeonWatchUserStore.cs:82-120` (`UpdateAsync`) already rejects duplicate display names, excluding the user's own id, and maps unique-index and concurrency failures to Identity errors.
- `AccountService.cs:34-53` (`ValidateDisplayName`) is the single place for length and `@` rules. It is private static, so the new rename method lives in the same class and reuses it. Static helper classes are forbidden by the architecture tests.
- `ConfigurationController.cs:29-46` is the authorized-endpoint pattern to copy: `[Authorize]`, parse `ClaimTypes.NameIdentifier` to a `Guid`, bare `Unauthorized()` on failure.
- `AccountController.cs:13-37` shows the error pattern: result record, then `ModelState.AddModelError(code, description)` and `ValidationProblem(ModelState)`. The SPA maps those codes to label keys.
- `ControllerActionTests.cs:10-28` requires every public action to return `Task<ActionResult<T>>` with `T` in `WebApi/Models`, so the password endpoint needs a minimal response model.
- `AccountRepository.ToAccountErrors` (`AccountRepository.cs:40-64`) maps Identity errors to `AccountError` and passes unknown codes through, so `PasswordMismatch` and `Password*` codes arrive as they are.
- `PigeonWatchUserStore` implements `IUserPasswordStore` and `IUserSecurityStampStore`, so `UserManager.ChangePasswordAsync` works and rotates the security stamp. Identity's `auth/refresh` validates the stamp, which ends other devices' sessions at their next refresh.
- S-01 review F3: validate the trimmed length before anything reaches `SaveChanges` (an over-length value caused a 500 and could be logged).
- `session.service.ts:74-80` (`login`) sets tokens and calls `loadGeneral()`; `register` wraps a failed auto-login in `AutoLoginError`, handled at `register-page.ts:202`. The password flow mirrors both.
- `ConfigurationService` exposes the general state read-only, so the header refreshes by calling `loadGeneral()` again rather than through a setter.
- `Frontend/CLAUDE.md`: new pages are lazy, use Signal Forms, `provideFormFieldDefaults()`, `FormSubmitState`, OnPush, `finalize(() => busy.set(false))`; `app.ts` and `core/` import `shared/ui` by file path, never the barrel; component SCSS may use tokens only; every UI text is a key in `UiLabels.resx`; initial bundle budget is 500 kB (last measured 365 kB).
- `label-coverage.spec.ts` fails on any `translate` key literal missing from the generated English snapshot and on literal text in templates.

## What We're NOT Doing

- No email change, no forgot-password or reset flow, no avatar, description, or any profile field beyond name.
- No name-change history or limits on how often a name can change (the change log, FR-008, is parked).
- No immediate revocation of access tokens on other devices. Identity bearer tokens are not revocable; other sessions end when their access token expires and the refresh is rejected.
- No Polish translation (S-12 owns it). Adding `UiLabels.pl.resx` here would also break the existing unsupported-culture test.
- No API integration test project or `WebApplicationFactory` (S-01 decision stands).
- No database migration and no schema change.
- No rate limit on the rename endpoint; display names are public and the uniqueness check leaks nothing new. Only the password endpoint is limited.
- No special handling of a rename to the same name; it follows the normal update path and succeeds.

## Implementation Approach

Backend first, one feature per phase, so each endpoint is unit-tested and manually verifiable with `curl.exe` before any UI depends on it. Then the frontend in two phases: the profile page with the name form, the shared validators and the header user menu; then the password form with the re-login flow. Extend the existing `AccountService`, `IAccountRepository` and `AccountController` rather than adding new services, so no new registrations are needed except new mappers or creators. Reuse the register page's validation, error codes and label keys; extract the duplicated validators into shared code in the same change.

## Phase 1: Backend rename endpoint

### Overview

Add an authorized `PUT account/profile` that renames the current user, with unit tests.

### Changes Required:

#### 1. Business objects

**File**: `PigeonWatch/Api/BusinessObjects/`

**Intent**: Add a result type for a profile update, shaped like `AccountCreationResult`, carrying the updated display name and email on success and `AccountError`s on failure.

**Contract**: `ProfileUpdateResult` record with `Succeeded`, `Errors`, and the updated profile (email, display name); `Success`/`Failure` factories. Reuse the existing `AccountErrorCodes` (`DuplicateDisplayName`, `DisplayNameLength`, `DisplayNameInvalidCharacter`); add only a code for a missing user if the existing ones do not fit.

#### 2. Service

**Files**: `PigeonWatch/Api/BusinessLogic/Services/AccountService.cs`, `PigeonWatch/Api/BusinessLogic/Services/Interfaces/IAccountService.cs`

**Intent**: Add `UpdateDisplayNameAsync(Guid userId, string displayName, CancellationToken)`. Trim, validate with the existing `ValidateDisplayName` before calling the repository, and return the failure list without touching the database when invalid.

**Contract**: Same trim-then-validate order as `RegisterAsync`. Boundaries to cover: 2, 3, 30, 31 characters; `@`; whitespace-only; surrounding whitespace trimmed.

#### 3. Repository

**Files**: `PigeonWatch/Api/Data/Repositories/AccountRepository.cs`, `PigeonWatch/Api/Data/Repositories/Interfaces/IAccountRepository.cs`

**Intent**: Add `UpdateDisplayNameAsync(Guid userId, string displayName, CancellationToken)`. Find the user through `UserManager`, set the new name, call `UpdateAsync`, and map the `IdentityResult` through the existing error mapping and `IUserAccountMapper`.

**Contract**: Returns `ProfileUpdateResult`. A missing user returns a failure the controller turns into `Unauthorized`. `DuplicateDisplayName` and `ConcurrencyFailure` come straight from the store. Check in a unit test that `UserManager.UpdateAsync`'s user validators accept an unchanged email and user name (they run on every update).

#### 4. Mapper

**File**: `PigeonWatch/Api/Data/Mappers/` (existing `IUserAccountMapper` and implementation)

**Intent**: Add the mapping needed to build the success result from `ApplicationUser`; repositories must not construct business objects inline (S-01 review F9).

**Contract**: Extend `IUserAccountMapper` only if no existing method (`ToCurrentUser`, `ToRegisteredAccount`) already yields the needed fields.

#### 5. Controller, models, mapper, view model creator

**Files**: `PigeonWatch/Api/WebApi/Controllers/AccountController.cs`, `PigeonWatch/Api/WebApi/Models/`, `PigeonWatch/Api/WebApi/Mappers/`, `PigeonWatch/Api/WebApi/ViewModelCreators/` (each with `Interfaces/`), `PigeonWatch/Api/DependencyInjection/WebApiServiceCollectionExtensions.cs`

**Intent**: Add `PUT account/profile`, `[Authorize]`, taking `UpdateProfileRequestModel { DisplayName }` and returning the updated profile model. Parse `ClaimTypes.NameIdentifier` as in `ConfigurationController`; `Unauthorized()` when it fails or the user is missing; failures via `ValidationProblem(ModelState)` keyed by error code.

**Contract**: `Task<ActionResult<UpdatedProfileModel>> UpdateProfile(UpdateProfileRequestModel, CancellationToken)`; request model uses `required ... init` like `RegisterRequestModel`; new mapper and view model creator get interfaces and `AddScoped` registrations. No rate-limit attribute.

#### 6. Unit tests

**Files**: `PigeonWatch/Api/UnitTests/` (`AccountServiceTests.cs`, `AccountRepositoryTests.cs`, a new controller test class following `ConfigurationTests.cs`)

**Intent**: Cover the service boundaries, the repository's mapping of duplicate and concurrency failures, and the controller's 200, 400 (`ValidationProblem` keyed by code) and 401 paths with a `ClaimsPrincipal`. Include a case-only rename of the user's own name succeeding (store test in `PigeonWatchUserStoreTests.cs` if not already covered).

**Contract**: xUnit v3 with NSubstitute and `TestContext.Current.CancellationToken`, matching existing tests.

### Success Criteria:

#### Automated Verification:

- Solution builds without warnings as errors: `dotnet build PigeonWatch/Api/PigeonWatchApi.slnx`
- Architecture tests pass: `dotnet test PigeonWatch/Api/ArchitectureTests/PigeonWatch.ArchitectureTests.csproj`
- Unit tests pass: `dotnet test PigeonWatch/Api/UnitTests/PigeonWatch.UnitTests.csproj`
- No pending EF model changes: `dotnet ef migrations has-pending-model-changes --project PigeonWatch/Api/Data/PigeonWatch.Data.csproj --startup-project PigeonWatch/Api/WebApi.Host/PigeonWatch.WebApi.Host.csproj`

#### Manual Verification:

- Against the local API, `curl.exe -X PUT account/profile` with a bearer token and a new name returns 200 and `GET configuration/general` shows the new name; a name taken by another user (differing only by case) returns 400 with `DuplicateDisplayName`; a missing token returns 401 (use `curl.exe` with backtick-n in PowerShell, per `context/foundation/lessons.md`)

**Implementation Note**: After completing this phase and all automated verification passes, pause here for manual confirmation from the human that the manual testing was successful before proceeding to the next phase. Phase blocks use plain bullets; the corresponding `- [ ]` checkboxes for these items live in the `## Progress` section at the bottom of the plan.

---

## Phase 2: Backend change-password endpoint

### Overview

Add an authorized, rate-limited `PUT account/password` that changes the current user's password through Identity, with unit tests.

### Changes Required:

#### 1. Business objects

**File**: `PigeonWatch/Api/BusinessObjects/`

**Intent**: Add a result type for a password change carrying `Succeeded` and `AccountError`s.

**Contract**: `PasswordChangeResult` record with `Success`/`Failure` factories. Identity's `PasswordMismatch` and `Password*` policy codes pass through unchanged; add them to `AccountErrorCodes` as constants only where the SPA or tests reference them.

#### 2. Service and repository

**Files**: `AccountService.cs`, `IAccountService.cs`, `AccountRepository.cs`, `IAccountRepository.cs`

**Intent**: Add `ChangePasswordAsync(Guid userId, string currentPassword, string newPassword, CancellationToken)` to the service (delegates, no extra rules beyond Identity's policy) and to the repository (find user, `UserManager.ChangePasswordAsync`, map errors).

**Contract**: The repository relies on `ChangePasswordAsync` verifying the current password, validating the new one against the configured policy, and rotating the security stamp. Passwords are never logged or placed in error descriptions.

#### 3. Controller, models, mapper, registrations

**Files**: `AccountController.cs`, `WebApi/Models/`, `WebApi/Mappers/`, `WebApi/ViewModelCreators/`, `DependencyInjection/WebApiServiceCollectionExtensions.cs`

**Intent**: Add `PUT account/password`, `[Authorize]` and `[EnableRateLimiting(RateLimitPolicyNames.Auth)]`, taking `ChangePasswordRequestModel { CurrentPassword, NewPassword }`. Same claim parsing and `ValidationProblem` pattern as Phase 1.

**Contract**: `ControllerActionTests` forces `Task<ActionResult<T>>` with `T` in `WebApi/Models`, so add the smallest possible response model (`PasswordChangedModel`, no secrets) returned with 200. Rate limit is the existing 10 requests per minute per IP policy.

#### 4. Unit tests

**Files**: `AccountServiceTests.cs`, `AccountRepositoryTests.cs`, controller test class from Phase 1

**Intent**: Cover wrong current password (`PasswordMismatch`), each policy violation passing through as its Identity code, success, missing user, and the controller's 200, 400 and 401 paths.

**Contract**: Repository tests mock `UserManager<ApplicationUser>` like the existing ones.

### Success Criteria:

#### Automated Verification:

- Solution builds: `dotnet build PigeonWatch/Api/PigeonWatchApi.slnx`
- Architecture tests pass: `dotnet test PigeonWatch/Api/ArchitectureTests/PigeonWatch.ArchitectureTests.csproj`
- Unit tests pass: `dotnet test PigeonWatch/Api/UnitTests/PigeonWatch.UnitTests.csproj`

#### Manual Verification:

- Against the local API with `curl.exe`: a wrong current password returns 400 `PasswordMismatch`; a weak new password returns the matching `Password*` codes; a valid change returns 200, the old password no longer logs in, the new one does, and a refresh token issued before the change is rejected by `auth/refresh`
- An eleventh request within a minute from the same IP returns 429

**Implementation Note**: After completing this phase and all automated verification passes, pause here for manual confirmation from the human that the manual testing was successful before proceeding to the next phase.

---

## Phase 3: Profile page with name edit and header user menu

### Overview

Add the guarded `/profile` page with the name form, extract the shared display-name and password validators, replace the header text and Log out button with a user menu, and add the new labels.

### Changes Required:

#### 1. Shared validators

**Files**: `PigeonWatch/Frontend/src/app/shared/` (new location chosen to match `shared/ui` conventions), `PigeonWatch/Frontend/src/app/features/auth/register-page/register-page.ts`

**Intent**: Move the display-name rules (required, min and max from `clientConfig().displayNameRules` with the 3-30 fallback, no `@`) and the password rules and confirm-match validators out of the register page so the profile page can use them. Register page behavior does not change.

**Contract**: Functions or small helpers consumable from Signal Forms schemas, no literal UI text (callers pass translated messages), and the existing register specs still pass unchanged.

#### 2. Profile API service

**File**: `PigeonWatch/Frontend/src/app/features/profile/profile-api.ts`

**Intent**: A `providedIn: 'root'` service in the `AuthApi` style with `updateDisplayName(displayName)` calling `PUT {apiUrl}/account/profile`. Observables only. No `SKIP_AUTH`; the interceptor adds the bearer token.

**Contract**: Returns an Observable of the updated-profile response; the page ignores the body and refetches the general configuration.

#### 3. Profile page and route

**Files**: `PigeonWatch/Frontend/src/app/features/profile/profile-page/`, `PigeonWatch/Frontend/src/app/app.routes.ts`

**Intent**: A lazy `profile` route guarded by `authGuard`, added before the `**` redirect. The page has the "Profile" card: the email as read-only text with a hint that it cannot be changed here, an editable display name field prefilled from `currentUser`, and a submit button. On success it calls `configuration.loadGeneral()` so the header updates, shows a success `app-alert`, and clears the notice when the user edits again. Server errors map `DuplicateDisplayName`, `DisplayNameLength` and `DisplayNameInvalidCharacter` to field errors tied to the submitted value; 429, 503 and status 0 reuse the existing transport mapping.

**Contract**: Follows `register-page` conventions: Signal Forms, `provideFormFieldDefaults()`, `FormSubmitState.markSubmitted()` first, `if (busy() || form().invalid()) return`, OnPush, `finalize(() => busy.set(false))`. Uses `app-page-card`, `app-alert`, `app-field-errors`, `app-submit-button`. The page is built so a second card can be added below in Phase 4.

#### 4. Header user menu

**Files**: `PigeonWatch/Frontend/src/app/app.ts`, `PigeonWatch/Frontend/src/app/app.html`, `PigeonWatch/Frontend/src/app/app.scss`

**Intent**: Replace the "Logged in as {name}" text and the Log out button with a button showing the user's display name that opens a Material menu with two items, Profile (navigates to `/profile`) and Log out. Logout behavior is unchanged.

**Contract**: Import `MatMenu` per component from its own file path (`app.ts` is initial-bundle code). Check the initial-bundle size stays under the 500 kB warning. Styles use tokens and `pw-*` classes only. Update `app.spec.ts` for the new structure (logout is now inside the menu).

#### 5. Labels

**File**: `PigeonWatch/Api/BusinessObjects/Resources/UiLabels.resx`

**Intent**: Add English keys for the profile page (title, email label and its "cannot be changed here" hint, save, success notice) and the menu's Profile item, under `profile.*` and `header.*`. Reuse existing `auth.fields.displayName`, `auth.validation.*`, `auth.errors.*`, `header.logout` and the existing transport error keys.

**Contract**: Lowercase dot paths, positional `{0}` placeholders only, literal keys in templates (the coverage spec matches them by regex). `UiResourceTests` must keep passing.

#### 6. Specs

**Files**: `PigeonWatch/Frontend/src/app/features/profile/profile-page/profile-page.spec.ts`, `PigeonWatch/Frontend/src/app/app.spec.ts`, specs for the extracted validators

**Intent**: Cover prefilled name and read-only email, client validation (required, 2 and 31 characters, `@`), success path (success notice and `loadGeneral` called), duplicate-name error clearing when the value is edited, a transport failure, the header menu showing Profile and Log out and logging out.

**Contract**: Follow `login-page.spec.ts` (`RouterTestingHarness`, `provideHttpClientTesting`, mocked `SessionService` via `useValue`, DOM-level helpers).

### Success Criteria:

#### Automated Verification:

- Production build succeeds and the initial bundle stays under the 500 kB budget: `npm run build` (from `PigeonWatch/Frontend`)
- Frontend tests pass, including label-coverage and style-token specs: `npm test -- --watch=false` (from `PigeonWatch/Frontend`)
- Backend unit tests still pass (labels): `dotnet test PigeonWatch/Api/UnitTests/PigeonWatch.UnitTests.csproj`

#### Manual Verification:

- With the local API and `npm start`, the header shows the user's name as a button; clicking it opens a menu with Profile and Log out; Profile opens `/profile`; Log out logs out
- The profile page shows the email as read-only text and the current name; saving a new name shows the success notice and the header name changes without a reload
- A name taken by another user (case-insensitive) shows the duplicate error under the field; a 2-character name and a name containing `@` show the validation messages
- The page and the menu are usable at phone width

**Implementation Note**: After completing this phase and all automated verification passes, pause here for manual confirmation from the human that the manual testing was successful before proceeding to the next phase.

---

## Phase 4: Password change and roadmap update

### Overview

Add the password card to the profile page with the silent re-login flow, then record the scope change in the roadmap.

### Changes Required:

#### 1. Profile API and session

**Files**: `PigeonWatch/Frontend/src/app/features/profile/profile-api.ts`, `PigeonWatch/Frontend/src/app/core/auth/session.service.ts`

**Intent**: Add `changePassword(currentPassword, newPassword)` calling `PUT {apiUrl}/account/password`. Add a `SessionService` method that performs the change and then logs in again with the current user's email and the new password (reusing `login`) so this tab gets fresh tokens after the security stamp rotates.

**Contract**: Mirror the `register`/`AutoLoginError` pattern: if the change succeeds but the follow-up login fails, raise a distinct error so the page can send the user to `/login` instead of leaving a dead session. Any code that changes session state goes through `SessionService` (the `sessionGeneration` race fix depends on it).

#### 2. Password card

**Files**: `PigeonWatch/Frontend/src/app/features/profile/profile-page/profile-page.ts`, `.html`, `.scss`

**Intent**: Add a second card below the profile card, with its own form, busy state and notice: current password, new password, confirm new password, submit. Client validation reuses the shared password rules and confirm-match validator from Phase 3. Server errors map `PasswordMismatch` to the current-password field and `Password*` policy codes to the new-password field; 429, 503 and status 0 reuse the transport mapping. On success, clear the three fields, show the success notice, and keep the user signed in.

**Contract**: The two cards are independent: submitting one never affects the other's errors or notice. Password fields are cleared after a successful change and never echoed.

#### 3. Labels

**File**: `PigeonWatch/Api/BusinessObjects/Resources/UiLabels.resx`

**Intent**: Add English keys for the password card (title, current-password label, new-password label, save, success notice, wrong-current-password error, "changed but could not sign you in, please log in again" message). Reuse `auth.fields.password`, `auth.fields.confirmPassword`, `auth.validation.password*` and `auth.errors.invalidPassword` where they fit.

**Contract**: Same key conventions as Phase 3.

#### 4. Specs

**Files**: `profile-page.spec.ts`, `session.service.spec.ts`

**Intent**: Cover the password form's client validation, `PasswordMismatch` and policy errors, the success path (fields cleared, notice shown, re-login called with the new password), the follow-up-login-failure path, and the session method's ordering.

**Contract**: Same harness as Phase 3.

#### 5. Roadmap and docs

**Files**: `context/foundation/roadmap.md`, `PigeonWatch/Frontend/CLAUDE.md` (only if the shared-validator location or user menu changes a documented convention)

**Intent**: Update the S-02 outcome and risk to include password change and the user menu, and add an Open Roadmap Question that PRD v1 has no FR for password change (and whether the next PRD revision should add one), mirroring Question 6 for S-12.

**Contract**: Edit-in-place; touch only the S-02 item, the At a glance row, and the questions list. Archive and status flips are handled by `/10x-archive`.

### Success Criteria:

#### Automated Verification:

- Production build succeeds and the initial bundle stays under the 500 kB budget: `npm run build` (from `PigeonWatch/Frontend`)
- Frontend tests pass: `npm test -- --watch=false` (from `PigeonWatch/Frontend`)
- Backend unit and architecture tests pass: `dotnet test PigeonWatch/Api/UnitTests/PigeonWatch.UnitTests.csproj`, `dotnet test PigeonWatch/Api/ArchitectureTests/PigeonWatch.ArchitectureTests.csproj`

#### Manual Verification:

- On the local stack, changing the password stays signed in on the same tab (header still shows the name, reload keeps the session), shows the success notice and clears the fields
- A second browser profile signed in before the change is sent to the login page once its access token expires or it triggers a refresh
- A wrong current password shows the error under the current-password field; a weak or mismatched new password shows the matching validation messages; logging out and in with the new password works and the old one is rejected
- The roadmap shows the updated S-02 outcome and the new open question

**Implementation Note**: After completing this phase and all automated verification passes, pause here for manual confirmation from the human that the manual testing was successful before archiving the change.

---

## Testing Strategy

### Unit Tests:

- Service: display-name boundaries (2, 3, 30, 31), `@`, whitespace-only, trimming; password change delegating and passing Identity errors through.
- Repository: duplicate-name and concurrency failures mapped to `AccountError`; `PasswordMismatch` and policy codes passed through; missing user.
- Controller: 200, 400 keyed by code, 401 for missing claim or missing user, on both new endpoints.
- Store: a case-only rename of the user's own name succeeds.
- Frontend: profile page (name and password forms), header menu, shared validators, session re-login ordering, label coverage.

### Integration Tests:

- None automated (no integration project by S-01 decision). The two endpoints are exercised manually with `curl.exe` in Phases 1 and 2, and the whole flow in the browser in Phases 3 and 4.

### Manual Testing Steps:

1. Log in, open the user menu, go to Profile, rename to a free name and confirm the header updates.
2. Try renaming to another user's name differing only by case and confirm the duplicate error.
3. Change the password, confirm the tab stays signed in, then log out and log in with the new password.
4. In a second browser profile logged in before the change, confirm the session ends at the next refresh.
5. Repeat the password change with a wrong current password and with a weak new password.

## Performance Considerations

No new load. The one real constraint is the initial-bundle budget: `MatMenu` enters `app.ts`, so confirm `npm run build` stays under the 500 kB warning (last measured 365 kB).

## Migration Notes

None. No column, index or data change; `has-pending-model-changes` must stay clean.

## References

- PRD: `context/foundation/prd.md` (FR-002; password change is an addition made during planning)
- Roadmap: `context/foundation/roadmap.md` (S-02)
- Prior change: `context/archive/2026-10-04-register-and-login/plan.md` and `reviews/impl-review.md` (display-name rules, F3, F9)
- Similar implementation: `PigeonWatch/Api/WebApi/Controllers/AccountController.cs:13`, `PigeonWatch/Api/WebApi/Controllers/ConfigurationController.cs:29`, `PigeonWatch/Frontend/src/app/features/auth/register-page/register-page.ts`
- Lessons: `context/foundation/lessons.md` (use `curl.exe` in PowerShell)

## Progress

> Convention: `- [ ]` pending, `- [x]` done. Append ` — <commit sha>` when a step lands. Do not rename step titles. See `references/progress-format.md`.

### Phase 1: Backend rename endpoint

#### Automated

- [x] 1.1 Solution builds without warnings as errors: `dotnet build PigeonWatch/Api/PigeonWatchApi.slnx` — a4ae85e
- [x] 1.2 Architecture tests pass: `dotnet test PigeonWatch/Api/ArchitectureTests/PigeonWatch.ArchitectureTests.csproj` — a4ae85e
- [x] 1.3 Unit tests pass: `dotnet test PigeonWatch/Api/UnitTests/PigeonWatch.UnitTests.csproj` — a4ae85e
- [x] 1.4 No pending EF model changes: `dotnet ef migrations has-pending-model-changes` — a4ae85e

#### Manual

- [x] 1.5 Against the local API, `PUT account/profile` renames the user, a duplicate name differing by case returns 400 `DuplicateDisplayName`, and a missing token returns 401 — a4ae85e

### Phase 2: Backend change-password endpoint

#### Automated

- [x] 2.1 Solution builds: `dotnet build PigeonWatch/Api/PigeonWatchApi.slnx` — dddbe39
- [x] 2.2 Architecture tests pass: `dotnet test PigeonWatch/Api/ArchitectureTests/PigeonWatch.ArchitectureTests.csproj` — dddbe39
- [x] 2.3 Unit tests pass: `dotnet test PigeonWatch/Api/UnitTests/PigeonWatch.UnitTests.csproj` — dddbe39

#### Manual

- [x] 2.4 Wrong current password returns 400 `PasswordMismatch`, weak new password returns `Password*` codes, a valid change returns 200, the old password stops working, and an old refresh token is rejected — dddbe39
- [x] 2.5 An eleventh request within a minute from the same IP returns 429 — dddbe39

### Phase 3: Profile page with name edit and header user menu

#### Automated

- [x] 3.1 Production build succeeds under the 500 kB initial budget: `npm run build` — 5311d68
- [x] 3.2 Frontend tests pass including label-coverage and style-token specs: `npm test -- --watch=false` — 5311d68
- [x] 3.3 Backend unit tests still pass: `dotnet test PigeonWatch/Api/UnitTests/PigeonWatch.UnitTests.csproj` — 5311d68

#### Manual

- [x] 3.4 The header user menu shows the name, opens Profile and Log out, Profile opens `/profile`, Log out logs out — 5311d68
- [x] 3.5 The profile page shows the read-only email, and saving a new name shows the success notice and updates the header without a reload — 5311d68
- [x] 3.6 A taken name, a 2-character name and a name containing `@` show the right inline errors — 5311d68
- [x] 3.7 The page and the menu are usable at phone width — 5311d68

### Phase 4: Password change and roadmap update

#### Automated

- [x] 4.1 Production build succeeds under the 500 kB initial budget: `npm run build`
- [x] 4.2 Frontend tests pass: `npm test -- --watch=false`
- [x] 4.3 Backend unit and architecture tests pass

#### Manual

- [x] 4.4 Changing the password keeps the tab signed in, shows the success notice and clears the fields
- [x] 4.5 A second browser profile signed in before the change is sent to login when its access token expires or it refreshes
- [x] 4.6 Wrong current password and weak or mismatched new passwords show the right errors, and the old password is rejected at login
- [x] 4.7 The roadmap shows the updated S-02 outcome and the new open question
