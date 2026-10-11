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
