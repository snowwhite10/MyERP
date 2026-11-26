import { HttpErrorResponse } from '@angular/common/http';
import { TimeoutError } from 'rxjs';
import { apiErrorTraceId, apiValidationDetails, NETWORK_ERROR_KEY, RATE_LIMITED_ERROR_KEY } from '../../core/http/api-error';

/** File name written by the server's Content-Disposition (ADR-202); the page saves under the same name. */
export const PURCHASE_INVOICE_EXPORT_FILE = 'purchase-invoices.csv';
export interface PurchaseInvoiceExportFailure { key: string; traceId: string | null; details: string[] }

/**
 * 介面 4.19 export errors as i18n keys (WU-PUR-008c3c2): permission, the 10,000-line limit (shared errors key),
 * invalid filters with the server's field reasons, timeout, network, rate limit; 5xx keeps the server trace ID.
 */
export function purchaseInvoiceExportFailure(error: unknown): PurchaseInvoiceExportFailure {
  const result = (key: string, details: string[] = []): PurchaseInvoiceExportFailure =>
    ({ key, traceId: apiErrorTraceId(error), details });
  if (error instanceof TimeoutError) return result('pur.invoice.exportTimeout');
  if (!(error instanceof HttpErrorResponse)) return result('pur.invoice.exportFailed');
  if (error.status === 0) return result(NETWORK_ERROR_KEY);
  if (error.status === 429) return result(RATE_LIMITED_ERROR_KEY);
  if (error.status === 403) return result('pur.invoice.exportDenied');
  const code = (error.error as { errorCode?: unknown } | null)?.errorCode;
  if (error.status === 422 && code === 'PUR.INVOICE.EXPORT_LIMIT') return result('errors.PUR.INVOICE.EXPORT_LIMIT');
  if (error.status === 400) return result('pur.invoice.exportInvalid', apiValidationDetails(error));
  return result('pur.invoice.exportFailed');
}
