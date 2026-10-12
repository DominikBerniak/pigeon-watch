import { HttpErrorResponse, provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { DebugElement } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { By } from '@angular/platform-browser';
import { provideRouter } from '@angular/router';
import { NEVER, Observable, Subject, isObservable, of, throwError } from 'rxjs';
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

const hint =
  'We couldn\'t get your location. Move the map to your area and tap "Search this area".';

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
    outcome: GeolocationOutcome | Observable<GeolocationOutcome>,
    result: Observable<SightingList> = of({ items: [named, unnamed] }),
  ): Promise<void> {
    const position = isObservable(outcome) ? outcome : of(outcome);

    currentPosition = vi.fn<(highAccuracy: boolean) => Observable<GeolocationOutcome>>(
      () => position,
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

  async function zoomTo(zoom: number): Promise<void> {
    map().zoomChange.emit(zoom);
    await settle();
  }

  function root(): HTMLElement {
    return fixture.nativeElement as HTMLElement;
  }

  function map(): MapViewStub {
    return fixture.debugElement.query(By.directive(MapViewStub)).componentInstance as MapViewStub;
  }

  function findButton(label: string): HTMLButtonElement | undefined {
    return [...root().querySelectorAll<HTMLButtonElement>('button')].find(
      (candidate) => candidate.textContent?.trim() === label,
    );
  }

  function button(label: string): HTMLButtonElement {
    const match = findButton(label);

    if (!match) throw new Error(`No button "${label}"`);

    return match;
  }

  function buttonDebug(label: string): DebugElement {
    const match = fixture.debugElement
      .queryAll(By.css('button'))
      .find((candidate) => candidate.nativeElement.textContent?.trim() === label);

    if (!match) throw new Error(`No button "${label}"`);

    return match;
  }

  function text(selector: string): string | undefined {
    return root().querySelector(selector)?.textContent?.trim();
  }

  function statusElement(): HTMLElement {
    const element = root().querySelector<HTMLElement>('[role="status"]');

    if (!element) throw new Error('No status element');

    return element;
  }

  function status(): string {
    return statusElement().textContent?.trim() ?? '';
  }

  function heading(): string | undefined {
    return text('h1');
  }

  it('asks for a low-accuracy position and queries once at that centre when it is known', async () => {
    await render({ status: 'ok', ...warsaw });

    expect(currentPosition).toHaveBeenCalledExactlyOnceWith(false);
    expect(nearby).toHaveBeenCalledExactlyOnceWith(warsaw);
    expect(map().center()).toEqual(warsaw);
    expect(map().zoom()).toBe(nearbyMapZoom);
    expect(root().querySelector('app-alert')).toBeNull();
  });

  it('shows a marker per sighting named with its criticality and no popup content', async () => {
    await render({ status: 'ok', ...warsaw });

    expect(map().markers()).toEqual([
      {
        id: 'sighting-1',
        latitude: 52.23,
        longitude: 21.01,
        kind: 'urgent',
        label: 'Limpy, Urgent',
      },
      {
        id: 'sighting-2',
        latitude: 52.23,
        longitude: 21.01,
        kind: 'monitor',
        label: 'Unnamed bird, Monitor',
      },
    ]);
  });

  it('passes the selected sighting to the map and clears it on Close', async () => {
    await render({ status: 'ok', ...warsaw });

    expect(map().selectedId()).toBeNull();

    map().markerSelect.emit('sighting-2');
    await settle();

    expect(map().selectedId()).toBe('sighting-2');

    button('Close').click();
    await settle();

    expect(map().selectedId()).toBeNull();
  });

  it('heads the page with the user position and says it is locating while it waits', async () => {
    await render(NEVER);

    expect(heading()).toBe('Sightings near you');
    expect(status()).toBe('Finding your location…');
    expect(findButton('Use my location')).toBeUndefined();
  });

  it('keeps the user-position heading once the position is known', async () => {
    await render({ status: 'ok', ...warsaw });

    expect(heading()).toBe('Sightings near you');
  });

  it.each(['denied', 'unavailable', 'timeout', 'unsupported'] as const)(
    'opens the neutral view with no query and asks to zoom in when the position is %s',
    async (outcome) => {
      await render({ status: outcome });

      expect(nearby).not.toHaveBeenCalled();
      expect(map().center()).toEqual(neutralMapView.centre);
      expect(map().zoom()).toBe(neutralMapView.zoom);
      expect(heading()).toBe('Sightings in this area');
      expect(status()).toBe('Zoom in to search an area');

      await zoomTo(10);

      expect(status()).toBe(hint);
    },
  );

  it('keeps the status element from the first render and changes only its text', async () => {
    const pending = new Subject<GeolocationOutcome>();
    await render(pending);
    const element = statusElement();

    expect(element.textContent?.trim()).toBe('Finding your location…');

    pending.next({ status: 'ok', ...warsaw });
    pending.complete();
    await settle();

    expect(statusElement()).toBe(element);
    expect(element.textContent?.trim()).toBe('2 open sightings within 10 km');
    expect(root().querySelectorAll('[role="status"]')).toHaveLength(1);
  });

  it('disables "Search this area" below zoom 10 and explains why in the status', async () => {
    await render({ status: 'denied' });
    map().centerChange.emit(krakow);
    await zoomTo(9);

    const search = button('Search this area');

    expect(search.getAttribute('aria-disabled')).toBe('true');
    expect(search.disabled).toBe(false);
    expect(search.getAttribute('aria-describedby')).toBe(statusElement().id);
    expect(status()).toBe('Zoom in to search an area');

    search.click();
    await settle();

    expect(nearby).not.toHaveBeenCalled();

    await zoomTo(10);

    expect(search.getAttribute('aria-disabled')).toBeNull();
    expect(search.hasAttribute('aria-describedby')).toBe(false);

    search.click();
    await settle();

    expect(nearby).toHaveBeenCalledExactlyOnceWith(krakow);
  });

  it('queries the current map centre when "Search this area" is pressed', async () => {
    await render({ status: 'denied' });
    map().centerChange.emit(krakow);
    await zoomTo(10);

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

  it('shows an inline loading status while the query is in flight', async () => {
    const pending = new Subject<SightingList>();
    await render({ status: 'ok', ...warsaw }, pending);

    expect(statusElement().querySelector('app-loading-indicator')).not.toBeNull();
    expect(statusElement().querySelector('app-loading-indicator')?.hasAttribute('role')).toBe(
      false,
    );
    expect(status()).toBe('Loading…');
    expect(button('Search this area').getAttribute('aria-disabled')).toBe('true');

    pending.next({ items: [] });
    pending.complete();
    await settle();

    expect(root().querySelector('mat-progress-spinner')).toBeNull();
    expect(button('Search this area').getAttribute('aria-disabled')).toBeNull();
  });

  it('keeps the previous markers while a new search loads and clears the selection', async () => {
    await render({ status: 'ok', ...warsaw });
    map().markerSelect.emit('sighting-1');
    await settle();

    const pending = new Subject<SightingList>();
    nearby.mockReturnValue(pending);
    button('Search this area').click();
    await settle();

    expect(status()).toBe('Loading…');
    expect(
      map()
        .markers()
        .map((marker) => marker.id),
    ).toEqual(['sighting-1', 'sighting-2']);
    expect(map().selectedId()).toBeNull();
    expect(root().querySelector('mat-card')).toBeNull();

    pending.next({ items: [unnamed] });
    pending.complete();
    await settle();

    expect(
      map()
        .markers()
        .map((marker) => marker.id),
    ).toEqual(['sighting-2']);
    expect(status()).toBe('1 open sightings within 10 km');
  });

  it('reports the number of results in the status', async () => {
    await render({ status: 'ok', ...warsaw });

    expect(status()).toBe('2 open sightings within 10 km');
  });

  it('shows the empty state with the radius when the area has no sightings', async () => {
    await render({ status: 'ok', ...warsaw }, of({ items: [] }));

    expect(status()).toBe('No open sightings within 10 km of this area.');
    expect(map().markers()).toEqual([]);
  });

  it('shows the error under the toolbar and a retry beside Search that repeats the last query', async () => {
    await render(
      { status: 'ok', ...warsaw },
      throwError(() => new HttpErrorResponse({ status: 500 })),
    );

    const toolbar = root().querySelector('.nearby-toolbar')!;
    const alert = root().querySelector('app-alert[role="alert"]');

    expect(alert?.textContent?.trim()).toBe("We couldn't load sightings for this area.");
    expect(toolbar.nextElementSibling).toBe(alert);
    expect(toolbar.querySelector('.nearby-toolbar__actions')?.contains(button('Try again'))).toBe(
      true,
    );
    expect(status()).toBe('');
    expect(statusElement().classList).toContain('pw-visually-hidden');

    nearby.mockReturnValue(of({ items: [named] }));
    button('Try again').click();
    await settle();

    expect(nearby).toHaveBeenCalledTimes(2);
    expect(nearby).toHaveBeenLastCalledWith(warsaw);
    expect(root().querySelector('app-alert[role="alert"]')).toBeNull();
    expect(findButton('Try again')).toBeUndefined();
    expect(statusElement().classList).not.toContain('pw-visually-hidden');
    expect(map().markers().length).toBe(1);
  });

  it('shows the legend and a prompt in the aside when nothing is selected', async () => {
    await render({ status: 'ok', ...warsaw });

    const aside = root().querySelector('aside')!;

    expect(aside.querySelector('h2')?.textContent?.trim()).toBe('Criticality');
    expect(
      [...aside.querySelectorAll('ul > li > app-criticality-badge')].map((badge) =>
        badge.textContent?.trim(),
      ),
    ).toEqual(['Monitor', 'Needs help soon', 'Urgent']);
    expect(aside.textContent).toContain('Select a marker to see details');
    expect(aside.querySelector('mat-card')).toBeNull();
  });

  it('shows a card in the aside for the selected marker with a link to its details', async () => {
    await render({ status: 'ok', ...warsaw });
    map().markerSelect.emit('sighting-1');
    await settle();

    const card = root().querySelector<HTMLElement>('aside mat-card');
    const mapElement = root().querySelector('app-map-view');

    expect(card).not.toBeNull();
    expect(
      mapElement!.compareDocumentPosition(card!) & Node.DOCUMENT_POSITION_FOLLOWING,
    ).toBeTruthy();
    expect(card!.hasAttribute('aria-live')).toBe(false);
    expect(card!.querySelector('h2')?.textContent?.trim()).toBe('Limpy');
    expect(card!.textContent).toContain('Caught');
    expect(card!.textContent).toContain('Urgent');
    expect(card!.querySelector('a')?.getAttribute('href')).toBe('/sightings/sighting-1');
    expect(root().querySelector('aside')?.textContent).not.toContain(
      'Select a marker to see details',
    );
  });

  it('moves focus to the card heading after a marker is selected', async () => {
    await render({ status: 'ok', ...warsaw });
    map().markerSelect.emit('sighting-1');
    await settle();

    const cardHeading = root().querySelector<HTMLElement>('mat-card h2');

    expect(cardHeading?.getAttribute('tabindex')).toBe('-1');
    expect(document.activeElement).toBe(cardHeading);
  });

  it('returns focus to the marker of the closed card', async () => {
    await render({ status: 'ok', ...warsaw });
    map().markerSelect.emit('sighting-2');
    await settle();

    button('Close').click();
    await settle();

    expect(map().focusedMarkers).toEqual(['sighting-2']);
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

  it.each(['denied', 'unavailable', 'timeout'] as const)(
    'offers "Use my location" after "Search this area" when the position is %s',
    async (outcome) => {
      await render({ status: outcome });

      const search = button('Search this area');
      const locate = button('Use my location');

      expect(
        search.compareDocumentPosition(locate) & Node.DOCUMENT_POSITION_FOLLOWING,
      ).toBeTruthy();
      expect(locate.disabled).toBe(false);
    },
  );

  it.each([{ status: 'ok', ...warsaw }, { status: 'unsupported' }] as const)(
    'does not offer "Use my location" when the position is $status',
    async (outcome) => {
      await render(outcome);

      expect(findButton('Use my location')).toBeUndefined();
    },
  );

  it('recentres, switches the heading and searches when "Use my location" succeeds', async () => {
    await render({ status: 'timeout' });
    const pending = new Subject<GeolocationOutcome>();
    currentPosition.mockReturnValue(pending);

    button('Use my location').click();
    await settle();

    expect(currentPosition).toHaveBeenLastCalledWith(false);
    expect(status()).toBe('Finding your location…');
    expect(button('Use my location').disabled).toBe(true);

    pending.next({ status: 'ok', ...krakow });
    pending.complete();
    await settle();

    expect(map().center()).toEqual(krakow);
    expect(map().zoom()).toBe(nearbyMapZoom);
    expect(heading()).toBe('Sightings near you');
    expect(findButton('Use my location')).toBeUndefined();
    expect(nearby).toHaveBeenCalledExactlyOnceWith(krakow);
    expect(status()).toBe('2 open sightings within 10 km');
  });

  it('keeps the button and the hint when "Use my location" fails again', async () => {
    await render({ status: 'denied' });
    await zoomTo(10);
    currentPosition.mockReturnValue(of({ status: 'timeout' }));

    button('Use my location').click();
    await settle();

    expect(currentPosition).toHaveBeenCalledTimes(2);
    expect(button('Use my location').disabled).toBe(false);
    expect(status()).toBe(hint);
    expect(heading()).toBe('Sightings in this area');
    expect(map().center()).toEqual(neutralMapView.centre);
    expect(nearby).not.toHaveBeenCalled();
  });

  it('lets only the latest "Use my location" request move the map', async () => {
    await render({ status: 'denied' });
    const first = new Subject<GeolocationOutcome>();
    const second = new Subject<GeolocationOutcome>();
    currentPosition.mockReturnValueOnce(first).mockReturnValueOnce(second);

    buttonDebug('Use my location').triggerEventHandler('click');
    await settle();
    buttonDebug('Use my location').triggerEventHandler('click');
    await settle();

    first.next({ status: 'ok', ...warsaw });
    first.complete();
    await settle();

    expect(map().center()).toEqual(neutralMapView.centre);
    expect(nearby).not.toHaveBeenCalled();
    expect(status()).toBe('Finding your location…');

    second.next({ status: 'ok', ...krakow });
    second.complete();
    await settle();

    expect(map().center()).toEqual(krakow);
    expect(nearby).toHaveBeenCalledExactlyOnceWith(krakow);
  });
});
