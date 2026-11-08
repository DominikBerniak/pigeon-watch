import { HttpErrorResponse, provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { environment } from '../../../environments/environment';
import { ErrorReportingService } from '../error-reporting/error-reporting.service';
import {
  UnreadCountService,
  unreadCountNavigationThrottleMilliseconds,
} from './unread-count.service';

const countUrl = `${environment.apiUrl}/threads/unread-count`;

describe('UnreadCountService', () => {
  let service: UnreadCountService;
  let httpMock: HttpTestingController;
  let report: ReturnType<typeof vi.fn>;

  beforeEach(() => {
    report = vi.fn();
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: ErrorReportingService, useValue: { report } },
      ],
    });
    service = TestBed.inject(UnreadCountService);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    vi.useRealTimers();
    httpMock.verify();
  });

  it('starts at zero and sends nothing while signed out', () => {
    service.refresh();
    service.navigated();

    expect(service.count()).toBe(0);
    httpMock.expectNone(countUrl);
  });

  it('fetches the count when started and on refresh', () => {
    service.start();
    const first = httpMock.expectOne(countUrl);

    expect(first.request.method).toBe('GET');

    first.flush({ count: 3 });

    expect(service.count()).toBe(3);

    service.refresh();
    httpMock.expectOne(countUrl).flush({ count: 1 });

    expect(service.count()).toBe(1);
  });

  it('clears the count and stops fetching when cleared', () => {
    service.start();
    httpMock.expectOne(countUrl).flush({ count: 2 });

    service.clear();
    service.refresh();
    service.navigated();

    expect(service.count()).toBe(0);
    httpMock.expectNone(countUrl);
  });

  it('drops an answer that arrives after sign-out', () => {
    service.start();
    const pending = httpMock.expectOne(countUrl);

    service.clear();

    expect(pending.cancelled).toBe(true);
    expect(service.count()).toBe(0);
  });

  it('throttles route changes to one leading and one trailing fetch per window', () => {
    vi.useFakeTimers();
    service.start();
    httpMock.expectOne(countUrl).flush({ count: 0 });

    service.navigated();
    httpMock.expectOne(countUrl).flush({ count: 1 });

    service.navigated();
    service.navigated();
    httpMock.expectNone(countUrl);

    vi.advanceTimersByTime(unreadCountNavigationThrottleMilliseconds);
    httpMock.expectOne(countUrl).flush({ count: 2 });

    expect(service.count()).toBe(2);
  });

  it('keeps the last count and reports an unexpected failure', () => {
    service.start();
    httpMock.expectOne(countUrl).flush({ count: 4 });

    service.refresh();
    httpMock.expectOne(countUrl).flush(null, { status: 500, statusText: 'Server Error' });

    expect(service.count()).toBe(4);
    expect(report).toHaveBeenCalledExactlyOnceWith(
      'api-failure',
      'messages.unread.count',
      expect.any(HttpErrorResponse),
    );

    service.refresh();
    httpMock.expectOne(countUrl).flush({ count: 5 });

    expect(service.count()).toBe(5);
  });

  it('keeps the last count without a report for an expected status', () => {
    service.start();
    httpMock.expectOne(countUrl).flush({ count: 4 });

    service.refresh();
    httpMock.expectOne(countUrl).flush(null, { status: 401, statusText: 'Unauthorized' });

    expect(service.count()).toBe(4);
    expect(report).not.toHaveBeenCalled();
  });
});
