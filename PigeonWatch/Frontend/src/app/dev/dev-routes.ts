import { Routes } from '@angular/router';

export const devRoutes: Routes = [
  {
    path: 'dev/kitchen-sink',
    loadComponent: () => import('./kitchen-sink/kitchen-sink').then((m) => m.KitchenSink),
  },
];
