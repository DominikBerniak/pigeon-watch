export interface SightingAge {
  key: string;
  value: number;
}

const minuteMilliseconds = 60_000;
const hourMilliseconds = 60 * minuteMilliseconds;
const dayMilliseconds = 24 * hourMilliseconds;

export function sightingAge(createdAt: string, now: Date): SightingAge {
  const elapsed = now.getTime() - new Date(createdAt).getTime();

  if (Number.isNaN(elapsed) || elapsed < minuteMilliseconds) {
    return { key: 'sighting.age.justNow', value: 0 };
  }

  if (elapsed < hourMilliseconds) {
    return { key: 'sighting.age.minutes', value: Math.floor(elapsed / minuteMilliseconds) };
  }

  if (elapsed < dayMilliseconds) {
    return { key: 'sighting.age.hours', value: Math.floor(elapsed / hourMilliseconds) };
  }

  return { key: 'sighting.age.days', value: Math.floor(elapsed / dayMilliseconds) };
}
