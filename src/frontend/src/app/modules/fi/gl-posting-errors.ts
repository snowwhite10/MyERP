import { HttpErrorResponse } from '@angular/common/http';
import { apiErrorKey } from '../../core/http/api-error';

/**
 * FI 過帳錯誤代碼白名單（WU-FI-004a3v）。必須與後端 `GlPostingErrors` 常數完全一致，
 * 由 ArchitectureTests `GlPostingErrorTranslationTests` 比對；翻譯在 `errors.json` 的 `FI.GL_POSTING`。
 */
export const GL_POSTING_ERROR_CODES = [
  'FI.GL_POSTING.INVALID_INPUT',
  'FI.GL_POSTING.INVALID_SOURCE',
  'FI.GL_POSTING.SOURCE_NOT_FOUND',
  'FI.GL_POSTING.SOURCE_CONFLICT',
  'FI.GL_POSTING.MAPPING_CHANGED',
  'FI.GL_POSTING.ACCOUNT_CHANGED',
  'FI.GL_POSTING.BASE_CURRENCY_INVALID',
  'FI.GL_POSTING.CURRENCY_MISMATCH',
  'FI.GL_POSTING.PERIOD_MISSING',
  'FI.GL_POSTING.PERIOD_CLOSED',
  'FI.GL_POSTING.PERIOD_AMBIGUOUS',
  'FI.GL_POSTING.ACCOUNT_INVALID',
  'FI.GL_POSTING.UNBALANCED',
  'FI.GL_POSTING.INVALID_DRAFT',
  'FI.GL_POSTING.INVALID_SAVED_RECEIPT',
  'FI.GL_POSTING.SOURCE_IDENTITY_MISMATCH',
  'FI.GL_POSTING.INVALID_DECISION',
  'FI.GL_POSTING.INVALID_WRITE_PLAN',
  'FI.GL_POSTING.INVALID_COMMIT',
  'FI.GL_POSTING.RESULT_UNKNOWN',
  'FI.GL_POSTING.RETRY_EXHAUSTED',
  'FI.GL_POSTING.WRITE_FAILED',
] as const;

export type GlPostingErrorCode = (typeof GL_POSTING_ERROR_CODES)[number];

/**
 * 提交結果未確定的代碼（BR-FI-028）：可能已有傳票，畫面不得宣稱「沒有過帳」，
 * 應重新載入來源單據查看是否已有傳票；重送同一來源不會產生重複傳票（004a3n 零變更重放）。
 */
export const GL_POSTING_UNCERTAIN_CODES: readonly GlPostingErrorCode[] = [
  'FI.GL_POSTING.RESULT_UNKNOWN',
  'FI.GL_POSTING.INVALID_SAVED_RECEIPT',
  'FI.GL_POSTING.SOURCE_IDENTITY_MISMATCH',
  'FI.GL_POSTING.INVALID_COMMIT',
];

function errorCodeOf(error: unknown): unknown {
  return error instanceof HttpErrorResponse ? (error.error as { errorCode?: unknown } | null)?.errorCode : undefined;
}

/** 過帳 API 錯誤 → i18n key；未知代碼回通用錯誤，不顯示原始代碼。 */
export function glPostingErrorKey(error: unknown): string {
  return apiErrorKey(error, GL_POSTING_ERROR_CODES);
}

/** 錯誤是否代表提交結果未確定（畫面須提示重新載入，而不是「沒有過帳」）。 */
export function glPostingResultUncertain(error: unknown): boolean {
  const code = errorCodeOf(error);
  return typeof code === 'string' && (GL_POSTING_UNCERTAIN_CODES as readonly string[]).includes(code);
}
