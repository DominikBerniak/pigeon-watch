import { after, before, describe, it } from 'node:test';
import assert from 'node:assert/strict';
import { spawnSync } from 'node:child_process';
import { randomUUID } from 'node:crypto';
import { chmodSync, cpSync, existsSync, mkdirSync, mkdtempSync, readFileSync, realpathSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { delimiter, dirname, join } from 'node:path';

const HOOKS_DIR = join(import.meta.dirname, '..');
const SETTINGS = join(HOOKS_DIR, '..', 'settings.json');
const SCRIPTS = ['lib.mjs', 'checks.mjs', 'check-edited-file.mjs', 'related-tests.mjs', 'end-of-turn.mjs'];

const FRONTEND = 'PigeonWatch/Frontend';
const BAD_TS = `${FRONTEND}/src/app/bad.ts`;
const BAD_SPEC = `${FRONTEND}/src/app/bad.spec.ts`;
const OK_TS = `${FRONTEND}/src/app/ok.ts`;
const OK_SPEC = `${FRONTEND}/src/app/ok.spec.ts`;
const OK_CS = 'PigeonWatch/Api/WebApi/Ok.cs';
const NEW_CS = 'PigeonWatch/Api/WebApi/New.cs';

const TOOL_STUB_HEADER = `
import { appendFileSync, existsSync, readdirSync, readFileSync } from 'node:fs';
import { join, relative } from 'node:path';
const args = process.argv.slice(2);
if (process.env.HOOK_STUB_LOG) appendFileSync(process.env.HOOK_STUB_LOG, process.env.STUB_NAME + ' ' + args.join(' ') + '\\n');
function walk(dir) {
  if (!existsSync(dir)) return [];
  return readdirSync(dir, { withFileTypes: true }).flatMap((entry) => {
    const full = join(dir, entry.name);
    return entry.isDirectory() ? walk(full) : [full];
  });
}
`;

const TSC_STUB = `process.env.STUB_NAME = 'tsc';${TOOL_STUB_HEADER}
const spec = args[args.indexOf('-p') + 1].includes('spec');
const bad = walk('src').filter((file) => file.endsWith('.ts') && file.endsWith('.spec.ts') === spec && readFileSync(file, 'utf8').includes('BROKEN_TYPE'));
for (const file of bad) console.log(relative('.', file).replace(/\\\\/g, '/') + '(1,1): error TS2322: stub broken type');
process.exit(bad.length > 0 ? 2 : 0);
`;

const NGC_STUB = `process.env.STUB_NAME = 'ngc';${TOOL_STUB_HEADER}
const bad = walk('src').filter((file) => /\\.(ts|html)$/.test(file) && !file.endsWith('.spec.ts') && readFileSync(file, 'utf8').includes('BROKEN_TYPE'));
for (const file of bad) console.log(relative('.', file).replace(/\\\\/g, '/') + ':1:1 - error TS2322: stub broken type');
process.exit(bad.length > 0 ? 1 : 0);
`;

const NG_STUB = `process.env.STUB_NAME = 'ng';${TOOL_STUB_HEADER}
const includes = args.flatMap((arg, index) => (args[index - 1] === '--include' ? [arg] : []));
const failing = includes.filter((file) => existsSync(file) && readFileSync(file, 'utf8').includes('FAIL_TEST'));
for (const file of failing) console.log('FAIL ' + file + ' stub failing assertion');
process.exit(failing.length > 0 ? 1 : 0);
`;

const DOTNET_STUB = `process.env.STUB_NAME = 'dotnet';${TOOL_STUB_HEADER}
if (process.env.HOOK_STUB_DOTNET_BROKEN) process.exit(127);
if (args[0] === 'build') {
  const bad = walk('PigeonWatch/Api').filter((file) => file.endsWith('.cs') && readFileSync(file, 'utf8').includes('BROKEN_BUILD'));
  for (const file of bad) console.log(relative('.', file).replace(/\\\\/g, '/') + '(1,1): error CS1002: stub broken build');
  process.exit(bad.length > 0 ? 1 : 0);
}
if (args[0] === 'test') {
  const project = args[1].replace(/\\\\/g, '/');
  const dir = project.slice(0, project.lastIndexOf('/'));
  const bad = walk(dir).filter((file) => file.endsWith('.cs') && readFileSync(file, 'utf8').includes('FAIL_DOTNET_TEST'));
  for (const file of bad) console.log('Failed stub test in ' + file);
  process.exit(bad.length > 0 ? 1 : 0);
}
process.exit(0);
`;

const created = [];
const sessionIds = [];

function track(path) {
  created.push(path);
  return path;
}

function write(path, content) {
  mkdirSync(dirname(path), { recursive: true });
  writeFileSync(path, content);
}

function gitIn(cwd, ...args) {
  const result = spawnSync('git', args, { cwd, encoding: 'utf8' });
  assert.equal(result.status, 0, `git ${args.join(' ')} failed: ${result.stderr}`);
  return result.stdout;
}

function installStubs(root, { typescript = true, ngc = true } = {}) {
  const dir = join(root, FRONTEND);
  const tool = (name, binName, relative, code, enabled) => {
    if (!enabled) return;
    write(join(dir, 'node_modules', name, 'package.json'), JSON.stringify({ name, bin: { [binName]: relative } }));
    write(join(dir, 'node_modules', name, relative), code);
  };
  tool('typescript', 'tsc', 'bin/tsc', TSC_STUB, typescript);
  tool('@angular/compiler-cli', 'ngc', 'bundles/ngc.js', NGC_STUB, ngc);
  tool('@angular/cli', 'ng', 'bin/ng.js', NG_STUB, true);
  write(join(dir, 'src/app/core/resources/generated/ui-labels.en.json'), '{}');
}

function makeRepo(options = {}) {
  const root = track(realpathSync(mkdtempSync(join(tmpdir(), 'agent-hooks-'))));
  gitIn(root, 'init', '-q');
  gitIn(root, 'config', 'user.email', 'hooks@example.test');
  gitIn(root, 'config', 'user.name', 'hooks');
  gitIn(root, 'config', 'commit.gpgsign', 'false');
  write(join(root, '.gitignore'), 'node_modules/\ngenerated/\n');
  write(join(root, 'README.md'), 'readme\n');
  write(join(root, OK_TS), 'export const ok: number = 1;\n');
  write(join(root, OK_SPEC), 'describe("ok", () => {});\n');
  write(join(root, `${FRONTEND}/src/app/lonely.ts`), 'export const lonely = 1;\n');
  write(join(root, OK_CS), 'class Ok { }\n');
  for (const name of SCRIPTS) {
    cpSync(join(HOOKS_DIR, name), join(root, '.claude/hooks', name));
  }
  cpSync(SETTINGS, join(root, '.claude/settings.json'));
  gitIn(root, 'add', '-A');
  gitIn(root, 'commit', '-q', '-m', 'base');
  installStubs(root, options);
  return root;
}

const stubDir = track(realpathSync(mkdtempSync(join(tmpdir(), 'agent-hooks-bin-'))));
const stubLog = join(stubDir, 'calls.log');
write(join(stubDir, 'dotnet-stub.mjs'), DOTNET_STUB);
write(join(stubDir, 'dotnet.cmd'), '@echo off\r\nnode "%~dp0dotnet-stub.mjs" %*\r\nexit /b %ERRORLEVEL%\r\n');
write(join(stubDir, 'dotnet'), '#!/bin/sh\nexec node "$(dirname "$0")/dotnet-stub.mjs" "$@"\n');
chmodSync(join(stubDir, 'dotnet'), 0o755);

function resetLog() {
  writeFileSync(stubLog, '');
}

function calls() {
  return readFileSync(stubLog, 'utf8').split(/\r?\n/).filter(Boolean);
}

function hook(root, script, payload, options = {}) {
  const input = typeof payload === 'string' ? payload : JSON.stringify(payload);
  return spawnSync(process.execPath, [join(root, '.claude/hooks', script)], {
    cwd: options.cwd ?? root,
    input,
    encoding: 'utf8',
    env: { ...process.env, PATH: `${stubDir}${delimiter}${process.env.PATH}`, HOOK_STUB_LOG: stubLog, ...options.env },
  });
}

function newSession() {
  const id = randomUUID();
  sessionIds.push(id);
  return id;
}

function edit(root, rel, options = {}) {
  return {
    tool_name: 'Edit',
    cwd: options.cwd ?? root,
    session_id: options.session ?? newSession(),
    tool_input: { file_path: options.relative ? rel : join(root, rel) },
  };
}

function stop(root, options = {}) {
  return { cwd: options.cwd ?? root, session_id: options.session ?? newSession(), stop_hook_active: options.active ?? false };
}

function put(root, rel, content) {
  write(join(root, rel), content);
}

function findBash() {
  if (process.platform !== 'win32') return 'bash';
  const candidates = [process.env.CLAUDE_CODE_GIT_BASH_PATH, 'C:\\Program Files\\Git\\bin\\bash.exe', 'C:\\Program Files\\Git\\usr\\bin\\bash.exe'];
  return candidates.find((candidate) => candidate && existsSync(candidate)) ?? null;
}

before(() => {
  resetLog();
});

after(() => {
  for (const path of created) rmSync(path, { recursive: true, force: true });
  for (const id of sessionIds) rmSync(join(tmpdir(), 'claude-hooks', `${id}.roots`), { force: true });
});

describe('check-edited-file.mjs (per edit)', () => {
  let root;

  before(() => {
    root = makeRepo();
  });

  it('blocks a type error in an edited app file and names the file', () => {
    put(root, BAD_TS, 'export const bad = BROKEN_TYPE;\n');
    const result = hook(root, 'check-edited-file.mjs', edit(root, BAD_TS));
    assert.equal(result.status, 2);
    assert.match(result.stderr, /src\/app\/bad\.ts/);
    assert.match(result.stderr, /TS2322/);
  });

  it('blocks a type error in an edited spec file', () => {
    put(root, BAD_SPEC, 'BROKEN_TYPE\n');
    const result = hook(root, 'check-edited-file.mjs', edit(root, BAD_SPEC));
    assert.equal(result.status, 2);
    assert.match(result.stderr, /src\/app\/bad\.spec\.ts/);
  });

  it('blocks a syntax error in an edited .mjs file', () => {
    put(root, '.claude/hooks/broken.mjs', 'const = ;\n');
    const result = hook(root, 'check-edited-file.mjs', edit(root, '.claude/hooks/broken.mjs'));
    assert.equal(result.status, 2);
    assert.match(result.stderr, /Syntax error in \.claude\/hooks\/broken\.mjs/);
  });

  it('passes a clean file even while another file has errors', () => {
    const result = hook(root, 'check-edited-file.mjs', edit(root, OK_TS));
    assert.equal(result.status, 0);
    assert.equal(result.stderr, '');
  });

  it('passes a file type the check does not cover and a C# file without running dotnet', () => {
    resetLog();
    assert.equal(hook(root, 'check-edited-file.mjs', edit(root, 'README.md')).status, 0);
    assert.equal(hook(root, 'check-edited-file.mjs', edit(root, OK_CS)).status, 0);
    assert.deepEqual(calls(), []);
  });

  it('passes a path that does not exist', () => {
    const result = hook(root, 'check-edited-file.mjs', edit(root, `${FRONTEND}/src/app/missing.ts`));
    assert.equal(result.status, 0);
  });

  it('passes empty, unparseable and path-less payloads', () => {
    for (const payload of ['{}', 'not json', '', '[]', { tool_name: 'Bash', tool_input: { command: 'ls' } }]) {
      for (const script of ['check-edited-file.mjs', 'related-tests.mjs']) {
        const result = hook(root, script, payload);
        assert.equal(result.status, 0, `${script} with ${JSON.stringify(payload)}`);
        assert.equal(result.stderr, '');
      }
    }
  });

  it('resolves a relative path against the payload cwd', () => {
    const result = hook(root, 'check-edited-file.mjs', edit(root, BAD_TS, { relative: true }));
    assert.equal(result.status, 2);
  });

  it('reads a notebook_path payload', () => {
    const payload = { cwd: root, session_id: newSession(), tool_input: { notebook_path: join(root, BAD_TS) } };
    assert.equal(hook(root, 'check-edited-file.mjs', payload).status, 2);
  });

  it('checks the checkout of the edited file, not CLAUDE_PROJECT_DIR', () => {
    const other = makeRepo();
    put(other, BAD_TS, 'BROKEN_TYPE\n');
    const result = hook(root, 'check-edited-file.mjs', edit(root, OK_TS), { env: { CLAUDE_PROJECT_DIR: other } });
    assert.equal(result.status, 0);
  });

  it('skips a broken file that belongs to another repository', () => {
    const other = makeRepo();
    put(other, BAD_TS, 'BROKEN_TYPE\n');
    const result = hook(root, 'check-edited-file.mjs', edit(other, BAD_TS, { cwd: root }));
    assert.equal(result.status, 0);
  });

  it('fails visibly when the typechecker is not installed', () => {
    const bare = makeRepo({ typescript: false, ngc: false });
    const spec = hook(bare, 'check-edited-file.mjs', edit(bare, OK_SPEC));
    assert.equal(spec.status, 2);
    assert.match(spec.stderr, /typescript is not installed/);
    const app = hook(bare, 'check-edited-file.mjs', edit(bare, OK_TS));
    assert.equal(app.status, 2);
    assert.match(app.stderr, /@angular\/compiler-cli is not installed/);
  });
});

describe('related-tests.mjs (per edit)', () => {
  let root;

  before(() => {
    root = makeRepo();
  });

  it('blocks when the sibling spec of the edited file fails', () => {
    put(root, OK_SPEC, 'FAIL_TEST\n');
    resetLog();
    const result = hook(root, 'related-tests.mjs', edit(root, OK_TS));
    assert.equal(result.status, 2);
    assert.match(result.stderr, /Frontend specs fail \(ok\.spec\.ts\)/);
    assert.match(result.stderr, /FAIL src\/app\/ok\.spec\.ts/);
    assert.deepEqual(calls(), ['ng test --watch=false --include src/app/ok.spec.ts']);
  });

  it('passes when the related spec is green', () => {
    put(root, OK_SPEC, 'describe("ok", () => {});\n');
    const result = hook(root, 'related-tests.mjs', edit(root, OK_SPEC));
    assert.equal(result.status, 0);
    assert.equal(result.stderr, '');
  });

  it('passes without running anything when no spec matches the edited file', () => {
    resetLog();
    const result = hook(root, 'related-tests.mjs', edit(root, `${FRONTEND}/src/app/lonely.ts`));
    assert.equal(result.status, 0);
    assert.deepEqual(calls(), []);
  });
});

describe('related-tests.mjs (risk scope: registration during a cold start)', () => {
  const APP = `${FRONTEND}/src/app`;
  const WARMUP_TS = `${APP}/core/warmup/warmup.interceptor.ts`;
  const WARMUP_SPEC = `${APP}/core/warmup/warmup.interceptor.spec.ts`;
  const COLD_START_SPEC = `${APP}/core/auth/register-cold-start.spec.ts`;
  const REGISTER_PAGE_SPEC = `${APP}/features/auth/register-page/register-page.spec.ts`;
  const TOKEN_STORE_TS = `${APP}/core/auth/token-store.ts`;
  const TOKEN_STORE_SPEC = `${APP}/core/auth/token-store.spec.ts`;
  const SCOPE_INCLUDES = [
    'src/app/core/auth/register-cold-start.spec.ts',
    'src/app/core/warmup/warmup.interceptor.spec.ts',
    'src/app/features/auth/register-page/register-page.spec.ts',
  ];
  const ACCOUNT_SERVICE = 'PigeonWatch/Api/BusinessLogic/Services/AccountService.cs';
  const REGISTER_CONTRACT = 'PigeonWatch/Api/IntegrationTests/RegisterContractTests.cs';
  const OTHER_CONTROLLER = 'PigeonWatch/Api/WebApi/Controllers/SightingsController.cs';
  const GREEN = 'describe("green", () => {});\n';
  let root;

  before(() => {
    root = makeRepo();
    put(root, WARMUP_TS, 'export const warmupBudgetMs = 1;\n');
    put(root, TOKEN_STORE_TS, 'export const tokenStore = 1;\n');
    put(root, ACCOUNT_SERVICE, 'class AccountService { }\n');
    put(root, OTHER_CONTROLLER, 'class SightingsController { }\n');
  });

  function resetSpecs() {
    for (const spec of [WARMUP_SPEC, COLD_START_SPEC, REGISTER_PAGE_SPEC, TOKEN_STORE_SPEC]) put(root, spec, GREEN);
    put(root, REGISTER_CONTRACT, 'class RegisterContractTests { }\n');
  }

  it('blocks an edit to a module file when a scope spec fails although its own spec is green', () => {
    resetSpecs();
    put(root, COLD_START_SPEC, 'FAIL_TEST\n');
    resetLog();
    const result = hook(root, 'related-tests.mjs', edit(root, WARMUP_TS));
    assert.equal(result.status, 2);
    assert.match(result.stderr, /Tests related to PigeonWatch\/Frontend\/src\/app\/core\/warmup\/warmup\.interceptor\.ts fail/);
    assert.match(result.stderr, /FAIL src\/app\/core\/auth\/register-cold-start\.spec\.ts/);
    const [call, ...rest] = calls();
    assert.deepEqual(rest, []);
    const includes = call.split(' ').filter((_, index, all) => all[index - 1] === '--include');
    assert.deepEqual([...includes].sort(), SCOPE_INCLUDES);
  });

  it('passes an edit to a module file when every scope spec is green', () => {
    resetSpecs();
    const result = hook(root, 'related-tests.mjs', edit(root, WARMUP_TS));
    assert.equal(result.status, 0);
    assert.equal(result.stderr, '');
  });

  it('runs only its own spec for a file outside the module, even with a failing scope spec', () => {
    resetSpecs();
    put(root, COLD_START_SPEC, 'FAIL_TEST\n');
    resetLog();
    const result = hook(root, 'related-tests.mjs', edit(root, TOKEN_STORE_TS));
    assert.equal(result.status, 0);
    assert.deepEqual(calls(), ['ng test --watch=false --include src/app/core/auth/token-store.spec.ts']);
  });

  it('fails visibly when a spec named by the scope map no longer exists', () => {
    resetSpecs();
    rmSync(join(root, REGISTER_PAGE_SPEC));
    resetLog();
    const result = hook(root, 'related-tests.mjs', edit(root, WARMUP_TS));
    assert.equal(result.status, 2);
    assert.match(result.stderr, /do not exist: PigeonWatch\/Frontend\/src\/app\/features\/auth\/register-page\/register-page\.spec\.ts/);
    assert.deepEqual(calls(), []);
  });

  it('runs only the registration integration tests for an API file of the module and blocks on a failure', () => {
    resetSpecs();
    put(root, REGISTER_CONTRACT, 'FAIL_DOTNET_TEST\n');
    resetLog();
    const result = hook(root, 'related-tests.mjs', edit(root, ACCOUNT_SERVICE));
    assert.equal(result.status, 2);
    assert.match(result.stderr, /Risk #1: registration during a cold start: API integration tests fail/);
    assert.match(result.stderr, /Failed stub test in .*RegisterContractTests\.cs/);
    const recorded = calls();
    assert.equal(recorded.length, 1);
    assert.match(recorded[0], /^dotnet test .*PigeonWatch\.IntegrationTests\.csproj --nologo --filter FullyQualifiedName~RegisterContractTests$/);
  });

  it('passes an API file of the module when the registration integration tests are green', () => {
    resetSpecs();
    const result = hook(root, 'related-tests.mjs', edit(root, ACCOUNT_SERVICE));
    assert.equal(result.status, 0);
    assert.equal(result.stderr, '');
  });

  it('runs no tests for an API file outside the module', () => {
    resetSpecs();
    put(root, REGISTER_CONTRACT, 'FAIL_DOTNET_TEST\n');
    resetLog();
    const result = hook(root, 'related-tests.mjs', edit(root, OTHER_CONTROLLER));
    assert.equal(result.status, 0);
    assert.deepEqual(calls(), []);
  });

  it('fails visibly when dotnet cannot run', () => {
    resetSpecs();
    const result = hook(root, 'related-tests.mjs', edit(root, ACCOUNT_SERVICE), { env: { HOOK_STUB_DOTNET_BROKEN: '1' } });
    assert.equal(result.status, 2);
    assert.match(result.stderr, /API integration tests fail/);
  });

  it('is swept at end of turn: a changed module file runs every scope spec once', () => {
    const sweepRoot = makeRepo();
    for (const spec of [WARMUP_SPEC, COLD_START_SPEC, REGISTER_PAGE_SPEC]) put(sweepRoot, spec, GREEN);
    put(sweepRoot, WARMUP_TS, 'export const warmupBudgetMs = 1;\n');
    gitIn(sweepRoot, 'add', '-A');
    gitIn(sweepRoot, 'commit', '-q', '-m', 'module');
    put(sweepRoot, WARMUP_TS, 'export const warmupBudgetMs = 2;\n');
    put(sweepRoot, COLD_START_SPEC, 'FAIL_TEST\n');
    resetLog();
    const result = hook(sweepRoot, 'end-of-turn.mjs', stop(sweepRoot));
    assert.equal(result.status, 2);
    assert.match(result.stderr, /FAIL src\/app\/core\/auth\/register-cold-start\.spec\.ts/);
    const specRuns = calls().filter((call) => call.startsWith('ng test'));
    assert.equal(specRuns.length, 1);
    for (const include of SCOPE_INCLUDES) assert.ok(specRuns[0].includes(include), include);
  });
});

describe('end-of-turn.mjs (Stop)', () => {
  let root;

  before(() => {
    root = makeRepo();
  });

  function restore() {
    gitIn(root, 'checkout', '-q', '--', '.');
    gitIn(root, 'clean', '-fdq', '-e', 'node_modules', '-e', 'generated');
  }

  it('passes without running any check when nothing changed', () => {
    resetLog();
    const result = hook(root, 'end-of-turn.mjs', stop(root));
    assert.equal(result.status, 0);
    assert.deepEqual(calls(), []);
  });

  it('passes without running any check when only documentation changed', () => {
    put(root, 'README.md', 'changed\n');
    resetLog();
    const result = hook(root, 'end-of-turn.mjs', stop(root));
    assert.equal(result.status, 0);
    assert.deepEqual(calls(), []);
    restore();
  });

  it('blocks on a type error written to disk without any per-edit payload', () => {
    put(root, BAD_TS, 'BROKEN_TYPE\n');
    const result = hook(root, 'end-of-turn.mjs', stop(root));
    assert.equal(result.status, 2);
    assert.match(result.stderr, /Fix these before you finish/);
    assert.match(result.stderr, /src\/app\/bad\.ts/);
    restore();
  });

  it('lets the agent finish when the retry flag is set even though the error is still there', () => {
    put(root, BAD_TS, 'BROKEN_TYPE\n');
    assert.equal(hook(root, 'end-of-turn.mjs', stop(root, { active: true })).status, 0);
    assert.equal(hook(root, 'end-of-turn.mjs', { ...stop(root), loop_count: 1 }).status, 0);
    restore();
  });

  it('blocks on a failing spec of a changed file that never went through a per-edit hook', () => {
    put(root, OK_TS, 'export const ok: number = 2;\n');
    put(root, OK_SPEC, 'FAIL_TEST\n');
    const result = hook(root, 'end-of-turn.mjs', stop(root));
    assert.equal(result.status, 2);
    assert.match(result.stderr, /Frontend specs fail \(ok\.spec\.ts\)/);
    restore();
  });

  it('passes a clean frontend change and runs the related spec once', () => {
    put(root, OK_TS, 'export const ok: number = 3;\n');
    resetLog();
    const result = hook(root, 'end-of-turn.mjs', stop(root));
    assert.equal(result.status, 0);
    assert.equal(result.stderr, '');
    assert.deepEqual(calls(), ['ngc -p tsconfig.app.json --noEmit', 'tsc -p tsconfig.spec.json --noEmit --pretty false', 'ng test --watch=false --include src/app/ok.spec.ts']);
    restore();
  });

  it('blocks on a build error in a new C# file and skips the tests', () => {
    put(root, NEW_CS, 'BROKEN_BUILD\n');
    resetLog();
    const result = hook(root, 'end-of-turn.mjs', stop(root));
    assert.equal(result.status, 2);
    assert.match(result.stderr, /dotnet build fails/);
    assert.match(result.stderr, /New\.cs/);
    assert.deepEqual(calls().filter((line) => line.startsWith('dotnet test')), []);
    restore();
  });

  it('blocks on a failing .NET test after a green build', () => {
    put(root, 'PigeonWatch/Api/UnitTests/Failing.cs', 'FAIL_DOTNET_TEST\n');
    const result = hook(root, 'end-of-turn.mjs', stop(root));
    assert.equal(result.status, 2);
    assert.match(result.stderr, /dotnet test fails for UnitTests/);
    restore();
  });

  it('passes a clean C# change and runs the build and all three test projects', () => {
    put(root, OK_CS, 'class Ok { int x; }\n');
    resetLog();
    const result = hook(root, 'end-of-turn.mjs', stop(root));
    assert.equal(result.status, 0);
    const lines = calls();
    assert.equal(lines.filter((line) => line.startsWith('dotnet build')).length, 1);
    assert.equal(lines.filter((line) => line.startsWith('dotnet test')).length, 3);
    restore();
  });

  it('fails visibly when dotnet exits non-zero without any output', () => {
    put(root, OK_CS, 'class Ok { int y; }\n');
    const result = hook(root, 'end-of-turn.mjs', stop(root), { env: { HOOK_STUB_DOTNET_BROKEN: '1' } });
    assert.equal(result.status, 2);
    assert.match(result.stderr, /dotnet build fails/);
    restore();
  });

  it('finds the checkout from a subdirectory cwd', () => {
    put(root, BAD_TS, 'BROKEN_TYPE\n');
    const result = hook(root, 'end-of-turn.mjs', stop(root, { cwd: join(root, FRONTEND) }));
    assert.equal(result.status, 2);
    restore();
  });

  it('ignores CLAUDE_PROJECT_DIR pointing at another checkout that holds an error', () => {
    const other = makeRepo();
    put(other, BAD_TS, 'BROKEN_TYPE\n');
    const result = hook(root, 'end-of-turn.mjs', stop(root), { env: { CLAUDE_PROJECT_DIR: other } });
    assert.equal(result.status, 0);
  });

  it('passes with a message when the cwd is not inside a git checkout', () => {
    const plain = track(realpathSync(mkdtempSync(join(tmpdir(), 'agent-hooks-plain-'))));
    const result = hook(root, 'end-of-turn.mjs', stop(root, { cwd: plain }));
    assert.equal(result.status, 0);
    assert.match(result.stdout, /no git checkout/);
  });

  it('passes on empty and unparseable payloads', () => {
    for (const payload of ['{}', 'not json', '']) {
      assert.equal(hook(root, 'end-of-turn.mjs', payload).status, 0, JSON.stringify(payload));
    }
  });
});

describe('sibling worktree', () => {
  it('checks an edit in a worktree and sweeps it at end of turn from the session checkout', () => {
    const main = makeRepo();
    const worktree = join(track(realpathSync(mkdtempSync(join(tmpdir(), 'agent-hooks-wt-')))), 'wt');
    gitIn(main, 'worktree', 'add', '-q', '-b', 'side', worktree);
    installStubs(worktree);
    const session = newSession();
    put(worktree, BAD_TS, 'BROKEN_TYPE\n');

    const perEdit = hook(main, 'check-edited-file.mjs', edit(worktree, BAD_TS, { cwd: main, session }));
    assert.equal(perEdit.status, 2);

    const sweep = hook(main, 'end-of-turn.mjs', stop(main, { session }));
    assert.equal(sweep.status, 2);
    assert.match(sweep.stderr, /src\/app\/bad\.ts/);
    assert.match(sweep.stderr, /wt\)/);
  });

  it('runs every configured command from a worktree with a stale CLAUDE_PROJECT_DIR', (context) => {
    const bash = findBash();
    if (!bash) return context.skip('no bash available to run the configured commands');
    const main = makeRepo();
    const worktree = join(track(realpathSync(mkdtempSync(join(tmpdir(), 'agent-hooks-cfg-')))), 'wt');
    gitIn(main, 'worktree', 'add', '-q', '-b', 'cfg', worktree);
    installStubs(worktree);
    const settings = JSON.parse(readFileSync(SETTINGS, 'utf8'));
    const commands = Object.values(settings.hooks).flatMap((groups) => groups.flatMap((group) => group.hooks.map((entry) => entry.command)));
    assert.equal(commands.length, 3);
    for (const command of commands) {
      const result = spawnSync(bash, ['-c', command], {
        cwd: worktree,
        input: '{}',
        encoding: 'utf8',
        env: { ...process.env, PATH: `${stubDir}${delimiter}${process.env.PATH}`, CLAUDE_PROJECT_DIR: join(tmpdir(), 'agent-hooks-missing') },
      });
      assert.equal(result.status, 0, `${command}\n${result.stderr}`);
      assert.doesNotMatch(result.stderr, /No such file|Cannot find module|not found/);
    }
  });
});

describe('settings.json', () => {
  const settings = JSON.parse(readFileSync(SETTINGS, 'utf8'));
  const entries = Object.values(settings.hooks).flatMap((groups) => groups.flatMap((group) => group.hooks));

  it('uses timeouts in seconds', () => {
    for (const entry of entries) {
      assert.ok(Number.isInteger(entry.timeout) && entry.timeout > 0 && entry.timeout <= 600, entry.command);
    }
  });

  it('points every command at a script that exists', () => {
    for (const entry of entries) {
      const script = /\/\.claude\/hooks\/([\w-]+\.mjs)/.exec(entry.command)?.[1];
      assert.ok(script && existsSync(join(HOOKS_DIR, script)), entry.command);
    }
  });

  it('registers the per-edit hooks on Write and Edit and the sweep on Stop', () => {
    assert.equal(settings.hooks.PostToolUse[0].matcher, 'Write|Edit');
    assert.equal(settings.hooks.PostToolUse[0].hooks.length, 2);
    assert.ok(settings.hooks.PostToolUse[0].hooks[1].timeout >= 120);
    assert.equal(settings.hooks.Stop.length, 1);
  });
});
