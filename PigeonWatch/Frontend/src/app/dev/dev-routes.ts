import { Routes } from '@angular/router';

export const devRoutes: Routes = [
  {
    path: 'dev/kitchen-sink',
    data: { layout: 'wide' },
    loadComponent: () => import('./kitchen-sink/kitchen-sink').then((m) => m.KitchenSink),
  },
];
