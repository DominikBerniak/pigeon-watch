import { defaultLabels } from '../../core/resources/generated/snapshots';
import {
  Sighting,
  SightingCriticality,
  SightingStatus,
  allSightingStatuses,
  editableSightingStatuses,
  filterableSightingStatuses,
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

  it('lists all nine statuses in workflow order, the five open ones first', () => {
    expect(allSightingStatuses).toEqual(Object.values(SightingStatus));
    expect(allSightingStatuses.slice(0, filterableSightingStatuses.length)).toEqual(
      filterableSightingStatuses,
    );
  });

  it('offers the three criticality levels in rising order', () => {
    expect(selectableSightingCriticalities).toEqual([1, 2, 3]);
  });

  describe('editableSightingStatuses', () => {
    const sighting: Sighting = {
      id: 'sighting-1',
      name: null,
      description: 'Grey pigeon',
      behaviour: 'Cannot fly',
      latitude: 52.2297,
      longitude: 21.0122,
      status: SightingStatus.spotted,
      criticality: SightingCriticality.urgent,
      reporterDisplayName: 'Jan K',
      createdAt: '2026-10-11T12:00:00Z',
      hasPhoto: false,
      removedAt: null,
      isMine: true,
      editPermissions: {
        canEditDescription: true,
        allowedNextStatuses: [SightingStatus.caught, SightingStatus.notFound],
      },
    };

    it('offers the current status first, then the allowed next statuses', () => {
      expect(editableSightingStatuses(sighting)).toEqual([
        SightingStatus.spotted,
        SightingStatus.caught,
        SightingStatus.notFound,
      ]);
    });

    it('offers only the current status in an end state', () => {
      expect(
        editableSightingStatuses({
          ...sighting,
          status: SightingStatus.adopted,
          editPermissions: { canEditDescription: true, allowedNextStatuses: [] },
        }),
      ).toEqual([SightingStatus.adopted]);
    });

    it('lists the current status once even when the server repeats it', () => {
      expect(
        editableSightingStatuses({
          ...sighting,
          editPermissions: {
            canEditDescription: true,
            allowedNextStatuses: [SightingStatus.spotted, SightingStatus.caught],
          },
        }),
      ).toEqual([SightingStatus.spotted, SightingStatus.caught]);
    });

    it('offers only the current status without permissions', () => {
      expect(editableSightingStatuses({ ...sighting, editPermissions: null })).toEqual([
        SightingStatus.spotted,
      ]);
    });
  });
});
