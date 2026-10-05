---
change_id: register-and-login
kind: external-research
date: 2026-10-04
sources: web search + official docs (exa.ai and Context7 not connected in this session)
---

# External research: register and log in (S-01, FR-001)

Scope: what the ecosystem says we *should* do for register / login / logout with an Angular 22 SPA and an ASP.NET Core .NET 10 Web API (controllers, EF Core 10, Azure SQL). Internal codebase evidence belongs in `research.md` (`/10x-research`).

## Topology constraint (drives the main decision)

- SPA: Azure Static Web Apps Free, `https://<x>.azurestaticapps.net` (eastus2). API: App Service F1, `https://<y>.azurewebsites.net` (swedencentral). See `context/foundation/infrastructure.md`.
- Both `azurestaticapps.net` and `azurewebsites.net` are on the Public Suffix List, so the SPA and API are **cross-site**, not merely cross-origin [PSL].
- Consequence: an auth cookie set by the API is a **third-party cookie** from the SPA's point of view.

| Browser (2026) | Third-party cookies |
|---|---|
| Chrome | Allowed by default; Google abandoned 3PC deprecation (Oct 2025) [PS-blog]. Always blocked in Incognito [Chrome-incog]. |
| Safari | Blocked, "no exceptions" (ITP) [WebKit-TP]. |
| Firefox | State-partitioned (Total Cookie Protection) [MDN-SP]. |
| Brave | Blocked by default [Brave]. |
| Edge | Balanced tracking prevention; full default block announced, no date [Edge]. |

**Cross-site `SameSite=None` cookies are not viable** (breaks Safari/iOS, Brave, Chrome Incognito). CHIPS (`Partitioned`) is a theoretical rescue but unverified with ASP.NET Core and Safari ITP.

## Option comparison

| | A. Bearer tokens (`MapIdentityApi`, no cookies) | B. SWA linked backend + same-origin cookie | C. Cross-site cookie |
|---|---|---|---|
| Works in Safari / Brave / Incognito | Yes | Yes | No |
| Cost | $0 | SWA Standard, ~$9/app/month (price unverified on official page) [SWA-AppSvc][SWA-plans] | $0 |
| XSS can steal credential | Yes (refresh token in JS storage) | No (HttpOnly) | No |
| CSRF defence | Not needed (header not auto-sent) [OWASP-CSRF] | Light: `SameSite=Lax` + Origin check | Heavy: antiforgery + Origin check |
| CORS | Explicit SWA origin, no credentials [MS-CORS] | None (same origin) | Explicit origin + `AllowCredentials` |
| Microsoft / RFC alignment | Token mode is "proprietary", for clients that can't use cookies [MS-IdentityAPI]; RFC 10017 "browser-only client" = least preferred [RFC10017] | Microsoft: "Cookies are preferred over tokens for both security and simplicity" [MS-Choose]; closest to BFF [RFC10017] | — |
| Extra constraints | Angular refresh-on-401 interceptor; token not revocable before expiry | API routes must sit under `/api`; App Service only accepts SWA-proxied traffic; no API in PR preview envs [SWA-AppSvc] | Fragile |

SWA Free cannot reverse-proxy to an external URL: rewrites must be relative to the app root [SWA-config].

## ASP.NET Core Identity (.NET 10) facts

- `AddIdentityApiEndpoints<TUser>()` + `MapIdentityApi<TUser>()` give: `POST /register`, `/login`, `/refresh`, `/resendConfirmationEmail`, `/forgotPassword`, `/resetPassword`, `/manage/2fa`, `/manage/info`, `GET /confirmEmail`, `GET /manage/info` [MS-IdentityAPI][aspnetcore-src].
- Mode per request: `/login?useCookies=true` or `?useSessionCookies=true` → cookie; neither → bearer token pair [MS-IdentityAPI].
- Bearer token is **opaque, Data-Protection-protected, not a JWT**. Defaults: access 1 h, refresh 14 days (`BearerTokenOptions`) [BearerTokenOptions]. `/refresh` validates security stamp [aspnetcore-src].
- **No built-in `/logout`** — docs pattern: custom POST calling `SignInManager.SignOutAsync()` with `RequireAuthorization()` [MS-IdentityAPI]. Bearer tokens cannot be revoked; logout = client discards tokens (security stamp update only blocks the next refresh).
- **`/register` DTO is not extensible** (Email + Password only, UserName = email); open issues #50303, #55529, #55792 [GH-issues]. Extra fields (e.g. display name) → own controller action using `UserManager.CreateAsync`.
- `/manage/info` returns no claims/roles → the SPA needs its own source of current-user info incl. roles (resolved as D5: `GeneralConfiguration`).
- `/login` hardcodes `lockoutOnFailure: true` [aspnetcore-src].
- .NET 10 breaking change (good for SPA): cookie auth returns 401/403 instead of redirect for `[ApiController]` endpoints [BC-cookie].
- .NET 10 passkeys exist in Identity but **not** in `MapIdentityApi` (endpoints land in 12.0 preview) [Passkeys][PR-68198]. Out of scope for MVP.
- `.WithOpenApi()` used in some doc samples is deprecated in .NET 10 [BC-overview].
- Identity metrics meter `Microsoft.AspNetCore.Identity` added in .NET 10 [Release-notes].

### EF Core integration

- Existing DbContext inherits `IdentityDbContext<ApplicationUser, IdentityRole, string>` (or Guid key), calls `base.OnModelCreating` first [Customize-model].
- `AddIdentityApiEndpoints<ApplicationUser>().AddRoles<IdentityRole>().AddEntityFrameworkStores<AppDbContext>()` — add roles now so admin (PRD) is data later [AddIdentityApiEndpoints].
- Pick the key type in the first migration; changing later means dropping tables [Customize-model].

### Defaults to override

- Password defaults: digit + lower + upper + non-alphanumeric required, `RequiredLength = 6` [Identity-config]. NIST forbids composition rules [NIST].
- Lockout defaults: 5 failures → 5 min; `AllowedForNewUsers = true` [Identity-config].
- `RequireConfirmedEmail = false`, `RequireUniqueEmail = false` by default → set `RequireUniqueEmail = true` [Identity-config].
- Hashing: PBKDF2-HMAC-SHA512, 100,000 iterations, 128-bit salt (V3) — acceptable, no change [PasswordHasher].

### Data Protection keys on App Service

- Keys persisted automatically to `%HOME%\ASP.NET\DataProtection-Keys` (shared, survives restarts) [DP-defaults]. Slot swaps do not share keys → users logged out. No evidence F1 differs (unverified). Avoid slots for MVP or persist keys to Blob/SQL.

## Password & login security rules

- NIST SP 800-63B-4 (final, Aug 2025) [NIST]: min **15** chars when password is the only factor (8 with MFA); allow max ≥ 64; **no composition rules**; SHALL check against breached/common password blocklist; no forced rotation; allow paste and Unicode.
- OWASP [OWASP-Auth]: generic "Invalid email or password"; registration should not reveal existing accounts (only fully achievable with an email-confirmation flow); per-account failed-attempt tracking; lockout must not enable DoS of other users; set a max length.
- Rate limiting: built-in `AddRateLimiter` + `[EnableRateLimiting("auth")]`, `UseRateLimiter` after `UseRouting` [RateLimit]. IP partitioning behind App Service requires forwarded headers (IIS/Windows App Service enables them automatically; Linux needs `ASPNETCORE_FORWARDEDHEADERS_ENABLED=true`) [Proxy].

## Angular 22 idioms

- Functional interceptors: `provideHttpClient(withInterceptors([authInterceptor]))`; `HttpContextToken` to skip auth on `/login`, `/refresh` [NG-interceptors].
- Functional guards `CanActivateFn`/`CanMatchFn` returning `UrlTree` to `/login?returnUrl=...`; guards are UX only, never access control [NG-guards].
- HttpClient uses the fetch backend by default in v22; per-request `credentials`/`withCredentials` [NG-requests].
- Signal Forms are **stable in v22**; zoneless default since v21; `OnPush` default in v22 [NG-v22][NG-signal-forms].
- Built-in XSRF header only sent to same-origin/relative URLs [NG-security] → irrelevant for option A, works for option B.
- Angular security guide says nothing on token storage [NG-security].
- No official refresh-and-retry recipe; community pattern: single shared in-flight refresh (`shareReplay`), retry once via context flag, logout on refresh failure (unverified against official source).

## Token storage (option A only)

- RFC 10017 (BCP 212, 2026): no browser storage protects tokens from XSS — malicious JS "has the same privileges as the legitimate application code" [RFC10017].
- OWASP: do not store session identifiers in localStorage [OWASP-HTML5].
- Pragmatic MVP stance: access token in memory; refresh token in `localStorage` (survive reload/new tab) or `sessionStorage` (tab-scoped); strict CSP via SWA `globalHeaders`; no `bypassSecurityTrust*`; accept residual XSS risk explicitly.

## Alternatives considered

- Custom JWT (`JwtBearer`): we would own key management, refresh rotation, revocation — too much security code for a solo 3-week MVP [MS-Choose].
- External IdP (Entra External ID: first 50k MAU free [Entra-pricing]; Auth0 not checked): no password storage, MFA free, but redirect/MSAL flow, limited UI customization, extra setup. Viable later; not recommended for S-01.

## Recommendation (pre-decision; superseded where it conflicts with Decisions below)

1. **Identity + `MapIdentityApi`** with own controller actions for `/logout`, current-user info (now D5) and `/register` only if needed for D2. Roles registered now.
2. Topology: A (bearer, $0) vs B (SWA Standard + cookie, ~$9/mo) — resolved as D1.
3. Password policy: NIST-aligned — resolved differently as D3.
4. Generic login error; keep Identity lockout; IP-partitioned rate limiter on auth endpoints.
5. Email confirmation: skip for MVP — resolved as D4.
6. Angular: functional interceptor + guard, signal-based auth service, Signal Forms.

## Decisions (user, 2026-10-04) — binding input for /10x-plan

| # | Decision | Consequence for the plan |
|---|---|---|
| D1 | **Option A: bearer tokens** (`MapIdentityApi` token mode, no auth cookies). Option B (SWA Standard + same-origin cookie) stays the documented upgrade path. | CORS with explicit SWA origin, `Authorization` header allowed, no `AllowCredentials`. Angular interceptor attaches access token and does refresh-on-401 with a single in-flight refresh. Custom `/logout` = client discards tokens (server cannot revoke bearer tokens). Residual XSS risk on stored refresh token accepted for MVP; mitigate with CSP via SWA `globalHeaders`. No CSRF/antiforgery work. |
| D2 | **Registration collects email + password only.** Display name is derived from the email's local part (text before `@`) and shown in the account UI. Users edit it later on a user-details page (S-02). | Built-in `/register` request shape is sufficient (Email + Password). The plan must pick where the derived name lives: (a) `DisplayName` column on `ApplicationUser` populated at registration (needs a custom register action or post-create hook, since built-in `/register` is not extensible), or (b) nullable `DisplayName`, falling back to the email local part on read. S-02 editing needs a persisted column either way. Current-user info (email, display name, roles) is served by `GeneralConfiguration` — see D5. |
| D3 | **Classic composition password policy, below NIST length.** Require lowercase, uppercase, digit and non-alphanumeric (Identity defaults kept). | Conscious deviation from NIST SP 800-63B-4 (which forbids composition rules and asks for 15+ chars single-factor) — accepted for MVP. Exact `RequiredLength` not specified by the user: Identity default is 6; plan should propose a value (8 is the common floor) and confirm. Breached-password check out of scope. |
| D4 | **No email confirmation for MVP.** | `RequireConfirmedEmail = false`, no `IEmailSender` provider, no `/confirmEmail` or password-reset flow wired in the UI. Accepted: `/register` reveals whether an email already exists; no trusted password recovery. Still set `RequireUniqueEmail = true`. |
| D5 | **No standalone `/me` endpoint. Two configuration endpoints:** **`ClientConfiguration`** — anonymous data the SPA needs before login (may be empty/minimal in S-01); **`GeneralConfiguration`** — everything for authenticated users, including a current-user section (working name `CurrentUser` / `CurrentUserDetails`: id, email, display name, roles). Later slices extend whichever model fits (public vs member-only config). | `ClientConfiguration`: `[AllowAnonymous]`, identical for every caller, publicly cacheable, never contains user data. `GeneralConfiguration`: `[Authorize]`, per-user, `Cache-Control: private, no-store`; an expired/invalid token returns **401**, so the standard refresh-on-401 interceptor covers it. Angular startup (`provideAppInitializer`): load `ClientConfiguration` → if a refresh token is stored, load `GeneralConfiguration` (interceptor refreshes on 401; refresh failure → clear tokens, go to login) → otherwise route to login. Load `GeneralConfiguration` after login and after the S-02 display-name edit; clear it on logout. Routes and exact names to follow existing controller conventions (internal research). |

### Still open (plan should resolve)

- Refresh-token persistence in the browser: `localStorage` (survives reload / new tab) vs `sessionStorage` (tab-scoped). Access token in memory either way.
- Exact minimum password length (D3).
- Display-name storage approach (D2 a vs b).

## Unverified

- Official SWA Standard price; App Service linking on F1 specifically ("all hosting plans" per docs).
- CHIPS `Partitioned` support in ASP.NET Core `CookieOptions` and with Safari ITP.
- Whether F1 affects Data Protection key persistence.
- Whether Linux App Service sets `ASPNETCORE_FORWARDEDHEADERS_ENABLED` automatically.
- Official Angular refresh-retry pattern; Edge 3PC block date; Entra paid per-MAU rate.

## Sources

- [PSL] https://publicsuffix.org/list/public_suffix_list.dat
- [PS-blog] https://privacysandbox.google.com/blog/update-on-plans-for-privacy-sandbox-technologies
- [Chrome-incog] https://support.google.com/chrome/answer/95464
- [WebKit-TP] https://webkit.org/tracking-prevention/
- [MDN-SP] https://developer.mozilla.org/en-US/docs/Web/Privacy/Guides/State_Partitioning
- [Brave] https://brave.com/shields/
- [Edge] https://support.microsoft.com/en-us/edge/learn-about-tracking-prevention-in-microsoft-edge
- [SWA-AppSvc] https://learn.microsoft.com/en-us/azure/static-web-apps/apis-app-service
- [SWA-plans] https://learn.microsoft.com/en-us/azure/static-web-apps/plans
- [SWA-config] https://learn.microsoft.com/en-us/azure/static-web-apps/configuration
- [RFC10017] https://www.rfc-editor.org/info/rfc10017
- [OWASP-HTML5] https://cheatsheetseries.owasp.org/cheatsheets/HTML5_Security_Cheat_Sheet.html
- [OWASP-CSRF] https://cheatsheetseries.owasp.org/cheatsheets/Cross-Site_Request_Forgery_Prevention_Cheat_Sheet.html
- [OWASP-Auth] https://cheatsheetseries.owasp.org/cheatsheets/Authentication_Cheat_Sheet.html
- [NIST] https://pages.nist.gov/800-63-4/sp800-63b.html
- [MS-IdentityAPI] https://learn.microsoft.com/en-us/aspnet/core/security/authentication/identity-api-authorization?view=aspnetcore-10.0
- [MS-Choose] https://learn.microsoft.com/en-us/aspnet/core/security/how-to-choose-identity-solution?view=aspnetcore-10.0
- [MS-CORS] https://learn.microsoft.com/en-us/aspnet/core/security/cors?view=aspnetcore-10.0
- [aspnetcore-src] https://github.com/dotnet/aspnetcore/blob/release/10.0/src/Identity/Core/src/IdentityApiEndpointRouteBuilderExtensions.cs
- [BearerTokenOptions] https://github.com/dotnet/aspnetcore/blob/release/10.0/src/Security/Authentication/BearerToken/src/BearerTokenOptions.cs
- [GH-issues] https://github.com/dotnet/aspnetcore/issues/50303 , https://github.com/dotnet/aspnetcore/issues/55529 , https://github.com/dotnet/aspnetcore/issues/55792
- [BC-cookie] https://learn.microsoft.com/en-us/aspnet/core/breaking-changes/10/cookie-authentication-api-endpoints?view=aspnetcore-10.0
- [BC-overview] https://learn.microsoft.com/en-us/aspnet/core/breaking-changes/10/overview
- [Passkeys] https://learn.microsoft.com/en-us/aspnet/core/security/authentication/passkeys/?view=aspnetcore-10.0
- [PR-68198] https://github.com/dotnet/aspnetcore/pull/68198
- [Release-notes] https://learn.microsoft.com/en-us/aspnet/core/release-notes/aspnetcore-10.0?view=aspnetcore-10.0
- [Customize-model] https://learn.microsoft.com/en-us/aspnet/core/security/authentication/customize-identity-model?view=aspnetcore-10.0
- [AddIdentityApiEndpoints] https://learn.microsoft.com/en-us/dotnet/api/microsoft.extensions.dependencyinjection.identityservicecollectionextensions.addidentityapiendpoints?view=aspnetcore-10.0
- [Identity-config] https://learn.microsoft.com/en-us/aspnet/core/security/authentication/identity-configuration?view=aspnetcore-10.0
- [PasswordHasher] https://github.com/dotnet/aspnetcore/blob/release/10.0/src/Identity/Extensions.Core/src/PasswordHasher.cs
- [DP-defaults] https://learn.microsoft.com/en-us/aspnet/core/security/data-protection/configuration/default-settings?view=aspnetcore-10.0
- [RateLimit] https://learn.microsoft.com/en-us/aspnet/core/performance/rate-limit?view=aspnetcore-10.0
- [Proxy] https://learn.microsoft.com/en-us/aspnet/core/host-and-deploy/proxy-load-balancer?view=aspnetcore-10.0
- [Entra-pricing] https://azure.microsoft.com/en-us/pricing/details/microsoft-entra-external-id/
- [NG-interceptors] https://angular.dev/guide/http/interceptors
- [NG-guards] https://angular.dev/guide/routing/route-guards
- [NG-requests] https://angular.dev/guide/http/making-requests
- [NG-security] https://angular.dev/best-practices/security
- [NG-signal-forms] https://angular.dev/guide/forms/signals/overview
- [NG-v22] https://www.infoq.com/news/2026/08/angular-v22-released/
