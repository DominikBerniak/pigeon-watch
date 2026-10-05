import { mkdtempSync, readFileSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join, resolve } from 'node:path';
import process from 'node:process';
import { generate, parseResx } from '../../../../scripts/generate-resources.mjs';

const resxDir = resolve(process.cwd(), '..', 'Api', 'BusinessObjects', 'Resources');

function resx(entries: string): string {
  return `<?xml version="1.0" encoding="utf-8"?>
<root>
  <resheader name="resmimetype"><value>text/microsoft-resx</value></resheader>
${entries}
</root>`;
}

function data(name: string, value: string): string {
  return `  <data name="${name}" xml:space="preserve">\n    <value>${value}</value>\n  </data>`;
}

describe('generate-resources', () => {
  let workDir: string;

  beforeEach(() => {
    workDir = mkdtempSync(join(tmpdir(), 'pw-resources-'));
  });

  afterEach(() => {
    rmSync(workDir, { recursive: true, force: true });
  });

  function readJson(path: string): Record<string, string> {
    return JSON.parse(readFileSync(path, 'utf8')) as Record<string, string>;
  }

  it('writes ui-labels.en.json with every key of the neutral resx', () => {
    const outDir = join(workDir, 'generated');
    const expectedKeys = [
      ...readFileSync(join(resxDir, 'UiLabels.resx'), 'utf8').matchAll(/<data\s+name="([^"]+)"/g),
    ].map((match) => match[1]);

    const result = generate({ resxDir, outDir });

    const labels = readJson(join(outDir, 'ui-labels.en.json'));
    expect(result.cultures).toContain('en');
    expect(Object.keys(labels)).toEqual(expectedKeys);
    expect(labels['common.appName']).toBe('PigeonWatch');
    expect(readFileSync(join(outDir, 'snapshots.ts'), 'utf8')).toContain(
      "import defaultLabelsJson from './ui-labels.en.json';",
    );
  });

  it('decodes XML entities', () => {
    const labels = parseResx(
      resx(
        [
          data('a.amp', 'Tom &amp; Jerry'),
          data('a.angle', '&lt;b&gt; &quot;quoted&quot; &apos;single&apos;'),
          data('a.numeric', '&#169; &#x2014;'),
        ].join('\n'),
      ),
    );

    expect(labels).toEqual({
      'a.amp': 'Tom & Jerry',
      'a.angle': `<b> "quoted" 'single'`,
      'a.numeric': '© —',
    });
  });

  it('fails on a duplicate key', () => {
    writeFileSync(
      join(workDir, 'UiLabels.resx'),
      resx([data('common.appName', 'One'), data('common.appName', 'Two')].join('\n')),
    );

    expect(() => generate({ resxDir: workDir, outDir: join(workDir, 'generated') })).toThrow(
      /Duplicate resource key "common\.appName"/,
    );
  });

  it('fails when another culture has a key missing from en', () => {
    writeFileSync(join(workDir, 'UiLabels.resx'), resx(data('common.appName', 'PigeonWatch')));
    writeFileSync(
      join(workDir, 'UiLabels.pl.resx'),
      resx([data('common.appName', 'PigeonWatch'), data('common.extra', 'Extra')].join('\n')),
    );

    expect(() => generate({ resxDir: workDir, outDir: join(workDir, 'generated') })).toThrow(
      /common\.extra/,
    );
  });

  it('lazy-loads non-en cultures and lists them as supported', () => {
    const outDir = join(workDir, 'generated');
    writeFileSync(join(workDir, 'UiLabels.resx'), resx(data('common.appName', 'PigeonWatch')));
    writeFileSync(join(workDir, 'UiLabels.pl.resx'), resx(data('common.appName', 'Gołębie')));

    const result = generate({ resxDir: workDir, outDir });

    const snapshots = readFileSync(join(outDir, 'snapshots.ts'), 'utf8');
    expect(result.cultures).toEqual(['en', 'pl']);
    expect(readJson(join(outDir, 'ui-labels.pl.json'))).toEqual({ 'common.appName': 'Gołębie' });
    expect(snapshots).toContain("supportedCultures: readonly string[] = ['en', 'pl']");
    expect(snapshots).toContain("import('./ui-labels.pl.json')");
  });
});
