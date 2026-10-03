# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

Scope: `PigeonWatch/Frontend/` only — the Angular app. See `@PigeonWatch/CLAUDE.md` for what PigeonWatch is and how Api/Frontend fit together.

## What this owns

Angular 22 app (standalone components, SCSS, routing enabled, no SSR). Consumes the `Api/` project over HTTP — there is no shared build or repo-root package manifest between them.

Currently a minimal shell — the root `App` component renders the PigeonWatch heading and a router outlet, with no real components/services yet. Unit tests run on Vitest with jsdom through the `@angular/build:unit-test` builder (`test` target in `angular.json`); `src/app/app.spec.ts` is the only spec. The schematics still default to `skipTests`, so add a `*.spec.ts` by hand when a component or service needs one. The `frontend-build-and-test` job in `.github/workflows/api-pr-checks.yml` runs the production build and the tests on every PR to `main`.

## Commands

```
npm start          # ng serve, dev server
npm run build      # ng build
npm run watch      # ng build --watch --configuration development
npm test           # ng test (Vitest, watch mode); CI uses: npm test -- --watch=false
```

Use `npx ng generate ...` for new components/services — the Angular CLI isn't installed globally as `ng`.
