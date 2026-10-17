# Archive SHA repoint: nearby-map-review-fixes

- **Date**: 2026-10-17
- **Target**: `origin` (https://github.com/DominikBerniak/pigeon-watch.git), branch `main`, snapshot `f55253a508796f91a2bb3c4ba86626e3a01f8f29`
- **Integration commit**: `f55253a` "Nearby map review fixes: stable toolbar and no results over a failed map (#12)"
- **PR**: https://github.com/DominikBerniak/pigeon-watch/pull/12 (MERGED 2026-10-17, base `main`, squash: single-parent commit)
- **Evidence**: the PR commit list contains 6759461, 6e828e2, 8bac9c1, 26b7ed2, b46eaa1 and f5b843f, cherry-picks of the branch commits onto a fresh branch from `origin/main` (the old branch had been squash-merged as PR #11). The three old SHAs are not in that list, but each has the same `git patch-id --stable` as its cherry-picked counterpart (e841560 = 6e828e2, 21ceaaf = 8bac9c1, 28e6477 = 26b7ed2). The product diff of `f55253a` over `f0a9c8e` has the same stat as the old branch tip `5e6f1a8` over `f0a9c8e` (13 files under `PigeonWatch/`, 280 insertions, 55 deletions). All three old SHAs resolve locally and are not ancestors of the target snapshot; `f55253a` is an ancestor of it (`git merge-base --is-ancestor` exit 0).
- **Decision**: user chose "Update and archive" in `/10x-archive` on 2026-10-17.
- **Affected rows**: 21

| Row ID | Old suffix (resolved OID) | New SHA |
|---|---|---|
| 1.1-1.6 | e841560 (e84156058a4a077057964b217604d514076aadd4) | f55253a |
| 2.1-2.9 | 21ceaaf (21ceaaf5a746b0a420be5950096a72877598b7fb) | f55253a |
| 3.1-3.6 | 28e6477 (28e6477319e6cf6b3b59dd359fcf1dd94815205b) | f55253a |
