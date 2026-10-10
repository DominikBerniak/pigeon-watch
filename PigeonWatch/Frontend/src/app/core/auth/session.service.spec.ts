import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { Observable, firstValueFrom } from 'rxjs';
import { environment } from '../../../environments/environment';
import { ConfigurationService } from '../configuration/configuration.service';
import { authInterceptor } from './auth.interceptor';
import { AutoLoginError, ReLoginError, SessionService } from './session.service';
import { TokenStore, refreshTokenStorageKey } from './token-store';

const apiUrl = environment.apiUrl;
const generalUrl = `${apiUrl}/configuration/general`;
const clientUrl = `${apiUrl}/configuration/client`;
const refreshUrl = `${apiUrl}/auth/refresh`;
const currentUser = { id: '1', email: 'jan@example.com', displayName: 'Jan K', roles: [] };
const tokenResponse = {
  tokenType: 'Bearer',
  accessToken: 'access-2',
  expiresIn: 3600,
  refreshToken: 'refresh-2',
};

function tick(): Promise<void> {
  return new Promise((resolve) => setTimeout(resolve));
}

describe('SessionService', () => {
  let httpMock: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(withInterceptors([authInterceptor])),
        provideHttpClientTesting(),
        provideRouter([]),
      ],
    });
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    localStorage.clear();
    vi.restoreAllMocks();
  });

  async function settle(ready: Observable<void>): Promise<boolean> {
    let emitted = false;
    let completed = false;
    ready.subscribe({ next: () => (emitted = true), complete: () => (completed = true) });
    await tick();

    return emitted && completed;
  }

  it('without a refresh token never calls general and is ready while client is pending', async () => {
    const session = TestBed.inject(SessionService);

    session.initialize();

    expect(await settle(session.whenReady())).toBe(true);
    expect(await settle(session.whenReady())).toBe(true);
    const client = httpMock.expectOne(clientUrl);
    expect(client.request.headers.has('Authorization')).toBe(false);
    httpMock.expectNone(generalUrl);
    expect(session.isAuthenticated()).toBe(false);
    client.flush({
      passwordRules: {
        minLength: 8,
        requireDigit: true,
        requireLowercase: true,
        requireUppercase: true,
        requireNonAlphanumeric: true,
      },
      displayNameRules: { minLength: 3, maxLength: 30 },
    });
    await tick();
    expect(TestBed.inject(ConfigurationService).clientConfig()?.passwordRules.minLength).toBe(8);
    httpMock.verify();
  });

  it('never rejects when the client configuration fails', async () => {
    const session = TestBed.inject(SessionService);

    session.initialize();
    httpMock.expectOne(clientUrl).flush(null, { status: 503, statusText: 'Unavailable' });

    await expect(firstValueFrom(session.whenReady())).resolves.toBeUndefined();
    httpMock.verify();
  });

  it('restores a stored session by refreshing, then loading general', async () => {
    localStorage.setItem(refreshTokenStorageKey, 'refresh-1');
    const session = TestBed.inject(SessionService);

    session.initialize();
    httpMock.expectOne(clientUrl).flush({});
    expect(await settle(session.whenReady())).toBe(false);

    const refresh = httpMock.expectOne(refreshUrl);
    expect(refresh.request.body).toEqual({ refreshToken: 'refresh-1' });
    refresh.flush(tokenResponse);
    await tick();

    const general = httpMock.expectOne(generalUrl);
    expect(general.request.headers.get('Authorization')).toBe('Bearer access-2');
    general.flush({ currentUser });
    await firstValueFrom(session.whenReady());

    expect(session.currentUser()?.displayName).toBe('Jan K');
    expect(session.isAuthenticated()).toBe(true);
    httpMock.verify();
  });

  it('drops a rejected refresh token during restore without rejecting', async () => {
    localStorage.setItem(refreshTokenStorageKey, 'refresh-1');
    const session = TestBed.inject(SessionService);

    session.initialize();
    httpMock.expectOne(clientUrl).flush({});
    httpMock.expectOne(refreshUrl).flush(null, { status: 401, statusText: 'Unauthorized' });
    await firstValueFrom(session.whenReady());

    expect(session.isAuthenticated()).toBe(false);
    expect(localStorage.getItem(refreshTokenStorageKey)).toBeNull();
    httpMock.expectNone(generalUrl);
    httpMock.verify();
  });

  it('keeps the refresh token when the restore fails with 503', async () => {
    localStorage.setItem(refreshTokenStorageKey, 'refresh-1');
    const session = TestBed.inject(SessionService);

    session.initialize();
    httpMock.expectOne(clientUrl).flush({});
    httpMock.expectOne(refreshUrl).flush(null, { status: 503, statusText: 'Unavailable' });
    await firstValueFrom(session.whenReady());

    expect(localStorage.getItem(refreshTokenStorageKey)).toBe('refresh-1');
    httpMock.verify();
  });

  it('shares one in-flight refresh between callers', () => {
    TestBed.inject(TokenStore).setTokens({ ...tokenResponse, refreshToken: 'refresh-1' });
    const session = TestBed.inject(SessionService);
    const received: string[] = [];

    session.refreshTokens().subscribe((response) => received.push(response.accessToken));
    session.refreshTokens().subscribe((response) => received.push(response.accessToken));
    httpMock.expectOne(refreshUrl).flush(tokenResponse);
    session.refreshTokens().subscribe((response) => received.push(response.accessToken));
    httpMock.expectOne(refreshUrl).flush({ ...tokenResponse, accessToken: 'access-3' });

    expect(received).toEqual(['access-2', 'access-2', 'access-3']);
    httpMock.verify();
  });

  it('drops a refresh that completes after logout', () => {
    TestBed.inject(TokenStore).setTokens({ ...tokenResponse, refreshToken: 'refresh-1' });
    const session = TestBed.inject(SessionService);
    vi.spyOn(TestBed.inject(Router), 'navigateByUrl').mockResolvedValue(true);
    const received: string[] = [];

    session.refreshTokens().subscribe((response) => received.push(response.accessToken));
    const refresh = httpMock.expectOne(refreshUrl);
    session.logout();
    refresh.flush(tokenResponse);

    expect(received).toEqual([]);
    expect(localStorage.getItem(refreshTokenStorageKey)).toBeNull();
    expect(TestBed.inject(TokenStore).accessToken()).toBeNull();
    httpMock.verify();
  });

  it('logs in by storing the tokens and loading general', async () => {
    const session = TestBed.inject(SessionService);

    const login = firstValueFrom(session.login('jan@example.com', 'Secret1!'));
    const request = httpMock.expectOne(`${apiUrl}/auth/login`);
    expect(request.request.body).toEqual({ email: 'jan@example.com', password: 'Secret1!' });
    request.flush(tokenResponse);
    await tick();
    httpMock.expectOne(generalUrl).flush({ currentUser });
    await login;

    expect(TestBed.inject(TokenStore).accessToken()).toBe('access-2');
    expect(localStorage.getItem(refreshTokenStorageKey)).toBe('refresh-2');
    expect(session.isAuthenticated()).toBe(true);
    httpMock.verify();
  });

  it('registers, then logs in', async () => {
    const session = TestBed.inject(SessionService);
    const request = { email: 'jan@example.com', password: 'Secret1!', displayName: 'Jan K' };

    const register = firstValueFrom(session.register(request));
    const registration = httpMock.expectOne(`${apiUrl}/account/register`);
    expect(registration.request.body).toEqual(request);
    httpMock.expectNone(`${apiUrl}/auth/login`);
    registration.flush({ email: request.email, displayName: request.displayName });
    const login = httpMock.expectOne(`${apiUrl}/auth/login`);
    expect(login.request.body).toEqual({ email: request.email, password: request.password });
    login.flush(tokenResponse);
    httpMock.expectOne(generalUrl).flush({ currentUser });
    await register;

    expect(session.isAuthenticated()).toBe(true);
    httpMock.verify();
  });

  it('reports a registration error as it is', async () => {
    const session = TestBed.inject(SessionService);
    const request = { email: 'jan@example.com', password: 'Secret1!', displayName: 'Jan K' };

    const register = firstValueFrom(session.register(request));
    httpMock
      .expectOne(`${apiUrl}/account/register`)
      .flush({ errors: { RegistrationFailed: ['x'] } }, { status: 400, statusText: 'Bad Request' });

    await expect(register).rejects.toMatchObject({ status: 400 });
    httpMock.expectNone(`${apiUrl}/auth/login`);
    httpMock.verify();
  });

  it('wraps a failed auto-login after registration in AutoLoginError', async () => {
    const session = TestBed.inject(SessionService);
    const request = { email: 'jan@example.com', password: 'Secret1!', displayName: 'Jan K' };

    const register = firstValueFrom(session.register(request));
    httpMock
      .expectOne(`${apiUrl}/account/register`)
      .flush({ email: request.email, displayName: request.displayName });
    httpMock
      .expectOne(`${apiUrl}/auth/login`)
      .flush(null, { status: 503, statusText: 'Service Unavailable' });

    const error = await register.catch((failure: unknown) => failure);
    expect(error).toBeInstanceOf(AutoLoginError);
    expect((error as AutoLoginError).cause).toMatchObject({ status: 503 });
    expect(session.isAuthenticated()).toBe(false);
    httpMock.verify();
  });

  describe('changePassword', () => {
    const passwordUrl = `${apiUrl}/account/password`;
    const loginUrl = `${apiUrl}/auth/login`;
    const renewedTokens = {
      tokenType: 'Bearer',
      accessToken: 'access-3',
      expiresIn: 3600,
      refreshToken: 'refresh-3',
    };

    async function signIn(session: SessionService): Promise<void> {
      const login = firstValueFrom(session.login('jan@example.com', 'Old1!pass'));
      httpMock.expectOne(loginUrl).flush(tokenResponse);
      await tick();
      httpMock.expectOne(generalUrl).flush({ currentUser });
      await login;
    }

    it('changes the password first, then logs in again with the new password', async () => {
      const session = TestBed.inject(SessionService);
      await signIn(session);

      const change = firstValueFrom(session.changePassword('Old1!pass', 'New1!passw'));
      const request = httpMock.expectOne(passwordUrl);
      expect(request.request.method).toBe('PUT');
      expect(request.request.headers.get('Authorization')).toBe('Bearer access-2');
      expect(request.request.body).toEqual({
        currentPassword: 'Old1!pass',
        newPassword: 'New1!passw',
      });
      httpMock.expectNone(loginUrl);

      request.flush({ changed: true });
      const relogin = httpMock.expectOne(loginUrl);
      expect(relogin.request.body).toEqual({ email: 'jan@example.com', password: 'New1!passw' });
      relogin.flush(renewedTokens);
      await tick();
      httpMock.expectOne(generalUrl).flush({ currentUser });
      await change;

      expect(TestBed.inject(TokenStore).accessToken()).toBe('access-3');
      expect(localStorage.getItem(refreshTokenStorageKey)).toBe('refresh-3');
      expect(session.isAuthenticated()).toBe(true);
      httpMock.verify();
    });

    it('reports a rejected change as it is without logging in again', async () => {
      const session = TestBed.inject(SessionService);
      await signIn(session);

      const change = firstValueFrom(session.changePassword('Wrong1!pass', 'New1!passw'));
      httpMock
        .expectOne(passwordUrl)
        .flush(
          { status: 400, errors: { PasswordMismatch: ['Incorrect password.'] } },
          { status: 400, statusText: 'Bad Request' },
        );

      await expect(change).rejects.toMatchObject({ status: 400 });
      httpMock.expectNone(loginUrl);
      expect(session.isAuthenticated()).toBe(true);
      expect(TestBed.inject(TokenStore).accessToken()).toBe('access-2');
      httpMock.verify();
    });

    it('wraps a failed login after the change in ReLoginError and ends the session', async () => {
      const session = TestBed.inject(SessionService);
      await signIn(session);

      const change = firstValueFrom(session.changePassword('Old1!pass', 'New1!passw'));
      httpMock.expectOne(passwordUrl).flush({ changed: true });
      httpMock.expectOne(loginUrl).flush(null, { status: 503, statusText: 'Service Unavailable' });

      const error = await change.catch((failure: unknown) => failure);
      expect(error).toBeInstanceOf(ReLoginError);
      expect((error as ReLoginError).cause).toMatchObject({ status: 503 });
      expect(session.isAuthenticated()).toBe(false);
      expect(TestBed.inject(TokenStore).accessToken()).toBeNull();
      expect(localStorage.getItem(refreshTokenStorageKey)).toBeNull();
      httpMock.verify();
    });

    it('rejects without any request when nobody is signed in', async () => {
      const session = TestBed.inject(SessionService);

      await expect(
        firstValueFrom(session.changePassword('Old1!pass', 'New1!passw')),
      ).rejects.toMatchObject({ status: 401 });

      httpMock.expectNone(passwordUrl);
      httpMock.expectNone(loginUrl);
      httpMock.verify();
    });
  });

  it('logs out by clearing the session and navigating to /login', () => {
    const session = TestBed.inject(SessionService);
    const navigate = vi.spyOn(TestBed.inject(Router), 'navigateByUrl').mockResolvedValue(true);
    TestBed.inject(TokenStore).setTokens(tokenResponse);

    session.logout();

    expect(localStorage.getItem(refreshTokenStorageKey)).toBeNull();
    expect(TestBed.inject(TokenStore).accessToken()).toBeNull();
    expect(navigate).toHaveBeenCalledWith('/login');
  });

  it('expires to /login with the return URL', () => {
    const session = TestBed.inject(SessionService);
    const navigate = vi.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true);

    session.expire('/sightings/1');

    expect(navigate).toHaveBeenCalledWith(['/login'], {
      queryParams: { returnUrl: '/sightings/1' },
    });
  });

  it('expires when another tab removes the refresh token', async () => {
    const session = TestBed.inject(SessionService);
    const login = firstValueFrom(session.login('jan@example.com', 'Secret1!'));
    httpMock.expectOne(`${apiUrl}/auth/login`).flush(tokenResponse);
    await tick();
    httpMock.expectOne(generalUrl).flush({ currentUser });
    await login;
    const expire = vi.spyOn(session, 'expire').mockImplementation(() => undefined);

    window.dispatchEvent(
      new StorageEvent('storage', { key: refreshTokenStorageKey, newValue: null }),
    );

    expect(expire).toHaveBeenCalledExactlyOnceWith('/');
  });

  it('ignores storage events that keep the refresh token', () => {
    const session = TestBed.inject(SessionService);
    const expire = vi.spyOn(session, 'expire');

    window.dispatchEvent(
      new StorageEvent('storage', { key: refreshTokenStorageKey, newValue: 'refresh-9' }),
    );

    expect(expire).not.toHaveBeenCalled();
  });
});
