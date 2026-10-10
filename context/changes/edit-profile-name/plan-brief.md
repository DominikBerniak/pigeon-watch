# Edit Profile Name (and Change Password) — Plan Brief

> Full plan: `context/changes/edit-profile-name/plan.md`

## What & Why

S-02 gives a logged-in member a profile page where they can see their email, rename themselves, and change their password. It also turns the header's name text and Log out button into a user menu. Renaming is PRD FR-002 (name only for MVP); password change was added at planning time and has no FR in PRD v1.

## Starting Point

The user table already has a unique, case-insensitive `DisplayName`, and the user store already rejects duplicates and handles concurrency. The API has no endpoint to change a profile or password, and the SPA has no profile page. The name appears only in the header, and the name and password validators exist only inside the register page.

## Desired End State

From the header menu a user opens `/profile`. One card shows the read-only email and a name form; saving keeps them on the page with a success notice and the header updates at once. A second card changes the password, and the tab stays signed in afterwards. Sessions on other devices end when their access token expires and the refresh is rejected.

## Key Decisions Made

| Decision                | Choice                                                                 | Why                                                                 |
| ----------------------- | ---------------------------------------------------------------------- | ------------------------------------------------------------------- |
| Where editing lives     | Guarded lazy `/profile` page                                           | Matches the page-per-feature pattern and gives S-03 a place to add sightings |
| Page content            | Read-only email, editable name, password change                       | "View profile" is more than the name; password change requested by user |
| Page layout             | Two independent cards                                                  | Two different backend calls and errors, plus the re-login side effect |
| Header entry            | Name button opening a menu with Profile and Log out                    | User's choice; replaces the text and Log out button                  |
| After a rename          | Stay on the page, success notice, refetch `/configuration/general`     | Clear confirmation; the general-config state has no setter           |
| Session after password change | Silent re-login with the new password; other devices end at refresh | Security stamp rotation invalidates every refresh token, including this tab's |
| Scope of password change | Folded into S-02, roadmap gets an open question                      | One plan and review for the page; PRD gap recorded like S-12's Q6    |
| Endpoints               | `PUT account/profile` and `PUT account/password`                      | Extends the existing `account` controller and service              |
| Rate limiting           | Existing `Auth` limit (10/min per IP) on the password endpoint only    | Limits guessing the current password with a stolen token; rename is low risk |
| Validators              | Extracted from the register page into shared code                      | Second use triggers the frontend promotion rule                    |
| Language                | English labels only                                                    | Polish belongs to S-12 and a `pl` file would break an existing test  |
| Migration               | None                                                                   | The column and unique index already exist                           |

## Scope

**In scope:** rename endpoint and password endpoint with unit tests; `/profile` page with two cards; header user menu; shared validators; English labels; roadmap update.

**Out of scope:** email change, forgot-password, other profile fields, name-change history, immediate revocation of other devices' access tokens, Polish translation, integration test project, any schema change.

## Architecture / Approach

Extend `AccountController`, `AccountService` and `AccountRepository` (through `UserManager`, so the store's uniqueness and concurrency handling apply) and keep the strict layering and architecture-test rules. Build the two backend features first and check each with `curl.exe`, then the Angular page: a `ProfileApi` service, a lazy page using the register page's Signal Forms conventions, and a Material menu in the header. A session method wraps "change password, then log in again" so session state stays inside `SessionService`.

## Phases at a Glance

| Phase                                       | What it delivers                                                       | Key risk                                                              |
| ------------------------------------------- | ---------------------------------------------------------------------- | --------------------------------------------------------------------- |
| 1. Backend rename endpoint                  | `PUT account/profile` with service, repository, DTOs and unit tests    | `UserManager.UpdateAsync` validators on a partial update              |
| 2. Backend change-password endpoint         | `PUT account/password`, rate limited, with unit tests                  | Response-model rule forces a minimal empty model                      |
| 3. Profile page with name edit and header menu | `/profile` name card, shared validators, user menu, labels, specs   | `MatMenu` enters the initial bundle (budget 500 kB, last 365 kB)      |
| 4. Password change and roadmap update       | Password card, silent re-login flow, specs, roadmap edit               | Re-login failure after a successful change must not leave a dead session |

**Prerequisites:** S-01 is done; local API with LocalDB and `npm start` available for the manual checks.
**Estimated effort:** about 3-4 sessions across 4 phases.

## Open Risks & Assumptions

- Access tokens on other devices stay valid until they expire; only their refresh is rejected.
- `UserManager.UpdateAsync` runs user validators on every update; Phase 1 verifies they accept an unchanged email and user name.
- The user-menu replacement changes header structure, so `app.spec.ts` and the header styles need updating.
- Password change has no FR in PRD v1; the roadmap records this as an open question instead of editing the PRD.

## Success Criteria (Summary)

- A user can rename themselves from the profile page and see the new name in the header immediately; taken and invalid names show clear errors.
- A user can change their password, stays signed in on that tab, and the old password stops working.
- The user menu offers Profile and Log out, and all tests, builds and manual checks pass without a migration.
