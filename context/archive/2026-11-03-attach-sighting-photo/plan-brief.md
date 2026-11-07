# Attach Sighting Photo — Plan Brief

> Full plan: `context/changes/attach-sighting-photo/plan.md`
> Research: `context/changes/attach-sighting-photo/research.md`

## What & Why

Roadmap slice S-04 (FR-005): a rescuer can attach one optional photo when reporting a distressed pigeon, so other members can recognise the bird. It is the app's first use of file storage. The photo must never leak the reporter's location data, and reporting without a photo must keep working as it does today.

## Starting Point

`POST /sightings` takes JSON. Nothing in the API or SPA handles files. The Blob account, the private `sighting-photos` container, the managed-identity role and the app settings were already set up during deployment. The SPA's security policy (CSP) blocks every image source that photos would need.

## Desired End State

The report form has a photo picker that shrinks the photo and shows a preview. The photo is sent with the create request, re-encoded on the server without metadata, and stored in Blob Storage. Logged-in members see it on the detail page. List rows show a camera icon. A bad photo gets a clear field error. A storage outage keeps the form filled and says to retry or remove the photo.

## Key Decisions Made

| Decision | Choice | Why (1 sentence) | Source |
| --- | --- | --- | --- |
| Storage | Azure Blob, private container, `DefaultAzureCredential` | Already provisioned; the deploy plan forbids key-based access. | Research |
| Serving | `[Authorize] GET /sightings/{id}/photo`, SPA shows a `blob:` URL | Enforces members-only viewing on every request; only `blob:` is added to the CSP. | Plan |
| Upload timing | One multipart `POST /sightings` | One atomic outcome for the user; the cooldown covers the photo automatically. | Plan |
| EXIF / processing | Re-encode in the browser (≤1600 px JPEG) and on the server (SkiaSharp) | Small uploads on F1 plus a guarantee that holds for any client. | Plan |
| Limits | 1 photo, 2 MB, JPEG/PNG/WebP checked from the file's bytes, ≤40 MP | Matches "attach a photo"; caps abuse and memory use. | Plan |
| Storage failure | Whole create fails with 503 + Retry-After; blob deleted if the insert fails | No half-saved sightings; mirrors the database-unavailable 503. | Plan |
| Image library | SkiaSharp + Linux NoDependencies native assets | MIT licence, low CPU; ImageSharp's licence needs checking for a company. | Plan |
| Display | Detail-page photo + camera icon in rows | One photo request per view keeps F1 bandwidth low. | Plan |
| Local dev / tests | Development-only local folder; in-memory fake in integration tests | No extra tooling or CI services; production can't silently fall back. | Plan |
| Cooldown | No separate rule | The photo is part of create, so the existing 60 s cooldown applies. | Plan |

## Scope

**In scope:**
- storage seam and start-up validation of its settings;
- server normalization;
- multipart create;
- photo download endpoint;
- `hasPhoto` on responses;
- photo limits in the client configuration;
- SPA picker with browser re-encode;
- error mapping and the warm-up exclusion;
- CSP `blob:`;
- detail photo and row icon;
- kitchen-sink cells and one Playwright spec;
- live checks on Azure.

**Out of scope:**
- more than one photo, or thumbnails;
- editing or deleting a photo (S-09 / S-10);
- moderation;
- SAS links or a public container;
- Azurite;
- saving a sighting without its photo when storage fails;
- retrying cleanup of orphaned blobs.

## Architecture / Approach

The SPA re-encodes the photo with a canvas and posts FormData. The controller binds `IFormFile` and maps it to a stream for the business layer. `SightingService` runs: validate → cooldown → `PhotoNormalizationProvider` (SkiaSharp) → `ISightingPhotoStore.SaveAsync` → repository insert with `PHOTO_BLOB_NAME`, deleting the blob if the insert fails. `ISightingPhotoStore` has an Azure implementation (managed identity) and a Development-only local-folder implementation, chosen by `BlobStorageOptions`; tests use an in-memory fake. Storage failures become `PhotoStorageUnavailableException`, which a handler returns as 503. `GET .../photo` streams the JPEG to the SPA, which fetches it as a Blob.

## Phases at a Glance

| Phase | What it delivers | Key risk |
| --- | --- | --- |
| 1. Photo storage seam | Store interface, Azure + local stores, options validation, 503 handler | First options class; architecture-test configuration must follow |
| 2. Server normalization (TDD) | SkiaSharp provider + rules + error codes | Native library and codec edge cases (orientation, crafted headers) |
| 3. API create and read | Migration, multipart create, GET photo, `hasPhoto`, fixtures, integration tests | Every JSON create caller and fixture must move to multipart |
| 4. SPA reporting | Picker, browser re-encode, FormData, error mapping, warm-up exclusion, CSP | Canvas decode varies by browser (HEIC) |
| 5. SPA display | Detail photo states, row icon, kitchen sink, Playwright | Initial bundle budget (~12 kB headroom) |
| 6. Live verification | Managed-identity upload, CSP, native library on Linux F1 | Only testable after deploy |

**Prerequisites:** S-03 done (it is). The storage account and role assignment exist. `BlobStorage:LocalDirectory` is added to `appsettings.Development.json`, by the user if the agent can't write it.
**Estimated effort:** ~4–5 sessions across 6 phases (phase 6 is a short manual pass).

## Open Risks & Assumptions

- SkiaSharp's native library has to load on Linux F1. It is only provable after deploy, and a failure would break photo creates (500), not reports without a photo.
- A body larger than the request limit gets a 413 from the server before the controller runs. The SPA maps that status itself.
- A failed compensating delete leaves an orphaned blob. It is logged, not retried.
- A warm-up resend of a multipart create after a dropped connection can still produce the duplicate-or-cooldown behaviour that JSON create already has.

## Success Criteria (Summary)

- A member can report with or without a photo, and other members see the photo on the detail page and a camera icon in lists.
- Stored photos carry no EXIF data, are at most 1600 px, and can't be read by anyone who isn't logged in.
- A bad photo or a storage outage never loses what the user typed and never leaves a half-saved sighting.
