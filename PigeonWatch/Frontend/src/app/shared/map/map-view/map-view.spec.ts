import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { from, throwError } from 'rxjs';
import { GeoPoint } from '../../../core/geolocation/geo-point';
import { MapMarker } from '../map-marker';
import { LEAFLET_LOADER, MapView, leafletStylesheetHref } from './map-view';

const centre: GeoPoint = { latitude: 52.07, longitude: 19.48 };
const marker: MapMarker = { id: 'sighting-1', latitude: 52.1, longitude: 19.5, kind: 'urgent' };
const other: MapMarker = { id: 'sighting-2', latitude: 52.0, longitude: 19.4, kind: 'monitor' };

describe('MapView', () => {
  let fixture: ComponentFixture<MapView>;

  async function render(
    pin: GeoPoint | null = null,
    markers: readonly MapMarker[] = [marker],
    interactiveMarkers = true,
  ): Promise<HTMLElement> {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    fixture = TestBed.createComponent(MapView);
    fixture.componentRef.setInput('label', 'Map of sightings');
    fixture.componentRef.setInput('center', centre);
    fixture.componentRef.setInput('zoom', 6);
    fixture.componentRef.setInput('markers', markers);
    fixture.componentRef.setInput('pin', pin);
    fixture.componentRef.setInput('interactiveMarkers', interactiveMarkers);
    await fixture.whenStable();

    const root = fixture.nativeElement as HTMLElement;
    await vi.waitFor(() => expect(root.querySelector('.leaflet-container')).not.toBeNull());
    await fixture.whenStable();

    return root;
  }

  function leafletStylesheets(): HTMLLinkElement[] {
    return [
      ...document.head.querySelectorAll<HTMLLinkElement>(
        `link[rel="stylesheet"][href="${leafletStylesheetHref}"]`,
      ),
    ];
  }

  function icon(root: HTMLElement, kind: 'urgent' | 'monitor'): HTMLElement {
    const element = root.querySelector<HTMLElement>(`.pw-map-marker--${kind}`);

    if (!element) throw new Error(`No ${kind} marker`);

    return element;
  }

  function press(element: HTMLElement, key: string, repeat = false): KeyboardEvent {
    const event = new KeyboardEvent('keydown', { key, repeat, bubbles: true, cancelable: true });

    element.dispatchEvent(event);

    return event;
  }

  async function select(id: string | null): Promise<void> {
    fixture.componentRef.setInput('selectedId', id);
    await fixture.whenStable();
  }

  afterEach(() => {
    leafletStylesheets().forEach((link) => link.remove());
  });

  it('adds the Leaflet stylesheet once, however many maps render', async () => {
    await render();
    const second = TestBed.createComponent(MapView);
    second.componentRef.setInput('label', 'Second map');
    second.componentRef.setInput('center', centre);
    second.componentRef.setInput('zoom', 6);
    await second.whenStable();
    await vi.waitFor(() =>
      expect(
        (second.nativeElement as HTMLElement).querySelector('.leaflet-container'),
      ).not.toBeNull(),
    );

    expect(leafletStylesheets()).toHaveLength(1);
  });

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

  it.each(['Enter', ' '])(
    'emits the id of a focused marker on %j without the default key action',
    async (key) => {
      const root = await render();
      const selected: string[] = [];
      fixture.componentInstance.markerSelect.subscribe((id) => selected.push(id));

      const event = press(icon(root, 'urgent'), key);

      expect(selected).toEqual([marker.id]);
      expect(event.defaultPrevented).toBe(true);
    },
  );

  it.each(['Enter', ' '])(
    'emits once for a held %j and still prevents the default key action',
    async (key) => {
      const root = await render();
      const selected: string[] = [];
      fixture.componentInstance.markerSelect.subscribe((id) => selected.push(id));

      press(icon(root, 'urgent'), key);
      const repeated = press(icon(root, 'urgent'), key, true);

      expect(selected).toEqual([marker.id]);
      expect(repeated.defaultPrevented).toBe(true);
    },
  );

  it.each(['a', 'Tab', 'Escape', 'Shift'])('ignores %j on a focused marker', async (key) => {
    const root = await render();
    const selected: string[] = [];
    fixture.componentInstance.markerSelect.subscribe((id) => selected.push(id));

    const event = press(icon(root, 'urgent'), key);

    expect(selected).toEqual([]);
    expect(event.defaultPrevented).toBe(false);
  });

  it('makes interactive markers a labelled tab stop with a pressed state by default', async () => {
    const root = await render();

    expect(icon(root, 'urgent').getAttribute('tabindex')).toBe('0');
    expect(icon(root, 'urgent').getAttribute('role')).toBe('button');
    expect(icon(root, 'urgent').getAttribute('aria-pressed')).toBe('false');
  });

  it('draws display-only markers with no tab stop, no button role and no pressed state', async () => {
    const root = await render(null, [marker, other], false);

    for (const kind of ['urgent', 'monitor'] as const) {
      expect(icon(root, kind).hasAttribute('tabindex')).toBe(false);
      expect(icon(root, kind).hasAttribute('role')).toBe(false);
      expect(icon(root, kind).hasAttribute('aria-pressed')).toBe(false);
    }

    await select(marker.id);

    expect(icon(root, 'urgent').classList).toContain('pw-map-marker--selected');
    expect(icon(root, 'urgent').hasAttribute('aria-pressed')).toBe(false);
  });

  it('emits nothing from a display-only marker on click or on Enter and Space', async () => {
    const root = await render(null, [marker], false);
    const selected: string[] = [];
    fixture.componentInstance.markerSelect.subscribe((id) => selected.push(id));

    icon(root, 'urgent').click();
    const enter = press(icon(root, 'urgent'), 'Enter');
    const space = press(icon(root, 'urgent'), ' ');

    expect(selected).toEqual([]);
    expect(enter.defaultPrevented).toBe(false);
    expect(space.defaultPrevented).toBe(false);
  });

  it('redraws the markers when they become display-only', async () => {
    const root = await render();

    fixture.componentRef.setInput('interactiveMarkers', false);
    await fixture.whenStable();

    expect(icon(root, 'urgent').hasAttribute('tabindex')).toBe(false);
    expect(icon(root, 'urgent').hasAttribute('aria-pressed')).toBe(false);
  });

  it('marks no marker as selected by default', async () => {
    const root = await render(null, [marker, other]);

    for (const kind of ['urgent', 'monitor'] as const) {
      expect(icon(root, kind).classList).not.toContain('pw-map-marker--selected');
      expect(icon(root, kind).getAttribute('aria-pressed')).toBe('false');
    }
  });

  it('marks and raises the selected marker and moves the mark when the selection changes', async () => {
    const root = await render(null, [marker, other]);

    await select(marker.id);

    expect(icon(root, 'urgent').classList).toContain('pw-map-marker--selected');
    expect(icon(root, 'urgent').getAttribute('aria-pressed')).toBe('true');
    expect(icon(root, 'monitor').classList).not.toContain('pw-map-marker--selected');
    expect(icon(root, 'monitor').getAttribute('aria-pressed')).toBe('false');
    expect(Number(icon(root, 'urgent').style.zIndex)).toBeGreaterThan(
      Number(icon(root, 'monitor').style.zIndex),
    );

    await select(other.id);

    expect(icon(root, 'monitor').classList).toContain('pw-map-marker--selected');
    expect(icon(root, 'monitor').getAttribute('aria-pressed')).toBe('true');
    expect(icon(root, 'urgent').classList).not.toContain('pw-map-marker--selected');
    expect(icon(root, 'urgent').getAttribute('aria-pressed')).toBe('false');
    expect(Number(icon(root, 'monitor').style.zIndex)).toBeGreaterThan(
      Number(icon(root, 'urgent').style.zIndex),
    );

    await select(null);

    expect(root.querySelector('.pw-map-marker--selected')).toBeNull();
    expect(root.querySelectorAll('[aria-pressed="true"]')).toHaveLength(0);
  });

  it('keeps the selected mark when the markers are redrawn', async () => {
    const root = await render(null, [marker]);
    await select(other.id);

    fixture.componentRef.setInput('markers', [marker, other]);
    await fixture.whenStable();

    expect(icon(root, 'monitor').classList).toContain('pw-map-marker--selected');
    expect(icon(root, 'monitor').getAttribute('aria-pressed')).toBe('true');
    expect(icon(root, 'urgent').getAttribute('aria-pressed')).toBe('false');
  });

  it('emits the zoom together with the centre when the map stops moving', async () => {
    await render();
    const zooms: number[] = [];
    const centres: GeoPoint[] = [];
    fixture.componentInstance.zoomChange.subscribe((zoom) => zooms.push(zoom));
    fixture.componentInstance.centerChange.subscribe((point) => centres.push(point));

    fixture.componentRef.setInput('zoom', 10);
    await fixture.whenStable();

    expect(zooms.at(-1)).toBe(10);
    expect(centres).toHaveLength(zooms.length);
  });

  it('focuses a shown marker on request and ignores an unknown id', async () => {
    const root = await render(null, [marker, other]);

    fixture.componentInstance.focusMarker(other.id);

    expect(document.activeElement).toBe(icon(root, 'monitor'));

    fixture.componentInstance.focusMarker('missing');

    expect(document.activeElement).toBe(icon(root, 'monitor'));
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

  it('emits mapReady once the map is created', async () => {
    let readyCount = 0;
    const failures: boolean[] = [];
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    fixture = TestBed.createComponent(MapView);
    fixture.componentInstance.mapReady.subscribe(() => readyCount++);
    fixture.componentInstance.loadFailed.subscribe((failed) => failures.push(failed));
    fixture.componentRef.setInput('label', 'Map of sightings');
    fixture.componentRef.setInput('center', centre);
    fixture.componentRef.setInput('zoom', 6);
    await fixture.whenStable();

    await vi.waitFor(() => expect(readyCount).toBe(1));
    expect(failures).toEqual([]);
  });

  it('shows an error with a retry when Leaflet fails to load, then loads on retry', async () => {
    const loader = vi
      .fn()
      .mockReturnValueOnce(throwError(() => new Error('chunk failed')))
      .mockImplementation(() => from(import('leaflet')));
    let readyCount = 0;
    const failures: boolean[] = [];
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: LEAFLET_LOADER, useValue: loader },
      ],
    });
    fixture = TestBed.createComponent(MapView);
    fixture.componentInstance.mapReady.subscribe(() => readyCount++);
    fixture.componentInstance.loadFailed.subscribe((failed) => failures.push(failed));
    fixture.componentRef.setInput('label', 'Map of sightings');
    fixture.componentRef.setInput('center', centre);
    fixture.componentRef.setInput('zoom', 6);
    await fixture.whenStable();

    const root = fixture.nativeElement as HTMLElement;

    expect(root.querySelector('app-alert')?.textContent?.trim()).toBe(
      "The map couldn't be loaded. Check your connection and try again.",
    );
    expect(readyCount).toBe(0);
    expect(failures).toEqual([true]);

    root.querySelector<HTMLButtonElement>('button')?.click();
    await vi.waitFor(() => expect(root.querySelector('.leaflet-container')).not.toBeNull());
    await fixture.whenStable();

    expect(root.querySelector('app-alert')).toBeNull();
    expect(readyCount).toBe(1);
    expect(failures).toEqual([true, false]);
    expect(loader).toHaveBeenCalledTimes(2);
  });

  it('emits true again when the retry fails too', async () => {
    const loader = vi.fn().mockImplementation(() => throwError(() => new Error('chunk failed')));
    const failures: boolean[] = [];
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: LEAFLET_LOADER, useValue: loader },
      ],
    });
    fixture = TestBed.createComponent(MapView);
    fixture.componentInstance.loadFailed.subscribe((failed) => failures.push(failed));
    fixture.componentRef.setInput('label', 'Map of sightings');
    fixture.componentRef.setInput('center', centre);
    fixture.componentRef.setInput('zoom', 6);
    await fixture.whenStable();

    const root = fixture.nativeElement as HTMLElement;

    root.querySelector<HTMLButtonElement>('button')?.click();
    await vi.waitFor(() => expect(failures).toEqual([true, false, true]));
    await fixture.whenStable();

    expect(root.querySelector('app-alert')).not.toBeNull();
    expect(loader).toHaveBeenCalledTimes(2);
  });
});
