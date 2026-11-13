import { apiErrorKey } from '../../core/http/api-error';

/**
 * FI sales invoice posting error codes (WU-FI-002a4c1). Must equal the backend `SalesPostingErrors` constants; checked by
 * ArchitectureTests `SalesPostingErrorTranslationTests`, translations live in `errors.json` under `FI.SALES_POSTING`.
 * SD screens that post invoices use {@link salesPostingErrorKey} so a refusal is never shown as an unknown error.
 */
export const SALES_POSTING_ERROR_CODES = [
  'FI.SALES_POSTING.INVALID_INPUT',
  'FI.SALES_POSTING.SOURCE_NOT_FOUND',
  'FI.SALES_POSTING.SOURCE_NOT_POSTABLE',
  'FI.SALES_POSTING.SOURCE_MISMATCH',
  'FI.SALES_POSTING.SOURCE_CONFLICT',
  'FI.SALES_POSTING.INVALID_AMOUNTS',
  'FI.SALES_POSTING.MAPPING_MISSING',
  'FI.SALES_POSTING.ACCOUNT_INVALID',
  'FI.SALES_POSTING.SOURCE_CONTRACT_UNAVAILABLE',
  'FI.SALES_POSTING.INCONSISTENT_STATE',
  'FI.SALES_POSTING.WRITE_FAILED',
] as const;

export type SalesPostingErrorCode = (typeof SALES_POSTING_ERROR_CODES)[number];

/** Sales posting API error → i18n key; unknown codes use the generic message, never the raw code. */
export function salesPostingErrorKey(error: unknown): string {
  return apiErrorKey(error, SALES_POSTING_ERROR_CODES);
}
