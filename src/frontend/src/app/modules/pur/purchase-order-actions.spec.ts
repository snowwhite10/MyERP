import { HttpErrorResponse, HttpRequest, provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ANIMATION_MODULE_TYPE, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { provideNzIcons } from 'ng-zorro-antd/icon';
import { of } from 'rxjs';
import { vi } from 'vitest';
import { provideTestTranslations } from '../../../testing/i18n-fixtures';
import { PermissionService } from '../../core/auth/permission.service';
import { CompanyContextService } from '../../core/company/company-context.service';
import { APP_ICONS } from '../../core/icons';
import { ErpDialogService } from '../../shared/dialogs/dialog.service';
import { PurchaseOrderPage } from './purchase-order-page';
import { purchaseOrderActionFailure, purchaseOrderCancelReasonIssue } from './purchase-order.service';

const API = '/api/v1/pur/purchase-orders';
const ALL = ['PUR.ORDER.VIEW', 'PUR.ORDER.SUBMIT', 'PUR.ORDER.DELETE', 'PUR.ORDER.CANCEL'];
const row = (id: number, status = 'Draft', version = 3) => ({ id, documentNo: `PO-${id}`, businessDate: '2026-11-20', supplierId: 5,
  status, currency: 'TWD', baseGross: 105, lineCount: 1, version });
const list = (...rows: object[]) => JSON.stringify({ items: rows, totalCount: rows.length, page: 1, pageSize: 20 });
const line = (lineNo: number) => `{"id":${10 + lineNo},"lineNo":${lineNo},"itemId":77,"requisitionLineId":44,"awardId":null,"quantity":5,
  "unitPrice":20,"taxRateId":3,"taxRatePercent":5,"originalNet":100,"originalTax":5,"originalGross":105,"baseNet":100,"baseTax":5,
  "baseGross":105,"requisitionId":4,"requisitionDocumentNo":"PR-0004","requisitionLineNo":2,"requisitionQuantity":10,
  "requisitionOrderedQuantity":0,"requisitionRemainingQuantity":10}`;
const detail = (status = 'Draft', version = 3, lines = 1) => `{"id":1,"documentNo":"PO-1","status":"${status}","version":${version},
  "supplierId":5,"rfqId":null,"rfqDocumentNo":null,"businessDate":"2026-11-20","currency":"TWD","baseCurrencyCode":"TWD","exchangeRate":1,
  "baseNet":100,"baseTax":5,"baseGross":105,"remark":null,"lines":[${Array.from({ length: lines }, (_, i) => line(i + 1)).join(',')}],
  "approvalHistory":[]}`;
const problem = (status: number, body: object) => ({ body: JSON.stringify(body), status });
const settle = async () => { for (let i = 0; i < 5; i++) await new Promise(resolve => setTimeout(resolve, 0)); };

describe('PUR-PO-ACT rules', () => {
  const http = (status: number, error: unknown) => new HttpErrorResponse({ status, error });

  it('PUR-PO-UI-010 action failures keep shortages (exact digits), invalid lines, field reasons, trace IDs and what to reload', () => {
    const shortage = { requisitionLineId: 44, orderLineNos: [1, 2], requested: '5.123456', available: '3' };
    expect(purchaseOrderActionFailure(http(422, { errorCode: 'PUR.ORDER.REQUISITION_INSUFFICIENT', shortages: [shortage] })))
      .toEqual({ key: 'errors.PUR.ORDER.REQUISITION_INSUFFICIENT', traceId: null, shortages: [shortage], lineNos: [], fields: [], reload: 'none' });
    expect(purchaseOrderActionFailure(http(422, { errorCode: 'PUR.ORDER.INVALID_REQUISITION_SOURCE', lineNos: [3, 1] })))
      .toMatchObject({ key: 'errors.PUR.ORDER.INVALID_REQUISITION_SOURCE', lineNos: [1, 3], reload: 'none' });
    expect(purchaseOrderActionFailure(http(409, { errorCode: 'COMMON.CONCURRENCY.CONFLICT' })))
      .toMatchObject({ key: 'errors.COMMON.CONCURRENCY.CONFLICT', reload: 'detail' });
    for (const code of ['NOT_SUBMITTABLE', 'NOT_DELETABLE', 'NOT_CANCELLABLE', 'HAS_RECEIPTS', 'EMPTY'])
      expect(purchaseOrderActionFailure(http(422, { errorCode: `PUR.ORDER.${code}` }))).toMatchObject({ key: `errors.PUR.ORDER.${code}`, reload: 'detail' });
    expect(purchaseOrderActionFailure(http(404, { errorCode: 'PUR.ORDER.NOT_FOUND' }))).toMatchObject({ key: 'errors.PUR.ORDER.NOT_FOUND', reload: 'list' });
    expect(purchaseOrderActionFailure(http(422, { errorCode: 'MD.SUPPLIER.NOT_APPROVED' })).key).toBe('errors.MD.SUPPLIER.NOT_APPROVED');
    expect(purchaseOrderActionFailure(http(422, { errorCode: 'MD.ITEM.INACTIVE' })).key).toBe('errors.MD.ITEM.INACTIVE');
    expect(purchaseOrderActionFailure(http(400, { errors: { reason: ['Reason is required.'] } })))
      .toMatchObject({ key: 'errors.COMMON.REQUEST.VALIDATION_FAILED', fields: ['reason: Reason is required.'] });
    expect(purchaseOrderActionFailure(http(403, {})).key).toBe('errors.COMMON.PERMISSION.DENIED');
    expect(purchaseOrderActionFailure(http(500, { traceId: 'act-500' }))).toMatchObject({ key: 'errors.http.unknown', traceId: 'act-500' });
    expect(purchaseOrderActionFailure(http(0, null)).key).toBe('errors.http.network');
    // Malformed extensions are dropped instead of rendering junk.
    expect(purchaseOrderActionFailure(http(422, { errorCode: 'PUR.ORDER.REQUISITION_INSUFFICIENT', shortages: [{ x: 1 }], lineNos: ['a', 0] })))
      .toMatchObject({ shortages: [], lineNos: [] });
  });

  it('PUR-PO-UI-011 cancellation reason is required after trimming and at most 500 characters', () => {
    expect(purchaseOrderCancelReasonIssue('')).toBe('required');
    expect(purchaseOrderCancelReasonIssue('   ')).toBe('required');
    expect(purchaseOrderCancelReasonIssue('x')).toBeNull();
    expect(purchaseOrderCancelReasonIssue('x'.repeat(500))).toBeNull();
    expect(purchaseOrderCancelReasonIssue(' ' + 'x'.repeat(500) + ' ')).toBeNull();
    expect(purchaseOrderCancelReasonIssue('x'.repeat(501))).toBe('length');
  });
});

describe('PUR-PO-UI 介面 4.1 lifecycle actions (WU-PUR-006f1)', () => {
  let granted: string[];
  let confirm: ReturnType<typeof vi.fn>;
  let success: ReturnType<typeof vi.fn>;
  beforeEach(() => {
    granted = [...ALL];
    confirm = vi.fn(() => of(true)); success = vi.fn();
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([]),
      ...provideTestTranslations('en'), provideNzIcons(APP_ICONS), { provide: ANIMATION_MODULE_TYPE, useValue: 'NoopAnimations' },
      { provide: ErpDialogService, useValue: { confirm, success } },
      { provide: CompanyContextService, useValue: { current: signal({ id: 1 }) } },
      { provide: PermissionService, useValue: { has: (c: string) => granted.includes(c), hasAny: (c: string[]) => c.some(x => granted.includes(x)) } }] });
  });
  afterEach(() => TestBed.inject(HttpTestingController).verify());

  const isList = (r: HttpRequest<unknown>) => r.url === API && r.method === 'GET';
  const isDetail = (r: HttpRequest<unknown>) => r.url === `${API}/1` && r.method === 'GET';
  async function open(status = 'Draft', version = 3, lines = 1) {
    const fixture = TestBed.createComponent(PurchaseOrderPage);
    TestBed.tick();
    const http = TestBed.inject(HttpTestingController);
    const body = list(row(1, status, version));
    http.expectOne(isList).flush(body);
    fixture.detectChanges(); await settle();
    for (const reload of http.match(isList)) reload.flush(body);
    fixture.componentInstance.view(row(1, status, version) as never);
    http.expectOne(isDetail).flush(detail(status, version, lines));
    fixture.detectChanges(); await settle(); fixture.detectChanges();
    const element = fixture.nativeElement as HTMLElement;
    const by = (id: string) => element.querySelector<HTMLElement>(`[data-testid="${id}"]`);
    const text = (id: string) => by(id)?.textContent?.replace(/\s+/g, ' ').trim() ?? null;
    const refresh = async () => { fixture.detectChanges(); await settle(); fixture.detectChanges(); };
    return { fixture, http, by, text, element, refresh, view: fixture.componentInstance };
  }

  it('PUR-PO-UI-008 only the actions allowed by status and permission are shown; disabled buttons and no-action states explain why', async () => {
    let ctx = await open('Draft');
    expect(ctx.by('pur-order-submit')).not.toBeNull();
    expect(ctx.by('pur-order-delete')).not.toBeNull();
    expect(ctx.by('pur-order-cancel')).toBeNull();
    ctx.fixture.destroy();

    ctx = await open('Rejected');
    expect((ctx.by('pur-order-submit') as HTMLButtonElement).disabled).toBe(true);
    expect(ctx.by('pur-order-submit')!.getAttribute('aria-describedby')).toBe('pur-order-action-hint');
    expect(ctx.text('pur-order-action-hint')).toBe('A rejected order must be revised and saved before it can be submitted again.');
    expect((ctx.by('pur-order-delete') as HTMLButtonElement).disabled).toBe(false);
    ctx.fixture.destroy();

    ctx = await open('Draft', 3, 0);
    expect((ctx.by('pur-order-submit') as HTMLButtonElement).disabled).toBe(true);
    expect(ctx.text('pur-order-action-hint')).toBe('Add at least one line before submitting.');
    ctx.fixture.destroy();

    ctx = await open('Approved');
    expect(ctx.by('pur-order-submit')).toBeNull();
    expect(ctx.by('pur-order-delete')).toBeNull();
    expect(ctx.by('pur-order-cancel')).not.toBeNull();
    ctx.fixture.destroy();

    ctx = await open('PendingApproval');
    expect(ctx.element.querySelector('[data-testid="pur-order-submit"],[data-testid="pur-order-delete"],[data-testid="pur-order-cancel"]')).toBeNull();
    expect(ctx.text('pur-order-action-hint')).toBe('Waiting for approval. The approver decides in the approval inbox; no other action is available now.');
    ctx.fixture.destroy();

    ctx = await open('Cancelled');
    expect(ctx.element.querySelector('[data-testid="pur-order-submit"],[data-testid="pur-order-delete"],[data-testid="pur-order-cancel"]')).toBeNull();
    expect(ctx.text('pur-order-action-hint')).toBe('This order is cancelled. No further action is possible.');
    ctx.fixture.destroy();

    granted = ['PUR.ORDER.VIEW'];
    ctx = await open('Draft');
    expect(ctx.element.querySelector('[data-testid="pur-order-submit"],[data-testid="pur-order-delete"]')).toBeNull();
    ctx.fixture.destroy();
    ctx = await open('Approved');
    expect(ctx.by('pur-order-cancel')).toBeNull();
  });

  it('PUR-PO-UI-009 SUBMIT confirms, posts the saved version, reports the real outcome and shows the re-read saved order', async () => {
    const ctx = await open('Draft');
    confirm.mockReturnValueOnce(of(false));
    ctx.by('pur-order-submit')!.click();
    ctx.http.expectNone(`${API}/1/submit`);
    expect(confirm).toHaveBeenLastCalledWith(expect.objectContaining({ titleKey: 'pur.order.confirmSubmit',
      contentKey: 'pur.order.submitExplanation', params: { number: 'PO-1' } }));

    ctx.by('pur-order-submit')!.click();
    const request = ctx.http.expectOne(`${API}/1/submit`);
    expect(request.request.method).toBe('POST');
    expect(request.request.body).toEqual({ version: 3 });
    request.flush({ id: 1, documentNo: 'PO-1', status: 'PendingApproval', version: 4, approval: { instanceId: 9, status: 'Pending' } });
    ctx.http.expectOne(isDetail).flush(detail('PendingApproval', 4));
    await ctx.refresh();
    expect(success).toHaveBeenCalledWith('pur.order.submittedPending', { number: 'PO-1' });
    expect(ctx.element.querySelector('[data-testid="pur-order-detail-status"]')?.textContent).toContain('Pending approval');
    expect(ctx.element.querySelector('[data-testid="pur-order-row-1"] .erp-status-tag')?.textContent?.trim()).toBe('Pending approval');
    expect(ctx.by('pur-order-submit')).toBeNull();
    ctx.fixture.destroy();

    const zeroStep = await open('Draft');
    zeroStep.by('pur-order-submit')!.click();
    zeroStep.http.expectOne(`${API}/1/submit`).flush({ id: 1, documentNo: 'PO-1', status: 'Approved', version: 5, approval: { status: 'Approved' } });
    zeroStep.http.expectOne(isDetail).flush(detail('Approved', 5));
    await zeroStep.refresh();
    expect(success).toHaveBeenLastCalledWith('pur.order.submittedApproved', { number: 'PO-1' });
    expect(zeroStep.by('pur-order-cancel')).not.toBeNull();
  });

  it('PUR-PO-UI-010 a refused SUBMIT names each short requisition line with exact digits, changes nothing and keeps the order', async () => {
    const ctx = await open('Draft');
    ctx.by('pur-order-submit')!.click(); ctx.fixture.detectChanges();
    expect((ctx.by('pur-order-submit') as HTMLButtonElement).disabled).toBe(true);
    expect(ctx.text('pur-order-action-hint')).toBe('Saving… please wait.');
    const p = problem(422, {});
    ctx.http.expectOne(`${API}/1/submit`).flush(
      '{"errorCode":"PUR.ORDER.REQUISITION_INSUFFICIENT","shortages":[{"requisitionLineId":44,"orderLineNos":[1,2],"requested":12345678901.123456,"available":3}]}',
      { status: p.status, statusText: 'x' });
    await ctx.refresh();
    expect(success).not.toHaveBeenCalled();
    expect(ctx.text('pur-order-action-error')).toContain('not enough approved quantity left');
    expect(ctx.text('pur-order-action-details')).toBe('Requisition line 44 (order lines 1, 2): requested 12345678901.123456, available 3');
    expect(ctx.element.querySelector('[data-testid="pur-order-detail-status"]')?.textContent).toContain('Draft');
    expect((ctx.by('pur-order-submit') as HTMLButtonElement).disabled).toBe(false);

    ctx.by('pur-order-submit')!.click();
    ctx.http.expectOne(`${API}/1/submit`).flush(JSON.stringify({ errorCode: 'PUR.ORDER.INVALID_REQUISITION_SOURCE', lineNos: [1] }),
      { status: 422, statusText: 'x' });
    await ctx.refresh();
    expect(ctx.text('pur-order-action-error')).toContain('approved requisition line');
    expect(ctx.text('pur-order-action-details')).toBe('Order lines: 1');

    ctx.by('pur-order-submit')!.click();
    ctx.http.expectOne(`${API}/1/submit`).flush(JSON.stringify({ errorCode: 'MD.SUPPLIER.NOT_APPROVED' }), { status: 422, statusText: 'x' });
    await ctx.refresh();
    expect(ctx.text('pur-order-action-error')).not.toBe('');
    expect(ctx.by('pur-order-action-details')).toBeNull();

    ctx.by('pur-order-submit')!.click();
    ctx.http.expectOne(`${API}/1/submit`).flush(JSON.stringify({ traceId: 'sub-500' }), { status: 500, statusText: 'x' });
    await ctx.refresh();
    expect(ctx.text('pur-order-action-error')).toContain('sub-500');
    expect(success).not.toHaveBeenCalled();
  });

  it('PUR-PO-UI-012 version conflict and changed status re-read the saved order; a vanished order reloads the list', async () => {
    const ctx = await open('Draft');
    ctx.by('pur-order-submit')!.click();
    ctx.http.expectOne(`${API}/1/submit`).flush(JSON.stringify({ errorCode: 'COMMON.CONCURRENCY.CONFLICT' }), { status: 409, statusText: 'x' });
    ctx.http.expectOne(isDetail).flush(detail('PendingApproval', 6));
    await ctx.refresh();
    expect(ctx.text('pur-order-action-error')).toContain('Someone else changed this record');
    expect(ctx.text('pur-order-detail')).toContain('6');
    expect(ctx.by('pur-order-submit')).toBeNull();
    ctx.fixture.destroy();

    const gone = await open('Draft');
    gone.by('pur-order-delete')!.click();
    gone.http.expectOne(r => r.url === `${API}/1` && r.method === 'DELETE')
      .flush(JSON.stringify({ errorCode: 'PUR.ORDER.NOT_FOUND' }), { status: 404, statusText: 'x' });
    await gone.refresh();
    gone.http.expectOne(isList).flush(list());
    await gone.refresh();
    expect(gone.by('pur-order-detail')).toBeNull();
    expect(gone.text('pur-order-error')).toContain('This purchase order was not found');
    expect(success).not.toHaveBeenCalled();
  });

  it('PUR-PO-UI-013 DELETE confirms as a danger action, sends the version, closes the detail and reloads the list', async () => {
    const ctx = await open('Rejected', 7);
    confirm.mockReturnValueOnce(of(false));
    ctx.by('pur-order-delete')!.click();
    ctx.http.expectNone(r => r.method === 'DELETE');
    expect(confirm).toHaveBeenLastCalledWith(expect.objectContaining({ titleKey: 'pur.order.confirmDelete',
      contentKey: 'pur.order.deleteExplanation', danger: true, params: { number: 'PO-1' } }));
    ctx.by('pur-order-delete')!.click();
    const request = ctx.http.expectOne(r => r.url === `${API}/1` && r.method === 'DELETE');
    expect(request.request.params.get('version')).toBe('7');
    request.flush(null, { status: 204, statusText: 'No Content' });
    await ctx.refresh();
    ctx.http.expectOne(isList).flush(list());
    await ctx.refresh();
    expect(success).toHaveBeenCalledWith('pur.order.deleted', { number: 'PO-1' });
    expect(ctx.by('pur-order-detail')).toBeNull();
    expect(ctx.by('pur-order-row-1')).toBeNull();

    const refused = await open('Draft');
    refused.by('pur-order-delete')!.click();
    refused.http.expectOne(r => r.method === 'DELETE').flush(JSON.stringify({ errorCode: 'PUR.ORDER.NOT_DELETABLE' }), { status: 422, statusText: 'x' });
    refused.http.expectOne(isDetail).flush(detail('PendingApproval', 4));
    await refused.refresh();
    expect(refused.text('pur-order-action-error')).toContain('Only a draft or rejected order can be deleted');
    expect(success).toHaveBeenCalledTimes(1);
  });

  it('PUR-PO-UI-011 CANCEL needs a reason, confirms the quantity release, posts version and trimmed reason, and shows the saved result', async () => {
    const ctx = await open('Approved', 5);
    ctx.by('pur-order-cancel')!.click(); ctx.fixture.detectChanges();
    const reason = () => ctx.by('pur-order-cancel-reason') as HTMLTextAreaElement;
    expect(reason()).not.toBeNull();
    expect(reason().maxLength).toBe(500);
    expect(document.activeElement).toBe(reason());
    ctx.by('pur-order-cancel-confirm')!.click(); ctx.fixture.detectChanges();
    expect(ctx.text('pur-order-cancel-reason-error')).toBe('Enter a cancellation reason.');
    expect(reason().getAttribute('aria-invalid')).toBe('true');
    expect(confirm).not.toHaveBeenCalled();
    ctx.http.expectNone(`${API}/1/cancel`);

    ctx.by('pur-order-cancel-back')!.click(); ctx.fixture.detectChanges();
    expect(ctx.by('pur-order-cancel-form')).toBeNull();
    ctx.by('pur-order-cancel')!.click(); ctx.fixture.detectChanges();
    expect(reason().value).toBe('');

    reason().value = '  Supplier stopped the item  '; reason().dispatchEvent(new Event('input')); ctx.fixture.detectChanges();
    confirm.mockReturnValueOnce(of(false));
    ctx.by('pur-order-cancel-confirm')!.click();
    ctx.http.expectNone(`${API}/1/cancel`);
    expect(confirm).toHaveBeenLastCalledWith(expect.objectContaining({ titleKey: 'pur.order.confirmCancel',
      contentKey: 'pur.order.cancelExplanation', danger: true, params: { number: 'PO-1' } }));
    ctx.by('pur-order-cancel-confirm')!.click();
    const request = ctx.http.expectOne(`${API}/1/cancel`);
    expect(request.request.body).toEqual({ version: 5, reason: 'Supplier stopped the item' });
    request.flush({ id: 1, documentNo: 'PO-1', status: 'Cancelled', version: 6 });
    ctx.http.expectOne(isDetail).flush(detail('Cancelled', 6));
    await ctx.refresh();
    expect(success).toHaveBeenCalledWith('pur.order.cancelled', { number: 'PO-1' });
    expect(ctx.by('pur-order-cancel-form')).toBeNull();
    expect(ctx.text('pur-order-action-hint')).toBe('This order is cancelled. No further action is possible.');

    const received = await open('Approved', 5);
    received.by('pur-order-cancel')!.click(); received.fixture.detectChanges();
    const r2 = received.by('pur-order-cancel-reason') as HTMLTextAreaElement;
    r2.value = 'Wrong supplier'; r2.dispatchEvent(new Event('input'));
    received.by('pur-order-cancel-confirm')!.click();
    received.http.expectOne(`${API}/1/cancel`).flush(JSON.stringify({ errorCode: 'PUR.ORDER.HAS_RECEIPTS' }), { status: 422, statusText: 'x' });
    received.http.expectOne(isDetail).flush(detail('Approved', 5));
    await received.refresh();
    expect(received.text('pur-order-action-error')).toContain('already has goods receipts');
    expect(success).toHaveBeenCalledTimes(1);
  });
});
