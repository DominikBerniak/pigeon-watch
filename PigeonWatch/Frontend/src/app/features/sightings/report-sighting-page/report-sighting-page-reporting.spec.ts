import { HttpErrorResponse, provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { HarnessLoader } from '@angular/cdk/testing';
import { TestbedHarnessEnvironment } from '@angular/cdk/testing/testbed';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { MatSelectHarness } from '@angular/material/select/testing';
import { By } from '@angular/platform-browser';
import { Router, provideRouter } from '@angular/router';
import { Observable, of, throwError } from 'rxjs';
import { ErrorReportingService } from '../../../core/error-reporting/error-reporting.service';
import { GeoPoint } from '../../../core/geolocation/geo-point';
import {
  GeolocationOutcome,
  GeolocationService,
} from '../../../core/geolocation/geolocation.service';
import { LocationPicker } from '../../../shared/map/location-picker/location-picker';
import { MapView } from '../../../shared/map/map-view/map-view';
import { MapViewStub } from '../../../shared/map/testing/map-view-stub';
import { NewSighting, Sighting } from '../sighting.types';
import { SightingsApi } from '../sightings-api';
import { ReportSightingPage } from './report-sighting-page';

const warsaw: GeoPoint = { latitude: 52.2297, longitude: 21.0122 };
const traceId = '00-abc123-def456-01';

describe('ReportSightingPage failure reporting', () => {
  let create: ReturnType<typeof vi.fn<(sighting: NewSighting) => Observable<Sighting>>>;
  let report: ReturnType<typeof vi.fn>;
  let fixture: ComponentFixture<ReportSightingPage>;
  let loader: HarnessLoader;

  beforeEach(async () => {
    report = vi.fn();
    create = vi.fn<(sighting: NewSighting) => Observable<Sighting>>();
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        {
          provide: GeolocationService,
          useValue: {
            currentPosition: (): Observable<GeolocationOutcome> => of({ status: 'denied' }),
          },
        },
        { provide: SightingsApi, useValue: { create } },
        { provide: ErrorReportingService, useValue: { report } },
      ],
    });
    TestBed.overrideComponent(LocationPicker, {
      remove: { imports: [MapView] },
      add: { imports: [MapViewStub] },
    });
    vi.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true);
    fixture = TestBed.createComponent(ReportSightingPage);
    loader = TestbedHarnessEnvironment.loader(fixture);
    await fixture.whenStable();
  });

  async function settle(): Promise<void> {
    await fixture.whenStable();
    fixture.detectChanges();
    await fixture.whenStable();
  }

  function root(): HTMLElement {
    return fixture.nativeElement as HTMLElement;
  }

  function setValue(element: HTMLInputElement | HTMLTextAreaElement, value: string): void {
    element.value = value;
    element.dispatchEvent(new Event('input'));
    element.dispatchEvent(new Event('change'));
    element.dispatchEvent(new Event('blur'));
  }

  async function submitWith(error: HttpErrorResponse): Promise<void> {
    create.mockReturnValue(throwError(() => error));

    const textareas = [...root().querySelectorAll<HTMLTextAreaElement>('textarea')];
    const name = root().querySelector<HTMLInputElement>('input[type="text"]');

    if (!name) throw new Error('No name input');

    setValue(name, 'Limpy');
    setValue(textareas[0], 'Grey pigeon');
    setValue(textareas[1], 'Cannot fly');

    const [status, criticality] = await loader.getAllHarnesses(MatSelectHarness);

    await status.open();
    await status.clickOptions({ text: 'Caught' });
    await criticality.open();
    await criticality.clickOptions({ text: 'Urgent' });
    (fixture.debugElement.query(By.directive(MapViewStub)).componentInstance as MapViewStub).mapClick.emit(
      warsaw,
    );
    await settle();
    root().querySelector('form')?.dispatchEvent(new Event('submit', { cancelable: true }));
    await settle();
  }

  function alertText(): string | undefined {
    return root().querySelector('app-alert[role="alert"]')?.textContent?.trim();
  }

  it('shows the reference and reports api-failure for a 500 with a trace id', async () => {
    const error = new HttpErrorResponse({ status: 500, error: { traceId } });

    await submitWith(error);

    expect(alertText()).toContain(`Reference: ${traceId}`);
    expect(report).toHaveBeenCalledExactlyOnceWith('api-failure', 'sighting.report', error);
  });

  it('reports a 500 without a trace id and shows no reference', async () => {
    const error = new HttpErrorResponse({ status: 500 });

    await submitWith(error);

    expect(alertText()).not.toBeUndefined();
    expect(alertText()).not.toContain('Reference');
    expect(report).toHaveBeenCalledExactlyOnceWith('api-failure', 'sighting.report', error);
  });

  it('does not report a validation 400 or show a reference', async () => {
    await submitWith(
      new HttpErrorResponse({
        status: 400,
        error: { status: 400, traceId, errors: { InvalidLocation: ['x'] } },
      }),
    );

    expect(alertText()).not.toBeUndefined();
    expect(alertText()).not.toContain('Reference');
    expect(report).not.toHaveBeenCalled();
  });

  it('does not report a cooldown or show a reference', async () => {
    await submitWith(
      new HttpErrorResponse({
        status: 400,
        error: {
          status: 400,
          traceId,
          errors: { CooldownActive: ['x'] },
          retryAfterSeconds: 30,
        },
      }),
    );

    expect(alertText()).toContain('30');
    expect(alertText()).not.toContain('Reference');
    expect(report).not.toHaveBeenCalled();
  });

  it('does not report a 429 or show a reference', async () => {
    await submitWith(new HttpErrorResponse({ status: 429, error: { traceId } }));

    expect(alertText()).not.toContain('Reference');
    expect(report).not.toHaveBeenCalled();
  });
});
