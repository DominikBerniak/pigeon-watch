import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';

export type LoadingIndicatorLayout = 'block' | 'inline';

const blockDiameter = 32;
const inlineDiameter = 20;

@Component({
  selector: 'app-loading-indicator',
  imports: [MatProgressSpinnerModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './loading-indicator.html',
  styleUrl: './loading-indicator.scss',
  host: {
    '[attr.role]': "live() ? 'status' : null",
    '[class.loading-indicator--inline]': "layout() === 'inline'",
  },
})
export class LoadingIndicator {
  readonly label = input.required<string>();
  readonly layout = input<LoadingIndicatorLayout>('block');
  readonly live = input(true);

  protected readonly diameter = computed(() =>
    this.layout() === 'inline' ? inlineDiameter : blockDiameter,
  );
}
