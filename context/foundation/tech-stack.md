---
starter_id: dotnet
package_manager: dotnet
project_name: pigeon-watch
hints:
  language_family: dotnet
  team_size: solo
  deployment_target: azure-app-service
  ci_provider: github-actions
  ci_default_flow: auto-deploy-on-merge
  bootstrapper_confidence: verified
  path_taken: standard
  quality_override: false
  self_check_answers: null
  has_auth: true
  has_payments: false
  has_realtime: false
  has_ai: false
  has_background_jobs: false
---

## Why this stack

PigeonWatch is a medium-scale web app with a 3-week timeline, targeting a solo full-stack .NET/Angular developer new to agent-assisted delivery — the user deliberately chose to build on familiar technology (.NET + Angular) so the project's learning focus stays on supervising an AI agent, not on also learning a new framework. ASP.NET Core webapi is the registry's recommended default for (web, dotnet) and clears all four agent-friendly gates (typed, convention-based, popular in training, well-documented), with verified bootstrapper confidence, so scaffolding should be smooth. It anchors the backend, which owns auth (FR-001) and the status-workflow/urgency-ranking business logic. Because the schema records one starter_id per hand-off and no bundled .NET+Angular starter exists in this registry, the Angular frontend is a manual companion: after bootstrapper scaffolds this ASP.NET Core API, run `npx @angular/cli new pigeon-watch-web --defaults --routing --style scss --skip-tests --ssr false` alongside it as a separate project consuming the API. Deployment targets Azure App Service's free (F1) tier — a deliberate cost tradeoff: it sleeps after idle time, caps around 60 min/day of compute, and has weak custom-domain support, accepted knowingly to keep hosting free for a small community project; upgrading to a paid tier or switching to Fly.io remains a low-effort escape hatch if these limits bite. CI runs on GitHub Actions with auto-deploy-on-merge, the standard shape for a solo builder. Payments, realtime, AI, and background jobs are all out of scope per the PRD.
