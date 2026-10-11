# Profile UI Contract Implementation Plan

## Overview

Bring the Profile view (`/profile`) and the password dialog it opens onto the design-system contract, and prove it on screen. The green Save becomes an input of `SubmitButton`, the Cancel/Save pair becomes a shared component, the card shows one notice at a time with the error next to the action, and the card gets a readable hierarchy. A dev-only kitchen sink gives a before/after screenshot gate for the 7-state matrix, and the change ends with a rule and a spec check that keep the next agent on the contract.

## Current State Analysis

The contract is already in place and enforced for this view: 0 hardcoded colour / palette / arbitrary-value hits in the 6 view files, only `pw-*` classes and `shared/ui` components in templates, and `style-token-coverage.spec.ts` guarding stylesheets. The audit (`research.md`, status `partial`: all findings come from source, none from a screen) found problems at the seams:

- **C1** The green Save is a global class, not a component variant. `class="pw-button-success"` sits on `<app-submit-button>` in `profile-page.html:49` and `password-dialog.html:65`; the override is defined in `_material-theme.scss:84-91`; `SubmitButton` has only `busy` and `disabled` (`submit-button.ts:13-14`).
- **C2** The Cancel/Save two-column grid is duplicated: `profile-page.scss:11-15` and `password-dialog.scss:1-5`.
- **C3** Three independent notice signals (`profile-page.html:3-11`, `profile-page.ts:74,89-90`) can render together, and a server error from Save renders at the top of the card, far from the Save button.
- **C4** The card is one flat stack: email and name are `<span>` pairs, Edit and Change password are identical full-width outlined buttons, and the `page-card` `actions` slot is unused by this view.
- **C5** There is no visual evidence and no visual gate in the repo (no screenshot tooling, no kitchen sink, no dev route).

Constraints from the repo:

- `Frontend/CLAUDE.md`: per-component Material tweaks live in `mat.<component>-overrides(...)` inside `_material-theme.scss`, never as CSS on `.mat-*` / `.mdc-*`; component stylesheets use `var(--pw-*)` and `pw-*` classes only; variations are inputs; every UI text is a key in `UiLabels.resx`; the initial bundle has a 500 kB budget (last measured 462.5 kB) and `core/` and `app.ts` import `shared/ui` by file path, not through the barrel.
- `page-card.html:11-13` always renders the `mat-card-actions` footer, with a top border and background (`page-card.scss:33-38`), even when nothing is projected.
- Production builds swap `environment.ts` for `environment.prod.ts` through `fileReplacements` (`angular.json`), which is the repo's existing mechanism for dev-versus-production differences.

## Desired End State

- `app-submit-button` has a `tone` input (`primary` default, `success`); no template uses `pw-button-success` and the class no longer exists.
- `app-form-actions` in `shared/ui` is the only place that lays out a Cancel + submit pair; the page and the dialog both use it and `password-dialog.scss` is gone.
- The Profile card shows at most one notice. Success notices appear at the top of the card; the Save error appears inside the form directly above the actions. Starting an edit, opening the password dialog, cancelling, and submitting each clear the current notice.
- Email and display name are a description list. Edit is a lighter text button in the name row. Change password lives in the `page-card` footer and is absent while editing (and no empty footer strip is left behind).
- `/dev/kitchen-sink` (development builds only) shows the Profile page and password dialog in every state of the 7-state matrix, or N/A with a reason, with no API or login. `npm run build` output contains no trace of it.
- `Frontend/CLAUDE.md` names the tone input, `app-form-actions` and the kitchen-sink route, and `style-token-coverage.spec.ts` fails when a template uses a `pw-*` class that `_common.scss` does not declare or contains a colour literal.
- `context/changes/profile-ui-contract/visual-notes.md` holds the before/after findings per state at desktop and 375px.

### Key Discoveries:

- Hardcoded-value scan already returns 0 hits on the view, so there is no token phase; a token is added only if a component needs one (`research.md`, Summary).
- Review F8 deliberately centralised green Save (`context/archive/2026-10-10-edit-profile-name/reviews/impl-review.md:135-143`); keeping it, as an input, honours that decision.
- `app-alert` already sets `role="alert"` for errors and `role="status"` otherwise (`shared/ui/alert/alert.ts:11,20`), so moving notices does not change their semantics.
- `authGuard` waits for the session and redirects logged-out visits to `/login?returnUrl=…` (`core/auth/auth.guards.ts:7-14`), so route-level empty and loading states are N/A.
- Existing profile spec helper reads notices by `app-alert[role=…]` (`profile-page.spec.ts:148,460`), which stays valid after the notice move.
- The only `pw-*` class used in templates that `_common.scss` does not declare is `pw-button-success` (grep of all templates), so a "declared in `_common.scss`" spec check passes once C1 lands.

## What We're NOT Doing

- Global token hygiene such as the gradient literals that restate accent hexes (`_tokens.scss:13-14` vs `:22-26`) — deferred D1.
- Dark mode — the app has none (`color-scheme: light`), so the "both themes" contrast check is N/A — deferred D2.
- Installing a screenshot runner or Storybook — deferred D3. The gate is the kitchen sink plus screenshots from the built-in browser.
- Moving the password dialog's form-level error: it stays at the top of the dialog content, which is short.
- Any change to other views (login, register, home), to the API, or to `UiLabels.resx` text for the existing profile keys.
- A stylelint or ESLint dependency; the guard is the CLAUDE.md rule plus the existing spec.
- Auto-dismissing success notices (decided: one at a time, no timer).
- Switching Profile Save to the primary blue (decided: keep green).

## Implementation Approach

Follow the `/10x-ui` order: environment (kitchen sink and baseline) → library (shared components) → one view → states and guard. The baseline is captured before anything changes so every visual claim in the research gets verified or corrected. Phase 2 adopts the new components in the page and dialog straight away, so the build never has a template pointing at a removed class and the view should look the same as the baseline except for any stacking decision. Phase 3 then restructures the card. Phase 4 fills the matrix, takes the "after" screenshots and leaves the guard.

Repo conventions to hold throughout: the working copy is CRLF, so files written with LF must be converted (see the `crlf-working-copy` memory); no code comments; TypeScript layout rules from `Frontend/CLAUDE.md` (blank line before `if`/`return`, no braces on one-line bodies).

## Critical Implementation Details

- **Dev-only route must not reach production.** Add `src/app/dev/dev-routes.ts` exporting `devRoutes` (the kitchen-sink lazy route) and register a `fileReplacements` entry in `angular.json` that swaps it for an empty-array version in the production configuration, the same way `environment.ts` is swapped. A runtime `environment.production` check around the lazy `import()` is not guaranteed to drop the chunk from the build, so the replacement is the mechanism and the `dist/` search in the success criteria is the proof. `...devRoutes` goes before the `**` route, which must stay last.
- **Dialog stubs need a scoped `MatDialog`.** `ProfilePage` calls `MatDialog.open(PasswordDialog)` with no config, and a root-provided `MatDialog` would instantiate the dialog with the real `SessionService`. Each kitchen-sink cell provides its own `MatDialog` next to its stub `SessionService`, `ConfigurationService` and `ProfileApi`. Verify in the browser that the dialog shows stub behaviour and issues no HTTP call.
- **Empty footer.** Hiding Change password while editing would leave an empty `mat-card-actions` strip, because `page-card.html:11-13` always renders it. `page-card` must collapse its footer when nothing is projected. The plan's first choice is a CSS `:empty` rule on the footer element (Angular drops whitespace-only text nodes, and an `@if` that renders nothing leaves only a comment node); if the browser shows the strip anyway, switch to an input on `app-page-card` instead of nested hacks.
- **Notice state.** A single signal replaces `saved`, `passwordChanged` and `formError` in `profile-page.ts`. The cleared-by events are: `startEditing()`, `changePassword()` (dialog opening), `cancel()`, and the start of `submit()`. The password-changed notice is set only when the dialog closes with `true`. Because Change password is hidden while editing, the dialog can only be opened from read-only mode, so the password-changed notice never appears over an open edit form.

## Phase 1: Kitchen sink and baseline

### Overview

Create the dev-only kitchen sink that renders the current Profile page and password dialog with stubbed services, capture the "before" evidence, and prove the production build excludes it. No production view changes in this phase.

### Changes Required:

#### 1. Dev routes and production replacement

**File**: `PigeonWatch/Frontend/src/app/dev/dev-routes.ts`, `PigeonWatch/Frontend/src/app/dev/dev-routes.prod.ts`, `PigeonWatch/Frontend/src/app/app.routes.ts`, `PigeonWatch/Frontend/angular.json`

**Intent**: Register the kitchen sink at `dev/kitchen-sink` in development builds only, and swap in an empty route list for production.

**Contract**: `dev-routes.ts` exports `devRoutes: Routes` (one route, no guard, lazy `loadComponent`). `dev-routes.prod.ts` exports `devRoutes: Routes = []`. `angular.json` production `fileReplacements` gains `{ "replace": "src/app/dev/dev-routes.ts", "with": "src/app/dev/dev-routes.prod.ts" }` beside the existing environment replacement. `app.routes.ts` spreads `...devRoutes` ahead of the `**` redirect, which stays last.

#### 2. Kitchen-sink page and state cells

**File**: `PigeonWatch/Frontend/src/app/dev/kitchen-sink/` (page and a state-cell wrapper component with `.ts`, `.html`, `.scss`)

**Intent**: Render the real `ProfilePage` and `PasswordDialog` in each state without an API or a login. A cell wrapper provides its own stubs (`SessionService`, `ConfigurationService`, `ProfileApi`, `MatDialog`) so each instance has independent state, then reaches its state by driving the component's own DOM (click Edit, type, submit, click Change password) rather than adding test hooks to production components. Dialog states open one at a time because the overlay is global.

**Contract**: Cells for the current view: read-only, edit pristine (Save disabled), edit with client validation error, edit with server error (`DuplicateDisplayName` and a form-level error), saving (stub never completes), saved, password-changed, dialog default, dialog with field errors, dialog wrong-current-password error, dialog busy. Stubs reuse the shapes the existing `profile-page.spec.ts` already builds (`user`, `updateDisplayName`, `changePassword` mocks). Templates and stylesheets follow the same token rules as the app (`pw-*` classes declared in `_common.scss`, `var(--pw-*)` only); state labels are literal English because the page is dev-only and never ships (recorded as an exception in Phase 4).

#### 3. Kitchen-sink spec

**File**: `PigeonWatch/Frontend/src/app/dev/kitchen-sink/kitchen-sink.spec.ts`

**Intent**: Make sure every cell renders without throwing, so the sink cannot silently rot.

**Contract**: One spec that mounts the page and asserts each labelled cell contains its expected markers (for example the Save button disabled in the pristine cell, a spinner in the saving cell, an `app-alert[role="alert"]` in the error cells).

#### 4. Baseline evidence

**File**: `context/changes/profile-ui-contract/visual-notes.md`

**Intent**: Record what the unchanged view actually looks like, state by state, at desktop and 375px, so Phases 2-4 are judged against it.

**Contract**: A "Baseline" section with one entry per state cell, and explicit answers to the research's unverified claims: label wrapping of Cancel / Save / Change password at 375px (decides the stacking rule in Phase 2), disabled green Save readability, focus-ring doubling on outlined inputs, and where the form error sits relative to Save.

### Success Criteria:

#### Automated Verification:

- Unit tests pass, including the new kitchen-sink spec: `npm test -- --watch=false` (from `PigeonWatch/Frontend`)
- Production build passes within the bundle budgets: `npm run build`
- Production output has no trace of the sink: no file under `dist/` contains `dev/kitchen-sink`
- Hardcoded-value scan from `/10x-ui` returns 0 hits on the 6 Profile view files and the kitchen-sink files

#### Manual Verification:

- With `npm start`, `/dev/kitchen-sink` renders every baseline cell of the unchanged view with no API running and no login, and the password dialog uses stub behaviour (no HTTP request to the API)
- Baseline screenshots at desktop and 375px have been reviewed and the findings recorded in `visual-notes.md`, including whether button labels wrap at 375px

**Implementation Note**: After completing this phase and all automated verification passes, pause here for manual confirmation from the human that the manual testing was successful before proceeding to the next phase. Phase blocks use plain bullets — the corresponding `- [ ]` checkboxes for these items live in the `## Progress` section at the bottom of the plan.

---

## Phase 2: Shared components (C1, C2)

### Overview

Add the missing shared-component variants and adopt them in the page and dialog. The Profile card should look the same as the baseline apart from the stacking rule below. There is no token phase: the scan found 0 literals; a token is added only if one of these components genuinely needs a new value.

### Changes Required:

#### 1. `SubmitButton` tone input

**File**: `PigeonWatch/Frontend/src/app/shared/ui/submit-button/submit-button.ts`, `submit-button.html`, `submit-button.spec.ts`, `PigeonWatch/Frontend/src/styles/_material-theme.scss`

**Intent**: Make the green "positive action" variant part of the component API instead of a global class, keeping the override where `Frontend/CLAUDE.md` says Material overrides live.

**Contract**: `readonly tone = input<'primary' | 'success'>('primary')`, reflected on the host as a `data-tone` attribute. `_material-theme.scss` keeps the `mat.button-overrides` mixin call but re-keys it from `.pw-button-success` to the component's host attribute selector for the success tone; `.pw-button-success` is removed. Spec cases: default is primary, `success` sets the attribute, busy and disabled behaviour unchanged.

#### 2. `app-form-actions`

**File**: `PigeonWatch/Frontend/src/app/shared/ui/form-actions/form-actions.ts`, `form-actions.html`, `form-actions.scss`, `form-actions.spec.ts`, `PigeonWatch/Frontend/src/app/shared/ui/index.ts`

**Intent**: One component owns the Cancel + submit pair layout so the page and the dialog cannot drift apart.

**Contract**: Standalone, OnPush, projects its two children in authored order into a two-column grid (`repeat(2, minmax(0, 1fr))`, `--pw-space-3` gap, the layout of `profile-page.scss:11-15`), `display: block; width: 100%`. Carries no text of its own. Stacking rule, decided by the Phase 1 baseline: keep two columns at every width unless the baseline shows a label wrapping or truncating at 375px; in that case stack to one column below the `sm` breakpoint (`@include bp.from(sm)`) with the submit button first. Exported from `index.ts`.

#### 3. `page-card` footer collapses when empty

**File**: `PigeonWatch/Frontend/src/app/shared/ui/page-card/page-card.scss`, `page-card.spec.ts` (and `page-card.html`/`page-card.ts` only if the CSS approach fails per Critical Implementation Details)

**Intent**: A card with nothing projected into `actions` should not draw an empty footer strip; this is needed by Phase 3 and must not change login, register or the warm-up panel.

**Contract**: The footer element is not displayed when it has no projected content. Spec cases: footer present when `[actions]` content is projected (existing test), footer hidden when none is.

#### 4. Adopt in the Profile page and dialog

**File**: `PigeonWatch/Frontend/src/app/features/profile/profile-page/profile-page.html`, `profile-page.scss`, `profile-page.ts`, `PigeonWatch/Frontend/src/app/features/profile/password-dialog/password-dialog.html`, `password-dialog.ts`, `password-dialog.scss` (deleted)

**Intent**: Replace `class="pw-button-success"` with `tone="success"` and the two copied grids with `app-form-actions`, with no other behavioural change.

**Contract**: `profile-page.scss` loses `.profile-actions`; `password-dialog.scss` is deleted and its `styleUrl` removed; `FormActions` is added to both components' `imports`; `app-form-actions` sits inside `mat-dialog-actions` in the dialog. Existing profile spec cases (for example "places Cancel to the left of Save", `profile-page.spec.ts:207`) must pass unchanged.

### Success Criteria:

#### Automated Verification:

- Unit tests pass, including the `SubmitButton` tone, `FormActions` and `PageCard` empty-footer cases: `npm test -- --watch=false`
- No template uses `pw-button-success` and `_material-theme.scss` no longer defines it (grep returns nothing)
- Production build passes within the bundle budgets: `npm run build`
- Hardcoded-value scan returns 0 hits on the Profile view files, the new components and the kitchen-sink files

#### Manual Verification:

- In `/dev/kitchen-sink` the Profile Save and the dialog Save are still green and the Cancel/Save pair looks like the baseline (or stacks as the 375px rule decided); login and register submit buttons are still blue
- Login, register and the warm-up panel still show their footer links exactly as before (page-card footer change has no regression)

**Implementation Note**: After completing this phase and all automated verification passes, pause here for manual confirmation from the human that the manual testing was successful before proceeding to the next phase. Phase blocks use plain bullets — the corresponding `- [ ]` checkboxes for these items live in the `## Progress` section at the bottom of the plan.

---

## Phase 3: Profile view (C3, C4)

### Overview

Restructure the card: one notice at a time with the error beside the action, semantic email/name rows, Edit as a lighter action, and Change password in the card footer.

### Changes Required:

#### 1. Single notice

**File**: `PigeonWatch/Frontend/src/app/features/profile/profile-page/profile-page.ts`, `profile-page.html`

**Intent**: Replace `saved`, `passwordChanged` and `formError` with one notice signal so stale or doubled banners are impossible, and render the Save error directly above the form actions.

**Contract**: One signal holding `null`, a saved notice, a password-changed notice, or an error (reusing `AuthFormError` and `authErrorMessage`). Set on submit success, on dialog close with `true`, and on submit failure. Cleared by `startEditing()`, `changePassword()`, `cancel()` and the start of `submit()`. Success notices render at the top of the card body as `app-alert kind="success"`; the error renders inside the edit form immediately above `app-form-actions` as `app-alert kind="error"`. Alert roles are unchanged.

#### 2. Semantic rows and action hierarchy

**File**: `PigeonWatch/Frontend/src/app/features/profile/profile-page/profile-page.html`, `profile-page.scss`

**Intent**: Make label-to-value association real and give the two kinds of action different weight.

**Contract**: Email and display name become a description list (`dl` with a `dt`/`dd` pair per row, rows wrapped in a `div` as HTML allows). In read-only mode the name row carries a text-style `mat-button` Edit beside the value, keeping the existing `profile.edit` label. In edit mode the name row is the form field and `app-form-actions`; the email row remains. Change password moves out of the body into the `page-card` `[actions]` slot as a stroked button, rendered only when not editing. Styles use `--pw-space-*` tokens and existing `pw-*` classes; no literals.

#### 3. Profile spec updates

**File**: `PigeonWatch/Frontend/src/app/features/profile/profile-page/profile-page.spec.ts`

**Intent**: Keep the 28 existing behaviour cases green and add cases for the new structure and notice rule.

**Contract**: New or changed cases: after a name save followed by a password change, only the password notice is present; after a password change followed by Edit, no notice is present; a server error renders inside the form above the Save button, not above the email row; Change password is in the card footer when read-only and absent while editing; email and name render as `dt`/`dd` pairs. Helpers that find Edit and Change password by button text (`buttonByText`, `profile-page.spec.ts:72-82`) are updated if the button leaves the body.

### Success Criteria:

#### Automated Verification:

- Unit tests pass with the new and updated cases: `npm test -- --watch=false`
- Production build passes within the bundle budgets: `npm run build`
- Hardcoded-value scan returns 0 hits and the style-token-coverage spec passes on the Profile view files

#### Manual Verification:

- In `/dev/kitchen-sink` read-only mode at desktop and 375px the card reads with a clear hierarchy: Edit is lighter than Save, Change password is in the footer, and in edit mode no empty footer strip remains
- Success and server-error notices in the sink: success at the top, error directly above Save, never two notices together

**Implementation Note**: After completing this phase and all automated verification passes, pause here for manual confirmation from the human that the manual testing was successful before proceeding to the next phase. Phase blocks use plain bullets — the corresponding `- [ ]` checkboxes for these items live in the `## Progress` section at the bottom of the plan.

---

## Phase 4: States, gate and guard (C5)

### Overview

Complete the 7-state matrix in the kitchen sink, capture the "after" screenshots, do the keyboard and accessible-name pass, and leave the rule and spec check that keep the next agent on the contract.

### Changes Required:

#### 1. Complete the matrix and the new-structure cells

**File**: `PigeonWatch/Frontend/src/app/dev/kitchen-sink/` (cells and spec), `context/changes/profile-ui-contract/visual-notes.md`

**Intent**: Make every state of the restructured view visible, and record which cells are shown and which are N/A with a reason.

**Contract**: Matrix for Profile page and dialog — default (read-only, edit pristine); hover (Material state layer on Edit, Change password, Cancel, Save; captured with the browser pane's hover); focus-visible (keyboard focus on every control; the single global outline from `_base.scss:50-53`, checked for doubling on outlined inputs); disabled (pristine Save, Cancel while busy); error (field error after submit, `DuplicateDisplayName`, form-level error above Save, dialog wrong current password, text plus alert role, not colour alone); empty (N/A: `authGuard` redirects before the page renders, so `currentUser()` is never null on screen); loading (Save spinner with no layout jump, Cancel disabled, dialog `disableClose`; route-level loading N/A for the same reason). `visual-notes.md` gets an "After" section and a state-by-state delta against the baseline, each delta labelled intended or not.

#### 2. Keyboard and accessible-name pass

**File**: findings in `context/changes/profile-ui-contract/visual-notes.md`; fixes in the Profile view and shared components only if the pass finds a defect

**Intent**: Verify the floor: every control has an accessible name and a visible focus ring, and tab order follows the visual order.

**Contract**: Tab through the page in read-only and edit mode and through the dialog; any defect found is fixed in the component that owns it (or recorded as deferred with a reason).

#### 3. Spec check

**File**: `PigeonWatch/Frontend/src/styles/style-token-coverage.spec.ts`

**Intent**: Turn the C1 class of drift into a failing test instead of a rule the next agent can forget.

**Contract**: Two added assertions over every template under `src/app` (which includes the dev folder): each `pw-*` class used in a template is declared in `_common.scss`; no template contains a colour literal (`#hex`, `rgb(`, `rgba(`, `hsl(`, `hsla(`). The existing inline-`style` check stays.

#### 4. Agent rule

**File**: `PigeonWatch/Frontend/CLAUDE.md`

**Intent**: Extend the existing UI section (not a new file) so the next agent finds the contract: variants are inputs, not global classes; where the kitchen sink lives and how to open it; the dev-page exception.

**Contract**: In the `shared/ui` bullet, list `app-form-actions` and note `app-submit-button` has `tone`; add a bullet that a variant is a component input and a global `pw-button-*`-style class is not allowed (enforced by the spec); add a bullet naming `src/app/dev/kitchen-sink` and the `dev/kitchen-sink` route (development builds only, removed from production through `fileReplacements`), requiring a new Profile state to get a cell, and recording that its state labels are literal English because the page never ships. No rule is found that invites one-off values (checked in the audit); none is added.

### Success Criteria:

#### Automated Verification:

- Unit tests pass, including the extended `style-token-coverage.spec.ts` and the kitchen-sink spec: `npm test -- --watch=false`
- Production build passes within the bundle budgets: `npm run build`
- Production output has no trace of the sink: no file under `dist/` contains `dev/kitchen-sink`
- Hardcoded-value scan returns 0 hits (no higher than the Phase 1 count) on the Profile view files, the shared components and the kitchen-sink files
- `Frontend/CLAUDE.md` names `kitchen-sink`, `tone` and `app-form-actions` (grep)

#### Manual Verification:

- Every cell of the 7-state matrix in `visual-notes.md` is shown on screen or marked N/A with a reason, at desktop and 375px
- Keyboard pass: every control on the page and the dialog shows a visible focus ring, has an accessible name, and tabs in visual order
- "After" screenshots compared with the baseline; every visual delta in `visual-notes.md` is labelled intended or fixed
- The next step, `/10x-impl-review`, is run before archive; UI findings are triaged by user impact, not skipped as cosmetic

**Implementation Note**: After completing this phase and all automated verification passes, pause here for manual confirmation from the human that the manual testing was successful before proceeding to the next phase. Phase blocks use plain bullets — the corresponding `- [ ]` checkboxes for these items live in the `## Progress` section at the bottom of the plan.

---

## Testing Strategy

### Unit Tests:

- `SubmitButton` tone, `FormActions` projection and layout class, `PageCard` empty-footer behaviour.
- `ProfilePage`: single-notice rule and its cleared-by events, error placement, footer Change password present/absent, `dl` structure; the 28 existing cases stay green.
- Kitchen-sink spec: every cell renders and carries its state marker.
- `style-token-coverage.spec.ts`: undeclared `pw-*` classes and colour literals in templates fail.

### Integration Tests:

- None added. The repo has no browser test runner and the plan does not install one; the kitchen sink plus browser-pane screenshots is the visual gate.

### Manual Testing Steps:

1. `npm start`, open `/dev/kitchen-sink`, go through each cell at desktop and at 375px (browser pane viewport emulation).
2. Hover each button; tab through the page in read-only and edit mode and through the dialog.
3. Open `/login` and `/register` to confirm submit buttons and card footers are unchanged.
4. Run `npm run build` and confirm the production output does not contain the sink.

## Performance Considerations

The sink and its stubs sit in a lazy chunk that is replaced by an empty route list in the production configuration, so the initial bundle (462.5 kB, budget 500 kB) is unaffected. `app-form-actions` and the `tone` input are tiny; lazy Profile code may use the `shared/ui` barrel, but `app.ts` and `core/` must keep importing by file path. `npm run build` is run in every phase to catch a budget regression.

## Migration Notes

None: no data, API or resx changes. The `.pw-button-success` class is removed in Phase 2 together with its only two usages.

## References

- Related research: `context/changes/profile-ui-contract/research.md`
- Prior decision to keep green Save: `context/archive/2026-10-10-edit-profile-name/reviews/impl-review.md:135-143`
- Design-system contract and UI rules: `PigeonWatch/Frontend/CLAUDE.md`, `.claude/skills/10x-ui/SKILL.md`
- Token source: `PigeonWatch/Frontend/src/styles/_tokens.scss`; Material mapping: `PigeonWatch/Frontend/src/styles/_material-theme.scss`
- Existing token guard: `PigeonWatch/Frontend/src/styles/style-token-coverage.spec.ts`
- Existing profile spec patterns: `PigeonWatch/Frontend/src/app/features/profile/profile-page/profile-page.spec.ts`

## Progress

> Convention: `- [ ]` pending, `- [x]` done. Append ` — <commit sha>` when a step lands. Do not rename step titles. See `references/progress-format.md`.

### Phase 1: Kitchen sink and baseline

#### Automated

- [x] 1.1 Unit tests pass, including the new kitchen-sink spec: `npm test -- --watch=false` (from `PigeonWatch/Frontend`) — aa482fe
- [x] 1.2 Production build passes within the bundle budgets: `npm run build` — aa482fe
- [x] 1.3 Production output has no trace of the sink: no file under `dist/` contains `dev/kitchen-sink` — aa482fe
- [x] 1.4 Hardcoded-value scan from `/10x-ui` returns 0 hits on the 6 Profile view files and the kitchen-sink files — aa482fe

#### Manual

- [x] 1.5 With `npm start`, `/dev/kitchen-sink` renders every baseline cell of the unchanged view with no API running and no login, and the password dialog uses stub behaviour (no HTTP request to the API) — aa482fe
- [x] 1.6 Baseline screenshots at desktop and 375px have been reviewed and the findings recorded in `visual-notes.md`, including whether button labels wrap at 375px — aa482fe

### Phase 2: Shared components (C1, C2)

#### Automated

- [x] 2.1 Unit tests pass, including the `SubmitButton` tone, `FormActions` and `PageCard` empty-footer cases: `npm test -- --watch=false` — 206d30a
- [x] 2.2 No template uses `pw-button-success` and `_material-theme.scss` no longer defines it (grep returns nothing) — 206d30a
- [x] 2.3 Production build passes within the bundle budgets: `npm run build` — 206d30a
- [x] 2.4 Hardcoded-value scan returns 0 hits on the Profile view files, the new components and the kitchen-sink files — 206d30a

#### Manual

- [x] 2.5 In `/dev/kitchen-sink` the Profile Save and the dialog Save are still green and the Cancel/Save pair looks like the baseline (or stacks as the 375px rule decided); login and register submit buttons are still blue — 206d30a
- [x] 2.6 Login, register and the warm-up panel still show their footer links exactly as before (page-card footer change has no regression) — 206d30a

### Phase 3: Profile view (C3, C4)

#### Automated

- [x] 3.1 Unit tests pass with the new and updated cases: `npm test -- --watch=false` — 2dc22de
- [x] 3.2 Production build passes within the bundle budgets: `npm run build` — 2dc22de
- [x] 3.3 Hardcoded-value scan returns 0 hits and the style-token-coverage spec passes on the Profile view files — 2dc22de

#### Manual

- [x] 3.4 In `/dev/kitchen-sink` read-only mode at desktop and 375px the card reads with a clear hierarchy: Edit is lighter than Save, Change password is in the footer, and in edit mode no empty footer strip remains — 2dc22de
- [x] 3.5 Success and server-error notices in the sink: success at the top, error directly above Save, never two notices together — 2dc22de

### Phase 4: States, gate and guard (C5)

#### Automated

- [x] 4.1 Unit tests pass, including the extended `style-token-coverage.spec.ts` and the kitchen-sink spec: `npm test -- --watch=false` — 3cf9873
- [x] 4.2 Production build passes within the bundle budgets: `npm run build` — 3cf9873
- [x] 4.3 Production output has no trace of the sink: no file under `dist/` contains `dev/kitchen-sink` — 3cf9873
- [x] 4.4 Hardcoded-value scan returns 0 hits (no higher than the Phase 1 count) on the Profile view files, the shared components and the kitchen-sink files — 3cf9873
- [x] 4.5 `Frontend/CLAUDE.md` names `kitchen-sink`, `tone` and `app-form-actions` (grep) — 3cf9873

#### Manual

- [x] 4.6 Every cell of the 7-state matrix in `visual-notes.md` is shown on screen or marked N/A with a reason, at desktop and 375px — 3cf9873
- [x] 4.7 Keyboard pass: every control on the page and the dialog shows a visible focus ring, has an accessible name, and tabs in visual order — 3cf9873
- [x] 4.8 "After" screenshots compared with the baseline; every visual delta in `visual-notes.md` is labelled intended or fixed — 3cf9873
- [ ] 4.9 The next step, `/10x-impl-review`, is run before archive; UI findings are triaged by user impact, not skipped as cosmetic
