import { inject } from '@angular/core';
import { CanActivateFn, RedirectFunction, Router, UrlTree } from '@angular/router';
import { map } from 'rxjs';
import { safeReturnUrl } from './safe-return-url';
import { SessionService } from './session.service';

export const authGuard: CanActivateFn = (_route, state) => {
  const session = inject(SessionService);
  const router = inject(Router);

  return session
    .whenReady()
    .pipe(map(() => session.isAuthenticated() || loginRedirect(router, state.url)));
};

export const guestGuard: CanActivateFn = (route) => {
  const session = inject(SessionService);
  const router = inject(Router);
  const returnUrl = route.queryParamMap.get('returnUrl');

  return session
    .whenReady()
    .pipe(map(() => !session.isAuthenticated() || router.parseUrl(safeReturnUrl(returnUrl))));
};

export const unknownRouteRedirect: RedirectFunction = ({ url, queryParams, fragment }) => {
  const session = inject(SessionService);
  const router = inject(Router);
  const navigation = router.currentNavigation();
  const requestedUrl = navigation
    ? router.serializeUrl(navigation.extractedUrl)
    : router.serializeUrl(
        router.createUrlTree(['/', ...url.map((segment) => segment.path)], {
          queryParams,
          fragment: fragment ?? undefined,
        }),
      );

  return session
    .whenReady()
    .pipe(map(() => (session.isAuthenticated() ? '' : loginRedirect(router, requestedUrl))));
};

function loginRedirect(router: Router, returnUrl: string): UrlTree {
  return router.createUrlTree(['/login'], { queryParams: { returnUrl } });
}
