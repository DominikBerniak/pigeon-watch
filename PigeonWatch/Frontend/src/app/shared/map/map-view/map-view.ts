import { DOCUMENT } from '@angular/common';
import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  ElementRef,
  InjectionToken,
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
import { EMPTY, Observable, catchError, from } from 'rxjs';
import { GeoPoint } from '../../../core/geolocation/geo-point';
import { ResourceService } from '../../../core/resources/resource.service';
import { TranslatePipe } from '../../../core/resources/translate.pipe';
import { ErrorRetry } from '../../ui/error-retry/error-retry';
import { attributionHtml, mapTiles } from '../map-config';
import {
  MapMarker,
  mapMarkerClassNames,
  mapMarkerSelectedClassName,
  mapPinClassName,
} from '../map-marker';

type LeafletModule = typeof Leaflet;
type LeafletImport = LeafletModule & { default?: LeafletModule };

export const leafletStylesheetHref = 'leaflet.css';

const raisedZIndexOffset = 1000;

const selectKeys: ReadonlySet<string> = new Set(['Enter', ' ']);

export const LEAFLET_LOADER = new InjectionToken<() => Observable<LeafletImport>>(
  'LEAFLET_LOADER',
  {
    providedIn: 'root',
    factory: () => {
      const document = inject(DOCUMENT);

      return () => {
        addLeafletStylesheet(document);

        return from(import('leaflet'));
      };
    },
  },
);

function addLeafletStylesheet(document: Document): void {
  if (document.head.querySelector(`link[href="${leafletStylesheetHref}"]`)) return;

  const link = document.createElement('link');

  link.rel = 'stylesheet';
  link.href = leafletStylesheetHref;
  document.head.appendChild(link);
}

@Component({
  selector: 'app-map-view',
  imports: [TranslatePipe, ErrorRetry],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './map-view.html',
  styleUrl: './map-view.scss',
})
export class MapView {
  private readonly resources = inject(ResourceService);
  private readonly destroyRef = inject(DestroyRef);
  private readonly loadLeaflet = inject(LEAFLET_LOADER);
  private readonly canvas = viewChild.required<ElementRef<HTMLElement>>('canvas');
  private readonly attribution = computed(() =>
    attributionHtml(this.resources.translate('map.attribution')),
  );
  private readonly ready = signal(false);
  private leaflet: LeafletModule | null = null;
  private map: Leaflet.Map | null = null;
  private markerLayer: Leaflet.LayerGroup | null = null;
  private pinMarker: Leaflet.Marker | null = null;
  private readonly shownMarkers = new Map<string, Leaflet.Marker>();
  private shownAttribution: string | null = null;

  readonly label = input.required<string>();
  readonly center = input.required<GeoPoint>();
  readonly zoom = input.required<number>();
  readonly markers = input<readonly MapMarker[]>([]);
  readonly pin = input<GeoPoint | null>(null);
  readonly selectedId = input<string | null>(null);
  readonly interactiveMarkers = input(true);
  readonly mapClick = output<GeoPoint>();
  readonly centerChange = output<GeoPoint>();
  readonly zoomChange = output<number>();
  readonly markerSelect = output<string>();
  readonly pinMove = output<GeoPoint>();
  readonly mapReady = output<void>();
  readonly loadFailed = output<boolean>();

  protected readonly failed = signal(false);

  constructor() {
    afterNextRender(() => this.load());
    effect(() => this.applyView());
    effect(() => this.applyMarkers());
    effect(() => this.applySelection(this.selectedId()));
    effect(() => this.applyPin());
    effect(() => this.applyAttribution());
    this.destroyRef.onDestroy(() => this.map?.remove());
  }

  focusMarker(id: string): void {
    this.shownMarkers.get(id)?.getElement()?.focus();
  }

  protected retry(): void {
    this.load();
  }

  private load(): void {
    if (this.failed()) this.loadFailed.emit(false);

    this.failed.set(false);
    this.loadLeaflet()
      .pipe(
        catchError(() => {
          this.failed.set(true);
          this.loadFailed.emit(true);

          return EMPTY;
        }),
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
    map.on('moveend', () => {
      this.centerChange.emit(pointOf(map.getCenter()));
      this.zoomChange.emit(map.getZoom());
    });

    this.leaflet = leaflet;
    this.map = map;
    this.markerLayer = leaflet.layerGroup().addTo(map);
    this.ready.set(true);
    this.mapReady.emit();
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
    const interactive = this.interactiveMarkers();
    const leaflet = this.leaflet;
    const layer = this.markerLayer;

    if (!leaflet || !layer) return;

    layer.clearLayers();
    this.shownMarkers.clear();

    for (const marker of markers) {
      const shown = leaflet
        .marker([marker.latitude, marker.longitude], {
          icon: leaflet.divIcon({
            className: mapMarkerClassNames[marker.kind],
            iconSize: undefined,
          }),
          title: marker.label ?? '',
          keyboard: interactive,
        })
        .addTo(layer);

      if (interactive) {
        shown
          .on('click', () => this.markerSelect.emit(marker.id))
          .on('keydown', (event: Leaflet.LeafletKeyboardEvent) =>
            this.onMarkerKey(event, marker.id),
          );
      }

      this.shownMarkers.set(marker.id, shown);
    }

    this.applySelection(untracked(this.selectedId));
  }

  private onMarkerKey(event: Leaflet.LeafletKeyboardEvent, id: string): void {
    if (!selectKeys.has(event.originalEvent.key)) return;

    event.originalEvent.preventDefault();

    if (event.originalEvent.repeat) return;

    this.markerSelect.emit(id);
  }

  private applySelection(selectedId: string | null): void {
    const interactive = untracked(this.interactiveMarkers);

    for (const [id, marker] of this.shownMarkers) {
      const selected = id === selectedId;
      const element = marker.getElement();

      element?.classList.toggle(mapMarkerSelectedClassName, selected);

      if (interactive) element?.setAttribute('aria-pressed', String(selected));
      marker.setZIndexOffset(selected ? raisedZIndexOffset : 0);
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
      zIndexOffset: raisedZIndexOffset,
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

function leafletOf(module: LeafletImport): LeafletModule {
  return module.default ?? module;
}

function pointOf(latLng: Leaflet.LatLng): GeoPoint {
  const wrapped = latLng.wrap();

  return { latitude: wrapped.lat, longitude: wrapped.lng };
}
