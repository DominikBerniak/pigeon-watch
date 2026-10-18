import { block, editContext, readPayload, run, tailLines } from './lib.mjs';
import { isFrontendSource, isSpec, typecheckFrontend } from './checks.mjs';

const context = editContext(readPayload());
if (!context) process.exit(0);

const { root, rel } = context;

if (rel.endsWith('.mjs') || rel.endsWith('.cjs')) {
  const result = run(process.execPath, ['--check', rel], { cwd: root });
  if (!result.ok) block(`Syntax error in ${rel}:\n${tailLines(result.output, 20)}`);
  process.exit(0);
}

if (isFrontendSource(rel)) {
  const spec = isSpec(rel);
  const problems = typecheckFrontend(root, { app: !spec, spec, only: [rel.replace(/^PigeonWatch\/Frontend\//, '')] });
  if (problems.length > 0) block(`${rel}:\n${problems.join('\n\n')}`);
}

process.exit(0);
