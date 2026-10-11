import { DOCUMENT } from '@angular/common';
import { ChangeDetectionStrategy, Component, DestroyRef, inject } from '@angular/core';
import { cellGroups } from './cell-definitions';
import { StateCell } from './state-cell';

@Component({
  selector: 'app-kitchen-sink',
  imports: [StateCell],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './kitchen-sink.html',
  styleUrl: './kitchen-sink.scss',
})
export class KitchenSink {
  protected readonly groups = cellGroups;

  constructor() {
    const warmupPanel = inject(DOCUMENT).querySelector<HTMLElement>('app-warmup-panel');

    if (!warmupPanel) return;

    warmupPanel.hidden = true;
    inject(DestroyRef).onDestroy(() => (warmupPanel.hidden = false));
  }
}
