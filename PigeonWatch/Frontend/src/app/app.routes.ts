import { Routes } from '@angular/router';
import { authGuard, guestGuard, unknownRouteRedirect } from './core/auth/auth.guards';
import { devRoutes } from './dev/dev-routes';

export const routes: Routes = [
  {
    path: '',
    pathMatch: 'full',
    canActivate: [authGuard],
    loadComponent: () =>
      import('./features/sightings/nearby-map-page/nearby-map-page').then((m) => m.NearbyMapPage),
  },
  {
    path: 'login',
    canActivate: [guestGuard],
    loadComponent: () => import('./features/auth/login-page/login-page').then((m) => m.LoginPage),
  },
  {
    path: 'register',
    canActivate: [guestGuard],
    loadComponent: () =>
      import('./features/auth/register-page/register-page').then((m) => m.RegisterPage),
  },
  {
    path: 'profile',
    canActivate: [authGuard],
    loadComponent: () =>
      import('./features/profile/profile-page/profile-page').then((m) => m.ProfilePage),
  },
  {
    path: 'sightings/new',
    canActivate: [authGuard],
    loadComponent: () =>
      import('./features/sightings/report-sighting-page/report-sighting-page').then(
        (m) => m.ReportSightingPage,
      ),
  },
  {
    path: 'sightings/:id',
    canActivate: [authGuard],
    loadComponent: () =>
      import('./features/sightings/sighting-detail-page/sighting-detail-page').then(
        (m) => m.SightingDetailPage,
      ),
  },
  ...devRoutes,
  {
    path: '**',
    redirectTo: unknownRouteRedirect,
  },
];
