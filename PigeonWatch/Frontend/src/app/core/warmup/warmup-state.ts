import { Injectable, computed, signal } from '@angular/core';

export type WarmupStatus = 'idle' | 'warming' | 'failed';

export type WarmupOutcome = 'recovered' | 'failed' | 'cancelled';

@Injectable({ providedIn: 'root' })
export class WarmupState {
  private readonly warmingCountState = signal(0);
  private readonly failedState = signal(false);
  private readonly startedDuringStartupState = signal(false);
  private startupComplete = false;

  readonly warmingCount = this.warmingCountState.asReadonly();
  readonly startedDuringStartup = this.startedDuringStartupState.asReadonly();
  readonly status = computed<WarmupStatus>(() => {
    if (this.warmingCountState() > 0) return 'warming';

    return this.failedState() ? 'failed' : 'idle';
  });

  begin(): void {
    if (this.warmingCountState() === 0) this.startedDuringStartupState.set(!this.startupComplete);

    this.warmingCountState.update((count) => count + 1);
  }

  end(outcome: WarmupOutcome): void {
    this.warmingCountState.update((count) => Math.max(0, count - 1));

    if (outcome === 'failed') this.failedState.set(true);
    else if (outcome === 'recovered') this.failedState.set(false);
  }

  dismiss(): void {
    this.failedState.set(false);
  }

  markStartupComplete(): void {
    this.startupComplete = true;
  }
}
