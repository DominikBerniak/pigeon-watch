import { ChangeDetectionStrategy, Component, input } from '@angular/core';

export type DetailListLayout = 'column' | 'inline';

@Component({
  selector: 'dl[app-detail-list]',
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './detail-list.html',
  styleUrl: './detail-list.scss',
  host: {
    '[class.detail-list--inline]': "layout() === 'inline'",
  },
})
export class DetailList {
  readonly layout = input<DetailListLayout>('column');
}
