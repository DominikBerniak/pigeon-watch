import { ChangeDetectionStrategy, Component, input } from '@angular/core';

export type DetailItemValue = 'plain' | 'text' | 'numeric';

@Component({
  selector: 'div[app-detail-item]',
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './detail-item.html',
  styleUrl: './detail-item.scss',
  host: {
    '[class.detail-item--text]': "value() === 'text'",
    '[class.detail-item--numeric]': "value() === 'numeric'",
  },
})
export class DetailItem {
  readonly label = input.required<string>();
  readonly value = input<DetailItemValue>('plain');
}
