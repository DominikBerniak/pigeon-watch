---
change_id: attach-sighting-photo
title: Attach sighting photo
status: implemented
created: 2026-11-03
updated: 2026-11-07
archived_at: null
---

## Notes

<!-- Free-form notes for this change: links, ad-hoc context, decisions that don't belong in research/frame/plan. -->

Decisions taken during /10x-implement that supersede plan text (2026-11-03):

- **Error codes are PascalCase**, matching the existing `SightingErrorCodes`: `PhotoTooLarge`, `PhotoUnsupportedType`, `PhotoUnreadable`, `PhotoStorageUnavailable`. The plan's camelCase spellings are superseded; the SPA (Phase 4) maps these names, and the warm-up exclusion matches `PhotoStorageUnavailable`.
- **No 413 contract.** A body over `SightingRules.PhotoRequestMaxBytes` returns 400 `ValidationProblemDetails` keyed `PhotoTooLarge` (MVC form binding swallows the overflow into ModelState). The SPA maps only `PhotoTooLarge`; the plan's "413 → photo too large" mapping is dropped. A chunked upload without Content-Length still gets the generic 400.
- **Invariant-culture form binding.** `WebApi/ModelBinding/InvariantCultureFormValueProviderFactory` replaces `FormValueProviderFactory`, because form values bind in the server culture (pl-PL rejected `52.2297`). `CancellationTokenTests` carries one pinned allow-list entry for `IValueProviderFactory.CreateValueProviderAsync`.
- **Empty photo part = no photo.** `PhotoAllowedContentTypes` is a comma-separated const, served to the SPA as an array.
- **Pkcs pin.** `System.Security.Cryptography.Pkcs` 10.0.13 is pinned in Data to clear a pre-existing transitive advisory from SqlClient.
