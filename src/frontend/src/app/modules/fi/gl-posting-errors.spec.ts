import { HttpErrorResponse } from '@angular/common/http';
import { ErpLanguage } from '../../core/i18n/languages';
import { translationsOf } from '../../../testing/i18n-fixtures';
import {
  GL_POSTING_ERROR_CODES,
  GL_POSTING_UNCERTAIN_CODES,
  glPostingErrorKey,
  glPostingResultUncertain,
} from './gl-posting-errors';

/** 「沒有過帳任何傳票」的各語言固定字樣；只有確定整筆回滾或被拒時才可出現（BR-FI-028～030）。 */
const NOTHING_POSTED: Record<ErpLanguage, string> = {
  en: 'No journal was posted',
  'zh-Hant': '沒有過帳任何傳票',
  'zh-Hans': '没有过账任何凭证',
};
const ERP_LANGUAGES: readonly ErpLanguage[] = ['zh-Hant', 'zh-Hans', 'en'];

function message(lang: ErpLanguage, code: string): unknown {
  const errors = translationsOf(lang)['errors'] as Record<string, unknown>;
  return code.split('.').reduce<unknown>((node, part) => (node as Record<string, unknown> | undefined)?.[part], errors);
}

function problem(status: number, errorCode?: string): HttpErrorResponse {
  return new HttpErrorResponse({ status, error: errorCode ? { errorCode, traceId: 'trace-1' } : null });
}

describe('FI-GL-POSTING error translations (WU-FI-004a3v)', () => {
  it('whitelists 23 unique FI.GL_POSTING codes', () => {
    expect(GL_POSTING_ERROR_CODES.length).toBe(23);
    expect(new Set(GL_POSTING_ERROR_CODES).size).toBe(GL_POSTING_ERROR_CODES.length);
    for (const code of GL_POSTING_ERROR_CODES) expect(code).toMatch(/^FI\.GL_POSTING\.[A-Z_]+$/);
  });

  it('translates every code in all three languages without leaking the raw code', () => {
    for (const lang of ERP_LANGUAGES) {
      for (const code of GL_POSTING_ERROR_CODES) {
        const text = message(lang, code);
        expect(typeof text, `${lang} ${code}`).toBe('string');
        expect((text as string).trim().length, `${lang} ${code}`).toBeGreaterThan(0);
        expect(text as string).not.toContain('FI.GL_POSTING');
      }
    }
  });

  it('maps known codes to errors.<code> for 409/422/500/503 responses', () => {
    expect(glPostingErrorKey(problem(409, 'FI.GL_POSTING.SOURCE_CONFLICT'))).toBe('errors.FI.GL_POSTING.SOURCE_CONFLICT');
    expect(glPostingErrorKey(problem(422, 'FI.GL_POSTING.PERIOD_CLOSED'))).toBe('errors.FI.GL_POSTING.PERIOD_CLOSED');
    expect(glPostingErrorKey(problem(500, 'FI.GL_POSTING.WRITE_FAILED'))).toBe('errors.FI.GL_POSTING.WRITE_FAILED');
    expect(glPostingErrorKey(problem(503, 'FI.GL_POSTING.RESULT_UNKNOWN'))).toBe('errors.FI.GL_POSTING.RESULT_UNKNOWN');
    expect(glPostingErrorKey(problem(503, 'FI.GL_POSTING.RETRY_EXHAUSTED'))).toBe('errors.FI.GL_POSTING.RETRY_EXHAUSTED');
  });

  it('falls back for unknown codes, network loss, rate limiting and plain 400', () => {
    expect(glPostingErrorKey(problem(500, 'FI.GL_POSTING.NOT_A_CODE'))).toBe('errors.http.unknown');
    expect(glPostingErrorKey(problem(503))).toBe('errors.http.unknown');
    expect(glPostingErrorKey(problem(0))).toBe('errors.http.network');
    expect(glPostingErrorKey(problem(429))).toBe('errors.COMMON.REQUEST.RATE_LIMITED');
    expect(glPostingErrorKey(problem(400))).toBe('errors.COMMON.REQUEST.VALIDATION_FAILED');
    expect(glPostingErrorKey(new Error('boom'))).toBe('errors.http.unknown');
  });

  it('treats only unknown-commit codes as uncertain', () => {
    expect([...GL_POSTING_UNCERTAIN_CODES].sort()).toEqual([
      'FI.GL_POSTING.INVALID_COMMIT',
      'FI.GL_POSTING.INVALID_SAVED_RECEIPT',
      'FI.GL_POSTING.RESULT_UNKNOWN',
      'FI.GL_POSTING.SOURCE_IDENTITY_MISMATCH',
    ]);
    for (const code of GL_POSTING_UNCERTAIN_CODES) expect(GL_POSTING_ERROR_CODES).toContain(code);
    expect(glPostingResultUncertain(problem(503, 'FI.GL_POSTING.RESULT_UNKNOWN'))).toBe(true);
    expect(glPostingResultUncertain(problem(503, 'FI.GL_POSTING.RETRY_EXHAUSTED'))).toBe(false);
    // ADR-107：加入呼叫者交易的過帳失敗一律已回滾，結果確定。
    expect(glPostingResultUncertain(problem(503, 'FI.GL_POSTING.TRANSACTION_ABORTED'))).toBe(false);
    expect(glPostingErrorKey(problem(503, 'FI.GL_POSTING.TRANSACTION_ABORTED'))).toBe('errors.FI.GL_POSTING.TRANSACTION_ABORTED');
    expect(glPostingResultUncertain(problem(0))).toBe(false);
    expect(glPostingResultUncertain(null)).toBe(false);
  });

  it('never claims nothing was posted when the commit result is uncertain', () => {
    for (const lang of ERP_LANGUAGES) {
      for (const code of GL_POSTING_UNCERTAIN_CODES) {
        expect(message(lang, code) as string, `${lang} ${code}`).not.toContain(NOTHING_POSTED[lang]);
      }
    }
  });

  it('states nothing was posted for rolled-back or rejected writes', () => {
    const certain = ['RETRY_EXHAUSTED', 'TRANSACTION_ABORTED', 'WRITE_FAILED', 'UNBALANCED', 'MAPPING_CHANGED', 'ACCOUNT_CHANGED', 'PERIOD_CLOSED']
      .map(c => `FI.GL_POSTING.${c}`);
    for (const lang of ERP_LANGUAGES) {
      for (const code of certain) {
        expect(message(lang, code) as string, `${lang} ${code}`).toContain(NOTHING_POSTED[lang]);
      }
    }
  });

  it('tells the user a timed-out posting can be retried without creating a duplicate', () => {
    expect(message('en', 'FI.GL_POSTING.RESULT_UNKNOWN') as string).toMatch(/duplicate/i);
    expect(message('zh-Hant', 'FI.GL_POSTING.RESULT_UNKNOWN') as string).toContain('重複');
    expect(message('zh-Hans', 'FI.GL_POSTING.RESULT_UNKNOWN') as string).toContain('重复');
  });
});
