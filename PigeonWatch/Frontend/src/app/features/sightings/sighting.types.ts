import { SightingRules } from '../../core/configuration/configuration.service';
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
}

export interface SightingList {
  items: Sighting[];
}

export interface NewSighting {
  name: string | null;
  description: string;
  behaviour: string;
  latitude: number;
  longitude: number;
  status: SightingStatus;
  criticality: SightingCriticality;
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

export const initialSightingStatuses: readonly SightingStatus[] = [
  SightingStatus.spotted,
  SightingStatus.caught,
];

export const selectableSightingCriticalities: readonly SightingCriticality[] = [
  SightingCriticality.monitor,
  SightingCriticality.needsHelpSoon,
  SightingCriticality.urgent,
];

export const fallbackSightingRules: SightingRules = {
  nameMaxLength: 60,
  descriptionMaxLength: 1000,
  behaviourMaxLength: 500,
  cooldownSeconds: 60,
  nearbyRadiusKm: 10,
};
