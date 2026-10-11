import { GeoPoint } from '../../core/geolocation/geo-point';

export const mapTiles = {
  urlTemplate: 'https://tile.openstreetmap.org/{z}/{x}/{y}.png',
  copyrightUrl: 'https://www.openstreetmap.org/copyright',
  minZoom: 3,
  maxZoom: 19,
} as const;

export const neutralMapView = {
  centre: { latitude: 52.07, longitude: 19.48 } satisfies GeoPoint,
  zoom: 6,
} as const;

export const nearbyMapZoom = 12;

export const pickerMapZoom = 16;

export const minSearchZoom = 10;

export function attributionHtml(text: string): string {
  return `<a href="${mapTiles.copyrightUrl}" target="_blank" rel="noopener noreferrer">${escapeHtml(text)}</a>`;
}

function escapeHtml(text: string): string {
  return text
    .replaceAll('&', '&amp;')
    .replaceAll('<', '&lt;')
    .replaceAll('>', '&gt;')
    .replaceAll('"', '&quot;')
    .replaceAll("'", '&#39;');
}
