import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { MatButtonModule } from '@angular/material/button';
import { MatMenu, MatMenuItem, MatMenuTrigger } from '@angular/material/menu';
import {
  ActivatedRouteSnapshot,
  NavigationEnd,
  Router,
  RouterLink,
  RouterOutlet,
} from '@angular/router';
import { filter, map } from 'rxjs';
import { SessionService } from './core/auth/session.service';
import { TranslatePipe } from './core/resources/translate.pipe';
import { WarmupPanel } from './core/warmup/warmup-panel/warmup-panel';
import { Brand } from './shared/ui/brand/brand';

@Component({
  imports: [
    Brand,
    MatButtonModule,
    MatMenu,
    MatMenuItem,
    MatMenuTrigger,
    RouterLink,
    RouterOutlet,
    TranslatePipe,
    WarmupPanel,
  ],
  selector: 'app-root',
  changeDetection: ChangeDetectionStrategy.OnPush,
  styleUrl: './app.scss',
  templateUrl: './app.html',
})
export class App {
  private readonly session = inject(SessionService);
  private readonly router = inject(Router);

  protected readonly currentUser = this.session.currentUser;
  protected readonly layout = toSignal(
    this.router.events.pipe(
      filter((event) => event instanceof NavigationEnd),
      map(() => layoutOf(this.router.routerState.snapshot.root)),
    ),
    { initialValue: null },
  );

  protected logout(): void {
    this.session.logout();
  }
}

type Layout = 'wide' | 'roomy';

function layoutOf(root: ActivatedRouteSnapshot): Layout | null {
  let route = root;

  while (route.firstChild) route = route.firstChild;

  const layout = route.data['layout'];

  return layout === 'wide' || layout === 'roomy' ? layout : null;
}
