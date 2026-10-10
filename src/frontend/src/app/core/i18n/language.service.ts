import { DOCUMENT } from '@angular/common';
import { inject, Injectable, signal } from '@angular/core';
import { TranslateService } from '@ngx-translate/core';
import { NzI18nService } from 'ng-zorro-antd/i18n';
import { map, Observable, tap } from 'rxjs';
import { PreferenceStorage } from '../storage/preference-storage';
import {
  DEFAULT_LANGUAGE,
  detectLanguage,
  ErpLanguage,
  getLanguageDefinition,
  isErpLanguage,
  LANGUAGES,
} from './languages';

export const LANGUAGE_STORAGE_KEY = 'lang';

/** 執行期語言切換：同步 ngx-translate、ng-zorro 元件語系、<html lang> 與本機偏好。 */
@Injectable({ providedIn: 'root' })
export class LanguageService {
  private readonly translate = inject(TranslateService);
  private readonly nzI18n = inject(NzI18nService);
  private readonly storage = inject(PreferenceStorage);
  private readonly document = inject(DOCUMENT);

  readonly languages = LANGUAGES;
  private readonly currentLanguage = signal<ErpLanguage>(DEFAULT_LANGUAGE);
  readonly current = this.currentLanguage.asReadonly();

  /** 啟動時呼叫：本機偏好 → 瀏覽器語言 → 預設繁體。 */
  init(): Observable<void> {
    this.translate.setFallbackLang(DEFAULT_LANGUAGE);
    const stored = this.storage.get(LANGUAGE_STORAGE_KEY);
    const initial = isErpLanguage(stored)
      ? stored
      : detectLanguage(this.document.defaultView?.navigator.languages ?? []);
    return this.use(initial);
  }

  use(language: ErpLanguage): Observable<void> {
    const code = isErpLanguage(language) ? language : DEFAULT_LANGUAGE;
    return this.translate.use(code).pipe(
      tap(() => {
        this.nzI18n.setLocale(getLanguageDefinition(code).nzLocale);
        this.document.documentElement.lang = code;
        this.storage.set(LANGUAGE_STORAGE_KEY, code);
        this.currentLanguage.set(code);
      }),
      map(() => undefined),
    );
  }
}
