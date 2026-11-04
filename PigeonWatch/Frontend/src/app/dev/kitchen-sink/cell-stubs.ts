import { HttpErrorResponse } from '@angular/common/http';
import { DestroyRef, Injectable, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import type * as Leaflet from 'leaflet';
import { NEVER, Observable, map, of, throwError, timer } from 'rxjs';
import {
  ClientConfiguration,
  CurrentUser,
  GeneralConfiguration,
} from '../../core/configuration/configuration.service';
import { GeoPoint } from '../../core/geolocation/geo-point';
import { GeolocationOutcome } from '../../core/geolocation/geolocation.service';
import { UpdatedProfile } from '../../features/profile/profile-api';
import {
  Sighting,
  SightingCriticality,
  SightingFilters,
  SightingList,
  SightingListRequest,
  SightingPage,
  SightingStatus,
} from '../../features/sightings/sighting.types';

export type SaveMode = 'success' | 'duplicate' | 'failure' | 'pending';
export type PasswordMode = 'success' | 'wrongCurrent' | 'pending';
export type MineMode = 'empty' | 'items';
export type NearbyMode =
  'items' | 'empty' | 'failure' | 'pending' | 'itemsThenPending' | 'filtered';
export type NextLocationMode = 'ok' | 'timeout' | 'pending';
export type GeolocationMode = 'ok' | 'denied' | 'timeoutThenNext' | NextLocationMode;
export type LeafletMode = 'tileless' | 'failure';
export type ListMode = 'items' | 'paged' | 'empty' | 'failure' | 'pending';
export type ListGeolocationMode = Exclude<GeolocationMode, 'timeoutThenNext'>;
export type DetailMode =
  | 'loaded'
  | 'loadedUnnamed'
  | 'loadedLongName'
  | 'loadedWithPhoto'
  | 'notFound'
  | 'failure'
  | 'pending'
  | 'failureThenLoaded';
export type PhotoMode = 'image' | 'pending' | 'failure';

export interface ProfileModes {
  readonly group: 'profile';
  readonly save: SaveMode;
  readonly password: PasswordMode;
  readonly mine?: MineMode;
}

export interface NearbyMapModes {
  readonly group: 'nearbyMap';
  readonly nearby: NearbyMode;
  readonly geolocation: GeolocationMode;
  readonly leaflet: LeafletMode;
  readonly nextLocation?: NextLocationMode;
}

export interface SightingDetailModes {
  readonly group: 'sightingDetail';
  readonly detail: DetailMode;
  readonly leaflet: LeafletMode;
  readonly photo?: PhotoMode;
}

export interface SightingListModes {
  readonly group: 'sightingList';
  readonly list: ListMode;
  readonly geolocation: ListGeolocationMode;
  readonly thumbnail?: PhotoMode;
}

export type StubModes = ProfileModes | NearbyMapModes | SightingDetailModes | SightingListModes;

type LeafletModule = typeof Leaflet;

export type LeafletLoader = () => Observable<LeafletModule & { default?: LeafletModule }>;

export const sinkUser: CurrentUser = {
  id: '1',
  email: 'jan@example.com',
  displayName: 'Jan K',
  roles: [],
};

export const sinkPosition: GeoPoint = { latitude: 52.2297, longitude: 21.0122 };

const sinkLoadedAt = Date.now();
const minuteMilliseconds = 60_000;

function sinkSighting(
  id: string,
  name: string | null,
  criticality: SightingCriticality,
  offset: GeoPoint,
  ageMinutes: number,
  hasPhoto = false,
): Sighting {
  return {
    id,
    name,
    description: 'Grey pigeon sitting by the bus stop',
    behaviour: 'Does not fly away when approached',
    latitude: sinkPosition.latitude + offset.latitude,
    longitude: sinkPosition.longitude + offset.longitude,
    status: SightingStatus.spotted,
    criticality,
    reporterDisplayName: sinkUser.displayName,
    createdAt: new Date(sinkLoadedAt - ageMinutes * minuteMilliseconds).toISOString(),
    hasPhoto,
  };
}

export const sinkSightings: readonly Sighting[] = [
  sinkSighting(
    'sink-urgent',
    'Limpy',
    SightingCriticality.urgent,
    { latitude: 0.004, longitude: -0.006 },
    5,
  ),
  sinkSighting(
    'sink-needs-help-soon',
    'Limpy',
    SightingCriticality.needsHelpSoon,
    { latitude: -0.005, longitude: 0.008 },
    3 * 60,
  ),
  sinkSighting(
    'sink-monitor',
    'Fluffy',
    SightingCriticality.monitor,
    { latitude: 0.009, longitude: 0.012 },
    2 * 24 * 60,
    true,
  ),
  sinkSighting(
    'sink-unnamed',
    null,
    SightingCriticality.monitor,
    { latitude: -0.008, longitude: -0.013 },
    0,
  ),
];

export const sinkLongNameSighting: Sighting = sinkSighting(
  'sink-long-name',
  'Grey pigeon with a ruffled left wing from the bus stop on Marszalkowska Street',
  SightingCriticality.urgent,
  { latitude: 0.004, longitude: -0.006 },
  45,
);

const listCriticalities: readonly SightingCriticality[] = [
  SightingCriticality.urgent,
  SightingCriticality.urgent,
  SightingCriticality.urgent,
  SightingCriticality.needsHelpSoon,
  SightingCriticality.needsHelpSoon,
  SightingCriticality.needsHelpSoon,
  SightingCriticality.needsHelpSoon,
  SightingCriticality.monitor,
  SightingCriticality.monitor,
  SightingCriticality.monitor,
  SightingCriticality.monitor,
  SightingCriticality.monitor,
  SightingCriticality.monitor,
];

const listAgesMinutes: readonly number[] = [
  3 * 24 * 60,
  5 * 60,
  20,
  2 * 24 * 60,
  10 * 60,
  90,
  5,
  4 * 24 * 60,
  24 * 60,
  8 * 60,
  60,
  15,
  0,
];

export const sinkListSightings: readonly Sighting[] = listCriticalities.map((criticality, index) =>
  sinkSighting(
    `sink-list-${index + 1}`,
    index === 4 ? null : `Pigeon ${index + 1}`,
    criticality,
    { latitude: index * 0.001, longitude: -index * 0.001 },
    listAgesMinutes[index],
    index % 4 === 1,
  ),
);

export const sinkPhotoSighting: Sighting = {
  ...sinkSightings[0],
  id: 'sink-photo',
  hasPhoto: true,
};

const sinkPhotoSvg = [
  '<svg xmlns="http://www.w3.org/2000/svg" width="1600" height="1200" viewBox="0 0 1600 1200">',
  '<rect width="1600" height="1200" fill="lightsteelblue"/>',
  '<ellipse cx="800" cy="720" rx="420" ry="300" fill="slategray"/>',
  '<circle cx="1080" cy="420" r="170" fill="slategray"/>',
  '<circle cx="1130" cy="390" r="24" fill="black"/>',
  '</svg>',
].join('');

export function sinkPhoto(): Blob {
  return new Blob([sinkPhotoSvg], { type: 'image/svg+xml' });
}

const busyHoldMilliseconds = 30000;

function badRequest(errors: Record<string, string[]>): HttpErrorResponse {
  return new HttpErrorResponse({ status: 400, error: { status: 400, errors } });
}

function matches(sighting: Sighting, filters?: SightingFilters): boolean {
  const statuses = filters?.statuses ?? [];
  const criticalities = filters?.criticalities ?? [];

  return (
    (statuses.length === 0 || statuses.includes(sighting.status)) &&
    (criticalities.length === 0 || criticalities.includes(sighting.criticality))
  );
}

function outcomeOf(
  mode: Exclude<GeolocationMode, 'timeoutThenNext'>,
): Observable<GeolocationOutcome> {
  switch (mode) {
    case 'ok':
      return of({ status: 'ok', ...sinkPosition });
    case 'timeout':
      return of({ status: 'timeout' });
    case 'pending':
      return NEVER;
    default:
      return of({ status: 'denied' });
  }
}

export function tilelessLeaflet(
  module: LeafletModule & { default?: LeafletModule },
): LeafletModule {
  const leaflet = module.default ?? module;
  const tileLayer = (_urlTemplate: string, options?: Leaflet.TileLayerOptions): Leaflet.TileLayer =>
    leaflet.gridLayer(options) as Leaflet.TileLayer;
  const stillMap = (element: string | HTMLElement, options?: Leaflet.MapOptions): Leaflet.Map =>
    leaflet.map(element, { ...options, zoomAnimation: false });

  return {
    ...leaflet,
    map: stillMap,
    tileLayer: Object.assign(tileLayer, { wms: leaflet.tileLayer.wms }),
  };
}

@Injectable()
export class CellStubs {
  private readonly destroyRef = inject(DestroyRef);
  private modes: () => StubModes = () => ({
    group: 'profile',
    save: 'success',
    password: 'success',
  });
  private readonly chosenNextLocation = signal<NextLocationMode | null>(null);
  private nearbyRequests = 0;
  private locationRequests = 0;
  private detailRequests = 0;

  readonly session = {
    currentUser: signal<CurrentUser | null>(sinkUser).asReadonly(),
    changePassword: (): Observable<void> => this.passwordResult(),
  };
  readonly configuration = {
    clientConfig: signal<ClientConfiguration | null>(null),
    loadGeneral: (): Observable<GeneralConfiguration> => of({ currentUser: sinkUser }),
  };
  readonly profileApi = {
    updateDisplayName: (displayName: string): Observable<UpdatedProfile> =>
      this.saveResult(displayName),
  };
  readonly sightingsApi = {
    mine: (): Observable<SightingList> => this.mineResult(),
    nearby: (_centre: GeoPoint, filters?: SightingFilters): Observable<SightingList> =>
      this.nearbyResult(filters),
    get: (): Observable<Sighting> => this.detailResult(),
    photo: (): Observable<Blob> => this.photoResult(),
    photoThumbnail: (): Observable<Blob> => this.thumbnailResult(),
    list: (request: SightingListRequest): Observable<SightingPage> => this.listResult(request),
  };
  readonly geolocation = {
    currentPosition: (): Observable<GeolocationOutcome> => this.positionResult(),
  };

  readonly nextLocation = computed<NextLocationMode>(() => {
    const modes = this.modes();

    return (
      this.chosenNextLocation() ?? (modes.group === 'nearbyMap' ? modes.nextLocation : null) ?? 'ok'
    );
  });

  follow(modes: () => StubModes): void {
    this.modes = modes;
  }

  chooseNextLocation(mode: NextLocationMode): void {
    this.chosenNextLocation.set(mode);
  }

  leafletLoader(load: LeafletLoader): LeafletLoader {
    return () => {
      const modes = this.modes();
      const failing =
        (modes.group === 'nearbyMap' || modes.group === 'sightingDetail') &&
        modes.leaflet === 'failure';

      if (failing) return throwError(() => new Error('Leaflet stubbed to fail'));

      return load().pipe(map(tilelessLeaflet));
    };
  }

  private saveResult(displayName: string): Observable<UpdatedProfile> {
    const modes = this.modes();

    switch (modes.group === 'profile' ? modes.save : 'success') {
      case 'duplicate':
        return throwError(() => badRequest({ DuplicateDisplayName: ['Display name is taken.'] }));
      case 'failure':
        return throwError(() => new HttpErrorResponse({ status: 500 }));
      case 'pending':
        return NEVER;
      default:
        return of({ email: sinkUser.email, displayName });
    }
  }

  private passwordResult(): Observable<void> {
    const modes = this.modes();

    switch (modes.group === 'profile' ? modes.password : 'success') {
      case 'wrongCurrent':
        return throwError(() => badRequest({ PasswordMismatch: ['Incorrect password.'] }));
      case 'pending':
        return timer(busyHoldMilliseconds).pipe(
          map((): void => undefined),
          takeUntilDestroyed(this.destroyRef),
        );
      default:
        return of(undefined);
    }
  }

  private mineResult(): Observable<SightingList> {
    const modes = this.modes();
    const withItems = modes.group === 'profile' && modes.mine === 'items';

    return of({ items: withItems ? [...sinkSightings] : [] });
  }

  private nearbyResult(filters?: SightingFilters): Observable<SightingList> {
    const modes = this.modes();
    const firstRequest = this.nearbyRequests++ === 0;

    switch (modes.group === 'nearbyMap' ? modes.nearby : 'empty') {
      case 'items':
        return of({ items: [...sinkSightings] });
      case 'itemsThenPending':
        return firstRequest ? of({ items: [...sinkSightings] }) : NEVER;
      case 'failure':
        return throwError(() => new HttpErrorResponse({ status: 500 }));
      case 'pending':
        return NEVER;
      case 'filtered':
        return of({ items: sinkSightings.filter((sighting) => matches(sighting, filters)) });
      default:
        return of({ items: [] });
    }
  }

  private listResult(request: SightingListRequest): Observable<SightingPage> {
    const modes = this.modes();
    const mode = modes.group === 'sightingList' ? modes.list : 'empty';

    switch (mode) {
      case 'failure':
        return throwError(() => new HttpErrorResponse({ status: 500 }));
      case 'pending':
        return NEVER;
      case 'empty':
        return of({ items: [], totalCount: 0 });
      default: {
        const source = mode === 'paged' ? sinkListSightings : sinkSightings;
        const matching = source.filter((sighting) => matches(sighting, request));
        const start = (request.page - 1) * request.pageSize;

        return of({
          items: matching.slice(start, start + request.pageSize),
          totalCount: matching.length,
        });
      }
    }
  }

  private detailResult(): Observable<Sighting> {
    const modes = this.modes();
    const firstRequest = this.detailRequests++ === 0;

    switch (modes.group === 'sightingDetail' ? modes.detail : 'loaded') {
      case 'loadedUnnamed':
        return of(sinkSightings[3]);
      case 'loadedLongName':
        return of(sinkLongNameSighting);
      case 'loadedWithPhoto':
        return of(sinkPhotoSighting);
      case 'notFound':
        return throwError(() => new HttpErrorResponse({ status: 404 }));
      case 'failure':
        return throwError(() => new HttpErrorResponse({ status: 500 }));
      case 'pending':
        return NEVER;
      case 'failureThenLoaded':
        return firstRequest
          ? throwError(() => new HttpErrorResponse({ status: 500 }))
          : of(sinkSightings[0]);
      default:
        return of(sinkSightings[0]);
    }
  }

  private photoResult(): Observable<Blob> {
    const modes = this.modes();

    switch (modes.group === 'sightingDetail' ? (modes.photo ?? 'image') : 'image') {
      case 'pending':
        return NEVER;
      case 'failure':
        return throwError(() => new HttpErrorResponse({ status: 500 }));
      default:
        return of(sinkPhoto());
    }
  }

  private thumbnailResult(): Observable<Blob> {
    const modes = this.modes();

    switch (modes.group === 'sightingList' ? (modes.thumbnail ?? 'image') : 'image') {
      case 'pending':
        return NEVER;
      case 'failure':
        return throwError(() => new HttpErrorResponse({ status: 500 }));
      default:
        return of(sinkPhoto());
    }
  }

  private positionResult(): Observable<GeolocationOutcome> {
    const modes = this.modes();
    if (modes.group === 'sightingList') return outcomeOf(modes.geolocation);

    const mode = modes.group === 'nearbyMap' ? modes.geolocation : 'denied';
    const firstRequest = this.locationRequests++ === 0;

    if (mode !== 'timeoutThenNext') return outcomeOf(mode);

    return outcomeOf(firstRequest ? 'timeout' : this.nextLocation());
  }
}
