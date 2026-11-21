import { HttpClient, HttpParams } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { Observable, timeout } from 'rxjs';
import { PagedResult } from '../../shared/list-page/table-query';

const API = '/api/v1/sal/document-options';

/** ADR-167: a customer or item a quotation/order can be saved with (active, approved customer; current company). */
export interface SalesDocumentOption { id: number; code: string; name: string; }
/** ADR-167: an output tax rate (Output/Both type, active code) effective on the document's business date. */
export interface SalesOutputTaxOption { id: number; code: string; name: string; ratePercent: number; }
export interface SalesDocumentOptionQuery { page: number; pageSize: number; keyword?: string; }

/**
 * Choices of the quotation and order forms (ADR-167). Needs SAL.QUOTATION.CREATE/EDIT or SAL.ORDER.CREATE/EDIT —
 * not MD.CUSTOMER/ITEM/TAX.VIEW — so sales users without master-data rights can fill the forms.
 */
@Injectable({ providedIn: 'root' })
export class SalesDocumentOptionsService {
  private readonly http = inject(HttpClient);

  customers(query: SalesDocumentOptionQuery): Observable<PagedResult<SalesDocumentOption>> {
    return this.http.get<PagedResult<SalesDocumentOption>>(`${API}/customers`, { params: optionParams(query) })
      .pipe(timeout(6000));
  }

  items(query: SalesDocumentOptionQuery): Observable<PagedResult<SalesDocumentOption>> {
    return this.http.get<PagedResult<SalesDocumentOption>>(`${API}/items`, { params: optionParams(query) })
      .pipe(timeout(6000));
  }

  /** Rates effective on `businessDate` (yyyy-MM-dd, required by the server). */
  outputTaxRates(businessDate: string, query: SalesDocumentOptionQuery): Observable<PagedResult<SalesOutputTaxOption>> {
    return this.http.get<PagedResult<SalesOutputTaxOption>>(`${API}/output-tax-rates`,
      { params: optionParams(query).set('businessDate', businessDate) }).pipe(timeout(6000));
  }
}

function optionParams(query: SalesDocumentOptionQuery): HttpParams {
  const params = new HttpParams().set('page', query.page).set('pageSize', query.pageSize);
  const keyword = query.keyword?.trim();
  return keyword ? params.set('keyword', keyword) : params;
}
