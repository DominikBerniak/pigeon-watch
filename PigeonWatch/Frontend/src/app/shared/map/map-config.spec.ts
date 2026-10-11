import { readFileSync } from 'node:fs';
import { resolve } from 'node:path';
import process from 'node:process';
import { attributionHtml, mapTiles } from './map-config';
import { mapMarkerClassNames, mapPinClassName } from './map-marker';

const root = process.cwd();

describe('map config', () => {
  it('links the attribution to the OSM copyright page', () => {
    expect(attributionHtml('© OpenStreetMap contributors')).toBe(
      `<a href="${mapTiles.copyrightUrl}" target="_blank" rel="noopener noreferrer">© OpenStreetMap contributors</a>`,
    );
  });

  it('escapes the attribution text before Leaflet parses it as HTML', () => {
    expect(attributionHtml(`<img src=x onerror="alert('x')"> & co`)).toContain(
      '&lt;img src=x onerror=&quot;alert(&#39;x&#39;)&quot;&gt; &amp; co',
    );
  });

  it('loads tiles from the single host the CSP admits for images', () => {
    const config = JSON.parse(
      readFileSync(resolve(root, 'public', 'staticwebapp.config.json'), 'utf8'),
    ) as { globalHeaders: Record<string, string> };
    const imgSrc = config.globalHeaders['Content-Security-Policy']
      .split(';')
      .map((directive) => directive.trim())
      .find((directive) => directive.startsWith('img-src '));
    const tileOrigin = new URL(mapTiles.urlTemplate.replace(/\{[xyz]\}/g, '0')).origin;

    expect(tileOrigin).toBe('https://tile.openstreetmap.org');
    expect(imgSrc).toBe(`img-src 'self' data: ${tileOrigin}`);
  });

  it('builds marker icons from static pw-map-marker classes', () => {
    expect(Object.values(mapMarkerClassNames)).toEqual([
      'pw-map-marker pw-map-marker--monitor',
      'pw-map-marker pw-map-marker--needs-help-soon',
      'pw-map-marker pw-map-marker--urgent',
    ]);
    expect(mapPinClassName).toBe('pw-map-marker pw-map-marker--pin');
  });

  it('declares every marker class in _common.scss', () => {
    const common = readFileSync(resolve(root, 'src', 'styles', '_common.scss'), 'utf8');
    const classes = [...Object.values(mapMarkerClassNames), mapPinClassName].flatMap((names) =>
      names.split(' '),
    );

    expect(classes.filter((name) => !common.includes(`.${name} {`))).toEqual([]);
  });
});
