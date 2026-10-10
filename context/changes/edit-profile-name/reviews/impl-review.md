<!-- IMPL-REVIEW-REPORT -->
# Implementation Review: Edit Profile Name (and Change Password)

- **Plan**: context/changes/edit-profile-name/plan.md
- **Scope**: Full plan
- **Reviewed phases**: 1, 2, 3, 4
- **Date**: 2026-10-10
- **Verdict**: NEEDS ATTENTION
- **Findings**: 0 critical, 5 warnings, 5 observations

## Verdicts

| Dimension | Verdict |
|-----------|---------|
| Plan Adherence | WARNING |
| Scope Discipline | WARNING |
| Safety & Quality | WARNING |
| Architecture | WARNING |
| Pattern Consistency | WARNING |
| Success Criteria | PASS |

## Verification run

- `dotnet build PigeonWatch/Api/PigeonWatchApi.slnx`: failed only with MSB3027/MSB3021 file locks because a running `PigeonWatch.WebApi.Host` (PID 10160) holds its bin DLLs. A rebuild to a separate output directory succeeded with 0 errors.
- Architecture tests: 42/42 passed. They were built to an alternative output directory inside the repo because of the same lock.
- Unit tests: 85/85 passed.
- `dotnet ef migrations has-pending-model-changes`: not run. The startup project could not be built while the host was running.
- `npm run build`: passed. Initial total is 462.52 kB, under the 500 kB budget.
- `npm test -- --watch=false`: 23 files and 222 tests passed.
- Manual items 1.5 to 4.7: all checked in Progress, and each has matching code and specs in the diff.

## Findings

### F1 — Password-change re-login ignores sessionGeneration

- **Severity**: ⚠️ WARNING
- **Impact**: 🔎 MEDIUM — real tradeoff; pause to reason through it
- **Dimension**: Safety & Quality
- **Location**: PigeonWatch/Frontend/src/app/core/auth/session.service.ts:103-119
- **Detail**: `changePassword` does not capture `this.sessionGeneration`. Other session flows do, at lines 134 and 142. Suppose the session is cleared while `PUT account/password` is in flight, for example by a logout in another tab (storage event, then `expire()`) or by another tab's refresh failing after the stamp rotation. The `switchMap` still calls `login(email, newPassword)`, which writes fresh tokens and silently undoes the logout. The plan says session-state changes go through SessionService because the sessionGeneration race fix depends on it. No spec covers a logout during the change.
- **Fix**: Capture `const generation = this.sessionGeneration` at entry. Before `login`, skip the re-login (or throw `ReLoginError`) when the generation has changed. Add a spec that expires the session between the PUT and the login.
  - Strength: Mirrors the guard used at session.service.ts:134-142, so all session writers obey the same invariant.
  - Tradeoff: You have to decide what the user sees: a skipped re-login means the password changed and the tab is logged out, which is consistent with the logout that happened elsewhere.
  - Confidence: HIGH — the existing guard pattern sits in the same file.
  - Blind spot: The interaction with the dialog's `ReLoginError` navigation when the generation changes is not verified.
- **Decision**: FIXED — guard on captured sessionGeneration before re-login + spec

### F2 — Password card became a dialog, and the name form became an Edit/Cancel/Save toggle, without a plan update

- **Severity**: ⚠️ WARNING
- **Impact**: 🔎 MEDIUM — real tradeoff; pause to reason through it
- **Dimension**: Plan Adherence
- **Location**: PigeonWatch/Frontend/src/app/features/profile/profile-page/profile-page.ts:124-130, profile-page.html:214-244, features/profile/password-dialog/
- **Detail**:
  - Plan Phase 4 item 2 asks for "a second card below the profile card" in which the two cards are independent.
  - The implementation instead opens a modal `PasswordDialog` from a button on the single profile card. The password success notice appears inside that profile card, so it can show next to the name-save notice and the profile form error.
  - Plan Phase 3 item 3 asks for an always-editable, prefilled name field whose notice clears when the user edits.
  - The implementation shows read-only text with an Edit button. Edit mode adds Cancel and Save, and Save stays disabled until the name changes. The notice clears when Edit is clicked.
  - This UX drove unplanned changes: `SubmitButton.disabled`, `profile.edit` and `profile.cancel` labels, global `mat.dialog-overrides`, and a new `auth-errors.passwordChangeFailure`.
  - The plan and plan-brief.md:23 ("Two independent cards") were never updated. The only record is the commit subjects.
- **Fix A ⭐ Recommended**: Add an addendum to plan.md and plan-brief.md that records the dialog, the Edit/Cancel/Save mode and the resulting extra changes.
  - Strength: Keeps tested, manually verified work (Progress 3.4-3.7 and 4.4-4.6). It also makes the plan accurate again before `/10x-archive` freezes it.
  - Tradeoff: The plan records a design decision after the fact.
  - Confidence: HIGH — specs cover the dialog flow, including independence from the profile form (spec:706-720).
  - Blind spot: Whether the shared-card notice placement is the UX you actually want.
- **Fix B**: Rework to the planned two-card layout.
  - Strength: Matches the plan and keeps the notices and errors fully independent.
  - Tradeoff: Large frontend rework plus spec rewrites, and the F3 and dialog-theming work would be thrown away.
  - Confidence: MEDIUM — the work is mechanical, but the 721-line spec is heavily tied to the dialog.
  - Blind spot: The UX reasoning behind the switch to the dialog was not recorded.
- **Decision**: FIXED via Fix A — plan addendum + plan-brief updated

### F3 — Password dialog can be dismissed mid-request, losing the success notice

- **Severity**: ⚠️ WARNING
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Safety & Quality
- **Location**: PigeonWatch/Frontend/src/app/features/profile/profile-page/profile-page.ts:126-129, password-dialog.ts:118-134
- **Detail**: The dialog opens without `disableClose`, and `busy()` only disables the Cancel button. Escape or a backdrop click during the request closes the dialog, so `afterClosed` emits `undefined`. The request still completes, so the password is changed but no notice appears and the user may assume the change failed.
- **Fix**: Set `dialogRef.disableClose = true` when submitting and reset it in `finalize`, or open the dialog with `disableClose: true` and close it explicitly.
- **Decision**: FIXED — disableClose while busy + Escape spec

### F4 — core/ imports from features/

- **Severity**: ⚠️ WARNING
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Architecture
- **Location**: PigeonWatch/Frontend/src/app/core/auth/session.service.ts:19
- **Detail**: `import { ProfileApi } from '../../features/profile/profile-api'` is the only `core/ -> features/` dependency in the app. It reverses the documented layering (Frontend/CLAUDE.md:11: core is cross-cutting plumbing, features are lazy pages) and pulls `ProfileApi` into the initial bundle.
- **Fix**: Move `changePassword` into `core/auth/auth-api.ts`, or move `ProfileApi` into `core/`, and keep `features/profile` depending on core only.
- **Decision**: FIXED — changePassword moved to core/auth/auth-api.ts; no core→features imports remain

### F5 — Email "cannot be changed here" hint missing

- **Severity**: ⚠️ WARNING
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Plan Adherence
- **Location**: PigeonWatch/Frontend/src/app/features/profile/profile-page/profile-page.html:14-15
- **Detail**: Plan Phase 3 items 3 and 5 ask for the read-only email to carry a "cannot be changed here" hint, backed by a label key. The template shows only the label and the value, and `UiLabels.resx` has `profile.email.label` but no hint key.
- **Fix**: Add a `profile.email.hint` key to UiLabels.resx and render it under the email with `pw-text-muted pw-text-small`.
- **Decision**: DISMISSED — hint removed from plan scope (Phase 3 items 3, 5 + addendum)

### F6 — Current-password guessing is limited only per IP, and the re-login spends a second permit

- **Severity**: 💡 OBSERVATION
- **Impact**: 🔎 MEDIUM — real tradeoff; pause to reason through it
- **Dimension**: Safety & Quality
- **Location**: PigeonWatch/Api/Data/Repositories/AccountRepository.cs:69, PigeonWatch/Frontend/src/app/core/auth/session.service.ts:108-110
- **Detail**:
  - `UserManager.ChangePasswordAsync` does not increment `AccessFailedCount` on `PasswordMismatch`, so lockout never applies.
  - Someone holding a stolen bearer token can guess the current password at 10 attempts per minute per IP, and faster from several IPs.
  - The follow-up `auth/login` shares the same per-IP `auth` bucket. After a few wrong attempts, a successful change can hit 429 on re-login, which logs the user out. That case is handled correctly, but the UX is avoidable.
- **Fix A ⭐ Recommended**: Accept the risk for the MVP and record it as an open question or follow-up.
  - Strength: The plan deliberately chose the existing Auth policy, and an attacker needs a valid access token first.
  - Tradeoff: The brute-force window stays open for as long as a stolen token lives.
  - Confidence: MED — this depends on the threat model for a hobby-scale app.
  - Blind spot: The access-token lifetime configured in Program.cs was not checked.
- **Fix B**: Call `AccessFailedAsync` on `PasswordMismatch`, and/or partition the password endpoint's limiter by user id.
  - Strength: Closes the gap using lockout, which Identity already has.
  - Tradeoff: Lockout also blocks the real user's next login, which creates a self-DoS vector for a token holder.
  - Confidence: MED — the lockout semantics need a careful decision.
  - Blind spot: Whether lockout is currently enabled in the Identity options.
- **Decision**: ACCEPTED via Fix A — MVP risk; follow-up queued in follow-ups/review-fixes.md

### F7 — Profile feature reuses register- and auth-specific code across features

- **Severity**: 💡 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Pattern Consistency
- **Location**: PigeonWatch/Frontend/src/app/features/profile/profile-page/profile-page.ts:35, password-dialog.ts:27-33
- **Detail**: `features/profile` imports `auth-errors` and the `LoginNavigationState` type from `features/auth`. Rename errors go through `registerFailure`, which also maps `RegistrationFailed`, `InvalidEmail` and password codes. This is the second feature to use this code, so it triggers the promote-to-shared rule the plan applied to the validators.
- **Fix**: Move the shared error mapping and `LoginNavigationState` to `shared/` (or `core/auth`) and add a dedicated `profileFailure` mapper.
- **Decision**: FIXED — auth-errors + LoginNavigationState promoted to shared/auth; dedicated profileFailure mapper + spec

### F8 — Material button tokens overridden in component SCSS

- **Severity**: 💡 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Pattern Consistency
- **Location**: PigeonWatch/Frontend/src/app/features/profile/profile-page/profile-page.scss:17-25, password-dialog/password-dialog.scss:7-15
- **Detail**: These are the first component styles to set `--mat-button-filled-*` variables. The same rules are duplicated in both files, and Cancel is coloured `--pw-color-danger`, which reads as a destructive action. Frontend/CLAUDE.md puts Material tweaks in `_material-theme.scss` via `mat.<component>-overrides`, as this change already does for the dialog.
- **Fix**: Promote danger/success button variants to `_material-theme.scss` (or to a `shared/ui` button), and use a neutral style for Cancel.
- **Decision**: FIXED — Cancel is mat-stroked-button; green Save centralised as .pw-button-success (mat.button-overrides) in _material-theme.scss

### F9 — Unplanned convention change and roadmap status flip

- **Severity**: 💡 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Scope Discipline
- **Location**: PigeonWatch/Api/CLAUDE.md:69, context/foundation/roadmap.md
- **Detail**: Two changes went beyond the plan:
  - Phase 1 added a new style rule (no blank lines between interface members) and reformatted `IUserAccountMapper` and `IAccountRepository` to match. The plan did not include this.
  - The roadmap edit flipped S-02 from `proposed` to `in-progress` and bumped `updated:`, although plan Phase 4 item 5 says status flips belong to `/10x-archive`.
  Both are harmless.
- **Fix**: Keep both and mention them in the plan addendum (F2). Confirm that `/10x-archive` will move S-02 from `in-progress` to done.
- **Decision**: FIXED — documented in plan Addendum (via F2)

### F10 — Initial bundle grew about 97 kB, leaving 37.5 kB of budget headroom

- **Severity**: 💡 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Safety & Quality
- **Location**: PigeonWatch/Frontend/src/app/app.ts:3, core/auth/session.service.ts:19
- **Detail**: The plan recorded the initial bundle as 365 kB. `npm run build` now reports 462.52 kB against the 500 kB warning budget. `MatMenu` in `app.ts` was expected. `ProfileApi` entering core (F4) adds a little more. Frontend/CLAUDE.md mentions only the 500 kB budget, so the next plan has no measured baseline to start from.
- **Fix**: Record the new baseline (462.5 kB) in Frontend/CLAUDE.md next to the budget rule. Fix F4 to keep feature code out of the initial chunk.
- **Decision**: FIXED — 462.5 kB baseline recorded in PigeonWatch/Frontend/CLAUDE.md
