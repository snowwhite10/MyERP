import { en_US, NzI18nInterface, zh_CN, zh_TW } from 'ng-zorro-antd/i18n';

/** 支援語言（BCP 47），與後端 Accept-Language 相同。 */
export type ErpLanguage = 'zh-Hant' | 'zh-Hans' | 'en';

export interface LanguageDefinition {
  code: ErpLanguage;
  /** 語言選單顯示名稱（以該語言本身書寫，不翻譯） */
  nativeName: string;
  /** ng-zorro 元件語系 */
  nzLocale: NzI18nInterface;
}

export const DEFAULT_LANGUAGE: ErpLanguage = 'zh-Hant';

export const LANGUAGES: readonly LanguageDefinition[] = [
  { code: 'zh-Hant', nativeName: '繁體中文', nzLocale: zh_TW },
  { code: 'zh-Hans', nativeName: '简体中文', nzLocale: zh_CN },
  { code: 'en', nativeName: 'English', nzLocale: en_US },
];

/** 啟動即載入的語言檔範圍；各業務模組的範圍（模組 prefix）由 I18nScopeLoader 於 lazy route 載入。 */
export const CORE_I18N_SCOPES = ['common', 'layout', 'errors'] as const;

export function isErpLanguage(value: unknown): value is ErpLanguage {
  return LANGUAGES.some((l) => l.code === value);
}

export function getLanguageDefinition(code: ErpLanguage): LanguageDefinition {
  return LANGUAGES.find((l) => l.code === code) ?? LANGUAGES[0];
}

/**
 * 依瀏覽器語言推斷預設語言：zh-TW / zh-HK / zh-MO / zh-Hant → 繁體；zh-CN / zh-SG / zh-Hans / zh → 簡體；
 * en-* → English；其他 → 預設繁體。
 */
export function detectLanguage(browserLanguages: readonly string[]): ErpLanguage {
  for (const raw of browserLanguages) {
    const tag = raw.toLowerCase();
    if (tag.startsWith('zh')) {
      return /hant|tw|hk|mo/.test(tag) ? 'zh-Hant' : 'zh-Hans';
    }
    if (tag === 'en' || tag.startsWith('en-')) {
      return 'en';
    }
  }
  return DEFAULT_LANGUAGE;
}
