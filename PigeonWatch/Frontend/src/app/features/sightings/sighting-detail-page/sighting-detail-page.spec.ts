import { HttpErrorResponse, provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { By } from '@angular/platform-browser';
import { provideRouter } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { Observable, of, throwError } from 'rxjs';
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

  it('shows the unnamed label as the title for a sighting without a name', async () => {
    get.mockReturnValue(of({ ...sighting, name: null }));

    const root = await open();

    expect(root.querySelector('app-page-card h1')?.textContent?.trim()).toBe('Unnamed bird');
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
        label: 'Limpy',
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

  it('links back to the map', async () => {
    const root = await open();
    const back = [...root.querySelectorAll('a')].find(
      (anchor) => anchor.textContent?.trim() === 'Back to map',
    );

    expect(back?.getAttribute('href')).toBe('/');
  });
});
