import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { RouterOutlet } from '@angular/router';
import { SessionService } from './core/auth/session.service';
import { TranslatePipe } from './core/resources/translate.pipe';
import { WarmupPanel } from './core/warmup/warmup-panel/warmup-panel';
import { Brand } from './shared/ui/brand/brand';

@Component({
  imports: [Brand, MatButtonModule, RouterOutlet, TranslatePipe, WarmupPanel],
  selector: 'app-root',
  changeDetection: ChangeDetectionStrategy.OnPush,
  styleUrl: './app.scss',
  templateUrl: './app.html',
})
export class App {
  private readonly session = inject(SessionService);

  protected readonly currentUser = this.session.currentUser;

  protected logout(): void {
    this.session.logout();
  }
}
