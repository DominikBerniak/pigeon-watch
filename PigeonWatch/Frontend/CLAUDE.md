# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

Scope: `PigeonWatch/Frontend/` only — the Angular app. See `@PigeonWatch/CLAUDE.md` for what PigeonWatch is and how Api/Frontend fit together.

## What this owns

Angular 22 app (standalone components, SCSS, routing enabled, no SSR). Consumes the `Api/` project over HTTP — there is no shared build or repo-root package manifest between them.

Currently a minimal shell — the root `App` component renders the PigeonWatch heading and a router outlet, with no real components/services yet. Unit tests run on Vitest with jsdom through the `@angular/build:unit-test` builder (`test` target in `angular.json`); `src/app/app.spec.ts` is the only spec. The schematics still default to `skipTests`, so add a `*.spec.ts` by hand when a component or service needs one. The `frontend-build-and-test` job in `.github/workflows/api-pr-checks.yml` runs the production build and the tests on every PR to `main`.

## UI library, styling and reusable components

These rules apply to every UI change. They are being introduced by the `register-and-login` change (Phase 3: library, tokens, theme, common styles; Phase 4: `shared/ui` components). Until those phases land, some of the files below don't exist yet.

- **Component library: Angular Material** (`@angular/material` + `@angular/cdk`, MIT, kept on the same minor as `@angular/core`). Use its primitives (`mat-form-field` + `matInput`, `mat-button`/`mat-flat-button`, `mat-card`, `mat-progress-spinner`, …) instead of hand-rolled equivalents. Import Material modules per component; there is no shared "material module". `matInput` binds to Signal Forms directly, so no `ControlValueAccessor` glue.
- **Never run `ng add @angular/material`** and never add web-font or icon-font `<link>`s: the CSP in `public/staticwebapp.config.json` allows only `'self'` fonts and stylesheets. Typography uses the system font stack; an icon font, if one is ever needed, is self-hosted.
- **No PrimeNG.** v22 uses the key-based PrimeUI license, not MIT. Also no Tailwind/Bootstrap.
- **Design tokens** live in `src/styles/_tokens.scss` as `--pw-*` CSS custom properties on `:root`: colors, spacing scale (`--pw-space-1…8`), typography, radii, border width, shadows, layout widths, control height, motion and z-index. Raw color and length values may appear only in `_tokens.scss`, `_material-theme.scss` and `_breakpoints.scss`. Need a new value? Add a token, don't inline it.
- **Material theme** lives in `src/styles/_material-theme.scss` (`mat.theme` + `mat.theme-overrides`) and maps our tokens onto Material's `--mat-sys-*` system tokens. Per-component tweaks use `mat.<component>-overrides(...)` there, never CSS that targets `.mat-*` / `.mdc-*` classes.
- **Common classes** live in `src/styles/_common.scss` (`pw-` prefix, built from tokens only) for what Material doesn't style: layout (`pw-container`, `pw-stack`, `pw-cluster`, `pw-center-screen`, `pw-full-width`), typography (`pw-title`, `pw-subtitle`, `pw-text-muted`, `pw-text-small`) and `pw-visually-hidden`. `src/styles.scss` only `@use`s tokens → material-theme → base → common. Breakpoints are SCSS (`@use 'breakpoints'` → `@include from(md)`), because media queries can't read custom properties.
- **Component stylesheets** use `var(--pw-*)` and `pw-*` classes only: no color/length literals, no `style="…"` in templates, no `::ng-deep`, no `!important`, no `.mat-*` / `.mdc-*` selectors, and no redefining a `pw-*` class. `src/styles/style-token-coverage.spec.ts` enforces this.
- **Reusable app components** live in `src/app/shared/ui/` (exported from `index.ts`): `app-page-card`, `app-alert`, `app-field-errors` and `app-submit-button`. They're standalone and OnPush with signal inputs, carry no literal text or resource keys of their own (callers pass translated text or project content), and expose variations as inputs. Check `shared/ui` before writing markup in a feature. When a pattern shows up in a second feature, promote it to `shared/ui` in the same change instead of copying it.

## TypeScript coding rules

The same layout rules as the API (`@PigeonWatch/Api/CLAUDE.md`), applied to `.ts` files. Angular template control flow (`@if`, `@for`) always needs its braces and is out of scope.

- Every `if` and `return` statement is preceded by a blank line, unless it is the first statement of its enclosing block or body (function, method, constructor, accessor, arrow-function block, `if`/`else` or loop body).
- An `if`, `else`, `for` or `while` body that is a single statement on one line has no braces. A body with more than one statement, or a single statement wrapped over several lines, keeps its braces. Prettier keeps such a body on the header line when it fits (`if (!token) return null;`).

  ```ts
  const saved = readStoredCulture();

  if (!saved) return defaultCulture;

  const culture = saved.toLowerCase();

  return supportedCultures.includes(culture) ? culture : defaultCulture;
  ```

Neither rule is checked by tooling: there is no ESLint, and Prettier preserves blank lines and braces as written.

## Commands

```
npm start          # ng serve, dev server
npm run build      # ng build
npm run watch      # ng build --watch --configuration development
npm test           # ng test (Vitest, watch mode); CI uses: npm test -- --watch=false
```

Use `npx ng generate ...` for new components/services — the Angular CLI isn't installed globally as `ng`.
