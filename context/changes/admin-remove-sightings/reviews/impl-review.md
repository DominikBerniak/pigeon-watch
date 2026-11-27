<!-- IMPL-REVIEW-REPORT -->
# Implementation Review: Admin removes sightings (S-10)

- **Plan**: context/changes/admin-remove-sightings/plan.md
- **Scope**: Full plan (Phases 1–4 of 5; Phase 5 not fully ticked because 5.4 waits for the production deploy)
- **Reviewed phases**: 1, 2, 3, 4
- **Date**: 2026-11-24
- **Verdict**: APPROVED
- **Findings**: 0 critical, 2 warnings, 8 observations

Reviewed range: `b843504..3882e51` (df5fd76, 7110820, 4b0f2b3, 3882e51), 131 files, +7427 −322. Plan drift check: every planned change in Phases 1–4 matches the plan's intent, with the adaptations recorded during implementation. Nothing is missing and nothing crosses the "What We're NOT Doing" list.

Automated criteria re-run on HEAD (e001958+):
- `dotnet build PigeonWatch/Api/PigeonWatchApi.slnx`: 0 errors. 5 warnings: 2× NU1603 plus CS8602, CS4014 and EF1002 (see F7).
- Architecture tests: 44/44. `has-pending-model-changes`: none. Unit tests: 774/774. Integration tests: 225/225.
- `npm run build`: pass, initial bundle 548.05 kB. `npx tsc -p tsconfig.spec.json --noEmit`: pass.
- `npm test -- --watch=false`: 1244/1246 while the .NET builds and the frontend build ran in parallel. Two kitchen-sink map tests hit "Test timed out in 15000ms". The kitchen-sink spec alone: 102/102 (521 s). See F5.

Manual criteria 1.6, 1.7, 2.6, 3.5 and 4.3–4.6 were each confirmed by the user in the session. 4.6 was ticked on the basis that the SPA has no dark theme.

## Verdicts

| Dimension | Verdict |
|-----------|---------|
| Plan Adherence | PASS |
| Scope Discipline | PASS |
| Safety & Quality | WARNING |
| Architecture | PASS |
| Pattern Consistency | WARNING |
| Success Criteria | PASS |

## Findings

### F1 — A member can probe whether a removed sighting exists through its thread list

- **Severity**: ⚠️ WARNING
- **Impact**: 🔎 MEDIUM — real tradeoff; pause to reason through it
- **Dimension**: Safety & Quality
- **Location**: PigeonWatch/Api/BusinessLogic/Services/SightingThreadService.cs:79
- **Detail**: `ListForSightingAsync` checks only that the sighting exists (`GetSightingOwnerAsync`) and never calls `CanView`. For Carol, a member with no thread on it, `GET sightings/{id}/threads` on a removed sighting is 200 with an empty list, while an unknown id is 404 and `GET sightings/{id}` is 404 in both cases. This contradicts Phase 3's "404 where they cannot see it" rule, although Phase 3 explicitly listed `ListForSightingAsync` as unfiltered: the plan left a gap here, not the code. Impact is low because the id is a GUID that must already be known, and no content leaks. No test covers Carol on this endpoint. `ListMessagesAsync` and `MarkReadAsync` use the same lookup but still answer 404 through `ThreadNotFound`.
- **Fix A ⭐ Recommended**: Use the access lookup in `ListForSightingAsync` and answer 404 when `CanView` is false and the caller has no thread on the sighting; add Carol's 404 row to `SightingRemovalVisibilityContractTests`.
  - Strength: Restores the "invisible means 404" rule on the last read path, while Bob (a participant) keeps his 200; the access lookup already exists in `SightingThreadRepository`.
  - Tradeoff: One more branch in the service and one more contract-test row; the plan's "stays unfiltered" line becomes a narrower rule.
  - Confidence: HIGH — the same pattern runs in `StartAsync` a few lines above.
  - Blind spot: The SPA detail page calls this endpoint for non-owners; for a removed sighting a non-viewer never reaches the detail page (404 first), so no UI path should change, but that is inferred, not tested.
- **Fix B**: Accept and document the existence probe in `Api/CLAUDE.md` as a known limitation.
  - Strength: No code change; GUID ids make probing impractical.
  - Tradeoff: Leaves one path that answers differently from the rule every other path follows.
  - Confidence: MEDIUM — acceptable for the MVP threat model, weaker once S-11 adds more moderation state.
  - Blind spot: None significant.
- **Decision**: FIXED (Fix A) — ListForSightingAsync now 404s a non-viewer without a thread; unit tests for outsider and admin, Carol 404 and Bob 200 in SightingRemovalVisibilityContractTests, Api/CLAUDE.md table row added

### F2 — Edit page tells the reporter of a removed sighting "You can only edit sightings you reported"

- **Severity**: ⚠️ WARNING
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Pattern Consistency
- **Location**: PigeonWatch/Frontend/src/app/features/sightings/edit-sighting-page/edit-sighting-page.ts:362
- **Detail**: For a removed sighting `editPermissions` is null, and the page maps any null to the `notOwner` state, which is false for the reporter. Only a typed or old `/sightings/:id/edit` URL reaches it, because the Edit link is hidden. The plan did not cover the edit page. The label `sighting.edit.errors.removed` already exists.
- **Fix**: Check `removedAt` before the `editPermissions === null` branch and show the existing removed message (with the back link), plus one spec.
- **Decision**: FIXED — edit page shows the removed alert (sighting.edit.errors.removed) before the not-owner check; edit-sighting-page spec, kitchen-sink cell edit-removed with its marker assertion, Frontend/CLAUDE.md updated

### F3 — One failing admin email stops the bootstrap for the rest

- **Severity**: 💡 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Safety & Quality
- **Location**: PigeonWatch/Api/Data/Identity/AdminRoleBootstrapper.cs:16-28
- **Detail**: One scope and one `DbContext` serve the whole email loop. An exception from one grant (for example a `USER_ROLE` unique-index violation when two instances start together) ends the loop, so later emails wait for the next restart; after a failed save the added row stays in the change tracker. The worker logs it at Error, so it is visible.
- **Fix**: Use a fresh scope per email and catch per email, logging at Error with the cause and continuing.
- **Decision**: FIXED — one DI scope per email and a per-email catch (Error with the exception and index, cancellation excluded); unit test with a SQLite trigger refusing the first grant; Api/CLAUDE.md bootstrap bullet updated

### F4 — Removal and the write paths are not atomic, and the race is not documented as accepted

- **Severity**: 💡 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Safety & Quality
- **Location**: PigeonWatch/Api/Data/Repositories/SightingRepository.cs:204-220; SightingService.UpdateAsync, SightingCommentService, SightingThreadService Start/Reply
- **Detail**: Remove reads, checks `RemovedAt is null` and saves, so two truly concurrent removals both write (the last wins). The write paths check `IsRemoved` and then write in a separate statement, so a removal landing in between lets one last comment, message or edit through. `Api/CLAUDE.md` documents the equivalent races as accepted for the cooldown and the engagement lock, but not for removal.
- **Fix**: Document both races as accepted in `Api/CLAUDE.md` (single admin, MVP), next to the existing accepted races.
- **Decision**: FIXED — both races documented as accepted ("Accepted races" bullet in Api/CLAUDE.md, Admin role and sighting removal)

### F5 — Kitchen-sink tests run close to their 15 s budget and time out under CPU load

- **Severity**: 💡 OBSERVATION
- **Impact**: 🔎 MEDIUM — real tradeoff; pause to reason through it
- **Dimension**: Success Criteria
- **Location**: PigeonWatch/Frontend/src/app/dev/kitchen-sink/kitchen-sink.spec.ts:40-44
- **Detail**: With the .NET builds running in parallel, "shows the card of the selected marker" and "shows the no-match text when the chosen filter matches nothing" hit "Test timed out in 15000ms". The spec alone passes 102/102 in 521 s. Phase 4 already needed a separate 30 s `sinkRenderTimeout` for the cold render. Each test renders the whole sink, so every new cell makes all 102 tests slower, and CI or a busy laptop will hit this again.
- **Fix A ⭐ Recommended**: Track a follow-up to render only the cells a test needs (or split the spec per view group).
  - Strength: Removes the growth problem at its source; each new cell stops slowing every test.
  - Tradeoff: A non-trivial test-harness change outside this slice's scope.
  - Confidence: MEDIUM — the cell definitions are already grouped by view, but the harness renders them as one component.
  - Blind spot: How much of the cost is the render versus jsdom setup per test has not been measured.
- **Fix B**: Raise `sinkTestTimeout` (for example to 30 s).
  - Strength: One-line change; stops the flakes now.
  - Tradeoff: Hides the trend; the suite keeps getting slower.
  - Confidence: HIGH — it would stop these timeouts.
  - Blind spot: None significant.
- **Decision**: FIXED (Fix B) + FOLLOW-UP (Fix A) — sinkTestTimeout raised to 30 s after the map tests timed out twice inside the pre-commit hook; rendering only the cells a test needs stays queued in follow-ups/review-fixes.md

### F6 — `Removing_twice_keeps_the_first_removal` cannot prove the first remover is kept

- **Severity**: 💡 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Success Criteria
- **Location**: PigeonWatch/Api/IntegrationTests/SightingRemovalContractTests.cs (around line 82)
- **Detail**: Both removals use the same admin, so asserting `REMOVED_BY` equals that admin would pass even if the second removal overwrote it. The `removedAt` assertion does protect the rule (the Phase 2 break-check turned it red), so only the `REMOVED_BY` half is weak.
- **Fix**: Remove the second time as a second admin (`CreateAdminAsync` with two listed emails) and assert `REMOVED_BY` is still the first.
- **Decision**: FIXED — the second removal now comes from a second admin and REMOVED_BY must still be the first; break-check (keep removedAt, overwrite REMOVED_BY) turns it red

### F7 — Interpolated `SqlQueryRaw` in a test (EF1002 build warning)

- **Severity**: 💡 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Safety & Quality
- **Location**: PigeonWatch/Api/UnitTests/AdminRoleBootstrapperTests.cs:175
- **Detail**: `SqlQueryRaw<int>($"SELECT COUNT(*) AS Value FROM {tableName}")`. It cannot be exploited (callers pass the constants "ROLE" and "USER_ROLE", test code, in-memory SQLite), but it adds a build warning and differs from the literal SQL used in `PigeonWatchUserStoreTests` and `SightingRepositoryTests`.
- **Fix**: Replace it with two literal queries (one per table).
- **Decision**: FIXED — callers pass literal COUNT queries; the interpolated table name is gone and the EF1002 warning no longer appears

### F8 — Extra round trip per photo, thumbnail, comment edit and delete; access projection repeated in three repositories

- **Severity**: 💡 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Safety & Quality
- **Location**: PigeonWatch/Api/BusinessLogic/Services/SightingService.cs:188-191, 206-209; SightingRepository/SightingCommentRepository/SightingThreadRepository access lookups
- **Detail**: `GetAccessAsync` and `GetPhotoBlobNameAsync` are two queries on the same row, so a list page of 30 thumbnail rows costs up to 60 queries instead of 30. The `(ReporterId, RemovedAt)` projection exists in three repositories, and in `SightingThreadRepository` the coexistence of the owner and access lookups is what produced F1.
- **Fix**: Read `(ReporterId, RemovedAt, PhotoBlobName)` in one projection for the photo paths; leave the repository duplication as is (it follows the existing per-repository lookup pattern).
- **Decision**: FIXED — photo and thumbnail read access and blob name in one GetPhotoAccessAsync projection (new SightingPhotoAccess BO); GetPhotoBlobNameAsync, GetAccessAsync and CanViewAsync removed as unused; repository test for the projection; repository duplication left as is

### F9 — Unreachable failure branch in the remove and restore responses skips the house logging

- **Severity**: 💡 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Pattern Consistency
- **Location**: PigeonWatch/Api/WebApi/Controllers/SightingsController.cs:252
- **Detail**: The final `GetValidationProblem` branch in `GetRemovalResponseAsync` does not go through `loggedErrorCodes` / `LogInvalidInput` like Create and Update. The service returns only `NotAdmin` or `SightingNotFound` today, so the branch cannot be reached.
- **Fix**: Route it through the same logging as Update, so a future error code is not silent.
- **Decision**: FIXED — the fallback branch in GetRemovalResponseAsync logs loggedErrorCodes through LogInvalidInput like Create and Update

### F10 — Operational notes for rollback, S-11 and the bundle budget

- **Severity**: 💡 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Safety & Quality
- **Location**: PigeonWatch/Api/Data/Migrations/20261117135759_AddSightingRemoval.cs; AdminRoleBootstrapWorker; PigeonWatch/Frontend/angular.json
- **Detail**: Rolling back `AddSightingRemoval` drops the moderation state, so every removed sighting becomes public again. The `REMOVED_BY` Restrict FK means an admin who removed anything cannot be hard-deleted (relevant for S-11). The bootstrap runs once per start and is not retried after a cold-database failure. The initial bundle is 548.05 kB against the 550 kB warning, about 2 kB of headroom.
- **Fix**: Add the rollback and admin-deletion notes to `context/deployment/deploy-plan.md` and the S-11 roadmap item; no code change.
- **Decision**: FIXED — deploy-plan.md gains an "Admin role and sighting removal" section (app setting, no retry, rollback, REMOVED_BY restrict, revocation); the S-11 roadmap risk names the claim, the FK and the no-revoke rule; the 548.05 kB bundle is already recorded in Frontend/CLAUDE.md
