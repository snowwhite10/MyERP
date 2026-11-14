import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { Observable } from 'rxjs';
import { apiErrorKey } from '../../core/http/api-error';
import { accountUnusable, SalesMappedAccount } from './sales-map.service';

const API = '/api/v1/gl/purchase-invoice-account-map';

/** Request fields, in screen order; also the keys of the 422 `accountErrors` extension (ADR-118). */
export const PURCHASE_MAP_FIELDS = ['payableAccountCode', 'purchaseAccountCode', 'inputTaxAccountCode'] as const;
export type PurchaseMapField = (typeof PURCHASE_MAP_FIELDS)[number];
/** MD account code length (`varchar(30)`, BR-FI-147). */
export const PURCHASE_MAP_CODE_MAX = 30;

/** Reasons the server may give per field; anything else is shown as a generic invalid-account reason. */
export const PURCHASE_MAP_ACCOUNT_REASONS = ['MD.ACCOUNT.NOT_FOUND', 'MD.ACCOUNT.INACTIVE', 'MD.ACCOUNT.NOT_POSTABLE'] as const;

/** Mapped account with its current MD label (same shape as the sales map, ADR-118). */
export type PurchaseMappedAccount = SalesMappedAccount;

/** `GET` result; an unconfigured company has `configured=false`, version 0 and null accounts. */
export interface PurchaseInvoiceAccountMap {
  configured: boolean;
  version: number;
  payableAccount: PurchaseMappedAccount | null;
  purchaseAccount: PurchaseMappedAccount | null;
  inputTaxAccount: PurchaseMappedAccount | null;
  createdAt: string | null;
  createdBy: number | null;
  updatedAt: string | null;
  updatedBy: number | null;
}

export type PurchaseMapCodes = Record<PurchaseMapField, string>;

/** `PUT` result: `changed=false` means the codes equal the saved ones and no new version was created. */
export interface PurchaseMapWriteResult { map: PurchaseInvoiceAccountMap; changed: boolean; }

/** Errors this page names; all others fall back to the shared generic messages. */
export const PURCHASE_MAP_ERROR_CODES = ['FI.PURCHASE_MAP.VERSION_CONFLICT', 'FI.PURCHASE_MAP.ACCOUNT_INVALID', 'COMMON.PERMISSION.DENIED'];

/** Account of the map for a request field. */
export function purchaseMappedAccount(map: PurchaseInvoiceAccountMap, field: PurchaseMapField): PurchaseMappedAccount | null {
  return field === 'payableAccountCode' ? map.payableAccount : field === 'purchaseAccountCode' ? map.purchaseAccount : map.inputTaxAccount;
}

/** True when the saved account would make purchase invoice posting fail (missing, inactive or has child accounts). */
export const purchaseAccountUnusable = accountUnusable;

/** Per-field reasons of a 422 `FI.PURCHASE_MAP.ACCOUNT_INVALID`; unknown fields are ignored, unknown reasons kept as `other`. */
export function purchaseMapAccountErrors(error: unknown): Partial<Record<PurchaseMapField, string>> {
  if (!(error instanceof HttpErrorResponse) || error.status !== 422) return {};
  const body = error.error as { errorCode?: unknown; accountErrors?: unknown } | null;
  if (body?.errorCode !== 'FI.PURCHASE_MAP.ACCOUNT_INVALID' || !body.accountErrors || typeof body.accountErrors !== 'object') return {};
  const reasons = body.accountErrors as Record<string, unknown>;
  const result: Partial<Record<PurchaseMapField, string>> = {};
  for (const field of PURCHASE_MAP_FIELDS) {
    const reason = reasons[field];
    if (reason === undefined) continue;
    result[field] = (PURCHASE_MAP_ACCOUNT_REASONS as readonly unknown[]).includes(reason) ? String(reason) : 'other';
  }
  return result;
}

/** PUR purchase invoice account map (REQ_F4116.9.6, interface 6.24); the server scopes the map to the current company. */
@Injectable({ providedIn: 'root' })
export class PurchaseMapService {
  private readonly http = inject(HttpClient);

  errorKey(error: unknown): string { return apiErrorKey(error, PURCHASE_MAP_ERROR_CODES); }

  get(): Observable<PurchaseInvoiceAccountMap> { return this.http.get<PurchaseInvoiceAccountMap>(API); }

  /** `version` 0 creates the map; otherwise it must be the saved version (409 when stale). */
  save(version: number, codes: PurchaseMapCodes): Observable<PurchaseMapWriteResult> {
    return this.http.put<PurchaseMapWriteResult>(API, { version, ...codes });
  }
}
