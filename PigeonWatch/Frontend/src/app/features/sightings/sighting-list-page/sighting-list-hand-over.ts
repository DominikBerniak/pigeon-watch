import { GeoPoint } from '../../../core/geolocation/geo-point';
import {
  SightingFilters,
  allSightingStatuses,
  filterableSightingStatuses,
  selectableSightingCriticalities,
} from '../sighting.types';

export interface SightingListHandOver {
  readonly centre: GeoPoint;
  readonly filters: SightingFilters;
}

const maxLatitude = 90;
const maxLongitude = 180;

export function readSightingListHandOver(state: unknown): SightingListHandOver | null {
  if (!isRecord(state)) return null;

  const centre = readCentre(state['centre']);
  const filters = readFilters(state['filters']);

  return centre && filters ? { centre, filters } : null;
}

export interface SightingDetailReturn {
  readonly returnToList: SightingListHandOver;
}

export function readSightingDetailReturn(state: unknown): SightingListHandOver | null {
  return isRecord(state) ? readSightingListHandOver(state['returnToList']) : null;
}

export interface NearbyMapReturn {
  readonly mapFilters: SightingFilters;
}

export function readNearbyMapFilters(state: unknown): SightingFilters | null {
  const filters = isRecord(state) ? readFilters(state['mapFilters']) : null;

  if (!filters) return null;

  return {
    statuses: filters.statuses.filter((status) => filterableSightingStatuses.includes(status)),
    criticalities: filters.criticalities,
  };
}

function readCentre(value: unknown): GeoPoint | null {
  if (!isRecord(value)) return null;

  const latitude = value['latitude'];
  const longitude = value['longitude'];

  if (!isWithin(latitude, maxLatitude) || !isWithin(longitude, maxLongitude)) return null;

  return { latitude, longitude };
}

function readFilters(value: unknown): SightingFilters | null {
  if (!isRecord(value)) return null;

  const statuses = readKnown(value['statuses'], allSightingStatuses);
  const criticalities = readKnown(value['criticalities'], selectableSightingCriticalities);

  return statuses && criticalities ? { statuses, criticalities } : null;
}

function readKnown<T>(value: unknown, known: readonly T[]): T[] | null {
  if (!Array.isArray(value)) return null;

  const items: readonly unknown[] = value;
  const accepted = items.filter((item): item is T => known.some((option) => option === item));

  return accepted.length === items.length ? accepted : null;
}

function isWithin(value: unknown, limit: number): value is number {
  return typeof value === 'number' && Number.isFinite(value) && Math.abs(value) <= limit;
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null && !Array.isArray(value);
}
