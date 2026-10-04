---
project: PigeonWatch
version: 1
status: draft
created: 2026-10-03
updated: 2026-10-04
prd_version: 1
main_goal: speed
top_blocker: decisions
milestone_id: rescue-coordination-mvp
milestone_seq: 1
milestone_status: open
---

# Roadmap: PigeonWatch

> Derived from `context/foundation/prd.md` (v1) + auto-researched codebase baseline.
> Edit-in-place; archive when superseded.
> Slices below are listed in dependency order. The "At a glance" table is the index.

## Milestone

**M-01: Rescue coordination MVP** — Status: open

- **Intent:** A logged-in rescuer can report a distressed pigeon, find sightings by location and urgency, discuss them with other members, and an admin can moderate. No dates; the milestone closes when its items are done.
- **Source materials:** `context/foundation/prd.md` (v1)
- **Done when:** every F-NN and S-NN below is `done`.
- **Scope anchors:** all `must-have` FRs (FR-001, FR-002, FR-004, FR-005, FR-006, FR-009, FR-012, FR-013, FR-017, FR-018, FR-019, FR-020) and US-01. The `nice-to-have` FRs are parked (see `## Parked`).

## Vision recap

Active members of the city pigeon/bird rescue community coordinate sightings of distressed pigeons in Facebook groups, where posts get buried, duplicate reports pile up, and nothing records a bird's status once someone takes over its care. PigeonWatch is a shared, mappable, status-tracked record built around the rescue lifecycle (spotted, contacted, taken to vet, healed/returned), scoped to the viewer's nearby area.

## North star

**S-03: User can log in, report a distressed-pigeon sighting, and see it on the map and in its detail page** — it is the only written user story (US-01) and the first primary success criterion, so it is the flow that proves the product works for the `speed` goal.

> "North star" here means the smallest end-to-end slice whose successful delivery would prove the product works. It is placed as early as Prerequisites allow, because everything else only matters if this works.

## At a glance

| ID   | Change ID                   | Outcome (user can …)                                                        | Prerequisites | PRD refs                       | Status   |
| ---- | --------------------------- | --------------------------------------------------------------------------- | ------------- | ------------------------------ | -------- |
| F-01 | persistence-wiring-smoke    | (foundation) deployed API reaches the provisioned database through a CI-applied migration | —             | NFR (map and list usable at expected record volumes) | done |
| S-01 | register-and-login          | register an account, log in and log out                                     | F-01          | FR-001                         | in-progress |
| S-02 | edit-profile-name           | view and edit their own profile name                                        | S-01          | FR-002                         | proposed |
| S-03 | report-sighting-on-map      | report a sighting and see it on the nearby map and its detail page          | S-01          | US-01, FR-004, FR-005, FR-019  | blocked  |
| S-04 | attach-sighting-photo       | attach an optional photo to a sighting                                      | S-03          | FR-005                         | proposed |
| S-05 | filter-sightings-nearby     | scope the map to their nearby area and filter by status and criticality     | S-03          | FR-004                         | proposed |
| S-06 | urgency-ranked-sighting-list | browse nearby sightings as a list ordered by urgency                       | S-03          | FR-020                         | proposed |
| S-07 | comment-on-sighting         | add comments to a sighting                                                  | S-03          | FR-009, FR-019                 | proposed |
| S-08 | sighting-message-threads    | contact a sighting's owner and continue the conversation in a per-sighting thread | S-03    | FR-012, FR-013                 | proposed |
| S-09 | edit-own-sighting-with-lock | edit their own sighting, with location and description locked once others engage | S-07, S-08 | FR-006                    | proposed |
| S-10 | admin-remove-sightings      | (as admin) remove sightings                                                 | S-03          | FR-017                         | proposed |
| S-11 | admin-moderate-users        | (as admin) message, warn or ban users                                       | S-08, S-10    | FR-018                         | proposed |
| S-12 | switch-ui-language          | switch the UI language between English and Polish, remembered in the browser | S-01         | — (post-PRD, see Q6)           | proposed |

## Streams

Navigation aid — groups items that share a Prerequisites chain. Canonical ordering still lives in the dependency graph below; this table is the proposed reading order across parallel tracks.

| Stream | Theme                    | Chain                                              | Note                                                                                   |
| ------ | ------------------------ | -------------------------------------------------- | -------------------------------------------------------------------------------------- |
| A      | Report and browse        | `F-01` → `S-01` → `S-03` → `S-04` → `S-05` → `S-06` | Strict must-have path first for `speed`; `S-04`, `S-05`, `S-06` are parallel after `S-03`. |
| B      | Talk about a sighting    | `S-07` → `S-08` → `S-09`                            | Joins Stream A at `S-03`; `S-09` needs the engagement signals from `S-07` and `S-08`.   |
| C      | Moderation               | `S-10` → `S-11`                                     | Joins Stream A at `S-03`; `S-11` also joins Stream B at `S-08`.                         |
| D      | Profile                  | `S-02`                                              | Joins Stream A at `S-01`; independent of everything after login.                        |
| E      | Localization             | `S-12`                                              | Joins Stream A at `S-01` (resource management lands there); independent of later slices. |

## Baseline

What's already in place in the codebase as of `2026-10-03` (auto-researched + user-confirmed).
Foundations below assume these are present and do NOT re-scaffold them.

- **Frontend:** partial — Angular scaffold with empty routes, a connectivity check page, no map library, no real pages (`PigeonWatch/Frontend/src/app/app.routes.ts`).
- **Backend / API:** partial — ASP.NET Core template with one sample controller, CORS and Swagger (`PigeonWatch/Api/Program.cs`).
- **Data:** absent — no ORM or driver, no data context or migrations; the cloud database and blob storage are provisioned but not wired to the API (`PigeonWatch/Api/PigeonWatchApi.csproj`, `context/deployment/deploy-plan.md`).
- **Auth:** absent — no identity or token packages, no protected endpoints, no login UI.
- **Deploy / infra:** present — API and frontend deploy workflows on push to main, hosting live and smoke-tested (`.github/workflows/`, `context/deployment/deploy-plan.md`).
- **Observability:** absent — no logging library, telemetry or health endpoint. No PRD requirement calls for it, so no item is opened for it.

## Foundations

### F-01: Persistence wiring smoke test

- **Outcome:** (foundation) the deployed API connects to the already-provisioned database and applies a schema migration through CI, proving a record can be stored and read in the live environment.
- **Change ID:** persistence-wiring-smoke
- **PRD refs:** NFR (map and list usable at expected record volumes)
- **Unlocks:** `S-01` (first slice that stores user records; without a proven live path its verification would fail on infrastructure instead of behaviour).
- **Prerequisites:** —
- **Parallel with:** —
- **Blockers:** —
- **Unknowns:** —
- **Risk:** Scoped to the single connection-and-migration path only; the actual schema grows inside each slice that needs it. Sequenced first because the free hosting and database tiers are the likeliest source of surprises.
- **Status:** done

## Slices

### S-01: Register and log in

- **Outcome:** user can register an account, log in and log out.
- **Change ID:** register-and-login
- **PRD refs:** FR-001
- **Prerequisites:** F-01
- **Parallel with:** —
- **Blockers:** —
- **Unknowns:**
  - Which login mechanism (email/password or OAuth) — Owner: user. Block: no.
- **Risk:** Login is a wall in front of every must-have, so it goes first after the foundation.
- **Status:** in-progress

### S-02: Edit profile name

- **Outcome:** user can view and edit their own profile name.
- **Change ID:** edit-profile-name
- **PRD refs:** FR-002
- **Prerequisites:** S-01
- **Parallel with:** S-03, S-04, S-05, S-06, S-07, S-08, S-09, S-10, S-11
- **Blockers:** —
- **Unknowns:** —
- **Risk:** Small and independent; can fill gaps while other slices are waiting on a decision.
- **Status:** proposed

### S-03: Report a sighting and see it on the map

- **Outcome:** user can log in, report a sighting (location, description, behaviour, status, criticality), see it plotted on the nearby map and on their own profile, and open its detail page.
- **Change ID:** report-sighting-on-map
- **PRD refs:** US-01, FR-004, FR-005, FR-019
- **Prerequisites:** S-01
- **Parallel with:** S-02
- **Blockers:** —
- **Unknowns:**
  - What are the exact status names and transitions of a sighting's rescue workflow? — Owner: user. Block: yes.
  - What are the criticality levels (names and count)? — Owner: user. Block: yes.
- **Risk:** Carries the first map integration, the cost-conscious map-provider guardrail, and the phone-usable reporting requirement; blocked until the status and criticality values exist because both are required fields.
- **Status:** blocked

### S-04: Attach a sighting photo

- **Outcome:** user can attach an optional photo to a sighting they report.
- **Change ID:** attach-sighting-photo
- **PRD refs:** FR-005
- **Prerequisites:** S-03
- **Parallel with:** S-02, S-05, S-06, S-07, S-08, S-10
- **Blockers:** —
- **Unknowns:** —
- **Risk:** First use of file storage, and the photo is optional, so reporting works without it.
- **Status:** proposed

### S-05: Filter sightings by nearby area, status and criticality

- **Outcome:** user can see the map scoped to their nearby area by default and filter sightings by status and criticality.
- **Change ID:** filter-sightings-nearby
- **PRD refs:** FR-004
- **Prerequisites:** S-03
- **Parallel with:** S-02, S-04, S-06, S-07, S-08, S-10
- **Blockers:** —
- **Unknowns:**
  - What defines "nearby area/city" (radius from the user's location vs. named city)? — Owner: user. Block: no.
- **Risk:** The definition of "nearby" is also needed by `S-06`; settling it once avoids two different rules.
- **Status:** proposed

### S-06: Urgency-ranked sighting list

- **Outcome:** user can browse nearby sightings as a list ordered by urgency, with the same filters as the map, resolved sightings hidden unless requested.
- **Change ID:** urgency-ranked-sighting-list
- **PRD refs:** FR-020
- **Prerequisites:** S-03
- **Parallel with:** S-02, S-04, S-05, S-07, S-08, S-10
- **Blockers:** —
- **Unknowns:**
  - How are criticality and time-since-reported weighted into the urgency order? — Owner: user. Block: no.
- **Risk:** Carries the PRD's business rule (urgency ranking); resolved sightings only appear once owners can change status in `S-09`, so the first version ranks on the reported status.
- **Status:** proposed

### S-07: Comment on a sighting

- **Outcome:** user can add comments to a sighting, subject to the submission cooldown.
- **Change ID:** comment-on-sighting
- **PRD refs:** FR-009, FR-019
- **Prerequisites:** S-03
- **Parallel with:** S-02, S-04, S-05, S-06, S-08, S-10
- **Blockers:** —
- **Unknowns:** —
- **Risk:** Open comments before moderation tools exist; the cooldown is the only spam guard until `S-10` and `S-11` land.
- **Status:** proposed

### S-08: Contact a sighting's owner and hold a thread

- **Outcome:** user can contact a sighting's owner and read and reply to plain-text messages grouped by sighting, without ever seeing the other person's contact details.
- **Change ID:** sighting-message-threads
- **PRD refs:** FR-012, FR-013
- **Prerequisites:** S-03
- **Parallel with:** S-02, S-04, S-05, S-06, S-07, S-10
- **Blockers:** —
- **Unknowns:** —
- **Risk:** Contact-info privacy (a PRD NFR) must hold on every surface; no read/unread state and no live delivery, per the PRD.
- **Status:** proposed

### S-09: Edit own sighting with engagement lock

- **Outcome:** user can edit their own sighting and change its status, and location and description lock once another user has commented or made contact.
- **Change ID:** edit-own-sighting-with-lock
- **PRD refs:** FR-006
- **Prerequisites:** S-07, S-08
- **Parallel with:** S-02, S-04, S-05, S-06, S-10
- **Blockers:** —
- **Unknowns:** —
- **Risk:** Sequenced after comments and messaging because those are the engagement signals that trigger the lock.
- **Status:** proposed

### S-10: Admin removes sightings

- **Outcome:** as the admin, user can remove a sighting from the map and lists.
- **Change ID:** admin-remove-sightings
- **PRD refs:** FR-017
- **Prerequisites:** S-03
- **Parallel with:** S-02, S-04, S-05, S-06, S-07, S-08, S-09
- **Blockers:** —
- **Unknowns:**
  - How is the single admin role assigned? — Owner: user. Block: no.
- **Risk:** First introduction of the admin role; later admin slices reuse it.
- **Status:** proposed

### S-11: Admin moderates users

- **Outcome:** as the admin, user can message, warn or ban another user.
- **Change ID:** admin-moderate-users
- **PRD refs:** FR-018
- **Prerequisites:** S-08, S-10
- **Parallel with:** S-02, S-04, S-05, S-06, S-07, S-09
- **Blockers:** —
- **Unknowns:**
  - What do a warning and a ban do to the affected user, and how do they see it? — Owner: user. Block: no.
- **Risk:** Admin messaging is the one permitted exception to the "contact only through a sighting" rule in `S-08`.
- **Status:** proposed

### S-12: Switch UI language

- **Outcome:** user can switch the UI language between English and Polish; the choice is stored in the browser (`localStorage`, key `pigeonwatch.culture`) and applied on every later visit, falling back to English when nothing is stored.
- **Change ID:** switch-ui-language
- **PRD refs:** — (requirement added after PRD v1 on 2026-10-04; see Open Roadmap Question 6)
- **Prerequisites:** S-01 (introduces resource management: every UI label is a key in `UiLabels.resx`, served by `GET /resources/{culture}`, bundled as per-culture snapshots, with saved-culture selection and EN fallback already implemented)
- **Parallel with:** S-02, S-03, S-04, S-05, S-06, S-07, S-08, S-09, S-10, S-11
- **Blockers:** —
- **Unknowns:**
  - Is the language switcher available before login (on the login and register pages) or only for logged-in users? — Owner: user. Block: no.
  - Does S-12 translate every label existing at that point, and must each later slice add Polish values for its own keys? — Owner: user. Block: no.
- **Risk:** Mostly content work (`UiLabels.pl.resx` plus a switcher calling `CultureStore.save`); the longer it waits, the more keys need translating at once.
- **Status:** proposed

## Backlog Handoff

| Roadmap ID | Change ID                    | Suggested issue title                                  | Ready for `/10x-plan` | Notes                                                    |
| ---------- | ---------------------------- | ------------------------------------------------------ | --------------------- | -------------------------------------------------------- |
| F-01       | persistence-wiring-smoke     | Prove live API-to-database path with a CI migration    | yes                   | Run `/10x-plan persistence-wiring-smoke`                 |
| S-01       | register-and-login           | Register, log in and log out                           | no                    | Needs F-01 done                                          |
| S-02       | edit-profile-name            | Edit own profile name                                  | no                    | Needs S-01 done                                          |
| S-03       | report-sighting-on-map       | Report a sighting and see it on the map                | no                    | Needs S-01 done; resolve status names and criticality levels |
| S-04       | attach-sighting-photo        | Attach optional photo to a sighting                    | no                    | Needs S-03 done                                          |
| S-05       | filter-sightings-nearby      | Scope map to nearby area and filter by status/criticality | no                 | Needs S-03 done                                          |
| S-06       | urgency-ranked-sighting-list | Urgency-ranked list of nearby sightings                | no                    | Needs S-03 done                                          |
| S-07       | comment-on-sighting          | Comment on a sighting                                  | no                    | Needs S-03 done                                          |
| S-08       | sighting-message-threads     | Per-sighting message threads                           | no                    | Needs S-03 done                                          |
| S-09       | edit-own-sighting-with-lock  | Edit own sighting with engagement lock                 | no                    | Needs S-07 and S-08 done                                 |
| S-10       | admin-remove-sightings       | Admin removes sightings                                | no                    | Needs S-03 done                                          |
| S-11       | admin-moderate-users         | Admin message, warn, ban users                         | no                    | Needs S-08 and S-10 done                                 |
| S-12       | switch-ui-language           | Switch UI language EN/PL, remembered in the browser    | no                    | Needs S-01 done                                          |

## Open Roadmap Questions

1. **What are the exact status names and the transition graph for a sighting's rescue workflow (e.g. Reported → Contacted → Taken to vet → Healed/Returned, plus any closed/unable-to-help end state)?** — Owner: user, pending consultation. Block: S-03 (and everything that depends on it).
2. **What specific latency/performance target, if any, applies to the map and sighting list?** — Owner: user. Block: none (not blocking for MVP).
3. **What access rule should govern anonymous/unregistered reporting, if it's built post-MVP?** — Owner: user. Block: none (deferred with the Secondary persona).
4. **What are the criticality levels a reporter can choose (names and count)?** — Owner: user. Block: S-03.
5. **The PRD's second primary success criterion (flock locations as a map layer) is covered only by nice-to-have FRs, which this milestone parks. Is that still the intent?** — Owner: user. Block: none (affects whether a later milestone is needed).
6. **S-12 (EN/PL language switch) has no FR in PRD v1. Should the next PRD revision add it (and its priority), or does it stay a roadmap-only addition?** — Owner: user. Block: none.

## Parked

- **FR-003: View flock locations on the map** — Why parked: nice-to-have; `speed` goal keeps the milestone to must-haves. See Open Roadmap Question 5.
- **FR-011: Submit a flock location pending admin approval** — Why parked: nice-to-have, depends on the flock feature.
- **FR-016: Admin accepts or rejects flock requests** — Why parked: nice-to-have, tied to FR-011.
- **FR-007: "I've seen this bird" counter** — Why parked: nice-to-have; PRD notes it encourages noise.
- **FR-008: Sighting change-log/history** — Why parked: nice-to-have and a PRD Non-Goal (current state only).
- **FR-010: Duplicate-sighting nudge (proximity + time window)** — Why parked: nice-to-have.
- **FR-015: Admin statistics panel** — Why parked: nice-to-have; MVP admin panel is moderation only.
- **Anonymous/unregistered reporting** — Why parked: PRD Non-Goal; only logged-in members can act.
- **Real-time/live messaging** — Why parked: PRD Non-Goal; dropped, not deferred.
- **A single worldwide unranked feed** — Why parked: PRD Non-Goal; map and list are always nearby-scoped.

## Milestone History

## Done
- **F-01: (foundation) the deployed API connects to the already-provisioned database and applies a schema migration through CI, proving a record can be stored and read in the live environment.** — Archived 2026-10-04 → `context/archive/2026-10-03-persistence-wiring-smoke/`. Lesson: —.
