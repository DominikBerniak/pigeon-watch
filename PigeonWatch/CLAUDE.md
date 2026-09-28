# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

This directory holds the actual PigeonWatch product code (backend + frontend). The repo root `CLAUDE.md` is the 10xDevs course-toolkit file (skill chain, not project architecture) — this file is scoped to `PigeonWatch/` only.

## Hard rules

- A user's contact info must never be exposed directly to another user — messaging/contact flows must proxy through the product, not leak raw contact details (PRD guardrail; not yet enforced in code, since no auth/messaging exists yet).

## What this project is

PigeonWatch is a map-based coordination app for a city pigeon/bird rescue community: users report distressed-bird sightings (location, description, photo, criticality), track a status lifecycle (spotted → contacted → taken to vet → healed/returned), see confirmed flock locations as a separate map layer, and message each other about a sighting. Full requirements: `@context/foundation/prd.md`. Stack rationale: `@context/foundation/tech-stack.md`.

Both projects are currently at their default CLI-scaffold state (no business logic, no auth, no tests yet) — this is the starting point for implementation, not a partially-built feature.

## Layout

- `Api/` — ASP.NET Core Web API (.NET 10). See `@PigeonWatch/Api/CLAUDE.md` for commands and specifics.
- `Frontend/` — Angular 22 app. See `@PigeonWatch/Frontend/CLAUDE.md` for commands and specifics.

There is no bundled .NET+Angular starter — the two are separate projects that talk over HTTP; the frontend consumes the API, they don't share a build or a repo-root package manifest.

## Notes for future work

- Deployment target is Azure App Service free (F1) tier — cost-conscious by design (sleeps on idle, ~60 min/day compute cap). CI is GitHub Actions with auto-deploy-on-merge. See `@context/foundation/tech-stack.md` for the full rationale and the Fly.io fallback if F1 limits bite.
- Map-provider API usage is meant to be cost-conscious — avoid rendering an embedded interactive map by default on pages like the bird detail page.
