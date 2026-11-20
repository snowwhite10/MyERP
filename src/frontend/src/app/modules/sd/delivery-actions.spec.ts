import { HttpRequest, provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ANIMATION_MODULE_TYPE, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { provideNzIcons } from 'ng-zorro-antd/icon';
import { of } from 'rxjs';
import { provideTestTranslations } from '../../../testing/i18n-fixtures';
import { PermissionService } from '../../core/auth/permission.service';
import { CompanyContextService } from '../../core/company/company-context.service';
import { APP_ICONS } from '../../core/icons';
import { ErpDialogService } from '../../shared/dialogs/dialog.service';
import { SalesDeliveryPage } from './delivery-page';

/** WU-SD-004c2b2a (ADR-150): submit / withdraw / delete from the saved outbound detail (REQ_IO_A2.7.9–12). */
const API = '/api/v1/sal/deliveries';
const summary = (status: string, version = 3) => JSON.stringify({ items: [{ id: 5, documentNo: 'DH001', orderId: 9,
  orderDocumentNo: 'SO001', customerId: 3, customerName: 'Acme', businessDate: '2028-02-29', status, lineCount: 2,
  totalQuantity: 2, version }], totalCount: 1, page: 1, pageSize: 20 });
const detail = (status: string, version = 3, history = true) => JSON.stringify({ id: 5, documentNo: 'DH001', orderId: 9,
  orderDocumentNo: 'SO001', customerId: 3, customerCode: 'C1', customerName: 'Acme', businessDate: '2028-02-29', status,
  remark: null, approvalInstanceId: null, postedAt: status === 'Posted' ? '2028-02-29T10:15:30' : null, version,
  lines: [{ id: 11, lineNo: 1, orderLineId: 21, orderLineNo: 1, itemId: 7, itemCode: 'I1', itemName: 'Bolt', warehouseId: 2,
    locationId: null, quantity: 1, tracking: [], invMovementId: status === 'Posted' ? 88 : null },
  { id: 12, lineNo: 2, orderLineId: 22, orderLineNo: 2, itemId: 8, itemCode: 'I2', itemName: 'Nut', warehouseId: 2,
    locationId: null, quantity: 1, tracking: [], invMovementId: status === 'Posted' ? 89 : null }],
  approvalHistory: history ? [] : null });
const problem = (status: number, body: object) => ({ body: JSON.stringify(body), opts: { status, statusText: 'Error' } });

describe('SD-DEL-ACT sales outbound detail actions', () => {
  const company = signal<{ id: number } | null>({ id: 1 });
  let granted: Set<string>;
  let confirm: ReturnType<typeof vi.fn>;
  beforeEach(() => {
    company.set({ id: 1 });
    granted = new Set(['SAL.DELIVERY.VIEW', 'SAL.DELIVERY.SUBMIT', 'SAL.DELIVERY.DELETE']);
    confirm = vi.fn(() => of(true));
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([]),
      ...provideTestTranslations('en'), provideNzIcons(APP_ICONS),
      { provide: ANIMATION_MODULE_TYPE, useValue: 'NoopAnimations' },
      { provide: CompanyContextService, useValue: { current: company } },
      { provide: ErpDialogService, useValue: { confirm } },
      { provide: PermissionService, useValue: { has: (code: string) => granted.has(code),
        hasAny: (codes: string[]) => codes.some(code => granted.has(code)) } }] });
  });
  afterEach(() => TestBed.inject(HttpTestingController).verify());

  const isList = (r: HttpRequest<unknown>) => r.url === API && r.method === 'GET';
  function open(status: string, version = 3) {
    const fixture = TestBed.createComponent(SalesDeliveryPage);
    TestBed.tick();
    const http = TestBed.inject(HttpTestingController);
    http.expectOne(isList).flush(summary(status, version));
    fixture.detectChanges();
    const element = fixture.nativeElement as HTMLElement;
    const by = (id: string) => element.querySelector<HTMLElement>(`[data-testid="${id}"]`);
    const text = (id: string) => by(id)?.textContent?.replace(/\s+/g, ' ').trim() ?? null;
    by('sd-delivery-open-5')!.click();
    http.expectOne(`${API}/5`).flush(detail(status, version));
    fixture.detectChanges();
    const render = () => fixture.detectChanges();
    return { fixture, http, element, by, text, render };
  }

  it('SD-DEL-ACT-010 submits a Draft with the loaded version, confirms Pending from the reply and refreshes row and detail', () => {
    const { http, by, text, render } = open('Draft');
    expect(by('sd-delivery-withdraw')).toBeNull();
    by('sd-delivery-submit')!.click();
    expect(confirm.mock.calls[0][0]).toMatchObject({ titleKey: 'sd.delivery.action.submitTitle', params: { number: 'DH001' } });
    const request = http.expectOne(`${API}/5/submit`);
    expect(request.request.method).toBe('POST');
    expect(request.request.body).toBe('{"version":3}');
    request.flush(detail('Pending', 4, false));
    http.expectOne(`${API}/5`).flush(detail('Pending', 4));
    render();
    expect(text('sd-delivery-action-notice')).toContain('DH001 was submitted and is pending approval (version 4)');
    expect(text('sd-delivery-action-notice')).toContain('Stock has not changed');
    expect(text('sd-delivery-row-5')).toContain('Pending approval');
    expect(text('sd-delivery-detail-status')).toBe('Pending approval');
    expect(by('sd-delivery-submit')).toBeNull();
    expect(by('sd-delivery-delete')).toBeNull();
    expect(by('sd-delivery-withdraw')).not.toBeNull();
  });

  it('SD-DEL-ACT-011 a zero-step submission that comes back Posted says stock was issued', () => {
    const { http, by, text, render } = open('Draft');
    by('sd-delivery-submit')!.click();
    http.expectOne(`${API}/5/submit`).flush(detail('Posted', 4, false));
    http.expectOne(`${API}/5`).flush(detail('Posted', 4));
    render();
    expect(text('sd-delivery-action-notice')).toContain('needed no approval and was posted; stock was issued');
    expect(text('sd-delivery-movement-11')).toBe('88');
    expect(by('sd-delivery-submit')).toBeNull();
  });

  it('SD-DEL-ACT-012 cancelling a confirmation sends nothing', () => {
    const { http, by, element } = open('Draft');
    confirm.mockReturnValueOnce(of(false));
    by('sd-delivery-submit')!.click();
    http.expectNone(`${API}/5/submit`);
    expect(element.querySelector('[data-testid="sd-delivery-action-notice"]')).toBeNull();
  });

  it('SD-DEL-ACT-013 withdraws a Pending outbound; a reply that is not a newer Draft is never shown as success', () => {
    const { http, by, text, render } = open('Pending', 4);
    expect(by('sd-delivery-submit')).toBeNull();
    expect(by('sd-delivery-delete')).toBeNull();
    by('sd-delivery-withdraw')!.click();
    expect(confirm.mock.calls[0][0]).toMatchObject({ titleKey: 'sd.delivery.action.withdrawTitle' });
    const request = http.expectOne(`${API}/5/withdraw`);
    expect(request.request.body).toBe('{"version":4}');
    request.flush(detail('Pending', 4, false));
    render();
    expect(by('sd-delivery-action-notice')).toBeNull();
    expect(text('sd-delivery-action-stale')).toContain('could not confirm the result');
    expect((by('sd-delivery-withdraw') as HTMLButtonElement).disabled).toBe(true);
    by('sd-delivery-action-refresh')!.click();
    http.expectOne(`${API}/5`).flush(detail('Pending', 4));
    render();
    expect(by('sd-delivery-action-stale')).toBeNull();
    by('sd-delivery-withdraw')!.click();
    http.expectOne(`${API}/5/withdraw`).flush(detail('Draft', 5, false));
    http.expectOne(`${API}/5`).flush(detail('Draft', 5));
    render();
    expect(text('sd-delivery-action-notice')).toContain('withdrawn to Draft (version 5)');
    expect(text('sd-delivery-row-5')).toContain('Draft');
  });

  it('SD-DEL-ACT-014 a 422 with lineNo marks that saved line and says the outbound is still Draft', () => {
    const { http, by, text, render } = open('Draft');
    by('sd-delivery-submit')!.click();
    const failure = problem(422, { errorCode: 'SAL.DELIVERY.TRACKING_REQUIRED', lineNo: 2, traceId: 't-422' });
    http.expectOne(`${API}/5/submit`).flush(failure.body, failure.opts);
    render();
    expect(text('sd-delivery-line-error-12')).toContain('needs lot or serial numbers');
    expect(by('sd-delivery-line-12')!.classList.contains('failed')).toBe(true);
    expect(by('sd-delivery-line-error-11')).toBeNull();
    expect(text('sd-delivery-action-error')).toContain('line 2');
    expect(text('sd-delivery-action-unchanged')).toContain('still Draft');
    expect(text('sd-delivery-detail-status')).toBe('Draft');
    expect((by('sd-delivery-submit') as HTMLButtonElement).disabled).toBe(false);
  });

  it('SD-DEL-ACT-015 INV insufficient stock and balance refusals explain the cause and change nothing', () => {
    const { http, by, text, render } = open('Draft');
    by('sd-delivery-submit')!.click();
    const stock = problem(422, { errorCode: 'INV.STOCK.INSUFFICIENT' });
    http.expectOne(`${API}/5/submit`).flush(stock.body, stock.opts);
    render();
    expect(text('sd-delivery-action-error')).toContain('Insufficient stock');
    expect(text('sd-delivery-action-unchanged')).toContain('still Draft');
    expect(text('sd-delivery-action-stock-hint')).toContain('warehouse, location');
    by('sd-delivery-submit')!.click();
    const balance = problem(422, { errorCode: 'SAL.DELIVERY.ORDER_BALANCE_EXCEEDED' });
    http.expectOne(`${API}/5/submit`).flush(balance.body, balance.opts);
    render();
    expect(text('sd-delivery-action-error')).toContain('exceeds the order');
    expect(by('sd-delivery-action-stock-hint')).toBeNull();
  });

  it('SD-DEL-ACT-016 409, 404 and a changed status pause actions until a refresh and focus Refresh', async () => {
    const { http, by, text, render, fixture, element } = open('Draft');
    document.body.appendChild(element);
    const cases: [object, number, string][] = [[{ errorCode: 'COMMON.CONCURRENCY.CONFLICT' }, 409, 'Someone else changed'],
      [{ errorCode: 'SAL.DELIVERY.NOT_FOUND' }, 404, 'no longer exists'],
      [{ errorCode: 'SAL.DELIVERY.INVALID_STATUS' }, 422, 'status has changed']];
    for (const [body, status, message] of cases) {
      by('sd-delivery-submit')!.click();
      const failure = problem(status, body);
      http.expectOne(`${API}/5/submit`).flush(failure.body, failure.opts);
      render();
      expect(text('sd-delivery-action-stale')).toContain(message);
      await fixture.whenStable();
      expect(document.activeElement).toBe(by('sd-delivery-action-refresh'));
      expect((by('sd-delivery-submit') as HTMLButtonElement).disabled).toBe(true);
      expect((by('sd-delivery-delete') as HTMLButtonElement).disabled).toBe(true);
      by('sd-delivery-action-refresh')!.click();
      http.expectOne(`${API}/5`).flush(detail('Draft', 3));
      render();
    }
  });

  it('SD-DEL-ACT-017 500 and network failure never claim success and keep the trace ID', () => {
    const { http, by, text, render, element } = open('Draft');
    by('sd-delivery-submit')!.click();
    const failure = problem(500, { traceId: 'trace-act' });
    http.expectOne(`${API}/5/submit`).flush(failure.body, failure.opts);
    render();
    expect(text('sd-delivery-action-stale')).toContain('could not confirm the result');
    expect(text('sd-delivery-action-error')).toContain('trace-act');
    expect(element.querySelector('[data-testid="sd-delivery-action-notice"]')).toBeNull();
    by('sd-delivery-action-refresh')!.click();
    http.expectOne(`${API}/5`).flush(detail('Draft', 3));
    render();
    by('sd-delivery-submit')!.click();
    http.expectOne(`${API}/5/submit`).error(new ProgressEvent('error'), { status: 0 });
    render();
    expect(text('sd-delivery-action-stale')).toContain('could not confirm the result');
  });

  it('SD-DEL-ACT-018 a user with DELETE but not EDIT deletes a Draft after a danger confirmation; the list reloads', () => {
    expect(granted.has('SAL.DELIVERY.EDIT')).toBe(false);
    const { http, by, text, render } = open('Draft');
    expect(by('sd-delivery-edit-link')).toBeNull();
    by('sd-delivery-delete')!.click();
    expect(confirm.mock.calls[0][0]).toMatchObject({ titleKey: 'sd.delivery.action.deleteTitle', danger: true, params: { number: 'DH001' } });
    const request = http.expectOne(r => r.url === `${API}/5` && r.method === 'DELETE');
    expect(request.request.params.get('version')).toBe('3');
    request.flush(null, { status: 204, statusText: 'No Content' });
    http.expectOne(isList).flush(JSON.stringify({ items: [], totalCount: 0, page: 1, pageSize: 20 }));
    render();
    expect(text('sd-delivery-action-notice')).toContain('Draft DH001 was deleted');
    expect(by('sd-delivery-detail')).toBeNull();
    expect(by('sd-delivery-row-5')).toBeNull();
  });

  it('SD-DEL-ACT-019 actions follow permission and status; Rejected explains how to resubmit', () => {
    granted = new Set(['SAL.DELIVERY.VIEW']);
    const viewer = open('Draft');
    expect(viewer.by('sd-delivery-submit')).toBeNull();
    expect(viewer.by('sd-delivery-delete')).toBeNull();
    viewer.fixture.destroy();
    granted = new Set(['SAL.DELIVERY.VIEW', 'SAL.DELIVERY.SUBMIT', 'SAL.DELIVERY.DELETE']);
    const rejected = open('Rejected');
    expect(rejected.by('sd-delivery-submit')).toBeNull();
    expect(rejected.by('sd-delivery-delete')).toBeNull();
    expect(rejected.text('sd-delivery-rejected-hint')).toContain('edited and saved');
    rejected.fixture.destroy();
    const posted = open('Posted');
    expect(posted.by('sd-delivery-submit')).toBeNull();
    expect(posted.by('sd-delivery-withdraw')).toBeNull();
    expect(posted.by('sd-delivery-delete')).toBeNull();
  });

  it('SD-DEL-ACT-020 closing the detail while a submit is in flight ignores the late reply', () => {
    const { http, by, render, element } = open('Draft');
    by('sd-delivery-submit')!.click();
    const request = http.expectOne(`${API}/5/submit`);
    by('sd-delivery-close')!.click();
    request.flush(detail('Pending', 4, false));
    render();
    expect(element.querySelector('[data-testid="sd-delivery-action-notice"]')).toBeNull();
    expect(element.querySelector('[data-testid="sd-delivery-detail"]')).toBeNull();
  });
});
