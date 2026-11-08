import { ChangeDetectionStrategy, Component, computed, inject } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { MatButtonModule } from '@angular/material/button';
import { MatMenu, MatMenuItem, MatMenuTrigger } from '@angular/material/menu';
import {
  ActivatedRouteSnapshot,
  Data,
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
import { Icon } from './shared/ui/icon/icon';
import { Tooltip } from './shared/ui/tooltip/tooltip';

@Component({
  imports: [
    Brand,
    Icon,
    MatButtonModule,
    MatMenu,
    MatMenuItem,
    MatMenuTrigger,
    RouterLink,
    RouterOutlet,
    Tooltip,
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
  private readonly routeData = toSignal(
    this.router.events.pipe(
      filter((event) => event instanceof NavigationEnd),
      map(() => leafData(this.router.routerState.snapshot.root)),
    ),
    { initialValue: {} as Data },
  );
  protected readonly layout = computed(() => layoutOf(this.routeData()));
  protected readonly fill = computed(() => this.routeData()['fill'] === true);

  protected logout(): void {
    this.session.logout();
  }
}

type Layout = 'wide' | 'roomy';

function leafData(root: ActivatedRouteSnapshot): Data {
  let route = root;

  while (route.firstChild) route = route.firstChild;

  return route.data;
}

function layoutOf(data: Data): Layout | null {
  const layout = data['layout'];

  return layout === 'wide' || layout === 'roomy' ? layout : null;
}
