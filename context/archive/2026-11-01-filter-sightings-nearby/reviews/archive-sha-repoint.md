# Archive SHA repoint

## 2026-11-01

- **Target**: `origin/main` @ `0bc71b1fb93ec02f331da126ad5e5dd4d59a43e0`
- **Integration commit**: `0bc71b1` Filter sightings nearby by status and criticality (S-05) (#28)
- **PR**: https://github.com/DominikBerniak/pigeon-watch/pull/28 (merged, base `main`, squash)
- **Evidence**: the PR commit list contains `720dbb8`, `97ea492`, `2bd493e`, `cc19de1`, `ca4725c` and `cdd8476`; none of the four Progress SHAs is an ancestor of the target (`git merge-base --is-ancestor` exit 1); the tree of `0bc71b1` is identical to the PR head `cdd8476` (`git diff --quiet` clean), so the squash integrates all four phases.
- **Decision**: the user chose "Update and archive" during `/10x-archive filter-sightings-nearby`.

| Row ID | Old suffix (resolved OID) | New SHA |
|---|---|---|
| 1.1–1.6 | `720dbb8` (720dbb86064f1cb943d4569c567fe26695eec50e) | `0bc71b1` |
| 2.1–2.2 | `97ea492` (97ea49222c044ed76eff72246dd68808328d46bf) | `0bc71b1` |
| 3.1–3.6 | `2bd493e` (2bd493e82dcb55ddabeaec0618ed6ef58a5e306b) | `0bc71b1` |
| 4.1–4.3 | `cc19de1` (cc19de113651a1bf6f929715120be6fdfe584730) | `0bc71b1` |

Rows repointed: 17.
