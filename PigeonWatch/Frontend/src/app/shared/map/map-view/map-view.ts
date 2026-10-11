import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  ElementRef,
  afterNextRender,
  computed,
  effect,
  inject,
  input,
  output,
  signal,
  untracked,
  viewChild,
} from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import type * as Leaflet from 'leaflet';
import { EMPTY, catchError, from } from 'rxjs';
import { GeoPoint } from '../../../core/geolocation/geo-point';
import { ResourceService } from '../../../core/resources/resource.service';
import { attributionHtml, mapTiles } from '../map-config';
import { MapMarker, mapMarkerClassNames, mapPinClassName } from '../map-marker';

type LeafletModule = typeof Leaflet;

@Component({
  selector: 'app-map-view',
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './map-view.html',
  styleUrl: './map-view.scss',
})
export class MapView {
  private readonly resources = inject(ResourceService);
  private readonly destroyRef = inject(DestroyRef);
  private readonly canvas = viewChild.required<ElementRef<HTMLElement>>('canvas');
  private readonly attribution = computed(() =>
    attributionHtml(this.resources.translate('map.attribution')),
  );
  private readonly ready = signal(false);
  private leaflet: LeafletModule | null = null;
  private map: Leaflet.Map | null = null;
  private markerLayer: Leaflet.LayerGroup | null = null;
  private pinMarker: Leaflet.Marker | null = null;
  private shownAttribution: string | null = null;

  readonly label = input.required<string>();
  readonly center = input.required<GeoPoint>();
  readonly zoom = input.required<number>();
  readonly markers = input<readonly MapMarker[]>([]);
  readonly pin = input<GeoPoint | null>(null);
  readonly mapClick = output<GeoPoint>();
  readonly centerChange = output<GeoPoint>();
  readonly markerSelect = output<string>();
  readonly pinMove = output<GeoPoint>();

  constructor() {
    afterNextRender(() => this.load());
    effect(() => this.applyView());
    effect(() => this.applyMarkers());
    effect(() => this.applyPin());
    effect(() => this.applyAttribution());
    this.destroyRef.onDestroy(() => this.map?.remove());
  }

  private load(): void {
    from(import('leaflet'))
      .pipe(
        catchError(() => EMPTY),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe((module) => this.create(leafletOf(module)));
  }

  private create(leaflet: LeafletModule): void {
    const centre = untracked(this.center);
    const map = leaflet.map(this.canvas().nativeElement, {
      center: [centre.latitude, centre.longitude],
      zoom: untracked(this.zoom),
      minZoom: mapTiles.minZoom,
      maxZoom: mapTiles.maxZoom,
    });

    map.attributionControl.setPrefix(false);
    leaflet
      .tileLayer(mapTiles.urlTemplate, { minZoom: mapTiles.minZoom, maxZoom: mapTiles.maxZoom })
      .addTo(map);
    map.on('click', (event: Leaflet.LeafletMouseEvent) =>
      this.mapClick.emit(pointOf(event.latlng)),
    );
    map.on('moveend', () => this.centerChange.emit(pointOf(map.getCenter())));

    this.leaflet = leaflet;
    this.map = map;
    this.markerLayer = leaflet.layerGroup().addTo(map);
    this.ready.set(true);
  }

  private applyView(): void {
    if (!this.ready()) return;

    const centre = this.center();
    const zoom = this.zoom();

    this.map?.setView([centre.latitude, centre.longitude], zoom);
  }

  private applyMarkers(): void {
    if (!this.ready()) return;

    const markers = this.markers();
    const leaflet = this.leaflet;
    const layer = this.markerLayer;

    if (!leaflet || !layer) return;

    layer.clearLayers();

    for (const marker of markers) {
      leaflet
        .marker([marker.latitude, marker.longitude], {
          icon: leaflet.divIcon({
            className: mapMarkerClassNames[marker.kind],
            iconSize: undefined,
          }),
          title: marker.label ?? '',
          keyboard: true,
        })
        .on('click', () => this.markerSelect.emit(marker.id))
        .addTo(layer);
    }
  }

  private applyPin(): void {
    if (!this.ready()) return;

    const pin = this.pin();
    const leaflet = this.leaflet;
    const map = this.map;

    if (!leaflet || !map) return;

    if (!pin) {
      this.pinMarker?.remove();
      this.pinMarker = null;

      return;
    }

    if (this.pinMarker) {
      this.pinMarker.setLatLng([pin.latitude, pin.longitude]);

      return;
    }

    const pinMarker = leaflet.marker([pin.latitude, pin.longitude], {
      icon: leaflet.divIcon({ className: mapPinClassName, iconSize: undefined }),
      draggable: true,
      autoPan: true,
      keyboard: true,
      zIndexOffset: 1000,
    });

    pinMarker.on('dragend', () => this.pinMove.emit(pointOf(pinMarker.getLatLng())));
    this.pinMarker = pinMarker.addTo(map);
  }

  private applyAttribution(): void {
    if (!this.ready()) return;

    const attribution = this.attribution();
    const control = this.map?.attributionControl;

    if (!control) return;

    if (this.shownAttribution) control.removeAttribution(this.shownAttribution);

    control.addAttribution(attribution);
    this.shownAttribution = attribution;
  }
}

function leafletOf(module: LeafletModule & { default?: LeafletModule }): LeafletModule {
  return module.default ?? module;
}

function pointOf(latLng: Leaflet.LatLng): GeoPoint {
  const wrapped = latLng.wrap();

  return { latitude: wrapped.lat, longitude: wrapped.lng };
}
