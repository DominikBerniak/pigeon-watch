import { SightingCriticality, SightingStatus } from '../sighting.types';
import {
  readNearbyMapFilters,
  readSightingDetailReturn,
  readSightingListHandOver,
} from './sighting-list-hand-over';

const centre = { latitude: 52.2297, longitude: 21.0122 };
const filters = { statuses: [SightingStatus.spotted], criticalities: [SightingCriticality.urgent] };

describe('readSightingListHandOver', () => {
  it('accepts a centre and filters', () => {
    expect(readSightingListHandOver({ centre, filters })).toEqual({ centre, filters });
  });

  it('accepts empty filters and ignores extra keys such as the router navigation id', () => {
    expect(
      readSightingListHandOver({
        centre,
        filters: { statuses: [], criticalities: [] },
        navigationId: 3,
      }),
    ).toEqual({ centre, filters: { statuses: [], criticalities: [] } });
  });

  it('accepts resolved statuses although the map sends only open ones', () => {
    const resolved = {
      statuses: [SightingStatus.adopted, SightingStatus.notFound],
      criticalities: [],
    };

    expect(readSightingListHandOver({ centre, filters: resolved })?.filters).toEqual(resolved);
  });

  it('accepts the coordinate limits', () => {
    const corner = { latitude: -90, longitude: 180 };

    expect(readSightingListHandOver({ centre: corner, filters })?.centre).toEqual(corner);
  });

  it.each([undefined, null, 'state', 42, [centre, filters]])(
    'rejects a state that is not an object (%s)',
    (state) => {
      expect(readSightingListHandOver(state)).toBeNull();
    },
  );

  it.each([
    { label: 'missing centre', state: { filters } },
    { label: 'missing filters', state: { centre } },
    { label: 'centre not an object', state: { centre: 'here', filters } },
    { label: 'latitude as text', state: { centre: { ...centre, latitude: '52.2' }, filters } },
    { label: 'missing longitude', state: { centre: { latitude: 52.2 }, filters } },
    { label: 'latitude above 90', state: { centre: { ...centre, latitude: 90.5 }, filters } },
    { label: 'longitude below -180', state: { centre: { ...centre, longitude: -181 }, filters } },
    { label: 'non-finite latitude', state: { centre: { ...centre, latitude: NaN }, filters } },
    {
      label: 'infinite longitude',
      state: { centre: { ...centre, longitude: Infinity }, filters },
    },
    { label: 'statuses not an array', state: { centre, filters: { ...filters, statuses: 1 } } },
    {
      label: 'missing criticalities',
      state: { centre, filters: { statuses: [SightingStatus.spotted] } },
    },
    { label: 'unknown status', state: { centre, filters: { ...filters, statuses: [10] } } },
    { label: 'status as text', state: { centre, filters: { ...filters, statuses: ['1'] } } },
    {
      label: 'unknown criticality',
      state: { centre, filters: { ...filters, criticalities: [0] } },
    },
  ])('rejects a hand-over with $label', ({ state }) => {
    expect(readSightingListHandOver(state)).toBeNull();
  });
});

describe('readSightingDetailReturn', () => {
  it('reads the list hand-over a list row passes to the detail page', () => {
    expect(
      readSightingDetailReturn({ returnToList: { centre, filters }, navigationId: 4 }),
    ).toEqual({ centre, filters });
  });

  it.each([
    { label: 'no state', state: undefined },
    { label: 'a map hand-over without returnToList', state: { centre, filters } },
    { label: 'an invalid list hand-over', state: { returnToList: { centre, filters: null } } },
  ])('returns null for $label', ({ state }) => {
    expect(readSightingDetailReturn(state)).toBeNull();
  });
});

describe('readNearbyMapFilters', () => {
  it('reads the filters the list hands back to the map', () => {
    expect(readNearbyMapFilters({ mapFilters: filters, navigationId: 2 })).toEqual(filters);
  });

  it('drops resolved statuses the map cannot filter by', () => {
    expect(
      readNearbyMapFilters({
        mapFilters: {
          statuses: [SightingStatus.adopted, SightingStatus.caught],
          criticalities: [SightingCriticality.monitor],
        },
      }),
    ).toEqual({ statuses: [SightingStatus.caught], criticalities: [SightingCriticality.monitor] });
  });

  it.each([
    { label: 'no state', state: undefined },
    { label: 'a state without mapFilters', state: { centre, filters } },
    { label: 'unknown criticalities', state: { mapFilters: { statuses: [], criticalities: [9] } } },
  ])('returns null for $label', ({ state }) => {
    expect(readNearbyMapFilters(state)).toBeNull();
  });
});
