---
date: 2026-11-07T14:00:39+01:00
researcher: Claude (Opus 5.5) for Dominik Berniak
git_commit: e65b780
branch: main
repository: 10xDevs (PigeonWatch)
topic: "S-07 comment-on-sighting: what exists, what constrains it, what must be decided"
tags: [research, sightings, comments, cooldown, privacy, engagement-lock, angular, aspnetcore]
status: complete
last_updated: 2026-11-07
last_updated_by: Claude (Opus 5.5)
---

# Research: S-07 comment-on-sighting

**Date**: 2026-11-07T14:00:39+01:00
**Researcher**: Claude (Opus 5.5) for Dominik Berniak
**Git Commit**: e65b780
**Branch**: main
**Repository**: 10xDevs (PigeonWatch)

## Research Question

Roadmap slice S-07 (`context/foundation/roadmap.md:174-184`): "user can add comments to a sighting, subject to the submission cooldown" (FR-009, FR-019). What do the current code and recorded context give us, what constrains the change, and which decisions are still open for `/10x-plan`?

## Summary

- **No comment code exists.** A case-insensitive grep for `comment` over `PigeonWatch/Api/**/*.cs` and `PigeonWatch/Frontend/src/**/*.ts` returns no files at commit `e65b780`.
- **The requirement is thin.** FR-009 says only "User can add comments to a sighting" (`context/foundation/prd.md:86`). FR-019 adds "a short cooldown period between a user's sighting submissions and comment submissions" (`prd.md:115`). Any logged-in member may comment on any sighting (`prd.md:160`). Edit/delete, length, ordering, pagination and notifications are not specified in the PRD or roadmap (roadmap Unknowns is "—", `roadmap.md:182`).
- **The sighting cooldown is a template, not a shared service.** It is a 60 s check inside `SightingService.CreateAsync` (`PigeonWatch/Api/BusinessLogic/Services/SightingService.cs:40-55`) against the reporter's latest `SIGHTING.CREATE_DATE`, with the value as a const `SightingRules.CooldownSeconds = 60` (`PigeonWatch/Api/BusinessObjects/SightingRules.cs:8`). S-03 explicitly deferred "a generalised cooldown service for comments (S-07 generalises it on second use)" (`context/archive/2026-10-11-report-sighting-on-map/plan.md:41`).
- **FR-019 is ambiguous and undecided:** one shared clock across sightings and comments, or one clock per kind. This is the main product decision for the plan.
- **S-09 depends on what this slice stores.** The engagement lock fires "once another user has commented on or contacted the reporter" (`prd.md:80`); S-09 is sequenced after S-07 for that reason (`roadmap.md:198-208`). The comment record must therefore keep the sighting and the author id (internally), so S-09 can ask "is there a comment by someone other than the reporter?".
- **New endpoints must join the API contract gates.** Every routed endpoint needs a fixture in `EndpointFixtureRegistry`, protection or an anonymous-list entry, a pass through the privacy sweep, and an ownership test (`context/foundation/test-plan.md:184-196`). `test-plan.md:213` asks for a re-evaluation of the free-text contact rule when comments land.
- **Natural shape (inferred from existing patterns):** a child entity `SightingComment` (table `SIGHTING_COMMENT`) with `SightingId` and `AuthorId` navigations, exposed as separate sub-resources `GET`/`POST sightings/{id}/comments` on `SightingsController`, and a `sighting-comments` child component at the end of the detail page's details column.

## Detailed Findings

### Requirements and prior decisions

- FR-009 is must-have; the "spam before moderation" objection was answered with admin remove/warn/ban plus "a submission cooldown extended to comments" (`prd.md:86-87`, same in `shape-notes.md:108-109`).
- FR-019 is the only spam guard until S-10/S-11 (`roadmap.md:183`). The only cooldown acceptance criterion is for sightings: "blocked with a clear message" (`prd.md:58`).
- Anonymous commenting is a Non-Goal (`prd.md:164`).
- FR-017 covers removing "sightings or flocks" (`prd.md:106`); comments are not named. S-10 is "remove a sighting" only (`roadmap.md:210-220`). So no requirement currently covers removing a comment, though FR-009's justification leans on admin removal.
- Polish: there is no `UiLabels.pl.resx`; earlier slices add English keys only and leave Polish to S-12 (`context/archive/2026-10-10-edit-profile-name/plan.md:14`, `:46`). Whether each slice must add Polish is open (`roadmap.md:246`).

### API: the sighting pattern a comment would mirror

Paths relative to `PigeonWatch/Api/`.

- **Entity:** `Data/Entities/SightingEntity.cs:6-31`, internal, derives from `AuditableEntity` (`Data/Entities/AuditableEntity.cs:5-16`). FK `ReporterId` plus `Reporter` navigation (lines 8-10); `[MaxLength(SightingRules.X)]` on text columns.
- **DbContext:** `Data/PigeonWatchDbContext.cs:30-40`: `ToTable("Sighting")`, `HasOne(Reporter).WithMany().HasForeignKey(ReporterId).OnDelete(Restrict)`, index `(ReporterId, CreateDate)`. `UpperSnakeCaseNamingConvention` (line 15) produces UPPER_SNAKE_CASE names.
- **Migrations:** `Data/Migrations/<timestamp>_<Name>.cs`, e.g. `20261011135944_AddSighting.cs`, `20261103141128_AddSightingPhoto.cs`. Command in `Api/CLAUDE.md:150`.
- **Repository:** `Data/Repositories/SightingRepository.cs` with interface under `Repositories/Interfaces/`. `CreateAsync` (10-23) loads the user with `FindAsync`, returns null if missing. Reads use `.AsNoTracking().Include(e => e.Reporter)` (27-30).
- **Display name:** taken from the navigation, `entity.Reporter.DisplayName` (`Data/Mappers/SightingMapper.cs:35`) into `BusinessObjects/Sighting.cs` (no reporter id) and `WebApi/Models/SightingModel.cs:21`. A comment copies this with `Include(Author)` and an `AuthorDisplayName`; `CreateUser` holds a user id and must not be surfaced.
- **Service / controller / mapping:** `BusinessLogic/Services/SightingService.cs`, `WebApi/Controllers/SightingsController.cs` (class-level `[Authorize]` at line 15, derives from `FailureLoggingControllerBase`), `WebApi/Mappers/CreateSightingRequestMapper.cs`, `WebApi/ViewModelCreators/SightingViewModelCreator.cs:8-41`.
- **DI:** `DependencyInjection/DataServiceCollectionExtensions.cs:60-61`, `BusinessLogicServiceCollectionExtensions.cs:16`, `WebApiServiceCollectionExtensions.cs:27-30`.
- **Current user:** `Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), ...)` else `UnauthorizedForInvalidIdentity()` (`SightingsController.cs:47-48`); a missing user maps to 401 (`:54-55`).
- **Errors:** result records like `SightingCreateResult` with `Success`/`Failure`/`CooldownFailure` (`BusinessObjects/SightingCreateResult.cs:15-18`); PascalCase codes in `SightingErrorCodes.cs`; business errors become a 400 `ValidationProblemDetails` keyed by code (`FailureLoggingControllerBase.cs:39-53`); unknown sighting id is a 404 with no body (`:32-37`). Only codes in `loggedErrorCodes` are logged at Warning (`SightingsController.cs:27-39`).
- **Validation:** DataAnnotations on request models from `SightingRules` consts (`WebApi/Models/CreateSightingRequestModel.cs:10-19`) plus domain checks with trimming in `SightingService` (trim at 28-33, `Validate` at 283). Rules reach the SPA through `ClientConfigurationService.cs:20` → `ClientConfiguration.cs:14` → `SightingRulesModel.cs:11`.
- **Audit:** `Data/Auditing/AuditSaveChangesInterceptor.cs` fills audit columns from `ICurrentUserProvider` and `TimeProvider`; `CreateDate` doubles as the business "created at" (`SightingMapper.cs:36`).
- **Sub-resources precedent:** the photo endpoints are separate actions under the same controller (`SightingsController.cs:83-109`). `SightingModel` is reused by nearby, mine and list, so embedding comments in it would load them for every list query.
- **Pagination precedent:** `POST sightings/list` with page sizes 10/20/30 validated in `SightingService.cs:19-24`, SQL count+skip+take in `SightingRepository.cs:105-116`, returning `SightingPage(Items, TotalCount)`. Other lists use fixed caps instead (`MineMaxResults = 50`, `SightingRules.cs:12`).
- **Rate limiter:** only `auth` and `clientErrors` policies exist (`WebApi.Host/Program.cs:41-85`); sightings have none.

### The cooldown in detail

- Check at `SightingService.cs:40-55`, after validation and before photo storage. "Last submission" is `SightingRepository.GetLatestCreateDateByReporterAsync` (`SightingRepository.cs:132-141`), the newest `SIGHTING.CREATE_DATE` for the reporter.
- Elapsed below 60 s is blocked; exactly 60 s is allowed (`elapsed < cooldown`, line 47). `retryAfterSeconds = min(ceil(remaining), 60)` (line 49).
- Message text is sighting-specific: "Wait {n} seconds before reporting another sighting." (line 52). Code `CooldownActive`; `GetValidationProblem` adds the `retryAfterSeconds` extension and `Retry-After` header (`FailureLoggingControllerBase.cs:39-53`). Cooldown rejections are not logged (`IntegrationTests/ControllerFailureLoggingTests.cs:57`).
- The check is not atomic; two concurrent submissions can both pass, which S-03 accepted (`context/archive/2026-10-11-report-sighting-on-map/plan-brief.md:64`, `Api/CLAUDE.md:45`).
- A shared cooldown would need: a "latest submission" query across both tables (max of two reads, or one combined query), an `(AUTHOR_ID, CREATE_DATE)` index on the comment table, the rule moved into a shared role type (e.g. a `*Provider` with interface and DI registration, per `ArchitectureTests/ServiceRegistrationTests.cs`), a neutral home for the const, and a reworded message.
- The SPA cooldown mapper matches only `cooldownactive` on a 400 (`Frontend/src/app/features/sightings/sighting-errors.ts:25-54`) and shows `sighting.errors.cooldown`, "You can report another sighting in {0} seconds." (`sighting-errors.ts:76-79`; `Api/BusinessObjects/Resources/UiLabels.resx:444`). A comment form needs comment-worded text either way.
- Inference: with a shared clock, an E2E or integration test that reports a sighting and then comments as the same user would hit the cooldown; tests would need a second user or the injected `TimeProvider` (`IntegrationTests/PigeonWatchApiFactory.cs:22`, `:199`).

### Architecture rules a new entity/service/controller must satisfy

- Layer direction and registration only in `DependencyInjection` (`ArchitectureTests/LayerDependencyTests.cs:82`, `:99`).
- Role-type suffixes (`Service`, `Provider`, `Repository`, `ViewModelCreator`, `Mapper`): non-static, implement `I<Name>`, registered by `AddPigeonWatch` (`ServiceRegistrationTests.cs:7-44`).
- Namespaces by suffix; entities only in `Data` and not public (`TypeLocationTests.cs:9-77`); interfaces in `Interfaces/` folders (`InterfaceLocationTests.cs:19`, `:37`).
- Actions return `Task<ActionResult<TModel>>`; API models expose no BusinessObjects/Data types (`ControllerActionTests.cs:12-53`); `CancellationToken` rules (`CancellationTokenTests.cs:17`).
- UPPER_SNAKE_CASE, singular tables, single `ID` uniqueidentifier PK, `AuditableEntity` with required audit columns (`DatabaseModelTests.cs:17-136`).

### API test gates for new endpoints

- `IntegrationTests/EndpointFixtureRegistry.cs` (entries 40-46, builders 159+): `EndpointCoverageTests` and `PrivacySweep.cs:54-58` fail on a routed endpoint with no fixture.
- `PrivacySweepTests.cs` sweeps as anonymous, bob and alice (45, 60, 75) plus an audit-column check (130); `EndpointProtectionTests.cs:74` requires 401 for anonymous calls; `FixtureContext.cs:6` carries `SightingId` (a seeded comment id may be needed).
- `ResponseLeakScanner.NeedlesFor` builds needles from email variants (`ResponseLeakScanner.cs:10-25`); none of those needles is a user id. For sightings, id absence rests on the model shape. A comments response would need an explicit "no author id / createUser" assertion to cover that gap.
- Ownership test with forged body fields precedent: `OwnershipContractTests.cs:128`, display-name-not-email check `:171-194`.
- Unit test precedents: `UnitTests/SightingServiceTests.cs` (NSubstitute, `ManualTimeProvider` at 18-22, cooldown around 58-64, validate-before-cooldown at 158), `SightingRepositoryTests.cs` (SQLite), `SightingsControllerTests.cs`, `ConfigurationTests.cs`.

### Frontend: detail page and form patterns

Paths relative to `PigeonWatch/Frontend/src/app/`.

- **Detail page:** `features/sightings/sighting-detail-page/sighting-detail-page.{ts,html,scss}`, route `sightings/:id` behind `authGuard` (`app.routes.ts:50-54`). State is a `DetailState` union loading/notFound/error/loaded (`sighting-detail-page.ts:68-72`) driven by `combineLatest(id, reloads)` → `switchMap(SightingsApi.get)` (`:212-227`). The photo loads in its own stream so it does not block the page (`:205-210`, `:263-278`), which is the pattern for a comments list.
- **Placement:** end of `.sighting-detail__details` after the second `dl` (`sighting-detail-page.html:99-100`), as its own child component (e.g. `sighting-detail-page/sighting-comments/`) with `sightingId` input, its own load stream and `providers: [provideFormFieldDefaults()]` (`FormSubmitState` is per component, `shared/ui/form-field/form-submit-state.ts:7-13`). That keeps it in the left column when the map is shown at `xl` (`sighting-detail-page.scss:92-99`).
- **Form pattern:** `features/sightings/report-sighting-page/report-sighting-page.ts`: Signal Forms with `required`, `maxLength` and whitespace-only-as-required (`:121-139`, `isBlank` `:214-216`); limits from `ConfigurationService.clientConfig()?.sightingRules ?? fallbackSightingRules` (`:118-120`; `core/configuration/configuration.service.ts:20-29`); submit with `busy` + `finalize` (`:173-191`); failure → `<app-alert kind="error">` + `<app-error-reference>` (`report-sighting-page.html:103-108`). No toast/snackbar exists in the app; acknowledgement is the `<app-submit-button [busy]>` state and the resulting navigation.
- **API client:** `features/sightings/sightings-api.ts:15-61` (one root-provided class per feature, `encodeURIComponent(id)`); wire types in `sighting.types.ts:26-38`; ProblemDetails helpers in `shared/http/problem-details.ts:3-41`; error reporting via `core/error-reporting/error-reporting.service.ts:19-28` with lowercase dotted contexts (e.g. a future `sighting.comments.load`).
- **Reusable UI:** `Alert`, `ErrorRetry`, `ErrorReference`, `LoadingIndicator`, `FieldErrors`, `FormActions`, `SubmitButton` (`shared/ui/index.ts`). No empty-state component; empty states are `<app-alert kind="info">`. Relative time helper `features/sightings/shared/sighting-age.ts:10-26`, rendered as `<time>` in `shared/sighting-row/sighting-row.html:39-41`.
- **Labels:** `PigeonWatch/Api/BusinessObjects/Resources/UiLabels.resx` (detail keys 366-427, errors 444-452). Key format `<area>.<group>.<name>` (Frontend `CLAUDE.md:84-87`). Gates: `core/resources/label-coverage.spec.ts:97-115` (every used key exists; no literal template text), `generate-resources.spec.ts:36-52`, `Api/UnitTests/UiResourceTests.cs:14-43`.
- **Tests:** detail spec with `vi.fn` API stubs and `RouterTestingHarness` (`sighting-detail-page.spec.ts:34-80`); cooldown form test (`report-sighting-page.spec.ts:315-332`); reporting spec (`report-sighting-page-reporting.spec.ts:186`, cooldown not reported). Kitchen sink requires a `detail-*` cell, a stub mode and a marker assertion for new detail-page states (Frontend `CLAUDE.md`). Playwright template: `tests/e2e/sighting-photo.spec.ts:12-64`.

## Code References

- `PigeonWatch/Api/BusinessLogic/Services/SightingService.cs:40-55` - sighting cooldown check
- `PigeonWatch/Api/BusinessObjects/SightingRules.cs:8` - `CooldownSeconds = 60`
- `PigeonWatch/Api/Data/Repositories/SightingRepository.cs:132-141` - latest create date by reporter
- `PigeonWatch/Api/Data/Entities/SightingEntity.cs:6-31` - entity template
- `PigeonWatch/Api/Data/PigeonWatchDbContext.cs:30-40` - EF config template
- `PigeonWatch/Api/Data/Mappers/SightingMapper.cs:35` - display name from navigation
- `PigeonWatch/Api/WebApi/Controllers/SightingsController.cs:83-109` - sub-resource precedent
- `PigeonWatch/Api/WebApi/Controllers/FailureLoggingControllerBase.cs:39-53` - ValidationProblem + Retry-After
- `PigeonWatch/Api/IntegrationTests/EndpointFixtureRegistry.cs:40-46` - endpoint fixture registry
- `PigeonWatch/Api/IntegrationTests/ResponseLeakScanner.cs:10-25` - email-only needles
- `PigeonWatch/Frontend/src/app/features/sightings/sighting-detail-page/sighting-detail-page.html:99-100` - comments insertion point
- `PigeonWatch/Frontend/src/app/features/sightings/sighting-errors.ts:25-79` - cooldown error mapping
- `PigeonWatch/Frontend/src/app/features/sightings/report-sighting-page/report-sighting-page.ts:118-207` - form pattern

## Architecture Insights

- Child data of a sighting is exposed as sub-resources on `SightingsController`, not embedded in the shared `SightingModel`.
- On the inspected sighting read paths, the reporter crosses the API as a display name from an EF navigation, with no reporter id (`SightingMapper.cs:35`, `SightingModel.cs:21`).
- Rules are consts in `BusinessObjects` used by request models, entities and the client configuration, so the report form reads limits from client configuration, with a fallback constant (`report-sighting-page.ts:118-120`).
- Business throttling is a database check in the service, not the ASP.NET rate limiter; cooldown is an expected condition (not logged server-side, not reported client-side).
- "Promote on second use": S-03 left the cooldown and S-03's UI left shared components un-generalised until a second consumer appears (`context/archive/2026-10-18-sighting-detail-ui-contract/plan.md:51`). Comments are that second consumer for the cooldown.

## Historical Context (from prior changes)

- `context/archive/2026-10-11-report-sighting-on-map/research.md:226-229` - left open whether S-03 builds the cooldown for S-07 reuse.
- `context/archive/2026-10-11-report-sighting-on-map/plan.md:41`, `:61`, `:74`, `:171`, `:179`, `:195` - 60 s DB check, coded 400 with `retryAfterSeconds`, generalisation deferred to S-07.
- `context/archive/2026-11-03-attach-sighting-photo/plan-brief.md:24`, `:31` - photo covered by the same cooldown; order validate → cooldown → storage.
- `context/archive/2026-10-25-server-side-observability-logging/plan.md:128-134` - `FailureLoggingControllerBase` and `loggedErrorCodes`.
- `context/archive/2026-10-18-sighting-detail-ui-contract/` - detail page contract (page card, focus, title, no tiles until "Show map", kitchen-sink cells, 7-state matrix); does not mention comments.
- `context/archive/2026-10-18-testing-api-privacy-authorization-contract/` - origin of the endpoint registry and privacy sweep; `test-plan.md:213` records its free-text exclusion and the S-07 re-evaluation trigger.

## Related Research

- `context/archive/2026-10-11-report-sighting-on-map/research.md`
- `context/archive/2026-11-03-attach-sighting-photo/research.md`
- `context/archive/2026-10-18-sighting-detail-ui-contract/research.md`

## Open Questions

Product decisions for `/10x-plan` (none answered by PRD, roadmap or archive):

1. **Cooldown model.** One shared clock across sightings and comments, or separate clocks per kind (and if separate, the comment duration)? FR-019 wording supports both (`prd.md:115`). Also whether the error code stays `CooldownActive`.
2. **Comment scope.** Length limit; ordering (oldest or newest first); pagination or a fixed cap; whether the owner can comment on their own sighting; edit/delete of own comments (deferring keeps S-09 simpler, since deletion raises whether the lock un-locks).
3. **Comments on resolved sightings.** Allowed or blocked?
4. **Acknowledgement.** No toast exists; likely the new comment appears in the list and the field clears, but this is a UI choice.
5. **Free-text contact rule** (`test-plan.md:213`). Keep the "self-disclosure, not a leak" stance for comments, or add a check?
6. **User-id needle gap.** Add an explicit no-author-id assertion for comment responses, or extend `ResponseLeakScanner` to user ids?
7. **Admin removal of comments.** Not covered by FR-017/S-10; record it as a roadmap gap or accept it.
8. **Polish labels.** English only per the established default unless `roadmap.md:246` is decided otherwise.

Minor observation: Frontend `CLAUDE.md` still points to `context/changes/sighting-detail-ui-contract/visual-notes.md`, which now lives under `context/archive/2026-10-18-sighting-detail-ui-contract/`.
