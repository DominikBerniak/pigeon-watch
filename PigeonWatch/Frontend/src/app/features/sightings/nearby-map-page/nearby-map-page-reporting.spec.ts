import { HttpErrorResponse, provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { By } from '@angular/platform-browser';
import { provideRouter } from '@angular/router';
import { Observable, of, throwError } from 'rxjs';
import { GeoPoint } from '../../../core/geolocation/geo-point';
import { GeolocationService } from '../../../core/geolocation/geolocation.service';
import { ErrorReportingService } from '../../../core/error-reporting/error-reporting.service';
import { MapView } from '../../../shared/map/map-view/map-view';
import { MapViewStub } from '../../../shared/map/testing/map-view-stub';
import { Sighting, SightingCriticality, SightingList, SightingStatus } from '../sighting.types';
import { SightingsApi } from '../sightings-api';
import { NearbyMapPage } from './nearby-map-page';

const warsaw: GeoPoint = { latitude: 52.2297, longitude: 21.0122 };
const traceId = '00-abc123-def456-01';

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

describe('NearbyMapPage failure reporting', () => {
  let nearby: ReturnType<typeof vi.fn<(centre: GeoPoint) => Observable<SightingList>>>;
  let report: ReturnType<typeof vi.fn>;
  let fixture: ComponentFixture<NearbyMapPage>;

  async function render(result: Observable<SightingList>): Promise<void> {
    report = vi.fn();
    nearby = vi.fn<(centre: GeoPoint) => Observable<SightingList>>(() => result);
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        {
          provide: GeolocationService,
          useValue: { currentPosition: () => of({ status: 'ok', ...warsaw }) },
        },
        { provide: SightingsApi, useValue: { nearby } },
        { provide: ErrorReportingService, useValue: { report } },
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

  function button(label: string): HTMLButtonElement {
    const match = [...root().querySelectorAll<HTMLButtonElement>('button')].find(
      (candidate) => candidate.textContent?.trim() === label,
    );

    if (!match) throw new Error(`No button "${label}"`);

    return match;
  }

  function alertText(): string | undefined {
    return root().querySelector('app-alert[role="alert"]')?.textContent?.trim();
  }

  it('shows the reference and reports api-failure for a 500 with a trace id', async () => {
    const error = new HttpErrorResponse({ status: 500, error: { traceId } });

    await render(throwError(() => error));

    expect(alertText()).toContain("We couldn't load sightings for this area.");
    expect(alertText()).toContain(`Reference: ${traceId}`);
    expect(report).toHaveBeenCalledExactlyOnceWith('api-failure', 'nearby.search', error);
  });

  it('shows no reference for a 500 without a trace id but still reports it', async () => {
    const error = new HttpErrorResponse({ status: 500 });

    await render(throwError(() => error));

    expect(alertText()).not.toContain('Reference');
    expect(report).toHaveBeenCalledExactlyOnceWith('api-failure', 'nearby.search', error);
  });

  it.each([400, 429])('does not report or show a reference for a %s', async (status) => {
    await render(throwError(() => new HttpErrorResponse({ status, error: { traceId } })));

    expect(alertText()).toContain("We couldn't load sightings for this area.");
    expect(alertText()).not.toContain('Reference');
    expect(report).not.toHaveBeenCalled();
  });

  it('keeps the previous markers and shows the error when a refresh fails', async () => {
    await render(of({ items: [named] }));

    expect(map().markers().length).toBe(1);

    nearby.mockReturnValue(throwError(() => new HttpErrorResponse({ status: 500, error: { traceId } })));
    button('Search this area').click();
    await settle();

    expect(map().markers().length).toBe(1);
    expect(alertText()).toContain(`Reference: ${traceId}`);
    expect(report).toHaveBeenCalledTimes(1);
  });

  it('drops the reference once a retry succeeds', async () => {
    await render(throwError(() => new HttpErrorResponse({ status: 500, error: { traceId } })));

    nearby.mockReturnValue(of({ items: [named] }));
    button('Try again').click();
    await settle();

    expect(root().textContent).not.toContain('Reference');
    expect(map().markers().length).toBe(1);
  });
});
