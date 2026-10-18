import { spawnSync } from 'node:child_process';
import { appendFileSync, existsSync, mkdirSync, readFileSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { dirname, isAbsolute, join, resolve } from 'node:path';

export const FRONTEND = 'PigeonWatch/Frontend';
export const API = 'PigeonWatch/Api';
export const SOLUTION = `${API}/PigeonWatchApi.slnx`;
export const INTEGRATION_PROJECT = `${API}/IntegrationTests/PigeonWatch.IntegrationTests.csproj`;
export const TEST_PROJECTS = [
  `${API}/ArchitectureTests/PigeonWatch.ArchitectureTests.csproj`,
  `${API}/UnitTests/PigeonWatch.UnitTests.csproj`,
  INTEGRATION_PROJECT,
];

const IS_WINDOWS = process.platform === 'win32';
const ANSI = /\u001b\[[0-9;]*[A-Za-z]/g;

export function readPayload() {
  let raw = '';
  try {
    raw = readFileSync(0, 'utf8');
  } catch {
    return {};
  }
  try {
    const parsed = JSON.parse(raw);
    return parsed && typeof parsed === 'object' && !Array.isArray(parsed) ? parsed : {};
  } catch {
    return {};
  }
}

export function norm(path) {
  const unified = resolve(path).replace(/\\/g, '/');
  return IS_WINDOWS ? unified.toLowerCase() : unified;
}

export function git(dir, args) {
  const result = spawnSync('git', ['-C', dir, ...args], { encoding: 'utf8', stdio: ['ignore', 'pipe', 'pipe'] });
  if (result.error || result.status !== 0) return null;
  return result.stdout.trim();
}

export function repoRoot(dir) {
  return git(dir, ['rev-parse', '--show-toplevel']);
}

export function commonDir(root) {
  const dir = git(root, ['rev-parse', '--path-format=absolute', '--git-common-dir']);
  return dir ? norm(dir) : null;
}

export function toRelative(root, file) {
  const base = norm(root);
  const target = norm(file);
  return target.startsWith(`${base}/`) ? file.replace(/\\/g, '/').slice(root.replace(/\\/g, '/').length + 1) : null;
}

export function stripAnsi(text) {
  return text.replace(ANSI, '');
}

export function tailLines(text, count) {
  const lines = text.split(/\r?\n/);
  return lines.slice(-count).join('\n').trim();
}

export function run(command, args, options = {}) {
  const useShell = options.shell === true && IS_WINDOWS;
  const quoted = useShell ? args.map((arg) => (/[\s"]/.test(arg) ? `"${arg}"` : arg)) : args;
  const result = spawnSync(command, quoted, {
    cwd: options.cwd,
    encoding: 'utf8',
    shell: useShell,
    stdio: ['ignore', 'pipe', 'pipe'],
    maxBuffer: 64 * 1024 * 1024,
    env: {
      ...process.env,
      NO_COLOR: '1',
      FORCE_COLOR: '0',
      DOTNET_NOLOGO: '1',
      DOTNET_CLI_TELEMETRY_OPTOUT: '1',
      CI: 'true',
    },
  });
  if (result.error) {
    return { ok: false, output: `could not run ${command}: ${result.error.message}` };
  }
  const output = stripAnsi(`${result.stdout ?? ''}${result.stderr ?? ''}`);
  return { ok: result.status === 0, output, status: result.status };
}

export function nodeBin(packageDir, packageName, binName) {
  const manifest = join(packageDir, 'node_modules', packageName, 'package.json');
  if (!existsSync(manifest)) return null;
  try {
    const bin = JSON.parse(readFileSync(manifest, 'utf8')).bin;
    const relative = typeof bin === 'string' ? bin : bin?.[binName];
    if (!relative) return null;
    const full = resolve(dirname(manifest), relative);
    return existsSync(full) ? full : null;
  } catch {
    return null;
  }
}

export function sanitizeSessionId(value) {
  return String(value ?? '').replace(/[^A-Za-z0-9_-]/g, '');
}

function registryPath(sessionId) {
  return join(tmpdir(), 'claude-hooks', `${sanitizeSessionId(sessionId) || 'none'}.roots`);
}

export function registerRoot(sessionId, root) {
  if (!sanitizeSessionId(sessionId)) return;
  try {
    mkdirSync(join(tmpdir(), 'claude-hooks'), { recursive: true });
    appendFileSync(registryPath(sessionId), `${root}\n`);
  } catch {
    return;
  }
}

export function registeredRoots(sessionId) {
  if (!sanitizeSessionId(sessionId)) return [];
  try {
    return readFileSync(registryPath(sessionId), 'utf8').split(/\r?\n/).filter(Boolean);
  } catch {
    return [];
  }
}

export function clearRoots(sessionId) {
  if (!sanitizeSessionId(sessionId)) return;
  try {
    writeFileSync(registryPath(sessionId), '');
  } catch {
    return;
  }
}

export function block(message) {
  process.stderr.write(`${message.trim()}\n`);
  process.exit(2);
}

export function editContext(payload) {
  const input = payload.tool_input ?? {};
  const raw = input.file_path ?? input.notebook_path;
  if (typeof raw !== 'string' || raw === '') return null;
  const cwd = typeof payload.cwd === 'string' && payload.cwd !== '' ? payload.cwd : process.cwd();
  const file = isAbsolute(raw) ? raw : resolve(cwd, raw);
  if (!existsSync(file)) return null;
  const root = repoRoot(dirname(file));
  if (!root) return null;
  const editedRepo = commonDir(root);
  const sessionRepo = commonDir(cwd);
  if (!editedRepo || editedRepo !== sessionRepo) return null;
  const rel = toRelative(root, file);
  if (!rel) return null;
  registerRoot(payload.session_id, root);
  return { file, root, rel };
}
