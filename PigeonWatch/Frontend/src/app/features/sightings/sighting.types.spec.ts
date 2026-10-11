import { defaultLabels } from '../../core/resources/generated/snapshots';
import {
  SightingCriticality,
  SightingStatus,
  initialSightingStatuses,
  selectableSightingCriticalities,
  sightingCriticalityLabel,
  sightingCriticalityLabels,
  sightingCriticalityMarker,
  sightingCriticalityMarkers,
  sightingStatusLabel,
  sightingStatusLabels,
  unknownSightingValueLabel,
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

  it('looks up known values and falls back for values the SPA does not know', () => {
    const unknownStatus = 99 as SightingStatus;
    const unknownCriticality = 99 as SightingCriticality;

    expect(sightingStatusLabel(SightingStatus.caught)).toBe('sighting.status.caught');
    expect(sightingCriticalityLabel(SightingCriticality.urgent)).toBe(
      'sighting.criticality.urgent',
    );
    expect(sightingCriticalityMarker(SightingCriticality.needsHelpSoon)).toBe('needsHelpSoon');
    expect(sightingStatusLabel(unknownStatus)).toBe(unknownSightingValueLabel);
    expect(sightingCriticalityLabel(unknownCriticality)).toBe(unknownSightingValueLabel);
    expect(sightingCriticalityMarker(unknownCriticality)).toBe('monitor');
    expect(Object.hasOwn(defaultLabels, unknownSightingValueLabel)).toBe(true);
  });

  it('offers only Spotted and Caught as initial statuses', () => {
    expect(initialSightingStatuses).toEqual([SightingStatus.spotted, SightingStatus.caught]);
  });

  it('offers the three criticality levels in rising order', () => {
    expect(selectableSightingCriticalities).toEqual([1, 2, 3]);
  });
});
