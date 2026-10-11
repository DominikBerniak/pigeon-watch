import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { RouterLink } from '@angular/router';
import { Subject, catchError, map, of, startWith, switchMap } from 'rxjs';
import { ConfigurationService } from '../../../core/configuration/configuration.service';
import { GeoPoint } from '../../../core/geolocation/geo-point';
import { GeolocationService } from '../../../core/geolocation/geolocation.service';
import { ResourceService } from '../../../core/resources/resource.service';
import { TranslatePipe } from '../../../core/resources/translate.pipe';
import { nearbyMapZoom, neutralMapView } from '../../../shared/map/map-config';
import { MapMarker } from '../../../shared/map/map-marker';
import { MapView } from '../../../shared/map/map-view/map-view';
import { Alert } from '../../../shared/ui';
import {
  Sighting,
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

@Component({
  selector: 'app-nearby-map-page',
  imports: [
    MatButtonModule,
    MatCardModule,
    MatProgressSpinnerModule,
    RouterLink,
    TranslatePipe,
    Alert,
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
  private readonly searches = new Subject<GeoPoint>();
  private readonly mapCentre = signal<GeoPoint>(neutralMapView.centre);
  private readonly selectedId = signal<string | null>(null);
  private lastSearchCentre: GeoPoint | null = null;

  protected readonly spinnerDiameter = 32;
  protected readonly viewCentre = signal<GeoPoint>(neutralMapView.centre);
  protected readonly viewZoom = signal<number>(neutralMapView.zoom);
  protected readonly locating = signal(true);
  protected readonly positionUnknown = signal(false);
  protected readonly state = signal<NearbyState>({ kind: 'idle' });
  protected readonly statusLabel = sightingStatusLabel;
  protected readonly criticalityLabel = sightingCriticalityLabel;
  protected readonly radiusKm = computed(
    () =>
      this.configuration.clientConfig()?.sightingRules?.nearbyRadiusKm ??
      fallbackSightingRules.nearbyRadiusKm,
  );
  protected readonly sightings = computed(() => {
    const state = this.state();

    return state.kind === 'loaded' ? state.items : [];
  });
  protected readonly markers = computed<readonly MapMarker[]>(() => {
    const unnamed = this.resources.translate('sighting.unnamed');

    return this.sightings().map((sighting) => ({
      id: sighting.id,
      latitude: sighting.latitude,
      longitude: sighting.longitude,
      kind: sightingCriticalityMarker(sighting.criticality),
      label: sighting.name ?? unnamed,
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
      .subscribe((state) => this.state.set(state));

    this.geolocation
      .currentPosition(false)
      .pipe(takeUntilDestroyed())
      .subscribe((outcome) => {
        this.locating.set(false);

        if (outcome.status !== 'ok') {
          this.positionUnknown.set(true);

          return;
        }

        const centre = { latitude: outcome.latitude, longitude: outcome.longitude };

        this.viewCentre.set(centre);
        this.viewZoom.set(nearbyMapZoom);
        this.mapCentre.set(centre);
        this.search(centre);
      });
  }

  protected trackCentre(point: GeoPoint): void {
    this.mapCentre.set(point);
  }

  protected searchThisArea(): void {
    this.search(this.mapCentre());
  }

  protected retry(): void {
    if (this.lastSearchCentre) this.search(this.lastSearchCentre);
  }

  protected select(id: string): void {
    this.selectedId.set(id);
  }

  protected clearSelection(): void {
    this.selectedId.set(null);
  }

  private search(centre: GeoPoint): void {
    this.lastSearchCentre = centre;
    this.selectedId.set(null);
    this.searches.next(centre);
  }
}
