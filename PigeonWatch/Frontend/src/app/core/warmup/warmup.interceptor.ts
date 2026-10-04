import { HttpErrorResponse, HttpEvent, HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';
import { Observable, Subscription } from 'rxjs';
import { environment } from '../../../environments/environment';
import { SKIP_WARMUP } from '../http/http-context-tokens';
import { WarmupState } from './warmup-state';

export const warmupPendingThresholdMs = 3_000;
export const warmupBudgetMs = 120_000;
export const defaultRetryAfterSeconds = 10;
export const minimumRetryAfterSeconds = 5;

export const warmupInterceptor: HttpInterceptorFn = (request, next) => {
  if (!request.url.startsWith(environment.apiUrl) || request.context.get(SKIP_WARMUP)) {
    return next(request);
  }

  const state = inject(WarmupState);

  return new Observable<HttpEvent<unknown>>((subscriber) => {
    const startedAt = Date.now();
    let warming = false;
    let attemptSubscription: Subscription | undefined;
    let retryHandle: ReturnType<typeof setTimeout> | undefined;

    const beginWarming = (): void => {
      if (warming) return;

      warming = true;
      state.begin();
    };

    const pendingHandle = setTimeout(beginWarming, warmupPendingThresholdMs);

    const endWarming = (outcome: 'recovered' | 'failed' | 'cancelled'): void => {
      clearTimeout(pendingHandle);

      if (!warming) return;

      warming = false;
      state.end(outcome);
    };

    const attempt = (): void => {
      attemptSubscription = next(request).subscribe({
        next: (event) => subscriber.next(event),
        error: (error: unknown) => {
          if (!isWarmupError(error)) {
            endWarming('recovered');
            subscriber.error(error);

            return;
          }

          beginWarming();

          if (Date.now() - startedAt >= warmupBudgetMs) {
            endWarming('failed');
            subscriber.error(error);

            return;
          }

          retryHandle = setTimeout(attempt, retryDelayMs(error));
        },
        complete: () => {
          endWarming('recovered');
          subscriber.complete();
        },
      });
    };

    attempt();

    return () => {
      clearTimeout(retryHandle);
      attemptSubscription?.unsubscribe();
      endWarming('cancelled');
    };
  });
};

function isWarmupError(error: unknown): error is HttpErrorResponse {
  return error instanceof HttpErrorResponse && (error.status === 503 || error.status === 0);
}

function retryDelayMs(error: HttpErrorResponse): number {
  const seconds = parseRetryAfter(error.headers?.get('Retry-After') ?? null);

  return Math.max(seconds, minimumRetryAfterSeconds) * 1000;
}

function parseRetryAfter(value: string | null): number {
  if (value === null || value.trim() === '') return defaultRetryAfterSeconds;

  const seconds = Number(value);

  if (Number.isFinite(seconds)) return seconds;

  const date = Date.parse(value);

  if (Number.isNaN(date)) return defaultRetryAfterSeconds;

  return (date - Date.now()) / 1000;
}
