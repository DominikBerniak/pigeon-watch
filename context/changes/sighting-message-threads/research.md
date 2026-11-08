---
date: 2026-11-07T17:43:09+01:00
researcher: Claude (Opus 5.5) for Dominik Berniak
git_commit: 86591f5
branch: main
repository: 10xDevs (PigeonWatch)
topic: "S-08 sighting-message-threads: what exists, what constrains it, what must be decided"
tags: [research, sightings, messaging, threads, privacy, cooldown, engagement-lock, angular, aspnetcore]
status: complete
last_updated: 2026-11-07
last_updated_by: Claude (Opus 5.5)
---

# Research: S-08 sighting-message-threads

**Date**: 2026-11-07T17:43:09+01:00
**Researcher**: Claude (Opus 5.5) for Dominik Berniak
**Git Commit**: 86591f5
**Branch**: main
**Repository**: 10xDevs (PigeonWatch)

## Research Question

Roadmap slice S-08 (`context/foundation/roadmap.md:186-196`): "user can contact a sighting's owner and read and reply to plain-text messages grouped by sighting, without ever seeing the other person's contact details" (FR-012, FR-013). What do the current code and recorded context give us, what constrains the change, and which decisions are still open for `/10x-plan`?

## Summary

- **No messaging code exists.** Case-insensitive searches for `message`, `thread`, `inbox`, `conversation`, `notification` and `contact` over tracked `.cs`/`.ts`/`.html` files under `PigeonWatch/Api` and `PigeonWatch/Frontend/src` find only log/exception/validation text, `System.Threading`, the comment component's `ThreadState` naming and `SightingRepository.ListInBoxAsync` (a bounding-box query). No file name matches.
- **Requirements are short.** FR-012: contact the owner, conversation tied to the sighting, contact only through a sighting, admin the only exception (`context/foundation/prd.md:96`). FR-013: messages grouped per sighting as a simple thread, plain-text replies, no read/unread and no "inbox-list-vs-thread navigation polish" (`prd.md:98`). Real-time delivery is dropped, not deferred (`prd.md:165`). Contact info is never exposed on any surface (`prd.md:44`, `prd.md:120`). Sending a message must acknowledge quickly (`prd.md:124`).
- **S-07 comments is a near-complete template.** Same layering, same sub-resource route shape (`sightings/{sightingId:guid}/...`), same cooldown provider, same client-configuration path for rules, same SPA error seam, same privacy test scaffolding. Messages differ in three ways the template does not cover: a **second participant** (the sighting owner, resolved server-side from `SightingEntity.ReporterId`, `PigeonWatch/Api/Data/Entities/SightingEntity.cs:8`), **visibility restricted to participants** (comments are readable by every signed-in user), and a **cross-sighting "my threads" view**.
- **The wire contract already hides other users' identity.** Other users appear only as a display name (`SightingModel.ReporterDisplayName`, `PigeonWatch/Api/WebApi/Models/SightingModel.cs:21`; `SightingCommentModel.AuthorDisplayName`) and a server-computed `IsMine` on comments. No response carries another user's id or email. `UserName` equals the email (`PigeonWatch/Api/Data/Repositories/AccountRepository.cs:48`), so it must never be projected.
- **The SPA cannot tell whether a sighting is its own.** `SightingModel` (`SightingModel.cs:3-26`) has no owner flag and no reporter id; the SPA knows only its own `currentUser.id`. "Contact owner" (hide on own sighting) and "owner sees threads" both need a new server-computed flag, e.g. `isMine` on the sighting detail.
- **The privacy tests will force correct coverage.** Every new routed endpoint must get a fixture in `EndpointFixtureRegistry` or `EndpointCoverageTests` fails; the privacy sweep and anonymous-401 sweep then run over it (`context/foundation/test-plan.md:171-186`). The sweep's world is seeded by Alice only (`PigeonWatch/Api/IntegrationTests/PrivacySweepTests.cs:47`, `SeedWorldAsync`), so a thread between Alice and Bob must be seeded for the sweep to see foreign message content. The scanner looks for emails only, never user ids, so a contract test in the style of `Comment_responses_carry_no_author_id_audit_user_or_user_id` (`PigeonWatch/Api/IntegrationTests/SightingCommentContractTests.cs:277`) is needed.
- **Open product decisions** (no document answers them): thread model, who may start a thread, cooldown on messages, length limit, edit/delete, where "my threads" lives, paging/order, and what counts as "made contact" for S-09. See Open Questions.

## Detailed Findings

### Requirements and roadmap

- FR-012 / FR-013 as above (`prd.md:96-99`). The FR-013 resolution says grouping by sighting is "effectively free"; the real cut is read state and separate inbox navigation (`prd.md:99`).
- Goals call it a "basic (non-real-time) message inbox" (`prd.md:37`) and the NFR says contact happens "only via the in-product message inbox" (`prd.md:120`), so some entry point listing a user's threads is implied, without navigation polish.
- FR-019 cooldown names "sighting submissions and comment submissions" only (`prd.md:115`); S-08's roadmap row cites FR-012/FR-013 and not FR-019 (`roadmap.md:52`).
- Downstream needs: S-09 locks location/description "once another user has commented on or contacted the reporter" (`prd.md:80`, `roadmap.md:200`), so S-08 data must answer "has a non-owner made contact on sighting X". S-11 (admin messaging, the one exception to sighting-only contact) depends on S-08 (`roadmap.md:228`, `roadmap.md:233`).
- `PigeonWatch/CLAUDE.md:9` still says "messaging does not exist yet"; S-08 must update it.

### Identity and privacy (from the identity audit)

- Users: `UserAccountEntity` → table `USER_ACCOUNT`, Guid id, unique `DisplayName` (S-02 profile name), `UserName` = email (`PigeonWatch/Api/Data/Entities/UserAccountEntity.cs`, `AccountRepository.cs:48`). No role store, no authorization policies, no admin concept; `CurrentUserModel.Roles` is always empty (`PigeonWatch/Api/UnitTests/ConfigurationTests.cs:115`). Not needed for S-08, needed by S-10/S-11.
- Current user resolution is inline in every action: `Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), ...)`, else `UnauthorizedForInvalidIdentity()` (e.g. `SightingCommentsController.cs:32-33`).
- Comment ownership is checked twice: service (`NotCommentAuthor` → 403) and repository write filter (`AuthorId == authorId`, `DeletedAt == null`). Comment impl-review F1 made "scope writes by every invariant, not only by id" a recurring rule.
- `BusinessObjects.CurrentUser(Id, Email, DisplayName)` (`PigeonWatch/Api/BusinessObjects/CurrentUser.cs:3`) carries the email; it must not be reused to describe a message counterpart.
- No endpoint takes a user id or email as a target, and no user lookup exists. Recipient identification by sighting id alone (server resolves `ReporterId`) keeps that property.

### Comment slice as the layer template (from the slice map)

- **Controller** `SightingCommentsController` (`PigeonWatch/Api/WebApi/Controllers/SightingCommentsController.cs:12-14`: `[Route("sightings/{sightingId:guid}/comments")]`, `[Authorize]`), derives from `FailureLoggingControllerBase` (logged 401/403/404 helpers and `GetValidationProblem` that adds `retryAfterSeconds` + `Retry-After` on cooldown). `GET` sets `Cache-Control: private, no-store`. Error mapping: not-found codes → 404, `NotCommentAuthor` → 403, `AuthorNotFound` → 401, others → 400 `ValidationProblemDetails`.
- **Service** `SightingCommentService`: order validate → sighting exists → (author) → cooldown → write (`PigeonWatch/Api/BusinessLogic/Services/SightingCommentService.cs:30-38` for create). Cooldown via `ISubmissionCooldownProvider.GetRetryAfterSeconds(latest)` (`PigeonWatch/Api/BusinessLogic/Services/SubmissionCooldownProvider.cs:7-19`, duration `SightingRules.CooldownSeconds = 60`, `PigeonWatch/Api/BusinessObjects/SightingRules.cs:8`); each feature reads its own "latest submission" from its own table.
- **BusinessObjects**: record + `New*` + `*Page` + `*PageResult` + `*Result` (with `CooldownFailure`) + `*ErrorCodes` + const-only `*Rules` (`SightingCommentRules`: `TextMaxLength = 500`, `PageSize = 20`).
- **Data**: internal entity deriving `AuditableEntity`; DbContext config with singular `ToTable`, UPPER_SNAKE convention, `DeleteBehavior.Restrict` FKs, `(SightingId, CreateDate)` and `(AuthorId, CreateDate)` indexes; migration via `dotnet ef migrations add` (command in `PigeonWatch/Api/CLAUDE.md`), no runtime `Migrate()`, tests use `EnsureCreated`. Offset paging ordered `CreateDate desc, Id desc` with `CountAsync`. The comment repository has no "sighting owner" lookup; messages need one.
- **DI**: three extension classes (business logic, data, web API); `ArchitectureTests/ServiceRegistrationTests.cs` fails on an unregistered role interface. `DatabaseModelTests` and `ControllerActionTests` enforce table naming and `Task<ActionResult<ApiModel>>` actions.
- **Client configuration**: `CommentRulesModel` travels `SightingCommentRules` → `ClientConfiguration` → `ClientConfigurationService` → `ClientConfigurationViewModelCreator` → `GET configuration/client` (`[AllowAnonymous]`, `PigeonWatch/Api/WebApi/Controllers/ConfigurationController.cs:21-22`) → SPA `ConfigurationService.clientConfig` with a local fallback in `sighting.types.ts`. A `MessageRules` model would follow the same five places.
- **SPA**: detail route `sightings/:id` (`authGuard`, `layout: 'wide'`, lazy). `<app-sighting-comments>` is a child component with its own `switchMap` load stream, signal forms, "show older" offset paging with id dedupe, local prepend on post. Errors: local `comment-errors.ts` union, `ErrorReportingService.report('api-failure', context, error)` only for 403 and unexpected; 400/401/404/cooldown silent. Labels only in `PigeonWatch/Api/BusinessObjects/Resources/UiLabels.resx` (English neutral only), generated into SPA snapshots; `label-coverage.spec.ts` guards keys.

### Tests and gates

- Levels shipped for comments: service, controller, VM-creator and repository unit tests (repository on in-memory SQLite); contract tests on `PigeonWatchApiFactory` with `ManualTimeProvider` and Alice/Bob `TestUser`s; Vitest component, reporting and error-mapper specs; one Playwright spec registering a fresh user with an empty `storageState`.
- test-plan Risk #2 names S-08 (contact leak through new surfaces, `test-plan.md:45`) and Risk #3 (reading/changing another user's records, two-user ownership tests on the real pipeline, `test-plan.md:46`). New-endpoint recipe: `test-plan.md:171-186`. Cooldown must remain a 400 because the privacy sweep fails on any 429 (`PigeonWatch/Api/CLAUDE.md`, comment plan-review F4).
- test-plan §7 excludes a contact-detail rule on free text and names S-08 as the re-evaluation trigger (`test-plan.md:213`); for comments the exclusion held because the author can edit/delete to retract. If messages have no edit/delete, that argument does not carry over and §7 must be re-decided.
- `FixtureContext` carries `SightingId` and `CommentId`; a thread/message id must be added for message endpoint fixtures. The audit-column check (`PrivacySweepTests.cs:129-160`) covers `USER_ACCOUNT` and `SIGHTING` only; a message table needs its own reader to be covered.
- A two-user E2E (owner sees the contact, replies) has no precedent: every existing spec uses one user.
- Initial bundle is 533.52 kB against a 540 kB warning (`PigeonWatch/Frontend/CLAUDE.md:33`). A header/menu entry for "my threads" touches initial-bundle code and must be measured.

### Observability

- No observability audit covers comments or messaging; the three audits (2026-10-25/26/30) cover auth and sightings. Rules that apply are codified in `PigeonWatch/Api/CLAUDE.md` (`FailureLoggingControllerBase`, `loggedErrorCodes`, no coordinates/IPs in logs) and `PigeonWatch/Frontend/CLAUDE.md:92-101` (report unexpected states with a dotted context such as `sighting.messages.send`, never user data such as message text in a report).

## Code References

- `PigeonWatch/Api/WebApi/Controllers/SightingCommentsController.cs:12-14` - sub-resource route and `[Authorize]` template
- `PigeonWatch/Api/BusinessLogic/Services/SightingCommentService.cs:30-38` - validate → exists → cooldown → write order
- `PigeonWatch/Api/BusinessLogic/Services/SubmissionCooldownProvider.cs:7-19` - shared cooldown calculation
- `PigeonWatch/Api/BusinessObjects/SightingRules.cs:8` - `CooldownSeconds = 60`
- `PigeonWatch/Api/Data/Entities/SightingEntity.cs:8` - `ReporterId`, the owner to resolve as recipient
- `PigeonWatch/Api/WebApi/Models/SightingModel.cs:3-26` - sighting wire model, no owner flag or reporter id
- `PigeonWatch/Api/Data/Repositories/AccountRepository.cs:48` - `UserName = account.Email`
- `PigeonWatch/Api/BusinessObjects/CurrentUser.cs:3` - carries email; not for counterparts
- `PigeonWatch/Api/WebApi/Controllers/ConfigurationController.cs:21-22` - anonymous client configuration
- `PigeonWatch/Api/IntegrationTests/EndpointFixtureRegistry.cs:47-50` - comment fixture entries to mirror
- `PigeonWatch/Api/IntegrationTests/PrivacySweepTests.cs:47` - sweep world seeding
- `PigeonWatch/Api/IntegrationTests/SightingCommentContractTests.cs:277` - no-id/no-email response test to copy
- `PigeonWatch/Frontend/src/app/features/sightings/comment-errors.ts` - SPA failure union and reportability
- `PigeonWatch/Frontend/tests/e2e/sighting-comments.spec.ts` - single-user E2E template

## Architecture Insights

- The recipient must be derived, never supplied: `POST` takes a sighting id (and for replies a thread id), the server resolves the owner from `ReporterId`. This matches the existing "owner comes from the token, forged body fields ignored" contract (`OwnershipContractTests`).
- Participant-only reads are new. Comments are open to every signed-in user; a thread must return 404 (not 403, to avoid confirming existence) or 403 to non-participants. Either way, the repository read must filter by participant, following the "scope by every invariant" rule.
- A thread row keyed by (sighting, contacting user) with the owner implied by the sighting gives S-09 its "contact made" signal by a simple existence query and leaves S-11 room for an admin thread type later; this is an inference, not a decision.
- Wire identity for messages should follow comments: counterpart display name plus server-computed `isMine` per message, never user ids.

## Historical Context (from prior changes)

- `context/archive/2026-10-11-report-sighting-on-map/plan.md:37` - messaging (S-08) explicitly out of scope for S-03. Supported.
- `context/archive/2026-11-07-comment-on-sighting/plan.md` and `plan-brief.md` - cooldown model, 500-char limit, offset paging (20, newest first), soft delete with `DELETED_AT`, `EDITED_AT` separate from audit columns, `isMine` wire identity, 400-not-429 cooldown; all directly reusable.
- `context/archive/2026-11-07-comment-on-sighting/research.md:32` - S-09 must decide whether deleted or owner-authored comments trigger the lock; the same question applies to messages (does an owner reply count, does a thread with no reply count).
- `context/archive/2026-10-18-testing-api-privacy-authorization-contract/research.md:94` - the endpoint coverage guard was installed so S-08's unknown endpoints fail loudly until covered. Supported by `EndpointCoverageTests`.

## Related Research

- `context/archive/2026-11-07-comment-on-sighting/research.md`

## Open Questions

1. **Thread model.** One thread per (sighting, contacting user), or one shared thread per sighting? FR-012's "contact the owner" suggests the former.
2. **Who starts a thread.** Only non-owners contact the owner, or can the owner start one with a commenter? Owner messaging themself should be rejected either way.
3. **Cooldown.** FR-019 does not name messages. None, the shared 60 s on its own clock, or something else? If any, it must stay a 400.
4. **Length limit.** No requirement; precedents are 500 (comments, behaviour) and 1000 (description).
5. **Edit/delete of messages.** Not required. Without them, test-plan §7's free-text exclusion must be re-decided and recorded (`test-plan.md:213`).
6. **"My threads" entry point.** Header/menu link (bundle budget 6.5 kB headroom), profile page section, or sighting detail only (owner sees a list of threads there). FR-013 forbids polish, not an entry point.
7. **Ordering and paging.** Comment precedent is newest first, 20 per page; a conversation usually reads oldest first.
8. **Messaging on resolved sightings.** Status today is only Spotted/Caught at creation, so this only matters once S-09 lets status advance.
9. **What S-09 counts as "made contact".** First message from a non-owner is the natural reading of `prd.md:80`.
10. **Polish labels.** English-only per the S-12 default unless decided otherwise (`roadmap.md:246`).
