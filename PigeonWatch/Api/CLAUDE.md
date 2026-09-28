# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

Scope: `PigeonWatch/Api/` only — the ASP.NET Core Web API. See `@PigeonWatch/CLAUDE.md` for what PigeonWatch is and how Api/Frontend fit together.

## What this owns

ASP.NET Core Web API (.NET 10). Owns auth and the status-workflow/urgency-ranking business logic for sightings (spotted → contacted → taken to vet → healed/returned). Project: `PigeonWatchApi.csproj`, solution: `PigeonWatchApi.slnx`.

Currently at default `dotnet new webapi` scaffold state — no auth, no business logic, no test project yet. `Controllers/WeatherForecastController.cs` and `WeatherForecast.cs` are still the CLI template sample and should be removed once real endpoints exist.

## Commands

```
dotnet build
dotnet run
dotnet list package --vulnerable --include-transitive   # dependency audit
```

No test project exists yet under `Api/` — there's nothing to run `dotnet test` against until one is added.
