# Archive SHA repoint

## 2026-11-07

- **Target**: `origin/main` (base of PR #31 and #32; remote default branch confirmed with `git ls-remote --symref origin HEAD`), snapshot `b1b5a909547c859a8014b6519c929543c9646500`.
- **Why**: both implementation PRs were squash-merged, so the per-phase commits in Progress are not in `main` history.
- **Integration commits**:
  - `6c1715f` "Attach a photo to a sighting, with list thumbnails (S-04) (#31)", https://github.com/DominikBerniak/pigeon-watch/pull/31. Its commit list contains the p1–p5 commits, and its diff adds the photo store seam, normalization provider, `AddSightingPhoto` migration, API create and read, and the SPA picker, encoder and detail photo.
  - `b1b5a90` "Apply implementation review fixes for attach-sighting-photo (S-04) (#32)", https://github.com/DominikBerniak/pigeon-watch/pull/32. Its commit list contains the p6 commit, and its diff carries that commit's Progress and deployment-doc updates.
- **Decision**: the user chose "Update and archive" in `/10x-archive`.

| Row ID | Old suffix (resolved OID) | New SHA |
|---|---|---|
| 1.1–1.5 (5 rows) | `75cba79` (`75cba79d7f05c23281a17fac48f9b0448c1c9dfc`) | `6c1715f` |
| 2.1–2.2 (2 rows) | `3381194` (`33811943b179c044abb4cd7d1148513c5b09d7fb`) | `6c1715f` |
| 3.1–3.6 (6 rows) | `2d25b9a` (`2d25b9af9addaea2f371250d23a80eb0e76ea166`) | `6c1715f` |
| 4.1–4.4 (4 rows) | `0ed0136` (`0ed0136cab8a7a5308423058a8d9662501661c25`) | `6c1715f` |
| 5.1–5.5 (5 rows) | `53d7e07` (`53d7e07b70c2286735045c548eb3b802345087a5`) | `6c1715f` |
| 6.1–6.6 (6 rows) | `818a8ab` (`818a8ab5520e48e7fce5ad3571d43bb137b6aa8f`) | `b1b5a90` |

Affected rows: 28.
