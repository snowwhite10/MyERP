import { computed, inject, Signal } from '@angular/core';
import { TranslateService } from '@ngx-translate/core';
import { LanguageService } from '../../core/i18n/language.service';
import { ThemeService } from '../../core/theme/theme.service';
import { ChartContext } from './chart-specs';

/**
 * 圖表共用上下文：主題（亮 / 暗、馬卡龍色盤）、語言（數字格式、翻譯）。
 * 任一改變時 computed 重新計算，圖表元件據此重新渲染（ui-conventions.md 第 4.2 節）。
 * 須在注入上下文（元件建構）中呼叫。
 */
export function injectChartContext(): Signal<ChartContext> {
  const theme = inject(ThemeService);
  const language = inject(LanguageService);
  const translate = inject(TranslateService);

  return computed<ChartContext>(() => {
    const lang = language.current();
    const numberFormat = new Intl.NumberFormat(lang, { maximumFractionDigits: 2 });
    return {
      theme: theme.effective(),
      tokens: theme.tokens(),
      formatNumber: (value) => numberFormat.format(value),
      translate: (key) => (key ? String(translate.instant(key)) : key),
    };
  });
}
