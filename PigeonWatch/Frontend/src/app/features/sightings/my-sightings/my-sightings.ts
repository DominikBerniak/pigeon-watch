import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { MatCardModule } from '@angular/material/card';
import { Subject, catchError, map, of, startWith, switchMap } from 'rxjs';
import { ErrorReportingService } from '../../../core/error-reporting/error-reporting.service';
import { TranslatePipe } from '../../../core/resources/translate.pipe';
import { isExpectedClientStatus, unexpectedTraceId } from '../../../shared/http/problem-details';
import { ErrorReference, ErrorRetry, LoadingIndicator } from '../../../shared/ui';
import { SightingRow } from '../shared/sighting-row/sighting-row';
import { Sighting } from '../sighting.types';
import { SightingsApi } from '../sightings-api';

type MySightingsState =
  | { readonly kind: 'loading' }
  | { readonly kind: 'error'; readonly traceId?: string }
  | { readonly kind: 'loaded'; readonly items: readonly Sighting[]; readonly loadedAt: Date };

@Component({
  selector: 'app-my-sightings',
  imports: [
    MatCardModule,
    TranslatePipe,
    ErrorReference,
    ErrorRetry,
    LoadingIndicator,
    SightingRow,
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './my-sightings.html',
  styleUrl: './my-sightings.scss',
})
export class MySightings {
  private readonly sightingsApi = inject(SightingsApi);
  private readonly reporting = inject(ErrorReportingService);
  private readonly reloads = new Subject<void>();

  protected readonly state = signal<MySightingsState>({ kind: 'loading' });
  protected readonly errorTraceId = computed(() => {
    const state = this.state();

    return state.kind === 'error' ? (state.traceId ?? null) : null;
  });

  constructor() {
    this.reloads
      .pipe(
        startWith(undefined),
        switchMap(() =>
          this.sightingsApi.mine().pipe(
            map((list): MySightingsState => ({
              kind: 'loaded',
              items: list.items,
              loadedAt: new Date(),
            })),
            catchError((error: unknown) => of(this.failureState(error))),
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

  private failureState(error: unknown): MySightingsState {
    if (!isExpectedClientStatus(error)) {
      this.reporting.report('api-failure', 'sightings.mine', error);
    }

    const traceId = unexpectedTraceId(error);

    return traceId === undefined ? { kind: 'error' } : { kind: 'error', traceId };
  }
}
