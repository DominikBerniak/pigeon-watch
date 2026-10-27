import { HttpErrorResponse, provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { Router } from '@angular/router';
import { environment } from '../../../environments/environment';
import { SKIP_AUTH, SKIP_WARMUP } from '../http/http-context-tokens';
import { ErrorReportingService } from './error-reporting.service';

const reportUrl = `${environment.apiUrl}/client-errors`;
const allowedKeys = ['context', 'errorName', 'httpStatus', 'kind', 'route', 'sessionId', 'traceId'];

describe('ErrorReportingService', () => {
  let httpMock: HttpTestingController;
  let service: ErrorReportingService;
  let url: string;

  beforeEach(() => {
    url = '/sightings/0f8fad5b-d9cb-469f-a165-70867728950e?email=a@b.pl&lat=52.1#frag';
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        {
          provide: Router,
          useValue: {
            get url() {
              return url;
            },
          },
        },
      ],
    });
    httpMock = TestBed.inject(HttpTestingController);
    service = TestBed.inject(ErrorReportingService);
  });

  afterEach(() => {
    httpMock.verify();
    vi.restoreAllMocks();
  });

  it('posts only allowlisted fields without query, id or message', () => {
    const error = new HttpErrorResponse({
      status: 500,
      error: { traceId: '00-abcdef0123456789-abcdef01-00', detail: 'secret@mail.com' },
      statusText: 'secret@mail.com',
    });

    service.report('api-failure', 'sightings.nearby', error);

    const request = httpMock.expectOne(reportUrl);
    const body = request.request.body as Record<string, unknown>;

    expect(request.request.method).toBe('POST');
    expect(Object.keys(body).every((key) => allowedKeys.includes(key))).toBe(true);
    expect(body).toMatchObject({
      kind: 'api-failure',
      context: 'sightings.nearby',
      httpStatus: 500,
      errorName: 'HttpErrorResponse',
      traceId: '00-abcdef0123456789-abcdef01-00',
      route: '/sightings/:id',
    });
    expect(JSON.stringify(body)).not.toMatch(/secret|@|\?|lat=|0f8fad5b|frag/);
    request.flush(null, { status: 202, statusText: 'Accepted' });
  });

  it('keeps one session id for the page load', () => {
    service.report('fallback', 'storage.unavailable');
    service.report('fallback', 'labels.failed');

    const [first, second] = httpMock.match(reportUrl);
    const a = (first.request.body as { sessionId: string }).sessionId;
    const b = (second.request.body as { sessionId: string }).sessionId;

    expect(a).toMatch(/^[0-9a-f-]{36}$/);
    expect(a).toBe(b);
  });

  it('omits an error name and trace id that do not match the contract', () => {
    const error = new HttpErrorResponse({ status: 500, error: { traceId: 'not hex!\n' } });

    service.report('api-failure', 'x.y', error);

    const body = httpMock.expectOne(reportUrl).request.body as Record<string, unknown>;

    expect(body['traceId']).toBeUndefined();

    service.report('uncaught', 'x.z', 'plain string');

    const second = httpMock.expectOne(reportUrl).request.body as Record<string, unknown>;

    expect(second['errorName']).toBeUndefined();
    expect(second['httpStatus']).toBeUndefined();
  });

  it('dedupes by kind, context and status', () => {
    const e503 = new HttpErrorResponse({ status: 503 });
    const e500 = new HttpErrorResponse({ status: 500 });

    service.report('api-failure', 'a.b', e503);
    service.report('api-failure', 'a.b', e503);
    service.report('api-failure', 'a.b', e500);
    service.report('fallback', 'a.b', e503);

    expect(httpMock.match(reportUrl).length).toBe(3);
  });

  it('drops the 21st distinct report', () => {
    for (let i = 0; i < 21; i++) service.report('fallback', `ctx.n${i}`);

    expect(httpMock.match(reportUrl).length).toBe(20);
  });

  it('opts out of auth and warm-up', () => {
    service.report('uncaught', 'app.error');

    const context = httpMock.expectOne(reportUrl).request.context;

    expect(context.get(SKIP_AUTH)).toBe(true);
    expect(context.get(SKIP_WARMUP)).toBe(true);
  });

  it('warns once on a failed send, does not throw and does not report it', () => {
    const warn = vi.spyOn(console, 'warn').mockImplementation(() => undefined);

    expect(() => service.report('uncaught', 'app.error')).not.toThrow();
    httpMock.expectOne(reportUrl).flush(null, { status: 0, statusText: 'Unknown' });

    expect(warn).toHaveBeenCalledTimes(1);
    httpMock.expectNone(reportUrl);
  });
});
