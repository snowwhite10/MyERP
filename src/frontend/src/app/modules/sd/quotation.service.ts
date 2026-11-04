import { HttpClient, HttpHeaders, HttpParams } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { map, Observable } from 'rxjs';
import { PagedResult } from '../../shared/list-page/table-query';
import { QuotationDraftForm, validateQuotationDraft } from './quotation-draft-form';

const API = '/api/v1/sal/quotations';

/** JSON decimal tokens are assembled from validated text so six-place values never pass through Number. */
export function quotationDraftJson(draft: QuotationDraftForm): string {
  if (Object.keys(validateQuotationDraft(draft)).length) throw new Error('Invalid quotation draft');
  const lines = draft.lines.map(line => `{${[
    `"lineNo":${line.lineNo}`, `"itemId":${line.itemId}`, `"quantity":${line.quantity}`,
    `"unitPrice":${line.unitPrice}`, `"discountRate":${line.discountRate}`,
    `"currencyCode":${JSON.stringify(line.currencyCode)}`, `"taxRateId":${line.taxRateId}`,
  ].join(',')}}`).join(',');
  return `{${[
    `"customerId":${draft.customerId}`, `"businessDate":${JSON.stringify(draft.businessDate)}`,
    `"expiryDate":${JSON.stringify(draft.expiryDate)}`, `"remark":${JSON.stringify(draft.remark.trim())}`,
    `"lines":[${lines}]`,
  ].join(',')}}`;
}

export type SalesQuotationStatus = 'Draft' | 'Pending' | 'Rejected' | 'Approved' | 'Voided';

export interface SalesQuotationSummary {
  id: number;
  documentNo: string;
  customerId: number;
  customerName: string;
  businessDate: string;
  expiryDate: string;
  status: SalesQuotationStatus;
  originalGross: number;
  version: number;
}

export interface SalesQuotationLineDetail {
  id: number;
  lineNo: number;
  itemId: number;
  itemCode: string;
  itemName: string;
  quantity: number;
  unitPrice: number;
  discountRate: number;
  taxRateId: number;
  taxRatePercent: number;
  originalNet: number;
  originalTax: number;
  originalGross: number;
  baseNet: number;
  baseTax: number;
  baseGross: number;
}

export interface SalesQuotationDetail extends SalesQuotationSummary {
  customerCode: string;
  currencyCode: string;
  baseCurrencyCode: string;
  exchangeRate: number;
  originalNet: number;
  originalTax: number;
  baseNet: number;
  baseTax: number;
  baseGross: number;
  remark: string | null;
  lines: SalesQuotationLineDetail[];
  approvalInstanceId: number | null;
  isExpired: boolean;
  approvalHistory: {
    instanceId: number;
    status: string;
    submittedAt: string;
    steps: { stepNo: number; name: string; status: string; actedBy: number | null;
      actedAt: string | null; comment: string | null }[];
  }[];
}

export type SalesQuotationEditableDetail = Omit<SalesQuotationDetail, 'lines'> & {
  lines: (Omit<SalesQuotationLineDetail, 'quantity' | 'unitPrice' | 'discountRate'> & {
    quantity: string; unitPrice: string; discountRate: string;
  })[];
};

export interface SalesQuotationQuery {
  page: number;
  pageSize: number;
  keyword?: string;
  from?: string;
  to?: string;
  status?: SalesQuotationStatus;
  customerId?: number;
  sort?: string;
}

/** Reads only persisted SD data. The server applies company and VIEW data scope. */
@Injectable({ providedIn: 'root' })
export class SalesQuotationService {
  private readonly http = inject(HttpClient);

  list(query: SalesQuotationQuery): Observable<PagedResult<SalesQuotationSummary>> {
    let params = new HttpParams().set('page', query.page).set('pageSize', query.pageSize);
    for (const key of ['keyword', 'from', 'to', 'status', 'sort'] as const) {
      const value = query[key]?.trim();
      if (value) params = params.set(key, value);
    }
    if (query.customerId != null) params = params.set('customerId', query.customerId);
    return this.http.get<PagedResult<SalesQuotationSummary>>(API, { params });
  }

  get(id: number): Observable<SalesQuotationDetail> {
    return this.http.get<SalesQuotationDetail>(`${API}/${id}`);
  }

  /** Parse the three editable decimals as text before JSON.parse can round them. */
  getEditable(id: number): Observable<SalesQuotationEditableDetail> {
    return this.http.get(`${API}/${id}`, { responseType: 'text' }).pipe(map(raw => {
      const exact = raw.replace(/("(?:quantity|unitPrice|discountRate)"\s*:\s*)(-?(?:0|[1-9]\d*)(?:\.\d+)?(?:[eE][+-]?\d+)?)/g,
        '$1"$2"');
      return JSON.parse(exact) as SalesQuotationEditableDetail;
    }));
  }

  create(draft: QuotationDraftForm): Observable<SalesQuotationDetail> {
    return this.http.post<SalesQuotationDetail>(API, quotationDraftJson(draft),
      { headers: new HttpHeaders({ 'Content-Type': 'application/json' }) });
  }

  replace(id: number, version: number, draft: QuotationDraftForm): Observable<SalesQuotationDetail> {
    if (!Number.isSafeInteger(id) || id <= 0 || !Number.isSafeInteger(version) || version <= 0)
      throw new Error('Invalid quotation identity or version');
    return this.http.put<SalesQuotationDetail>(`${API}/${id}`,
      `{"version":${version},"draft":${quotationDraftJson(draft)}}`,
      { headers: new HttpHeaders({ 'Content-Type': 'application/json' }) });
  }
}
