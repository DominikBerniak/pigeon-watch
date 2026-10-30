import { HttpErrorResponse, provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { Observable, throwError } from 'rxjs';
import { ErrorReportingService } from '../../../core/error-reporting/error-reporting.service';
import { SightingList } from '../sighting.types';
import { SightingsApi } from '../sightings-api';
import { MySightings } from './my-sightings';

const traceId = '00-abc123-def456-01';

describe('MySightings failure reporting', () => {
  let report: ReturnType<typeof vi.fn>;

  async function render(error: HttpErrorResponse): Promise<HTMLElement> {
    report = vi.fn();
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        {
          provide: SightingsApi,
          useValue: { mine: (): Observable<SightingList> => throwError(() => error) },
        },
        { provide: ErrorReportingService, useValue: { report } },
      ],
    });

    const fixture = TestBed.createComponent(MySightings);
    await fixture.whenStable();

    return fixture.nativeElement as HTMLElement;
  }

  it('shows the reference and reports api-failure for a 500 with a trace id', async () => {
    const error = new HttpErrorResponse({ status: 500, error: { traceId } });
    const root = await render(error);

    expect(root.querySelector('app-error-retry')?.textContent).toContain(`Reference: ${traceId}`);
    expect(report).toHaveBeenCalledExactlyOnceWith('api-failure', 'sightings.mine', error);
  });

  it('shows no reference for a 500 without a trace id but still reports it', async () => {
    const error = new HttpErrorResponse({ status: 500 });
    const root = await render(error);

    expect(root.querySelector('app-error-retry')).not.toBeNull();
    expect(root.textContent).not.toContain('Reference');
    expect(report).toHaveBeenCalledExactlyOnceWith('api-failure', 'sightings.mine', error);
  });

  it.each([400, 429])('does not report or show a reference for a %s', async (status) => {
    const root = await render(new HttpErrorResponse({ status, error: { traceId } }));

    expect(root.querySelector('app-error-retry')).not.toBeNull();
    expect(root.textContent).not.toContain('Reference');
    expect(report).not.toHaveBeenCalled();
  });
});
