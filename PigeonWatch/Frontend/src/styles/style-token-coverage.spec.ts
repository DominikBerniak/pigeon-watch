import { readFileSync, readdirSync } from 'node:fs';
import { join, relative, resolve } from 'node:path';
import process from 'node:process';

const root = process.cwd();
const appDir = resolve(root, 'src', 'app');
const stylesDir = resolve(root, 'src', 'styles');
const tokensFile = join(stylesDir, '_tokens.scss');

const allowedLengthLiterals: Readonly<Record<string, readonly string[]>> = {
  'src/styles/_common.scss': ['1px', '-1px'],
};

const colorLiteral = /#[0-9a-fA-F]{3,8}\b|\b(?:rgba?|hsla?)\(/g;
const lengthLiteral = /(?<![\w-])-?(?:\d+\.?\d*|\.\d+)(?:px|rem|em)\b/g;
const forbiddenSelector = /\.(?:mat|mdc)-|::ng-deep|!important/g;
const tokenReference = /var\(\s*(--pw-[\w-]+)/g;
const tokenDeclaration = /^\s*(--pw-[\w-]+)\s*:/gm;

function filesUnder(directory: string, extension: string): string[] {
  return readdirSync(directory, { recursive: true, withFileTypes: true })
    .filter((entry) => entry.isFile() && entry.name.endsWith(extension))
    .map((entry) => join(entry.parentPath, entry.name));
}

function displayPath(file: string): string {
  return relative(root, file).split('\\').join('/');
}

function scannedStylesheets(): string[] {
  return [
    ...filesUnder(appDir, '.scss'),
    join(stylesDir, '_base.scss'),
    join(stylesDir, '_common.scss'),
  ];
}

function matchesOf(pattern: RegExp, text: string): string[] {
  return [...text.matchAll(pattern)].map((match) => match[0]);
}

describe('style token coverage', () => {
  const declaredTokens = new Set(
    [...readFileSync(tokensFile, 'utf8').matchAll(tokenDeclaration)].map((match) => match[1]),
  );

  it('finds the stylesheets it guards', () => {
    expect(scannedStylesheets().map(displayPath)).toEqual(
      expect.arrayContaining([
        'src/app/app.scss',
        'src/styles/_base.scss',
        'src/styles/_common.scss',
      ]),
    );
  });

  it('declares the core tokens', () => {
    expect([...declaredTokens]).toEqual(
      expect.arrayContaining([
        '--pw-color-primary',
        '--pw-space-1',
        '--pw-space-8',
        '--pw-font-family',
      ]),
    );
  });

  it.each(scannedStylesheets().map(displayPath))('%s uses tokens only', (path) => {
    const css = readFileSync(resolve(root, path), 'utf8');
    const allowedLengths = allowedLengthLiterals[path] ?? [];

    expect(matchesOf(colorLiteral, css), 'color literals').toEqual([]);
    expect(
      matchesOf(lengthLiteral, css).filter((literal) => !allowedLengths.includes(literal)),
      'length literals',
    ).toEqual([]);
    expect(
      matchesOf(forbiddenSelector, css),
      'Material internals, ::ng-deep or !important',
    ).toEqual([]);
    expect(
      [...css.matchAll(tokenReference)]
        .map((match) => match[1])
        .filter((token) => !declaredTokens.has(token)),
      'undefined --pw-* tokens',
    ).toEqual([]);
  });

  it('templates have no inline style attributes', () => {
    const offenders = filesUnder(appDir, '.html')
      .filter((file) => /\sstyle\s*=\s*"/.test(readFileSync(file, 'utf8')))
      .map(displayPath);

    expect(offenders).toEqual([]);
  });
});
