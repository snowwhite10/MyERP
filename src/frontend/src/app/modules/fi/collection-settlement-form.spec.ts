import { HttpErrorResponse, provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ANIMATION_MODULE_TYPE, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { ActivatedRoute, convertToParamMap, provideRouter, Router } from '@angular/router';
import { provideNzIcons } from 'ng-zorro-antd/icon';
import { of } from 'rxjs';
import { provideTestTranslations } from '../../../testing/i18n-fixtures';
import { CompanyContextService } from '../../core/company/company-context.service';
import { APP_ICONS } from '../../core/icons';
import { SelectDialogOptions } from '../../shared/dialogs/select-dialog';
import { ErpDialogService } from '../../shared/dialogs/dialog.service';
import { FI_ROUTES } from './fi.routes';
import { CollectionSettlementForm } from './collection-settlement-form';
import {
  amountUnits, SettlementDraft, settlementDraftIssues, settlementFieldErrors, settlementJson, settlementTotalText, unitsText,
} from './collection-settlement.service';

const API = '/api/v1/ar/collection-settlements';
const line = (receivableId: number, amount: string, openAmount: string | null = null) =>
  ({ receivableId, amount, openAmount, sourceType: null, sourceId: null, postingDate: null });
const draft = (over: Partial<SettlementDraft> = {}): SettlementDraft =>
  ({ receiptId: 31, customerId: 7, businessDate: '2026-11-15', remark: '', lines: [line(10, '100')], ...over });
const bad400 = (errors: Record<string, string[]>) => new HttpErrorResponse({ status: 400, error: { status: 400, errors } });

describe('FI-SETL-UI settlement form rules', () => {
  it('FI-SETL-UI-U04 exact units: 19-digit amounts add without rounding; invalid text gives null', () => {
    expect(amountUnits('999999999999999.9999')).toBe(9999999999999999999n);
    expect(amountUnits('1,000.5')).toBe(10005000n);
    expect(amountUnits('1.00000')).toBe(10000n);
    expect(amountUnits('1.00001')).toBeNull();
    expect(amountUnits('-1')).toBeNull();
    expect(amountUnits(null)).toBeNull();
    expect(unitsText(-5n)).toBe('-0.0005');
    expect(settlementTotalText(draft({ lines: [line(1, '999999999999999.9999'), line(2, '0.0001'), line(3, 'x')] })))
      .toBe('1000000000000000');
  });

  it('FI-SETL-UI-U05 issues: required collection / date / lines, date before collection, amount format, zero, over open, total over unapplied', () => {
    expect(settlementDraftIssues(draft({ receiptId: null, businessDate: '', lines: [] }), null, null))
      .toEqual({ header: { receiptId: 'receiptRequired', businessDate: 'required', lines: 'noLines' }, lines: {} });
    expect(settlementDraftIssues(draft({ businessDate: '2026-11-13' }), null, '2026-11-14').header.businessDate).toBe('beforeReceipt');
    expect(settlementDraftIssues(draft({ businessDate: '2026-11-14' }), null, '2026-11-14').header.businessDate).toBeUndefined();
    const issues = settlementDraftIssues(draft({ lines: [line(1, ''), line(2, '1.23456'), line(3, '0.0000'), line(4, '100.0001', '100'),
      line(5, '100', '100'), line(6, '1234567890123456')] }), null, null);
    expect(issues.lines).toEqual({ 0: 'required', 1: 'amountFormat', 2: 'amountPositive', 3: 'exceedsOpen', 5: 'amountFormat' });
    expect(settlementDraftIssues(draft({ lines: [line(1, '60'), line(2, '40.0001')] }), '100', null).header.lines).toBe('exceedsUnapplied');
    expect(settlementDraftIssues(draft({ lines: [line(1, '60'), line(2, '40')] }), '100', null)).toEqual({ header: {}, lines: {} });
    expect(settlementDraftIssues(draft({ lines: Array.from({ length: 1001 }, (_, i) => line(i + 1, '1')) }), null, null).header.lines)
      .toBe('tooManyLines');
    expect(settlementDraftIssues(draft({ remark: 'x'.repeat(501) }), null, null).header.remark).toBe('tooLong');
  });

  it('FI-SETL-UI-U06 request JSON keeps every digit in screen order, adds the version and sends a blank remark as null', () => {
    const json = settlementJson(draft({ remark: '  ', lines: [line(12, '999999999999999.9999'), line(10, '1,000.5')] }), 4);
    expect(json).toBe('{"version":4,"receiptId":31,"businessDate":"2026-11-15","remark":null,'
      + '"lines":[{"receivableId":12,"amount":999999999999999.9999},{"receivableId":10,"amount":1000.5}]}');
    expect(() => settlementJson(draft({ lines: [line(1, 'abc')] }))).toThrow();
  });

  it('FI-SETL-UI-U07 400 field names map to header fields and line indexes; other statuses map to none', () => {
    expect(settlementFieldErrors(bad400({ ReceiptId: ['x'], 'lines[1].amount': ['x'], '$.lines[0].ReceivableId': ['x'], lines: ['x'] })))
      .toEqual({ header: ['receiptId', 'lines'], lines: { 1: 'amount', 0: 'receivableId' } });
    expect(settlementFieldErrors(new HttpErrorResponse({ status: 422, error: { errors: { receiptId: ['x'] } } })))
      .toEqual({ header: [], lines: {} });
  });

  it('FI-SETL-UI-U08 create needs AR.SETTLEMENT.CREATE and edit needs AR.SETTLEMENT.EDIT; both guard unsaved changes', () => {
    const settle = FI_ROUTES.filter(r => r.path?.startsWith('collection-settlements/'));
    expect(settle.map(r => r.path)).toEqual(['collection-settlements/new', 'collection-settlements/:id/edit']);
    for (const route of settle) expect(route.canDeactivate?.length).toBe(1);
  });
});

describe('FI-SETL-UI settlement form', () => {
  const company = signal<{ id: number } | null>({ id: 1 });
  let select: ReturnType<typeof vi.fn>;
  function setup(id: string | null) {
    company.set({ id: 1 });
    select = vi.fn();
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([]),
      ...provideTestTranslations('en'), provideNzIcons(APP_ICONS), { provide: ANIMATION_MODULE_TYPE, useValue: 'NoopAnimations' },
      { provide: CompanyContextService, useValue: { current: company } },
      { provide: ErpDialogService, useValue: { select } },
      { provide: ActivatedRoute, useValue: { snapshot: { paramMap: convertToParamMap(id ? { id } : {}) } } }] });
    const router = TestBed.inject(Router);
    const navigate = vi.spyOn(router, 'navigateByUrl').mockResolvedValue(true);
    const fixture = TestBed.createComponent(CollectionSettlementForm);
    TestBed.tick();
    const http = TestBed.inject(HttpTestingController);
    const element = fixture.nativeElement as HTMLElement;
    const by = (testId: string) => element.querySelector<HTMLElement>(`[data-testid="${testId}"]`);
    const text = (testId: string) => by(testId)?.textContent?.replace(/\s+/g, ' ').trim() ?? null;
    return { fixture, http, element, by, text, view: fixture.componentInstance, navigate };
  }
  afterEach(() => TestBed.inject(HttpTestingController).verify());
  const receipt = (over: object = {}) => ({ id: 31, documentNo: 'RC202611031', customerId: 7, businessDate: '2026-11-10', currency: 'TWD',
    amount: '500', bankAccountId: 3, bankReference: 'TT', status: 'Posted', version: 3, unappliedAmount: '250.5', customerCode: 'C001',
    customerName: 'Acme', bankAccountCode: null, bankAccountName: null, ...over });
  const open = (receivableId: number, openAmount: string, postingDate = '2026-11-01') =>
    ({ receivableId, sourceType: 'Invoice', sourceId: 900 + receivableId, postingDate, currency: 'TWD', originalAmount: openAmount, openAmount });
  const detail = (status: string, over: object = {}) => JSON.stringify({ id: 5, documentNo: 'ST202611005', customerId: 7, businessDate: '2026-11-14',
    currency: 'TWD', receiptId: 31, receiptDocumentNo: 'RC202611031', totalAmount: '150', status, version: 2, customerCode: 'C001',
    customerName: 'Acme', receiptUnappliedAmount: '250.5', remark: 'r', createdAt: '', createdBy: 1, updatedAt: null, updatedBy: null,
    lines: [{ lineNo: 1, receivableId: 10, amount: '100', receivableOpenAmount: '100' }, { lineNo: 2, receivableId: 12, amount: '50', receivableOpenAmount: '80' }],
    ...over });

  it('FI-SETL-UI-012 create: empty save names the collection and lines; receivables need a collection first; no request', () => {
    const { fixture, http, view, text, by } = setup(null);
    fixture.detectChanges();
    expect((by('fi-settle-add-lines') as HTMLButtonElement).disabled).toBe(true);
    expect(text('fi-settle-add-hint')).toContain('Choose a collection first');
    expect(text('fi-settle-form-no-lines')).toBe('No receivables added yet.');
    view.save(); fixture.detectChanges();
    expect(text('fi-settle-issue-receiptId')).toBe('Choose a posted collection.');
    expect(text('fi-settle-issue-lines')).toBe('Add at least one receivable.');
    view.pickReceivables();
    expect(select).not.toHaveBeenCalled();
    http.expectNone(() => true);
  });

  it('FI-SETL-UI-013 create: collection then receivables (as of the date) prefill amounts capped by unapplied; exact save; list notice state', () => {
    const { fixture, http, view, text, navigate } = setup(null);
    fixture.detectChanges();
    view.draft.businessDate = '2026-11-05';
    select.mockImplementationOnce((options: SelectDialogOptions<unknown>) => {
      options.load({ page: 1, pageSize: 10, keyword: 'RC' }).subscribe();
      return of([receipt()]);
    });
    view.pickReceipt();
    const receipts = http.expectOne(r => r.url === '/api/v1/ar/sales-collections');
    expect(receipts.request.params.get('status')).toBe('Posted');
    expect(receipts.request.params.get('keyword')).toBe('RC');
    receipts.flush(JSON.stringify({ items: [], totalCount: 0, page: 1, pageSize: 10 }));
    fixture.detectChanges();
    expect(text('fi-settle-form-receipt')).toBe('RC202611031');
    expect(text('fi-settle-form-customer')).toBe('Customer: C001 · Acme');
    expect(view.draft.businessDate).toBe('2026-11-10');
    expect(text('fi-settle-form-unapplied')).toBe('250.5');
    select.mockImplementationOnce((options: SelectDialogOptions<unknown>) => {
      expect(options.multiple).toBe(true);
      options.load({ page: 2, pageSize: 10, keyword: '' }).subscribe();
      return of([open(10, '200'), open(12, '80'), open(14, '30')]);
    });
    view.pickReceivables();
    const reads = http.expectOne(r => r.url === `${API}/open-receivables`);
    expect(['customerId', 'postedOnOrBefore', 'page'].map(k => reads.request.params.get(k))).toEqual(['7', '2026-11-10', '2']);
    reads.flush('{"items":[],"totalCount":0,"page":2,"pageSize":10}');
    fixture.detectChanges();
    expect(view.draft.lines.map(l => l.amount)).toEqual(['200', '50.5', '']);
    expect(text('fi-settle-form-open-12')).toBe('80');
    expect(text('fi-settle-form-line-12')).toContain('Invoice #912');
    view.removeLine(2);
    view.draft.lines[0].amount = '199.9999';
    fixture.detectChanges();
    expect(text('fi-settle-form-total')).toBe('250.4999');
    expect(text('fi-settle-form-remaining')).toBe('0.0001');
    view.save();
    const post = http.expectOne(r => r.url === API && r.method === 'POST');
    expect(post.request.body).toBe('{"receiptId":31,"businessDate":"2026-11-10","remark":null,'
      + '"lines":[{"receivableId":10,"amount":199.9999},{"receivableId":12,"amount":50.5}]}');
    post.flush(detail('Draft', { totalAmount: 250.4999 }).replace('"totalAmount":250.4999', '"totalAmount":250.4999'));
    expect(navigate).toHaveBeenCalledWith('/ar/collection-settlements', { state: { collectionSettlementSaved:
      { no: 'ST202611005', created: true, total: '250.4999', currency: 'TWD', count: 2 } } });
    expect(view.hasUnsavedChanges()).toBe(false);
  });

  it('FI-SETL-UI-014 line over its open amount and total over unapplied are refused on screen without a request', () => {
    const { fixture, http, view, text } = setup(null);
    fixture.detectChanges();
    select.mockReturnValueOnce(of([receipt({ unappliedAmount: '100' })])).mockReturnValueOnce(of([open(10, '80'), open(12, '80')]));
    view.pickReceipt(); view.pickReceivables();
    view.draft.lines[0].amount = '80.0001'; view.draft.lines[1].amount = '20';
    view.save(); fixture.detectChanges();
    expect(text('fi-settle-line-issue-10')).toBe("The amount is more than the receivable's open amount.");
    expect(text('fi-settle-line-issue-12')).toBeNull();
    view.draft.lines[0].amount = '80'; view.draft.lines[1].amount = '20.0001';
    view.save(); fixture.detectChanges();
    expect(text('fi-settle-issue-lines')).toBe("The total (100.0001) is more than the collection's unapplied cash (100).");
    expect(text('fi-settle-form-remaining')).toBe('-0.0001');
    // Correcting a value drops the message about it at once (no stale "total is more than" text).
    view.amountChanged(0, '79'); fixture.detectChanges();
    expect(text('fi-settle-issue-lines')).toBeNull();
    view.issues.set({ header: { lines: 'exceedsUnapplied' }, lines: {} });
    view.amountChanged(0, '81'); fixture.detectChanges();
    expect(text('fi-settle-issue-lines')).toBe("The total (101.0001) is more than the collection's unapplied cash (100).");
    view.issues.set({ header: {}, lines: { 0: 'exceedsOpen' } });
    view.amountChanged(0, '80'); fixture.detectChanges();
    expect(text('fi-settle-line-issue-10')).toBeNull();
    http.expectNone(API);
  });

  it('FI-SETL-UI-015 choosing another customer\'s collection drops the chosen lines; the same customer keeps them', () => {
    const { fixture, view } = setup(null);
    fixture.detectChanges();
    select.mockReturnValueOnce(of([receipt()])).mockReturnValueOnce(of([open(10, '80')]))
      .mockReturnValueOnce(of([receipt({ id: 32, documentNo: 'RC2' })])).mockReturnValueOnce(of([receipt({ id: 33, customerId: 8 })]));
    view.pickReceipt(); view.pickReceivables(); view.pickReceipt();
    expect(view.draft.lines.length).toBe(1);
    expect(view.draft.receiptId).toBe(32);
    view.pickReceipt();
    expect(view.draft.lines.length).toBe(0);
    expect(view.draft.customerId).toBe(8);
  });

  it('FI-SETL-UI-016 edit loads saved lines; 400 line fields and 422 lineErrors are shown on their own line', () => {
    const { fixture, http, view, text, by } = setup('5');
    http.expectOne(`${API}/5`).flush(detail('Rejected').replace('"totalAmount":"150"', '"totalAmount":150'));
    fixture.detectChanges();
    expect(text('fi-settle-form-no')).toBe('ST202611005');
    expect(by('fi-settle-form-rejected')).not.toBeNull();
    expect(text('fi-settle-form-total')).toBe('150');
    expect(text('fi-settle-form-remaining')).toBe('100.5');
    view.save();
    const put = http.expectOne(r => r.url === `${API}/5` && r.method === 'PUT');
    expect(JSON.parse(put.request.body as string).version).toBe(2);
    put.flush(JSON.stringify({ status: 400, errors: { 'lines[1].amount': ['bad'] } }), { status: 400, statusText: 'Bad' });
    fixture.detectChanges();
    expect(text('fi-settle-line-issue-12')).toContain('was refused');
    expect(text('fi-settle-line-issue-10')).toBeNull();
    view.save();
    http.expectOne(r => r.method === 'PUT').flush(JSON.stringify({ status: 422, errorCode: 'FI.COLLECTION_WRITEOFF.ALLOCATION_INVALID',
      lineErrors: [{ line: 1, receivableId: 10, reason: 'FI.COLLECTION_WRITEOFF.LINE.EXCEEDS_OPEN' },
        { line: 2, receivableId: 12, reason: 'FI.COLLECTION_WRITEOFF.LINE.SOMETHING_NEW' }] }), { status: 422, statusText: 'x' });
    // "Open now" is re-read for the customer (all dates) so the refused line shows what can still be applied.
    const refresh = http.expectOne(r => r.url === `${API}/open-receivables`);
    expect(['customerId', 'postedOnOrBefore', 'pageSize'].map(k => refresh.request.params.get(k))).toEqual(['7', null, '200']);
    refresh.flush('{"items":[{"receivableId":10,"sourceType":"Invoice","sourceId":910,"postingDate":"2026-11-01","currency":"TWD",'
      + '"originalAmount":100,"openAmount":30.5}],"totalCount":1,"page":1,"pageSize":200}');
    fixture.detectChanges();
    expect(text('fi-settle-form-open-10')).toBe('30.5');
    expect(text('fi-settle-form-open-12')).toBe('0');
    expect(view.draft.lines.map(l => l.amount)).toEqual(['100', '50']);
    expect(by('fi-settle-form-error')).not.toBeNull();
    expect(text('fi-settle-line-issue-10')).not.toBeNull();
    expect(text('fi-settle-line-issue-10')).not.toContain('fi.');
    expect(text('fi-settle-line-issue-12')).toBe('this line cannot be settled.');
    expect(by('fi-settle-form-reload')).toBeNull();
    // With the refreshed open amounts the screen itself now refuses the old values until they are corrected.
    view.save(); fixture.detectChanges();
    expect(text('fi-settle-line-issue-10')).toBe("The amount is more than the receivable's open amount.");
    view.amountChanged(0, '30.5'); view.removeLine(1);
    view.save();
    http.expectOne(r => r.method === 'PUT').flush(JSON.stringify({ status: 422, errorCode: 'FI.COLLECTION_WRITEOFF.RECEIPT_INSUFFICIENT' }),
      { status: 422, statusText: 'x' });
    fixture.detectChanges();
    expect(text('fi-settle-issue-receiptId')).not.toBeNull();
    expect(text('fi-settle-line-issue-10')).toBeNull();
  });

  it('FI-SETL-UI-017 a pending settlement opens read-only with the reason; a stale save (409) offers reload', async () => {
    const { fixture, http, view, by, text } = setup('5');
    http.expectOne(`${API}/5`).flush(detail('Pending'));
    fixture.detectChanges();
    expect(text('fi-settle-form-locked')).toContain('waiting for approval');
    expect((by('fi-settle-save') as HTMLButtonElement).disabled).toBe(true);
    await fixture.whenStable(); fixture.detectChanges();
    expect((by('fi-settle-form-amount-10') as HTMLInputElement).disabled).toBe(true);
    expect(view.hasUnsavedChanges()).toBe(false);
    view.save(); http.expectNone(`${API}/5`);
    view.lockedKey.set(null);
    view.save();
    http.expectOne(r => r.method === 'PUT').flush(JSON.stringify({ status: 409, errorCode: 'COMMON.CONCURRENCY.CONFLICT' }), { status: 409, statusText: 'x' });
    fixture.detectChanges();
    by('fi-settle-form-reload')!.click();
    http.expectOne(`${API}/5`).flush(detail('Draft', { version: 3 }));
    fixture.detectChanges();
    expect(by('fi-settle-form-error')).toBeNull();
    expect(by('fi-settle-form-locked')).toBeNull();
  });

  it('FI-SETL-UI-018 a 500 on read shows the trace ID and no form; a company switch leaves for the list without asking', () => {
    const { fixture, http, by, text, view, navigate } = setup('5');
    http.expectOne(`${API}/5`).flush(JSON.stringify({ status: 500, traceId: 'trace-77' }), { status: 500, statusText: 'x' });
    fixture.detectChanges();
    expect(text('fi-settle-form-error')).toContain('trace-77');
    expect(by('fi-settle-form')).toBeNull();
    company.set({ id: 2 });
    TestBed.tick();
    expect(view.hasUnsavedChanges()).toBe(false);
    expect(navigate).toHaveBeenCalledWith('/ar/collection-settlements');
  });
});
