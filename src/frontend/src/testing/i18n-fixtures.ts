// 測試專用：直接匯入語言檔（不經 HTTP），供三語 key 一致性檢查與元件測試使用。
// 新增語言檔範圍（例如模組 sal）時，必須在此登記三種語言。
import { EnvironmentProviders, Provider } from '@angular/core';
import {
  provideTranslateLoader,
  provideTranslateService,
  TranslateLoader,
  TranslationObject,
} from '@ngx-translate/core';
import { provideNzI18n, zh_TW } from 'ng-zorro-antd/i18n';
import { Observable, of } from 'rxjs';
import enCommon from '../../public/i18n/en/common.json';
import enErrors from '../../public/i18n/en/errors.json';
import enLayout from '../../public/i18n/en/layout.json';
import enMd from '../../public/i18n/en/md.json';
import enSys from '../../public/i18n/en/sys.json';
import hansCommon from '../../public/i18n/zh-Hans/common.json';
import hansErrors from '../../public/i18n/zh-Hans/errors.json';
import hansLayout from '../../public/i18n/zh-Hans/layout.json';
import hansMd from '../../public/i18n/zh-Hans/md.json';
import hansSys from '../../public/i18n/zh-Hans/sys.json';
import hantCommon from '../../public/i18n/zh-Hant/common.json';
import hantErrors from '../../public/i18n/zh-Hant/errors.json';
import hantLayout from '../../public/i18n/zh-Hant/layout.json';
import hantMd from '../../public/i18n/zh-Hant/md.json';
import hantSys from '../../public/i18n/zh-Hant/sys.json';
import { ErpLanguage } from '../app/core/i18n/languages';

export const I18N_FIXTURES: Record<ErpLanguage, Record<string, TranslationObject>> = {
  'zh-Hant': { common: hantCommon, layout: hantLayout, errors: hantErrors, md: hantMd, sys: hantSys },
  'zh-Hans': { common: hansCommon, layout: hansLayout, errors: hansErrors, md: hansMd, sys: hansSys },
  en: { common: enCommon, layout: enLayout, errors: enErrors, md: enMd, sys: enSys },
};

/** 攤平成 `scope.a.b` 形式的 key 清單（排序）。 */
export function flattenKeys(node: unknown, prefix = ''): string[] {
  if (node === null || typeof node !== 'object' || Array.isArray(node)) {
    return [prefix];
  }
  return Object.entries(node as Record<string, unknown>)
    .flatMap(([key, value]) => flattenKeys(value, prefix ? `${prefix}.${key}` : key))
    .sort();
}

export function translationsOf(lang: ErpLanguage): TranslationObject {
  return I18N_FIXTURES[lang] as unknown as TranslationObject;
}

/** 同步回傳 fixture 的 loader（of() 同步發出，TranslateService 建立後即可翻譯）。 */
export class FixtureTranslateLoader implements TranslateLoader {
  getTranslation(lang: string): Observable<TranslationObject> {
    return of(translationsOf(lang as ErpLanguage) ?? {});
  }
}

/** 元件測試用：TranslateService 已載入指定語言（不需 HTTP）+ ng-zorro 語系。 */
export function provideTestTranslations(
  lang: ErpLanguage = 'zh-Hant',
): (Provider | EnvironmentProviders)[] {
  return [
    provideTranslateService({
      loader: provideTranslateLoader(FixtureTranslateLoader),
      lang,
      fallbackLang: lang,
    }),
    provideNzI18n(zh_TW),
  ];
}
