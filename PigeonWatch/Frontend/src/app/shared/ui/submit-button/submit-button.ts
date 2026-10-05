import { ChangeDetectionStrategy, Component, input } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';

@Component({
  selector: 'app-submit-button',
  imports: [MatButtonModule, MatProgressSpinnerModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './submit-button.html',
  styleUrl: './submit-button.scss',
})
export class SubmitButton {
  readonly busy = input(false);

  protected readonly spinnerDiameter = 18;
}
