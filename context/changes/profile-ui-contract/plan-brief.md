# Profile UI Contract — Plan Brief

> Full plan: `context/changes/profile-ui-contract/plan.md`
> Research: `context/changes/profile-ui-contract/research.md`

## What & Why

Bring the Profile view and its password dialog onto the design-system contract and prove it on screen. The view already obeys the tokens (0 hardcoded-value hits), but its seams drifted: green Save is a global CSS class, the Cancel/Save pair is copied twice, notices can stack, and nothing has ever been looked at on a screen. The change fixes the seams, adds a visual gate, and leaves a rule and a spec check so the next view inherits the contract.

## Starting Point

`/profile` is a flat card: email and name as loose spans, identical full-width Edit and Change password buttons, three independent notice flags, and `.pw-button-success` applied by class. The repo has `--pw-*` tokens, Material mapped onto them, `shared/ui` components and a style-token spec, but no screenshot tooling, no kitchen sink and no dev route. Research is status `partial`: every finding is from source, none from a screen.

## Desired End State

`SubmitButton` has a `tone` input and the global class is gone; one `app-form-actions` lays out Cancel + submit in the page and the dialog. The card shows one notice at a time (success on top, Save error directly above the buttons), email and name as a description list, Edit as a light text button in the name row, and Change password in the card footer (absent while editing, with no empty footer strip). A development-only `/dev/kitchen-sink` shows every state with no API or login, production builds do not contain it, and `Frontend/CLAUDE.md` plus the style-token spec keep the next agent on the contract.

## Key Decisions Made

| Decision | Choice | Why (1 sentence) | Source |
| --- | --- | --- | --- |
| Save button colour | Keep green as a `SubmitButton` `tone` input | Review F8 centralised green Save; an input makes the variant discoverable instead of a copyable class. | Plan |
| Notices | One signal, one notice at a time, no auto-dismiss; error next to the buttons | Ends doubled or stale banners and puts the error beside the action that caused it, without timers that can hide a message from a screen reader. | Plan |
| Layout scope | `dl` rows, Edit as text button, Change password in `page-card` footer hidden while editing | Fixes label/value semantics and equal-weight buttons, and finally uses the existing `actions` slot. | Plan |
| Visual gate | Dev-only lazy route with stubbed services, screenshots from the built-in browser | No API or login needed, a repeatable before/after, and the initial bundle budget is untouched. | Plan |
| Keeping the sink out of production | `fileReplacements` swap of `dev-routes.ts` for an empty list | Reuses the repo's existing dev-versus-prod mechanism; a runtime check is not guaranteed to drop the chunk. | Plan |
| Guard | CLAUDE.md rule plus a spec check (template `pw-*` classes must be declared in `_common.scss`, no colour literals) | A failing test beats a rule an agent forgot, with no new dependency. | Plan |
| Token phase | Skipped | The scan found 0 literals; a token is added only if a component needs one. | Research |
| Empty and route-level loading states | N/A with reason | `authGuard` redirects before the page renders. | Research |
| Deferred | Gradient literals (D1), dark mode (D2), screenshot tooling (D3) | Outside one view; the skill says not to install a runner for this. | Research |

## Scope

**In scope:** `profile-page` and `password-dialog`; `SubmitButton` tone; new `app-form-actions`; `page-card` footer collapsing when empty; a dev-only kitchen sink with spec; `style-token-coverage.spec.ts` extension; `Frontend/CLAUDE.md` UI rule; `visual-notes.md` evidence.

**Out of scope:** other views, API and resx text, global token hygiene, dark mode, a screenshot runner or lint dependency, moving the dialog's form error, auto-dismissing notices, switching Save to blue.

## Architecture / Approach

Follow the `/10x-ui` order: environment (sink and baseline) → shared components (adopted at once so the build stays green) → the view → states and guard. Each cell of the sink wraps the real `ProfilePage` or dialog with its own stub `SessionService`, `ConfigurationService`, `ProfileApi` and `MatDialog`, and reaches its state by driving the component's own DOM, so no test hooks are added to production code. The sink's route list is replaced by an empty one in the production configuration.

## Phases at a Glance

| Phase | What it delivers | Key risk |
| --- | --- | --- |
| 1. Kitchen sink and baseline | Dev-only `/dev/kitchen-sink`, spec, production exclusion, baseline findings in `visual-notes.md` | Scoped stubs must really isolate the dialog (needs a per-cell `MatDialog`); chunk must be absent from `dist/` |
| 2. Shared components (C1, C2) | `tone` input, `app-form-actions`, collapsing `page-card` footer, adopted in page and dialog | The footer `:empty` rule may not hold with projected `@if`; 375px stacking decided by the baseline |
| 3. Profile view (C3, C4) | Single notice, `dl` rows, Edit text button, Change password in footer, updated specs | Rewrites notice state; 28 existing spec cases must stay green |
| 4. States, gate and guard (C5) | 7-state matrix, keyboard pass, after screenshots, spec check, CLAUDE.md rule | Hover and focus can only be shown from the browser pane, not from unit tests |

**Prerequisites:** none beyond the repo; no API, credentials or new dependencies.
**Estimated effort:** ~3-4 sessions across 4 phases (Phase 1 and Phase 3 are the largest).

## Open Risks & Assumptions

- The visual claims in the research are unverified until Phase 1; the baseline may overturn the stacking rule or the layout detail.
- The app shell may still attempt its normal startup calls when `/dev/kitchen-sink` is opened; the sink itself needs no network.
- The built-in browser pane is assumed available for screenshots and hover/focus; if not, findings are recorded in text only.
- Repo files are CRLF; new files must be converted after writing.

## Success Criteria (Summary)

- A reader of the Profile card sees one notice at a time, the error beside Save, and a clear hierarchy between rename and password actions.
- Every state in the 7-state matrix is on screen at desktop and 375px, or marked N/A with a reason, and the delta from the baseline is explained.
- The next agent is blocked by a failing spec from reintroducing a global variant class or a colour literal in a template, and the rule says where the kitchen sink lives.
