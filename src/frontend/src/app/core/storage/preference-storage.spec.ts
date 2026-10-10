import { TestBed } from '@angular/core/testing';
import { PREFERENCE_KEY_PREFIX, PreferenceStorage } from './preference-storage';

describe('PreferenceStorage', () => {
  let storage: PreferenceStorage;

  beforeEach(() => {
    localStorage.clear();
    storage = TestBed.inject(PreferenceStorage);
  });

  afterEach(() => {
    vi.restoreAllMocks();
    localStorage.clear();
  });

  it('stores values in localStorage with the myerp prefix', () => {
    storage.set('lang', 'en');

    expect(localStorage.getItem(PREFERENCE_KEY_PREFIX + 'lang')).toBe('en');
    expect(storage.get('lang')).toBe('en');
  });

  it('returns null for unknown keys', () => {
    expect(storage.get('missing')).toBeNull();
  });

  it('removes values', () => {
    storage.set('theme', 'dark');
    storage.remove('theme');

    expect(storage.get('theme')).toBeNull();
    expect(localStorage.getItem(PREFERENCE_KEY_PREFIX + 'theme')).toBeNull();
  });

  it('falls back to memory when localStorage throws', () => {
    vi.spyOn(Storage.prototype, 'setItem').mockImplementation(() => {
      throw new Error('QuotaExceededError');
    });
    vi.spyOn(Storage.prototype, 'getItem').mockImplementation(() => {
      throw new Error('SecurityError');
    });
    vi.spyOn(Storage.prototype, 'removeItem').mockImplementation(() => {
      throw new Error('SecurityError');
    });

    storage.set('lang', 'zh-Hans');
    expect(storage.get('lang')).toBe('zh-Hans');

    storage.remove('lang');
    expect(storage.get('lang')).toBeNull();
  });
});
