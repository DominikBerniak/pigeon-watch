import { HttpClient, provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { environment } from '../../../environments/environment';
import { ErrorReportingService } from '../error-reporting/error-reporting.service';
import { warmupBudgetMs, warmupInterceptor } from './warmup.interceptor';

const url = `${environment.apiUrl}/auth/login`;

describe('warm-up exhaustion reporting', () => {
  let http: HttpClient;
  let httpMock: HttpTestingController;
  let report: ReturnType<typeof vi.fn>;

  beforeEach(() => {
    vi.useFakeTimers();
    report = vi.fn();
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(withInterceptors([warmupInterceptor])),
        provideHttpClientTesting(),
        { provide: ErrorReportingService, useValue: { report } },
      ],
    });
    http = TestBed.inject(HttpClient);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    httpMock.verify();
    vi.useRealTimers();
  });

  function fail(status: number): void {
    const request = httpMock.expectOne(url);

    if (status === 0) {
      request.error(new ProgressEvent('error'));

      return;
    }

    request.flush(null, {
      status,
      statusText: 'Service Unavailable',
      headers: { 'Retry-After': '10' },
    });
  }

  function exhaust(status: number): void {
    http.post(url, {}).subscribe({ error: () => undefined });

    for (let elapsed = 0; elapsed < warmupBudgetMs; elapsed += 10_000) {
      fail(status);
      expect(report).not.toHaveBeenCalled();
      vi.advanceTimersByTime(10_000);
    }

    fail(status);
  }

  it('reports once with status 503 when the budget is exhausted', () => {
    exhaust(503);

    expect(report).toHaveBeenCalledTimes(1);
    expect(report).toHaveBeenCalledWith(
      'warmup-exhausted',
      'warmup.budget.exhausted',
      expect.objectContaining({ status: 503 }),
    );
    vi.advanceTimersByTime(60_000);
    httpMock.expectNone(url);
    expect(report).toHaveBeenCalledTimes(1);
  });

  it('reports once with status 0 when the budget is exhausted', () => {
    exhaust(0);

    expect(report).toHaveBeenCalledTimes(1);
    expect(report).toHaveBeenCalledWith(
      'warmup-exhausted',
      'warmup.budget.exhausted',
      expect.objectContaining({ status: 0 }),
    );
  });

  it('reports nothing when the request recovers inside the budget', () => {
    http.post(url, {}).subscribe();

    fail(503);
    vi.advanceTimersByTime(10_000);
    httpMock.expectOne(url).flush({});

    expect(report).not.toHaveBeenCalled();
  });
});
