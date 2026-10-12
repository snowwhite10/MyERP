import { describe, expect, it } from 'vitest';
import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { eligibleAccounts, FundAccountService, IncomeExpenseCategoryService, normalizeFundDraft } from './fund-setup.service';
import { Account } from './account.service';

const account = (id: number, accountType: Account['accountType'], isPostable = true): Account =>
  ({ id, code: String(id), name: String(id), parentId: null, accountType, balanceDirection: 'Debit', isActive: true, isPostable, version: 1 });

describe('MD-FUND-UI-001 fund form rules', () => {
  it('limits ledger options to active postable accounts of the required type', () => {
    const rows = [account(1, 'Asset'), account(2, 'Revenue'), account(3, 'Expense'), account(4, 'Cost'),
      account(5, 'Asset', false), { ...account(6, 'Asset'), isActive: false }];
    expect(eligibleAccounts(rows, 'fund').map(x => x.id)).toEqual([1]);
    expect(eligibleAccounts(rows, 'Income').map(x => x.id)).toEqual([2]);
    expect(eligibleAccounts(rows, 'Expense').map(x => x.id)).toEqual([3, 4]);
  });

  it('clears bank fields on cash and trims values', () => {
    expect(normalizeFundDraft({ code: ' c1 ', name: ' Cash ', fundType: 'Cash', currencyId: 1,
      bankName: ' Bank ', bankAccountNo: ' 123 ', ledgerAccountId: null })).toMatchObject({
        code: 'C1', name: 'Cash', bankName: null, bankAccountNo: null,
      });
  });

  it('does not send immutable currency or direction on update', () => {
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
    const http = TestBed.inject(HttpTestingController);
    const funds = TestBed.inject(FundAccountService);
    funds.update(7, { code: 'OLD', name: 'Cash', fundType: 'Cash', currencyId: 99,
      bankName: 'Bank', bankAccountNo: '123', ledgerAccountId: null, isActive: false, version: 2 }).subscribe();
    const fund = http.expectOne('/api/v1/md/fund-accounts/7');
    expect(fund.request.body).toEqual({ name: 'Cash', fundType: 'Cash', bankName: null,
      bankAccountNo: null, ledgerAccountId: null, isActive: false, version: 2 });
    fund.flush({});
    const categories = TestBed.inject(IncomeExpenseCategoryService);
    categories.update(8, { code: 'OLD', name: 'Cost', direction: 'Expense', ledgerAccountId: 3,
      isActive: true, version: 4 }).subscribe();
    const category = http.expectOne('/api/v1/md/income-expense-categories/8');
    expect(category.request.body).toEqual({ name: 'Cost', ledgerAccountId: 3, isActive: true, version: 4 });
    category.flush({});
    http.verify();
  });
});
