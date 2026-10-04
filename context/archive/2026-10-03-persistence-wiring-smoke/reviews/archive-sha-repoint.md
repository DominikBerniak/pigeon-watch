# Archive SHA repoint: persistence-wiring-smoke

## 2026-10-04

- **Target**: `origin/main` (remote default branch via `git ls-remote --symref origin HEAD`), snapshot `0012335cd70e3810b9d97e5cbe5b0b4d09017039`, freshly fetched; repository not shallow.
- **Problem**: all 9 distinct Progress SHAs resolved to commits that are not ancestors of the target (`git merge-base --is-ancestor` exit 1), because every PR was squash-merged.
- **Integration commits**:

| PR | Branch | Squash commit | Title |
|---|---|---|---|
| [#1](https://github.com/DominikBerniak/pigeon-watch/pull/1) | `feature/persistence-wiring-smoke` | `aed3289` | F-01: Persistence wiring smoke test (layered API, EF Core, CI migrate) (#1) |
| [#2](https://github.com/DominikBerniak/pigeon-watch/pull/2) | `fix/persistence-wiring-smoke-bundle-restore` | `954367c` | fix(persistence-wiring-smoke): Restore before migration bundle (p4) (#2) |
| [#3](https://github.com/DominikBerniak/pigeon-watch/pull/3) | `fix/persistence-wiring-smoke-clean-deploy` | `29b7155` | fix(persistence-wiring-smoke): Clean deploy and Node 24 actions (p4) (#3) |
| [#4](https://github.com/DominikBerniak/pigeon-watch/pull/4) | `chore/persistence-wiring-smoke-closeout` | `0012335` | F-01 close-out: live verification, implementation review fixes, toolkit update (#4) |

- **Evidence**:
  - Each old SHA appears in the commit list of exactly one merged PR (`gh pr view --json commits`).
  - For each PR, the squash commit's parent equals the merge base of the PR head, and the squash commit's tree is identical to the PR head's tree. So each squash commit integrates exactly that PR's full diff, including the listed commits. PR #1 changes 101 files, #2 changes 2, #3 changes 5 and #4 changes 33.
  - All four squash commits are ancestors of the target snapshot (exit 0).
- **Decision**: the user chose **Update and archive** in `/10x-archive`.

| Row IDs | Old suffix (resolved OID) | New SHA |
|---|---|---|
| 0.1–0.6 | `bb9952b` (bb9952bd62d56e6bbbc35ce1c0af7e7dc3c42bcd) | `aed3289` |
| 1.1–1.10 | `4448c4f` (4448c4fe765469f1671a4e0e1f5880d3870f7dbb) | `aed3289` |
| 2.1–2.5 | `8186b77` (8186b7723042fa37900c1312520ca871e4da4cc8) | `aed3289` |
| 3.1–3.5 | `9ec7e6f` (9ec7e6f8d41f915c48dd0ecb4824a3432e4ba3dd) | `aed3289` |
| 4.1–4.6 | `b612afe` (b612afe7a0808a151b4598200cc5cd7a9654deea) | `aed3289` |
| 4.8 | `f79819e` (f79819efc35e05449d2a202fb23b114a2625bdc3) | `aed3289` |
| 4.7, 4.9, 4.10 | `83c96c5` (83c96c5362b4c164261505e7ab59e2cc6bcec790) | `954367c` |
| 4.11 | `7a0719c` (7a0719c7b3e8bfbb7184388737a8e3a68622c1c7) | `29b7155` |
| 5.1–5.7 | `7c417ae` (7c417ae75a051fa16b6a96168ec8c17938e2bb7f) | `0012335` |

**Affected rows: 44** (every completed Progress row). This note records provenance only; it is neither an implementation review nor review coverage.
