---
change_id: sighting-row-photo-thumbnail
title: Real photo thumbnails in sighting list rows
status: implementing
created: 2026-11-04
updated: 2026-11-04
archived_at: null
---

## Notes

Show a real photo thumbnail in sighting list rows (sighting list and My sightings) instead of only the camera icon. Decided by the user: the thumbnail is created at upload time (SkiaSharp, ~160 px square centre crop, modest JPEG quality) and stored through ISightingPhotoStore as <guid>-thumb.jpg. Photos uploaded before this change get theirs on first request of GET sightings/{id}/photo/thumbnail and it is stored, so no backfill job. Per-request resizing was rejected (F1 CPU budget, private no-store responses cannot be cached).
