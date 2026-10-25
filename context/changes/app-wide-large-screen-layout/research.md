---
date: 2026-10-19T00:00:00+02:00
researcher: Dominik Berniak
git_commit: fb97eaa78be9b6d0d7b1ba11538f2656c756c4e3
branch: main
repository: 10xDevs
topic: "Which views and shared pieces must change to make the PigeonWatch app use large screens, and should login and register stay narrow?"
tags: [research, codebase, frontend, layout, app-shell, app-page-card, app-detail-list, map-view]
status: complete
last_updated: 2026-10-19
last_updated_by: Dominik Berniak
---

# Research: App-wide layout for large screens

**Date**: 2026-10-19
**Researcher**: Dominik Berniak
**Git Commit**: fb97eaa78be9b6d0d7b1ba11538f2656c756c4e3
**Branch**: main
**Repository**: 10xDevs

## Research Question

`change.md` raises a scope question: at about 2000 px the sighting detail page is a 28rem card in a mostly empty page. This change is app-wide, but `/10x-ui` normally takes one view. Which views and which shared pieces (shell width, `app-page-card`, `app-detail-list`) change, and should login and register stay narrow?

## Summary

In the inspected frontend (`PigeonWatch/Frontend/src`), width is set by four independent caps, all tokens in `styles/_tokens.scss`:

1. The shell `<main>` is capped at `--pw-content-max-width` (40rem), or `--pw-content-wide-max-width` (64rem) from `md` when the active route has `data: { layout: 'wide' }` (`app/app.scss:22-32`, `styles/_tokens.scss:70-71`).
2. `app-page-card` caps its host at `--pw-form-max-width` (28rem) and centres it (`app/shared/ui/page-card/page-card.scss:3-10`, `styles/_tokens.scss:74`).
3. The header bar uses `pw-container`, capped at 40rem, on every route including the wide one (`styles/_common.scss:1-5`, `app/app.html:5`).
4. The map canvas has a fixed height of `--pw-map-height` (24rem) and the map home's aside a fixed width of `--pw-map-aside-width` (18rem) (`styles/_tokens.scss:78-79`, `app/shared/map/map-view/map-view.scss:5-7`).

Layout rules under `src/app` use exactly three breakpoints: `sm` (2 uses), `md` (2 uses) and `lg` (1 use), counted by grep for `from(` over `src/app`. `xl` (1280px) is defined in `styles/_breakpoints.scss:4-7` but no rule under `src/app` uses it. At 2000 px every route therefore renders its `lg` layout: the map home at 64rem, every other page as a 28rem card (plus the 40rem My sightings card on the profile page).

Two product routes have content that could use more space: the map home (already wide, but capped at 64rem with a short map) and the sighting detail page (a single-column list of seven label/value rows, then a map behind a toggle). The other card pages (login, register, profile, report) are forms or a short list, and a narrow centred column is the usual pattern for them.

## Detailed Findings

### Shell width and the route flag

- `.app-main` is `max-width: var(--pw-content-max-width)`, auto margins, `padding: 0 var(--pw-space-4)` (`app/app.scss:22-26`). `.app-main--wide` raises the cap to `--pw-content-wide-max-width` from `md` only (`app/app.scss:28-32`).
- `App.wide` is a signal over `NavigationEnd`, true when the deepest active route's `data['layout'] === 'wide'` (`app/app.ts:40-48`, `app/app.ts:57-63`). The flag is binary: there is no third width and no per-route value.
- Routes: on the product routes in `app/app.routes.ts`, only `''` (map home) sets `layout: 'wide'` (`app/app.routes.ts:6-12`); the dev kitchen-sink route also does (`app/dev/dev-routes.ts:6`).
- `app/app.spec.ts:126-132` pins the product routes with `layout: 'wide'` to exactly `['']`. Adding another wide route (for example `sightings/:id`) fails this spec until it is edited. `app/app.spec.ts:109-124` pins that `/profile` does not get `app-main--wide`.
- The guest shell (`app-main--guest`) centres its content vertically in a flex column and is independent of the wide flag (`app/app.scss:34-47`); login and register render `app-page-card` inside it (`login-page.html:1`, `register-page.html:1`, under `app/features/auth/`).
- The warm-up overlay has its own 28rem content cap (`app/core/warmup/warmup-panel/warmup-panel.scss:12-14`). It is a centred overlay message, not a consumer of page widths.

### app-page-card

- Host: `display: block; width: 100%; max-width: var(--pw-form-max-width); margin-inline: auto` (`page-card.scss:3-10`). Used by login, register, profile, report and detail pages (first line of each page's `.html`).
- The cap is a component-level constant, so every consumer gets 28rem and a page cannot widen its card without a new input or a different wrapper. `Frontend/CLAUDE.md` says a variation of a shared component is a component input keyed through tokens, never a global class; a width input would follow that rule.
- On the profile page the page card is 28rem, but `app-my-sightings` is a plain `mat-card` with `:host { display: block }` and no width cap (`features/sightings/my-sightings/my-sightings.scss:1-3`, `my-sightings.html:1`), so it fills the 40rem column. The archived `report-sighting-on-map` visual notes recorded this mismatch as V2 (`context/archive/2026-10-11-report-sighting-on-map/visual-notes.md:33`); by the inspected code it still exists. Any change to the page-card cap or the shell cap moves this relationship.

### Map home (`/`)

- The page body is a grid: one column below `lg`, `minmax(0, 1fr) var(--pw-map-aside-width)` from `lg` (`nearby-map-page.scss:36-43`); the toolbar becomes a row from `md` (`nearby-map-page.scss:8-16`).
- The map canvas height is the fixed `--pw-map-height` token (24rem) at all widths (`map-view.scss:5-7`). At the 64rem cap the map column is the remainder after the 18rem aside and the grid gap, so extra screen width never reaches the map, and extra screen height is not used either.
- The header stays at the 40rem `pw-container` while `<main>` is 64rem on this route (`app.html:5`, `app.scss:28-32`). `Frontend/CLAUDE.md` records this as intended ("The header keeps the narrow width"); the visual mismatch grows if the wide cap grows.

### Sighting detail page (`/sightings/:id`)

- Everything is inside one `app-page-card` (28rem): a `dl[app-detail-list]` in the default `column` layout with seven `app-detail-item` rows (status, criticality, description, behaviour, reporter, reported at, location), then a Show/Hide map button and an `#sighting-detail-map` container whose `app-map-view` is created only after the toggle (`sighting-detail-page.html:1-69`).
- `app-detail-list` supports `layout` `column` or `inline` (flex-wrap row) (`shared/ui/detail-list/detail-list.scss:1-11`). `inline` is already used in the map home's selected card (`nearby-map-page.html`, `<dl app-detail-list layout="inline">`). Description and behaviour use `value="text"` (`white-space: pre-line`), so they need reading width rather than a one-line cell.
- The map exists only behind the toggle by design (cost guardrail, `Frontend/CLAUDE.md`, "Maps and geolocation"), so any side-by-side layout must keep the map out of the DOM until shown.
- `MapView` has no explicit resize handling: a grep for `invalidate|resize|observer` (case-insensitive) over `shared/map/map-view/map-view.ts` finds nothing, and the only `size` matches are `iconSize` (`map-view.ts:201`, `map-view.ts:269`). The earlier change `nearby-map-ui-contract` deferred this as D5, to be revisited when layout work changes the map container size (`context/archive/2026-10-11-nearby-map-ui-contract/research.md:92`). Inference, not observed in a browser: Leaflet's own window-resize tracking may cover plain window resizing, but a container whose width changes without a window resize may not be covered. A layout whose map is created after the layout settles avoids the question.

### Login and register

- Both are `app-page-card` forms in the centred guest shell with a few fields each, and no content competes for horizontal space; widening the card would only stretch inputs.
- Inference (design, not a code fact): a narrow centred card is the usual form pattern, and no inspected document asks for wider auth pages. This stays a product choice under Open Questions, but the evidence favours leaving them at 28rem. Profile and Report are single-column forms in the same category; the Report page's map picker (`shared/map/location-picker`) is the one element there that could use width.

### Style-contract constraints that apply

- Raw lengths are allowed only in `_tokens.scss`, `_material-theme.scss` and `_breakpoints.scss`; component styles use `var(--pw-*)`, enforced by `style-token-coverage.spec.ts` (`Frontend/CLAUDE.md`, "Design tokens" and "Component stylesheets"). A new width or map height needs a token; a new breakpoint needs an entry in `$breakpoints` (`_breakpoints.scss:3-8`).
- Route width must use the route flag, not a page-level override (`Frontend/CLAUDE.md`, "Route width"). A third width tier means extending the flag's value set (today only the literal `'wide'` is read, `app.ts:57-63`), not a per-page `max-width`.
- Initial-bundle code (`app.ts`, `core/`) imports `shared/ui` by file path; the initial bundle was last measured at 486.2 kB against a 500 kB budget (`Frontend/CLAUDE.md`). Shell changes are small, but the build budget should be re-checked.
- The kitchen sink is the visual gate; its route declares the wide layout and its wrapper caps itself at 40rem (`app/dev/kitchen-sink/kitchen-sink.scss:12`, `app/dev/dev-routes.ts:6`), so cells meant to show large-screen behaviour need that wrapper reviewed.

## Code References

- `PigeonWatch/Frontend/src/app/app.scss:22-32` - `.app-main` and `.app-main--wide` caps.
- `PigeonWatch/Frontend/src/app/app.ts:40-48,57-63` - `wide` signal and `layoutOf` (reads only `'wide'`).
- `PigeonWatch/Frontend/src/app/app.html:5` - header `pw-container`.
- `PigeonWatch/Frontend/src/app/app.routes.ts:6-12` - only the home route sets `layout: 'wide'`.
- `PigeonWatch/Frontend/src/app/app.spec.ts:109-132` - specs pinning wide to the home route.
- `PigeonWatch/Frontend/src/styles/_tokens.scss:70-79` - width, form width, map height and aside width tokens.
- `PigeonWatch/Frontend/src/styles/_common.scss:1-5` - `pw-container` 40rem cap.
- `PigeonWatch/Frontend/src/styles/_breakpoints.scss:3-8` - `sm`, `md`, `lg`, `xl`.
- `PigeonWatch/Frontend/src/app/shared/ui/page-card/page-card.scss:3-10` - 28rem page-card cap.
- `PigeonWatch/Frontend/src/app/shared/ui/detail-list/detail-list.scss:1-11` - column and inline layouts.
- `PigeonWatch/Frontend/src/app/shared/map/map-view/map-view.scss:5-7` - fixed map height.
- `PigeonWatch/Frontend/src/app/features/sightings/nearby-map-page/nearby-map-page.scss:36-43` - map plus aside grid from `lg`.
- `PigeonWatch/Frontend/src/app/features/sightings/sighting-detail-page/sighting-detail-page.html:1-69` - detail page markup.
- `PigeonWatch/Frontend/src/app/features/sightings/my-sightings/my-sightings.scss:1-3` - uncapped card in a 40rem column.
- `PigeonWatch/Frontend/src/app/core/warmup/warmup-panel/warmup-panel.scss:12-14` - separate 28rem overlay cap.
- `PigeonWatch/Frontend/src/app/dev/kitchen-sink/kitchen-sink.scss:12` - sink wrapper 40rem cap.

## Architecture Insights

- The design system already has the mechanism (route flag plus width tokens), but only one tier above the default, and `app-page-card`'s cap is not connected to it. Large-screen support is mostly a decision about tiers and which route gets which, not a refactor.
- A reasonable shape, as a recommendation to settle in planning: keep forms narrow (28rem card in the 40rem shell), keep `wide` for today's 64rem, and add a larger tier from `xl` for the map home and the detail page. The shell would read the flag's value instead of a boolean, and the header would follow the main width so it does not sit visibly narrower than the content.
- Keeping `app-page-card` at 28rem for forms while the detail page gets a different container avoids changing five consumers at once. The detail page could stop using `app-page-card` for its body, or the card could gain a width input, which the "variant is a component input" rule favours.

## Historical Context (from prior changes)

- `context/archive/2026-10-11-nearby-map-ui-contract/plan.md:20,30,50,395` - introduced the `wide` route layout (64rem from `md`, aside beside the map from `lg`); left the header and other routes narrow, deferring app-shell navigation and width (research D3). Supported by the cited lines.
- `context/archive/2026-10-11-nearby-map-ui-contract/research.md:92,160` - D5 (no `invalidateSize`; revisit if layout work changes the map container) and the map-width product choice "deferred twice already". The two deferrals are the report-sighting plan (`context/archive/2026-10-11-report-sighting-on-map/plan.md:42`, "A full-width map layout", read in this research) and the nearby-map plan (`plan.md:50`); supported.
- `context/archive/2026-10-11-report-sighting-on-map/visual-notes.md:7,33` - a 1280 px window showed the same layout as 800 px, and the 28rem/40rem column mismatch (V2). The first claim was true of that commit's code; it is still true at 1280 px for every route except the map home. V2 still holds (above).
- `context/archive/2026-10-11-profile-ui-contract/visual-notes.md:47` - the card did not change above `sm`; still true of `app-page-card` today (`page-card.scss` has one `sm` rule for padding and title size only).
- `context/archive/2026-10-18-sighting-detail-ui-contract/plan-brief.md:36,58` - that change kept the detail page at 28rem and left width work to this change. It is archived, so its boundary no longer blocks edits to the detail page.

## Related Research

- `context/archive/2026-10-11-nearby-map-ui-contract/research.md` - wide layout origin and deferrals.

## Open Questions

Product choices for `/10x-frame` or `/10x-plan`:

1. **Which routes get which tier.** Evidence supports: login, register, profile and report stay narrow; map home and sighting detail are the routes that can use width. Confirm this split.
2. **Detail page shape at large widths.** Options: two columns (details beside the map when shown), a wider single card with the inline detail list, or the current card centred. The map toggle (cost guardrail) must stay.
3. **Map home at large widths.** Raise the 64rem cap and make the map taller (a height token that grows with the viewport), or only raise the cap.
4. **Header width.** Follow the main width on wide routes, or stay narrow as recorded in `Frontend/CLAUDE.md`.
5. **My sightings card on the profile page.** Cap it to the page-card width, or leave it at the shell width (the existing V2 mismatch).
6. **Delivery.** One change for the shell plus both routes, or a shell-tier change first and then one `/10x-ui` change per view.

Not verified: rendered behaviour from 1280 to 2000 px in a browser (this research read source only), and map resize behaviour in a changing container.
