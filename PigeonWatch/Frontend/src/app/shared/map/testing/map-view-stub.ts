import { ChangeDetectionStrategy, Component, input, output } from '@angular/core';
import { GeoPoint } from '../../../core/geolocation/geo-point';
import { MapMarker } from '../map-marker';

@Component({
  selector: 'app-map-view',
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './map-view-stub.html',
})
export class MapViewStub {
  readonly label = input.required<string>();
  readonly center = input.required<GeoPoint>();
  readonly zoom = input.required<number>();
  readonly markers = input<readonly MapMarker[]>([]);
  readonly pin = input<GeoPoint | null>(null);
  readonly selectedId = input<string | null>(null);
  readonly mapClick = output<GeoPoint>();
  readonly centerChange = output<GeoPoint>();
  readonly zoomChange = output<number>();
  readonly markerSelect = output<string>();
  readonly pinMove = output<GeoPoint>();
  readonly mapReady = output<void>();
  readonly loadFailed = output<boolean>();
  readonly focusedMarkers: string[] = [];

  focusMarker(id: string): void {
    this.focusedMarkers.push(id);
  }
}
