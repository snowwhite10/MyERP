// REQ_NF423.3：三語語言檔 key 必須完全一致，且不得有空字串。
import { flattenKeys, I18N_FIXTURES } from '../../../testing/i18n-fixtures';
import { CORE_I18N_SCOPES, DEFAULT_LANGUAGE, ErpLanguage, LANGUAGES } from './languages';

const KEY_SEGMENT = /^[a-z][a-zA-Z0-9]*$|^[A-Z][A-Z0-9_]*$/;

function leafValues(node: unknown): unknown[] {
  if (node === null || typeof node !== 'object') {
    return [node];
  }
  return Object.values(node as Record<string, unknown>).flatMap(leafValues);
}

describe('i18n language files', () => {
  const languages = LANGUAGES.map((l) => l.code);
  const reference = I18N_FIXTURES[DEFAULT_LANGUAGE];

  it('registers every supported language', () => {
    expect(Object.keys(I18N_FIXTURES).sort()).toEqual([...languages].sort());
  });

  it('provides every core scope', () => {
    for (const lang of languages) {
      for (const scope of CORE_I18N_SCOPES) {
        expect(I18N_FIXTURES[lang][scope], `${lang}/${scope}.json`).toBeDefined();
      }
    }
  });

  for (const scope of Object.keys(reference)) {
    describe(scope, () => {
      const expected = flattenKeys(reference[scope]);

      for (const lang of languages.filter((l) => l !== DEFAULT_LANGUAGE) as ErpLanguage[]) {
        it(`${lang} has exactly the same keys as ${DEFAULT_LANGUAGE}`, () => {
          const actual = flattenKeys(I18N_FIXTURES[lang][scope]);
          const missing = expected.filter((k) => !actual.includes(k));
          const extra = actual.filter((k) => !expected.includes(k));
          expect({ missing, extra }).toEqual({ missing: [], extra: [] });
        });
      }

      it('uses camelCase key segments (error codes may be UPPER_CASE)', () => {
        const invalid = expected
          .flatMap((key) => key.split('.'))
          .filter((segment) => !KEY_SEGMENT.test(segment));
        expect(invalid).toEqual([]);
      });

      it('has only non-empty string values in every language', () => {
        for (const lang of languages) {
          const values = leafValues(I18N_FIXTURES[lang][scope]);
          expect(values.every((v) => typeof v === 'string' && v.trim().length > 0), lang).toBe(true);
        }
      });
    });
  }
});
