---
project: pigeon-watch
researched_at: 2026-09-27
recommended_platform: azure-app-service-f1
runner_up: fly-io
context_type: mvp
tech_stack:
  language: csharp-net10
  framework: aspnet-core-webapi-plus-angular
  runtime: dotnet10-linux-native
---

## Recommendation

**Deploy on Azure App Service, Free (F1) tier.**

This confirms and hardens the deployment target already recorded in `tech-stack.md`. Azure App Service is the only shortlisted platform with first-class, non-container-workaround ASP.NET Core support, and it offers the strongest same-vendor co-location for this stack's needs (Azure SQL free tier for the relational DB, Blob Storage for sighting photos, Azure Static Web Apps as a companion for the Angular frontend). The developer's top priority is minimizing cost for a small community project, and F1 is genuinely free — the anti-bias cross-check surfaced real F1-tier risks (a shared 60 CPU-min/day compute cap, no Always-On, no custom domain/SSL), and the developer explicitly chose to accept these as MVP-stage tradeoffs rather than start on the paid B1 tier, consistent with the tradeoff already documented in `tech-stack.md`. The interview's original "global reach matters" answer was later retracted (single-region is fine), removing what would otherwise have been Azure's biggest weakness against Fly.io.

## Platform Comparison

Four platforms cleared the hard runtime filter (genuine, documented, always-on .NET/Docker support, not a serverless-function workaround): **Fly.io, Railway, Render, Azure App Service**. Two were dropped before scoring:

- **Netlify** — hard fail. No .NET/C# runtime exists on Netlify Functions (JS/Go/Python/Rust only); confirmed via official docs and a long-standing unresolved community thread. Would only be viable as a frontend-only host in a split architecture.
- **Vercel** and **Cloudflare** — dropped from the full-stack shortlist, not scored. Both can technically run a containerized ASP.NET Core app via a recently-GA generic-container-function path (Vercel's "Dockerfile to Vercel Functions", Cloudflare Containers), but both forfeit the platform's core value proposition for this use case: Vercel's container-function mode loses Secure Compute/Static IPs and has no proven .NET track record; Cloudflare Containers are not edge-replicated, so the platform's signature edge-latency benefit doesn't actually apply to the API tier. Either remains viable as a **frontend-only host in a hybrid split** (Angular on Vercel/Cloudflare Pages, .NET API elsewhere) but that adds a second platform to operate for no clear MVP-stage benefit here.

| Platform | CLI-first | Managed/Serverless | Agent-readable docs | Stable deploy API | MCP / Integration | Total |
|---|---|---|---|---|---|---|
| **Azure App Service** | Pass | Pass | Pass | Pass | Partial | 4 Pass, 1 Partial |
| Fly.io | Pass | Pass | Partial | Pass | Pass | 4 Pass, 1 Partial |
| Railway | Partial | Pass | Pass | Pass | Pass | 4 Pass, 1 Partial |
| Render | Partial | Pass | Pass | Pass | Pass | 4 Pass, 1 Partial |

Notes per platform:
- **Azure App Service**: CLI (`az`, `azd`) and GitHub Actions (`azure/webapps-deploy@v3`) are GA and fully scriptable; ASP.NET Core is native (not containerized-workaround); Microsoft Learn docs are markdown/agent-fetchable. The one gap: App Service's *built-in* MCP (turns the app's OpenAPI surface into an MCP server) is public-preview and Basic-tier-and-above only — not available on F1. The general-purpose Azure MCP Server (resource/CLI management, GA as of 2026-04-10) still works fine on F1.
- **Fly.io**: Full container/VM model (Firecracker Machines) suits ASP.NET Core directly — no serverless restrictions. `flyctl` ships a first-class built-in MCP server (genuinely stronger agent integration than the others). Docs are markdown-sourced and GitHub-editable, but a root `llms.txt` could not be confirmed. No free tier since Oct 2024 — pure usage billing with no subscription cap.
- **Railway**: No .NET auto-detection (Railpack doesn't support it) — a Dockerfile is required, which is fine but not zero-config. CLI covers deploy/logs well but true point-in-time rollback is dashboard-only. Strong co-located Postgres + S3-compatible Buckets (good fit for photo uploads). No viable free tier for a real 3-service app (~$15-20/mo realistic floor).
- **Render**: Docker-only for .NET (no native buildpack). CLI/Blueprint/API deploys are scriptable but rollback isn't a clean CLI subcommand. Free web services cannot attach persistent disks at all — a hard gotcha for photo uploads, which would need self-hosted MinIO or a paid Disk from day one. Free Postgres also expires after 30 days. Single-region only, with no cross-region support even as an opt-in (long-open, unresolved feature request).

### Shortlisted Platforms

#### 1. Azure App Service (Recommended)

Wins on cost predictability (genuinely $0 on F1, with B1 at ~$13/mo as a known, simple upgrade path) and co-location: Azure SQL Database's free tier (100,000 vCore-seconds/month, 32 GB, auto-pause) plus Blob Storage for photos plus Azure Static Web Apps for the Angular frontend all sit in the same vendor account the solo .NET developer already has to use for the API. It's also the path of least resistance — no re-architecture needed relative to what `tech-stack.md` already assumed.

#### 2. Fly.io

Best CLI and MCP story of the four (built-in `flyctl` MCP server is a genuine first-class agent integration, not a bolt-on), and the only platform with a straightforward multi-region growth path if the community app ever needs it later. Loses to Azure here mainly on cost predictability (no free tier, pure usage billing with no spending cap) and on requiring the developer to operate their own Postgres cluster rather than a fully-managed same-vendor DB.

#### 3. Railway

Cleanest GA co-location for photo uploads specifically (Buckets is a purpose-built S3-compatible object store, better fit than Render's disk-or-DIY situation). Loses to Azure and Fly.io mainly on cost — no free tier and a realistic ~$15-20/mo floor even at low traffic, the highest of the four candidates — which conflicts directly with the developer's stated cost-minimization priority.

## Anti-Bias Cross-Check: Azure App Service (F1)

### Devil's Advocate — Weaknesses

1. **The 60 CPU-minute/day cap is a shared subscription+region pool, not scoped to just this app** — any other Free-tier resource in the same region/subscription (a staging slot, a side project) draws from the same daily budget, and a legitimate traffic burst (or a crawler) can trip a 403-quota-exceeded outage with no advance warning.
2. **No Always-On on F1** — the app unloads after ~20 minutes idle, so the first request after any idle period pays a full .NET cold start, in tension with the PRD's own NFR that user actions "show acknowledgement quickly."
3. **No custom domain or SSL upload on F1** — stuck on `*.azurewebsites.net` unless upgraded to B1 or fronted by a second service (e.g. Cloudflare), which would mean operating two platforms instead of one.
4. **Hard ceiling of 5 concurrent WebSocket connections on the Free Linux SKU** — not a blocker for MVP (no realtime requirement), but the PRD's own Secondary-persona roadmap floats live messaging later; if built, F1 caps it silently at 5 connections.
5. **No deployment slots on F1/B1** — every GitHub Actions auto-deploy-on-merge push goes straight to production with no blue/green safety net, a real risk for a solo developer with no manual gate before production traffic hits new code.

### Pre-Mortem — How This Could Fail

The team deployed on Azure App Service F1 because it was free and already the assumed default. For the first month, everything looked fine — light traffic, no visible cold starts during testing. Then the app got shared in a local Facebook rescue group and saw a real burst of first-time visitors in one evening; the shared 60-CPU-minute daily cap tripped mid-evening, and the app returned 403s for several hours with no alerting in place to explain why, right during the traffic spike it was supposed to capture. Around the same time, the developer discovered F1 couldn't take a custom domain, so the "shareable" link was still an ugly `*.azurewebsites.net` URL months after launch — nobody had budgeted the follow-up work to upgrade to B1 and re-point DNS. Meanwhile, cold starts after idle periods (most of the day, given low traffic) meant the first sighting report each morning consistently took several seconds to acknowledge, undermining trust exactly in the "report a distressed bird quickly" flow the product exists for. By the time B1 was reluctantly adopted to fix both problems, the "free tier" cost savings had evaporated and the migration itself (verifying no downtime, re-pointing DNS) ate a weekend that should have gone to features.

### Unknown Unknowns

- The 60 CPU-minute cap is a **subscription+region** pool, not scoped to just this one app — anything else in the same subscription/region silently competes for the same budget.
- F1's "no SLA" isn't just fine print — Microsoft explicitly does not consider F1 suitable for anything beyond a demo/prototype, in tension with treating it as the MVP's actual production home for a real (if small) community.
- Azure's *built-in* MCP for App Service (turning the OpenAPI surface into structured agent tools) is Basic-tier-and-above only — not available on F1. The general Azure MCP Server for resource management still works, but the more granular "ask the running app about itself" tooling does not.
- Azure SQL's free offer auto-pauses the database when its monthly vCore-second quota is exhausted — a second, distinct silent failure mode from the App Service compute cap, easy to misdiagnose as "the app is down" when it's actually "the database went to sleep."

**Decision**: proceed with Azure App Service F1, risks accepted for MVP stage (explicit developer choice after the cross-check). Upgrade to B1 (~$13/mo) is the known escape hatch if the compute cap, cold starts, or custom-domain gap start to bite in practice — already flagged as an option in `tech-stack.md`.

## Operational Story

- **Preview deploys**: GitHub Actions (`azure/webapps-deploy@v3`) auto-deploys on merge to main per `tech-stack.md`'s `ci_default_flow`. F1 has no deployment slots, so there is no built-in staging-slot preview — a PR-based preview environment would require a second (paid) App Service plan or slot-enabled tier; for MVP, verify changes locally/in CI before merge instead.
- **Secrets**: App settings (connection strings, Blob Storage keys; no signing key: Identity bearer tokens are protected by the Data Protection key ring) live in Azure App Service Configuration (encrypted at rest, injected as env vars at runtime) and/or GitHub Actions Secrets for the deploy credential itself. Only the account owner can read them via the Azure Portal or `az webapp config appsettings list`; rotate by updating the App Service setting and redeploying — no code change needed since the app reads from environment/configuration.
- **Rollback**: No deployment slots on F1/B1 means no swap-based rollback. Revert is git-based: revert the merge commit, let GitHub Actions auto-deploy-on-merge redeploy the previous version. Any EF Core migration applied by the failed deploy does not automatically roll back — a migration-down step must be run manually if the failed deploy included a schema change.
- **Approval**: Publishing to production is unattended by design (auto-deploy-on-merge) — the human gate is the PR review/merge itself, not a separate deploy approval. Rotating the primary DB connection string (there is no signing key: Identity bearer tokens are protected by the Data Protection key ring), or deleting the Azure SQL database, are human-only, panel-or-CLI-by-hand operations, never delegated to an agent.
- **Logs**: `az webapp log tail --name <app> --resource-group <rg>` streams live application/platform logs; `az webapp log download` pulls historical logs as a zip. The Azure MCP Server (GA) also exposes structured log/monitor tools an agent can call directly instead of parsing CLI text output.

## Risk Register

| Risk | Source | Likelihood | Impact | Mitigation |
|---|---|---|---|---|
| Shared 60 CPU-min/day quota trips a 403 outage during a real traffic burst | Pre-mortem | M | H | Keep only this one Free-tier resource in the subscription/region; monitor usage via `az webapp` metrics; set a mental trigger to upgrade to B1 if this happens more than once |
| Cold start after ~20 min idle undermines "quick acknowledgement" NFR | Devil's advocate | H | M | Accept for MVP; if it becomes a UX complaint, upgrade to B1 for Always-On rather than working around it in-app |
| No custom domain/SSL on F1 keeps the shareable link on `*.azurewebsites.net` | Devil's advocate | H | L | Accept for MVP; budget a B1 upgrade + DNS cutover as the first post-launch infra task if a branded domain becomes a priority |
| EF Core migration doesn't auto-rollback on a reverted deploy | Operational story | L | M | Document a manual migration-down runbook step before any schema-changing release; test rollback locally before merging schema changes |
| Azure SQL free tier auto-pauses when its monthly vCore-second quota is exhausted, misread as an app outage | Unknown unknowns | M | M | When investigating any "app is down" report, check both App Service logs and Azure SQL status before assuming a compute-quota issue |
| App Service's built-in MCP (structured agent tooling for the running app) is unavailable on F1 | Research finding | H | L | Use the general-purpose Azure MCP Server (GA) for resource/log management instead; re-evaluate if/when upgrading to B1 |
| No deployment slots means every merge deploys straight to production with no blue/green safety net | Devil's advocate | M | M | Keep PR review as the actual gate; add a smoke-test step in CI before the deploy step completes |
| Future realtime messaging (PRD Secondary persona) would hit F1's 5-concurrent-WebSocket ceiling | Devil's advocate | L | M | Not an MVP concern; revisit platform/tier choice specifically if/when live messaging is scoped in |
| Region choice for F1 compute isn't a free pick — most regions returned live quota/policy failures on this subscription (`westeurope` blocked for new-customer resource creation; `germanywestcentral`/`eastus2`/`northeurope` gave `0 F1 VMs` quota; `polandcentral` has no Static Web Apps support and a reported SQL free-tier billing bug) | Deploy-plan execution | H | M | Confirmed live: `swedencentral` works for App Service F1 + Azure SQL free tier + Storage. Static Web Apps' 5-region list doesn't include `swedencentral`, so frontend lives separately in `eastus2` — accepted as harmless since SWA content is CDN-served regardless of origin region. Any future re-deploy or second environment should start from `swedencentral`/`eastus2` directly rather than re-discovering this by trial and error |

## Getting Started

1. Confirm the Azure subscription and resource group to deploy into (`az account show`, `az group create --name pigeon-watch-rg --location <region>` if none exists).
2. Create the Free-tier App Service plan and web app for the ASP.NET Core API: `az appservice plan create --name pigeon-watch-plan --resource-group pigeon-watch-rg --sku F1 --is-linux`, then `az webapp create --resource-group pigeon-watch-rg --plan pigeon-watch-plan --name <api-app-name> --runtime "DOTNETCORE:10.0"`.
3. Provision the Azure SQL free-tier database (`az sql server create` + `az sql db create --edition GeneralPurpose --compute-model Serverless --family Gen5 ... --use-free-limit`) and an Azure Storage account with a Blob container for sighting photos (`az storage account create`, `az storage container create`).
4. Wire the connection string and Blob Storage credentials as App Service application settings (`az webapp config appsettings set`), never committed to source.
5. Add the `azure/webapps-deploy@v3` GitHub Actions workflow (publish profile or OIDC federated credential as the GitHub secret) so merges to `main` auto-deploy the API; deploy the Angular build separately to Azure Static Web Apps (`swa deploy` or its own GitHub Actions workflow) rather than serving it from the same App Service instance.

## Out of Scope

The following were not evaluated in this research:
- Docker image configuration
- CI/CD pipeline setup beyond confirming GitHub Actions as the deploy mechanism
- Production-scale architecture (multi-region, HA, DR) — explicitly dropped from consideration per the developer's revised answer that single-region is fine for MVP
