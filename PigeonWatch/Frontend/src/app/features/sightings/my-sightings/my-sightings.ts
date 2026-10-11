import { DatePipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { RouterLink } from '@angular/router';
import { Subject, catchError, map, of, startWith, switchMap } from 'rxjs';
import { TranslatePipe } from '../../../core/resources/translate.pipe';
import { Alert } from '../../../shared/ui';
import { Sighting, sightingCriticalityLabels, sightingStatusLabels } from '../sighting.types';
import { SightingsApi } from '../sightings-api';

type MySightingsState =
  | { readonly kind: 'loading' }
  | { readonly kind: 'error' }
  | { readonly kind: 'loaded'; readonly items: readonly Sighting[] };

@Component({
  selector: 'app-my-sightings',
  imports: [
    DatePipe,
    MatButtonModule,
    MatCardModule,
    MatProgressSpinnerModule,
    RouterLink,
    TranslatePipe,
    Alert,
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './my-sightings.html',
  styleUrl: './my-sightings.scss',
})
export class MySightings {
  private readonly sightingsApi = inject(SightingsApi);
  private readonly reloads = new Subject<void>();

  protected readonly spinnerDiameter = 32;
  protected readonly state = signal<MySightingsState>({ kind: 'loading' });
  protected readonly statusLabels = sightingStatusLabels;
  protected readonly criticalityLabels = sightingCriticalityLabels;

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
