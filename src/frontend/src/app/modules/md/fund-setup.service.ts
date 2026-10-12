import { HttpClient, HttpParams } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { Observable } from 'rxjs';
import { apiErrorKey } from '../../core/http/api-error';
import { PagedResult } from '../../shared/list-page/table-query';
import { Account } from './account.service';

export type FundType = 'Cash' | 'Bank';
export type IncomeExpenseDirection = 'Income' | 'Expense';
export interface FundAccount { id: number; code: string; name: string; fundType: FundType; currencyId: number;
  bankName: string | null; bankAccountNo: string | null; ledgerAccountId: number | null; isActive: boolean; version: number }
export interface IncomeExpenseCategory { id: number; code: string; name: string; direction: IncomeExpenseDirection;
  ledgerAccountId: number | null; isActive: boolean; version: number }
export interface FundDraft { code: string; name: string; fundType: FundType; currencyId: number;
  bankName: string | null; bankAccountNo: string | null; ledgerAccountId: number | null }
export interface IncomeExpenseDraft { code: string; name: string; direction: IncomeExpenseDirection; ledgerAccountId: number | null }
export interface FundQuery { page: number; pageSize: number; keyword?: string; sort?: string;
  fundType?: FundType | ''; direction?: IncomeExpenseDirection | '' }

export function eligibleAccounts(rows: readonly Account[], kind: 'fund' | IncomeExpenseDirection): Account[] {
  const types = kind === 'fund' ? ['Asset'] : kind === 'Income' ? ['Revenue'] : ['Expense', 'Cost'];
  return rows.filter(x => x.isActive && x.isPostable && types.includes(x.accountType));
}

export function normalizeFundDraft(draft: FundDraft): FundDraft {
  return { ...draft, code: draft.code.trim().toUpperCase(), name: draft.name.trim(),
    bankName: draft.fundType === 'Bank' ? draft.bankName?.trim() || null : null,
    bankAccountNo: draft.fundType === 'Bank' ? draft.bankAccountNo?.trim() || null : null };
}

abstract class FundSetupApi<T, D> {
  private readonly http = inject(HttpClient);
  protected constructor(private readonly url: string, private readonly prefix: string) {}
  errorKey(error: unknown): string {
    return apiErrorKey(error, ['DUPLICATE_CODE', 'NOT_FOUND', 'IN_USE', 'INVALID_CURRENCY', 'INVALID_LEDGER_ACCOUNT',
      'INACTIVE', 'DIRECTION_MISMATCH'].map(x => `${this.prefix}.${x}`).concat(['COMMON.CONCURRENCY.CONFLICT', 'COMMON.PERMISSION.DENIED']));
  }
  list(query: FundQuery): Observable<PagedResult<T>> {
    let params = new HttpParams().set('page', query.page).set('pageSize', query.pageSize);
    if (query.keyword?.trim()) params = params.set('keyword', query.keyword.trim());
    if (query.sort) params = params.set('sort', query.sort);
    if (query.fundType) params = params.set('fundType', query.fundType);
    if (query.direction) params = params.set('direction', query.direction);
    return this.http.get<PagedResult<T>>(this.url, { params });
  }
  create(body: D): Observable<T> { return this.http.post<T>(this.url, body); }
  update(id: number, body: object): Observable<T> { return this.http.put<T>(`${this.url}/${id}`, body); }
  remove(id: number, version: number): Observable<void> {
    return this.http.delete<void>(`${this.url}/${id}`, { params: new HttpParams().set('version', version) });
  }
}

@Injectable({ providedIn: 'root' })
export class FundAccountService extends FundSetupApi<FundAccount, FundDraft> {
  constructor() { super('/api/v1/md/fund-accounts', 'MD.FUND_ACCOUNT'); }
  override create(body: FundDraft): Observable<FundAccount> { return super.create(normalizeFundDraft(body)); }
  override update(id: number, body: FundDraft & { isActive: boolean; version: number }): Observable<FundAccount> {
    const { currencyId: _currencyId, code: _code, ...rest } = normalizeFundDraft(body);
    return super.update(id, rest);
  }
}

@Injectable({ providedIn: 'root' })
export class IncomeExpenseCategoryService extends FundSetupApi<IncomeExpenseCategory, IncomeExpenseDraft> {
  constructor() { super('/api/v1/md/income-expense-categories', 'MD.INCOME_EXPENSE_CATEGORY'); }
  override create(body: IncomeExpenseDraft): Observable<IncomeExpenseCategory> {
    return super.create({ ...body, code: body.code.trim().toUpperCase(), name: body.name.trim() });
  }
  override update(id: number, body: IncomeExpenseDraft & { isActive: boolean; version: number }): Observable<IncomeExpenseCategory> {
    return super.update(id, { name: body.name.trim(), ledgerAccountId: body.ledgerAccountId,
      isActive: body.isActive, version: body.version });
  }
}
