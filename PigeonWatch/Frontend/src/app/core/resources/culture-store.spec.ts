import { TestBed } from '@angular/core/testing';
import { CultureStore, SUPPORTED_CULTURES, cultureStorageKey } from './culture-store';

describe('CultureStore', () => {
  let store: CultureStore;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [{ provide: SUPPORTED_CULTURES, useValue: ['en', 'pl'] }],
    });
    store = TestBed.inject(CultureStore);
  });

  afterEach(() => {
    vi.restoreAllMocks();
    localStorage.clear();
  });

  it('returns en when nothing is saved', () => {
    expect(store.selected()).toBe('en');
  });

  it('returns en for an unsupported culture', () => {
    localStorage.setItem(cultureStorageKey, 'xx');

    expect(store.selected()).toBe('en');
  });

  it('returns en when localStorage throws', () => {
    vi.spyOn(Storage.prototype, 'getItem').mockImplementation(() => {
      throw new DOMException('Storage is disabled', 'SecurityError');
    });

    expect(store.selected()).toBe('en');
  });

  it('returns a saved supported culture', () => {
    store.save('pl');

    expect(localStorage.getItem(cultureStorageKey)).toBe('pl');
    expect(store.selected()).toBe('pl');
  });

  it('does not throw when saving fails', () => {
    vi.spyOn(Storage.prototype, 'setItem').mockImplementation(() => {
      throw new DOMException('Quota exceeded', 'QuotaExceededError');
    });

    expect(() => store.save('pl')).not.toThrow();
  });
});
