import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ANIMATION_MODULE_TYPE } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideNzIcons } from 'ng-zorro-antd/icon';
import { of } from 'rxjs';
import { provideTestTranslations } from '../../../testing/i18n-fixtures';
import { PermissionService } from '../../core/auth/permission.service';
import { APP_ICONS } from '../../core/icons';
import { ErpDialogService } from '../../shared/dialogs/dialog.service';
import { AccountPage } from './account-page';
import { ACCOUNT_API, Account } from './account.service';

const assets: Account = { id: 1, code: '1', name: 'Assets', parentId: null, accountType: 'Asset',
  balanceDirection: 'Debit', isActive: true, isPostable: false, version: 2 };
const cash: Account = { id: 2, code: '1101', name: 'Cash', parentId: 1, accountType: 'Asset',
  balanceDirection: 'Debit', isActive: true, isPostable: true, version: 1 };
const revenue: Account = { id: 3, code: '4', name: 'Revenue', parentId: null, accountType: 'Revenue',
  balanceDirection: 'Credit', isActive: false, isPostable: false, version: 1 };
const isList = (r: { url: string; method: string }) => r.url === ACCOUNT_API && r.method === 'GET';

describe('MD-ACC-UI account page', () => {
  const confirm = vi.fn(() => of(true));
  let granted = new Set<string>();
  beforeEach(() => {
    confirm.mockClear();
    granted = new Set(['MD.ACCOUNT.VIEW', 'MD.ACCOUNT.CREATE', 'MD.ACCOUNT.EDIT', 'MD.ACCOUNT.DELETE']);
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting(),
      ...provideTestTranslations('en'), provideNzIcons(APP_ICONS),
      { provide: ANIMATION_MODULE_TYPE, useValue: 'NoopAnimations' },
      { provide: ErpDialogService, useValue: { confirm } },
      { provide: PermissionService, useValue: { has: (code: string) => granted.has(code),
        hasAny: (codes: string[]) => codes.some((code) => granted.has(code)) } }] });
  });
  afterEach(() => TestBed.inject(HttpTestingController).verify());

  function create() {
    const fixture = TestBed.createComponent(AccountPage);
    TestBed.tick();
    const http = TestBed.inject(HttpTestingController);
    http.expectOne(isList).flush([revenue, cash, assets]);
    fixture.detectChanges();
    return { pageComponent: fixture.componentInstance, http, fixture, element: fixture.nativeElement as HTMLElement };
  }

  it('shows the chart as a tree with type, direction and postable flags, and filters on the client', () => {
    const { pageComponent, fixture, element } = create();
    expect(element.querySelector('[data-testid="md-account-title"]')?.textContent).toContain('Chart of accounts');
    const rows = [...element.querySelectorAll('tr[data-testid^="md-account-row-"]')];
    expect(rows.map((row) => row.getAttribute('data-testid'))).toEqual(['md-account-row-1', 'md-account-row-1101', 'md-account-row-4']);
    expect(rows[1].getAttribute('aria-level')).toBe('2');
    expect(rows[1].textContent).toContain('Asset');
    expect(rows[1].textContent).toContain('Debit');
    expect(rows[1].textContent).toContain('Postable');
    expect(rows[0].textContent).toContain('Summary');
    expect(rows[2].textContent).toContain('Credit');
    expect(element.querySelector('[data-testid="md-account-add-child-4"]')).toBeNull();
    expect(element.querySelector('[data-testid="md-account-add-child-1"]')).not.toBeNull();

    pageComponent.searchControl.setValue('cash');
    pageComponent.search();
    fixture.detectChanges();
    expect(pageComponent.visibleRows().map((x) => x.code)).toEqual(['1', '1101']);
  });

  it('creates a root account with the type default direction', () => {
    const { pageComponent, http } = create();
    pageComponent.openCreate(null);
    expect(pageComponent.parent()).toBeNull();
    pageComponent.form.patchValue({ code: '2', name: 'Liabilities' });
    pageComponent.save();
    http.expectNone((r) => r.method === 'POST');
    expect(pageComponent.form.controls.accountType.invalid).toBe(true);
    pageComponent.form.controls.accountType.setValue('Liability');
    expect(pageComponent.form.controls.balanceDirection.value).toBe('Credit');
    pageComponent.save();
    pageComponent.save();
    const post = http.expectOne((r) => r.url === ACCOUNT_API && r.method === 'POST');
    expect(post.request.body).toEqual({ code: '2', name: 'Liabilities', parentId: null,
      accountType: 'Liability', balanceDirection: 'Credit' });
    post.flush({ ...revenue, id: 5, code: '2', accountType: 'Liability', isActive: true });
    expect(pageComponent.dialogOpen()).toBe(false);
    http.expectOne(isList).flush([revenue, cash, assets, { ...revenue, id: 5, code: '2', accountType: 'Liability' }]);
    expect(pageComponent.rows().map((x) => x.code)).toEqual(['1', '1101', '2', '4']);
  });

  it('creates a subaccount with the parent code prefix and inherited type, keeping the dialog open on errors', () => {
    const { pageComponent, http } = create();
    pageComponent.openCreate(pageComponent.rows().find((x) => x.code === '1101')!);
    expect(pageComponent.form.controls.code.value).toBe('1101');
    expect(pageComponent.form.controls.accountType.disabled).toBe(true);
    expect(pageComponent.form.controls.accountType.value).toBe('Asset');
    expect(pageComponent.form.controls.balanceDirection.value).toBe('Debit');
    pageComponent.form.patchValue({ name: 'Petty cash' });
    pageComponent.save();
    expect(pageComponent.form.controls.code.hasError('prefix')).toBe(true);
    http.expectNone((r) => r.method === 'POST');

    // 備抵類科目可改為貸方。
    pageComponent.form.patchValue({ code: '110101', balanceDirection: 'Credit' });
    pageComponent.save();
    const post = http.expectOne((r) => r.url === ACCOUNT_API && r.method === 'POST');
    expect(post.request.body).toEqual({ code: '110101', name: 'Petty cash', parentId: 2, balanceDirection: 'Credit' });
    post.flush({ errorCode: 'MD.ACCOUNT.PARENT_IN_USE' }, { status: 422, statusText: 'Unprocessable Entity' });
    expect(pageComponent.dialogOpen()).toBe(true);
    expect(pageComponent.saveError()).toBe('errors.MD.ACCOUNT.PARENT_IN_USE');
  });

  it('edits name, direction and status with the version; code and type stay read-only', () => {
    const { pageComponent, http } = create();
    const cashRow = pageComponent.rows().find((x) => x.code === '1101')!;
    // 備抵科目（資產類貸方）編輯時保留原方向，不被類別預設覆蓋。
    pageComponent.openEdit({ ...cashRow, balanceDirection: 'Credit' });
    expect(pageComponent.form.controls.balanceDirection.value).toBe('Credit');
    pageComponent.openEdit(cashRow);
    expect(pageComponent.form.controls.balanceDirection.value).toBe('Debit');
    expect(pageComponent.form.controls.code.disabled).toBe(true);
    expect(pageComponent.form.controls.accountType.disabled).toBe(true);
    pageComponent.form.patchValue({ name: ' Cash on hand ', balanceDirection: 'Credit', isActive: false });
    pageComponent.save();
    const put = http.expectOne(`${ACCOUNT_API}/2`);
    expect(put.request.body).toEqual({ name: 'Cash on hand', balanceDirection: 'Credit', isActive: false, version: 1 });
    put.flush({ errorCode: 'MD.ACCOUNT.IN_USE' }, { status: 409, statusText: 'Conflict' });
    expect(pageComponent.saveError()).toBe('errors.MD.ACCOUNT.IN_USE');

    pageComponent.save();
    http.expectOne(`${ACCOUNT_API}/2`).flush({ ...cash, name: 'Cash on hand', version: 2 });
    expect(pageComponent.dialogOpen()).toBe(false);
    http.expectOne(isList).flush([assets, { ...cash, name: 'Cash on hand', version: 2 }]);
  });

  it('confirms deletion with the version and shows HAS_CHILDREN', () => {
    const { pageComponent, http } = create();
    pageComponent.confirmDelete(pageComponent.rows().find((x) => x.code === '1')!);
    expect(confirm).toHaveBeenCalledWith(expect.objectContaining({ titleKey: 'md.account.deleteConfirm', params: { code: '1' } }));
    http.expectOne(`${ACCOUNT_API}/1?version=2`)
      .flush({ errorCode: 'MD.ACCOUNT.HAS_CHILDREN' }, { status: 409, statusText: 'Conflict' });
    expect(pageComponent.error()).toBe('errors.MD.ACCOUNT.HAS_CHILDREN');
    expect(pageComponent.busy()).toBe(false);

    pageComponent.confirmDelete(pageComponent.rows().find((x) => x.code === '1101')!);
    http.expectOne(`${ACCOUNT_API}/2?version=1`).flush(null);
    http.expectOne(isList).flush([assets, revenue]);
    expect(pageComponent.rows().map((x) => x.code)).toEqual(['1', '4']);
  });

  it('hides write actions without permissions and shows load errors', () => {
    granted = new Set(['MD.ACCOUNT.VIEW']);
    const fixture = TestBed.createComponent(AccountPage);
    TestBed.tick();
    TestBed.inject(HttpTestingController).expectOne(isList)
      .flush({ errorCode: 'COMMON.PERMISSION.DENIED' }, { status: 403, statusText: 'Forbidden' });
    fixture.detectChanges();
    expect(fixture.componentInstance.error()).toBe('errors.COMMON.PERMISSION.DENIED');
    expect(fixture.componentInstance.rows()).toEqual([]);
    expect((fixture.nativeElement as HTMLElement).querySelector('[data-testid="md-account-create"]')).toBeNull();
  });
});
