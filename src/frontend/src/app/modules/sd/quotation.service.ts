import { HttpClient, HttpParams } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { Observable } from 'rxjs';
import { PagedResult } from '../../shared/list-page/table-query';

const API = '/api/v1/sal/quotations';

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
}
