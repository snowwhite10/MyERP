import { HttpErrorResponse, provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { firstValueFrom } from 'rxjs';
import { Account, AccountService, accountCodeValidator, defaultBalanceDirection, flattenAccountTree } from './account.service';
import { FormControl } from '@angular/forms';

const account = (id: number, code: string, parentId: number | null, extra: Partial<Account> = {}): Account =>
  ({ id, code, name: `Name ${code}`, parentId, accountType: 'Asset', balanceDirection: 'Debit',
    isActive: true, isPostable: true, version: 1, ...extra });

describe('MD account API', () => {
  beforeEach(() => TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] }));

  it('loads the chart and sends trimmed, versioned writes', async () => {
    const api = TestBed.inject(AccountService);
    const http = TestBed.inject(HttpTestingController);
    const list = firstValueFrom(api.list());
    http.expectOne('/api/v1/md/accounts').flush([account(1, '1', null)]);
    expect((await list).map((x) => x.code)).toEqual(['1']);

    const root = firstValueFrom(api.create({ code: ' 2 ', name: ' Liabilities ', parentId: null,
      accountType: 'Liability', balanceDirection: 'Credit' }));
    const post = http.expectOne('/api/v1/md/accounts');
    expect(post.request.body).toEqual({ code: '2', name: 'Liabilities', parentId: null,
      accountType: 'Liability', balanceDirection: 'Credit' });
    post.flush(account(2, '2', null));
    await root;

    // 子科目不送類別，由後端沿用上層。
    const child = firstValueFrom(api.create({ code: '1101', name: 'Cash', parentId: 1, accountType: null, balanceDirection: 'Debit' }));
    const childPost = http.expectOne('/api/v1/md/accounts');
    expect(childPost.request.body).toEqual({ code: '1101', name: 'Cash', parentId: 1, balanceDirection: 'Debit' });
    childPost.flush(account(3, '1101', 1));
    await child;

    const updated = firstValueFrom(api.update(3, { name: ' Petty cash ', balanceDirection: 'Credit', isActive: false, version: 1 }));
    const put = http.expectOne('/api/v1/md/accounts/3');
    expect(put.request.body).toEqual({ name: 'Petty cash', balanceDirection: 'Credit', isActive: false, version: 1 });
    put.flush(account(3, '1101', 1));
    await updated;

    const removed = firstValueFrom(api.remove(3, 2));
    http.expectOne('/api/v1/md/accounts/3?version=2').flush(null);
    await removed;
    http.verify();
  });

  it('maps chart rule errors and ignores other modules', () => {
    const api = TestBed.inject(AccountService);
    const problem = (errorCode: string) => new HttpErrorResponse({ status: 422, error: { errorCode } });
    for (const code of ['DUPLICATE_CODE', 'NOT_FOUND', 'INVALID_PARENT', 'CODE_PREFIX', 'TYPE_MISMATCH', 'PARENT_IN_USE',
      'HAS_ACTIVE_CHILDREN', 'PARENT_INACTIVE', 'HAS_CHILDREN', 'IN_USE']) {
      expect(api.errorKey(problem(`MD.ACCOUNT.${code}`))).toBe(`errors.MD.ACCOUNT.${code}`);
    }
    expect(api.errorKey(problem('MD.ITEM_CATEGORY.CYCLE'))).toBe('errors.http.unknown');
  });
});

describe('MD account helpers', () => {
  it('defaults the balance direction by account type', () => {
    expect((['Asset', 'Cost', 'Expense'] as const).map(defaultBalanceDirection)).toEqual(['Debit', 'Debit', 'Debit']);
    expect((['Liability', 'Equity', 'Revenue'] as const).map(defaultBalanceDirection)).toEqual(['Credit', 'Credit', 'Credit']);
  });

  it('builds the tree from parent ids in code order', () => {
    const rows = flattenAccountTree([account(3, '1102', 1), account(4, '2', null, { accountType: 'Liability' }),
      account(1, '1', null, { isPostable: false }), account(2, '1101', 1)]);
    expect(rows.map((x) => `${x.code}:${x.level}`)).toEqual(['1:0', '1101:1', '1102:1', '2:0']);
    expect(rows[0].accountType).toBe('Asset');
  });

  it('accepts digits only and requires a longer code starting with the parent code', () => {
    const control = new FormControl('', { nonNullable: true });
    const check = (value: string, parentCode: string | null) => {
      control.setValue(value);
      return accountCodeValidator(() => parentCode)(control);
    };
    expect(check('1101', null)).toBeNull();
    expect(check('', null)).toEqual({ required: true });
    expect(check('11A', null)).toEqual({ pattern: true });
    expect(check('1'.repeat(31), null)).toEqual({ pattern: true });
    expect(check('110101', '1101')).toBeNull();
    expect(check('1101', '1101')).toEqual({ prefix: true });
    expect(check('1201', '1101')).toEqual({ prefix: true });
  });
});
