import { HttpClient, HttpErrorResponse, HttpParams } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { Observable } from 'rxjs';
import { apiErrorKey } from '../../core/http/api-error';
import { PagedResult } from '../../shared/list-page/table-query';
import { SelectDialogQuery } from '../../shared/dialogs/select-dialog';
import { requisitionQuantityPattern, validRequisitionDate } from './requisition.service';
import { RequisitionItemOption } from './requisition.service';

export type RfqStatus = 'Draft' | 'Sent' | 'Closed';
export interface RfqSummary { id: number; documentNo: string; businessDate: string; replyDeadline: string;
  ownerId: number; status: RfqStatus; lineCount: number; invitationCount: number; version: number }
export interface RfqDetail extends Omit<RfqSummary, 'lineCount' | 'invitationCount'> {
  remark: string | null; sentAt: string | null; createdAt: string;
  lines: { id: number; lineNo: number; itemId: number; requisitionLineId: number | null;
    requiredDate: string; quantity: number; remark: string | null }[];
  invitations: { id: number; supplierId: number; supplierCodeSnapshot: string | null;
    supplierNameSnapshot: string | null; sentAt: string | null }[];
}
export interface RfqQuery { page: number; pageSize: number; keyword?: string; from?: string; to?: string;
  status?: RfqStatus; requisitionLineId?: number; sort?: string }
export interface RfqLineDraft { itemId: number | null; requisitionLineId: number | null;
  requiredDate: string; quantity: string; remark: string }
export interface RfqDraft { businessDate: string; replyDeadline: string; remark: string;
  lines: RfqLineDraft[]; supplierIds: number[] }
export interface RfqSupplierOption { id: number; code: string; name: string; isActive: boolean; isApproved: boolean }
export interface RfqSavedOptions { items: RequisitionItemOption[]; suppliers: RfqSupplierOption[]; sources: RfqSourceOption[] }
export interface QuotationContext { rfq: Pick<RfqDetail, 'id' | 'status' | 'lines' | 'invitations'>;
  currencies: { code: string; name: string }[];
  taxRates: { id: number; code: string; name: string; ratePercent: number }[] }
export interface RfqSourceOption { id: number; documentNo: string; lineNo: number;
  itemId: number; remainingQuantity: number; isAvailable?: boolean }
export type QuotationStatus = 'Active' | 'Withdrawn' | 'Awarded';
export interface QuotationSummary { id: number; rfqId: number; supplierId: number;
  status: QuotationStatus; currentRevisionNo: number; quoteDate: string; validUntil: string;
  version: number; isExpired: boolean }
export interface QuotationLine { id: number; rfqLineId: number; unitPrice: number;
  currencyCode: string; taxRateId: number }
export interface QuotationRevision { id: number; revisionNo: number; quoteDate: string;
  validUntil: string; remark: string | null; lines: QuotationLine[] }
export interface QuotationDetail { id: number; rfqId: number; supplierId: number;
  status: QuotationStatus; currentRevisionNo: number; version: number;
  revisions: QuotationRevision[]; isExpired: boolean }
export interface QuotationQuery { page: number; pageSize: number; supplierId?: number;
  status?: QuotationStatus; validOn?: string; sort?: string }
export interface QuotationDraft { supplierId: number | null; quoteDate: string; validUntil: string;
  remark: string; lines: { rfqLineId: number; unitPrice: string; currencyCode: string; taxRateId: number | null }[] }
export interface ComparisonOffer { quotationId: number; supplierId: number; quotationLineId: number;
  unitPrice: number; currencyCode: string; taxRateId: number; isComparable: boolean;
  unavailableReason: string | null; exchangeRate: number | null; exchangeRateId: number | null;
  exchangeRateFrom: string | null; exchangeRateTo: string | null; taxRatePercent: number | null;
  taxRateFrom: string | null; taxRateTo: string | null; baseNet: number | null;
  baseTax: number | null; baseGross: number | null; status: QuotationStatus }
export interface ComparisonLine { rfqLineId: number; lineNo: number; quantity: number; offers: ComparisonOffer[] }
export interface QuotationComparison { rfqId: number; comparisonDate: string;
  baseCurrencyCode: string; lines: ComparisonLine[] }

export function quotationFieldErrors(draft: QuotationDraft): string[] {
  const errors: string[] = [];
  if (!Number.isSafeInteger(draft.supplierId) || (draft.supplierId ?? 0) <= 0) errors.push('supplierId');
  const dateValid = validRequisitionDate(draft.quoteDate);
  if (!dateValid) errors.push('quoteDate');
  if (!validRequisitionDate(draft.validUntil) || (dateValid && draft.validUntil < draft.quoteDate))
    errors.push('validUntil');
  if (draft.remark.trim().length > 500) errors.push('remark');
  if (draft.lines.length < 1 || draft.lines.length > 1000 ||
    new Set(draft.lines.map(x => x.rfqLineId)).size !== draft.lines.length) errors.push('lines');
  draft.lines.forEach((line, index) => {
    const path = `lines.${index}`;
    if (!Number.isSafeInteger(line.rfqLineId) || line.rfqLineId <= 0) errors.push(`${path}.rfqLineId`);
    if (!/^(?:0|[1-9]\d{0,12})(?:\.\d{1,6})?$/.test(line.unitPrice.trim()))
      errors.push(`${path}.unitPrice`);
    if (!/^[A-Za-z]{3}$/.test(line.currencyCode.trim())) errors.push(`${path}.currencyCode`);
    if (!Number.isSafeInteger(line.taxRateId) || (line.taxRateId ?? 0) <= 0) errors.push(`${path}.taxRateId`);
  });
  return errors;
}

/** Field paths match the RFQ editor and server validation response. */
export function rfqFieldErrors(draft: RfqDraft): string[] {
  const errors: string[] = [];
  const validDate = validRequisitionDate(draft.businessDate);
  if (!validDate) errors.push('businessDate');
  if (!validRequisitionDate(draft.replyDeadline) ||
    (validDate && draft.replyDeadline < draft.businessDate)) errors.push('replyDeadline');
  if (draft.remark.trim().length > 500) errors.push('remark');
  if (draft.lines.length < 1 || draft.lines.length > 1000) errors.push('lines');
  if (draft.supplierIds.length < 1 || draft.supplierIds.length > 50 ||
    draft.supplierIds.some(id => !Number.isSafeInteger(id) || id <= 0) ||
    new Set(draft.supplierIds).size !== draft.supplierIds.length) errors.push('supplierIds');
  draft.lines.forEach((line, index) => {
    const path = `lines.${index}`;
    if (!Number.isSafeInteger(line.itemId) || (line.itemId ?? 0) <= 0) errors.push(`${path}.itemId`);
    if (line.requisitionLineId !== null &&
      (!Number.isSafeInteger(line.requisitionLineId) || line.requisitionLineId <= 0))
      errors.push(`${path}.requisitionLineId`);
    if (!validRequisitionDate(line.requiredDate) ||
      (validDate && line.requiredDate < draft.businessDate)) errors.push(`${path}.requiredDate`);
    if (!requisitionQuantityPattern.test(line.quantity.trim()) || !/[1-9]/.test(line.quantity))
      errors.push(`${path}.quantity`);
    if (line.remark.trim().length > 500) errors.push(`${path}.remark`);
  });
  return errors;
}

@Injectable({ providedIn: 'root' })
export class RfqService {
  private readonly http = inject(HttpClient);
  errorKey(error: unknown): string {
    if (error instanceof HttpErrorResponse) {
      const code = (error.error as { errorCode?: unknown } | null)?.errorCode;
      if (code === 'MD.ITEM.NOT_FOUND' || code === 'MD.ITEM.INACTIVE') return 'pur.rfq.itemUnavailable';
      if (code === 'MD.SUPPLIER.NOT_FOUND' || code === 'MD.SUPPLIER.NOT_APPROVED')
        return 'pur.rfq.supplierUnavailable';
      if (code === 'PUR.REQUISITION.LINE_NOT_FOUND') return 'pur.rfq.sourceUnavailable';
      if (code === 'PUR.QUOTATION.NOT_FOUND') return 'pur.quotation.notFound';
      if (code === 'PUR.RFQ.NOT_SENT') return 'pur.comparison.notSent';
      if (code === 'MD.EXCHANGE_RATE.NO_BASE_CURRENCY') return 'pur.comparison.noBaseCurrency';
      if (code === 'MD.CURRENCY.NOT_FOUND' || code === 'MD.CURRENCY.INACTIVE') return 'pur.quotation.currencyUnavailable';
      if (code === 'MD.TAX_RATE.NOT_FOUND' || code === 'MD.TAX_RATE.INACTIVE' ||
        code === 'MD.TAX_RATE.DIRECTION_MISMATCH') return 'pur.quotation.taxUnavailable';
    }
    return apiErrorKey(error, [
    'PUR.RFQ.NOT_FOUND', 'PUR.RFQ.NOT_EDITABLE', 'PUR.RFQ.INVALID_SOURCE',
    'PUR.RFQ.NOT_SENDABLE', 'PUR.RFQ.NOT_CLOSABLE', 'PUR.RFQ.NOT_OPEN',
    'PUR.QUOTATION.NOT_INVITED', 'PUR.QUOTATION.LINE_MISMATCH',
    'PUR.QUOTATION.NOT_EDITABLE', 'PUR.QUOTATION.EXPIRED',
    'COMMON.PERMISSION.DENIED', 'COMMON.CONCURRENCY.CONFLICT']);
  }
  list(query: RfqQuery): Observable<PagedResult<RfqSummary>> {
    let params = new HttpParams().set('page', query.page).set('pageSize', query.pageSize);
    for (const key of ['keyword', 'from', 'to', 'status', 'sort'] as const) {
      const value = query[key]?.trim();
      if (value) params = params.set(key, value);
    }
    if (query.requisitionLineId) params = params.set('requisitionLineId', query.requisitionLineId);
    return this.http.get<PagedResult<RfqSummary>>('/api/v1/pur/rfqs', { params });
  }
  get(id: number): Observable<RfqDetail> { return this.http.get<RfqDetail>(`/api/v1/pur/rfqs/${id}`); }
  quotationContext(id: number, quoteDate?: string): Observable<QuotationContext> {
    const params = quoteDate ? new HttpParams().set('quoteDate', quoteDate) : undefined;
    return this.http.get<QuotationContext>(`/api/v1/pur/rfqs/${id}/quotation-context`, { params });
  }
  listQuotations(rfqId: number, query: QuotationQuery): Observable<PagedResult<QuotationSummary>> {
    let params = new HttpParams().set('page', query.page).set('pageSize', query.pageSize);
    if (query.supplierId) params = params.set('supplierId', query.supplierId);
    if (query.status) params = params.set('status', query.status);
    if (query.validOn) params = params.set('validOn', query.validOn);
    if (query.sort) params = params.set('sort', query.sort);
    return this.http.get<PagedResult<QuotationSummary>>(`/api/v1/pur/rfqs/${rfqId}/quotations`, { params });
  }
  getQuotation(rfqId: number, quotationId: number): Observable<QuotationDetail> {
    return this.http.get<QuotationDetail>(`/api/v1/pur/rfqs/${rfqId}/quotations/${quotationId}`);
  }
  compare(rfqId: number): Observable<QuotationComparison> {
    return this.http.get<QuotationComparison>(`/api/v1/pur/rfqs/${rfqId}/comparison`);
  }
  createQuotation(rfqId: number, draft: QuotationDraft): Observable<QuotationDetail> {
    return this.http.post<QuotationDetail>(`/api/v1/pur/rfqs/${rfqId}/quotations`, this.quotationBody(draft));
  }
  reviseQuotation(rfqId: number, quotationId: number, version: number, draft: QuotationDraft): Observable<QuotationDetail> {
    return this.http.post<QuotationDetail>(`/api/v1/pur/rfqs/${rfqId}/quotations/${quotationId}/revisions`,
      this.quotationBody(draft), { params: new HttpParams().set('version', version) });
  }
  withdrawQuotation(rfqId: number, quotationId: number, version: number): Observable<QuotationDetail> {
    return this.http.post<QuotationDetail>(`/api/v1/pur/rfqs/${rfqId}/quotations/${quotationId}/withdraw`, null,
      { params: new HttpParams().set('version', version) });
  }
  private quotationBody(draft: QuotationDraft) {
    return { supplierId: draft.supplierId, quoteDate: draft.quoteDate, validUntil: draft.validUntil,
      remark: draft.remark.trim() || null, lines: draft.lines.map(line => ({ rfqLineId: line.rfqLineId,
        unitPrice: line.unitPrice.trim(), currencyCode: line.currencyCode.trim().toUpperCase(),
        taxRateId: line.taxRateId })) };
  }
  getForEdit(id: number): Observable<RfqDetail> {
    return this.http.get<RfqDetail>(`/api/v1/pur/rfqs/${id}/edit`);
  }
  savedOptions(id: number): Observable<RfqSavedOptions> {
    return this.http.get<RfqSavedOptions>(`/api/v1/pur/rfqs/${id}/saved-options`);
  }
  itemOptions(query: SelectDialogQuery): Observable<PagedResult<RequisitionItemOption>> {
    return this.http.get<PagedResult<RequisitionItemOption>>('/api/v1/pur/rfqs/item-options',
      { params: this.optionParams(query) });
  }
  supplierOptions(query: SelectDialogQuery): Observable<PagedResult<RfqSupplierOption>> {
    return this.http.get<PagedResult<RfqSupplierOption>>('/api/v1/pur/rfqs/supplier-options',
      { params: this.optionParams(query) });
  }
  sourceOptions(query: SelectDialogQuery, itemId: number | null): Observable<PagedResult<RfqSourceOption>> {
    let params = this.optionParams(query);
    if (itemId) params = params.set('itemId', itemId);
    return this.http.get<PagedResult<RfqSourceOption>>('/api/v1/pur/rfqs/source-options', { params });
  }
  private optionParams(query: SelectDialogQuery): HttpParams {
    let params = new HttpParams().set('page', query.page).set('pageSize', query.pageSize);
    if (query.keyword.trim()) params = params.set('keyword', query.keyword.trim());
    return params;
  }
  create(draft: RfqDraft): Observable<RfqDetail> {
    return this.http.post<RfqDetail>('/api/v1/pur/rfqs', this.body(draft));
  }
  update(id: number, version: number, draft: RfqDraft): Observable<RfqDetail> {
    return this.http.put<RfqDetail>(`/api/v1/pur/rfqs/${id}`, this.body(draft),
      { params: new HttpParams().set('version', version) });
  }
  send(id: number, version: number): Observable<RfqDetail> {
    return this.http.post<RfqDetail>(`/api/v1/pur/rfqs/${id}/send`, null,
      { params: new HttpParams().set('version', version) });
  }
  close(id: number, version: number): Observable<RfqDetail> {
    return this.http.post<RfqDetail>(`/api/v1/pur/rfqs/${id}/close`, null,
      { params: new HttpParams().set('version', version) });
  }
  private body(draft: RfqDraft) {
    return { businessDate: draft.businessDate, replyDeadline: draft.replyDeadline,
      remark: draft.remark.trim() || null, supplierIds: draft.supplierIds,
      lines: draft.lines.map((line, index) => ({ lineNo: index + 1, itemId: line.itemId,
        requisitionLineId: line.requisitionLineId, requiredDate: line.requiredDate,
        quantity: line.quantity.trim(), remark: line.remark.trim() || null })) };
  }
}
