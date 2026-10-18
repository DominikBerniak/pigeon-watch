import { HttpErrorResponse, provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { environment } from '../../../environments/environment';
import { warmupBudgetMs, warmupInterceptor } from '../warmup/warmup.interceptor';
import { RegisterRequest } from './auth-api';
import { authInterceptor } from './auth.interceptor';
import { SessionService } from './session.service';
import { TokenStore } from './token-store';

const apiUrl = environment.apiUrl;
const registerUrl = `${apiUrl}/account/register`;
const loginUrl = `${apiUrl}/auth/login`;
const generalUrl = `${apiUrl}/configuration/general`;
const currentUser = { id: '1', email: 'jan@example.com', displayName: 'Jan K', roles: [] };
const tokenResponse = {
  tokenType: 'Bearer',
  accessToken: 'access-1',
  expiresIn: 3600,
  refreshToken: 'refresh-1',
};
const registrationFailedBody = {
  type: 'https://tools.ietf.org/html/rfc9110#section-15.5.1',
  title: 'One or more validation errors occurred.',
  status: 400,
  errors: { RegistrationFailed: ['The account could not be created.'] },
};
const account: RegisterRequest = {
  email: 'jan@example.com',
  password: 'Secret1!',
  displayName: 'Jan K',
};

describe('register during a cold start', () => {
  let httpMock: HttpTestingController;
  let session: SessionService;
  let outcomes: { completed: number; errors: unknown[] };

  beforeEach(() => {
    vi.useFakeTimers();
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(withInterceptors([authInterceptor, warmupInterceptor])),
        provideHttpClientTesting(),
        provideRouter([]),
      ],
    });
    httpMock = TestBed.inject(HttpTestingController);
    session = TestBed.inject(SessionService);
    outcomes = { completed: 0, errors: [] };
  });

  afterEach(() => {
    localStorage.clear();
    vi.useRealTimers();
  });

  function register(request: RegisterRequest = account): void {
    session.register(request).subscribe({
      complete: () => outcomes.completed++,
      error: (error: unknown) => outcomes.errors.push(error),
    });
  }

  function answerUnanswered(url: string, status: 0 | 503): void {
    const request = httpMock.expectOne(url);

    if (status === 0) {
      request.error(new ProgressEvent('error'));

      return;
    }

    request.flush(null, {
      status: 503,
      statusText: 'Service Unavailable',
      headers: { 'Retry-After': '10' },
    });
  }

  function exhaustWarmup(url: string, status: 0 | 503): void {
    for (let elapsed = 0; elapsed < warmupBudgetMs; elapsed += 10_000) {
      answerUnanswered(url, status);
      expect(outcomes.errors).toEqual([]);
      vi.advanceTimersByTime(10_000);
    }

    answerUnanswered(url, status);
  }

  function answerRegistrationFailed(): void {
    httpMock
      .expectOne(registerUrl)
      .flush(registrationFailedBody, { status: 400, statusText: 'Bad Request' });
  }

  function answerLoginSuccess(email: string, password: string): void {
    const login = httpMock.expectOne(loginUrl);
    expect(login.request.body).toEqual({ email, password });
    login.flush(tokenResponse);
    httpMock.expectOne(generalUrl).flush({ currentUser });
  }

  function expectRegistrationFailed(error: unknown): void {
    expect(error).toBeInstanceOf(HttpErrorResponse);
    expect(error).toMatchObject({ status: 400, error: registrationFailedBody });
  }

  function expectSignedIn(): void {
    expect(outcomes.errors).toEqual([]);
    expect(outcomes.completed).toBe(1);
    expect(session.isAuthenticated()).toBe(true);
    expect(TestBed.inject(TokenStore).accessToken()).toBe('access-1');
  }

  function unanswerEmail(email: string, status: 0 | 503 = 0): void {
    register({ ...account, email });
    exhaustWarmup(registerUrl, status);
    expect(outcomes.errors.length).toBe(1);
    expect(outcomes.errors[0]).toMatchObject({ status });
    outcomes = { completed: 0, errors: [] };
  }

  it('recovers with a login when a resend answers RegistrationFailed', () => {
    register();
    const first = httpMock.expectOne(registerUrl);
    const originalBody = structuredClone(first.request.body);
    first.error(new ProgressEvent('error'));
    vi.advanceTimersByTime(10_000);

    const resend = httpMock.expectOne(registerUrl);
    expect(resend.request.body).toEqual(originalBody);
    expect(resend.request.body).toEqual(account);
    resend.flush(registrationFailedBody, { status: 400, statusText: 'Bad Request' });

    answerLoginSuccess(account.email, account.password);

    expectSignedIn();
    vi.advanceTimersByTime(warmupBudgetMs);
    httpMock.verify();
  });

  it('gives the original 400 back when the recovery login is rejected', () => {
    register();
    httpMock.expectOne(registerUrl).error(new ProgressEvent('error'));
    vi.advanceTimersByTime(10_000);
    answerRegistrationFailed();

    const login = httpMock.expectOne(loginUrl);
    expect(login.request.body).toEqual({ email: account.email, password: account.password });
    login.flush(null, { status: 401, statusText: 'Unauthorized' });

    expect(outcomes.completed).toBe(0);
    expect(outcomes.errors.length).toBe(1);
    expectRegistrationFailed(outcomes.errors[0]);
    expect(session.isAuthenticated()).toBe(false);
    expect(TestBed.inject(TokenStore).accessToken()).toBeNull();
    vi.advanceTimersByTime(warmupBudgetMs);
    httpMock.verify();
  });

  it('does not recover when the resend succeeds after a 503 with Retry-After', () => {
    register();
    httpMock.expectOne(registerUrl).flush(null, {
      status: 503,
      statusText: 'Service Unavailable',
      headers: { 'Retry-After': '7' },
    });
    vi.advanceTimersByTime(7_000);

    httpMock
      .expectOne(registerUrl)
      .flush({ email: account.email, displayName: account.displayName });
    answerLoginSuccess(account.email, account.password);

    expectSignedIn();
    vi.advanceTimersByTime(warmupBudgetMs);
    httpMock.verify();
  });

  it.each([0, 503] as const)(
    'recovers a resubmit after a register left unanswered with status %i',
    (status) => {
      unanswerEmail(account.email, status);

      register();
      answerRegistrationFailed();
      answerLoginSuccess(account.email, account.password);

      expectSignedIn();
      httpMock.verify();
    },
  );

  it('does not log in after an ordinary duplicate', () => {
    register();
    answerRegistrationFailed();

    expect(outcomes.completed).toBe(0);
    expect(outcomes.errors.length).toBe(1);
    expectRegistrationFailed(outcomes.errors[0]);
    expect(session.isAuthenticated()).toBe(false);
    vi.advanceTimersByTime(warmupBudgetMs);
    httpMock.expectNone(loginUrl);
    httpMock.verify();
  });

  it('compares the unanswered email trimmed and case-insensitively', () => {
    unanswerEmail('  Jan@Example.COM ');

    register({ ...account, email: 'jan@example.com' });
    answerRegistrationFailed();
    answerLoginSuccess('jan@example.com', account.password);

    expectSignedIn();
    httpMock.verify();
  });

  it('keeps the remembered email when a different email answers RegistrationFailed', () => {
    unanswerEmail(account.email);

    register({ ...account, email: 'other@example.com' });
    answerRegistrationFailed();

    expect(outcomes.errors.length).toBe(1);
    expectRegistrationFailed(outcomes.errors[0]);
    httpMock.expectNone(loginUrl);
    outcomes = { completed: 0, errors: [] };

    register();
    answerRegistrationFailed();
    answerLoginSuccess(account.email, account.password);

    expectSignedIn();
    httpMock.verify();
  });

  it.each([0, 503] as const)(
    'propagates a recovery login that ends unanswered with status %i and keeps the email',
    (status) => {
      register();
      httpMock.expectOne(registerUrl).error(new ProgressEvent('error'));
      vi.advanceTimersByTime(10_000);
      answerRegistrationFailed();

      exhaustWarmup(loginUrl, status);

      expect(outcomes.completed).toBe(0);
      expect(outcomes.errors.length).toBe(1);
      expect(outcomes.errors[0]).toBeInstanceOf(HttpErrorResponse);
      expect(outcomes.errors[0]).toMatchObject({ status });
      expect(session.isAuthenticated()).toBe(false);
      outcomes = { completed: 0, errors: [] };

      register();
      answerRegistrationFailed();
      answerLoginSuccess(account.email, account.password);

      expectSignedIn();
      httpMock.verify();
    },
  );
});
