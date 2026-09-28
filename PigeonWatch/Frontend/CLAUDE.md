# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

Scope: `PigeonWatch/Frontend/` only — the Angular app. See `@PigeonWatch/CLAUDE.md` for what PigeonWatch is and how Api/Frontend fit together.

## What this owns

Angular 22 app (standalone components, SCSS, routing enabled, no SSR). Consumes the `Api/` project over HTTP — there is no shared build or repo-root package manifest between them.

Currently at default `ng new` scaffold state — no real components/services yet, and no test scaffolding (`--skip-tests` was used at generation time, so `ng test` will fail until a test setup is added).

## Commands

```
npm start          # ng serve, dev server
npm run build      # ng build
npm run watch      # ng build --watch --configuration development
npm test           # ng test — will fail, no test setup yet
```

Use `npx ng generate ...` for new components/services — the Angular CLI isn't installed globally as `ng`.
