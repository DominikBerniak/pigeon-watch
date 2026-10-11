import { ChangeDetectionStrategy, Component, input } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';

export type SubmitButtonTone = 'primary' | 'success';

@Component({
  selector: 'app-submit-button',
  imports: [MatButtonModule, MatProgressSpinnerModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './submit-button.html',
  styleUrl: './submit-button.scss',
  host: {
    '[attr.data-tone]': 'tone()',
  },
})
export class SubmitButton {
  readonly tone = input<SubmitButtonTone>('primary');
  readonly busy = input(false);
  readonly disabled = input(false);

  protected readonly spinnerDiameter = 18;
}
