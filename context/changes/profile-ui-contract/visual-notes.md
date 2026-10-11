# Visual notes: Profile view

Captured in the built-in browser pane against `ng serve` (development build) at `/dev/kitchen-sink`, with no API running. Desktop is the pane's responsive width (about 800 px wide, one 1280 px attempt was reset by the pane). Mobile is the 375x812 viewport preset. "Measured" means read from the DOM with the page's own computed styles; "seen" means looked at in a screenshot.

## Baseline (before Phases 2-4)

State of the code: commit `8bef79c` plus the Phase 1 kitchen sink. No production view file has changed.

### Per-state entries

| Cell | Desktop (seen) | 375 px (measured unless noted) |
| --- | --- | --- |
| Read-only | Email and name as small label over value; Edit and Change password are identical full-width outlined buttons; an empty grey footer strip sits under Change password. | Buttons 259 px wide, 1 line each, no overflow. |
| Edit, pristine | Name field with hint; Cancel (outlined) and Save side by side; Save is grey with grey text; Change password still shown below. Empty footer strip below. | Cancel and Save 124 px each, 1 line each. |
| Edit, client validation error | Red outlined field with "Use at least 3 characters." Save is green and enabled. | Same widths as pristine. |
| Edit, server error (`DuplicateDisplayName`) | Red outlined field with "This name is already taken." Save green. | Same widths. |
| Edit, server error (form-level) | Pink alert at the top of the card, above the Email row; the Save button is further down the card. | Same widths. Alert position not looked at in a 375 screenshot. |
| Saving | Cancel greyed out, Save greyed with a spinner; button height unchanged. | Cancel and Save 124 px, height unchanged. |
| Saved | Green success alert at the top, then email and the new name, Edit, Change password. | Buttons 259 px, 1 line. |
| Password changed | Not run in the screenshots; the cell shows the read-only card until "Run password change" is pressed. Not evaluated visually. | Not evaluated. |
| Dialog, default | Title, three outlined password fields, Cancel (outlined) and green "Change password" side by side. | Not opened at 375 in this cell; see the field-errors entry. |
| Dialog, field errors | Not looked at on desktop. | Three "This field is required." messages, Cancel and the green submit side by side; the submit label wraps to two lines. |
| Dialog, wrong current password | "The current password is incorrect." under the first field; the other two fields filled and valid; Cancel and Change password side by side. | Not looked at. |
| Dialog, busy | Cancel and submit both disabled and grey; the spinner in the submit button is a very small dot. Escape does not close the dialog. | Not looked at. |

### Answers to the research's unverified claims

- **Label wrapping at 375 px.** On the page, no label wraps or truncates: Cancel, Save, Edit and Change password are one line in every cell, and the document has no horizontal scroll (document width 375). **In the dialog the submit label "Change password" wraps to two lines** (the dialog surface is 274 px wide, each button 113 px). Cancel stays on one line. This decides the Phase 2 rule: stack Cancel and submit below the `sm` breakpoint with the submit first, because the pair is a shared component and the dialog is the narrow case.
- **Disabled green Save.** It is not green when disabled: Material renders it as a grey button (ink at 12 % as background, ink at 38 % as text). Computed contrast of the label is about 2.2:1 against its own background. Disabled controls are exempt from WCAG contrast, but the label is faint. Whether to lift it is a separate choice; it is recorded, not changed here.
- **Focus ring doubling on outlined inputs.** Not present. The focused input's own outline is `none`; the only indicator is Material's 2 px accent border on the notched outline (rgb 59, 91, 219), which is one ring. The global `:focus-visible` outline from `_base.scss` therefore does not stack on text inputs. Buttons were not checked in this pass (Phase 4 keyboard pass).
- **Where the form error sits relative to Save.** In the form-level error cell the alert is at the very top of the card, above the Email row, and Save is at the bottom of the form. Confirms C3.
- **Empty footer strip.** Every cell shows a grey footer band under the last button because `page-card` always renders its `actions` footer. Confirms the plan's premise for the Phase 2 change.
- **Change password in edit mode.** It stays visible below Cancel and Save in every edit cell. Confirms C4.

### Findings that are not in the plan

- **Warm-up overlay covers the sink when the API is down.** The app's own startup requests (`GET /configuration/client` and `GET /resources/en` from `provideAppInitializer` and `SessionService.initialize`) fail with `ERR_CONNECTION_REFUSED`, and after 3 s `app-warmup-panel` shows its full-screen "Waking up PigeonWatch" overlay on top of `/dev/kitchen-sink`. The sink's cells themselves make no API call, so the plan's wording "no API running" holds for the cells but not for the page as a whole. For the baseline screenshots the overlay element was removed in the page with the browser's script tool. **Fixed afterwards:** `KitchenSink` sets `hidden` on `app-warmup-panel` while it is alive and restores it on destroy (`kitchen-sink.ts`, covered by a spec case). Re-checked in the browser with the API down: after 9 s the panel is `display: none` and the sink is fully visible without any manual step. Production is unaffected, since the sink is not in the production build.
- **Pane width.** The browser pane is narrower than 1280 px and resets a custom width when it changes, so "desktop" here is about 800 px of pane width. The layout is capped at 40 rem either way, so the card looks the same.

## After (Phase 4)

State of the code: commit `2dc22de` (Phases 1-3) plus the Phase 4 changes: one added sink cell, extra sink assertions, the template checks in `style-token-coverage.spec.ts`, the rule in `Frontend/CLAUDE.md`, and one theme change found by the keyboard pass (`mat.strong-focus-indicators` in `_material-theme.scss`, see "Keyboard and accessible-name pass").

Captured in the built-in browser pane against `ng serve` (development build) at `/dev/kitchen-sink`, API down. Method limits worth knowing:

- The pane was hidden behind the app window, so screenshots were flaky: the first capture after a resize often timed out or came back blank, and several captures caught a fade-in animation half way (the cell was captured again once the animation had settled).
- "Desktop" is the pane's own width, 533 to 569 px, not 1280 px. The card is capped at 28 rem and the layout does not change above the `sm` breakpoint (480 px), so this is the same layout as a wide screen. Custom widths below 768 px make the pane emulate a phone without hover, so hover was only captured at the pane's own width.
- 375 px is the pane's 375x812 mobile preset. Where a number is given it was read from the DOM (`getBoundingClientRect`, computed style); "seen" means looked at in a screenshot.

### The 7-state matrix

| State | Profile page | Password dialog |
| --- | --- | --- |
| default | Shown: `read-only` and `edit-pristine` cells; desktop seen, 375 seen for both. | Shown: `dialog-default`; desktop seen. 375 not captured for the default cell (the field-errors cell at 375 shows the same layout). |
| hover | Shown on screen with the pane's real mouse at desktop width: Edit (state layer 0.08 of primary, seen as a lavender fill), Change password (0.08, seen), Cancel (0.08, seen), Save (white layer 0.12 on the green; Save was keyboard-focused at the time, so hover cannot be told apart from the focus tint in the screenshot). 375: N/A, a touch viewport has no hover (`(hover: hover)` is false). | Not captured separately: the buttons are the same components as on the page. |
| focus-visible | Shown with real Tab presses: Edit, Change password, name input, Cancel and Save each take focus and show a ring (see the pass below). Seen for Edit, Change password, input, Cancel and Save. | Tabbed through all five controls (3 fields, Cancel, submit) in a 375 px dialog; ring display read from the DOM for the buttons (`block`), the focused field's 2 px accent border seen in the screenshots. |
| disabled | Shown: `edit-pristine` (Save disabled, Cancel enabled) and `saving` (Cancel and Save disabled); desktop seen, 375 seen for pristine. | Shown: `dialog-default` (submit disabled until all fields are filled) and `dialog-busy` (Cancel and submit disabled; Escape does not close the dialog, checked by pressing it). |
| error | Shown: `edit-client-error` (red outlined field, "Use at least 3 characters."), `edit-duplicate-name` (red field, "This name is already taken."), `edit-form-error` (alert with a red tint and a left bar, "Something went wrong. Try again.", directly above Save). Desktop seen; 375 seen for client error and form-level. The input has `aria-invalid="true"` and is described by the `mat-error`; the form-level error has the `alert` role. Not colour alone: every error has text. | Shown: `dialog-field-errors` and `dialog-wrong-current` ("The current password is incorrect." under the first field, no alert). Desktop seen; field errors seen at 375. |
| empty | N/A: `authGuard` redirects a logged-out visit to `/login` before the page renders, so `currentUser()` is never null on screen (`auth.guards.ts:7-14`). | N/A for the same reason: the dialog only opens from the page. |
| loading | Shown: `saving` (spinner in Save, Cancel disabled, `aria-busy="true"` on Save). No layout jump: the button is 48 px high. Route-level loading: N/A, the route does not render before the session is ready. | Shown: `dialog-busy` (spinner, both buttons disabled, `disableClose` effective). |

Not shown on purpose: the sink has no cell for a very long display name (not in the plan).

### State-by-state delta against the Baseline

Labels: **intended** = the plan asked for it; **fixed** = a baseline finding that is now gone; **unchanged**; **deferred** = recorded, not changed.

| Cell | Baseline | After | Label |
| --- | --- | --- | --- |
| Read-only | Label-over-value `span` pairs; Edit and Change password identical full-width outlined buttons; empty grey strip under them. | `dl` rows (Email, Display name); Edit is a text button at the right of the name row, 64x40 px at 375; Change password is the stroked full-width button in the card footer; no empty strip. Desktop and 375 seen. | intended (C4); empty strip fixed |
| Edit, pristine | Change password still shown below the form; empty strip. | Only Cancel and Save below the field; no footer. Save grey (disabled), Cancel outlined. Desktop and 375 seen. | intended (C4); empty strip fixed |
| Edit, client validation error | Red field, Save green. | Same, no footer. | unchanged |
| Edit, `DuplicateDisplayName` | Red field, Save green. | Same, no footer. | unchanged |
| Edit, form-level error | Alert at the top of the card, above the Email row, far from Save. | Alert sits inside the form between the hint and the buttons; measured 16 px above Save at 375, seen at desktop and 375. | intended (C3) |
| Saving | Cancel grey, Save grey with a spinner, no height change. | Identical. The spinner is still a small blue arc on the grey button, low prominence. | unchanged; spinner prominence deferred (D5) |
| Saved | Success alert at the top, Edit and Change password identical. | Success alert at the top, `dl` rows, Edit lighter, Change password in the footer. | intended (C4) |
| Password changed | Not evaluated in the baseline. | Evaluated: after "Run password change" the card shows one `status` alert, "Your password has been changed.", the name unchanged, Change password in the footer. Desktop seen. | evaluated now |
| Saved, then password changed (new cell) | Not in the baseline. | Before the dialog: one alert (saved). After the dialog completes: one alert (password changed), never two. Read from the DOM and seen on screen. | intended (C3) |
| Dialog, default | Title, three fields, Cancel and green submit side by side. | Same at desktop (two columns). The first field is auto-focused with Material's single 2 px accent border. | unchanged |
| Dialog, field errors | At 375 the submit label "Change password" wrapped to two lines. | At 375 the pair is stacked, submit first; both buttons 232x48 px, label on one line, document width 375. Seen. | fixed (C2, the Phase 2 stacking rule) |
| Dialog, wrong current password | Error under the first field. | Same. | unchanged |
| Dialog, busy | Both disabled, small spinner, Escape ignored. | Same; Escape pressed again and the dialog stayed open. | unchanged |
| Cancel/Save pair at 375 on the page | Two columns, 124 px each. | Stacked, Save on top, Cancel below, 261x48 px each. | intended (Phase 2 rule); tab-order side effect in D3 |
| Keyboard focus on buttons | Not checked in the baseline. | Before the Phase 4 change: no ring at all on Material buttons, only a 12 % tint. After: a 2 px ring in the focus colour. | fixed (found by the pass, F1) |

### Keyboard and accessible-name pass

Real Tab, Shift+Tab, Return and Escape key presses in the pane; `document.activeElement`, `:focus-visible` and computed styles read after each press. The sink showed one cell at a time for this pass (other cells hidden with the script tool, not a change to the page).

Tab order. Read-only: Edit, then Change password (visual order). Edit mode: input, Cancel, Save (Save is skipped while disabled), visual order at desktop width. Dialog: current, new, confirm, Cancel, submit, then back to current (focus stays trapped in the dialog). At 375 px the stacked pair is a problem, see D3.

Accessible names, read from the accessibility tree and `labels`: Edit and Change password are named by their text; the name input is named "Display name" by its `label` and described by the hint or the error; Cancel and Save by text; Save while saving keeps its name and has `aria-busy="true"` with the spinner `aria-hidden`; the success notice is a `status`, the form-level error an `alert`; the three dialog fields are named by their labels. Nothing was found without a name.

Findings:

- **F1. Material buttons had no visible keyboard focus ring (fixed).** The global `:focus-visible` outline from `_base.scss:50-53` never shows on a Material button: the button's own stylesheet sets `outline: none`, and Material's own focus indicator (`.mat-focus-indicator::before`) is `display: none` unless `mat.strong-focus-indicators` is included, which the theme did not do. The only keyboard cue on Edit, Change password, Cancel and Save was the 12 % state-layer tint (measured: outline style `none`, indicator `display: none`, state layer 0.12). Fix: one mixin call in `_material-theme.scss` that points Material's indicator at the existing tokens (`--pw-color-focus`, `--pw-focus-ring-width`, `--pw-radius-md`). After: 2 px blue ring on Edit, Change password, Cancel and Save, seen on screen, not clipped by the card or the footer. **This is a global theme change, so it also gives the ring to the login, register and header buttons (those views were not opened in this pass); it is the only change in this phase outside the Profile view, the shared components, the sink and the guard.** Text inputs were not changed and do not double: the focused input's own outline is `none` and the one visible ring is Material's 2 px accent border (matches the baseline answer).
- **D1. Focus is lost after Edit, Cancel and a successful Save (deferred).** Pressing Edit removes the Edit button and shows the form; `document.activeElement` becomes `body` (same after Cancel). A keyboard user who presses Tab lands on the input in Chrome, but a screen reader user is left without a focus target. This behaviour predates this change (the baseline had the same button swap), so it is not a regression. A fix needs focus management in `ProfilePage` (focus the field after Edit, focus Edit after Cancel and Save) plus specs; that is behaviour work, not a visual contract fix, so it is left for its own change.
- **D2. The Edit button's accessible name is "Edit" only (deferred).** Context comes from the `dt` in the same row, which a screen reader reads when browsing, but the button read alone is just "Edit". A name such as "Edit display name" needs new resx text, which this change may not add. `aria-labelledby` over the existing "Display name" term and the button text would avoid new text but needs generated ids per card (the sink shows 13 cards on one page) and makes the name start with text that is not on the button, which weakens voice control by label. With one Edit per page and a `dl` row around it, not worth the cost now.
- **D3. At 375 px the stacked pair reads bottom to top for Tab (deferred, needs a decision).** Below `sm` the pair stacks with Save first (`column-reverse`) but the DOM order stays Cancel, Save; measured at 375: Cancel top 582, Save top 522, tab order Cancel then Save. Above `sm` the order matches. This follows directly from the Phase 2 rule "submit first when stacked", which was decided on the baseline; the two ways out are (a) accept it as the usual mobile pattern, or (b) stack Cancel above Save below `sm` (drop `column-reverse`) so tab order and visual order agree. A change to the stacking rule is a design decision, not made here.
- **D4. Disabled Save is faint (deferred, unchanged).** About 2.2:1 for the label on its grey fill, as in the baseline; disabled controls are exempt from the contrast rule, nothing was changed.
- **D5. The busy spinner is a very small arc (deferred, unchanged).** 18 px in a 48 px button; same as the baseline.

Not verified: that focus returns to Change password after the dialog closes (the sink opens the dialog from its own button by script, so there was no focused trigger to return to); the focus ring on the dialog buttons was read from the DOM (`display: block`), not looked at in a screenshot; nothing was captured at 1280 px wide (see method limits).
