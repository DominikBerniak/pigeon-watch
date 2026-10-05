import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';

export type AlertKind = 'error' | 'info' | 'success';

@Component({
  selector: 'app-alert',
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './alert.html',
  styleUrl: './alert.scss',
  host: {
    '[attr.role]': 'role()',
    '[class.alert--error]': "kind() === 'error'",
    '[class.alert--info]': "kind() === 'info'",
    '[class.alert--success]': "kind() === 'success'",
  },
})
export class Alert {
  readonly kind = input<AlertKind>('info');

  protected readonly role = computed(() => (this.kind() === 'error' ? 'alert' : 'status'));
}
