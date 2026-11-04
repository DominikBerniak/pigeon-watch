# Sighting row photo thumbnail — Plan Brief

> Full plan: `context/changes/sighting-row-photo-thumbnail/plan.md`
> Research: `context/changes/sighting-row-photo-thumbnail/research.md`

## What & Why

The sighting list and My sightings rows only show a camera icon when a sighting has a photo. Members want to recognise the bird at a glance, so each photo row should show a real thumbnail. The App Service is F1 (about 60 CPU-minutes a day) and the photo responses are private and no-store, so the thumbnail must not be resized on every request.

## Starting Point

`attach-sighting-photo` stores one normalized `<guid>.jpg` per sighting and streams it from `GET sightings/{id}/photo`. The SPA renders blobs through `PhotoEncoder` object URLs, and the row shows an inline camera glyph.

## Desired End State

An upload stores the photo and a 160 px square thumbnail. A new endpoint streams the thumbnail. When a thumbnail is missing (photos uploaded before this change), the endpoint creates and stores it on first request. List rows with a photo show a 3rem thumbnail square that loads once the row scrolls into view, with the camera icon as the placeholder and the error fallback.

## Key Decisions Made

| Decision | Choice | Why (1 sentence) | Source |
| --- | --- | --- | --- |
| When thumbnails are made | At upload; missing ones are created on first request and stored | Avoids per-request CPU on F1, and needs no backfill job | User brief |
| Size and quality | 160 px square centre crop, never upscaled, JPEG quality 70 | Sharp at 3rem up to 3x DPR, and small | User brief / Plan |
| Storage name | `<guid>-thumb.jpg`, derived from `PhotoBlobName` | No migration | User brief |
| Thumbnail save failure at upload | 503 `PhotoStorageUnavailable`, both blobs deleted, no row | Same as any storage outage | User brief |
| Save failure on create-on-miss | Logged at Error with the cause, bytes still returned | The viewer still sees the image | User brief |
| Row layout | 3rem square at the start of photo rows only | Recognisable thumbnail with no layout shift; rows without a photo stay compact | Plan (user chose) |
| Lazy loading | `IntersectionObserver` once per row, immediate when undefined | Fetch only visible rows; jsdom-safe | User brief |

## Scope

**In scope:** thumbnail provider, create-time storage with compensation, the thumbnail endpoint with create-on-miss, store name validation, unit, integration and sweep coverage, SPA client and row, kitchen sink, e2e update, both CLAUDE.md files.

**Out of scope:** a backfill job, a migration, HTTP caching, and thumbnails on the map or the detail page.

## Architecture / Approach

`CreateAsync`: normalize → thumbnail → save the full photo → `try { save the thumbnail; insert } finally { on failure, delete both }`. `OpenPhotoThumbnailAsync`: blob name → stored thumbnail, or full photo → create → try to save → bytes. In the SPA, `SightingRow` combines visibility and `hasPhoto` into a `switchMap` over `photoThumbnail(id)`, producing an object URL that is revoked in the teardown.

## Phases at a Glance

| Phase | What it delivers | Key risk |
| --- | --- | --- |
| 1. Thumbnail provider (test-first) | `PhotoThumbnailProvider` with crop and size tests | SkiaSharp crop math |
| 2. API create and thumbnail endpoint | Both blobs at upload, the new endpoint, tests, sweeps, docs | Compensation scope placement |
| 3. SPA lazy row thumbnail | Lazy thumbnail in rows, kitchen sink, e2e, docs, bundle check | The row now injects services, which every host spec must satisfy |

**Prerequisites:** `attach-sighting-photo` Phases 1–5 on local `main` (53d7e07).
**Estimated effort:** about 1 session across 3 phases.

## Open Risks & Assumptions

- Two concurrent first requests may both regenerate the same thumbnail. This is accepted: the write is an idempotent overwrite.
- The thumbnail endpoint, like the photo endpoint, is open to any logged-in member.

## Success Criteria (Summary)

- New uploads store a thumbnail, and old photos get one on first view.
- Both lists render thumbnails lazily, without layout shift.
- API, unit, integration and frontend tests cover these paths, and the build stays within the bundle budget.
