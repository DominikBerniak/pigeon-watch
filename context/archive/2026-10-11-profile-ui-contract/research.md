---
date: 2026-10-11T12:43:43+02:00
researcher: Claude (claude-sonnet-5-5)
git_commit: 8bef79c62e9b76392de14b33cda62c4d4386d7a7
branch: main
repository: 10xDevs
topic: "Profile view: audit against the design-system contract (charges for /10x-ui)"
tags: [research, ui, profile, design-system, angular-material, states]
status: partial
last_updated: 2026-10-11
last_updated_by: Claude (claude-sonnet-5-5)
---

# Research: Profile view audit against the design-system contract

**Date**: 2026-10-11T12:43:43+02:00
**Researcher**: Claude (claude-sonnet-5-5)
**Git Commit**: 8bef79c62e9b76392de14b33cda62c4d4386d7a7
**Branch**: main
**Repository**: 10xDevs

## Research Question

Audit the Profile view (`features/profile/profile-page` and the `password-dialog` it opens) in both directions, source to views and view to source. Produce 3-5 charges, each with file:line evidence and the effect on the user, for `/10x-plan`. Variant: existing design system, extend it.

All paths below are relative to `PigeonWatch/Frontend/src/` unless prefixed otherwise.

## Summary

- The contract is already in place and enforced. The hardcoded-value scan returned 0 hits across the 6 view files (`profile-page.{html,scss,ts}`, `password-dialog.{html,scss,ts}`). The templates use only `pw-*` classes and `shared/ui` components. `styles/style-token-coverage.spec.ts` guards component stylesheets. The audit therefore found no missing-token charge for this view.
- The charges are about shared-component gaps, how notices are arranged, layout hierarchy, and unexercised states. There are 5 charges and 3 deferred items (below).
- **Evidence limit (why status is `partial`):** every finding comes from reading source. No screenshot of the running view was taken, because the app needs the API and a login to reach `/profile`, and the repo has no screenshot tooling or kitchen sink. Visual claims are marked *unverified*. Phase 1 of the plan should capture the baseline screenshots before changing anything.

## Detailed Findings

### Source → views (where the contract lives and who reads it)

- Token source: `styles/_tokens.scss` defines the `--pw-*` custom properties on `:root` (`_tokens.scss:1-81`). Material is mapped onto them with `mat.theme-overrides` and per-component `mat.*-overrides` in `styles/_material-theme.scss:17-81`.
- Shared components: `shared/ui/index.ts` exports `Alert`, `Brand`, `FieldErrors`, form-field defaults, `PageCard`, `SubmitButton` (`shared/ui/index.ts:1-11`).
- Consumers in this inspected set: the files importing `shared/ui` are `login-page.ts`, `register-page.ts`, `profile-page.ts`, `password-dialog.ts`, `app.ts` and `warmup-panel.ts` (one import line each); `home-page.ts` has none (`features/home/home-page/home-page.html:1-4` uses only `pw-*` classes). The profile files are the only consumers of the `.pw-button-success` class (`profile-page.html:49`, `password-dialog.html:65`).
- Agent rules: `PigeonWatch/Frontend/CLAUDE.md` has a full UI section (tokens, Material overrides, `pw-*` classes, `shared/ui`, no literals). Across the inspected file, no rule invites one-off values. It does not say where a visual check or kitchen sink lives. There is no `AGENTS.md`, `.cursor/` or other rules file at the repo root (checked with `ls`).
- The profile stylesheets reference tokens: `profile-page.scss:1-15` uses `--pw-space-1`, `--pw-space-3` only through `var()`; `password-dialog.scss:1-5` uses `--pw-space-3`.

### View → source (profile files)

- Colour literals, palette classes, arbitrary values, `style="…"`, `::ng-deep`, `!important`: 0 occurrences in the 6 files (grep scan from the `/10x-ui` skill plus the rules in `CLAUDE.md`).
- Alert colours come from tokens (`shared/ui/alert/alert.scss:10-23`). `app-alert` sets `role="alert"` for errors and `role="status"` otherwise (`shared/ui/alert/alert.ts:11,20`).
- Focus: a single global rule `:focus-visible { outline: var(--pw-focus-ring-width) solid var(--pw-color-focus); … }` at `styles/_base.scss:50-53`. The profile files add no focus styling of their own.

### Charge details

See `## Charges` below.

### Reachability and entry points

- `/profile` is lazy-loaded behind `authGuard` (`app.routes.ts:22-27`). `authGuard` waits for `session.whenReady()` and redirects an unauthenticated visit to `/login?returnUrl=…` (`core/auth/auth.guards.ts:7-14,44-46`). So a logged-out arrival gets the login page, not a blank or raw-JSON profile.
- The header menu links to `/profile` (`app.html:12`).

## Charges

Five charges, ordered by the plan phases they map to (library/components → tokens → view → states). Each has evidence and the effect on the user.

### C1 — Missing shared-component variant: green submit is a global CSS class

- **Category**: missing shared component.
- **Evidence**: `profile-page.html:49` and `password-dialog.html:65` put `class="pw-button-success"` on `<app-submit-button>`. The class is defined in `styles/_material-theme.scss:84-91`, outside the "common classes" list in `Frontend/CLAUDE.md`. `login-page.html:31` and the register page use the default (primary) submit. `CLAUDE.md` says shared components "expose variations as inputs"; `SubmitButton` has `busy` and `disabled` but no tone input (`shared/ui/submit-button/submit-button.html:1-8`).
- **User impact**: the Save buttons on Profile are green while every other submit is blue, and nothing in the component API says why. The next view that wants "positive action" copies the class or invents another.
- **Decision for the plan**: keep green (give `SubmitButton` a `tone` input and move the class into the component) or switch Profile to the primary button. Prior review F8 deliberately centralised green Save (`context/archive/2026-10-10-edit-profile-name/reviews/impl-review.md:135-143`), so the default is to keep it and make it an input.

### C2 — Missing shared component: Cancel/Save action pair duplicated

- **Category**: missing shared component.
- **Evidence**: identical two-column grid and Cancel (outlined) + submit pair in `profile-page.html:39-52` with `profile-page.scss:11-15`, and in `password-dialog.html:55-68` with `password-dialog.scss:1-5`. Both use `repeat(2, minmax(0, 1fr))` with `--pw-space-3` gap.
- **User impact**: two places have to change together for any spacing, narrow-width stacking or button-order change; unverified on a 320px width because the stacking behaviour has never been screenshotted.
- **Fix direction**: one `shared/ui` component for the pair (for example `app-form-actions`), per the "promote when a pattern appears twice" rule in `CLAUDE.md`.

### C3 — Accidental architecture: notices stack and the error sits away from the action

- **Category**: accidental architecture.
- **Evidence**: three independent notice signals render in one block at the top of the card: form error (`profile-page.html:3-5`), password-changed (`:6-8`), name-saved (`:9-11`). `saved` and `passwordChanged` are separate signals (`profile-page.ts:89-90`). `changePassword()` resets only `passwordChanged` (`profile-page.ts:124-125`); `submit()` resets only `saved` (`profile-page.ts:148`). Nothing clears either on a timer. So after a successful name save followed by a successful password change, both success banners render together. The archived implementation review already noted that the two notices "can show next to" each other, but only as plan drift (`context/archive/2026-10-10-edit-profile-name/reviews/impl-review.md:56`); it was never raised as a UX problem.
- **User impact**: stale or doubled confirmations ("Name saved" is still shown after the user has moved on to the password change). In edit mode a server error from Save appears above the email row, while the Save button is at the bottom of the card (`profile-page.html:49`), so the error is not next to the action that caused it.
- **Fix direction**: one notice at a time (a single signal) and the form-level error placed inside the form near the actions. Decide whether success banners auto-dismiss.

### C4 — Accidental architecture: layout mirrors the order features were added

- **Category**: accidental architecture (layout; do after C1-C3, per the skill's order).
- **Evidence**: the card is one flat `pw-stack` (`profile-page.html:2-66`). Email and display name are `<span>` label/value pairs (`profile-page.html:12-17,55-58`) rather than a description list, so the label-to-value link is visual only. Edit (`:59-61`) and Change password (`:63-65`) are identical full-width outlined buttons. In edit mode Change password stays visible below Cancel/Save. The `page-card` footer `actions` slot (`shared/ui/page-card/page-card.html:12`, `<ng-content select="[actions]" />`) is not used by this view.
- **User impact**: two actions of different weight (rename vs account security) look the same and sit in the same stack; a screen reader reads the label and the value as unrelated text; in edit mode there are three buttons competing for attention.
- **Fix direction** (to be judged against a baseline screenshot, not decided now): semantic label/value rows, Edit as a lighter action next to the name, Change password in its own section or the card footer, hidden or disabled while editing.

### C5 — States: no visual evidence, and no visual gate in the repo

- **Category**: accidental architecture / process (the states phase's input).
- **Evidence by state** (profile page and dialog; observed in source, not on screen):
  - default / hover: Material defaults mapped through tokens (`_material-theme.scss:36-70`). Unverified visually.
  - focus-visible: global token-driven outline (`_base.scss:50-53`). Whether the outline doubles up with Material's own focus ring on outlined inputs is unverified.
  - disabled: Save is disabled until the trimmed name differs (`profile-page.ts:92-94`, `profile-page.html:49`); Cancel is disabled while busy (`:44`). Readability of the disabled green button is unverified.
  - error: field-level via `mat-error` + `app-field-errors` (`profile-page.html:32-37`), form-level via `app-alert kind="error"` with `role="alert"` (see C3).
  - empty: if `currentUser()` is null the email row is not rendered and the name is `''` (`profile-page.html:12`, `profile-page.ts:71`). Reachable only if the guard is bypassed, so treat as N/A with that reason, not as a charge.
  - loading: the route does not render until the session is ready (`auth.guards.ts:11-14`). Save shows a spinner (`submit-button.html:9-15`); the dialog sets `disableClose` while busy (`password-dialog.ts:126`).
- **Evidence for the gate**: a search of the frontend for `toHaveScreenshot`, Storybook and a screenshot/kitchen-sink page found none. The existing profile spec is jsdom unit tests of behaviour (`features/profile/profile-page/profile-page.spec.ts`, 28 `it(` blocks, lines 20-731), which do not check appearance.
- **User impact**: disabled, focus and error looks have never been looked at on a screen, which is where they drift first.
- **Fix direction**: baseline screenshots now (desktop + one mobile), then a kitchen-sink page for the 7-state matrix. Where it lives is an open question below.

## Deferred (not addressed by this change unless the plan pulls them in)

- **D1 — gradient literals repeat accent tokens.** `--pw-gradient-brand` and `--pw-gradient-backdrop` re-state the same hex/rgba values instead of referencing `--pw-color-accent-teal` / `--pw-color-accent-violet` (`_tokens.scss:13-14` vs `:22-26`). Global token hygiene, not part of the Profile view.
- **D2 — dark mode.** The theme is light only (`color-scheme: light`, `_material-theme.scss:4`; no `.dark` block). The app has no dark mode, so the "both themes" contrast check is N/A for this change.
- **D3 — whole-app visual regression tooling.** Installing a screenshot runner is out of scope; the skill says not to install one to satisfy it.

## Code References

- `Frontend/src/app/features/profile/profile-page/profile-page.html:1-67` - the card: notices, email, name read/edit, Change password.
- `Frontend/src/app/features/profile/profile-page/profile-page.ts:89-90,124-130,140-166` - notice signals, dialog open, submit.
- `Frontend/src/app/features/profile/password-dialog/password-dialog.html:1-67` - dialog form and actions.
- `Frontend/src/styles/_material-theme.scss:36-91` - button/card/form-field/dialog overrides and `.pw-button-success`.
- `Frontend/src/styles/_base.scss:50-53` - global focus-visible rule.
- `Frontend/src/app/shared/ui/submit-button/submit-button.html:1-16` - submit button without a tone input.
- `Frontend/src/app/shared/ui/page-card/page-card.html` - header, body slot, `actions` slot.
- `Frontend/src/app/core/auth/auth.guards.ts:7-14` - `authGuard`.

## Architecture Insights

- The repo's contract is two-layer and enforced: tokens (`--pw-*`) mapped onto Material system tokens, plus `shared/ui` components. The profile view obeys both; the weakness is at the seams: a variant implemented as a global class (C1) and a layout fragment copied between page and dialog (C2).
- State is held as separate booleans (`saved`, `passwordChanged`, `editing`, `busy`) rather than one view state, which is what lets notices overlap (C3).

## Historical Context (from prior changes)

- `context/archive/2026-10-10-edit-profile-name/plan.md:370-371` - the Edit/Cancel/Save mode and the password dialog were deliberate deviations from the planned always-editable field and second card. Supported by the later implementation; the layout in C4 is the result of those late decisions.
- `context/archive/2026-10-10-edit-profile-name/reviews/impl-review.md:135-143` (F8) - green Save was centralised as `.pw-button-success`; Cancel was made outlined. Supported, and it is the origin of C1.
- `context/archive/2026-10-10-edit-profile-name/reviews/impl-review.md:56` (F2) - notices can show side by side. Partial: it was recorded as plan drift, not as a UX problem (C3).
- `context/foundation/lessons.md` contains one entry (curl.exe in PowerShell); not relevant to UI.

## Related Research

None for UI. `context/archive/2026-10-04-register-and-login/research.md` covers the auth flow that introduced the design system; not re-read for this audit.

## Open Questions

1. Keep the green submit (C1) or move Profile to the primary button? It is a product/visual choice, not a code fact.
2. Where does the kitchen sink live without shipping to users (dev-only route, a spec that renders it, or a standalone page excluded from the production build)? The Angular build and the 500 kB initial-bundle budget (`Frontend/CLAUDE.md`) constrain this.
3. Do success notices auto-dismiss, or only one show at a time (C3)?
4. A live baseline is missing. Can the API and a test account be started locally for the Phase 1 screenshots? (Test credentials for a local dev build are allowed; the `context/deployment/credentials.local.md` file was not opened.)
