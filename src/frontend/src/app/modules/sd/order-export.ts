import { HttpErrorResponse, HttpResponse } from '@angular/common/http';
import { TimeoutError } from 'rxjs';
import { apiErrorTraceId, apiValidationDetails, NETWORK_ERROR_KEY, RATE_LIMITED_ERROR_KEY } from '../../core/http/api-error';
import { countCsvDataRows } from '../../shared/export/csv-export';

export type OrderExportOutcome = { kind: 'saved'; count: number; blob: Blob } | { kind: 'empty' } | { kind: 'invalid' };
export interface OrderExportFailure { key: string; traceId: string | null; details: string[] }

/**
 * ADR-141：X-Export-Row-Count 必須是非負整數，且與檔案實際資料列數一致才下載；
 * 0 列只有表頭，頁面以警告呈現而不下載。
 */
export async function checkOrderExport(response: HttpResponse<Blob>): Promise<OrderExportOutcome> {
  const raw = response.headers.get('X-Export-Row-Count');
  const count = raw !== null && /^\d{1,9}$/.test(raw) ? Number(raw) : Number.NaN;
  if (!Number.isSafeInteger(count) || !response.body) return { kind: 'invalid' };
  let rows: number | null;
  try { rows = countCsvDataRows(await response.body.text()); } catch { return { kind: 'invalid' }; }
  if (rows === null || rows !== count) return { kind: 'invalid' };
  return count === 0 ? { kind: 'empty' } : { kind: 'saved', count, blob: response.body };
}

/** 匯出錯誤轉成三語 key：權限、上限、條件無效、逾時、網絡與 5xx 追蹤碼分開說明。 */
export function orderExportFailure(error: unknown): OrderExportFailure {
  const result = (key: string, details: string[] = []): OrderExportFailure =>
    ({ key, traceId: apiErrorTraceId(error), details });
  if (error instanceof TimeoutError) return result('sd.order.exportTimeout');
  if (!(error instanceof HttpErrorResponse)) return result('sd.order.exportFailed');
  if (error.status === 0) return result(NETWORK_ERROR_KEY);
  if (error.status === 429) return result(RATE_LIMITED_ERROR_KEY);
  if (error.status === 403) return result('sd.order.exportDenied');
  const code = (error.error as { errorCode?: unknown } | null)?.errorCode;
  if (error.status === 422 && code === 'SAL.ORDER.EXPORT_LIMIT') return result('sd.order.exportLimit');
  if (error.status === 400) return result('sd.order.exportInvalid', apiValidationDetails(error));
  return result('sd.order.exportFailed');
}
