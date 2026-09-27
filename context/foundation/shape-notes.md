---
project: "PigeonWatch"
context_type: greenfield
product_type: web-app
target_scale:
  users: medium
  qps: low
  data_volume: small
created: 2026-09-26
updated: 2026-09-26
timeline_budget:
  mvp_weeks: 3
  hard_deadline: 2026-12-06
  after_hours_only: false                  # mostly after-hours/weekends, with some working-hours time available too
checkpoint:
  current_phase: 8
  phases_completed: [1, 2, 3, 4, 5, 6, 7]
  gray_areas_resolved:
    - topic: "pain category"
      decision: "coordination overhead + data trapped in Facebook posts + missing capability (no map) + workflow friction, all present"
    - topic: "insight"
      decision: "city pigeon welfare is a newly-forming community; a shared, status-tracked, mappable record fits this specific niche better than a generic lost-pet/classifieds tool"
    - topic: "primary persona scope"
      decision: "MVP targets active bird-rescue community members; open/anonymous public reporting is a later stretch goal, not MVP-blocking"
    - topic: "auth strategy"
      decision: "login-based (email/password or OAuth); no local-only or keyless access"
    - topic: "role model"
      decision: "flat permissions for all logged-in members, plus a single admin role for moderation"
    - topic: "map scope"
      decision: "herds (general flock/congregation locations) are a real second map layer, distinct from individual distress sightings; MVP timeline still 3 weeks"
    - topic: "MVP timeline"
      decision: "3 weeks after-hours confirmed achievable by user for the described flow"
    - topic: "business logic"
      decision: "ordered non-linear status workflow + urgency ranking from criticality and time-since-reported; exact status names/transition graph deferred to Open Questions"
    - topic: "non-functional requirements"
      decision: "contact-info privacy, minimal map-API usage, mobile-usable reporting flow, fast perceived response, resolved sightings hidden from default view but not deleted"
    - topic: "product framing"
      decision: "web-app; medium scale (dozens to a hundred users initially); hard deadline 2026-12-06; mostly after-hours/weekends with some working-hours time available"
    - topic: "geo-scoping (surfaced during scale probe)"
      decision: "product intended for worldwide use, so map/list/urgency ranking are scoped to viewer's nearby area/city by default, not a global feed"
    - topic: "non-goals"
      decision: "anonymous reporting, real-time messaging, worldwide unranked feed, and full audit history are all explicitly out of MVP scope"
  frs_drafted: 19
  quality_check_status: accepted
---

## Vision & Problem Statement

Active members of the city pigeon/bird rescue community currently coordinate sightings of distressed pigeons (tied feet, visible injury, other conditions needing attention) through Facebook groups. Posts get buried in the feed, duplicate reports happen for the same bird, there is no way to see at a glance what sightings are nearby or already being handled, and there is no persistent record of a bird's status once someone picks up its care.

City pigeon welfare is a community that has only recently started organizing itself. A shared, mappable, status-tracked record — with a status lifecycle specific to bird rescue (spotted, contacted, taken to vet, healed/returned) — serves this community's actual coordination need in a way that a generic lost-pet board or classifieds app does not.

## User & Persona

**Primary persona**: An active member of the city pigeon/bird rescue community — someone who already watches Facebook groups for sightings, wants to log a sighting or pick one up to coordinate care, and track what happens to that bird over time (contacted, taken to vet, healed and returned, etc.).

**Scale note**: the product is intended to work worldwide, not one city — so the map/list must scope sightings to a user's nearby area or city by default, rather than showing a single global unranked feed. This surfaced during scale discussion in Phase 6 and affects FR-004/FR-004b and the urgency rule below.

### Secondary persona

A member of the general public who spots a distressed pigeon and wants to report it without joining the rescue community — potentially anonymously, but reachable (e.g. via an email address) so a rescuer can follow up. This is a stretch goal beyond the MVP, not a Phase 1 requirement.

## Access Control

Login required (email/password or OAuth — mechanism is a downstream stack decision). All logged-in members share the same flat permission set: report a sighting, comment on any sighting, contact the reporter/registrant of a sighting, and update status on records they own. A single admin role exists for moderation (e.g. removing bad-faith or duplicate reports). Unauthenticated visitors cannot view or act on sightings in the MVP — the anonymous-reporting flow described under Secondary persona is deferred and, if built, would need its own narrower access rule (see Open Questions).

## Success Criteria

### Primary
- A rescuer can log a distressed-bird sighting (description, photo, criticality), see it and other members' sightings plotted on a map, and open one through to its full detail page.
- A rescuer can see confirmed pigeon-herd (flock/congregation) locations as a separate map layer from individual distress sightings.
- Users can contact each other about a sighting via a basic (non-real-time) message inbox.

### Secondary
- When adding a sighting, the system nudges the user if a similar sighting (by location + description) may already be registered, to reduce duplicates.
- Messaging becomes real-time/live (beyond a basic inbox).

### Guardrails
- A user's contact info (email, etc.) is never exposed directly to other users; contact happens through the product, not by revealing raw contact details.
- Map-provider API usage is cost-conscious by design — e.g. the bird detail page does not render an embedded interactive map by default.
- A cooldown period between a user's sighting submissions exists to deter spam/flooding of the map with junk records.

## Functional Requirements

### Authentication & Profile
- FR-001: User can register and log in. Priority: must-have
  > Socrates: Counter-argument considered: "requiring an account before browsing the map adds friction for a first-time reporter who just wants to flag one bird." Resolution: kept as written — login-wall stays for MVP simplicity.
- FR-002: User can view and edit their own profile (name only for MVP). Priority: must-have
  > Socrates: Counter-argument considered: "a full profile page (name+description+photo) is over-scoped for v1." Resolution: revised — MVP captures name only; description/photo deferred (see Non-Goals).

### Map & Browsing
- FR-003: User can view pigeon flock (congregation) locations on the map. Priority: nice-to-have
  > Socrates: Counter-argument considered: "flocks are a real second data model + admin-approval workflow + separate map layer on top of an already full MVP." Resolution: demoted to nice-to-have; MVP focuses on individual distress sightings only.
- FR-004: User can view individual distressed-pigeon sightings on the map, scoped to their nearby area/city by default, filterable by status and criticality. Priority: must-have
  > Socrates: Counter-argument considered: "two filter dimensions may be premature with few records at launch." Resolution: kept as written — both filters are cheap to build together and useful as soon as there's more than a handful of records.
- FR-004b: User can view sightings as a list (as an alternative to the map), scoped to their nearby area/city by default, ordered by urgency, respecting the same status/criticality filters as the map view; resolved sightings are excluded by default but can be included on request. Priority: must-have
  > Socrates: Counter-argument considered: "the map already lets users browse and filter — a second full list view might be redundant effort before either is validated with real users." Resolution: kept as written — working through sightings in urgency order is a distinct task from browsing a map, and matters especially on mobile.

### Sightings
- FR-005: User can add a new sighting with name (optional), appearance description, behaviour, location, status, and criticality; photo is optional but encouraged. Priority: must-have
  > Socrates: Counter-argument considered: "requiring a photo blocks reporting when the bird is in a large flock (hard to photograph a specific bird) or the reporter is just passing by without a chance to take one." Resolution: revised — photo is now optional.
- FR-006: User can edit their own sightings; once another user has commented on or contacted the reporter about a sighting, its location and description become locked (status remains freely editable by the owner). Priority: must-have
  > Socrates: Counter-argument considered: "unrestricted editing could silently change details an in-progress rescue is already relying on, wasting a trip or losing the bird." Resolution: revised — core fields lock once another user has engaged with the sighting.
- FR-007: User can mark on a sighting's detail page that they have seen the bird, incrementing a sighting counter. Priority: nice-to-have
  > Socrates: Counter-argument considered: "an unverified raw tally encourages noise (meaningless duplicate taps) rather than useful signal." Resolution: demoted to nice-to-have — not essential for MVP.
- FR-008: User can view a sighting's change-log/history on its detail page. Priority: nice-to-have
  > Socrates: Counter-argument considered: "a proper audit trail is nontrivial to build well and isn't essential to prove the core value." Resolution: demoted to nice-to-have; MVP detail page shows current state only.
- FR-009: User can add comments to a sighting. Priority: must-have
  > Socrates: Counter-argument considered: "open comments without pre-built moderation tooling could get spammy or abusive before the admin notices." Resolution: kept as must-have; covered by admin's existing remove/warn/ban powers (FR-017/018) plus a submission cooldown extended to comments (see FR-019).
- FR-010: System nudges the user with possible duplicate sightings — flagged by nearby location within a recent time window (not text/description matching) — when adding a new sighting. Priority: nice-to-have
  > Socrates: Counter-argument considered: "reliable duplicate detection via fuzzy location+text matching is a real engineering problem that could balloon in scope." Resolution: revised — simplified to proximity + time window only, no text-similarity matching.

### Flocks
- FR-011: User can submit a new flock location, pending admin approval before it appears on the map. Priority: nice-to-have
  > Socrates: Counter-argument considered: "requiring admin approval could bottleneck if the admin isn't always available." Resolution: kept as written — approval stays, accepted as a deliberate quality-control tradeoff even at nice-to-have priority.

### Messaging
- FR-012: User can contact the owner of a sighting; the resulting conversation is tied to that sighting. Contacting other users is only possible through a sighting (the admin is the only exception). Priority: must-have
  > Socrates: Counter-argument considered: "restricting contact to sighting threads blocks two rescuers who want to coordinate generally, not about one specific bird." Resolution: kept as written — a deliberate scope boundary for MVP, not a gap to fix now.
- FR-013: User can view their messages grouped by sighting as a simple per-sighting thread, and reply with plain text; no read/unread tracking or separate inbox-list-vs-thread navigation polish for MVP. Priority: must-have
  > Socrates: Counter-argument considered: "a full inbox (list + thread view + read state) is a real feature in itself, more than an MVP needs." Resolution: revised twice — first simplified to a flat list, then reconsidered: since every message is already tied to a sighting (FR-012), grouping by sighting is the data's natural structure and effectively free; the real scope cut is dropping read/unread state and separate inbox-navigation UI, not the grouping itself.

### Admin
- FR-015: Admin can view a panel with basic statistics. Priority: nice-to-have
  > Socrates: Counter-argument considered: "with a very small/part-time admin team and few users at launch, a stats dashboard has little to act on yet." Resolution: demoted to nice-to-have; MVP admin panel is moderation actions only.
- FR-016: Admin can accept or reject pending flock-location requests. Priority: nice-to-have
  > Socrates: Counter-argument considered as part of FR-011/FR-016 pair above. Resolution: kept as written, tied to flock feature's nice-to-have priority.
- FR-017: Admin can remove sightings or flocks. Priority: must-have
  > Socrates: Counter-argument considered: "removal without a stated reason or appeal path could feel arbitrary to affected users." Resolution: kept as written — accepted for MVP given a small, trusted community and a single admin.
- FR-018: Admin can message, warn, or ban users. Priority: must-have
  > Socrates: Counter-argument considered: "three separate moderation actions may be more than needed at launch." Resolution: kept as written — graduated moderation (message → warn → ban) is better than a blunt ban-only tool.

### Anti-abuse
- FR-019: System enforces a short cooldown period between a user's sighting submissions and comment submissions (short enough that genuine back-to-back reports during an outing aren't blocked, only rapid spam). Priority: must-have
  > Socrates: Counter-argument considered: "a cooldown could throttle a legitimate rescuer reporting several distinct birds in one outing." Resolution: kept as written — cooldown duration is set short enough to allow genuine multi-bird reporting while still deterring spam.

## Business Logic

The app enforces an ordered (non-linear) status workflow for each sighting and computes an urgency ranking from criticality and time-since-reported, so unresolved sightings surface in the order they most need attention.

The rule consumes two user-facing inputs: the criticality level a reporter sets when logging a sighting, and the sighting's current position in its status workflow (each transition timestamped as it happens). From these, the app produces an urgency ranking that orders unresolved sightings so the ones needing attention longest, or reported most critical, surface first — this ordering is what a rescuer sees on the map and any sighting list, not just a flat feed by recency. Because the product is intended to work anywhere, not one city, this ranking and the map/list itself are scoped to the viewer's nearby area/city by default, rather than surfacing a single unranked worldwide feed.

The exact set of status names and the shape of the transition graph (which statuses can follow which) are not yet decided — the user wants to finalize these after further consultation. This is captured as an open question below; it does not block the rest of the PRD.

## Non-Functional Requirements

- A user's raw contact info (email, etc.) is never exposed to another user through any product surface; contact happens only via the in-product message inbox.
- Map-provider API calls are minimized by design: a sighting's location is not rendered as an embedded interactive map by default on its detail page.
- The map and sighting list remain usable at the small-to-medium record volumes expected during and after the MVP period (no specific latency target set yet — see Open Questions).
- The core reporting flow (adding a sighting) is fully usable from a phone's browser in the field, not just from a desktop.
- Any user action (submitting a sighting, sending a message, changing status) shows acknowledgement quickly, with visible progress on anything that takes noticeably longer.
- Resolved (healed/returned) sightings are excluded from the default map/list view, but remain viewable when a user explicitly asks to see them.

## Non-Goals

- **Anonymous/unregistered reporting** — the general-public reporting flow (Secondary persona) is out of scope for MVP; only logged-in rescue-community members can report, comment, contact, or message.
- **Real-time/live messaging** — the message inbox is asynchronous plain-text only; live/real-time delivery was considered and dropped, not deferred.
- **A single worldwide unranked feed** — the map and list are always scoped to the viewer's nearby area/city; there is no global unscoped view.
- **Full audit/change-log history** — a sighting's detail page shows current state only for MVP; history tracking was demoted to a future nice-to-have.

## Open Questions

1. **What are the exact status names and the transition graph for a sighting's rescue workflow (e.g. Reported → Contacted → Taken to vet → Healed/Returned, plus any closed/unable-to-help end state)?** — Owner: user, pending consultation. Block: partially — FR-005/FR-006/Business Logic assume an ordered, non-linear workflow exists, but the concrete states are not yet named.
2. **What specific latency/performance target, if any, applies to the map and sighting list?** — Owner: user. Not blocking for MVP; "usable at small-to-medium record volumes" is the working assumption until a concrete number is needed.

## Forward: tech-stack

- User is a full-stack .NET / Angular developer, new to agent-assisted delivery. No stack decision made here — flagged for `/10x-tech-stack-selector` to weigh alongside other candidates.

## User Stories

### US-01: Rescuer reports a distressed pigeon sighting

- **Given** a logged-in user
- **When** they submit a new sighting with description, behaviour, location, photo, status and criticality
- **Then** the sighting is saved and appears on the map and on the reporter's own profile

#### Acceptance Criteria
- A sighting cannot be submitted without a location, description, behaviour, status, and criticality level
- Submitting a second sighting before the cooldown period elapses is blocked with a clear message
