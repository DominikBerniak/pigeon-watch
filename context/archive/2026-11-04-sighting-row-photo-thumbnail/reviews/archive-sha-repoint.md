# Archive SHA repoint

## 2026-11-07

- **Target**: `origin/main` (base of PR #31; remote default branch confirmed with `git ls-remote --symref origin HEAD`), snapshot `48a84572c9fec8c0b66c20ea5c808fd65541dfeb`.
- **Why**: PR #31 was squash-merged, so the per-phase commits in Progress are not in `main` history.
- **Integration commit**: `6c1715f` "Attach a photo to a sighting, with list thumbnails (S-04) (#31)", https://github.com/DominikBerniak/pigeon-watch/pull/31. Its commit list contains `3579f15`, `06c9213` and `cd05f24`. Its diff adds `PhotoThumbnailProvider` and its tests, the `photo/thumbnail` endpoint in `SightingsController`, the `EndpointFixtureRegistry` entry and the lazy thumbnail in `sighting-row.ts`.
- **Decision**: the user chose "Update and archive" in `/10x-archive`.

| Row ID | Old suffix (resolved OID) | New SHA |
|---|---|---|
| 1.1–1.2 (2 rows) | `3579f15` (`3579f1562ec206e5358fb383587a9fef11c1d612`) | `6c1715f` |
| 2.1–2.4 (4 rows) | `06c9213` (`06c92131d9b2945328ff660fcb07195f6805a8bf`) | `6c1715f` |
| 3.1–3.4 (4 rows) | `cd05f24` (`cd05f243b2c5da82f8fcfb9e28a5e469d1a25a75`) | `6c1715f` |

Affected rows: 10.
