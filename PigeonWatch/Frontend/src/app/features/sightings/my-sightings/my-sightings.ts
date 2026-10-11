import { DatePipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { MatCardModule } from '@angular/material/card';
import { RouterLink } from '@angular/router';
import { Subject, catchError, map, of, startWith, switchMap } from 'rxjs';
import { TranslatePipe } from '../../../core/resources/translate.pipe';
import { CriticalityBadge, ErrorRetry, LoadingIndicator } from '../../../shared/ui';
import {
  Sighting,
  sightingCriticalityLabel,
  sightingCriticalityMarker,
  sightingStatusLabel,
} from '../sighting.types';
import { SightingsApi } from '../sightings-api';

type MySightingsState =
  | { readonly kind: 'loading' }
  | { readonly kind: 'error' }
  | { readonly kind: 'loaded'; readonly items: readonly Sighting[] };

@Component({
  selector: 'app-my-sightings',
  imports: [
    DatePipe,
    MatCardModule,
    RouterLink,
    TranslatePipe,
    CriticalityBadge,
    ErrorRetry,
    LoadingIndicator,
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './my-sightings.html',
  styleUrl: './my-sightings.scss',
})
export class MySightings {
  private readonly sightingsApi = inject(SightingsApi);
  private readonly reloads = new Subject<void>();

  protected readonly state = signal<MySightingsState>({ kind: 'loading' });
  protected readonly statusLabel = sightingStatusLabel;
  protected readonly criticalityLabel = sightingCriticalityLabel;
  protected readonly criticalityKind = sightingCriticalityMarker;

  constructor() {
    this.reloads
      .pipe(
        startWith(undefined),
        switchMap(() =>
          this.sightingsApi.mine().pipe(
            map((list): MySightingsState => ({ kind: 'loaded', items: list.items })),
            catchError(() => of<MySightingsState>({ kind: 'error' })),
            startWith<MySightingsState>({ kind: 'loading' }),
          ),
        ),
        takeUntilDestroyed(),
      )
      .subscribe((state) => this.state.set(state));
  }

  protected retry(): void {
    this.reloads.next();
  }
}
