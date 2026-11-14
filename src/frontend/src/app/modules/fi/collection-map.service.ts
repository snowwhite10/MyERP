import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { Observable } from 'rxjs';
import { apiErrorKey } from '../../core/http/api-error';
import { accountUnusable, SalesMappedAccount } from './sales-map.service';

const API = '/api/v1/gl/sales-collection-account-map';

/** Request fields, in screen order; also the keys of the 422 `accountErrors` extension (ADR-123). */
export const COLLECTION_MAP_FIELDS = ['unappliedCashAccountCode', 'receivableAccountCode'] as const;
export type CollectionMapField = (typeof COLLECTION_MAP_FIELDS)[number];
/** MD account code length (`varchar(30)`, BR-FI-154). */
export const COLLECTION_MAP_CODE_MAX = 30;

/** 422 reason on the AR field when it equals the unapplied-cash code (trimmed, case-insensitive). */
export const COLLECTION_MAP_SAME_ACCOUNT = 'FI.COLLECTION_MAP.SAME_ACCOUNT';
/** Read warning: the AR control code differs from the SD invoice map's receivable code (allowed, ADR-120). */
export const COLLECTION_MAP_RECEIVABLE_DIFFERS = 'FI.COLLECTION_MAP.RECEIVABLE_DIFFERS_FROM_SALES_MAP';

/** Reasons the server may give per field; anything else is shown as a generic invalid-account reason. */
export const COLLECTION_MAP_ACCOUNT_REASONS = ['MD.ACCOUNT.NOT_FOUND', 'MD.ACCOUNT.INACTIVE', 'MD.ACCOUNT.NOT_POSTABLE',
  COLLECTION_MAP_SAME_ACCOUNT] as const;

/** Mapped account with its current MD label (same shape as the sales and purchase maps). */
export type CollectionMappedAccount = SalesMappedAccount;

/**
 * `GET` result; an unconfigured company has `configured=false`, version 0 and null accounts. `salesInvoiceReceivableAccountCode`
 * is the SD invoice map's AR code (null when that map is not set up); `warnings` may hold `RECEIVABLE_DIFFERS_FROM_SALES_MAP`.
 */
export interface SalesCollectionAccountMap {
  configured: boolean;
  version: number;
  unappliedCashAccount: CollectionMappedAccount | null;
  receivableAccount: CollectionMappedAccount | null;
  salesInvoiceReceivableAccountCode: string | null;
  warnings: string[];
  createdAt: string | null;
  createdBy: number | null;
  updatedAt: string | null;
  updatedBy: number | null;
}

export type CollectionMapCodes = Record<CollectionMapField, string>;

/** `PUT` result: `changed=false` means the codes equal the saved ones and no new version was created. */
export interface CollectionMapWriteResult { map: SalesCollectionAccountMap; changed: boolean; }

/** Errors this page names; all others fall back to the shared generic messages. */
export const COLLECTION_MAP_ERROR_CODES = ['FI.COLLECTION_MAP.VERSION_CONFLICT', 'FI.COLLECTION_MAP.ACCOUNT_INVALID',
  COLLECTION_MAP_SAME_ACCOUNT, COLLECTION_MAP_RECEIVABLE_DIFFERS, 'COMMON.PERMISSION.DENIED'];

/** Account of the map for a request field. */
export function collectionMappedAccount(map: SalesCollectionAccountMap, field: CollectionMapField): CollectionMappedAccount | null {
  return field === 'unappliedCashAccountCode' ? map.unappliedCashAccount : map.receivableAccount;
}

/** True when the saved account would make collection or write-off posting fail (missing, inactive or has child accounts). */
export const collectionAccountUnusable = accountUnusable;

/** True when the saved AR control account differs from the SD sales invoice map's receivable account. */
export function receivableDiffersFromSalesMap(map: SalesCollectionAccountMap): boolean {
  return map.configured && (map.warnings ?? []).includes(COLLECTION_MAP_RECEIVABLE_DIFFERS);
}

/** Per-field reasons of a 422 `FI.COLLECTION_MAP.ACCOUNT_INVALID`; unknown fields are ignored, unknown reasons kept as `other`. */
export function collectionMapAccountErrors(error: unknown): Partial<Record<CollectionMapField, string>> {
  if (!(error instanceof HttpErrorResponse) || error.status !== 422) return {};
  const body = error.error as { errorCode?: unknown; accountErrors?: unknown } | null;
  if (body?.errorCode !== 'FI.COLLECTION_MAP.ACCOUNT_INVALID' || !body.accountErrors || typeof body.accountErrors !== 'object') return {};
  const reasons = body.accountErrors as Record<string, unknown>;
  const result: Partial<Record<CollectionMapField, string>> = {};
  for (const field of COLLECTION_MAP_FIELDS) {
    const reason = reasons[field];
    if (reason === undefined) continue;
    result[field] = (COLLECTION_MAP_ACCOUNT_REASONS as readonly unknown[]).includes(reason) ? String(reason) : 'other';
  }
  return result;
}

/** FI sales collection account map (REQ_F4116.1.10, interface 6.25); the server scopes the map to the current company. */
@Injectable({ providedIn: 'root' })
export class CollectionMapService {
  private readonly http = inject(HttpClient);

  errorKey(error: unknown): string { return apiErrorKey(error, COLLECTION_MAP_ERROR_CODES); }

  get(): Observable<SalesCollectionAccountMap> { return this.http.get<SalesCollectionAccountMap>(API); }

  /** `version` 0 creates the map; otherwise it must be the saved version (409 when stale). */
  save(version: number, codes: CollectionMapCodes): Observable<CollectionMapWriteResult> {
    return this.http.put<CollectionMapWriteResult>(API, { version, ...codes });
  }
}
