# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

This directory holds the actual PigeonWatch product code (backend + frontend). The repo root `CLAUDE.md` is the 10xDevs course-toolkit file (skill chain, not project architecture) — this file is scoped to `PigeonWatch/` only.

## Hard rules

- A user's contact info must never be exposed directly to another user — messaging/contact flows must proxy through the product, not leak raw contact details (PRD guardrail; messaging does not exist yet). The public display name is what other users see: it rejects `@` so it cannot carry an email address, the email is returned only to its owner (`GET /configuration/general`), and audit columns record the user id, not the email.
- The API never confirms that an email is registered. `POST /account/register` turns Identity's `DuplicateEmail`/`DuplicateUserName` into one generic `RegistrationFailed` error whose description does not name the email, and the SPA shows a generic form-level message for it. Display names are public, so `DuplicateDisplayName` stays specific. Without email confirmation, a registration that fails for no visible reason can still hint at an existing account, so the per-IP rate limit and per-account lockout remain what makes probing slow. Login keeps the `LockedOut` detail (accepted).

## What this project is

PigeonWatch is a map-based coordination app for a city pigeon/bird rescue community: users report distressed-bird sightings (location, description, photo, criticality), track a status lifecycle (spotted → contacted → taken to vet → healed/returned), see confirmed flock locations as a separate map layer, and message each other about a sighting. Full requirements: `@context/foundation/prd.md`. Stack rationale: `@context/foundation/tech-stack.md`.

Slice S-01 (register and log in) is in progress. The API has ASP.NET Core Identity bearer-token auth over its own `USER_ACCOUNT` table, and its surface is `POST /account/register`, `POST /auth/login`, `POST /auth/refresh`, `GET /configuration/client` (anonymous validation rules), `GET /configuration/general` (the current user), and `GET /resources/{culture}` (UI labels), guarded by architecture and unit tests. The F-01 database smoke probe is gone; the post-deploy probe calls `GET /configuration/client`, which does not touch the database. The frontend is still a placeholder shell showing the app name and a router outlet until the S-01 SPA phases land. No sighting business logic exists yet.

## Layout

- `Api/` — ASP.NET Core Web API (.NET 10), solution `Api/PigeonWatchApi.slnx` with six layered projects plus architecture and unit tests:
  - `WebApi.Host/` — the deployed host and EF startup project (`Program.cs`, appsettings, pipeline, rate limiter); calls only `AddPigeonWatch` and `MapPigeonWatch` for application code.
  - `WebApi/` — controllers, API models, view model creators, mappers.
  - `DependencyInjection/` — the only place services are registered.
  - `BusinessLogic/` — services and providers.
  - `Data/` — DbContext, internal EF entities, repositories, entity-to-BO mappers, migrations.
  - `BusinessObjects/` — plain domain and result types.
  - `ArchitectureTests/` — tests that enforce the layer and coding rules.
  - `UnitTests/` — xUnit unit tests (NSubstitute mocks, SQLite in-memory for the user store).

  The layer rules, reference direction, coding rules and commands live in `@PigeonWatch/Api/CLAUDE.md`; read it before changing anything under `Api/`.
- `Frontend/` — Angular 22 app on Angular Material, styled through `--pw-*` design tokens, `pw-*` common classes and reusable `shared/ui` components. See `@PigeonWatch/Frontend/CLAUDE.md` for the UI rules, commands and specifics.

There is no bundled .NET+Angular starter — the two are separate projects that talk over HTTP; the frontend consumes the API, they don't share a build or a repo-root package manifest.

## Notes for future work

- Deployment target is Azure App Service free (F1) tier — cost-conscious by design (sleeps on idle, ~60 min/day compute cap). CI is GitHub Actions with auto-deploy-on-merge. See `@context/foundation/tech-stack.md` for the full rationale and the Fly.io fallback if F1 limits bite.
- Map-provider API usage is meant to be cost-conscious — avoid rendering an embedded interactive map by default on pages like the bird detail page.
