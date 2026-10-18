import { existsSync } from 'node:fs';
import { join } from 'node:path';
import { FRONTEND, block, git, repoRoot, run, tailLines } from '../.claude/hooks/lib.mjs';
import { isApiChange, isFrontendSource, relatedSpecs, runDotnet, runFrontendSpecs, typecheckFrontend } from '../.claude/hooks/checks.mjs';

const FRONTEND_CONFIG = new Set(['package.json', 'angular.json', 'tsconfig.json', 'tsconfig.app.json', 'tsconfig.spec.json']);

const root = repoRoot(process.cwd());
if (!root) process.exit(0);

function lines(args) {
  return (git(root, args) ?? '').split(/\r?\n/).filter(Boolean);
}

const staged = lines(['diff', '--cached', '--name-only', '--diff-filter=ACMRD']);
if (staged.length === 0) process.exit(0);

const unstaged = new Set(lines(['diff', '--name-only']));
const partial = staged.filter((rel) => unstaged.has(rel));
if (partial.length > 0) {
  process.stderr.write(`pre-commit: checks read the working tree, and these files have unstaged edits too:\n${partial.map((rel) => `  ${rel}`).join('\n')}\n`);
}

function step(message) {
  process.stderr.write(`pre-commit: ${message}\n`);
}

const report = [];

const scripts = staged.filter((rel) => (rel.endsWith('.mjs') || rel.endsWith('.cjs')) && existsSync(join(root, rel)));
if (scripts.length > 0) {
  step(`syntax check of ${scripts.length} staged script(s)`);
  for (const rel of scripts) {
    const result = run(process.execPath, ['--check', rel], { cwd: root });
    if (!result.ok) report.push(`Syntax error in ${rel}:\n${tailLines(result.output, 20)}`);
  }
}

const frontendStaged = staged.some(
  (rel) => isFrontendSource(rel) || (rel.startsWith(`${FRONTEND}/`) && FRONTEND_CONFIG.has(rel.slice(FRONTEND.length + 1))),
);
if (frontendStaged) {
  step('frontend typecheck (tsconfig.app.json, tsconfig.spec.json)');
  report.push(...typecheckFrontend(root));
  const specs = [...new Set(staged.filter((rel) => existsSync(join(root, rel))).flatMap((rel) => relatedSpecs(root, rel)))];
  if (specs.length > 0) {
    step(`frontend specs related to the staged files (${specs.length})`);
    const problem = runFrontendSpecs(root, specs);
    if (problem) report.push(problem);
  }
}

if (staged.some(isApiChange)) {
  step('dotnet build (compiler and .editorconfig style rules) and tests');
  report.push(...runDotnet(root));
}

if (report.length > 0) {
  block(`Commit blocked, fix these (or bypass once with git commit --no-verify):\n\n${report.join('\n\n')}`);
}

process.exit(0);
