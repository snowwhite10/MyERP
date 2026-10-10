import { en_US, zh_CN, zh_TW } from 'ng-zorro-antd/i18n';
import {
  DEFAULT_LANGUAGE,
  detectLanguage,
  getLanguageDefinition,
  isErpLanguage,
  LANGUAGES,
} from './languages';

describe('languages', () => {
  it('supports zh-Hant (default), zh-Hans and en', () => {
    expect(LANGUAGES.map((l) => l.code)).toEqual(['zh-Hant', 'zh-Hans', 'en']);
    expect(DEFAULT_LANGUAGE).toBe('zh-Hant');
  });

  it('maps each language to its ng-zorro locale', () => {
    expect(getLanguageDefinition('zh-Hant').nzLocale).toBe(zh_TW);
    expect(getLanguageDefinition('zh-Hans').nzLocale).toBe(zh_CN);
    expect(getLanguageDefinition('en').nzLocale).toBe(en_US);
  });

  it('falls back to the default definition for unknown codes', () => {
    expect(getLanguageDefinition('fr' as never).code).toBe('zh-Hant');
  });

  it('validates language codes', () => {
    expect(isErpLanguage('en')).toBe(true);
    expect(isErpLanguage('zh-TW')).toBe(false);
    expect(isErpLanguage(null)).toBe(false);
  });

  describe('detectLanguage', () => {
    it.each([
      [['zh-TW'], 'zh-Hant'],
      [['zh-HK'], 'zh-Hant'],
      [['zh-Hant-MO'], 'zh-Hant'],
      [['zh-CN'], 'zh-Hans'],
      [['zh-SG'], 'zh-Hans'],
      [['zh'], 'zh-Hans'],
      [['en-US'], 'en'],
      [['en'], 'en'],
      [['fr-FR', 'en-GB'], 'en'],
      [['ja-JP'], 'zh-Hant'],
      [[], 'zh-Hant'],
    ] as const)('%j -> %s', (browser, expected) => {
      expect(detectLanguage(browser)).toBe(expected);
    });

    it('does not treat languages that merely start with "en" as English', () => {
      expect(detectLanguage(['eno'])).toBe('zh-Hant');
    });
  });
});
