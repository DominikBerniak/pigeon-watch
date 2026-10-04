import { safeReturnUrl } from './safe-return-url';

describe('safeReturnUrl', () => {
  it.each(['//evil.com', '/\\evil.com', 'https://evil.com', 'evil.com', '', null, undefined])(
    'rejects %s',
    (value) => {
      expect(safeReturnUrl(value)).toBe('/');
    },
  );

  it.each(['/', '/a?b=1', '/sightings/1', '/register?returnUrl=%2Fa'])('keeps %s', (value) => {
    expect(safeReturnUrl(value)).toBe(value);
  });
});
