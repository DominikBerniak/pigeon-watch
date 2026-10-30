import {
  HttpClient,
  HttpErrorResponse,
  provideHttpClient,
  withInterceptors,
} from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { environment } from '../../../environments/environment';
import { ErrorReportingService } from '../error-reporting/error-reporting.service';
import { authInterceptor } from './auth.interceptor';
import { SessionService } from './session.service';
import { TokenStore, refreshTokenStorageKey } from './token-store';

const apiUrl = environment.apiUrl;
const refreshUrl = `${apiUrl}/auth/refresh`;
const clientUrl = `${apiUrl}/configuration/client`;
const unauthorized = { status: 401, statusText: 'Unauthorized' };

describe('session expiry reporting', () => {
  let http: HttpClient;
  let httpMock: HttpTestingController;
  let tokens: TokenStore;
  let session: SessionService;
  let report: ReturnType<typeof vi.fn>;

  beforeEach(() => {
    report = vi.fn();
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(withInterceptors([authInterceptor])),
        provideHttpClientTesting(),
        provideRouter([]),
        { provide: ErrorReportingService, useValue: { report } },
      ],
    });
    http = TestBed.inject(HttpClient);
    httpMock = TestBed.inject(HttpTestingController);
    tokens = TestBed.inject(TokenStore);
    session = TestBed.inject(SessionService);
    vi.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true);
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

  it('reports a rejected refresh once and still expires the session', () => {
    signIn();
    const expire = vi.spyOn(session, 'expire');

    http.get(`${apiUrl}/a`).subscribe({ error: () => undefined });
    httpMock.expectOne(`${apiUrl}/a`).flush(null, unauthorized);
    httpMock.expectOne(refreshUrl).flush(null, unauthorized);

    expect(report).toHaveBeenCalledTimes(1);
    expect(report).toHaveBeenCalledWith(
      'session-expired',
      'auth.refresh.rejected',
      expect.objectContaining({ status: 401 }),
    );
    expect(expire).toHaveBeenCalledTimes(1);
    expect(tokens.accessToken()).toBeNull();
  });

  it('reports a missing refresh token once and still expires the session', () => {
    const expire = vi.spyOn(session, 'expire');

    http.get(`${apiUrl}/a`).subscribe({ error: () => undefined });
    httpMock.expectOne(`${apiUrl}/a`).flush(null, unauthorized);

    expect(report).toHaveBeenCalledTimes(1);
    expect(report).toHaveBeenCalledWith(
      'session-expired',
      'auth.refresh.missing',
      expect.objectContaining({ status: 401 }),
    );
    expect(expire).toHaveBeenCalledTimes(1);
  });

  it('reports nothing when the refresh fails with a transient status', () => {
    signIn();

    http.get(`${apiUrl}/a`).subscribe({ error: () => undefined });
    httpMock.expectOne(`${apiUrl}/a`).flush(null, unauthorized);
    httpMock.expectOne(refreshUrl).flush(null, { status: 503, statusText: 'Unavailable' });

    expect(report).not.toHaveBeenCalled();
  });

  it('reports a failed restore with a 5xx and still starts anonymous', async () => {
    localStorage.setItem(refreshTokenStorageKey, 'refresh-1');

    session.initialize();
    httpMock.expectOne(clientUrl).flush({});
    httpMock.expectOne(refreshUrl).flush(null, { status: 500, statusText: 'Server Error' });
    await firstValueFrom(session.whenReady());

    expect(report).toHaveBeenCalledTimes(1);
    expect(report).toHaveBeenCalledWith(
      'session-expired',
      'session.restore.failed',
      expect.objectContaining({ status: 500 }),
    );
    expect(session.isAuthenticated()).toBe(false);
  });

  it('does not report a restore that the API rejects as an expired session', async () => {
    localStorage.setItem(refreshTokenStorageKey, 'refresh-1');

    session.initialize();
    httpMock.expectOne(clientUrl).flush({});
    httpMock.expectOne(refreshUrl).flush(null, unauthorized);
    await firstValueFrom(session.whenReady());

    expect(report).not.toHaveBeenCalled();
  });

  it('reports a failed client configuration load as a fallback', () => {
    session.initialize();
    httpMock.expectOne(clientUrl).flush(null, { status: 500, statusText: 'Server Error' });

    expect(report).toHaveBeenCalledTimes(1);
    expect(report).toHaveBeenCalledWith(
      'fallback',
      'config.client.failed',
      expect.any(HttpErrorResponse),
    );
  });
});
