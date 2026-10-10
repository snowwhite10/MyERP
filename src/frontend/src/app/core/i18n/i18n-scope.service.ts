import { DestroyRef, inject, Injectable } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { CanActivateFn } from '@angular/router';
import { TranslateService } from '@ngx-translate/core';
import { map, Observable, of, tap } from 'rxjs';
import { ScopedTranslateLoader } from './scoped-translate-loader';

/**
 * 業務模組語言檔（範圍 = 模組 prefix）的延遲載入。
 * 模組路由加上 `canActivate: [i18nScopeGuard('sal')]`，進入模組前載入目前語言的 sal.json；
 * 之後切換語言時自動補載已登記的範圍。
 */
@Injectable({ providedIn: 'root' })
export class I18nScopeService {
  private readonly translate = inject(TranslateService);
  private readonly loader = inject(ScopedTranslateLoader);
  private readonly loaded = new Set<string>();

  constructor() {
    this.translate.onLangChange
      .pipe(takeUntilDestroyed(inject(DestroyRef)))
      .subscribe(({ lang }) => this.loadMissing(lang).subscribe());
  }

  load(scope: string): Observable<void> {
    this.loader.registerScope(scope);
    const lang = this.translate.getCurrentLang() ?? this.translate.getFallbackLang();
    if (!lang) {
      return of(undefined);
    }
    return this.loadMissing(lang);
  }

  private loadMissing(lang: string): Observable<void> {
    const translations = this.translate.getTranslations(lang) as Record<string, unknown>;
    const missing = this.loader.registeredScopes.filter(
      (scope) => !this.loaded.has(`${lang}/${scope}`) && translations?.[scope] === undefined,
    );
    if (missing.length === 0) {
      return of(undefined);
    }
    return this.loader.fetchScopes(lang, missing).pipe(
      tap((content) => {
        this.translate.setTranslation(lang, content, true);
        missing.forEach((scope) => this.loaded.add(`${lang}/${scope}`));
      }),
      map(() => undefined),
    );
  }
}

export function i18nScopeGuard(scope: string): CanActivateFn {
  return () =>
    inject(I18nScopeService)
      .load(scope)
      .pipe(map(() => true));
}
