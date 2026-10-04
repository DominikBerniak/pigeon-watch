import { DOCUMENT } from '@angular/common';
import { HttpErrorResponse } from '@angular/common/http';
import { DestroyRef, Injectable, computed, inject } from '@angular/core';
import { Router } from '@angular/router';
import {
  Observable,
  ReplaySubject,
  catchError,
  defer,
  finalize,
  map,
  of,
  shareReplay,
  switchMap,
  tap,
  throwError,
} from 'rxjs';
import { ConfigurationService } from '../configuration/configuration.service';
import { WarmupState } from '../warmup/warmup-state';
import { AuthApi, RegisterRequest } from './auth-api';
import { AccessTokenResponse, TokenStore, refreshTokenStorageKey } from './token-store';

@Injectable({ providedIn: 'root' })
export class SessionService {
  private readonly tokens = inject(TokenStore);
  private readonly authApi = inject(AuthApi);
  private readonly configuration = inject(ConfigurationService);
  private readonly warmup = inject(WarmupState);
  private readonly router = inject(Router);
  private readonly ready = new ReplaySubject<void>(1);
  private refreshInFlight: Observable<AccessTokenResponse> | null = null;
  private initialized = false;

  readonly currentUser = computed(() => this.configuration.generalConfig()?.currentUser ?? null);
  readonly isAuthenticated = computed(() => this.currentUser() !== null);

  constructor() {
    const view = inject(DOCUMENT).defaultView;
    const onStorage = (event: StorageEvent): void => this.handleStorage(event);
    view?.addEventListener('storage', onStorage);
    inject(DestroyRef).onDestroy(() => view?.removeEventListener('storage', onStorage));
  }

  initialize(): void {
    if (this.initialized) return;

    this.initialized = true;
    this.configuration.loadClient().subscribe({ error: () => undefined });

    if (!this.tokens.refreshToken()) {
      this.markReady();

      return;
    }

    this.restore()
      .pipe(finalize(() => this.markReady()))
      .subscribe();
  }

  whenReady(): Observable<void> {
    return this.ready.asObservable();
  }

  login(email: string, password: string): Observable<void> {
    return this.authApi.login(email, password).pipe(
      tap((response) => this.tokens.setTokens(response)),
      switchMap(() => this.configuration.loadGeneral()),
      map(() => undefined),
    );
  }

  register(request: RegisterRequest): Observable<void> {
    return this.authApi
      .register(request)
      .pipe(switchMap(() => this.login(request.email, request.password)));
  }

  logout(): void {
    this.clearSession();
    void this.router.navigateByUrl('/login');
  }

  expire(returnUrl: string): void {
    this.clearSession();
    void this.router.navigate(['/login'], { queryParams: { returnUrl } });
  }

  refreshTokens(): Observable<AccessTokenResponse> {
    if (this.refreshInFlight) return this.refreshInFlight;

    this.refreshInFlight = defer(() => {
      const refreshToken = this.tokens.refreshToken();

      if (!refreshToken) return throwError(() => new HttpErrorResponse({ status: 401 }));

      return this.authApi.refresh(refreshToken);
    }).pipe(
      tap((response) => this.tokens.setTokens(response)),
      finalize(() => (this.refreshInFlight = null)),
      shareReplay({ bufferSize: 1, refCount: false }),
    );

    return this.refreshInFlight;
  }

  private restore(): Observable<void> {
    return this.refreshTokens().pipe(
      switchMap(() => this.configuration.loadGeneral()),
      map(() => undefined),
      catchError((error: unknown) => {
        if (isSessionRejected(error)) this.clearSession();

        return of(undefined);
      }),
    );
  }

  private markReady(): void {
    this.warmup.markStartupComplete();
    this.ready.next();
    this.ready.complete();
  }

  private clearSession(): void {
    this.tokens.clear();
    this.configuration.clearGeneral();
  }

  private handleStorage(event: StorageEvent): void {
    const refreshTokenRemoved =
      event.key === null || (event.key === refreshTokenStorageKey && event.newValue === null);

    if (!refreshTokenRemoved) return;

    if (this.isAuthenticated()) this.expire(this.router.url);
    else this.clearSession();
  }
}

function isSessionRejected(error: unknown): boolean {
  return error instanceof HttpErrorResponse && (error.status === 400 || error.status === 401);
}
