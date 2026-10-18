# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

This directory holds the actual PigeonWatch product code (backend + frontend). The repo root `CLAUDE.md` is the 10xDevs course-toolkit file (skill chain, not project architecture) — this file is scoped to `PigeonWatch/` only.

## Hard rules

- A user's contact info must never be exposed directly to another user — messaging/contact flows must proxy through the product, not leak raw contact details (PRD guardrail; messaging does not exist yet). The public display name is what other users see: it rejects `@` so it cannot carry an email address, the email is returned only to its owner (`GET /configuration/general`), sighting responses carry the reporter's display name only (no reporter id, no email), and audit columns record the user id, not the email.
- The API never confirms that an email is registered. `POST /account/register` turns Identity's `DuplicateEmail`/`DuplicateUserName` into one generic `RegistrationFailed` error whose description does not name the email, and the SPA shows a generic form-level message for it. Display names are public, so `DuplicateDisplayName` stays specific. Without email confirmation, a registration that fails for no visible reason can still hint at an existing account, so the per-IP rate limit and per-account lockout remain what makes probing slow. Login keeps the `LockedOut` detail (accepted).

## What this project is

PigeonWatch is a map-based coordination app for a city pigeon/bird rescue community: users report distressed-bird sightings (location, description, photo, criticality), track a status lifecycle (spotted → caught → at the vet / in home care → awaiting adoption → adopted, released, passed away or not found; the exact transitions are in the PRD Business Logic section), see confirmed flock locations as a separate map layer, and message each other about a sighting. Full requirements: `@context/foundation/prd.md`. Stack rationale: `@context/foundation/tech-stack.md`.

Slices S-01 (register and log in), S-02 (edit profile name) and S-03 (report a sighting and see it on the map) are built; slice status lives in `@context/foundation/roadmap.md`. The API has ASP.NET Core Identity bearer-token auth over its own `USER_ACCOUNT` table plus one domain table, `SIGHTING`. Its surface is `POST /account/register`, `PUT /account/profile`, `PUT /account/password`, `POST /auth/login`, `POST /auth/refresh`, `GET /configuration/client` (anonymous validation and sighting rules), `GET /configuration/general` (the current user), `GET /resources/{culture}` (UI labels), and the authorized `POST /sightings`, `GET /sightings/{id}`, `GET /sightings/nearby` and `GET /sightings/mine`, guarded by architecture and unit tests. Sighting business logic lives in `Api/BusinessLogic`: a new sighting starts as Spotted or Caught, the full PRD status transition graph is encoded and unit-tested but no endpoint changes a status yet, a user can report at most one sighting per 60 seconds, and "nearby" means open (unresolved) sightings within 10 km of a point. The F-01 database smoke probe is gone; the post-deploy probe calls `GET /configuration/client`, which does not touch the database. The SPA has login, registration, a header user menu, a profile page with the user's own sightings, the nearby map as the home page, a report form with a map pin-picker, a sighting detail page with an opt-in map, and a warm-up panel for cold starts, with every label from the API's `UiLabels.resx`.

## Layout

- `Api/` — ASP.NET Core Web API (.NET 10), solution `Api/PigeonWatchApi.slnx` with six layered projects plus architecture, unit and integration tests:
  - `WebApi.Host/` — the deployed host and EF startup project (`Program.cs`, appsettings, pipeline, rate limiter); calls only `AddPigeonWatch` and `MapPigeonWatch` for application code.
  - `WebApi/` — controllers, API models, view model creators, mappers.
  - `DependencyInjection/` — the only place services are registered.
  - `BusinessLogic/` — services and providers.
  - `Data/` — DbContext, internal EF entities, repositories, entity-to-BO mappers, migrations.
  - `BusinessObjects/` — plain domain and result types.
  - `ArchitectureTests/` — tests that enforce the layer and coding rules.
  - `UnitTests/` — xUnit unit tests (NSubstitute mocks, SQLite in-memory for the user store).
  - `IntegrationTests/` — xUnit tests that drive the real HTTP pipeline through `WebApplicationFactory<Program>` on SQLite in-memory.

  The layer rules, reference direction, coding rules and commands live in `@PigeonWatch/Api/CLAUDE.md`; read it before changing anything under `Api/`.
- `Frontend/` — Angular 22 app on Angular Material, styled through `--pw-*` design tokens, `pw-*` common classes and reusable `shared/ui` components. See `@PigeonWatch/Frontend/CLAUDE.md` for the UI rules, commands and specifics.

There is no bundled .NET+Angular starter — the two are separate projects that talk over HTTP; the frontend consumes the API, they don't share a build or a repo-root package manifest.

## Notes for future work

- Deployment target is Azure App Service free (F1) tier — cost-conscious by design (sleeps on idle, ~60 min/day compute cap). CI is GitHub Actions with auto-deploy-on-merge. See `@context/foundation/tech-stack.md` for the full rationale and the Fly.io fallback if F1 limits bite.
- Map-provider API usage is meant to be cost-conscious — avoid rendering an embedded interactive map by default on pages like the bird detail page. The map is Leaflet over OpenStreetMap's public tile server (the one external host the CSP admits); the detail page shows coordinates as text and loads tiles only behind its "Show map" toggle. Keep that pattern for new pages.
