import { DOCUMENT } from '@angular/common';
import { TestBed } from '@angular/core/testing';
import { PreferenceStorage } from '../storage/preference-storage';
import { MACARON_THEMES } from './macaron-palette';
import {
  DARK_CLASS,
  DARK_STYLESHEET_HREF,
  DARK_STYLESHEET_ID,
  isThemeMode,
  THEME_STORAGE_KEY,
  ThemeService,
} from './theme.service';

/** 可控制的 prefers-color-scheme（jsdom 沒有 matchMedia）。 */
class FakeMediaQueryList {
  private listeners: ((event: MediaQueryListEvent) => void)[] = [];
  constructor(public matches: boolean) {}
  addEventListener(_: string, listener: (event: MediaQueryListEvent) => void): void {
    this.listeners.push(listener);
  }
  removeEventListener(_: string, listener: (event: MediaQueryListEvent) => void): void {
    this.listeners = this.listeners.filter((l) => l !== listener);
  }
  emit(matches: boolean): void {
    this.matches = matches;
    this.listeners.forEach((l) => l({ matches } as MediaQueryListEvent));
  }
  get listenerCount(): number {
    return this.listeners.length;
  }
}

describe('ThemeService', () => {
  let media: FakeMediaQueryList;
  let document: Document;

  function create(): ThemeService {
    const service = TestBed.inject(ThemeService);
    TestBed.tick();
    return service;
  }

  beforeEach(() => {
    localStorage.clear();
    media = new FakeMediaQueryList(false);
    window.matchMedia = vi.fn().mockReturnValue(media) as unknown as typeof window.matchMedia;
    document = TestBed.inject(DOCUMENT);
  });

  afterEach(() => {
    document.getElementById(DARK_STYLESHEET_ID)?.remove();
    document.documentElement.classList.remove(DARK_CLASS);
    localStorage.clear();
  });

  it('defaults to following the system preference', () => {
    const service = create();

    expect(service.mode()).toBe('system');
    expect(service.effective()).toBe('light');
    expect(service.tokens()).toBe(MACARON_THEMES.light);
    expect(document.documentElement.classList.contains(DARK_CLASS)).toBe(false);
    expect(document.getElementById(DARK_STYLESHEET_ID)).toBeNull();
  });

  it('applies dark mode: html class, dark ng-zorro stylesheet and dark tokens', () => {
    const service = create();

    service.setMode('dark');
    TestBed.tick();

    expect(service.effective()).toBe('dark');
    expect(service.tokens()).toBe(MACARON_THEMES.dark);
    expect(document.documentElement.classList.contains(DARK_CLASS)).toBe(true);
    const link = document.getElementById(DARK_STYLESHEET_ID) as HTMLLinkElement;
    expect(link.rel).toBe('stylesheet');
    expect(link.getAttribute('href')).toBe(DARK_STYLESHEET_HREF);
  });

  it('removes the dark stylesheet when switching back to light', () => {
    const service = create();
    service.setMode('dark');
    TestBed.tick();

    service.setMode('light');
    TestBed.tick();

    expect(document.documentElement.classList.contains(DARK_CLASS)).toBe(false);
    expect(document.getElementById(DARK_STYLESHEET_ID)).toBeNull();
  });

  it('does not add the dark stylesheet twice', () => {
    const service = create();
    service.setMode('dark');
    TestBed.tick();
    service.setMode('system');
    media.emit(true);
    TestBed.tick();

    expect(document.querySelectorAll(`#${DARK_STYLESHEET_ID}`)).toHaveLength(1);
  });

  it('follows system changes while in system mode', () => {
    const service = create();

    media.emit(true);
    TestBed.tick();
    expect(service.effective()).toBe('dark');

    media.emit(false);
    TestBed.tick();
    expect(service.effective()).toBe('light');
  });

  it('ignores system changes when an explicit mode is chosen', () => {
    const service = create();
    service.setMode('light');

    media.emit(true);
    TestBed.tick();

    expect(service.effective()).toBe('light');
  });

  it('persists and restores the chosen mode', () => {
    create().setMode('dark');
    expect(TestBed.inject(PreferenceStorage).get(THEME_STORAGE_KEY)).toBe('dark');

    TestBed.resetTestingModule();
    window.matchMedia = vi.fn().mockReturnValue(media) as unknown as typeof window.matchMedia;
    const restored = create();

    expect(restored.mode()).toBe('dark');
  });

  it('ignores an invalid stored mode', () => {
    TestBed.inject(PreferenceStorage).set(THEME_STORAGE_KEY, 'neon');

    expect(create().mode()).toBe('system');
  });

  it('treats an invalid mode as system', () => {
    const service = create();

    service.setMode('neon' as never);

    expect(service.mode()).toBe('system');
  });

  it('removes the media listener when destroyed', () => {
    create();
    expect(media.listenerCount).toBe(1);

    TestBed.resetTestingModule();

    expect(media.listenerCount).toBe(0);
  });

  it('works without matchMedia (light unless chosen)', () => {
    window.matchMedia = undefined as unknown as typeof window.matchMedia;

    expect(create().effective()).toBe('light');
  });

  it('validates theme modes', () => {
    expect(isThemeMode('dark')).toBe(true);
    expect(isThemeMode('auto')).toBe(false);
  });
});
