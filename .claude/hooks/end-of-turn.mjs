import { existsSync } from 'node:fs';
import { join } from 'node:path';
import { FRONTEND, block, clearRoots, git, norm, readPayload, registeredRoots, repoRoot, run, sanitizeSessionId, tailLines } from './lib.mjs';
import { isApiChange, isFrontendSource, relatedSpecs, runDotnet, runFrontendSpecs, typecheckFrontend } from './checks.mjs';

const payload = readPayload();

if (payload.stop_hook_active === true || (payload.loop_count ?? 0) !== 0) process.exit(0);

const cwd = typeof payload.cwd === 'string' && payload.cwd !== '' ? payload.cwd : process.cwd();
const sessionId = sanitizeSessionId(payload.session_id);

const roots = new Map();
for (const candidate of [repoRoot(cwd), ...registeredRoots(sessionId)]) {
  if (candidate && existsSync(candidate)) roots.set(norm(candidate), candidate);
}

if (roots.size === 0) {
  process.stdout.write(`${JSON.stringify({ systemMessage: `end-of-turn.mjs: no git checkout at ${cwd}, nothing checked.` })}\n`);
  process.exit(0);
}

function changedFiles(root) {
  const tracked = git(root, ['diff', '--name-only', 'HEAD']) ?? '';
  const untracked = git(root, ['ls-files', '-o', '--exclude-standard']) ?? '';
  const names = `${tracked}\n${untracked}`.split(/\r?\n/).filter(Boolean);
  return [...new Set(names)].sort();
}

const FRONTEND_CONFIG = new Set(['package.json', 'angular.json', 'tsconfig.json', 'tsconfig.app.json', 'tsconfig.spec.json']);

const report = [];

for (const root of roots.values()) {
  const changed = changedFiles(root);
  if (changed.length === 0) continue;

  const mjs = changed.filter((rel) => (rel.endsWith('.mjs') || rel.endsWith('.cjs')) && existsSync(join(root, rel)));
  for (const rel of mjs) {
    const result = run(process.execPath, ['--check', rel], { cwd: root });
    if (!result.ok) report.push(`Syntax error in ${rel} (${root}):\n${tailLines(result.output, 20)}`);
  }

  const frontendChanged = changed.some(
    (rel) => (isFrontendSource(rel) && existsSync(join(root, rel))) || (rel.startsWith(`${FRONTEND}/`) && FRONTEND_CONFIG.has(rel.slice(FRONTEND.length + 1))),
  );
  if (frontendChanged) {
    for (const problem of typecheckFrontend(root)) report.push(`${problem} (${root})`);
    const specs = [...new Set(changed.filter((rel) => existsSync(join(root, rel))).flatMap((rel) => relatedSpecs(root, rel)))];
    const problem = runFrontendSpecs(root, specs);
    if (problem) report.push(`${problem} (${root})`);
  }

  if (changed.some(isApiChange)) {
    for (const problem of runDotnet(root)) report.push(`${problem} (${root})`);
  }
}

if (report.length > 0) block(`Fix these before you finish:\n\n${report.join('\n\n')}`);

clearRoots(sessionId);
process.exit(0);
