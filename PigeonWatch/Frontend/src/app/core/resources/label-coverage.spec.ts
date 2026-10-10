import {
  ASTWithSource,
  Interpolation,
  TmplAstBoundText,
  TmplAstRecursiveVisitor,
  TmplAstText,
  parseTemplate,
  tmplAstVisitAll,
} from '@angular/compiler';
import { readFileSync, readdirSync } from 'node:fs';
import { join, relative, resolve } from 'node:path';
import process from 'node:process';

const root = process.cwd();
const appDir = resolve(root, 'src', 'app');
const generatedDir = resolve(appDir, 'core', 'resources', 'generated');
const snapshotFile = join(generatedDir, 'ui-labels.en.json');

const allowedTextNodes: readonly string[] = ['·'];

const pipeUsage = /'([^']+)'\s*\|\s*translate\b/g;
const callUsage = /\btranslate\(\s*'([^']+)'/g;

function sourceFiles(extension: string): string[] {
  return readdirSync(appDir, { recursive: true, withFileTypes: true })
    .filter((entry) => entry.isFile() && entry.name.endsWith(extension))
    .map((entry) => join(entry.parentPath, entry.name))
    .filter((file) => !file.endsWith('.spec.ts') && !file.startsWith(generatedDir));
}

function displayPath(file: string): string {
  return relative(root, file).split('\\').join('/');
}

function usedKeys(): Map<string, string[]> {
  const keys = new Map<string, string[]>();

  for (const file of [...sourceFiles('.html'), ...sourceFiles('.ts')]) {
    const source = readFileSync(file, 'utf8');

    for (const pattern of [pipeUsage, callUsage]) {
      for (const match of source.matchAll(pattern)) {
        keys.set(match[1], [...(keys.get(match[1]) ?? []), displayPath(file)]);
      }
    }
  }

  return keys;
}

class LiteralTextCollector extends TmplAstRecursiveVisitor {
  readonly literals: string[] = [];

  override visitText(text: TmplAstText): void {
    this.collect(text.value);
  }

  override visitBoundText(text: TmplAstBoundText): void {
    const value = text.value instanceof ASTWithSource ? text.value.ast : text.value;

    if (value instanceof Interpolation) value.strings.forEach((part) => this.collect(part));
  }

  private collect(value: string): void {
    const literal = value.trim();

    if (literal !== '' && !allowedTextNodes.includes(literal)) this.literals.push(literal);
  }
}

function literalTextOf(file: string): string[] {
  const parsed = parseTemplate(readFileSync(file, 'utf8'), file);

  expect(parsed.errors ?? [], `${displayPath(file)} parse errors`).toEqual([]);

  const collector = new LiteralTextCollector();
  tmplAstVisitAll(collector, parsed.nodes);

  return collector.literals;
}

describe('label coverage', () => {
  const labels = JSON.parse(readFileSync(snapshotFile, 'utf8')) as Record<string, string>;
  const keys = usedKeys();

  it('finds the label usages it guards', () => {
    expect([...keys.keys()]).toEqual(
      expect.arrayContaining([
        'auth.login.title',
        'auth.errors.registrationFailed',
        'header.profile',
        'warmup.failed.retry',
      ]),
    );
  });

  it('uses only keys that exist in the en snapshot', () => {
    const missing = [...keys.entries()]
      .filter(([key]) => !Object.hasOwn(labels, key))
      .map(([key, files]) => `${key} (${[...new Set(files)].join(', ')})`);

    expect(missing).toEqual([]);
  });

  it('has warm-up messages for the rotation', () => {
    expect(Object.keys(labels).filter((key) => key.startsWith('warmup.messages.'))).not.toEqual([]);
  });

  it.each(sourceFiles('.html').map(displayPath))('%s has no literal text', (path) => {
    expect(literalTextOf(resolve(root, path))).toEqual([]);
  });
});
