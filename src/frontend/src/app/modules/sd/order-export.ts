import { HttpErrorResponse } from '@angular/common/http';
import { TimeoutError } from 'rxjs';
import { apiErrorTraceId, apiValidationDetails, NETWORK_ERROR_KEY, RATE_LIMITED_ERROR_KEY } from '../../core/http/api-error';
import { checkCsvExport, CsvExportOutcome } from '../../shared/export/csv-export';

export type OrderExportOutcome = CsvExportOutcome;
export interface OrderExportFailure { key: string; traceId: string | null; details: string[] }

/** ADR-141 行數核對；邏輯已移至共用 `checkCsvExport`（ADR-203），保留原名供 SD 頁面使用。 */
export const checkOrderExport = checkCsvExport;

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
