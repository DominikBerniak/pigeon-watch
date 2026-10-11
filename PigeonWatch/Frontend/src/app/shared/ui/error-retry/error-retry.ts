import { ChangeDetectionStrategy, Component, input, output } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { Alert } from '../alert/alert';

@Component({
  selector: 'app-error-retry',
  imports: [MatButtonModule, Alert],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './error-retry.html',
  styleUrl: './error-retry.scss',
})
export class ErrorRetry {
  readonly retryLabel = input.required<string>();
  readonly retry = output<void>();
}
