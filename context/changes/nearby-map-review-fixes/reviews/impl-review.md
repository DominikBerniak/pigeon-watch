<!-- IMPL-REVIEW-REPORT -->
# Implementation Review: Nearby map review fixes

- **Plan**: context/changes/nearby-map-review-fixes/plan.md
- **Scope**: Full plan (3 of 3 phases)
- **Reviewed phases**: 1, 2, 3
- **Date**: 2026-10-17
- **Verdict**: APPROVED
- **Findings**: 0 critical, 2 warnings, 3 observations

## Verdicts

| Dimension | Verdict |
|-----------|---------|
| Plan Adherence | PASS |
| Scope Discipline | PASS |
| Safety & Quality | WARNING |
| Architecture | PASS |
| Pattern Consistency | PASS |
| Success Criteria | WARNING |

Automated criteria re-run at review time: `npm test -- --watch=false` PASS (43 files, 478 tests), `npm run build` PASS, `dotnet build PigeonWatch/Api/PigeonWatchApi.slnx` PASS (0 warnings, 0 errors). Not reviewed as findings: pre-existing double-Retry race in `map-view.ts` `load()` (no in-flight guard), and `loadFailed` being a state-carrying output named like an event; neither is introduced by this change.

## Findings

### F1 — Search described by an empty hidden element when the map failed after a nearby error

- **Severity**: ⚠️ WARNING
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Safety & Quality
- **Location**: PigeonWatch/Frontend/src/app/features/sightings/nearby-map-page/nearby-map-page.html:14
- **Detail**: With `mapFailed()` true and the nearby state `error`, `status()` is `'error'` and `searchDisabled()` is true, so `status() !== 'none'` passes and Search gets `aria-describedby` pointing at the `role="status"` element, which is empty and visually hidden in the error state. This is the case the plan's rule ("a disabled Search never describes an empty element") was written to prevent; the plan's literal condition misses it. Before the change this state could not occur (`zoomedOut`, `locating` and `loading` took precedence over `error`). No spec asserts `aria-describedby` in this state.
- **Fix**: Exclude `error` from the describedby condition (a small computed, e.g. `describesSearch = searchDisabled() && status() !== 'none' && status() !== 'error'`) and add the `aria-describedby` assertion to the spec "keeps the nearby error alert and its retry while the map failed".
- **Decision**: FIXED — Fix now (`describesSearch` computed excludes `none` and `error`; spec asserts no `aria-describedby` while the map failed after a nearby error)

### F2 — Progress 3.5 is ticked although no screen reader was run

- **Severity**: ⚠️ WARNING
- **Impact**: 🔎 MEDIUM — real tradeoff; pause to reason through it
- **Dimension**: Success Criteria
- **Location**: context/changes/nearby-map-review-fixes/plan.md (Progress, 3.5); context/changes/nearby-map-review-fixes/visual-notes.md ("Method", "Not verified")
- **Detail**: 3.5 ("A screen reader announces the status text changes and the in-slot error alert once each, and focus lands where the page code puts it") is ticked, but `visual-notes.md` says no screen reader was run and keeps it as a human checklist. Keyboard focus moves were measured; announcements were not. The row was ticked after the user answered "It's good" to a message that listed 3.5 as a human step, so it was a blanket confirmation with no recorded findings.
- **Fix A ⭐ Recommended**: Keep 3.5 ticked and amend `visual-notes.md` ("Method" and "Not verified") to say the screen-reader pass was confirmed by the user on 2026-10-17 without a logged transcript, so the notes match the plan.
  - Strength: Reflects what actually happened; the user did confirm the item.
  - Tradeoff: The evidence for announcements is the user's word only.
  - Confidence: MEDIUM — depends on whether the user in fact ran a screen reader.
  - Blind spot: Whether "It's good" covered 3.5 or only the visual items.
- **Fix B**: Untick 3.5 in Progress (and leave the change at `implemented`) until a screen-reader pass is run and logged.
  - Strength: Keeps the Progress section strictly evidence-based.
  - Tradeoff: Leaves an open manual row that `/10x-archive` will warn about.
  - Confidence: HIGH — nothing contradicts the notes.
  - Blind spot: None significant.
- **Decision**: FIXED — Fix A (3.5 stays ticked; visual-notes.md amended to record the user confirmation on 2026-10-17 without a logged transcript)

### F3 — Status texts return between a retry start and a second failure

- **Severity**: 💡 OBSERVATION
- **Impact**: 🔎 MEDIUM — real tradeoff; pause to reason through it
- **Dimension**: Safety & Quality
- **Location**: PigeonWatch/Frontend/src/app/shared/map/map-view/map-view.ts:126-133
- **Detail**: `loadFailed(false)` is emitted when Retry starts, so the page restores the result, zoom and hint statuses and enables Search while the map is neither loaded nor failed again. If the retry fails, the `role="status"` live region can announce the count, blank, then the count again. Acceptable as the plan defines the contract ("false when a retry starts").
- **Fix**: Accept as is; revisit only if retry flicker shows up in use (option: treat `false` as still-unavailable until `mapReady`).
- **Decision**: SKIPPED — accepted as planned (retry-start contract kept)

### F4 — Sink spec and map-view spec miss two cheap assertions

- **Severity**: 💡 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Pattern Consistency
- **Location**: PigeonWatch/Frontend/src/app/dev/kitchen-sink/kitchen-sink.spec.ts; PigeonWatch/Frontend/src/app/shared/map/map-view/map-view.spec.ts
- **Detail**: Both map-failed sink cells assert only that the loader rejects; `Frontend/CLAUDE.md` asks for a marker assertion per new state, and `mapOf(id).loadFailed.emit(true)` works on the stub. The map-view spec has no case for a retry that fails again (`[true, false, true]`). The page spec already covers the suppression, so this is a depth gap, not a hole.
- **Fix**: In the `map-load-failed-unknown-position` sink test emit `loadFailed(true)` and assert the empty status and disabled Search; add a `[true, false, true]` case to `map-view.spec.ts`.
- **Decision**: FIXED — Fix now (sink spec emits loadFailed(true) on map-load-failed-unknown-position and asserts empty status, disabled Search, no aria-describedby; map-view spec adds the [true, false, true] retry-fails-again case)

### F5 — "Try again" still searches while the map failed

- **Severity**: 💡 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Safety & Quality
- **Location**: PigeonWatch/Frontend/src/app/features/sightings/nearby-map-page/nearby-map-page.ts:229-234
- **Detail**: `retry()` calls `search()` without checking `searchDisabled()`, so Try again works while Search is disabled because the map failed; results stay hidden. This matches the plan ("the nearby error remains a true statement") and the documented behaviour.
- **Fix**: Accept as is.
- **Decision**: SKIPPED — accepted (documented behaviour)
