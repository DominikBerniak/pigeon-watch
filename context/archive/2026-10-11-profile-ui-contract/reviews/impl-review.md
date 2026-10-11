<!-- IMPL-REVIEW-REPORT -->
# Implementation Review: Profile UI Contract Implementation Plan

- **Plan**: context/changes/profile-ui-contract/plan.md
- **Scope**: Full plan
- **Reviewed phases**: 1, 2, 3, 4
- **Date**: 2026-10-11
- **Verdict**: NEEDS ATTENTION
- **Findings**: 0 critical, 4 warnings, 4 observations

## Verdicts

| Dimension | Verdict |
|-----------|---------|
| Plan Adherence | WARNING |
| Scope Discipline | WARNING |
| Safety & Quality | WARNING |
| Architecture | PASS |
| Pattern Consistency | PASS |
| Success Criteria | WARNING |

Automated criteria were re-run on 2026-10-11 and all passed:
- `npm test -- --watch=false`: 26 files, 271 tests.
- `npm run build`: initial bundle 463.20 kB, within the 500 kB budget.
- No `kitchen-sink` text anywhere in `dist/`.
- `pw-button-success` grep: 0 hits.
- `Frontend/CLAUDE.md` grep finds `kitchen-sink`, `tone` and `app-form-actions`.
- Hardcoded-value scan: 0 hits.

The manual items have concrete evidence in `visual-notes.md`.

## Findings

### F1 — Stacked Cancel/Save: tab order disagrees with visual order below `sm`

- **Severity**: ⚠️ WARNING
- **Impact**: 🔎 MEDIUM — real tradeoff; pause to reason through it
- **Dimension**: Safety & Quality
- **Location**: PigeonWatch/Frontend/src/app/shared/ui/form-actions/form-actions.scss:10
- **Detail**: Below `sm`, the pair uses `flex-direction: column-reverse`, so Save is drawn above Cancel. The DOM order is still Cancel, Save, so Tab goes from the lower button to the upper one. Measured at 375 px, Cancel's top is 582 and Save's is 522. This breaks WCAG 2.4.3 (Focus Order) and 1.3.2 (Meaningful Sequence) on both the Profile page and the password dialog. `visual-notes.md` records it as D3 with "needs a decision", but it was left open. It comes straight from the plan's own rule, "submit first when stacked", so the plan is at fault here too.
- **Fix A ⭐ Recommended**: Below `sm`, use `flex-direction: column` (Cancel on top, Save below), so visual order and DOM order match at every width.
  - Strength: A one-line CSS change in one shared component. It fixes both consumers. The desktop "Cancel left of Save" spec still holds, and the dialog's label-wrap fix is kept, because stacking is what solved the wrap, not the order.
  - Tradeoff: The primary action is no longer first on mobile. Both buttons are full width, so it is still easy to reach.
  - Confidence: HIGH — the existing spec and the visual-notes measurements show the layout does not depend on the order.
  - Blind spot: `form-actions.spec.ts` may assert the reversed order. Update the D3 entry in visual-notes and the plan's stacking rule.
- **Fix B**: Keep `column-reverse` and record it as an accepted exception in `Frontend/CLAUDE.md` and in visual-notes D3.
  - Strength: No code change, and it keeps the "primary on top" mobile convention.
  - Tradeoff: A known accessibility defect ships in a shared component that every future form will reuse.
  - Confidence: MED — common on the web, but it still fails the focus-order criterion.
  - Blind spot: None significant.
- **Decision**: FIXED (Fix B) — accepted exception recorded in `Frontend/CLAUDE.md` (shared/ui bullet) and visual-notes D3.

### F2 — Unplanned behaviour change: dialog submit disabled until all fields are filled

- **Severity**: ⚠️ WARNING
- **Impact**: 🔎 MEDIUM — real tradeoff; pause to reason through it
- **Dimension**: Plan Adherence
- **Location**: PigeonWatch/Frontend/src/app/features/profile/password-dialog/password-dialog.ts:91
- **Detail**: Phase 2 said "with no other behavioural change". Commit 206d30a still added `allFieldsFilled` and bound it at `password-dialog.html:66` with `[disabled]="!allFieldsFilled()"`. It also changed the `dialog-field-errors` sink cell to fill invalid values, and added a spec. The commit message mentions the change, but the plan and the visual-notes delta table do not. Side effects:
  - The three "This field is required." messages can no longer be reached.
  - Enter in a field no longer submits, because implicit submission is blocked when the default button is disabled.
  - The disabled button drops out of the tab order with no explanation.
- **Fix A ⭐ Recommended**: Revert the gate and the sink-cell tweak, and keep the dialog's on-submit validation, as the plan intended.
  - Strength: Restores the planned scope, the required-field errors and Enter-to-submit. The baseline field-errors cell becomes valid again.
  - Tradeoff: Undoes a change someone may have wanted; a few lines in ts, html, the spec and the cell definition.
  - Confidence: MED — the plan is explicit, but the reason for the change was not recorded.
  - Blind spot: The gate may have been asked for during the Phase 2 manual check.
- **Fix B**: Keep it. Add a plan addendum and a visual-notes delta row, and note in the dialog that required errors are not shown by design.
  - Strength: Keeps the work. It matches the Profile Save, which is already disabled until the name changes.
  - Tradeoff: The Enter-key and tab-order regressions stay. The plan becomes a moving target.
  - Confidence: MED — there is a precedent in the same view, but the accessibility side effects are real.
  - Blind spot: Whether users expect Enter to submit in the dialog.
- **Decision**: FIXED (Fix B) — kept; documented as plan addendum A1 and a visual-notes delta row.

### F3 — Unplanned changes outside the view: global focus ring and supporting edits

- **Severity**: ⚠️ WARNING
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Scope Discipline
- **Location**: PigeonWatch/Frontend/src/styles/_material-theme.scss:84
- **Detail**: The keyboard pass (visual-notes F1) added a global `mat.strong-focus-indicators` call. It changes focus rings on login, register and the header, and `visual-notes.md:96` admits those views were not opened. The plan allowed fixes "in the component that owns it", not in the global theme. Other unplanned but benign extras:
  - The `label-coverage.spec.ts` exemption for `src/app/dev/`.
  - Hiding `app-warmup-panel` in the sink.
  - `.claude/launch.json`.
  - The pre-existing dirty tooling files (root `CLAUDE.md`, `.claude/skills/10x-ui`) bundled into aa482fe.
- **Fix**: Open `/login`, `/register` and the header in the browser pane, check the ring is visible and not clipped, and record that in visual-notes. Add a short plan addendum listing these extras.
- **Decision**: FIXED — login/register focus ring verified (computed styles, no clipping), header not checked (needs a logged-in session); recorded in visual-notes F1 follow-up and plan addendum A2.

### F4 — Kitchen-sink spec relies on real timers with a 5 s patience equal to the test timeout

- **Severity**: ⚠️ WARNING
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Success Criteria
- **Location**: PigeonWatch/Frontend/src/app/dev/kitchen-sink/kitchen-sink.spec.ts:8
- **Detail**: `patience = { timeout: 5000 }` equals Vitest's default `testTimeout`. Tests that chain `openDialogOf` with `eventually` can wait up to twice that, and every test boots all 13 cells and their 20 ms pollers. On a slow CI runner this fails as a test timeout rather than as a clean assertion failure. It passes today: 271 tests in about 10 s.
- **Fix**: Lower `patience.timeout` to 2000 ms, or set a larger per-file `testTimeout`.
- **Decision**: FIXED — `patience.timeout` set to 2000 ms; suite re-run: 271/271 pass.

### F5 — Kitchen-sink cells share the root MatDialog streams; driver errors unhandled

- **Severity**: 💡 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Safety & Quality
- **Location**: PigeonWatch/Frontend/src/app/dev/kitchen-sink/state-cell.ts:68
- **Detail**: A `MatDialog` provided per cell still delegates `afterOpened` and `openDialogs` to the root instance. As a result:
  - Two cells opened within one poll window would both take the first dialog.
  - Destroying a cell does not close its dialog.
  - `runSteps(...).subscribe()` has no error callback, and the `afterOpened` branch has no timeout, so a missed step can leave `running` stuck at true.

  The sink is dev-only, so the impact is small.
- **Fix**: Filter `afterOpened` to this cell's dialog, for example by checking that the ref's injector resolves this cell's `CellStubs`. Close it in `destroyRef.onDestroy`, and add an `error` callback that logs the cell id.
- **Decision**: FIXED — `afterOpened` filtered by the cell's `CellStubs` plus a `timeout`; the open dialog is closed on destroy; both subscriptions log errors with the cell id. Tests 271/271 pass and the build has no sink.

### F6 — "Clears the error notice when a new submit starts" does not test clearing at start

- **Severity**: 💡 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Success Criteria
- **Location**: PigeonWatch/Frontend/src/app/features/profile/profile-page/profile-page.spec.ts:470
- **Detail**: The second submit uses a synchronous `of(...)`, and the success notice overwrites the error either way. The test would still pass if the notice were cleared only on success.
- **Fix**: Hold the second request with a `Subject`, and assert that no `alert` is shown while busy, before emitting.
- **Decision**: FIXED — second request held with a `Subject`; asserts no alert or status while busy, then the success notice after emit. 271/271 pass.

### F7 — Colour-literal check can false-positive on hex-only template refs; host bindings not scanned

- **Severity**: 💡 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Success Criteria
- **Location**: PigeonWatch/Frontend/src/styles/style-token-coverage.spec.ts:14
- **Detail**: `/#[0-9a-fA-F]{3,8}\b/` run over templates matches template reference variables or fragments made only of hex letters, such as `#add`, `#face` or `#cafe`. Current refs like `#userMenu` pass. Also, `pw-*` classes applied from `.ts` `host` bindings or built dynamically (`'pw-' + x`) are not checked.
- **Fix**: Skip `#name` attribute tokens before running the colour check, for example by requiring the hex to sit in an attribute value or text, not directly after whitespace inside a tag.
- **Decision**: FIXED — `#ref` attribute names are stripped before the template colour check, and a sanity case was added (272/272 pass). The `.ts` host-binding gap was left as is: no component sets `pw-*` classes from `.ts` today.

### F8 — Notice cleared after the invalid-form guard in `submit()`

- **Severity**: 💡 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Plan Adherence
- **Location**: PigeonWatch/Frontend/src/app/features/profile/profile-page/profile-page.ts:157
- **Detail**: The plan says the notice is cleared "at the start of submit()". The code clears it after `if (this.busy() || this.profileForm().invalid()) return;`. So after a form-level server error, changing the name to an invalid value and pressing Save leaves the old "Something went wrong" alert next to the new field error. The pre-change code behaved the same way.
- **Fix**: Move `this.notice.set(null)` above the guard (the notice is already null while busy).
- **Decision**: FIXED — the notice is cleared before the guard, and a spec case was added for the blocked invalid resubmit. 273/273 pass; build at 463.20 kB initial.

## Triage summary

- **Fixed**: F3, F4, F5, F6, F7, F8 (6)
- **Fixed via Fix B** (documented and accepted rather than changed): F1, F2 (2)
- **Skipped**: none
- **Lessons**: none

Final check after triage:
- `npm test -- --watch=false`: 26 files, 273 tests pass.
- `npm run build`: initial bundle 463.20 kB, no sink in `dist/`.
