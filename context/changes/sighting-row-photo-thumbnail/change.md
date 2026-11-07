---
change_id: sighting-row-photo-thumbnail
title: Real photo thumbnails in sighting list rows
status: impl_reviewed
created: 2026-11-04
updated: 2026-11-07
archived_at: null
---

## Notes

Show a real photo thumbnail in sighting list rows (sighting list and My sightings) instead of only the camera icon. Decided by the user: the thumbnail is created at upload time (SkiaSharp, ~160 px square centre crop, modest JPEG quality) and stored through ISightingPhotoStore as <guid>-thumb.jpg. Photos uploaded before this change get theirs on first request of GET sightings/{id}/photo/thumbnail and it is stored, so no backfill job. Per-request resizing was rejected (F1 CPU budget, private no-store responses cannot be cached).

Recorded after /10x-impl-review (2026-11-07, F6):

- **Kitchen-sink cell name.** The pending thumbnail cell is `list-thumbnails-loading` (in `cell-definitions.ts`, `kitchen-sink.spec.ts` and Frontend CLAUDE.md), not `list-thumbnails-pending` as the plan says. Its stub mode is still `thumbnail: 'pending'`.
