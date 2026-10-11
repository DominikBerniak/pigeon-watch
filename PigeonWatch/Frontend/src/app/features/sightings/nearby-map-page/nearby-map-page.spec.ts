import { HttpErrorResponse, provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { By } from '@angular/platform-browser';
import { provideRouter } from '@angular/router';
import { Observable, Subject, of, throwError } from 'rxjs';
import { GeoPoint } from '../../../core/geolocation/geo-point';
import {
  GeolocationOutcome,
  GeolocationService,
} from '../../../core/geolocation/geolocation.service';
import { nearbyMapZoom, neutralMapView } from '../../../shared/map/map-config';
import { MapView } from '../../../shared/map/map-view/map-view';
import { MapViewStub } from '../../../shared/map/testing/map-view-stub';
import { Sighting, SightingCriticality, SightingList, SightingStatus } from '../sighting.types';
import { SightingsApi } from '../sightings-api';
import { NearbyMapPage } from './nearby-map-page';

const warsaw: GeoPoint = { latitude: 52.2297, longitude: 21.0122 };
const krakow: GeoPoint = { latitude: 50.0614, longitude: 19.9366 };

const named: Sighting = {
  id: 'sighting-1',
  name: 'Limpy',
  description: 'Grey pigeon',
  behaviour: 'Cannot fly',
  latitude: 52.23,
  longitude: 21.01,
  status: SightingStatus.caught,
  criticality: SightingCriticality.urgent,
  reporterDisplayName: 'Jan K',
  createdAt: '2026-10-11T12:00:00Z',
};

const unnamed: Sighting = {
  ...named,
  id: 'sighting-2',
  name: null,
  status: SightingStatus.spotted,
  criticality: SightingCriticality.monitor,
};

describe('NearbyMapPage', () => {
  let currentPosition: ReturnType<
    typeof vi.fn<(highAccuracy: boolean) => Observable<GeolocationOutcome>>
  >;
  let nearby: ReturnType<typeof vi.fn<(centre: GeoPoint) => Observable<SightingList>>>;
  let fixture: ComponentFixture<NearbyMapPage>;

  async function render(
    outcome: GeolocationOutcome,
    result: Observable<SightingList> = of({ items: [named, unnamed] }),
  ): Promise<void> {
    currentPosition = vi.fn<(highAccuracy: boolean) => Observable<GeolocationOutcome>>(() =>
      of(outcome),
    );
    nearby = vi.fn<(centre: GeoPoint) => Observable<SightingList>>(() => result);
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        { provide: GeolocationService, useValue: { currentPosition } },
        { provide: SightingsApi, useValue: { nearby } },
      ],
    });
    TestBed.overrideComponent(NearbyMapPage, {
      remove: { imports: [MapView] },
      add: { imports: [MapViewStub] },
    });
    fixture = TestBed.createComponent(NearbyMapPage);
    await fixture.whenStable();
  }

  async function settle(): Promise<void> {
    await fixture.whenStable();
    fixture.detectChanges();
    await fixture.whenStable();
  }

  function root(): HTMLElement {
    return fixture.nativeElement as HTMLElement;
  }

  function map(): MapViewStub {
    return fixture.debugElement.query(By.directive(MapViewStub)).componentInstance as MapViewStub;
  }

  function button(text: string): HTMLButtonElement {
    const match = [...root().querySelectorAll<HTMLButtonElement>('button')].find(
      (candidate) => candidate.textContent?.trim() === text,
    );

    if (!match) throw new Error(`No button "${text}"`);

    return match;
  }

  function text(selector: string): string | undefined {
    return root().querySelector(selector)?.textContent?.trim();
  }

  it('asks for a low-accuracy position and queries once at that centre when it is known', async () => {
    await render({ status: 'ok', ...warsaw });

    expect(currentPosition).toHaveBeenCalledExactlyOnceWith(false);
    expect(nearby).toHaveBeenCalledExactlyOnceWith(warsaw);
    expect(map().center()).toEqual(warsaw);
    expect(map().zoom()).toBe(nearbyMapZoom);
    expect(root().querySelector('app-alert')).toBeNull();
  });

  it('shows a marker per sighting with its criticality and no popup content', async () => {
    await render({ status: 'ok', ...warsaw });

    expect(map().markers()).toEqual([
      { id: 'sighting-1', latitude: 52.23, longitude: 21.01, kind: 'urgent', label: 'Limpy' },
      {
        id: 'sighting-2',
        latitude: 52.23,
        longitude: 21.01,
        kind: 'monitor',
        label: 'Unnamed bird',
      },
    ]);
  });

  it.each(['denied', 'unavailable', 'timeout', 'unsupported'] as const)(
    'opens the neutral view with a hint and no query when the position is %s',
    async (status) => {
      await render({ status });

      expect(nearby).not.toHaveBeenCalled();
      expect(map().center()).toEqual(neutralMapView.centre);
      expect(map().zoom()).toBe(neutralMapView.zoom);
      expect(text('app-alert')).toBe(
        'We couldn\'t get your location. Move the map to your area and tap "Search this area".',
      );
    },
  );

  it('queries the current map centre when "Search this area" is pressed', async () => {
    await render({ status: 'denied' });
    map().centerChange.emit(krakow);
    await settle();

    expect(nearby).not.toHaveBeenCalled();

    button('Search this area').click();
    await settle();

    expect(nearby).toHaveBeenCalledExactlyOnceWith(krakow);
    expect(map().markers().length).toBe(2);
  });

  it('re-queries the moved centre after a position-based query', async () => {
    await render({ status: 'ok', ...warsaw });
    map().centerChange.emit(krakow);
    await settle();

    button('Search this area').click();
    await settle();

    expect(nearby).toHaveBeenNthCalledWith(1, warsaw);
    expect(nearby).toHaveBeenNthCalledWith(2, krakow);
  });

  it('shows a spinner while the query is in flight', async () => {
    const pending = new Subject<SightingList>();
    await render({ status: 'ok', ...warsaw }, pending);

    expect(root().querySelector('mat-progress-spinner')).not.toBeNull();

    pending.next({ items: [] });
    pending.complete();
    await settle();

    expect(root().querySelector('mat-progress-spinner')).toBeNull();
  });

  it('shows the empty state with the radius when the area has no sightings', async () => {
    await render({ status: 'ok', ...warsaw }, of({ items: [] }));

    expect(text('.nearby-empty')).toBe('No open sightings within 10 km of this area.');
    expect(map().markers()).toEqual([]);
  });

  it('shows an error with a retry that repeats the last query', async () => {
    await render(
      { status: 'ok', ...warsaw },
      throwError(() => new HttpErrorResponse({ status: 500 })),
    );

    expect(text('app-alert[role="alert"]')).toBe("We couldn't load sightings for this area.");

    nearby.mockReturnValue(of({ items: [named] }));
    button('Try again').click();
    await settle();

    expect(nearby).toHaveBeenCalledTimes(2);
    expect(nearby).toHaveBeenLastCalledWith(warsaw);
    expect(root().querySelector('app-alert[role="alert"]')).toBeNull();
    expect(map().markers().length).toBe(1);
  });

  it('shows a card below the map for the selected marker with a link to its details', async () => {
    await render({ status: 'ok', ...warsaw });

    expect(root().querySelector('mat-card')).toBeNull();

    map().markerSelect.emit('sighting-1');
    await settle();

    const card = root().querySelector<HTMLElement>('mat-card');
    const mapElement = root().querySelector('app-map-view');

    expect(card).not.toBeNull();
    expect(
      mapElement!.compareDocumentPosition(card!) & Node.DOCUMENT_POSITION_FOLLOWING,
    ).toBeTruthy();
    expect(card!.querySelector('h2')?.textContent?.trim()).toBe('Limpy');
    expect(card!.textContent).toContain('Caught');
    expect(card!.textContent).toContain('Urgent');
    expect(card!.querySelector('a')?.getAttribute('href')).toBe('/sightings/sighting-1');
  });

  it('shows the unnamed label for a sighting without a name and closes the card', async () => {
    await render({ status: 'ok', ...warsaw });
    map().markerSelect.emit('sighting-2');
    await settle();

    expect(text('mat-card h2')).toBe('Unnamed bird');
    expect(text('mat-card')).toContain('Spotted');
    expect(text('mat-card')).toContain('Monitor');

    button('Close').click();
    await settle();

    expect(root().querySelector('mat-card')).toBeNull();
  });

  it('puts a prominent "Report sighting" link above the map', async () => {
    await render({ status: 'denied' });

    const link = [...root().querySelectorAll('a')].find(
      (anchor) => anchor.textContent?.trim() === 'Report sighting',
    );

    expect(link?.getAttribute('href')).toBe('/sightings/new');
    expect(
      link!.compareDocumentPosition(root().querySelector('app-map-view')!) &
        Node.DOCUMENT_POSITION_FOLLOWING,
    ).toBeTruthy();
  });
});
