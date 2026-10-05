import { DOCUMENT } from '@angular/common';
import {
  ChangeDetectionStrategy,
  Component,
  computed,
  effect,
  inject,
  signal,
} from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { PageCard } from '../../../shared/ui/page-card/page-card';
import { ResourceService } from '../../resources/resource.service';
import { TranslatePipe } from '../../resources/translate.pipe';
import { WarmupState } from '../warmup-state';

export const warmupMessageKeyPrefix = 'warmup.messages.';
export const warmupMessageIntervalMs = 6_000;

@Component({
  selector: 'app-warmup-panel',
  imports: [MatButtonModule, PageCard, TranslatePipe],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './warmup-panel.html',
  styleUrl: './warmup-panel.scss',
})
export class WarmupPanel {
  private readonly warmup = inject(WarmupState);
  private readonly resources = inject(ResourceService);
  private readonly document = inject(DOCUMENT);
  private readonly messageIndex = signal(0);
  private readonly messageKeys = computed(() =>
    Object.keys(this.resources.labels())
      .filter((key) => key.startsWith(warmupMessageKeyPrefix))
      .sort((first, second) => first.localeCompare(second, undefined, { numeric: true })),
  );

  protected readonly status = this.warmup.status;
  protected readonly message = computed(() => {
    const keys = this.messageKeys();

    if (keys.length === 0) return null;

    return this.resources.translate(keys[this.messageIndex() % keys.length]);
  });

  constructor() {
    effect((onCleanup) => {
      if (this.status() !== 'warming') return;

      this.messageIndex.set(0);
      const handle = setInterval(
        () => this.messageIndex.update((index) => index + 1),
        warmupMessageIntervalMs,
      );
      onCleanup(() => clearInterval(handle));
    });
  }

  protected retry(): void {
    if (this.warmup.startedDuringStartup()) this.document.defaultView?.location.reload();
    else this.warmup.dismiss();
  }
}
