import { block, editContext, readPayload } from './lib.mjs';
import { relatedSpecs, riskApiScopes, runFrontendSpecs, runIntegrationScopes } from './checks.mjs';

const context = editContext(readPayload());
if (!context) process.exit(0);

const { root, rel } = context;
const problems = [runFrontendSpecs(root, relatedSpecs(root, rel)), runIntegrationScopes(root, riskApiScopes(rel))].filter(Boolean);

if (problems.length > 0) block(`Tests related to ${rel} fail:\n${problems.join('\n\n')}`);

process.exit(0);
