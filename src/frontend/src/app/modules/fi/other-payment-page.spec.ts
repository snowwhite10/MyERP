import { HttpRequest, provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ANIMATION_MODULE_TYPE, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideNzIcons } from 'ng-zorro-antd/icon';
import { provideTestTranslations } from '../../../testing/i18n-fixtures';
import { PermissionService } from '../../core/auth/permission.service';
import { CompanyContextService } from '../../core/company/company-context.service';
import { APP_ICONS } from '../../core/icons';
import { By } from '@angular/platform-browser';
import { OtherPaymentEditor } from './other-payment-editor';
import { OtherPaymentPage } from './other-payment-page';

const row = (status = 'Draft') => ({ id: 7, documentNo: 'OP2026110007', payeeDescription: 'Utility', businessDate: '2026-11-27',
  currency: 'TWD', amount: 'EXACT_AMOUNT', expenseCategoryId: 3, expenseCategoryCode: 'EXP', expenseCategoryName: 'Utilities',
  bankAccountId: 9, bankAccountCode: 'BANK', bankAccountName: 'Cash', bankReference: 'WD-7', status, version: 1, journalId: null });
const list = (rows: object[] = [row()]) => JSON.stringify({ items: rows, totalCount: rows.length, page: 1, pageSize: 20 })
  .replace('"EXACT_AMOUNT"', '999999999999999.9999');
const isList = (r: HttpRequest<unknown>) => r.url === '/api/v1/ap/other-payments';

describe('FI-OP-UI other payment list', () => {
  let granted: Set<string>;
  const company = signal<{ id: number } | null>({ id: 1 });
  beforeEach(() => {
    granted = new Set(['AP.OTHER_PAYMENT.VIEW', 'GL.OTHER_PAYMENT_MAP.VIEW']);
    company.set({ id: 1 });
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting(),
      ...provideTestTranslations('en'), provideNzIcons(APP_ICONS), { provide: ANIMATION_MODULE_TYPE, useValue: 'NoopAnimations' },
      { provide: CompanyContextService, useValue: { current: company } },
      { provide: PermissionService, useValue: { has: (code: string) => granted.has(code) } }] });
  });
  afterEach(() => TestBed.inject(HttpTestingController).verify());
  function create(body = list()) {
    const fixture = TestBed.createComponent(OtherPaymentPage);
    TestBed.tick();
    const http = TestBed.inject(HttpTestingController);
    http.expectOne(isList).flush(body);
    if (granted.has('GL.OTHER_PAYMENT_MAP.VIEW')) http.expectOne('/api/v1/gl/other-payment-account-map').flush({ items: [
      { expenseCategoryId: 3, configured: false, expenseCategoryActive: true, expenseAccount: null },
    ] });
    fixture.detectChanges();
    const element = fixture.nativeElement as HTMLElement;
    return { fixture, http, element, view: fixture.componentInstance };
  }

  it('FI-OP-UI-003 labels a non-AP expense, exact amount, and missing account setup', () => {
    const { element } = create();
    expect(element.textContent).toContain('does not settle a purchase invoice');
    expect(element.textContent).toContain('999,999,999,999,999.9999 TWD');
    expect(element.querySelector('[data-testid="fi-op-map-7"]')?.textContent).toContain('Missing expense account');
    expect(element.textContent).toContain('Draft');
  });

  it('FI-OP-UI-002 without GL map permission does not request or guess account setup', () => {
    granted.delete('GL.OTHER_PAYMENT_MAP.VIEW');
    const { element } = create();
    expect(element.querySelector('[data-testid="fi-op-map-7"]')?.textContent).toContain('Check with a GL map viewer');
  });

  it('FI-OP-UI-004 distinguishes empty, filtered empty and invalid date range', () => {
    const { fixture, http, element, view } = create(list([]));
    expect(element.textContent).toContain('No other payments yet.');
    view.status = 'Posted'; view.search();
    http.expectOne(r => isList(r) && r.params.get('status') === 'Posted').flush(list([]));
    http.expectOne('/api/v1/gl/other-payment-account-map').flush({ items: [] });
    fixture.detectChanges();
    expect(element.textContent).toContain('No payments match these filters.');
    view.from = '2026-11-30'; view.to = '2026-11-01'; view.search(); fixture.detectChanges();
    expect(element.querySelector('[data-testid="fi-op-range-error"]')?.textContent).toContain('End date');
    http.expectNone(isList);
  });

  it('FI-OP-UI-004 500 shows traceId and retry; mapping failure is never called missing', () => {
    const { fixture, http, element, view } = create();
    view.load();
    http.expectOne(isList).flush({ errorCode: 'UNKNOWN', traceId: 'trace-77' }, { status: 500, statusText: 'Server Error' });
    http.expectOne('/api/v1/gl/other-payment-account-map').flush('error', { status: 500, statusText: 'Server Error' });
    fixture.detectChanges();
    expect(element.textContent).toContain('trace-77');
    element.querySelector<HTMLButtonElement>('[data-testid="fi-op-retry"]')!.click();
    http.expectOne(isList).flush(list());
    http.expectOne('/api/v1/gl/other-payment-account-map').flush('error', { status: 500, statusText: 'Server Error' });
    fixture.detectChanges();
    expect(element.querySelector('[data-testid="fi-op-map-7"]')?.textContent).toContain('unavailable');
  });

  it('FI-OP-UI-010 a dirty inline editor survives filter reset and blocks route leave', () => {
    granted.add('AP.OTHER_PAYMENT.CREATE');
    const { fixture, http, view, element } = create();
    view.openCreate(); fixture.detectChanges();
    http.expectOne(r => r.url === '/api/v1/md/income-expense-categories').flush({ items: [], totalCount: 0 });
    http.expectOne(r => r.url === '/api/v1/md/fund-accounts').flush({ items: [], totalCount: 0 });
    const child = fixture.debugElement.query(By.directive(OtherPaymentEditor)).componentInstance as OtherPaymentEditor;
    child.draft.payeeDescription = 'Power'; fixture.detectChanges();
    expect(view.hasUnsavedChanges()).toBe(true);
    view.reset(); http.expectOne(isList).flush(list());
    http.expectOne('/api/v1/gl/other-payment-account-map').flush({ items: [] });
    fixture.detectChanges();
    expect(element.querySelector('[data-testid="fi-op-editor"]')).not.toBeNull();
    expect(view.hasUnsavedChanges()).toBe(true);
  });

  it('FI-OP-UI-022 a deleted draft is confirmed by number and the refreshed list no longer has it', () => {
    granted.add('AP.OTHER_PAYMENT.DELETE');
    const { fixture, http, view, element } = create();
    view.openDetail(7); fixture.detectChanges();
    http.expectOne(r => r.url === '/api/v1/md/income-expense-categories').flush({ items: [], totalCount: 0 });
    http.expectOne(r => r.url === '/api/v1/md/fund-accounts').flush({ items: [], totalCount: 0 });
    http.expectOne('/api/v1/ap/other-payments/7').flush(list().replace(/^.*?\[(.*)\].*$/, '$1'));
    fixture.detectChanges();
    vi.spyOn(window, 'confirm').mockReturnValue(true);
    element.querySelector<HTMLButtonElement>('[data-testid="fi-op-delete"]')!.click();
    http.expectOne(r => r.method === 'DELETE').flush(null, { status: 204, statusText: 'No Content' });
    http.expectOne(isList).flush(list([]));
    http.expectOne('/api/v1/gl/other-payment-account-map').flush({ items: [] });
    fixture.detectChanges();
    expect(element.querySelector('[data-testid="fi-op-editor"]')).toBeNull();
    expect(element.querySelector('[data-testid="fi-op-deleted"]')?.textContent).toContain('Draft payment OP2026110007 was deleted');
    expect(element.querySelector('[data-testid="fi-op-row-7"]')).toBeNull();
  });
});
