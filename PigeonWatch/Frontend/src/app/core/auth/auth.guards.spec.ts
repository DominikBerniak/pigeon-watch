import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import {
  ActivatedRouteSnapshot,
  Router,
  RouterStateSnapshot,
  UrlTree,
  convertToParamMap,
  provideRouter,
} from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { Observable, firstValueFrom, isObservable, of } from 'rxjs';
import { routes } from '../../app.routes';
import { authGuard, guestGuard } from './auth.guards';
import { SessionService } from './session.service';

describe('auth guards', () => {
  const authenticated = signal(false);

  beforeEach(() => {
    authenticated.set(false);
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter(routes),
        {
          provide: SessionService,
          useValue: {
            whenReady: () => of(undefined),
            isAuthenticated: authenticated.asReadonly(),
          },
        },
      ],
    });
  });

  function serialize(result: unknown): string {
    expect(result).toBeInstanceOf(UrlTree);

    return TestBed.inject(Router).serializeUrl(result as UrlTree);
  }

  function runGuard(guard: () => unknown): Promise<unknown> {
    const result = TestBed.runInInjectionContext(guard);
    expect(isObservable(result)).toBe(true);

    return firstValueFrom(result as Observable<unknown>);
  }

  function guestRoute(returnUrl: string | null): ActivatedRouteSnapshot {
    const params = returnUrl === null ? {} : { returnUrl };

    return { queryParamMap: convertToParamMap(params) } as ActivatedRouteSnapshot;
  }

  it('sends a logged-out visit to a protected page to login with a return URL', async () => {
    const result = await runGuard(() =>
      authGuard({} as ActivatedRouteSnapshot, { url: '/sightings/1' } as RouterStateSnapshot),
    );

    expect(serialize(result)).toBe('/login?returnUrl=%2Fsightings%2F1');
  });

  it('lets an authenticated user through', async () => {
    authenticated.set(true);

    const result = await runGuard(() =>
      authGuard({} as ActivatedRouteSnapshot, { url: '/' } as RouterStateSnapshot),
    );

    expect(result).toBe(true);
  });

  it('routes a logged-out deep link to login with the full return URL', async () => {
    const harness = await RouterTestingHarness.create();

    await harness.navigateByUrl('/sightings/1');

    expect(TestBed.inject(Router).url).toBe('/login?returnUrl=%2Fsightings%2F1');
  });

  it('keeps the query string of an unknown deep link in the return URL', async () => {
    const harness = await RouterTestingHarness.create();

    await harness.navigateByUrl('/anything?x=1');

    expect(TestBed.inject(Router).url).toBe('/login?returnUrl=%2Fanything%3Fx%3D1');
  });

  it('sends an authenticated user from an unknown URL to the home page', async () => {
    authenticated.set(true);
    const harness = await RouterTestingHarness.create();

    await harness.navigateByUrl('/anything');

    expect(TestBed.inject(Router).url).toBe('/');
  });

  it('routes a logged-out visit to the home page to login', async () => {
    const harness = await RouterTestingHarness.create();

    await harness.navigateByUrl('/');

    expect(TestBed.inject(Router).url).toBe('/login?returnUrl=%2F');
  });

  it('lets a guest open login', async () => {
    const result = await runGuard(() => guestGuard(guestRoute('/a'), {} as RouterStateSnapshot));

    expect(result).toBe(true);
  });

  it('sends an authenticated user from login to a safe return URL', async () => {
    authenticated.set(true);

    const result = await runGuard(() =>
      guestGuard(guestRoute('/a?b=1'), {} as RouterStateSnapshot),
    );

    expect(serialize(result)).toBe('/a?b=1');
  });

  it.each(['//evil.com', 'https://evil.com', null])(
    'sends an authenticated user to / for return URL %s',
    async (returnUrl) => {
      authenticated.set(true);

      const result = await runGuard(() =>
        guestGuard(guestRoute(returnUrl), {} as RouterStateSnapshot),
      );

      expect(serialize(result)).toBe('/');
    },
  );
});
