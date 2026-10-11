import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { GeoPoint } from '../../../core/geolocation/geo-point';
import { MapMarker } from '../map-marker';
import { MapView } from './map-view';

const centre: GeoPoint = { latitude: 52.07, longitude: 19.48 };
const marker: MapMarker = { id: 'sighting-1', latitude: 52.1, longitude: 19.5, kind: 'urgent' };

describe('MapView', () => {
  let fixture: ComponentFixture<MapView>;

  async function render(pin: GeoPoint | null = null): Promise<HTMLElement> {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    fixture = TestBed.createComponent(MapView);
    fixture.componentRef.setInput('label', 'Map of sightings');
    fixture.componentRef.setInput('center', centre);
    fixture.componentRef.setInput('zoom', 6);
    fixture.componentRef.setInput('markers', [marker]);
    fixture.componentRef.setInput('pin', pin);
    await fixture.whenStable();

    const root = fixture.nativeElement as HTMLElement;
    await vi.waitFor(() => expect(root.querySelector('.leaflet-container')).not.toBeNull());
    await fixture.whenStable();

    return root;
  }

  it('labels the map region', async () => {
    const root = await render();

    expect(root.querySelector('[role="region"]')?.getAttribute('aria-label')).toBe(
      'Map of sightings',
    );
  });

  it('loads tiles from the OSM host and links the escaped attribution', async () => {
    const root = await render();

    expect(root.querySelector('img.leaflet-tile')?.getAttribute('src')).toMatch(
      /^https:\/\/tile\.openstreetmap\.org\/6\/\d+\/\d+\.png$/,
    );
    expect(root.querySelector('.leaflet-control-attribution a')?.textContent).toBe(
      '© OpenStreetMap contributors',
    );
  });

  it('draws markers with static classes and no inline size', async () => {
    const root = await render();
    const icon = root.querySelector<HTMLElement>('.pw-map-marker--urgent');

    expect(icon?.classList).toContain('pw-map-marker');
    expect(icon?.innerHTML).toBe('');
    expect(icon?.style.width).toBe('');
    expect(icon?.style.marginLeft).toBe('');
  });

  it('emits the id of a selected marker', async () => {
    const root = await render();
    const selected: string[] = [];
    fixture.componentInstance.markerSelect.subscribe((id) => selected.push(id));

    root.querySelector<HTMLElement>('.pw-map-marker--urgent')?.click();

    expect(selected).toEqual([marker.id]);
  });

  it('shows, moves and removes the pin', async () => {
    const root = await render({ latitude: 52.2, longitude: 19.6 });

    expect(root.querySelectorAll('.pw-map-marker--pin')).toHaveLength(1);

    fixture.componentRef.setInput('pin', { latitude: 52.3, longitude: 19.7 });
    await fixture.whenStable();

    expect(root.querySelectorAll('.pw-map-marker--pin')).toHaveLength(1);

    fixture.componentRef.setInput('pin', null);
    await fixture.whenStable();

    expect(root.querySelector('.pw-map-marker--pin')).toBeNull();
  });

  it('removes the Leaflet map on teardown', async () => {
    const root = await render();
    const canvas = root.querySelector<HTMLElement>('.map-view__canvas');

    expect(canvas?.querySelector('.leaflet-map-pane')).not.toBeNull();

    fixture.destroy();

    expect(canvas?.querySelector('.leaflet-map-pane')).toBeNull();
  });
});
