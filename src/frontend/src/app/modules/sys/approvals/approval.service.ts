import { HttpClient, HttpParams } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { map, Observable } from 'rxjs';
import { apiErrorKey } from '../../../core/http/api-error';
import { PagedResult } from '../../../shared/list-page/table-query';
import { ROLE_API, Role } from '../roles/role.service';
import { USER_API, UserSummary } from '../users/user.service';

export const APPROVAL_FLOW_API = '/api/v1/sys/approval-flows';
export const APPROVAL_DELEGATION_API = '/api/v1/sys/approval-delegations';
export const APPROVAL_API = '/api/v1/sys/approvals';

/** 步驟審批人類型（與後端 `ApproverType` 相同，JSON 以字串傳送）。 */
export type ApproverType = 'Role' | 'User';
/** 審批實例狀態。 */
export type ApprovalStatus = 'Pending' | 'Approved' | 'Rejected' | 'Cancelled';
/** 實例步驟狀態：Waiting 尚未輪到、Pending 目前步驟。 */
export type ApprovalStepStatus = 'Waiting' | 'Pending' | 'Approved' | 'Rejected' | 'Skipped';

export const APPROVER_TYPES: readonly ApproverType[] = ['Role', 'User'];
export const APPROVAL_STATUSES: readonly ApprovalStatus[] = ['Pending', 'Approved', 'Rejected', 'Cancelled'];

/** 流程步驟（api.md「審批流程」）。`minAmount` 為本位幣金額門檻，null 表示一律需要。 */
export interface ApprovalStep {
  stepNo: number;
  name: string;
  approverType: ApproverType;
  roleId: number | null;
  userId: number | null;
  minAmount: number | null;
}

export interface ApprovalFlow {
  id: number;
  documentType: string;
  name: string;
  allowSelfApproval: boolean;
  isActive: boolean;
  steps: ApprovalStep[];
  version: number;
}

export type ApprovalStepInput = Omit<ApprovalStep, 'stepNo'>;

export interface ApprovalFlowInput {
  documentType: string;
  name: string;
  allowSelfApproval: boolean;
  steps: ApprovalStepInput[];
}

export interface ApprovalFlowUpdate extends Omit<ApprovalFlowInput, 'documentType'> {
  isActive: boolean;
  version: number;
}

/** 審批代理；日期為 `yyyy-MM-dd`（含頭尾）。 */
export interface ApprovalDelegation {
  id: number;
  delegatorUserId: number;
  delegatorUsername: string;
  delegateUserId: number;
  delegateUsername: string;
  startDate: string;
  endDate: string;
  remark: string | null;
  isActive: boolean;
  version: number;
}

export interface ApprovalDelegationInput {
  delegatorUserId: number;
  delegateUserId: number;
  startDate: string;
  endDate: string;
  remark: string | null;
}

export interface ApprovalDelegationUpdate {
  startDate: string;
  endDate: string;
  remark: string | null;
  isActive: boolean;
  version: number;
}

/** 我的待辦；`onBehalfOf*` 為代理時的被代理人，`version` 於核准／駁回時帶回。 */
export interface ApprovalTask {
  instanceId: number;
  documentType: string;
  documentId: number;
  documentNo: string;
  summary: string | null;
  amount: number;
  submittedBy: number;
  submittedByUsername: string;
  submittedAt: string;
  stepNo: number;
  stepCount: number;
  stepName: string;
  onBehalfOfUserId: number | null;
  onBehalfOfUsername: string | null;
  version: number;
}

export interface ApprovalInstanceSummary {
  id: number;
  documentType: string;
  documentId: number;
  documentNo: string;
  summary: string | null;
  amount: number;
  status: ApprovalStatus;
  currentStepNo: number | null;
  submittedBy: number;
  submittedByUsername: string;
  submittedAt: string;
  completedAt: string | null;
  version: number;
}

export interface ApprovalInstanceStep {
  stepNo: number;
  name: string;
  approverType: ApproverType;
  roleId: number | null;
  roleName: string | null;
  userId: number | null;
  username: string | null;
  minAmount: number | null;
  status: ApprovalStepStatus;
  actedBy: number | null;
  actedByUsername: string | null;
  onBehalfOfUserId: number | null;
  onBehalfOfUsername: string | null;
  actedAt: string | null;
  comment: string | null;
}

export interface ApprovalInstanceDetail extends ApprovalInstanceSummary {
  allowSelfApproval: boolean;
  steps: ApprovalInstanceStep[];
}

/** 審批人下拉選項（角色顯示「代碼 名稱」，用戶顯示帳號）。 */
export interface ApproverOption {
  id: number;
  label: string;
}

export interface ApprovalFlowQuery {
  page: number;
  pageSize: number;
  keyword?: string;
  /** `documentType`、`name`，可加 `-` 降序 */
  sort?: string;
}

export interface ApprovalDelegationQuery {
  page: number;
  pageSize: number;
  /** 被代理人或代理人 */
  userId?: number | null;
  /** `yyyy-MM-dd`：只列出該日有效的代理 */
  activeOn?: string;
  /** `startDate` 或 `-startDate` */
  sort?: string;
}

export interface ApprovalTaskQuery {
  page: number;
  pageSize: number;
  documentType?: string;
  keyword?: string;
  /** `submittedAt` 或 `-submittedAt` */
  sort?: string;
}

export interface ApprovalHistoryQuery extends ApprovalTaskQuery {
  status?: ApprovalStatus | null;
}

/** 與後端相同的限制。 */
export const FLOW_NAME_MAX_LENGTH = 100;
export const MAX_STEPS = 10;
export const REMARK_MAX_LENGTH = 200;
export const COMMENT_MAX_LENGTH = 500;
/**
 * 金額門檻：最多 11 位整數、4 位小數（共 15 位有效數字，JSON 數值可精確來回轉換；後端上限更寬）。
 */
export const AMOUNT_PATTERN = /^\d{1,11}(\.\d{1,4})?$/;

/** 頁面會顯示的審批錯誤代碼（其他錯誤顯示通用訊息）。 */
export const APPROVAL_ERROR_CODES = [
  'SYS.APPROVAL_FLOW.DUPLICATE_TYPE',
  'SYS.APPROVAL_FLOW.NOT_FOUND',
  'SYS.APPROVAL_DELEGATION.OVERLAP',
  'SYS.APPROVAL_DELEGATION.NOT_FOUND',
  'SYS.APPROVAL.NOT_FOUND',
  'SYS.APPROVAL.NOT_PENDING',
  'SYS.APPROVAL.NOT_APPROVER',
  'SYS.APPROVAL.SELF_APPROVAL',
  'COMMON.CONCURRENCY.CONFLICT',
  'COMMON.PERMISSION.DENIED',
  'COMMON.REQUEST.VALIDATION_FAILED',
] as const;

/** 將 API 錯誤轉成 i18n key。 */
export function approvalErrorKey(error: unknown): string {
  return apiErrorKey(error, APPROVAL_ERROR_CODES);
}

/** 列舉值的 i18n key（i18n key 段落一律 camelCase）。 */
export function approvalStatusKey(status: ApprovalStatus): string {
  return `sys.approval.statuses.${camel(status)}`;
}

export function stepStatusKey(status: ApprovalStepStatus): string {
  return `sys.approval.stepStatuses.${camel(status)}`;
}

export function approverTypeKey(type: ApproverType): string {
  return `sys.approvalFlow.approverTypes.${camel(type)}`;
}

/** 狀態標籤顏色。 */
export function statusColor(status: ApprovalStatus | ApprovalStepStatus): string {
  switch (status) {
    case 'Approved':
      return 'success';
    case 'Rejected':
      return 'error';
    case 'Pending':
      return 'processing';
    default:
      return 'default';
  }
}

function camel(value: string): string {
  return value.charAt(0).toLowerCase() + value.slice(1);
}

/** 審批 API：流程、代理、待辦與歷史都屬於目前公司（`X-Company-Id`）。 */
@Injectable({ providedIn: 'root' })
export class ApprovalService {
  private readonly http = inject(HttpClient);

  listFlows(query: ApprovalFlowQuery): Observable<PagedResult<ApprovalFlow>> {
    let params = paging(query.page, query.pageSize);
    if (query.keyword?.trim()) params = params.set('keyword', query.keyword.trim());
    if (query.sort) params = params.set('sort', query.sort);
    return this.http.get<PagedResult<ApprovalFlow>>(APPROVAL_FLOW_API, { params });
  }

  createFlow(input: ApprovalFlowInput): Observable<ApprovalFlow> {
    return this.http.post<ApprovalFlow>(APPROVAL_FLOW_API, input);
  }

  updateFlow(id: number, input: ApprovalFlowUpdate): Observable<ApprovalFlow> {
    return this.http.put<ApprovalFlow>(`${APPROVAL_FLOW_API}/${id}`, input);
  }

  listDelegations(query: ApprovalDelegationQuery): Observable<PagedResult<ApprovalDelegation>> {
    let params = paging(query.page, query.pageSize);
    if (query.userId) params = params.set('userId', query.userId);
    if (query.activeOn) params = params.set('activeOn', query.activeOn);
    if (query.sort) params = params.set('sort', query.sort);
    return this.http.get<PagedResult<ApprovalDelegation>>(APPROVAL_DELEGATION_API, { params });
  }

  createDelegation(input: ApprovalDelegationInput): Observable<ApprovalDelegation> {
    return this.http.post<ApprovalDelegation>(APPROVAL_DELEGATION_API, input);
  }

  updateDelegation(id: number, input: ApprovalDelegationUpdate): Observable<ApprovalDelegation> {
    return this.http.put<ApprovalDelegation>(`${APPROVAL_DELEGATION_API}/${id}`, input);
  }

  tasks(query: ApprovalTaskQuery): Observable<PagedResult<ApprovalTask>> {
    return this.http.get<PagedResult<ApprovalTask>>(`${APPROVAL_API}/tasks`, { params: documentParams(query) });
  }

  history(query: ApprovalHistoryQuery): Observable<PagedResult<ApprovalInstanceSummary>> {
    let params = documentParams(query);
    if (query.status) params = params.set('status', query.status);
    return this.http.get<PagedResult<ApprovalInstanceSummary>>(APPROVAL_API, { params });
  }

  get(id: number): Observable<ApprovalInstanceDetail> {
    return this.http.get<ApprovalInstanceDetail>(`${APPROVAL_API}/${id}`);
  }

  approve(id: number, comment: string | null, version: number): Observable<ApprovalInstanceDetail> {
    return this.http.post<ApprovalInstanceDetail>(`${APPROVAL_API}/${id}/approve`, { comment, version });
  }

  reject(id: number, comment: string, version: number): Observable<ApprovalInstanceDetail> {
    return this.http.post<ApprovalInstanceDetail>(`${APPROVAL_API}/${id}/reject`, { comment, version });
  }

  /** 啟用角色（最多 200 筆，需 `SYS.ROLE.VIEW`）。 */
  roleOptions(): Observable<ApproverOption[]> {
    return this.http
      .get<PagedResult<Role>>(ROLE_API, { params: paging(1, 200).set('sort', 'code') })
      .pipe(map((result) => result.items.filter((r) => r.isActive).map((r) => ({ id: r.id, label: `${r.code} ${r.name}` }))));
  }

  /** 目前公司的啟用用戶（最多 200 筆，需 `SYS.USER.VIEW`）。 */
  userOptions(): Observable<ApproverOption[]> {
    return this.http
      .get<PagedResult<UserSummary>>(USER_API, { params: paging(1, 200).set('sort', 'username') })
      .pipe(map((result) => result.items.filter((u) => u.isActive).map((u) => ({ id: u.id, label: u.username }))));
  }
}

function paging(page: number, pageSize: number): HttpParams {
  return new HttpParams().set('page', page).set('pageSize', pageSize);
}

function documentParams(query: ApprovalTaskQuery): HttpParams {
  let params = paging(query.page, query.pageSize);
  if (query.documentType?.trim()) params = params.set('documentType', query.documentType.trim().toUpperCase());
  if (query.keyword?.trim()) params = params.set('keyword', query.keyword.trim());
  if (query.sort) params = params.set('sort', query.sort);
  return params;
}
