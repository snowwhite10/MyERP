import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { Observable } from 'rxjs';
import { apiErrorKey } from '../../core/http/api-error';

const API = '/api/v1/gl/sales-invoice-account-map';

/** Request fields, in screen order; also the keys of the 422 `accountErrors` extension (ADR-110). */
export const SALES_MAP_FIELDS = ['receivableAccountCode', 'revenueAccountCode', 'outputTaxAccountCode'] as const;
export type SalesMapField = (typeof SALES_MAP_FIELDS)[number];
/** MD account code length (`varchar(30)`, BR-FI-147). */
export const SALES_MAP_CODE_MAX = 30;

/** Reasons the server may give per field; anything else is shown as a generic invalid-account reason. */
export const SALES_MAP_ACCOUNT_REASONS = ['MD.ACCOUNT.NOT_FOUND', 'MD.ACCOUNT.INACTIVE', 'MD.ACCOUNT.NOT_POSTABLE'] as const;

/** Mapped account with its current MD label; `name` is null when MD no longer has the code. */
export interface SalesMappedAccount { code: string; name: string | null; isActive: boolean; isPostable: boolean; }

/** `GET` result; an unconfigured company has `configured=false`, version 0 and null accounts. */
export interface SalesInvoiceAccountMap {
  configured: boolean;
  version: number;
  receivableAccount: SalesMappedAccount | null;
  revenueAccount: SalesMappedAccount | null;
  outputTaxAccount: SalesMappedAccount | null;
  createdAt: string | null;
  createdBy: number | null;
  updatedAt: string | null;
  updatedBy: number | null;
}

export type SalesMapCodes = Record<SalesMapField, string>;

/** `PUT` result: `changed=false` means the codes equal the saved ones and no new version was created. */
export interface SalesMapWriteResult { map: SalesInvoiceAccountMap; changed: boolean; }

/** Errors this page names; all others fall back to the shared generic messages. */
export const SALES_MAP_ERROR_CODES = ['FI.SALES_MAP.VERSION_CONFLICT', 'FI.SALES_MAP.ACCOUNT_INVALID', 'COMMON.PERMISSION.DENIED'];

/** Account of the map for a request field. */
export function mappedAccount(map: SalesInvoiceAccountMap, field: SalesMapField): SalesMappedAccount | null {
  return field === 'receivableAccountCode' ? map.receivableAccount : field === 'revenueAccountCode' ? map.revenueAccount : map.outputTaxAccount;
}

/** True when the saved account would make SD invoice posting fail (missing, inactive or has child accounts). */
export function accountUnusable(account: SalesMappedAccount | null): boolean {
  return !!account && (account.name === null || !account.isActive || !account.isPostable);
}

/** Per-field reasons of a 422 `FI.SALES_MAP.ACCOUNT_INVALID`; unknown fields are ignored, unknown reasons kept as `other`. */
export function salesMapAccountErrors(error: unknown): Partial<Record<SalesMapField, string>> {
  if (!(error instanceof HttpErrorResponse) || error.status !== 422) return {};
  const body = error.error as { errorCode?: unknown; accountErrors?: unknown } | null;
  if (body?.errorCode !== 'FI.SALES_MAP.ACCOUNT_INVALID' || !body.accountErrors || typeof body.accountErrors !== 'object') return {};
  const reasons = body.accountErrors as Record<string, unknown>;
  const result: Partial<Record<SalesMapField, string>> = {};
  for (const field of SALES_MAP_FIELDS) {
    const reason = reasons[field];
    if (reason === undefined) continue;
    result[field] = (SALES_MAP_ACCOUNT_REASONS as readonly unknown[]).includes(reason) ? String(reason) : 'other';
  }
  return result;
}

/** SD invoice account map (REQ_F4116.8.5, interface 6.23); the server scopes the map to the current company. */
@Injectable({ providedIn: 'root' })
export class SalesMapService {
  private readonly http = inject(HttpClient);

  errorKey(error: unknown): string { return apiErrorKey(error, SALES_MAP_ERROR_CODES); }

  get(): Observable<SalesInvoiceAccountMap> { return this.http.get<SalesInvoiceAccountMap>(API); }

  /** `version` 0 creates the map; otherwise it must be the saved version (409 when stale). */
  save(version: number, codes: SalesMapCodes): Observable<SalesMapWriteResult> {
    return this.http.put<SalesMapWriteResult>(API, { version, ...codes });
  }
}
