import { CommentRules, SightingRules } from '../../core/configuration/configuration.service';
import { MapMarkerKind } from '../../shared/map/map-marker';

export const SightingStatus = {
  spotted: 1,
  caught: 2,
  atTheVet: 3,
  inHomeCare: 4,
  awaitingAdoption: 5,
  adopted: 6,
  released: 7,
  passedAway: 8,
  notFound: 9,
} as const;

export type SightingStatus = (typeof SightingStatus)[keyof typeof SightingStatus];

export const SightingCriticality = {
  monitor: 1,
  needsHelpSoon: 2,
  urgent: 3,
} as const;

export type SightingCriticality = (typeof SightingCriticality)[keyof typeof SightingCriticality];

export interface Sighting {
  id: string;
  name: string | null;
  description: string;
  behaviour: string;
  latitude: number;
  longitude: number;
  status: SightingStatus;
  criticality: SightingCriticality;
  reporterDisplayName: string;
  createdAt: string;
  hasPhoto: boolean;
}

export interface SightingList {
  items: Sighting[];
}

export interface SightingPage {
  items: Sighting[];
  totalCount: number;
}

export interface SightingComment {
  id: string;
  text: string | null;
  authorDisplayName: string | null;
  createdAt: string;
  editedAt: string | null;
  isDeleted: boolean;
  isMine: boolean;
}

export interface SightingCommentPage {
  items: SightingComment[];
  totalCount: number;
}

export interface SightingListRequest {
  latitude: number;
  longitude: number;
  statuses: readonly SightingStatus[];
  criticalities: readonly SightingCriticality[];
  page: number;
  pageSize: number;
}

export const sightingListPageSizes: readonly number[] = [10, 20, 30];

export const defaultSightingListPageSize = 10;

export interface NewSighting {
  name: string | null;
  description: string;
  behaviour: string;
  latitude: number;
  longitude: number;
  status: SightingStatus;
  criticality: SightingCriticality;
  photo: Blob | null;
}

export const sightingStatusLabels: Readonly<Record<SightingStatus, string>> = {
  1: 'sighting.status.spotted',
  2: 'sighting.status.caught',
  3: 'sighting.status.atTheVet',
  4: 'sighting.status.inHomeCare',
  5: 'sighting.status.awaitingAdoption',
  6: 'sighting.status.adopted',
  7: 'sighting.status.released',
  8: 'sighting.status.passedAway',
  9: 'sighting.status.notFound',
};

export const sightingCriticalityLabels: Readonly<Record<SightingCriticality, string>> = {
  1: 'sighting.criticality.monitor',
  2: 'sighting.criticality.needsHelpSoon',
  3: 'sighting.criticality.urgent',
};

export const sightingCriticalityMarkers: Readonly<Record<SightingCriticality, MapMarkerKind>> = {
  1: 'monitor',
  2: 'needsHelpSoon',
  3: 'urgent',
};

export const unknownSightingValueLabel = 'sighting.unknown';

export function sightingStatusLabel(status: SightingStatus): string {
  const label: string | undefined = sightingStatusLabels[status];

  return label ?? unknownSightingValueLabel;
}

export function sightingCriticalityLabel(criticality: SightingCriticality): string {
  const label: string | undefined = sightingCriticalityLabels[criticality];

  return label ?? unknownSightingValueLabel;
}

export function sightingCriticalityMarker(criticality: SightingCriticality): MapMarkerKind {
  const kind: MapMarkerKind | undefined = sightingCriticalityMarkers[criticality];

  return kind ?? 'monitor';
}

export const initialSightingStatuses: readonly SightingStatus[] = [
  SightingStatus.spotted,
  SightingStatus.caught,
];

export const selectableSightingCriticalities: readonly SightingCriticality[] = [
  SightingCriticality.monitor,
  SightingCriticality.needsHelpSoon,
  SightingCriticality.urgent,
];

export const filterableSightingStatuses: readonly SightingStatus[] = [
  SightingStatus.spotted,
  SightingStatus.caught,
  SightingStatus.atTheVet,
  SightingStatus.inHomeCare,
  SightingStatus.awaitingAdoption,
];

export const allSightingStatuses: readonly SightingStatus[] = [
  SightingStatus.spotted,
  SightingStatus.caught,
  SightingStatus.atTheVet,
  SightingStatus.inHomeCare,
  SightingStatus.awaitingAdoption,
  SightingStatus.adopted,
  SightingStatus.released,
  SightingStatus.passedAway,
  SightingStatus.notFound,
];

export interface SightingFilters {
  statuses: readonly SightingStatus[];
  criticalities: readonly SightingCriticality[];
}

export const fallbackSightingRules: SightingRules = {
  nameMaxLength: 60,
  descriptionMaxLength: 1000,
  behaviourMaxLength: 500,
  cooldownSeconds: 60,
  nearbyRadiusKm: 10,
  photoMaxBytes: 2097152,
  photoMaxDimension: 1600,
  photoAllowedContentTypes: ['image/jpeg', 'image/png', 'image/webp'],
};

export const fallbackCommentRules: CommentRules = {
  textMaxLength: 500,
  cooldownSeconds: 60,
  pageSize: 20,
};
