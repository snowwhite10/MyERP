import { DOCUMENT } from '@angular/common';
import { computed, DestroyRef, effect, inject, Injectable, signal } from '@angular/core';
import { PreferenceStorage } from '../storage/preference-storage';
import { EffectiveTheme, MACARON_THEMES, ThemeTokens } from './macaron-palette';

/** 用戶選擇的主題模式：亮 / 暗 / 跟隨系統（ui-conventions.md 第 4.2 節）。 */
export type ThemeMode = 'light' | 'dark' | 'system';

export const THEME_MODES: readonly ThemeMode[] = ['light', 'dark', 'system'];
export const THEME_STORAGE_KEY = 'theme';
/** <html> 上的暗色 class，styles.scss 以此切換 CSS 變數。 */
export const DARK_CLASS = 'erp-dark';
/** 暗色 ng-zorro 樣式 bundle（angular.json styles，inject: false）。 */
export const DARK_STYLESHEET_ID = 'erp-theme-dark';
export const DARK_STYLESHEET_HREF = 'theme-dark.css';

const DARK_QUERY = '(prefers-color-scheme: dark)';

export function isThemeMode(value: unknown): value is ThemeMode {
  return THEME_MODES.includes(value as ThemeMode);
}

/**
 * 主題狀態與套用：
 * - mode：用戶選擇（存本機偏好）；effective：實際亮 / 暗（system 時跟隨 prefers-color-scheme）。
 * - effective 改變時切換 <html class="erp-dark">、插入 / 移除暗色 ng-zorro 樣式。
 * - 圖表元件讀取 tokens() 重新渲染（G2 主題同步）。
 */
@Injectable({ providedIn: 'root' })
export class ThemeService {
  private readonly document = inject(DOCUMENT);
  private readonly storage = inject(PreferenceStorage);

  private readonly modeSignal = signal<ThemeMode>(this.readStoredMode());
  private readonly systemPrefersDark = signal(false);

  readonly mode = this.modeSignal.asReadonly();
  readonly effective = computed<EffectiveTheme>(() => {
    const mode = this.modeSignal();
    if (mode === 'system') {
      return this.systemPrefersDark() ? 'dark' : 'light';
    }
    return mode;
  });
  readonly tokens = computed<ThemeTokens>(() => MACARON_THEMES[this.effective()]);

  constructor() {
    this.watchSystemPreference();
    effect(() => this.apply(this.effective()));
  }

  setMode(mode: ThemeMode): void {
    const next = isThemeMode(mode) ? mode : 'system';
    this.storage.set(THEME_STORAGE_KEY, next);
    this.modeSignal.set(next);
  }

  private readStoredMode(): ThemeMode {
    const stored = this.storage.get(THEME_STORAGE_KEY);
    return isThemeMode(stored) ? stored : 'system';
  }

  private watchSystemPreference(): void {
    const media = this.document.defaultView?.matchMedia?.(DARK_QUERY);
    if (!media) {
      return;
    }
    this.systemPrefersDark.set(media.matches);
    const listener = (event: MediaQueryListEvent) => this.systemPrefersDark.set(event.matches);
    media.addEventListener('change', listener);
    inject(DestroyRef).onDestroy(() => media.removeEventListener('change', listener));
  }

  private apply(theme: EffectiveTheme): void {
    const root = this.document.documentElement;
    root.classList.toggle(DARK_CLASS, theme === 'dark');

    const existing = this.document.getElementById(DARK_STYLESHEET_ID);
    if (theme === 'dark' && !existing) {
      const link = this.document.createElement('link');
      link.id = DARK_STYLESHEET_ID;
      link.rel = 'stylesheet';
      link.href = DARK_STYLESHEET_HREF;
      this.document.head.appendChild(link);
    } else if (theme === 'light' && existing) {
      existing.remove();
    }
  }
}
