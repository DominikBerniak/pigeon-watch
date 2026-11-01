import { defaultLabels } from '../../../core/resources/generated/snapshots';
import { sightingAge } from './sighting-age';

const now = new Date('2026-10-11T12:00:00Z');

function ago(milliseconds: number): string {
  return new Date(now.getTime() - milliseconds).toISOString();
}

const second = 1000;
const minute = 60 * second;
const hour = 60 * minute;
const day = 24 * hour;

describe('sightingAge', () => {
  it.each([
    ['in the future', -5 * minute, 'sighting.age.justNow', 0],
    ['at the same instant', 0, 'sighting.age.justNow', 0],
    ['just under a minute', minute - second, 'sighting.age.justNow', 0],
    ['exactly one minute', minute, 'sighting.age.minutes', 1],
    ['just under two minutes', 2 * minute - second, 'sighting.age.minutes', 1],
    ['just under an hour', hour - second, 'sighting.age.minutes', 59],
    ['exactly one hour', hour, 'sighting.age.hours', 1],
    ['just under a day', day - second, 'sighting.age.hours', 23],
    ['exactly one day', day, 'sighting.age.days', 1],
    ['forty days', 40 * day + 5 * hour, 'sighting.age.days', 40],
  ])('reports %s with %s', (_, elapsed, key, value) => {
    expect(sightingAge(ago(elapsed), now)).toEqual({ key, value });
  });

  it('treats an unreadable date as just now', () => {
    expect(sightingAge('not a date', now)).toEqual({ key: 'sighting.age.justNow', value: 0 });
  });

  it('uses only keys that exist, with one placeholder for the counted units', () => {
    const keys = [
      'sighting.age.justNow',
      'sighting.age.minutes',
      'sighting.age.hours',
      'sighting.age.days',
    ];

    expect(keys.filter((key) => !Object.hasOwn(defaultLabels, key))).toEqual([]);
    expect(keys.slice(1).map((key) => defaultLabels[key].includes('{0}'))).toEqual([
      true,
      true,
      true,
    ]);
  });
});
