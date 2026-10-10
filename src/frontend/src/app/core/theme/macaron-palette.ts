/**
 * 馬卡龍色系設計 token（ui-conventions.md 第 4 節）。
 * 與 src/styles.scss 的 CSS 變數、src/styles/theme/*.less 的 ng-zorro 變數保持一致；
 * 對比度由 macaron-palette.spec.ts 檢查。
 */
export type EffectiveTheme = 'light' | 'dark';

export interface ThemeTokens {
  /** 主色（按鈕底色、連結、選中狀態） */
  primary: string;
  /** 主要按鈕上的文字色 */
  onPrimary: string;
  text: string;
  textSecondary: string;
  bgBody: string;
  bgContainer: string;
  /** 上升 / 正數（KPI 環比） */
  up: string;
  /** 下降 / 負數 */
  down: string;
  /** 狀態標籤底色，文字用 statusText */
  status: Record<'draft' | 'pending' | 'success' | 'error' | 'muted', string>;
  statusText: string;
  /** G2 圖表 8 色序列 */
  chart: readonly string[];
}

export const MACARON_THEMES: Record<EffectiveTheme, ThemeTokens> = {
  light: {
    primary: '#3f6fa3',
    onPrimary: '#ffffff',
    text: '#262626',
    textSecondary: '#595959',
    bgBody: '#f5f7fa',
    bgContainer: '#ffffff',
    up: '#2f7a48',
    down: '#c0504d',
    status: {
      draft: '#e4e7ec',
      pending: '#f9e79f',
      success: '#b5e3b0',
      error: '#f7b7a3',
      muted: '#f0f0f0',
    },
    statusText: '#262626',
    chart: ['#5fb3d4', '#f29e85', '#86c97f', '#d9a93f', '#a68fd6', '#f28fb0', '#6fa8dc', '#f5a96b'],
  },
  dark: {
    primary: '#6fa8dc',
    onPrimary: '#141414',
    text: '#e6e6e6',
    textSecondary: '#a6a6a6',
    bgBody: '#141414',
    bgContainer: '#1f1f1f',
    up: '#8fd18a',
    down: '#f0907a',
    status: {
      draft: '#434343',
      pending: '#6b5d1e',
      success: '#3d6b3a',
      error: '#8a4a3c',
      muted: '#303030',
    },
    statusText: '#f0f0f0',
    chart: ['#7ec8e3', '#f7b7a3', '#b5e3b0', '#f9e79f', '#c3b1e1', '#ffc2d4', '#a8d8ea', '#ffdab9'],
  },
};
