# Test stack

## E2E

<!-- Written by /10x-e2e-setup. Re-run it to change this section; other skills only read it. -->

- runner: Playwright Test, @playwright/test 1.64.1
- config: PigeonWatch/Frontend/playwright.config.ts (run every command from PigeonWatch/Frontend)
- single-spec command: npx playwright test tests/e2e/seed.spec.ts
- full-suite command: npx playwright test
- base URL: http://localhost:4200
- port: 4200 (detected from the API CORS policy in Api/WebApi.Host/Program.cs and the Angular default; detected default 4200, override with E2E_PORT, but the API only allows the 4200 origin)
- web server command: npm start -- --port $E2E_PORT (dev server, development configuration so the SPA calls the local API at http://localhost:5285; the production build points at Azure); reuseExistingServer outside CI
- auth setup project: setup (tests/e2e/auth.setup.ts), credentials from E2E_USERNAME / E2E_PASSWORD in PigeonWatch/Frontend/.env
- storageState: playwright/.auth/user.json (gitignored)
- seed: tests/e2e/seed.spec.ts — protects risk #4: a reported sighting appears on the nearby map where it was reported
- browser CLI: playwright-cli, command skill at .claude/skills/playwright-cli/SKILL.md
- updated: 2026-10-25
