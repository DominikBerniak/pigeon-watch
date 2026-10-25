---
change_id: app-wide-large-screen-layout
title: App-wide layout for large screens
status: archived
created: 2026-10-18
updated: 2026-10-25
archived_at: 2026-10-25T15:50:32Z
---

## Notes

Make the whole app work on larger screens. Raised during the manual check of `sighting-detail-ui-contract` Phase 2 (2026-10-18): at about 2000 px wide the sighting detail page is a 28rem card (`--pw-form-max-width`) centred in a mostly empty page, and the content should use the available space.

- **Evidence:** screenshots of `/sightings/:id` on a large screen, map hidden and shown, taken by the user during the Phase 2 manual check.
- **Known mechanics (from `PigeonWatch/Frontend/CLAUDE.md`):** the shell caps `<main>` at `--pw-content-max-width` (40rem); a route can opt into `data: { layout: 'wide' }` (64rem from `md`, today only the map home); `app-page-card` is capped at `--pw-form-max-width` (28rem) for every card page (login, register, profile, report, detail). The header keeps the narrow width.
- **Scope question for research or frame:** this is app-wide, while `/10x-ui` normally takes one view per change. Decide which views and which shared pieces (shell width, `app-page-card`, `app-detail-list`) change, and whether login and register should stay narrow.
- **Boundary with `sighting-detail-ui-contract`:** that change keeps the detail page at its current 28rem width and does not touch shell or page-card width; this change owns all width work.
