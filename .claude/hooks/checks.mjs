import { existsSync } from 'node:fs';
import { basename, dirname, join } from 'node:path';
import { API, FRONTEND, INTEGRATION_PROJECT, SOLUTION, TEST_PROJECTS, nodeBin, run, tailLines } from './lib.mjs';

const GENERATED_LABELS = 'src/app/core/resources/generated/ui-labels.en.json';

export function frontendDir(root) {
  return join(root, FRONTEND);
}

export function isFrontendSource(rel) {
  return rel.startsWith(`${FRONTEND}/src/`) && (rel.endsWith('.ts') || rel.endsWith('.html'));
}

export function isSpec(rel) {
  return rel.endsWith('.spec.ts');
}

export function isApiChange(rel) {
  return rel.startsWith(`${API}/`) && !rel.endsWith('.md');
}

const APP = `${FRONTEND}/src/app`;

export const RISK_SCOPES = [
  {
    name: 'Risk #1: registration during a cold start',
    frontendSources: [
      'core/auth/auth-api',
      'core/auth/auth.interceptor',
      'core/auth/session.service',
      'core/warmup/',
      'features/auth/register-page/',
      'shared/auth/auth-errors',
    ],
    frontendSpecs: [
      'core/auth/register-cold-start.spec.ts',
      'core/warmup/warmup.interceptor.spec.ts',
      'features/auth/register-page/register-page.spec.ts',
    ],
    apiSources: [
      'WebApi/Controllers/AccountController',
      'WebApi/Mappers/RegisterRequestMapper',
      'WebApi/Models/Register',
      'WebApi/ViewModelCreators/RegisteredAccount',
      'BusinessLogic/Services/AccountService',
      'Data/Repositories/AccountRepository',
      'IntegrationTests/RegisterContractTests',
      'IntegrationTests/AccountApiClient',
      'IntegrationTests/PigeonWatchApiFactory',
    ],
    apiFilter: 'FullyQualifiedName~RegisterContractTests',
  },
];

export function relatedSpec(root, rel) {
  if (!isFrontendSource(rel)) return null;
  const candidate = isSpec(rel) ? rel : rel.replace(/\.(ts|html)$/, '.spec.ts');
  return existsSync(join(root, candidate)) ? candidate : null;
}

export function relatedSpecs(root, rel) {
  const specs = new Set();
  const own = relatedSpec(root, rel);
  if (own) specs.add(own);
  if (isFrontendSource(rel) && !isSpec(rel)) {
    for (const scope of RISK_SCOPES) {
      if (scope.frontendSources.some((prefix) => rel.startsWith(`${APP}/${prefix}`))) {
        for (const spec of scope.frontendSpecs) specs.add(`${APP}/${spec}`);
      }
    }
  }
  return [...specs];
}

export function riskApiScopes(rel) {
  return RISK_SCOPES.filter((scope) => scope.apiSources.some((prefix) => rel.startsWith(`${API}/${prefix}`)));
}

function missingTool(name) {
  return `${name} is not installed in ${FRONTEND}/node_modules: run "npm ci" in ${FRONTEND}.`;
}

function ensureGeneratedResources(root) {
  const dir = frontendDir(root);
  if (existsSync(join(dir, GENERATED_LABELS))) return null;
  const result = run(process.execPath, ['scripts/generate-resources.mjs'], { cwd: dir });
  if (result.ok) return null;
  return `Generated UI labels are missing and "node scripts/generate-resources.mjs" failed:\n${tailLines(result.output, 20)}`;
}

function diagnosticsFor(output, rels) {
  const lines = output.split(/\r?\n/);
  return lines.filter((line) => rels.some((rel) => line.includes(`${rel}:`) || line.includes(`${rel}(`)));
}

function describeTypecheck(label, result, rels) {
  if (result.ok) return null;
  const hasDiagnostics = /error (TS|NG)\d+/.test(result.output);
  if (!hasDiagnostics) {
    return `${label} could not run:\n${tailLines(result.output, 20)}`;
  }
  if (!rels) return `${label} fails:\n${tailLines(result.output, 60)}`;
  const mine = diagnosticsFor(result.output, rels);
  return mine.length > 0 ? `${label} reports errors in the edited file:\n${mine.join('\n')}` : null;
}

export function typecheckFrontend(root, { app = true, spec = true, only = null } = {}) {
  const dir = frontendDir(root);
  const ngc = nodeBin(dir, '@angular/compiler-cli', 'ngc');
  const tsc = nodeBin(dir, 'typescript', 'tsc');
  if (app && !ngc) return [missingTool('@angular/compiler-cli')];
  if (spec && !tsc) return [missingTool('typescript')];
  const generated = ensureGeneratedResources(root);
  if (generated) return [generated];
  const problems = [];
  if (app) {
    const result = run(process.execPath, [ngc, '-p', 'tsconfig.app.json', '--noEmit'], { cwd: dir });
    const problem = describeTypecheck('Angular compile (tsconfig.app.json)', result, only);
    if (problem) problems.push(problem);
  }
  if (spec) {
    const result = run(process.execPath, [tsc, '-p', 'tsconfig.spec.json', '--noEmit', '--pretty', 'false'], { cwd: dir });
    const problem = describeTypecheck('Spec typecheck (tsconfig.spec.json)', result, only);
    if (problem) problems.push(problem);
  }
  return problems;
}

export function runFrontendSpecs(root, specRels) {
  if (specRels.length === 0) return null;
  const absent = specRels.filter((rel) => !existsSync(join(root, rel)));
  if (absent.length > 0) {
    return `Spec files named by RISK_SCOPES in .claude/hooks/checks.mjs do not exist: ${absent.join(', ')}. Update the scope map.`;
  }
  const dir = frontendDir(root);
  const ng = nodeBin(dir, '@angular/cli', 'ng');
  if (!ng) return missingTool('@angular/cli');
  const generated = ensureGeneratedResources(root);
  if (generated) return generated;
  const includes = specRels.flatMap((rel) => ['--include', rel.slice(FRONTEND.length + 1)]);
  const result = run(process.execPath, [ng, 'test', '--watch=false', ...includes], { cwd: dir });
  if (result.ok) return null;
  return `Frontend specs fail (${specRels.map((rel) => basename(rel)).join(', ')}):\n${tailLines(result.output, 80)}`;
}

export function runIntegrationScopes(root, scopes) {
  if (scopes.length === 0) return null;
  const filter = scopes.map((scope) => scope.apiFilter).join('|');
  const result = run('dotnet', ['test', join(root, INTEGRATION_PROJECT), '--nologo', '--filter', filter], { cwd: root, shell: true });
  if (result.ok) return null;
  return `${scopes.map((scope) => scope.name).join(', ')}: API integration tests fail (${filter}):
${tailLines(result.output, 80)}`;
}

export function runDotnet(root) {
  const solution = join(root, SOLUTION);
  const build = run('dotnet', ['build', solution, '--nologo', '-v', 'q', '-clp:NoSummary;ErrorsOnly'], { cwd: root, shell: true });
  if (!build.ok) {
    return [`dotnet build fails (compiler errors and .editorconfig style rules):\n${tailLines(build.output, 60)}`];
  }
  const problems = [];
  for (const project of TEST_PROJECTS) {
    const result = run('dotnet', ['test', join(root, project), '--no-build', '--nologo'], { cwd: root, shell: true });
    if (!result.ok) {
      problems.push(`dotnet test fails for ${basename(dirname(project))}:\n${tailLines(result.output, 80)}`);
    }
  }
  return problems;
}
