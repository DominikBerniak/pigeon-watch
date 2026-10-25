import { HttpErrorResponse, provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { By } from '@angular/platform-browser';
import { provideRouter } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { NEVER, Observable, Subject, of, throwError } from 'rxjs';
import { MapView } from '../../../shared/map/map-view/map-view';
import { MapViewStub } from '../../../shared/map/testing/map-view-stub';
import { Sighting, SightingCriticality, SightingStatus } from '../sighting.types';
import { SightingsApi } from '../sightings-api';
import { SightingDetailPage } from './sighting-detail-page';

const sighting: Sighting = {
  id: 'sighting-1',
  name: 'Limpy',
  description: 'Grey pigeon with a ruffled wing',
  behaviour: 'Cannot fly',
  latitude: 52.2297,
  longitude: 21.0122,
  status: SightingStatus.caught,
  criticality: SightingCriticality.needsHelpSoon,
  reporterDisplayName: 'Jan K',
  createdAt: '2026-10-11T12:00:00Z',
};

describe('SightingDetailPage', () => {
  let get: ReturnType<typeof vi.fn<(id: string) => Observable<Sighting>>>;
  let harness: RouterTestingHarness;

  beforeEach(async () => {
    get = vi.fn<(id: string) => Observable<Sighting>>(() => of(sighting));
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([{ path: 'sightings/:id', component: SightingDetailPage }]),
        { provide: SightingsApi, useValue: { get } },
      ],
    });
    TestBed.overrideComponent(SightingDetailPage, {
      remove: { imports: [MapView] },
      add: { imports: [MapViewStub] },
    });
    harness = await RouterTestingHarness.create();
  });

  async function open(id = 'sighting-1'): Promise<HTMLElement> {
    await harness.navigateByUrl(`/sightings/${id}`);
    await settle();

    return harness.routeNativeElement as HTMLElement;
  }

  async function settle(): Promise<void> {
    await harness.fixture.whenStable();
    harness.fixture.detectChanges();
    await harness.fixture.whenStable();
  }

  function button(root: HTMLElement, text: string): HTMLButtonElement {
    const match = [...root.querySelectorAll<HTMLButtonElement>('button')].find(
      (candidate) => candidate.textContent?.trim() === text,
    );

    if (!match) throw new Error(`No button "${text}"`);

    return match;
  }

  function rows(root: HTMLElement): (string | undefined)[][] {
    return [...root.querySelectorAll('dl > div')].map((row) => [
      row.querySelector('dt')?.textContent?.trim(),
      row.querySelector('dd')?.textContent?.trim(),
    ]);
  }

  function mapStub(): MapViewStub | null {
    const element = harness.fixture.debugElement.query(By.directive(MapViewStub));

    return element ? (element.componentInstance as MapViewStub) : null;
  }

  it('loads the sighting by id and shows its data with the coordinates as text', async () => {
    const root = await open();

    expect(get).toHaveBeenCalledExactlyOnceWith('sighting-1');
    expect(root.querySelector('app-page-card h1')?.textContent?.trim()).toBe('Limpy');
    expect(rows(root).filter(([label]) => label !== 'Reported')).toEqual([
      ['Status', 'Caught'],
      ['Criticality', 'Needs help soon'],
      ['Description', 'Grey pigeon with a ruffled wing'],
      ['Behaviour', 'Cannot fly'],
      ['Reported by', 'Jan K'],
      ['Location', '52.22970, 21.01220'],
    ]);
    expect(root.querySelector('time')?.getAttribute('datetime')).toBe(sighting.createdAt);
    expect(root.querySelector('time')?.textContent?.trim()).not.toBe('');
  });

  it('widens the page card while the map is shown', async () => {
    const root = await open();
    const card = root.querySelector('app-page-card');

    expect(card?.classList).not.toContain('page-card-host--wide');

    button(root, 'Show map').click();
    await settle();

    expect(card?.classList).toContain('page-card-host--wide');

    button(root, 'Hide map').click();
    await settle();

    expect(card?.classList).not.toContain('page-card-host--wide');
  });

  it('shows the unnamed label as the title for a sighting without a name', async () => {
    get.mockReturnValue(of({ ...sighting, name: null }));

    const root = await open();

    expect(root.querySelector('app-page-card h1')?.textContent?.trim()).toBe('Unnamed bird');
  });

  it('puts the map toggle in the card header beside the title', async () => {
    const root = await open();

    expect(root.querySelector('app-page-card header button')).toBe(button(root, 'Show map'));
  });

  it('renders no map until "Show map" is pressed, then one map with a single marker', async () => {
    const root = await open();

    expect(root.querySelector('app-map-view')).toBeNull();
    expect(button(root, 'Show map').getAttribute('aria-expanded')).toBe('false');

    button(root, 'Show map').click();
    await settle();

    expect(root.querySelectorAll('app-map-view').length).toBe(1);
    expect(mapStub()?.center()).toEqual({ latitude: 52.2297, longitude: 21.0122 });
    expect(mapStub()?.markers()).toEqual([
      {
        id: 'sighting-1',
        latitude: 52.2297,
        longitude: 21.0122,
        kind: 'needsHelpSoon',
        label: 'Limpy, Needs help soon',
      },
    ]);
    expect(button(root, 'Hide map').getAttribute('aria-expanded')).toBe('true');

    button(root, 'Hide map').click();
    await settle();

    expect(root.querySelector('app-map-view')).toBeNull();
  });

  it('shows the not-found state for a 404', async () => {
    get.mockReturnValue(throwError(() => new HttpErrorResponse({ status: 404 })));

    const root = await open('missing');

    expect(root.querySelector('app-alert')?.textContent?.trim()).toBe(
      "This sighting doesn't exist or is no longer available.",
    );
    expect(root.querySelector('dl')).toBeNull();
    expect(root.querySelector('app-page-card h1')?.textContent?.trim()).toBe('Sighting');
    expect([...root.querySelectorAll('button')].map((b) => b.textContent?.trim())).not.toContain(
      'Try again',
    );
  });

  it('shows a generic error with a retry that loads again', async () => {
    get.mockReturnValue(throwError(() => new HttpErrorResponse({ status: 500 })));
    const root = await open();

    expect(root.querySelector('app-alert[role="alert"]')?.textContent?.trim()).toBe(
      "We couldn't reach the sightings right now. Try again.",
    );

    get.mockReturnValue(of(sighting));
    button(root, 'Try again').click();
    await settle();

    expect(get).toHaveBeenCalledTimes(2);
    expect(root.querySelector('app-alert')).toBeNull();
    expect(root.querySelector('app-page-card h1')?.textContent?.trim()).toBe('Limpy');
  });

  it('links back to the map from the card actions footer', async () => {
    const root = await open();
    const back = root.querySelector<HTMLAnchorElement>('mat-card-actions a');

    expect(back?.textContent?.trim()).toBe('Back to map');
    expect(back?.getAttribute('href')).toBe('/');
    expect(root.querySelectorAll('a').length).toBe(1);
  });

  it('shows the Sighting heading and a loading indicator while the sighting loads', async () => {
    get.mockReturnValue(NEVER);

    const root = await open();

    expect(root.querySelector('app-page-card h1')?.textContent?.trim()).toBe('Sighting');
    expect(root.querySelector('app-loading-indicator')).not.toBeNull();
    expect(root.querySelector('dl')).toBeNull();
  });

  it('sets the tab title from the sighting name once it has loaded', async () => {
    await open();

    expect(document.title).toBe('Limpy – PigeonWatch');
  });

  it('uses the Sighting tab title while loading', async () => {
    get.mockReturnValue(NEVER);

    await open();

    expect(document.title).toBe('Sighting – PigeonWatch');
  });

  it('uses the Sighting tab title in the not-found state', async () => {
    get.mockReturnValue(throwError(() => new HttpErrorResponse({ status: 404 })));

    await open('missing');

    expect(document.title).toBe('Sighting – PigeonWatch');
  });

  it('uses the Sighting tab title in the error state', async () => {
    get.mockReturnValue(throwError(() => new HttpErrorResponse({ status: 500 })));

    await open();

    expect(document.title).toBe('Sighting – PigeonWatch');
  });

  it('resets the tab title to the app name when the page is destroyed', async () => {
    await open();

    harness.fixture.destroy();

    expect(document.title).toBe('PigeonWatch');
  });

  it('focuses the heading once the sighting has loaded by navigation', async () => {
    const root = await open();

    expect(document.activeElement).toBe(root.querySelector('app-page-card h1'));
  });

  it('focuses the heading in the not-found state', async () => {
    get.mockReturnValue(throwError(() => new HttpErrorResponse({ status: 404 })));

    const root = await open('missing');

    expect(document.activeElement).toBe(root.querySelector('app-page-card h1'));
  });

  it('focuses the heading in the error state', async () => {
    get.mockReturnValue(throwError(() => new HttpErrorResponse({ status: 500 })));

    const root = await open();

    expect(document.activeElement).toBe(root.querySelector('app-page-card h1'));
  });

  it('does not focus the heading while the sighting is still loading', async () => {
    get.mockReturnValue(NEVER);

    const root = await open();

    expect(document.activeElement).not.toBe(root.querySelector('app-page-card h1'));
  });

  it('focuses the heading again after a retry that succeeds', async () => {
    get.mockReturnValue(throwError(() => new HttpErrorResponse({ status: 500 })));
    const root = await open();

    (document.activeElement as HTMLElement | null)?.blur();

    expect(document.activeElement).not.toBe(root.querySelector('app-page-card h1'));

    get.mockReturnValue(of(sighting));
    button(root, 'Try again').click();
    await settle();

    expect(document.activeElement).toBe(root.querySelector('app-page-card h1'));
  });

  it('does not take focus on first load when the page is not created by a navigation', async () => {
    const fixture = TestBed.createComponent(SightingDetailPage);

    await fixture.whenStable();
    fixture.detectChanges();
    await fixture.whenStable();

    expect(fixture.nativeElement.querySelector('app-page-card h1')?.textContent?.trim()).toBe(
      'Limpy',
    );
    expect(document.activeElement).not.toBe(fixture.nativeElement.querySelector('h1'));
  });

  it('gives the map a one-sighting label, display-only markers and the criticality in the marker label', async () => {
    const root = await open();

    button(root, 'Show map').click();
    await settle();

    expect(mapStub()?.interactiveMarkers()).toBe(false);
    expect(mapStub()?.label()).toBe('Map showing where this sighting was reported');
    const labels = (mapStub()?.markers() ?? []).map((marker) => marker.label);

    expect(labels).toEqual(['Limpy, Needs help soon']);
  });

  it('hides a shown map and follows the title when the sighting reloads after a failure', async () => {
    const first = new Subject<Sighting>();

    get.mockReturnValueOnce(first);

    const root = await open();

    first.next(sighting);
    await settle();
    button(root, 'Show map').click();
    await settle();

    expect(root.querySelectorAll('app-map-view').length).toBe(1);
    expect(document.title).toBe('Limpy – PigeonWatch');

    first.error(new HttpErrorResponse({ status: 500 }));
    await settle();

    expect(document.title).toBe('Sighting – PigeonWatch');

    get.mockReturnValue(of(sighting));
    button(root, 'Try again').click();
    await settle();

    expect(root.querySelector('app-map-view')).toBeNull();
    expect(button(root, 'Show map').getAttribute('aria-expanded')).toBe('false');
  });

  it('starts over when the route moves to another sighting: loading, hidden map, new title, heading focus', async () => {
    const second = new Subject<Sighting>();

    get.mockImplementation((id) =>
      id === 'a' ? of({ ...sighting, id: 'a', name: 'First' }) : second,
    );

    const root = await open('a');

    button(root, 'Show map').click();
    await settle();

    expect(root.querySelectorAll('app-map-view').length).toBe(1);
    expect(document.title).toBe('First – PigeonWatch');

    (document.activeElement as HTMLElement | null)?.blur();

    await harness.navigateByUrl('/sightings/b');
    await settle();

    expect(get).toHaveBeenLastCalledWith('b');
    expect(root.querySelector('app-map-view')).toBeNull();
    expect(root.querySelector('app-page-card h1')?.textContent?.trim()).toBe('Sighting');
    expect(root.querySelector('app-loading-indicator')).not.toBeNull();
    expect(document.title).toBe('Sighting – PigeonWatch');
    expect(document.activeElement).not.toBe(root.querySelector('app-page-card h1'));

    second.next({ ...sighting, id: 'b', name: 'Second' });
    await settle();

    expect(root.querySelector('app-page-card h1')?.textContent?.trim()).toBe('Second');
    expect(document.title).toBe('Second – PigeonWatch');
    expect(document.activeElement).toBe(root.querySelector('app-page-card h1'));
  });

  it('ignores a slow answer for the sighting the route has already left', async () => {
    const slow = new Subject<Sighting>();

    get.mockImplementation((id) =>
      id === 'a' ? slow : of({ ...sighting, id: 'b', name: 'Second' }),
    );

    const root = await open('a');

    await harness.navigateByUrl('/sightings/b');
    await settle();

    slow.next({ ...sighting, id: 'a', name: 'First' });
    await settle();

    expect(root.querySelector('app-page-card h1')?.textContent?.trim()).toBe('Second');
    expect(document.title).toBe('Second – PigeonWatch');
  });
});
