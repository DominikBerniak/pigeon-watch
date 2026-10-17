import {
  ChangeDetectionStrategy,
  Component,
  ElementRef,
  Injector,
  afterNextRender,
  computed,
  inject,
  signal,
  viewChild,
} from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { RouterLink } from '@angular/router';
import { Subject, catchError, map, of, startWith, switchMap } from 'rxjs';
import { ConfigurationService } from '../../../core/configuration/configuration.service';
import { GeoPoint } from '../../../core/geolocation/geo-point';
import {
  GeolocationOutcome,
  GeolocationService,
} from '../../../core/geolocation/geolocation.service';
import { ResourceService } from '../../../core/resources/resource.service';
import { TranslatePipe } from '../../../core/resources/translate.pipe';
import { minSearchZoom, nearbyMapZoom, neutralMapView } from '../../../shared/map/map-config';
import { MapMarker } from '../../../shared/map/map-marker';
import { MapView } from '../../../shared/map/map-view/map-view';
import {
  Alert,
  CriticalityBadge,
  DetailItem,
  DetailList,
  LoadingIndicator,
} from '../../../shared/ui';
import {
  Sighting,
  SightingCriticality,
  fallbackSightingRules,
  sightingCriticalityLabel,
  sightingCriticalityMarker,
  sightingStatusLabel,
} from '../sighting.types';
import { SightingsApi } from '../sightings-api';

type NearbyState =
  | { readonly kind: 'idle' }
  | { readonly kind: 'loading' }
  | { readonly kind: 'error' }
  | { readonly kind: 'loaded'; readonly items: readonly Sighting[] };

type NearbyStatus =
  'none' | 'locating' | 'zoomToSearch' | 'hint' | 'loading' | 'found' | 'empty' | 'error';

type LocationStatus = GeolocationOutcome['status'];

const retryableLocationStatuses: ReadonlySet<LocationStatus> = new Set([
  'denied',
  'unavailable',
  'timeout',
]);

const legendCriticalities: readonly SightingCriticality[] = [
  SightingCriticality.monitor,
  SightingCriticality.needsHelpSoon,
  SightingCriticality.urgent,
];

let nextStatusId = 0;

@Component({
  selector: 'app-nearby-map-page',
  imports: [
    MatButtonModule,
    MatCardModule,
    RouterLink,
    TranslatePipe,
    CriticalityBadge,
    DetailItem,
    DetailList,
    Alert,
    LoadingIndicator,
    MapView,
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './nearby-map-page.html',
  styleUrl: './nearby-map-page.scss',
})
export class NearbyMapPage {
  private readonly sightingsApi = inject(SightingsApi);
  private readonly geolocation = inject(GeolocationService);
  private readonly configuration = inject(ConfigurationService);
  private readonly resources = inject(ResourceService);
  private readonly injector = inject(Injector);
  private readonly searches = new Subject<GeoPoint>();
  private readonly locationRequests = new Subject<void>();
  private readonly mapCentre = signal<GeoPoint>(neutralMapView.centre);
  private readonly mapZoom = signal<number>(neutralMapView.zoom);
  private readonly awaitingSearch = signal(false);
  private readonly locationStatus = signal<LocationStatus | null>(null);
  private readonly mapView = viewChild<MapView>('mapView');
  private readonly selectedHeading = viewChild<ElementRef<HTMLElement>>('selectedHeading');
  private readonly searchButton = viewChild('searchButton', {
    read: ElementRef<HTMLButtonElement>,
  });
  private lastSearchCentre: GeoPoint | null = null;

  protected readonly statusId = `nearby-status-${nextStatusId++}`;
  protected readonly selectedId = signal<string | null>(null);
  protected readonly viewCentre = signal<GeoPoint>(neutralMapView.centre);
  protected readonly viewZoom = signal<number>(neutralMapView.zoom);
  protected readonly locating = signal(true);
  protected readonly mapFailed = signal(false);
  protected readonly positionUnknown = signal(false);
  protected readonly state = signal<NearbyState>({ kind: 'idle' });
  protected readonly sightings = signal<readonly Sighting[]>([]);
  protected readonly statusLabel = sightingStatusLabel;
  protected readonly criticalityLabel = sightingCriticalityLabel;
  protected readonly criticalityKind = sightingCriticalityMarker;
  protected readonly legend = legendCriticalities;
  protected readonly radiusKm = computed(
    () =>
      this.configuration.clientConfig()?.sightingRules?.nearbyRadiusKm ??
      fallbackSightingRules.nearbyRadiusKm,
  );
  protected readonly title = computed(() =>
    this.positionUnknown() ? 'map.titleArea' : 'map.title',
  );
  protected readonly zoomedOut = computed(() => this.mapZoom() < minSearchZoom);
  protected readonly searchDisabled = computed(
    () =>
      this.zoomedOut() ||
      this.locating() ||
      this.mapFailed() ||
      this.state().kind === 'loading',
  );
  protected readonly canRetryLocation = computed(() => {
    const status = this.locationStatus();

    return status !== null && retryableLocationStatuses.has(status);
  });
  protected readonly status = computed<NearbyStatus>(() => {
    if (this.locating()) return 'locating';

    if (this.mapFailed()) {
      const kind = this.state().kind;

      return kind === 'loading' || kind === 'error' ? kind : 'none';
    }

    if (this.zoomedOut()) return 'zoomToSearch';

    if (this.positionUnknown() && this.awaitingSearch()) return 'hint';

    const state = this.state();

    if (state.kind === 'loaded') return state.items.length > 0 ? 'found' : 'empty';

    return state.kind === 'idle' ? 'none' : state.kind;
  });
  protected readonly markers = computed<readonly MapMarker[]>(() => {
    const unnamed = this.resources.translate('sighting.unnamed');

    return this.sightings().map((sighting) => ({
      id: sighting.id,
      latitude: sighting.latitude,
      longitude: sighting.longitude,
      kind: sightingCriticalityMarker(sighting.criticality),
      label: this.resources.translate(
        'map.markerLabel',
        sighting.name ?? unnamed,
        this.resources.translate(sightingCriticalityLabel(sighting.criticality)),
      ),
    }));
  });
  protected readonly selected = computed(() => {
    const id = this.selectedId();

    return this.sightings().find((sighting) => sighting.id === id) ?? null;
  });

  constructor() {
    this.searches
      .pipe(
        switchMap((centre) =>
          this.sightingsApi.nearby(centre).pipe(
            map((list): NearbyState => ({ kind: 'loaded', items: list.items })),
            catchError(() => of<NearbyState>({ kind: 'error' })),
            startWith<NearbyState>({ kind: 'loading' }),
          ),
        ),
        takeUntilDestroyed(),
      )
      .subscribe((state) => this.applyState(state));

    this.locationRequests
      .pipe(
        startWith(undefined),
        switchMap(() => this.geolocation.currentPosition(false)),
        takeUntilDestroyed(),
      )
      .subscribe((outcome) => this.applyLocation(outcome));
  }

  protected trackCentre(point: GeoPoint): void {
    this.mapCentre.set(point);
  }

  protected trackZoom(zoom: number): void {
    this.mapZoom.set(zoom);
  }

  protected trackLoadFailed(failed: boolean): void {
    this.mapFailed.set(failed);
  }

  protected searchThisArea(): void {
    if (this.searchDisabled()) return;

    this.search(this.mapCentre());
  }

  protected useMyLocation(): void {
    if (this.locating()) return;

    this.locating.set(true);
    this.locationRequests.next();
  }

  protected retry(): void {
    if (!this.lastSearchCentre) return;

    this.search(this.lastSearchCentre);
    this.focusSearch();
  }

  protected select(id: string): void {
    this.selectedId.set(id);
    afterNextRender(() => this.selectedHeading()?.nativeElement.focus(), {
      injector: this.injector,
    });
  }

  protected clearSelection(): void {
    const id = this.selectedId();

    this.selectedId.set(null);

    if (id) this.mapView()?.focusMarker(id);
  }

  private applyState(state: NearbyState): void {
    this.state.set(state);

    if (state.kind === 'loaded') this.sightings.set(state.items);

    if (state.kind === 'error') this.sightings.set([]);
  }

  private applyLocation(outcome: GeolocationOutcome): void {
    const retried = this.canRetryLocation();

    this.locating.set(false);
    this.locationStatus.set(outcome.status);

    if (outcome.status !== 'ok') {
      this.positionUnknown.set(true);
      this.awaitingSearch.set(this.state().kind === 'idle');

      return;
    }

    const centre = { latitude: outcome.latitude, longitude: outcome.longitude };

    this.positionUnknown.set(false);
    this.viewCentre.set(centre);
    this.viewZoom.set(nearbyMapZoom);
    this.mapCentre.set(centre);
    this.mapZoom.set(nearbyMapZoom);
    this.search(centre);

    if (retried) this.focusSearch();
  }

  private focusSearch(): void {
    afterNextRender(() => this.searchButton()?.nativeElement.focus(), {
      injector: this.injector,
    });
  }

  private search(centre: GeoPoint): void {
    this.lastSearchCentre = centre;
    this.awaitingSearch.set(false);
    this.selectedId.set(null);
    this.searches.next(centre);
  }
}
