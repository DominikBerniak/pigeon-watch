# Review follow-ups: admin-remove-sightings

Queued from `reviews/impl-review.md` during triage on 2026-11-24.

## F5 — Kitchen-sink spec time grows with every cell

- **Where**: `PigeonWatch/Frontend/src/app/dev/kitchen-sink/kitchen-sink.spec.ts` (`beforeEach` renders the whole sink per test; `sinkTestTimeout` 15 s, `sinkRenderTimeout` 30 s).
- **Evidence**: with the .NET builds running in parallel, "shows the card of the selected marker" and "shows the no-match text when the chosen filter matches nothing" hit "Test timed out in 15000ms"; the spec alone passes 103/103 in about 409–521 s. Phase 4 already needed the separate 30 s cold-render budget. During the review-fix commit the pre-commit hook then timed out three different map tests across two runs, so `sinkTestTimeout` was raised from 15 s to 30 s as a stopgap (F5 Fix B).
- **Follow-up**: render only the cells a test needs (for example a TestBed host per cell or per view group, or splitting the spec by `group`), so a new cell stops slowing all 100+ tests. Measure how much of the per-test cost is the render versus jsdom setup first.
- **Done when**: the kitchen-sink spec's per-test time no longer grows with the number of cells, and the full frontend suite passes under the parallel .NET build load that triggered the timeouts.
