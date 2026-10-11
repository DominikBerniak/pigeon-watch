import { GeoPoint } from '../../core/geolocation/geo-point';

export type MapMarkerKind = 'monitor' | 'needsHelpSoon' | 'urgent';

export interface MapMarker extends GeoPoint {
  readonly id: string;
  readonly kind: MapMarkerKind;
  readonly label?: string;
}

export const mapMarkerClassNames: Readonly<Record<MapMarkerKind, string>> = {
  monitor: 'pw-map-marker pw-map-marker--monitor',
  needsHelpSoon: 'pw-map-marker pw-map-marker--needs-help-soon',
  urgent: 'pw-map-marker pw-map-marker--urgent',
};

export const mapMarkerSelectedClassName = 'pw-map-marker--selected';

export const mapPinClassName = 'pw-map-marker pw-map-marker--pin';
