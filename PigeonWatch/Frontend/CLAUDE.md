# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

Scope: `PigeonWatch/Frontend/` only — the Angular app. See `@PigeonWatch/CLAUDE.md` for what PigeonWatch is and how Api/Frontend fit together.

## What this owns

Angular 22 app (standalone components, SCSS, routing enabled, no SSR). Consumes the `Api/` project over HTTP — there is no shared build or repo-root package manifest between them.

Cross-cutting plumbing lives in `src/app/core/` (`auth/` token store, session, auth interceptor and guards; `configuration/`; `geolocation/` the geolocation service; `warmup/` state and interceptor; `resources/` UI labels and the `translate` pipe; `http/` context tokens), lazy-loaded pages in `src/app/features/` (`sightings/` holds the map home, report form, detail page, My sightings, the API client, the wire types and the error mapping), shared UI in `src/app/shared/` (`ui/`, `map/`, and `http/problem-details.ts` for reading error codes and extensions from a ProblemDetails body), and global styles in `src/styles/`. Unit tests run on Vitest with jsdom through the `@angular/build:unit-test` builder (`test` target in `angular.json`); specs sit next to the code they cover as `*.spec.ts`. The schematics still default to `skipTests`, so add a `*.spec.ts` by hand when a component or service needs one. The `frontend-build-and-test` job in `.github/workflows/api-pr-checks.yml` runs the production build and the tests on every PR to `main`.

## UI library, styling and reusable components

These rules apply to every UI change. They were introduced by the `register-and-login` change (S-01).

- **Component library: Angular Material** (`@angular/material` + `@angular/cdk`, MIT, kept on the same minor as `@angular/core`). Use its primitives (`mat-form-field` + `matInput`, `mat-button`/`mat-flat-button`, `mat-card`, `mat-progress-spinner`, …) instead of hand-rolled equivalents. Import Material modules per component; there is no shared "material module". `matInput` binds to Signal Forms directly, so no `ControlValueAccessor` glue.
- **Never run `ng add @angular/material`** and never add web-font or icon-font `<link>`s: the CSP in `public/staticwebapp.config.json` allows only `'self'` fonts and stylesheets. Typography uses the system font stack; an icon font, if one is ever needed, is self-hosted.
- **No PrimeNG.** v22 uses the key-based PrimeUI license, not MIT. Also no Tailwind/Bootstrap.
- **Design tokens** live in `src/styles/_tokens.scss` as `--pw-*` CSS custom properties on `:root`: colors, spacing scale (`--pw-space-1…8`), typography, radii, border width, shadows, layout widths, control height, motion and z-index. Raw color and length values may appear only in `_tokens.scss`, `_material-theme.scss` and `_breakpoints.scss`. Need a new value? Add a token, don't inline it.
- **Material theme** lives in `src/styles/_material-theme.scss` (`mat.theme` + `mat.theme-overrides`) and maps our tokens onto Material's `--mat-sys-*` system tokens. Per-component tweaks use `mat.<component>-overrides(...)` there, never CSS that targets `.mat-*` / `.mdc-*` classes. The keyboard focus ring on Material buttons comes from `mat.strong-focus-indicators` in the same file, pointed at the `--pw-focus-*` tokens, because Material buttons hide the global `:focus-visible` outline.
- **Common classes** live in `src/styles/_common.scss` (`pw-` prefix, built from tokens only) for what Material doesn't style: layout (`pw-container`, `pw-stack`, `pw-cluster`, `pw-center-screen`, `pw-full-width`), typography (`pw-title`, `pw-subtitle`, `pw-text-muted`, `pw-text-small`) and `pw-visually-hidden`. `src/styles.scss` only `@use`s tokens → material-theme → base → common. Breakpoints are SCSS (`@use 'breakpoints'` → `@include from(md)`), because media queries can't read custom properties.
- **Component stylesheets** use `var(--pw-*)` and `pw-*` classes only: no color/length literals, no `style="…"` in templates, no `::ng-deep`, no `!important`, no `.mat-*` / `.mdc-*` selectors, and no redefining a `pw-*` class. `src/styles/style-token-coverage.spec.ts` enforces this.
- **Reusable app components** live in `src/app/shared/ui/` (exported from `index.ts`): `app-page-card` (title, optional subtitle, actions slot; the footer collapses when nothing is projected), `app-alert`, `app-field-errors`, `app-submit-button` (`busy`, `disabled` and a `tone` input, `primary` or `success`), `app-form-actions` (the Cancel + submit pair, laid out in one place; below `sm` it stacks with the submit on top while DOM and Tab order stay Cancel, submit — an accepted exception, see impl-review F1) and `app-brand` (logo mark + app name, `md`/`lg`). They're standalone and OnPush with signal inputs, carry no literal text or resource keys of their own (callers pass translated text or project content), and expose variations as inputs. Check `shared/ui` before writing markup in a feature. When a pattern shows up in a second feature, promote it to `shared/ui` in the same change instead of copying it.
- **A variant is a component input, never a global class.** A different look of a shared component (the green Save is `<app-submit-button tone="success">`) is an input on that component, reflected as a host attribute and keyed in `_material-theme.scss` through `mat.<component>-overrides(...)`. A global `pw-button-*`-style class is not allowed: `style-token-coverage.spec.ts` fails when a template uses a `pw-*` class that `_common.scss` does not declare, or contains a colour literal or an inline `style`.
- **Kitchen sink for the Profile view** lives in `src/app/dev/kitchen-sink` and is served at the `dev/kitchen-sink` route in development builds only (`npm start`, no API and no login: every cell runs its own stubbed services). `dev/dev-routes.ts` is swapped for an empty route list through `fileReplacements` in the production configuration, so nothing of it reaches `dist/`. A new Profile state (or a change to the page or password dialog that adds one) needs a cell in `cell-definitions.ts` and a marker assertion in `kitchen-sink.spec.ts`. The Profile page embeds `app-my-sightings`, so `CellStubs.sightingsApi` (provided in `state-cell.ts`) and `profile-page.spec.ts` stub `SightingsApi` with only an empty `mine()` result; that keeps every cell free of HTTP without adding My sightings cells. A new data dependency on the Profile page needs the same minimal stub in both places. It is the one place with literal English text (state labels, dialog action names), because the page never ships and production UI text rules do not apply to it. The visual evidence for each state lives in `context/changes/profile-ui-contract/visual-notes.md`.
- **Form fields are outlined** with dynamic subscript sizing, and **validation errors show on submit, not on blur**. A component that uses `mat-form-field` adds `providers: [provideFormFieldDefaults()]` (from `shared/ui`) and calls `inject(FormSubmitState).markSubmitted()` at the start of its submit handler instead of `markAsTouched()`. The provider brings a page-scoped `FormSubmitState` and a `SubmitErrorStateMatcher`, so the red outline and the `app-field-errors` message both appear only once the form has been submitted, then follow edits live. It is not provided in `app.config.ts`, because that would pull Material form-field into the initial bundle.
- **Initial-bundle code imports `shared/ui` by file path.** `app.ts` and everything under `core/` import a component from its own file (`shared/ui/page-card/page-card`), never from `shared/ui/index.ts`: the barrel re-exports form-field and Signal Forms code, which would land in the main bundle and break the 500 kB initial budget. Lazy feature pages may use the barrel. The initial bundle was last measured at 486.2 kB after S-03, once the Leaflet CSS moved out of it. That is about 14 kB under the warning. Check `npm run build` against the budget whenever initial-bundle code changes.

## Maps and geolocation

Introduced by the `report-sighting-on-map` change (S-03).

- **Leaflet 1.9.4** (`leaflet` + `@types/leaflet`, raster tiles from OpenStreetMap). It is CommonJS, so `angular.json` lists it in `allowedCommonJsDependencies`; its CSS is a non-injected `styles` bundle (`inject: false`, emitted unhashed as `leaflet.css`), which the default `LEAFLET_LOADER` in `map-view` adds as a `<link>` on first use, so no part of Leaflet is in the initial bundle. Markers are `divIcon`s; there are no marker image files.
- **Map code lives in `src/app/shared/map/`**: `map-view/` (`app-map-view`, the only file that imports Leaflet, loading it with `from(import('leaflet'))`), `location-picker/`, `map-config.ts` (tile URL, attribution link, zoom limits, the neutral Poland view and the default zooms), `map-marker.ts` (marker type and class names) and `testing/map-view-stub.ts`. It has no barrel and is **not exported from `shared/ui/index.ts`**: lazy feature pages import it by file path, so Leaflet and the adapter land only in the chunks that render a map, never in the initial bundle or in pages that merely use the `shared/ui` barrel. `core/` and `app.ts` never import it.
- **Never pass user text into Leaflet HTML.** Popups, tooltips and `divIcon` `html` parse strings as HTML. Marker icons use static class names only (`pw-map-marker` plus a criticality modifier, declared in `_common.scss` from the `--pw-color-criticality-*` and `--pw-map-*` tokens), and selecting a marker shows an Angular-rendered card below the map instead of a popup. The attribution text is escaped in `map-config.ts`.
- **CSP**: `public/staticwebapp.config.json` admits exactly one external host, `img-src https://tile.openstreetmap.org`, which widens a policy that protects the `localStorage` refresh token. A tile provider change touches `map-config.ts` and that one CSP line, nothing else. The CSP is applied by the Static Web Apps edge, so `ng serve` cannot verify it: after any change to the CSP, the tile host or how the map loads, check the production build served with those headers (for example the Static Web Apps CLI emulator) or the deployed site, with the console and network panel open, for tiles loading and no CSP violations.
- **Cost guardrail**: render a map only where the page is about the map. The detail page shows coordinates as text and creates its map only behind the "Show map" toggle, so no tile is fetched until it is pressed.
- **Pin-picker**: `app-location-picker` is the repo's first custom Signal Forms control (`FormValueControl<GeoPoint | null>`), bound with `[formField]` like a native input. Tapping the map places or moves the pin, the pin is draggable, "Use my location" fills it from geolocation, and "Place pin at map centre" is the non-pointer path; the chosen coordinates show as text. Its required error renders through `app-field-errors` gated by `FormSubmitState`, so the host page needs `provideFormFieldDefaults()` like any other form.
- **Geolocation**: `core/geolocation/geolocation.service.ts` is the only caller of `navigator.geolocation`. `currentPosition(highAccuracy)` returns an Observable that emits once and never errors: `{ status: 'ok', latitude, longitude }` or `denied`, `unavailable`, `timeout` (10 s, always set) or `unsupported` (no `navigator.geolocation`, as in jsdom). The map home asks with low accuracy, the picker with high accuracy. Without a position the map opens on the neutral Poland view and waits for "Search this area"; nearby is 10 km around the map centre, applied by the API (`GET sightings/nearby`); the SPA reads `clientConfig.sightingRules.nearbyRadiusKm` only to show it in text.
- **Stub the map in specs.** jsdom has no layout or canvas. Any spec that renders a component containing `app-map-view`, directly or through a routed page (as `core/auth/auth.guards.spec.ts` does for the home route), swaps it for the stub: `TestBed.overrideComponent(Host, { remove: { imports: [MapView] }, add: { imports: [MapViewStub] } })`. The stub has the same selector, inputs and outputs, so a spec reads `center()`/`zoom()`/`markers()` from it and emits `mapClick`, `centerChange`, `markerSelect` or `pinMove` to drive the page. Component specs provide a `GeolocationService` stub returning the outcome under test; only `geolocation.service.spec.ts` fakes `navigator.geolocation`.

## UI text and resources

- **Every UI text is a key in `PigeonWatch/Api/BusinessObjects/Resources/UiLabels.resx`.** Templates and components contain no literal UI text: labels, buttons, messages, hints, validation and warm-up lines all come from that file. Need a new text? Add a key to the resx in the same change, not a string in the SPA.
- **Key format**: lowercase dot paths, `<area>.<group>.<name>` with camelCase segments (`common.appName`, `auth.login.title`, `auth.errors.invalidCredentials`, `warmup.messages.1`). Values use positional placeholders only (`{0}`, `{1}`); no ICU or plural syntax.
- **A new culture** is added as `UiLabels.<culture>.resx` next to the neutral English file (for example `UiLabels.pl.resx`). Missing keys fall back to English per key, and the API serves `en` for any culture without a resx.
- **Reading labels**: templates use the `translate` pipe (`{{ 'auth.login.title' | translate }}`, `{{ 'auth.validation.passwordMinLength' | translate: 8 }}`); code uses `ResourceService.translate(key, ...args)`. The API serves the map at `GET {apiUrl}/resources/{culture}`. `scripts/generate-resources.mjs` (run by `prestart`, `prebuild` and `pretest`) bundles every `UiLabels*.resx` into `src/app/core/resources/generated/` (gitignored), so labels render before the API answers.

## Angular conventions

- Components use `templateUrl` with a sibling `.html` file. No inline `template:` outside host components declared in `*.spec.ts` files.
- Async APIs in services, guards and resolvers return RxJS Observables, not Promises: no `async`/`await`/`firstValueFrom` in production code. The exceptions are `loadComponent` dynamic imports and router navigation calls (`void router.navigate(...)`); wrap other promise sources with `from(...)`.

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
