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
import { MatDialog } from '@angular/material/dialog';
import { Title } from '@angular/platform-browser';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import {
  Observable,
  Subject,
  catchError,
  combineLatest,
  map,
  of,
  startWith,
  switchMap,
  tap,
} from 'rxjs';
import { GeoPoint } from '../../../core/geolocation/geo-point';
import { ErrorReportingService } from '../../../core/error-reporting/error-reporting.service';
import { ResourceService } from '../../../core/resources/resource.service';
import { TranslatePipe } from '../../../core/resources/translate.pipe';
import { PhotoEncoder } from '../shared/photo-encoder';
import { pickerMapZoom } from '../../../shared/map/map-config';
import { MapMarker } from '../../../shared/map/map-marker';
import { MapView } from '../../../shared/map/map-view/map-view';
import {
  Alert,
  CriticalityBadge,
  DetailItem,
  DetailList,
  ErrorReference,
  ErrorRetry,
  LoadingIndicator,
  PageCard,
} from '../../../shared/ui';
import {
  isReportablePhotoFailure,
  isReportableSightingFailure,
  sightingFailure,
} from '../sighting-errors';
import {
  Sighting,
  SightingFilters,
  sightingCriticalityLabel,
  sightingCriticalityMarker,
  sightingStatusLabel,
} from '../sighting.types';
import {
  NearbyMapReturn,
  readNearbyMapFilters,
  readSightingDetailReturn,
} from '../sighting-list-page/sighting-list-hand-over';
import { SightingsApi } from '../sightings-api';
import { PhotoDialog, PhotoDialogData } from './photo-dialog/photo-dialog';
import { SightingComments } from './sighting-comments/sighting-comments';

type DetailState =
  | { readonly kind: 'loading' }
  | { readonly kind: 'notFound' }
  | { readonly kind: 'error'; readonly traceId?: string }
  | { readonly kind: 'loaded'; readonly sighting: Sighting };

type PhotoState =
  | { readonly kind: 'none' }
  | { readonly kind: 'loading' }
  | { readonly kind: 'error' }
  | { readonly kind: 'loaded'; readonly url: string };

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
    ErrorReference,
    ErrorRetry,
    LoadingIndicator,
    MapView,
    PageCard,
    SightingComments,
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './sighting-detail-page.html',
  styleUrl: './sighting-detail-page.scss',
})
export class SightingDetailPage {
  private readonly route = inject(ActivatedRoute);
  private readonly sightingsApi = inject(SightingsApi);
  private readonly resources = inject(ResourceService);
  private readonly reporting = inject(ErrorReportingService);
  private readonly photoUrls = inject(PhotoEncoder);
  private readonly dialog = inject(MatDialog);
  private readonly pageTitle = inject(Title);
  private readonly injector = inject(Injector);
  private readonly router = inject(Router);
  private readonly destroyRef = inject(DestroyRef);
  private readonly reloads = new Subject<void>();
  private readonly shownSightings = new Subject<Sighting | null>();
  private readonly card = viewChild(PageCard);
  private focusOnSettle = this.router.currentNavigation() !== null;

  protected readonly returnToList = readSightingDetailReturn(
    this.router.currentNavigation()?.extras.state,
  );
  protected readonly returnToMap = mapReturnOf(
    readNearbyMapFilters(this.router.currentNavigation()?.extras.state),
  );
  protected readonly mapZoom = pickerMapZoom;
  protected readonly state = signal<DetailState>({ kind: 'loading' });
  protected readonly mapShown = signal(false);
  protected readonly photo = signal<PhotoState>({ kind: 'none' });
  protected readonly photoUrl = computed(() => {
    const photo = this.photo();

    return photo.kind === 'loaded' ? photo.url : null;
  });
  protected readonly statusLabel = sightingStatusLabel;
  protected readonly criticalityLabel = sightingCriticalityLabel;
  protected readonly criticalityKind = sightingCriticalityMarker;
  protected readonly sighting = computed(() => {
    const state = this.state();

    return state.kind === 'loaded' ? state.sighting : null;
  });
  protected readonly errorTraceId = computed(() => {
    const state = this.state();

    return state.kind === 'error' ? (state.traceId ?? null) : null;
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

    this.destroyRef.onDestroy(() =>
      this.pageTitle.setTitle(this.resources.translate('common.appName')),
    );

    this.shownSightings
      .pipe(
        switchMap((sighting) => this.photoOf(sighting)),
        takeUntilDestroyed(),
      )
      .subscribe((photo) => this.photo.set(photo));

    combineLatest([
      this.route.paramMap.pipe(map((params) => params.get('id') ?? '')),
      this.reloads.pipe(startWith(undefined)),
    ])
      .pipe(
        tap(() => this.mapShown.set(false)),
        switchMap(([id]) =>
          this.sightingsApi.get(id).pipe(
            map((sighting): DetailState => ({ kind: 'loaded', sighting })),
            catchError((error: unknown) => of(this.failureState(error))),
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

  protected openPhoto(url: string): void {
    const title = this.title();

    this.dialog.open<PhotoDialog, PhotoDialogData>(PhotoDialog, {
      autoFocus: 'dialog',
      width: 'var(--pw-photo-dialog-max-width)',
      maxWidth: 'var(--pw-photo-dialog-max-width)',
      data: { url, title, alt: this.resources.translate('sighting.detail.photoAlt', title) },
    });
  }

  private failureState(error: unknown): DetailState {
    const failure = sightingFailure(error);

    if (failure.kind === 'notFound') return { kind: 'notFound' };

    if (isReportableSightingFailure(error, failure)) {
      this.reporting.report('api-failure', 'sighting.detail', error);
    }

    return failure.kind === 'generic' && failure.traceId !== undefined
      ? { kind: 'error', traceId: failure.traceId }
      : { kind: 'error' };
  }

  private photoOf(sighting: Sighting | null): Observable<PhotoState> {
    if (!sighting?.hasPhoto) return of<PhotoState>({ kind: 'none' });

    return this.sightingsApi.photo(sighting.id).pipe(
      switchMap((blob) => this.objectUrlOf(blob)),
      map((url): PhotoState => ({ kind: 'loaded', url })),
      catchError((error: unknown) => {
        if (isReportablePhotoFailure(error)) {
          this.reporting.report('api-failure', 'sighting.detail.photo', error);
        }

        return of<PhotoState>({ kind: 'error' });
      }),
      startWith<PhotoState>({ kind: 'loading' }),
    );
  }

  private objectUrlOf(blob: Blob): Observable<string> {
    return new Observable<string>((subscriber) => {
      const url = this.photoUrls.createPreviewUrl(blob);

      subscriber.next(url);

      return () => this.photoUrls.revokePreviewUrl(url);
    });
  }

  private applyState(state: DetailState): void {
    const previous = this.state();

    this.state.set(state);
    this.shownSightings.next(state.kind === 'loaded' ? state.sighting : null);

    if (state.kind === 'loading') {
      if (previous.kind !== 'loading') this.focusOnSettle = true;

      return;
    }

    if (!this.focusOnSettle) {
      this.focusOnSettle = true;

      return;
    }

    afterNextRender(() => this.card()?.focusHeading(), { injector: this.injector });
  }
}

function mapReturnOf(filters: SightingFilters | null): NearbyMapReturn | undefined {
  return filters ? { mapFilters: filters } : undefined;
}
