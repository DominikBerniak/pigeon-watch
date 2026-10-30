import { ChangeDetectionStrategy, Component, input } from '@angular/core';
import { TranslatePipe } from '../../../core/resources/translate.pipe';

@Component({
  selector: 'app-error-reference',
  imports: [TranslatePipe],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './error-reference.html',
})
export class ErrorReference {
  readonly traceId = input<string | null | undefined>(null);
}
