import { HttpErrorResponse, HttpInterceptorFn, HttpRequest } from '@angular/common/http';
import { inject } from '@angular/core';
import { Router } from '@angular/router';
import { catchError, switchMap, throwError } from 'rxjs';
import { isApiUrl } from '../http/api-url';
import { AUTH_RETRIED, SKIP_AUTH } from '../http/http-context-tokens';
import { SessionService } from './session.service';
import { TokenStore } from './token-store';

export const authInterceptor: HttpInterceptorFn = (request, next) => {
  if (!isApiUrl(request.url) || request.context.get(SKIP_AUTH)) {
    return next(request);
  }

  const tokens = inject(TokenStore);
  const session = inject(SessionService);
  const router = inject(Router);

  return next(withBearer(request, tokens.accessToken())).pipe(
    catchError((error: unknown) => {
      if (!isUnauthorized(error) || request.context.get(AUTH_RETRIED)) {
        return throwError(() => error);
      }

      if (!tokens.refreshToken()) {
        session.expire(router.url);

        return throwError(() => error);
      }

      return session.refreshTokens().pipe(
        catchError((refreshError: unknown) => {
          if (isSessionRejected(refreshError)) session.expire(router.url);

          return throwError(() => refreshError);
        }),
        switchMap(() => {
          const retried = request.clone({ context: request.context.set(AUTH_RETRIED, true) });

          return next(withBearer(retried, tokens.accessToken()));
        }),
      );
    }),
  );
};

function withBearer<T>(request: HttpRequest<T>, accessToken: string | null): HttpRequest<T> {
  if (!accessToken) return request;

  return request.clone({ setHeaders: { Authorization: `Bearer ${accessToken}` } });
}

function isUnauthorized(error: unknown): boolean {
  return error instanceof HttpErrorResponse && error.status === 401;
}

function isSessionRejected(error: unknown): boolean {
  return error instanceof HttpErrorResponse && (error.status === 400 || error.status === 401);
}
