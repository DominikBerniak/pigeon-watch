import { HttpErrorResponse, provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { Observable, throwError } from 'rxjs';
import { ErrorReportingService } from '../../../core/error-reporting/error-reporting.service';
import { MapView } from '../../../shared/map/map-view/map-view';
import { MapViewStub } from '../../../shared/map/testing/map-view-stub';
import { Sighting } from '../sighting.types';
import { SightingsApi } from '../sightings-api';
import { SightingDetailPage } from './sighting-detail-page';

const traceId = '00-abc123-def456-01';

describe('SightingDetailPage failure reporting', () => {
  let report: ReturnType<typeof vi.fn>;

  async function open(error: HttpErrorResponse): Promise<HTMLElement> {
    report = vi.fn();
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([{ path: 'sightings/:id', component: SightingDetailPage }]),
        {
          provide: SightingsApi,
          useValue: { get: (): Observable<Sighting> => throwError(() => error) },
        },
        { provide: ErrorReportingService, useValue: { report } },
      ],
    });
    TestBed.overrideComponent(SightingDetailPage, {
      remove: { imports: [MapView] },
      add: { imports: [MapViewStub] },
    });

    const harness = await RouterTestingHarness.create();

    await harness.navigateByUrl('/sightings/sighting-1');
    await harness.fixture.whenStable();
    harness.fixture.detectChanges();
    await harness.fixture.whenStable();

    return harness.routeNativeElement as HTMLElement;
  }

  it('shows the reference and reports api-failure for a 500 with a trace id', async () => {
    const error = new HttpErrorResponse({ status: 500, error: { traceId } });
    const root = await open(error);

    expect(root.querySelector('app-error-retry')?.textContent).toContain(`Reference: ${traceId}`);
    expect(report).toHaveBeenCalledExactlyOnceWith('api-failure', 'sighting.detail', error);
  });

  it('shows no reference for a 500 without a trace id but still reports it', async () => {
    const error = new HttpErrorResponse({ status: 500 });
    const root = await open(error);

    expect(root.querySelector('app-error-retry')).not.toBeNull();
    expect(root.textContent).not.toContain('Reference');
    expect(report).toHaveBeenCalledExactlyOnceWith('api-failure', 'sighting.detail', error);
  });

  it('does not report or show a reference for a 404', async () => {
    const root = await open(new HttpErrorResponse({ status: 404, error: { traceId } }));

    expect(root.querySelector('app-error-retry')).toBeNull();
    expect(root.textContent).not.toContain('Reference');
    expect(report).not.toHaveBeenCalled();
  });

  it.each([400, 429])('does not report or show a reference for a %s', async (status) => {
    const root = await open(new HttpErrorResponse({ status, error: { traceId } }));

    expect(root.querySelector('app-error-retry')).not.toBeNull();
    expect(root.textContent).not.toContain('Reference');
    expect(report).not.toHaveBeenCalled();
  });
});
