import { Component } from '@angular/core';
import { HttpClient, HttpContext, provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { environment } from '../../../environments/environment';
import { SKIP_AUTH } from '../http/http-context-tokens';
import { authInterceptor } from './auth.interceptor';
import { SessionService } from './session.service';
import { TokenStore } from './token-store';

@Component({ template: '' })
class BlankPage {}

const apiUrl = environment.apiUrl;
const refreshUrl = `${apiUrl}/auth/refresh`;

describe('authInterceptor', () => {
  let http: HttpClient;
  let httpMock: HttpTestingController;
  let tokens: TokenStore;
  let session: SessionService;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(withInterceptors([authInterceptor])),
        provideHttpClientTesting(),
        provideRouter([{ path: 'sightings/:id', component: BlankPage }]),
      ],
    });
    http = TestBed.inject(HttpClient);
    httpMock = TestBed.inject(HttpTestingController);
    tokens = TestBed.inject(TokenStore);
    session = TestBed.inject(SessionService);
  });

  afterEach(() => {
    httpMock.verify();
    localStorage.clear();
    vi.restoreAllMocks();
  });

  function signIn(): void {
    tokens.setTokens({
      tokenType: 'Bearer',
      accessToken: 'access-1',
      expiresIn: 3600,
      refreshToken: 'refresh-1',
    });
  }

  it('adds the bearer header to API requests', () => {
    signIn();

    http.get(`${apiUrl}/configuration/general`).subscribe();

    const request = httpMock.expectOne(`${apiUrl}/configuration/general`);
    expect(request.request.headers.get('Authorization')).toBe('Bearer access-1');
    request.flush({});
  });

  it('does not add the header to requests outside the API', () => {
    signIn();

    http.get('https://example.com/data').subscribe();

    const request = httpMock.expectOne('https://example.com/data');
    expect(request.request.headers.has('Authorization')).toBe(false);
    request.flush({});
  });

  it('never adds the header to SKIP_AUTH requests', () => {
    signIn();

    http
      .get(`${apiUrl}/configuration/client`, { context: new HttpContext().set(SKIP_AUTH, true) })
      .subscribe();

    const request = httpMock.expectOne(`${apiUrl}/configuration/client`);
    expect(request.request.headers.has('Authorization')).toBe(false);
    request.flush({});
  });

  it('refreshes once for concurrent 401s and retries each request once', () => {
    signIn();
    const results: unknown[] = [];

    http.get(`${apiUrl}/a`).subscribe((body) => results.push(body));
    http.get(`${apiUrl}/b`).subscribe((body) => results.push(body));

    httpMock.expectOne(`${apiUrl}/a`).flush(null, { status: 401, statusText: 'Unauthorized' });
    httpMock.expectOne(`${apiUrl}/b`).flush(null, { status: 401, statusText: 'Unauthorized' });

    const refresh = httpMock.expectOne(refreshUrl);
    expect(refresh.request.body).toEqual({ refreshToken: 'refresh-1' });
    expect(refresh.request.headers.has('Authorization')).toBe(false);
    refresh.flush({
      tokenType: 'Bearer',
      accessToken: 'access-2',
      expiresIn: 3600,
      refreshToken: 'refresh-2',
    });

    const retriedA = httpMock.expectOne(`${apiUrl}/a`);
    const retriedB = httpMock.expectOne(`${apiUrl}/b`);
    expect(retriedA.request.headers.get('Authorization')).toBe('Bearer access-2');
    expect(retriedB.request.headers.get('Authorization')).toBe('Bearer access-2');
    retriedA.flush('a');
    retriedB.flush('b');

    httpMock.expectNone(refreshUrl);
    expect(results).toEqual(['a', 'b']);
  });

  it('does not retry a request a second time', () => {
    signIn();
    const errors: unknown[] = [];

    http.get(`${apiUrl}/a`).subscribe({ error: (error) => errors.push(error) });
    httpMock.expectOne(`${apiUrl}/a`).flush(null, { status: 401, statusText: 'Unauthorized' });
    httpMock.expectOne(refreshUrl).flush({
      tokenType: 'Bearer',
      accessToken: 'access-2',
      expiresIn: 3600,
      refreshToken: 'refresh-2',
    });
    httpMock.expectOne(`${apiUrl}/a`).flush(null, { status: 401, statusText: 'Unauthorized' });

    httpMock.expectNone(refreshUrl);
    expect(errors.length).toBe(1);
  });

  it('expires the session with the current URL when the refresh fails', async () => {
    await TestBed.inject(Router).navigateByUrl('/sightings/1');
    signIn();
    const expire = vi.spyOn(session, 'expire').mockImplementation(() => undefined);

    http.get(`${apiUrl}/a`).subscribe({ error: () => undefined });
    httpMock.expectOne(`${apiUrl}/a`).flush(null, { status: 401, statusText: 'Unauthorized' });
    httpMock.expectOne(refreshUrl).flush(null, { status: 401, statusText: 'Unauthorized' });

    expect(expire).toHaveBeenCalledExactlyOnceWith('/sightings/1');
  });

  it('expires the session when there is no refresh token', async () => {
    await TestBed.inject(Router).navigateByUrl('/sightings/1');
    const expire = vi.spyOn(session, 'expire').mockImplementation(() => undefined);

    http.get(`${apiUrl}/a`).subscribe({ error: () => undefined });
    httpMock.expectOne(`${apiUrl}/a`).flush(null, { status: 401, statusText: 'Unauthorized' });

    httpMock.expectNone(refreshUrl);
    expect(expire).toHaveBeenCalledExactlyOnceWith('/sightings/1');
  });

  it.each([503, 0, 429])('keeps the session when the refresh fails with %s', (status) => {
    signIn();
    const expire = vi.spyOn(session, 'expire');
    const errors: unknown[] = [];

    http.get(`${apiUrl}/a`).subscribe({ error: (error) => errors.push(error) });
    httpMock.expectOne(`${apiUrl}/a`).flush(null, { status: 401, statusText: 'Unauthorized' });
    const refresh = httpMock.expectOne(refreshUrl);

    if (status === 0) refresh.error(new ProgressEvent('error'));
    else refresh.flush(null, { status, statusText: 'Failure' });

    expect(expire).not.toHaveBeenCalled();
    expect(errors.length).toBe(1);
    expect(tokens.refreshToken()).toBe('refresh-1');
  });
});
