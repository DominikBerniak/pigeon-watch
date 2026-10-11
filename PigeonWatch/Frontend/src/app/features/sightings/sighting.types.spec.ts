import { defaultLabels } from '../../core/resources/generated/snapshots';
import {
  SightingCriticality,
  SightingStatus,
  initialSightingStatuses,
  selectableSightingCriticalities,
  sightingCriticalityLabels,
  sightingCriticalityMarkers,
  sightingStatusLabels,
} from './sighting.types';

describe('sighting types', () => {
  it('maps every status to a label that exists', () => {
    const statuses = Object.values(SightingStatus);

    expect(Object.keys(sightingStatusLabels).map(Number)).toEqual(statuses);
    expect(
      Object.values(sightingStatusLabels).filter((key) => !Object.hasOwn(defaultLabels, key)),
    ).toEqual([]);
  });

  it('maps every criticality to a label that exists and a marker kind', () => {
    const levels = Object.values(SightingCriticality);

    expect(Object.keys(sightingCriticalityLabels).map(Number)).toEqual(levels);
    expect(
      Object.values(sightingCriticalityLabels).filter((key) => !Object.hasOwn(defaultLabels, key)),
    ).toEqual([]);
    expect(Object.keys(sightingCriticalityMarkers).map(Number)).toEqual(levels);
  });

  it('offers only Spotted and Caught as initial statuses', () => {
    expect(initialSightingStatuses).toEqual([SightingStatus.spotted, SightingStatus.caught]);
  });

  it('offers the three criticality levels in rising order', () => {
    expect(selectableSightingCriticalities).toEqual([1, 2, 3]);
  });
});
