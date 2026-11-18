import { HttpClient, HttpParams } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { Observable, timeout } from 'rxjs';
import { PagedResult } from '../../shared/list-page/table-query';

const API = '/api/v1/sal/orders';
export type SalesOrderStatus = 'Draft' | 'Pending' | 'Rejected' | 'Approved' | 'Cancelled';
export interface SalesOrderSummary {
  id: number; documentNo: string; customerId: number; customerName: string;
  businessDate: string; status: SalesOrderStatus; originalGross: number; version: number;
}
export interface SalesOrderLine {
  id: number; lineNo: number; itemId: number; itemCode: string; itemName: string;
  quantity: number; deliveredQuantity: number; pendingQuantity: number;
  remainingQuantity: number; unitPrice: number; discountRate: number; taxRatePercent: number;
  originalNet: number; originalTax: number; originalGross: number;
}
export interface SalesOrderDetail extends SalesOrderSummary {
  customerCode: string; currencyCode: string; baseCurrencyCode: string;
  exchangeRate: number; sourceQuotationId: number | null; originalNet: number;
  originalTax: number; baseNet: number; baseTax: number; baseGross: number;
  remark: string | null; lines: SalesOrderLine[];
  approvalInstanceId: number | null;
  approvalHistory?: { instanceId: number; status: string; submittedAt: string;
    steps: { stepNo: number; name: string; status: string; actedBy: number | null;
      actedAt: string | null; comment: string | null }[] }[];
}
export interface SalesOrderQuery {
  page: number; pageSize: number; keyword?: string; from?: string; to?: string;
  status?: SalesOrderStatus; customerId?: number; sort?: string;
}

@Injectable({ providedIn: 'root' })
export class SalesOrderService {
  private readonly http = inject(HttpClient);
  list(query: SalesOrderQuery): Observable<PagedResult<SalesOrderSummary>> {
    let params = new HttpParams().set('page', query.page).set('pageSize', query.pageSize);
    for (const key of ['keyword', 'from', 'to', 'status', 'sort'] as const) {
      const value = query[key]?.trim();
      if (value) params = params.set(key, value);
    }
    if (query.customerId != null) params = params.set('customerId', query.customerId);
    return this.http.get<PagedResult<SalesOrderSummary>>(API, { params }).pipe(timeout(6000));
  }
  get(id: number): Observable<SalesOrderDetail> {
    return this.http.get<SalesOrderDetail>(`${API}/${id}`).pipe(timeout(6000));
  }
}
