<!-- BEGIN @przeprogramowani/10x-cli -->

## 10xDevs AI Toolkit - Module 3, Lesson 4 (E2E Tests)

**For E2E tests, use the two M3L4 skills in this order:**

1. **`/10x-e2e-setup`** — one-time setup: Playwright config (`webServer`,
   auth `setup` project, `storageState`), a green seed test, and `context/foundation/test-stack.md`.
2. **`/10x-e2e`** — the per-risk loop: risk → explore the running app with
   `playwright-cli` → generate → review against the five anti-patterns →
   re-prompt by name → verify with a deliberate break.

The skills' `references/` carry the full rules, anti-patterns, seed pattern, and
prompt-template.

A few hard rules that hold even before you invoke the skill:

- **Locators:** `getByRole` / `getByLabel` / `getByText` first; `getByTestId`
  only when accessibility attributes are ambiguous. Never CSS selectors, XPath,
  or DOM structure.
- **Never `page.waitForTimeout()`.** Wait for state: `toBeVisible()`,
  `waitForURL()`, `waitForResponse()`.
- **Test independence + cleanup.** Each test runs standalone — its own setup,
  action, assertion, and cleanup; unique ids (timestamp suffix) so parallel runs
  and re-runs don't collide.

Two boundaries to keep straight:

- **DOM (snapshot) is the default.** Vision (`--caps=vision`) is a supplement for
  visual-only risks (layout, z-index, animation); for pixel regression prefer
  deterministic tools (`toHaveScreenshot`, Argos, Lost Pixel). VLM model
  selection/cost is a debugging topic (Lesson 5), not testing.
- **A red test is a signal, not a chore.** A changed selector → update the
  locator in a reviewed diff. A changed business behavior → the test caught a
  bug; never edit the assertion to match it. Fixing failing tests is Lesson 5.

<!-- END @przeprogramowani/10x-cli -->

### Git pre-commit gate

`.githooks/pre-commit` runs `.githooks/pre-commit.mjs`, which reuses `.claude/hooks/checks.mjs`. It reads the staged files and runs only what they touch: `node --check` for staged `.mjs`/`.cjs`, the Angular app and spec typecheck plus the related specs for staged frontend files, and `dotnet build` (compiler plus `.editorconfig` style rules) with the three .NET test projects for staged API files. Frontend lint stays deferred by `test-plan.md`, so there is no ESLint or Prettier step.

Activate it once per clone (git does not version `core.hooksPath`):

```bash
git config core.hooksPath .githooks
```

Bypass a single commit with `git commit --no-verify`; CI stays the authority. A failure exits non-zero and prints the failing checks, it does not loop.
