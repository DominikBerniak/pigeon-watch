# Sighting Message Threads (S-08) — Plan Brief

> Full plan: `context/changes/sighting-message-threads/plan.md`
> Research: `context/changes/sighting-message-threads/research.md`

## What & Why

Rescuers need to reach the person who reported a sighting without exchanging emails or phone numbers in public. FR-012/FR-013 ask for contact through a sighting, kept as a simple per-sighting thread with plain-text replies, and the PRD forbids exposing anyone's contact details.

## Starting Point

No messaging exists. S-07 comments provides every layer to copy (controller, service, rules, client configuration, SPA failure mapping, privacy tests). The API never returns another user's id or email, but the SPA cannot yet tell whether a sighting is the caller's own.

## Desired End State

Bob opens Alice's sighting, contacts her, and sees his private thread there. Alice finds it on a new Messages page (header user menu), opens it on her sighting and replies. Carol cannot see or post to that thread. Other users appear only by display name, proven by the privacy sweep and a contract test.

## Key Decisions Made

| Decision | Choice | Why (1 sentence) | Source |
| --- | --- | --- | --- |
| Thread model | One thread per (sighting, contacting user) | Private two-person contact matches FR-012 and gives S-09 a simple "contact made" check. | Plan |
| Who starts | Only non-owners; owner replies | Recipient is always derived from the sighting, so no user is ever addressed directly. | Plan |
| Cooldown | None | FR-019 names sightings and comments only; moderation arrives with S-10/S-11. | Plan |
| Length | 1000 characters | Room to arrange a handover. | Plan |
| Edit/delete | Neither, append-only | Smallest scope and a moderation trail; test-plan §7 records message text as deliberate self-disclosure. | Plan |
| Entry point | `/messages` page from the user menu; thread opens on the sighting page | One inbox with a single shared thread view; only a menu item touches the initial bundle. | Plan |
| Order/paging | Newest first, 20 per page, "Show older" | Same as comments. | Plan |
| Outsider access | 404, not 403 | Thread ids cannot be probed. | Plan |
| Owner signal | `isMine` on `SightingModel` | Lets the SPA pick the owner view without exposing the reporter id. | Research |
| Labels | English only | S-12 default. | Research |

## Scope

**In scope:** two tables and migration; five endpoints (start, list per sighting, list mine, list messages, reply); `isMine` on sightings; `messageRules` in client configuration; detail-page messages panel; `/messages` page; privacy fixtures and contract tests; one two-user Playwright spec; docs updates.

**Out of scope:** read/unread, notifications, live delivery; edit/delete; cooldown; owner-initiated threads; admin messaging (S-11) and removal (S-10); engagement lock (S-09); Polish labels.

## Architecture / Approach

`SIGHTING_THREAD` (sighting, participant, last message time; unique per pair) and `SIGHTING_MESSAGE` (thread, sender, text). The owner is always the sighting's reporter. Every thread read and write is scoped to the participant or the owner; anyone else gets 404. API models carry display names plus `isMine`/`isOwner` flags computed on the server. The SPA adds a child panel to the sighting detail page and a lazy inbox page that deep-links to `/sightings/:id?thread=<id>`.

## Phases at a Glance

| Phase | What it delivers | Key risk |
| --- | --- | --- |
| 1. Data model | Entities, migration, scoped repository + tests | Unique-pair race on first contact |
| 2. Messaging API | Service, two controllers, `isMine`, `messageRules` | Five sighting actions change signature for `isMine` |
| 3. Privacy contract | Fixtures, sweep seed, Alice/Bob/Carol tests, §7 note | Missing fixture or id leak |
| 4. Detail panel | Contact box, owner thread list, thread view | Owner vs non-owner UI branching |
| 5. Inbox page | `/messages` + menu item | Initial bundle (6.5 kB headroom) |
| 6. E2E + docs | Two-context Playwright spec, CLAUDE.md updates | First two-user E2E in the suite |

**Prerequisites:** S-03 done; API on LocalDB for E2E.
**Estimated effort:** ~4–6 sessions across 6 phases.

## Open Risks & Assumptions

- No cooldown means a user could flood one owner until S-10/S-11 moderation exists.
- An owner with many contacts sees a paged list on the sighting page; assumed low volume for MVP.
- S-09 will treat "a thread exists on the sighting" as "contact made".

## Success Criteria (Summary)

- A non-owner can contact the owner and both can hold a private conversation from the sighting page and `/messages`.
- A third user can neither see nor post to that conversation.
- No response exposes another user's id or email; all suites, including the new E2E, pass.
