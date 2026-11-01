import { HttpErrorResponse } from '@angular/common/http';
import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatPaginatorIntl, MatPaginatorModule, PageEvent } from '@angular/material/paginator';
import { MatSelectModule } from '@angular/material/select';
import { Router, RouterLink } from '@angular/router';
import { Observable, Subject, catchError, of, startWith, switchMap } from 'rxjs';
import { ConfigurationService } from '../../../core/configuration/configuration.service';
import { ErrorReportingService } from '../../../core/error-reporting/error-reporting.service';
import { GeoPoint } from '../../../core/geolocation/geo-point';
import {
  GeolocationOutcome,
  GeolocationService,
} from '../../../core/geolocation/geolocation.service';
import { TranslatePipe } from '../../../core/resources/translate.pipe';
import {
  isExpectedClientStatus,
  problemTraceId,
  unexpectedTraceId,
} from '../../../shared/http/problem-details';
import { Alert, ErrorReference, ErrorRetry, LoadingIndicator, PageCard } from '../../../shared/ui';
import { SightingFilterRow } from '../shared/sighting-filters/sighting-filters';
import { SightingRow } from '../shared/sighting-row/sighting-row';
import {
  Sighting,
  SightingFilters,
  SightingPage,
  allSightingStatuses,
  defaultSightingListPageSize,
  fallbackSightingRules,
  sightingListPageSizes,
} from '../sighting.types';
import { SightingsApi } from '../sightings-api';
import {
  NearbyMapReturn,
  SightingDetailReturn,
  readSightingListHandOver,
} from './sighting-list-hand-over';
import { SightingListPaginatorIntl } from './sighting-list-paginator-intl';

type SightingListState =
  | { readonly kind: 'locating' }
  | { readonly kind: 'locationUnavailable' }
  | { readonly kind: 'loading' }
  | { readonly kind: 'error'; readonly traceId?: string }
  | {
      readonly kind: 'loaded';
      readonly items: readonly Sighting[];
      readonly filtered: boolean;
      readonly loadedAt: Date;
    };

interface SightingListQuery {
  readonly centre: GeoPoint;
  readonly filters: SightingFilters;
  readonly page: number;
  readonly pageSize: number;
}

const noFilters: SightingFilters = { statuses: [], criticalities: [] };

@Component({
  selector: 'app-sighting-list-page',
  imports: [
    MatButtonModule,
    MatFormFieldModule,
    MatPaginatorModule,
    MatSelectModule,
    RouterLink,
    TranslatePipe,
    Alert,
    ErrorReference,
    ErrorRetry,
    LoadingIndicator,
    PageCard,
    SightingFilterRow,
    SightingRow,
  ],
  providers: [{ provide: MatPaginatorIntl, useClass: SightingListPaginatorIntl }],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './sighting-list-page.html',
  styleUrl: './sighting-list-page.scss',
})
export class SightingListPage {
  private readonly sightingsApi = inject(SightingsApi);
  private readonly geolocation = inject(GeolocationService);
  private readonly configuration = inject(ConfigurationService);
  private readonly reporting = inject(ErrorReportingService);
  private readonly handOver = readSightingListHandOver(
    inject(Router).currentNavigation()?.extras.state,
  );
  private readonly queries = new Subject<SightingListQuery>();
  private readonly centre = signal<GeoPoint | null>(this.handOver?.centre ?? null);
  private readonly filters = signal<SightingFilters>(this.handOver?.filters ?? noFilters);

  protected readonly mapReturn = computed<NearbyMapReturn>(() => ({
    mapFilters: this.filters(),
  }));
  protected readonly detailReturn = computed<SightingDetailReturn | undefined>(() => {
    const centre = this.centre();

    return centre ? { returnToList: { centre, filters: this.filters() } } : undefined;
  });
  protected readonly initialFilters = this.filters();
  protected readonly statusOptions = allSightingStatuses;
  protected readonly pageSizes = [...sightingListPageSizes];
  protected readonly page = signal(1);
  protected readonly pageSize = signal(defaultSightingListPageSize);
  protected readonly totalCount = signal<number | null>(null);
  protected readonly filtersOpen = signal(false);
  protected readonly activeFilterCount = computed(
    () => this.filters().statuses.length + this.filters().criticalities.length,
  );
  protected readonly state = signal<SightingListState>(
    this.handOver ? { kind: 'loading' } : { kind: 'locating' },
  );
  protected readonly radiusKm = computed(
    () =>
      this.configuration.clientConfig()?.sightingRules?.nearbyRadiusKm ??
      fallbackSightingRules.nearbyRadiusKm,
  );
  protected readonly errorTraceId = computed(() => {
    const state = this.state();

    return state.kind === 'error' ? (state.traceId ?? null) : null;
  });
  protected readonly pageCount = computed(() =>
    Math.max(1, Math.ceil((this.totalCount() ?? 0) / this.pageSize())),
  );
  protected readonly paginatorShown = computed(() => {
    const kind = this.state().kind;

    return (this.totalCount() ?? 0) > 0 && (kind === 'loaded' || kind === 'loading');
  });

  constructor() {
    this.queries
      .pipe(
        switchMap((query) =>
          this.fetch(query, false).pipe(
            catchError((error: unknown) => of(this.failureState(error))),
            startWith<SightingListState>({ kind: 'loading' }),
          ),
        ),
        takeUntilDestroyed(),
      )
      .subscribe((state) => this.state.set(state));

    if (this.handOver) {
      this.request();

      return;
    }

    this.geolocation
      .currentPosition(false)
      .pipe(takeUntilDestroyed())
      .subscribe((outcome) => this.applyLocation(outcome));
  }

  protected toggleFilters(): void {
    this.filtersOpen.update((open) => !open);
  }

  protected changeFilters(filters: SightingFilters): void {
    this.filters.set(filters);
    this.page.set(1);
    this.request();
  }

  protected changePage(event: PageEvent): void {
    this.page.set(event.pageIndex + 1);
    this.request();
  }

  protected changePageSize(pageSize: number): void {
    this.pageSize.set(pageSize);
    this.page.set(1);
    this.request();
  }

  protected retry(): void {
    this.request();
  }

  private applyLocation(outcome: GeolocationOutcome): void {
    if (outcome.status !== 'ok') {
      this.state.set({ kind: 'locationUnavailable' });

      return;
    }

    this.centre.set({ latitude: outcome.latitude, longitude: outcome.longitude });
    this.request();
  }

  private request(): void {
    const centre = this.centre();

    if (!centre) return;

    this.queries.next({
      centre,
      filters: this.filters(),
      page: this.page(),
      pageSize: this.pageSize(),
    });
  }

  private fetch(query: SightingListQuery, recovering: boolean): Observable<SightingListState> {
    return this.sightingsApi
      .list({
        latitude: query.centre.latitude,
        longitude: query.centre.longitude,
        statuses: query.filters.statuses,
        criticalities: query.filters.criticalities,
        page: query.page,
        pageSize: query.pageSize,
      })
      .pipe(
        switchMap((result) => {
          if (isPastEnd(query, result)) {
            const page = recovering ? 1 : Math.ceil(result.totalCount / query.pageSize);

            this.page.set(page);

            return this.fetch({ ...query, page }, true);
          }

          this.totalCount.set(result.totalCount);

          return of<SightingListState>({
            kind: 'loaded',
            items: result.items,
            filtered: query.filters.statuses.length > 0 || query.filters.criticalities.length > 0,
            loadedAt: new Date(),
          });
        }),
      );
  }

  private failureState(error: unknown): SightingListState {
    const badRequest = error instanceof HttpErrorResponse && error.status === 400;

    if (badRequest || !isExpectedClientStatus(error)) {
      this.reporting.report('api-failure', 'sightings.list', error);
    }

    const traceId = badRequest ? (problemTraceId(error) ?? undefined) : unexpectedTraceId(error);

    return traceId === undefined ? { kind: 'error' } : { kind: 'error', traceId };
  }
}

function isPastEnd(query: SightingListQuery, result: SightingPage): boolean {
  return result.items.length === 0 && result.totalCount > 0 && query.page > 1;
}
