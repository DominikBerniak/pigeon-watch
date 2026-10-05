import { mkdirSync, readFileSync, readdirSync, rmSync, writeFileSync } from 'node:fs';
import { join, resolve } from 'node:path';
import process from 'node:process';

const defaultCulture = 'en';
const resxFilePattern = /^UiLabels(?:\.([A-Za-z0-9-]+))?\.resx$/;
const generatedFilePattern = /^ui-labels\.[A-Za-z0-9-]+\.json$/;
const dataElementPattern = /<data\b([^>]*?)(?:\/>|>([\s\S]*?)<\/data>)/g;
const valueElementPattern = /<value\s*\/>|<value(?:\s[^>]*)?>([\s\S]*?)<\/value>/;
const namedEntities = { amp: '&', lt: '<', gt: '>', quot: '"', apos: "'" };

function decodeEntities(text) {
  return text.replace(/&(#x[0-9a-fA-F]+|#[0-9]+|[a-zA-Z]+);/g, (match, entity) => {
    if (entity.startsWith('#x')) return String.fromCodePoint(Number.parseInt(entity.slice(2), 16));

    if (entity.startsWith('#')) return String.fromCodePoint(Number.parseInt(entity.slice(1), 10));

    return Object.hasOwn(namedEntities, entity) ? namedEntities[entity] : match;
  });
}

function readAttribute(attributes, name) {
  const match = new RegExp(`\\b${name}\\s*=\\s*"([^"]*)"`).exec(attributes);

  return match ? decodeEntities(match[1]) : undefined;
}

export function parseResx(xml) {
  const labels = {};

  for (const match of xml.matchAll(dataElementPattern)) {
    const attributes = match[1];
    const name = readAttribute(attributes, 'name');

    if (name === undefined || readAttribute(attributes, 'type') !== undefined) continue;

    if (Object.hasOwn(labels, name)) throw new Error(`Duplicate resource key "${name}".`);

    const value = valueElementPattern.exec(match[2] ?? '');
    labels[name] = value?.[1] === undefined ? '' : decodeEntities(value[1]);
  }

  return labels;
}

function cultureOf(fileName) {
  const match = resxFilePattern.exec(fileName);

  if (!match) return undefined;

  return (match[1] ?? defaultCulture).toLowerCase();
}

function quote(value) {
  return `'${value}'`;
}

function renderSnapshots(cultures) {
  const lazyCultures = cultures.filter((culture) => culture !== defaultCulture);
  const loaders = [
    `  ${quote(defaultCulture)}: () => Promise.resolve(defaultLabels),`,
    ...lazyCultures.map(
      (culture) =>
        `  ${quote(culture)}: () => import('./ui-labels.${culture}.json').then((module) => module.default),`,
    ),
  ];

  return [
    `import defaultLabelsJson from './ui-labels.${defaultCulture}.json';`,
    '',
    'export type UiLabels = Readonly<Record<string, string>>;',
    '',
    `export const defaultCulture = ${quote(defaultCulture)};`,
    '',
    `export const supportedCultures: readonly string[] = [${cultures.map(quote).join(', ')}];`,
    '',
    'export const defaultLabels: UiLabels = defaultLabelsJson;',
    '',
    'export const snapshots: Readonly<Record<string, () => Promise<UiLabels>>> = {',
    ...loaders,
    '};',
    '',
  ].join('\n');
}

export function generate({ resxDir, outDir }) {
  const sources = readdirSync(resxDir)
    .map((fileName) => ({ fileName, culture: cultureOf(fileName) }))
    .filter((source) => source.culture !== undefined)
    .sort((left, right) => left.culture.localeCompare(right.culture));

  const labelsByCulture = new Map();

  for (const source of sources) {
    if (labelsByCulture.has(source.culture)) {
      throw new Error(`Culture "${source.culture}" is defined by more than one resx file.`);
    }

    labelsByCulture.set(
      source.culture,
      parseResx(readFileSync(join(resxDir, source.fileName), 'utf8')),
    );
  }

  const defaultLabels = labelsByCulture.get(defaultCulture);

  if (!defaultLabels) throw new Error(`No neutral UiLabels.resx found in ${resxDir}.`);

  for (const [culture, labels] of labelsByCulture) {
    const unknownKeys = Object.keys(labels).filter((key) => !Object.hasOwn(defaultLabels, key));

    if (unknownKeys.length > 0) {
      throw new Error(
        `Culture "${culture}" has keys missing from "${defaultCulture}": ${unknownKeys.join(', ')}.`,
      );
    }
  }

  const cultures = [
    defaultCulture,
    ...[...labelsByCulture.keys()].filter((culture) => culture !== defaultCulture),
  ];

  mkdirSync(outDir, { recursive: true });

  readdirSync(outDir)
    .filter((fileName) => generatedFilePattern.test(fileName))
    .forEach((fileName) => rmSync(join(outDir, fileName)));

  for (const culture of cultures) {
    const json = `${JSON.stringify(labelsByCulture.get(culture), null, 2)}\n`;
    writeFileSync(join(outDir, `ui-labels.${culture}.json`), json, 'utf8');
  }

  writeFileSync(join(outDir, 'snapshots.ts'), renderSnapshots(cultures), 'utf8');

  return { cultures, keyCount: Object.keys(defaultLabels).length };
}

const scriptPath = resolve(process.cwd(), 'scripts', 'generate-resources.mjs');
const invokedDirectly = process.argv[1] !== undefined && resolve(process.argv[1]) === scriptPath;

if (invokedDirectly) {
  try {
    const result = generate({
      resxDir: resolve(process.cwd(), '..', 'Api', 'BusinessObjects', 'Resources'),
      outDir: resolve(process.cwd(), 'src', 'app', 'core', 'resources', 'generated'),
    });
    console.log(
      `Generated UI label snapshots for ${result.cultures.join(', ')} (${result.keyCount} keys).`,
    );
  } catch (error) {
    console.error(error instanceof Error ? error.message : error);
    process.exitCode = 1;
  }
}
