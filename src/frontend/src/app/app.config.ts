import { provideHttpClient, withFetch, withInterceptors } from '@angular/common/http';
import {
  ApplicationConfig,
  inject,
  provideAppInitializer,
  provideBrowserGlobalErrorListeners,
} from '@angular/core';
import { provideRouter, withComponentInputBinding } from '@angular/router';
import { provideTranslateLoader, provideTranslateService } from '@ngx-translate/core';
import { provideNzI18n, zh_TW } from 'ng-zorro-antd/i18n';
import { provideNzIcons } from 'ng-zorro-antd/icon';
import { routes } from './app.routes';
import { apiHeadersInterceptor } from './core/http/api-headers.interceptor';
import { LanguageService } from './core/i18n/language.service';
import { DEFAULT_LANGUAGE } from './core/i18n/languages';
import { ScopedTranslateLoader } from './core/i18n/scoped-translate-loader';
import { APP_ICONS } from './core/icons';
import { ThemeService } from './core/theme/theme.service';

export const appConfig: ApplicationConfig = {
  providers: [
    provideBrowserGlobalErrorListeners(),
    provideRouter(routes, withComponentInputBinding()),
    provideHttpClient(withFetch(), withInterceptors([apiHeadersInterceptor])),
    provideTranslateService({
      // 與 I18nScopeService 共用同一個 loader 實例（登記的模組範圍才會在切換語言時一併載入）
      loader: provideTranslateLoader(() => inject(ScopedTranslateLoader)),
      fallbackLang: DEFAULT_LANGUAGE,
    }),
    provideNzI18n(zh_TW),
    provideNzIcons(APP_ICONS),
    provideAppInitializer(() => {
      inject(ThemeService);
      return inject(LanguageService).init();
    }),
  ],
};
