import { HttpClient, HttpParams } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { Observable } from 'rxjs';
import { apiErrorKey } from '../../../core/http/api-error';
import { PagedResult } from '../organization/organization.service';

export const AUDIT_LOG_API = '/api/v1/sys/audit-logs';

/** 稽核類別（後端 `AuditCategories`）。 */
export const AUDIT_CATEGORIES = ['DATA', 'SECURITY'] as const;
export type AuditCategory = (typeof AUDIT_CATEGORIES)[number];

/** 已知的操作類型（後端 `AuditActions`）；有 i18n 標籤，未知的操作照原樣顯示。 */
export const AUDIT_ACTIONS = [
  'CREATE',
  'UPDATE',
  'DELETE',
  'VOID',
  'APPROVE',
  'REJECT',
  'POST',
  'LOGIN',
  'LOGIN_FAILED',
  'MASTER_CODE_LOGIN',
  'LOGOUT',
  'PERMISSION_CHANGE',
  'EXPORT',
] as const;

/** 稽核記錄（api.md「稽核日誌」）。`changes` 為欄位差異 `{"欄位":{"old":…,"new":…}}` 或安全事件明細；敏感欄位值為 `***`。 */
export interface AuditLogItem {
  id: number;
  /** 伺服器時間（UTC+8） */
  occurredAt: string;
  category: string;
  action: string;
  tableName: string | null;
  recordId: string | null;
  userId: number | null;
  username: string | null;
  companyId: number | null;
  changes: Record<string, unknown> | null;
  remoteIp: string | null;
  traceId: string | null;
}

export interface AuditLogQuery {
  page: number;
  pageSize: number;
  /** `yyyy-MM-dd`（含頭尾） */
  from?: string;
  to?: string;
  category?: string;
  action?: string;
  tableName?: string;
  recordId?: string;
  userId?: number | null;
  /** `occurredAt` 或 `-occurredAt` */
  sort?: string;
}

/**
 * 展開後的一列明細。資料變更有 `old`／`new`；安全事件明細只有 `value`。
 * 值為 `null` 代表空值（畫面顯示「空」），其他值轉成字串（`***` 照原樣）。
 */
export type AuditChangeRow =
  | { field: string; kind: 'diff'; old: string | null; new: string | null }
  | { field: string; kind: 'detail'; value: string | null };

function display(value: unknown): string | null {
  if (value === null || value === undefined) return null;
  return typeof value === 'string' ? value : JSON.stringify(value);
}

function isDiff(value: unknown): value is { old?: unknown; new?: unknown } {
  if (value === null || typeof value !== 'object' || Array.isArray(value)) return false;
  const keys = Object.keys(value);
  return keys.length > 0 && keys.every((key) => key === 'old' || key === 'new');
}

/** 將 `changes` 轉成明細列（保留後端欄位順序）。 */
export function auditChangeRows(changes: Record<string, unknown> | null | undefined): AuditChangeRow[] {
  if (!changes) return [];
  return Object.entries(changes).map(([field, value]) =>
    isDiff(value)
      ? { field, kind: 'diff', old: display(value.old), new: display(value.new) }
      : { field, kind: 'detail', value: display(value) },
  );
}

/** 已知操作回傳 i18n key，未知操作回傳 null（畫面顯示原代碼）。 */
export function auditActionKey(action: string): string | null {
  return (AUDIT_ACTIONS as readonly string[]).includes(action) ? `sys.audit.actions.${action}` : null;
}

/** 已知類別回傳 i18n key，未知類別回傳 null。 */
export function auditCategoryKey(category: string): string | null {
  return (AUDIT_CATEGORIES as readonly string[]).includes(category) ? `sys.audit.categories.${category}` : null;
}

/** 頁面會顯示的稽核錯誤代碼（400 顯示通用驗證訊息，其他錯誤顯示通用訊息）。 */
export const AUDIT_ERROR_CODES = ['COMMON.PERMISSION.DENIED'] as const;

export function auditErrorKey(error: unknown): string {
  return apiErrorKey(error, AUDIT_ERROR_CODES);
}

/** 稽核查詢 API（只讀）；X-Company-Id 由攔截器帶入，只會看到目前公司的記錄（ADR-019）。 */
@Injectable({ providedIn: 'root' })
export class AuditLogService {
  private readonly http = inject(HttpClient);

  list(query: AuditLogQuery): Observable<PagedResult<AuditLogItem>> {
    let params = new HttpParams().set('page', query.page).set('pageSize', query.pageSize);
    const text: [string, string | undefined][] = [
      ['from', query.from],
      ['to', query.to],
      ['category', query.category],
      ['action', query.action],
      ['tableName', query.tableName],
      ['recordId', query.recordId],
    ];
    for (const [name, value] of text) {
      if (value?.trim()) params = params.set(name, value.trim());
    }
    if (query.userId != null) params = params.set('userId', query.userId);
    if (query.sort) params = params.set('sort', query.sort);
    return this.http.get<PagedResult<AuditLogItem>>(AUDIT_LOG_API, { params });
  }
}
