import { HttpClient, HttpContext, provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { environment } from '../../../environments/environment';
import { SKIP_WARMUP } from '../http/http-context-tokens';
import { WarmupState } from './warmup-state';
import { warmupInterceptor } from './warmup.interceptor';

const url = `${environment.apiUrl}/auth/login`;

describe('warmupInterceptor', () => {
  let http: HttpClient;
  let httpMock: HttpTestingController;
  let state: WarmupState;

  beforeEach(() => {
    vi.useFakeTimers();
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(withInterceptors([warmupInterceptor])),
        provideHttpClientTesting(),
      ],
    });
    http = TestBed.inject(HttpClient);
    httpMock = TestBed.inject(HttpTestingController);
    state = TestBed.inject(WarmupState);
  });

  afterEach(() => {
    httpMock.verify();
    vi.useRealTimers();
  });

  function unavailable(retryAfter?: string): void {
    const headers: Record<string, string> =
      retryAfter === undefined ? {} : { 'Retry-After': retryAfter };
    httpMock
      .expectOne(url)
      .flush(null, { status: 503, statusText: 'Service Unavailable', headers });
  }

  it('becomes warming when a request is still pending after 3 s', () => {
    http.post(url, {}).subscribe();

    vi.advanceTimersByTime(2_999);
    expect(state.status()).toBe('idle');

    vi.advanceTimersByTime(1);
    expect(state.status()).toBe('warming');

    httpMock.expectOne(url).flush({});
    expect(state.status()).toBe('idle');
  });

  it('stays idle for a request that answers quickly', () => {
    http.post(url, {}).subscribe();
    httpMock.expectOne(url).flush({});

    vi.advanceTimersByTime(5_000);

    expect(state.status()).toBe('idle');
  });

  it('retries a 503 after Retry-After seconds', () => {
    const results: unknown[] = [];
    http.post(url, {}).subscribe((body) => results.push(body));

    unavailable('7');
    expect(state.status()).toBe('warming');

    vi.advanceTimersByTime(6_999);
    httpMock.expectNone(url);

    vi.advanceTimersByTime(1);
    httpMock.expectOne(url).flush({ ok: true });

    expect(results).toEqual([{ ok: true }]);
    expect(state.status()).toBe('idle');
  });

  it('waits at least 5 s and defaults to 10 s between retries', () => {
    http.post(url, {}).subscribe();

    unavailable('1');
    vi.advanceTimersByTime(4_999);
    httpMock.expectNone(url);
    vi.advanceTimersByTime(1);

    unavailable();
    vi.advanceTimersByTime(9_999);
    httpMock.expectNone(url);
    vi.advanceTimersByTime(1);

    httpMock.expectOne(url).flush({});
    expect(state.status()).toBe('idle');
  });

  it('treats status 0 like a 503', () => {
    http.post(url, {}).subscribe();

    httpMock.expectOne(url).error(new ProgressEvent('error'));
    expect(state.status()).toBe('warming');

    vi.advanceTimersByTime(10_000);
    httpMock.expectOne(url).flush({});
    expect(state.status()).toBe('idle');
  });

  it('fails after 120 s and propagates the error', () => {
    const errors: unknown[] = [];
    http.post(url, {}).subscribe({ error: (error) => errors.push(error) });

    for (let elapsed = 0; elapsed < 120_000; elapsed += 10_000) {
      unavailable('10');
      expect(errors).toEqual([]);
      vi.advanceTimersByTime(10_000);
    }

    unavailable('10');

    expect(errors.length).toBe(1);
    expect(state.status()).toBe('failed');
    vi.advanceTimersByTime(60_000);
    httpMock.expectNone(url);
  });

  it('returns to idle once a failed warm-up is followed by a recovered request', () => {
    http.post(url, {}).subscribe({ error: () => undefined });

    for (let elapsed = 0; elapsed <= 120_000; elapsed += 10_000) {
      unavailable('10');
      vi.advanceTimersByTime(10_000);
    }

    expect(state.status()).toBe('failed');

    http.post(url, {}).subscribe();
    unavailable('10');
    expect(state.status()).toBe('warming');
    vi.advanceTimersByTime(10_000);
    httpMock.expectOne(url).flush({});

    expect(state.status()).toBe('idle');
  });

  it('does not retry a 429 and never shows warming', () => {
    const errors: unknown[] = [];
    http.post(url, {}).subscribe({ error: (error) => errors.push(error) });

    httpMock.expectOne(url).flush(null, { status: 429, statusText: 'Too Many Requests' });
    vi.advanceTimersByTime(60_000);

    httpMock.expectNone(url);
    expect(errors.length).toBe(1);
    expect(state.status()).toBe('idle');
  });

  it('ignores requests carrying SKIP_WARMUP', () => {
    const errors: unknown[] = [];
    const context = new HttpContext().set(SKIP_WARMUP, true);
    http.get(url, { context }).subscribe({ error: (error) => errors.push(error) });

    vi.advanceTimersByTime(5_000);
    expect(state.status()).toBe('idle');
    unavailable('5');
    vi.advanceTimersByTime(60_000);

    httpMock.expectNone(url);
    expect(errors.length).toBe(1);
    expect(state.status()).toBe('idle');
  });

  it('records whether the warm-up began during startup', () => {
    http.post(url, {}).subscribe();
    unavailable('5');
    expect(state.startedDuringStartup()).toBe(true);
    vi.advanceTimersByTime(5_000);
    httpMock.expectOne(url).flush({});

    state.markStartupComplete();
    http.post(url, {}).subscribe();
    unavailable('5');
    expect(state.startedDuringStartup()).toBe(false);
    vi.advanceTimersByTime(5_000);
    httpMock.expectOne(url).flush({});
  });
});
