import { TestBed } from '@angular/core/testing';
import { AccessTokenResponse, TokenStore, refreshTokenStorageKey } from './token-store';

const tokens: AccessTokenResponse = {
  tokenType: 'Bearer',
  accessToken: 'access-1',
  expiresIn: 3600,
  refreshToken: 'refresh-1',
};

describe('TokenStore', () => {
  afterEach(() => {
    vi.restoreAllMocks();
    localStorage.clear();
  });

  it('keeps the access token in memory and the refresh token in localStorage', () => {
    const store = TestBed.inject(TokenStore);

    store.setTokens(tokens);

    expect(store.accessToken()).toBe('access-1');
    expect(store.refreshToken()).toBe('refresh-1');
    expect(localStorage.getItem(refreshTokenStorageKey)).toBe('refresh-1');
    expect(JSON.stringify(localStorage)).not.toContain('access-1');
  });

  it('clears both tokens', () => {
    const store = TestBed.inject(TokenStore);
    store.setTokens(tokens);

    store.clear();

    expect(store.accessToken()).toBeNull();
    expect(store.refreshToken()).toBeNull();
    expect(localStorage.getItem(refreshTokenStorageKey)).toBeNull();
  });

  it('keeps working in memory when localStorage throws', () => {
    const failure = () => {
      throw new DOMException('Storage is disabled', 'SecurityError');
    };
    vi.spyOn(Storage.prototype, 'getItem').mockImplementation(failure);
    vi.spyOn(Storage.prototype, 'setItem').mockImplementation(failure);
    vi.spyOn(Storage.prototype, 'removeItem').mockImplementation(failure);
    const store = TestBed.inject(TokenStore);

    expect(store.refreshToken()).toBeNull();

    store.setTokens(tokens);

    expect(store.accessToken()).toBe('access-1');
    expect(store.refreshToken()).toBe('refresh-1');

    store.clear();

    expect(store.accessToken()).toBeNull();
    expect(store.refreshToken()).toBeNull();
  });
});
