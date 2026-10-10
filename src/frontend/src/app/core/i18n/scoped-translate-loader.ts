import { HttpClient } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { TranslateLoader, TranslationObject } from '@ngx-translate/core';
import { catchError, forkJoin, map, Observable, of } from 'rxjs';
import { CORE_I18N_SCOPES } from './languages';

/** 語言檔根路徑（public/i18n，建置後位於網站根目錄 /i18n）。 */
export const I18N_BASE_URL = 'i18n';

const SCOPE_NAME = /^[a-z][a-z0-9-]*$/;

export function assertValidScope(scope: string): void {
  if (!SCOPE_NAME.test(scope)) {
    throw new Error(`Invalid i18n scope "${scope}"`);
  }
}

/**
 * 按範圍載入語言檔：`i18n/{lang}/{scope}.json` 的內容放在 `{scope}` 節點下，
 * 因此 key 一律以範圍開頭（例如 common.action.save，ui-conventions.md 第 3 節）。
 * 單一檔案載入失敗時以空物件代替（缺字時顯示 key），不阻擋整個語言。
 */
@Injectable({ providedIn: 'root' })
export class ScopedTranslateLoader implements TranslateLoader {
  private readonly http = inject(HttpClient);
  private readonly scopes = new Set<string>(CORE_I18N_SCOPES);

  /** 已登記的範圍（啟動範圍 + 已載入的模組範圍）。切換語言時全部載入。 */
  get registeredScopes(): readonly string[] {
    return [...this.scopes];
  }

  /** 登記範圍；回傳 true 表示新登記。 */
  registerScope(scope: string): boolean {
    assertValidScope(scope);
    if (this.scopes.has(scope)) {
      return false;
    }
    this.scopes.add(scope);
    return true;
  }

  getTranslation(lang: string): Observable<TranslationObject> {
    return this.fetchScopes(lang, this.registeredScopes);
  }

  fetchScopes(lang: string, scopes: readonly string[]): Observable<TranslationObject> {
    if (scopes.length === 0) {
      return of({});
    }
    scopes.forEach(assertValidScope);
    return forkJoin(
      scopes.map((scope) =>
        this.http.get<TranslationObject>(`${I18N_BASE_URL}/${lang}/${scope}.json`).pipe(
          catchError(() => of({} as TranslationObject)),
          map((content) => ({ [scope]: content }) as TranslationObject),
        ),
      ),
    ).pipe(map((parts) => Object.assign({}, ...parts) as TranslationObject));
  }
}
