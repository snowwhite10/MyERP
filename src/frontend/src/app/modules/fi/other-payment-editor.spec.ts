import { ANIMATION_MODULE_TYPE, signal } from '@angular/core';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideHttpClient } from '@angular/common/http';
import { TestBed } from '@angular/core/testing';
import { APP_ICONS } from '../../core/icons';
import { provideNzIcons } from 'ng-zorro-antd/icon';
import { CompanyContextService } from '../../core/company/company-context.service';
import { PermissionService } from '../../core/auth/permission.service';
import { provideTestTranslations } from '../../../testing/i18n-fixtures';
import { OtherPaymentEditor } from './other-payment-editor';

describe('FI-OP-UI-007 editor actions', () => {
  beforeEach(() => TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting(),
    ...provideTestTranslations('en'), provideNzIcons(APP_ICONS), { provide: ANIMATION_MODULE_TYPE, useValue: 'NoopAnimations' },
    { provide: CompanyContextService, useValue: { current: signal({ id: 1 }) } },
    { provide: PermissionService, useValue: { has: () => true } }] }));
  afterEach(() => TestBed.inject(HttpTestingController).verify());

  it('FI-OP-UI-008 blocks empty draft before a write and shows field reasons', () => {
    const fixture = TestBed.createComponent(OtherPaymentEditor);
    fixture.componentRef.setInput('paymentId', null);
    fixture.detectChanges();
    const http = TestBed.inject(HttpTestingController);
    http.expectOne(r => r.url === '/api/v1/md/income-expense-categories').flush({ items: [], totalCount: 0 });
    http.expectOne(r => r.url === '/api/v1/md/fund-accounts').flush({ items: [], totalCount: 0 });
    fixture.nativeElement.querySelector('[data-testid="fi-op-save"]').click();
    fixture.detectChanges();
    expect(fixture.nativeElement.textContent).toContain('Choose an expense category');
    http.expectNone(r => r.method === 'POST' && r.url === '/api/v1/ap/other-payments');
  });

  it('FI-OP-UI-009 duplicate reference keeps input, then a saved response is read back', () => {
    const fixture = TestBed.createComponent(OtherPaymentEditor);
    fixture.componentRef.setInput('paymentId', null); fixture.detectChanges();
    const http = TestBed.inject(HttpTestingController);
    http.expectOne(r => r.url === '/api/v1/md/income-expense-categories').flush({ items: [], totalCount: 0 });
    http.expectOne(r => r.url === '/api/v1/md/fund-accounts').flush({ items: [], totalCount: 0 });
    fixture.componentInstance.draft = { payeeDescription: 'Power', expenseCategoryId: 3, bankAccountId: 9,
      bankReference: 'WD-1', businessDate: '2026-11-27', amount: '12.3400', remark: '' };
    fixture.componentInstance.save();
    http.expectOne(r => r.method === 'POST' && r.url === '/api/v1/ap/other-payments')
      .flush({ errorCode: 'FI.OTHER_PAYMENT.BANK_REFERENCE_DUPLICATE' }, { status: 409, statusText: 'Conflict' });
    fixture.detectChanges();
    expect(fixture.nativeElement.textContent).toContain('already used');
    expect(fixture.componentInstance.draft.bankReference).toBe('WD-1');
    expect(fixture.nativeElement.querySelector('[data-testid="fi-op-reference-error"]')?.textContent).toContain('already used');
    expect(fixture.nativeElement.querySelector('[data-testid="fi-op-reference"]').getAttribute('aria-invalid')).toBe('true');
    fixture.componentInstance.draft.bankReference = 'WD-2';
    fixture.componentInstance.save();
    http.expectOne(r => r.method === 'POST' && r.url === '/api/v1/ap/other-payments').flush({ id: 7 });
    http.expectOne('/api/v1/ap/other-payments/7').flush('{"id":7,"documentNo":"OP7","payeeDescription":"Power",' +
      '"expenseCategoryId":3,"bankAccountId":9,"bankReference":"WD-2","businessDate":"2026-11-27",' +
      '"currency":"TWD","amount":12.3400,"remark":null,"status":"Draft","version":1,"journalId":null}');
    fixture.detectChanges();
    expect(fixture.nativeElement.textContent).toContain('Payment saved');
    expect(fixture.componentInstance.detail()?.amount).toBe('12.3400');
  });

  const savedJson = (status: string, version: number, journalId: number | null) =>
    `{"id":7,"documentNo":"OP7","payeeDescription":"Power","expenseCategoryId":3,"bankAccountId":9,"bankReference":"WD-2",` +
    `"businessDate":"2026-11-27","currency":"TWD","amount":12.34,"remark":null,"status":"${status}","version":${version},"journalId":${journalId}}`;
  const openSaved = () => {
    const fixture = TestBed.createComponent(OtherPaymentEditor);
    fixture.componentRef.setInput('paymentId', 7); fixture.detectChanges();
    const http = TestBed.inject(HttpTestingController);
    http.expectOne(r => r.url === '/api/v1/md/income-expense-categories').flush({ items: [], totalCount: 0 });
    http.expectOne(r => r.url === '/api/v1/md/fund-accounts').flush({ items: [], totalCount: 0 });
    http.expectOne('/api/v1/ap/other-payments/7').flush(savedJson('Draft', 1, null));
    fixture.detectChanges();
    return { fixture, http };
  };

  it('FI-OP-UI-018 zero-step submit that posts says approved and posted, not waiting', () => {
    const { fixture, http } = openSaved();
    fixture.nativeElement.querySelector('[data-testid="fi-op-submit"]').click();
    http.expectOne('/api/v1/ap/other-payments/7/submit')
      .flush({ id: 7, documentNo: 'OP7', status: 'Posted', version: 3, approval: null, posting: { journalId: 55 } });
    http.expectOne('/api/v1/ap/other-payments/7').flush(savedJson('Posted', 3, 55));
    fixture.detectChanges();
    const notice = fixture.nativeElement.querySelector('[data-testid="fi-op-notice"]').textContent as string;
    expect(notice).toContain('OP7');
    expect(notice).toContain('approved and posted');
    expect(notice).not.toContain('waiting');
  });

  it('FI-OP-UI-019 submit that waits for approval says nothing is posted yet', () => {
    const { fixture, http } = openSaved();
    fixture.nativeElement.querySelector('[data-testid="fi-op-submit"]').click();
    http.expectOne('/api/v1/ap/other-payments/7/submit')
      .flush({ id: 7, documentNo: 'OP7', status: 'Pending', version: 2, approval: {}, posting: null });
    http.expectOne('/api/v1/ap/other-payments/7').flush(savedJson('Pending', 2, null));
    fixture.detectChanges();
    const notice = fixture.nativeElement.querySelector('[data-testid="fi-op-notice"]').textContent as string;
    expect(notice).toContain('OP7');
    expect(notice).toContain('waiting for approval');
    expect(notice).toContain('Nothing has been posted');
  });

  it('FI-OP-UI-020 delete reports the deleted document number to the list', () => {
    const { fixture, http } = openSaved();
    const deleted: string[] = [];
    fixture.componentInstance.deleted.subscribe(no => deleted.push(no));
    vi.spyOn(window, 'confirm').mockReturnValue(true);
    fixture.nativeElement.querySelector('[data-testid="fi-op-delete"]').click();
    http.expectOne(r => r.method === 'DELETE' && r.url === '/api/v1/ap/other-payments/7').flush(null, { status: 204, statusText: 'No Content' });
    expect(deleted).toEqual(['OP7']);
  });

  it('FI-OP-UI-021 reload button after a conflict keeps its natural width', () => {
    const { fixture, http } = openSaved();
    fixture.nativeElement.querySelector('[data-testid="fi-op-submit"]').click();
    http.expectOne('/api/v1/ap/other-payments/7/submit')
      .flush({ errorCode: 'FI.OTHER_PAYMENT.VERSION_CONFLICT' }, { status: 409, statusText: 'Conflict' });
    fixture.detectChanges();
    const reload = fixture.nativeElement.querySelector('[data-testid="fi-op-reload"]') as HTMLElement;
    expect(reload.parentElement?.classList.contains('recover')).toBe(true);
  });
});
