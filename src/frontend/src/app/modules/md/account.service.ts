import { HttpClient, HttpParams } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { AbstractControl, ValidationErrors, ValidatorFn } from '@angular/forms';
import { Observable } from 'rxjs';
import { apiErrorKey } from '../../core/http/api-error';
import { flattenCategoryTree, TreeRow } from './item-category.service';

export const ACCOUNT_API = '/api/v1/md/accounts';

export type AccountType = 'Asset' | 'Liability' | 'Equity' | 'Cost' | 'Revenue' | 'Expense';
export type BalanceDirection = 'Debit' | 'Credit';
export const ACCOUNT_TYPES: readonly AccountType[] = ['Asset', 'Liability', 'Equity', 'Cost', 'Revenue', 'Expense'];
export const BALANCE_DIRECTIONS: readonly BalanceDirection[] = ['Debit', 'Credit'];

export interface Account {
  id: number;
  code: string;
  name: string;
  /** null 為根科目。 */
  parentId: number | null;
  accountType: AccountType;
  balanceDirection: BalanceDirection;
  isActive: boolean;
  /** 啟用且沒有子科目才可過帳。 */
  isPostable: boolean;
  version: number;
}

export type AccountRow = TreeRow<Account>;

export interface AccountCreate {
  code: string;
  name: string;
  parentId: number | null;
  /** 子科目傳 null，由後端沿用上層類別。 */
  accountType: AccountType | null;
  balanceDirection: BalanceDirection;
}

const KNOWN_CODES = ['MD.ACCOUNT.DUPLICATE_CODE', 'MD.ACCOUNT.NOT_FOUND', 'MD.ACCOUNT.INVALID_PARENT',
  'MD.ACCOUNT.CODE_PREFIX', 'MD.ACCOUNT.TYPE_MISMATCH', 'MD.ACCOUNT.PARENT_IN_USE', 'MD.ACCOUNT.HAS_ACTIVE_CHILDREN',
  'MD.ACCOUNT.PARENT_INACTIVE', 'MD.ACCOUNT.HAS_CHILDREN', 'MD.ACCOUNT.IN_USE',
  'COMMON.CONCURRENCY.CONFLICT', 'COMMON.PERMISSION.DENIED'];

/** 會計科目 API：GET 一次回傳公司全部科目（含停用），由前端組樹。 */
@Injectable({ providedIn: 'root' })
export class AccountService {
  private readonly http = inject(HttpClient);

  errorKey(error: unknown): string {
    return apiErrorKey(error, KNOWN_CODES);
  }

  list(): Observable<Account[]> {
    return this.http.get<Account[]>(ACCOUNT_API);
  }

  create(body: AccountCreate): Observable<Account> {
    const { accountType, ...rest } = body;
    return this.http.post<Account>(ACCOUNT_API, { ...rest, code: body.code.trim(), name: body.name.trim(),
      ...(accountType ? { accountType } : {}) });
  }

  update(id: number, body: { name: string; balanceDirection: BalanceDirection; isActive: boolean; version: number }): Observable<Account> {
    return this.http.put<Account>(`${ACCOUNT_API}/${id}`, { ...body, name: body.name.trim() });
  }

  remove(id: number, version: number): Observable<void> {
    return this.http.delete<void>(`${ACCOUNT_API}/${id}`, { params: new HttpParams().set('version', version) });
  }
}

/** 與後端相同的預設方向：資產、成本、費用為借方，其餘為貸方。 */
export function defaultBalanceDirection(type: AccountType): BalanceDirection {
  return type === 'Asset' || type === 'Cost' || type === 'Expense' ? 'Debit' : 'Credit';
}

/** 科目樹按代碼排序，上層在子層之前。 */
export function flattenAccountTree(items: readonly Account[]): AccountRow[] {
  return flattenCategoryTree(items);
}

/** 代碼 1–30 位數字；有上層時須以上層代碼開頭且較長（後端 `MD.ACCOUNT.CODE_PREFIX`）。 */
export function accountCodeValidator(parentCode: () => string | null): ValidatorFn {
  return (control: AbstractControl<string>): ValidationErrors | null => {
    const code = (control.value ?? '').trim();
    if (!code) return { required: true };
    if (!/^\d{1,30}$/.test(code)) return { pattern: true };
    const prefix = parentCode();
    return prefix !== null && (!code.startsWith(prefix) || code.length <= prefix.length) ? { prefix: true } : null;
  };
}
