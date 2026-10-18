import { DatePipe } from '@angular/common';
import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  Injector,
  afterNextRender,
  computed,
  effect,
  inject,
  signal,
  viewChild,
} from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { MatButtonModule } from '@angular/material/button';
import { Title } from '@angular/platform-browser';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { Subject, catchError, combineLatest, map, of, startWith, switchMap, tap } from 'rxjs';
import { GeoPoint } from '../../../core/geolocation/geo-point';
import { ResourceService } from '../../../core/resources/resource.service';
import { TranslatePipe } from '../../../core/resources/translate.pipe';
import { pickerMapZoom } from '../../../shared/map/map-config';
import { MapMarker } from '../../../shared/map/map-marker';
import { MapView } from '../../../shared/map/map-view/map-view';
import {
  Alert,
  CriticalityBadge,
  DetailItem,
  DetailList,
  ErrorRetry,
  LoadingIndicator,
  PageCard,
} from '../../../shared/ui';
import { sightingFailure } from '../sighting-errors';
import {
  Sighting,
  sightingCriticalityLabel,
  sightingCriticalityMarker,
  sightingStatusLabel,
} from '../sighting.types';
import { SightingsApi } from '../sightings-api';

type DetailState =
  | { readonly kind: 'loading' }
  | { readonly kind: 'notFound' }
  | { readonly kind: 'error' }
  | { readonly kind: 'loaded'; readonly sighting: Sighting };

const coordinateDecimals = 5;

@Component({
  selector: 'app-sighting-detail-page',
  imports: [
    DatePipe,
    MatButtonModule,
    RouterLink,
    TranslatePipe,
    Alert,
    CriticalityBadge,
    DetailItem,
    DetailList,
    ErrorRetry,
    LoadingIndicator,
    MapView,
    PageCard,
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './sighting-detail-page.html',
})
export class SightingDetailPage {
  private readonly route = inject(ActivatedRoute);
  private readonly sightingsApi = inject(SightingsApi);
  private readonly resources = inject(ResourceService);
  private readonly pageTitle = inject(Title);
  private readonly injector = inject(Injector);
  private readonly reloads = new Subject<void>();
  private readonly card = viewChild(PageCard);
  private focusOnSettle = inject(Router).currentNavigation() !== null;
  private loadsStarted = 0;

  protected readonly mapZoom = pickerMapZoom;
  protected readonly state = signal<DetailState>({ kind: 'loading' });
  protected readonly mapShown = signal(false);
  protected readonly statusLabel = sightingStatusLabel;
  protected readonly criticalityLabel = sightingCriticalityLabel;
  protected readonly criticalityKind = sightingCriticalityMarker;
  protected readonly sighting = computed(() => {
    const state = this.state();

    return state.kind === 'loaded' ? state.sighting : null;
  });
  protected readonly title = computed(() => {
    const sighting = this.sighting();

    if (!sighting) return this.resources.translate('sighting.detail.title');

    return sighting.name ?? this.resources.translate('sighting.unnamed');
  });
  protected readonly coordinatesText = computed(() => {
    const sighting = this.sighting();

    if (!sighting) return '';

    return this.resources.translate(
      'sighting.detail.coordinates',
      sighting.latitude.toFixed(coordinateDecimals),
      sighting.longitude.toFixed(coordinateDecimals),
    );
  });
  protected readonly position = computed<GeoPoint | null>(() => {
    const sighting = this.sighting();

    return sighting ? { latitude: sighting.latitude, longitude: sighting.longitude } : null;
  });
  protected readonly markers = computed<readonly MapMarker[]>(() => {
    const sighting = this.sighting();

    if (!sighting) return [];

    return [
      {
        id: sighting.id,
        latitude: sighting.latitude,
        longitude: sighting.longitude,
        kind: sightingCriticalityMarker(sighting.criticality),
        label: this.resources.translate(
          'map.markerLabel',
          this.title(),
          this.resources.translate(sightingCriticalityLabel(sighting.criticality)),
        ),
      },
    ];
  });

  constructor() {
    effect(() =>
      this.pageTitle.setTitle(
        this.resources.translate(
          'sighting.detail.pageTitle',
          this.title(),
          this.resources.translate('common.appName'),
        ),
      ),
    );

    inject(DestroyRef).onDestroy(() =>
      this.pageTitle.setTitle(this.resources.translate('common.appName')),
    );

    combineLatest([
      this.route.paramMap.pipe(map((params) => params.get('id') ?? '')),
      this.reloads.pipe(startWith(undefined)),
    ])
      .pipe(
        tap(() => this.mapShown.set(false)),
        switchMap(([id]) =>
          this.sightingsApi.get(id).pipe(
            map((sighting): DetailState => ({ kind: 'loaded', sighting })),
            catchError((error: unknown) => of(failureState(error))),
            startWith<DetailState>({ kind: 'loading' }),
          ),
        ),
        takeUntilDestroyed(),
      )
      .subscribe((state) => this.applyState(state));
  }

  protected retry(): void {
    this.reloads.next();
  }

  protected toggleMap(): void {
    this.mapShown.update((shown) => !shown);
  }

  private applyState(state: DetailState): void {
    this.state.set(state);

    if (state.kind === 'loading') {
      if (this.loadsStarted > 0) this.focusOnSettle = true;

      this.loadsStarted++;

      return;
    }

    if (!this.focusOnSettle) {
      this.focusOnSettle = true;

      return;
    }

    afterNextRender(() => this.card()?.focusHeading(), { injector: this.injector });
  }
}

function failureState(error: unknown): DetailState {
  return sightingFailure(error).kind === 'notFound' ? { kind: 'notFound' } : { kind: 'error' };
}
