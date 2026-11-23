import { HttpRequest, provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ANIMATION_MODULE_TYPE, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { ActivatedRoute, convertToParamMap, provideRouter } from '@angular/router';
import { provideNzIcons } from 'ng-zorro-antd/icon';
import { of } from 'rxjs';
import { provideTestTranslations } from '../../../testing/i18n-fixtures';
import { PermissionService } from '../../core/auth/permission.service';
import { CompanyContextService } from '../../core/company/company-context.service';
import { APP_ICONS } from '../../core/icons';
import { ErpDialogService } from '../../shared/dialogs/dialog.service';
import { SalesReturnPage } from './return-page';

/** WU-SD-008b3b1 (ADR-195): submit and delete a saved Draft return from the 2.16 detail (REQ_IO_A2.16.11–13). */
const API = '/api/v1/sal/returns';
const summary = (status: string, version = 2) => JSON.stringify({ items: [{ id: 5, documentNo: 'SR001', orderId: 9,
  orderDocumentNo: 'SO001', customerId: 3, customerName: 'Acme', businessDate: '2028-02-29', currencyCode: 'USD',
  baseCurrencyCode: 'TWD', status, originalGross: 35, baseGross: 1085, postedAt: null, lineCount: 2, version }],
  totalCount: 1, page: 1, pageSize: 20 });
const line = (id: number, lineNo: number, posted: boolean) => `{"id":${id},"lineNo":${lineNo},"deliveryId":30,` +
  `"deliveryDocumentNo":"DH00${lineNo}","deliveryLineId":${id + 20},"orderLineId":21,"orderLineNo":${lineNo},"itemId":7,` +
  `"itemCode":"I${lineNo}","itemName":"Bolt","warehouseId":51,"locationId":null,"quantity":1.000000,` +
  `"unbilledQuantity":1.000000,"billedQuantity":0,"originalOutboundMovementId":501,` +
  `"invMovementId":${posted ? 900 + lineNo : null},"originalNet":10,"originalTax":0.5,"originalGross":10.5,` +
  `"baseNet":310,"baseTax":15,"baseGross":325,"allocations":[],"warehouseCode":"WH1","warehouseName":"Main",` +
  `"warehouseActive":true,"locationCode":null,"locationName":null,"locationActive":null}`;
const detail = (status: string, version = 2, documentNo = 'SR001') => '{"id":5,"documentNo":"' + documentNo + '",' +
  '"requestKey":"k-1","orderId":9,"orderDocumentNo":"SO001","customerId":3,"customerCode":"C1","customerName":"Acme",' +
  '"businessDate":"2028-02-29","currencyCode":"USD","baseCurrencyCode":"TWD","exchangeRate":31,' +
  `"status":"${status}","remark":null,"approvalInstanceId":null,` +
  `"postedAt":${status === 'Posted' ? '"2028-02-29T10:15:30"' : null},"fiCreditNoteId":null,"originalNet":20,` +
  `"originalTax":1,"originalGross":21,"baseNet":620,"baseTax":30,"baseGross":650,"version":${version},` +
  `"lines":[${line(11, 1, status === 'Posted')},${line(12, 2, status === 'Posted')}]}`;
const problem = (status: number, body: object) => ({ body: JSON.stringify(body), opts: { status, statusText: 'Error' } });

describe('SD-RETURN-ACT sales return detail actions', () => {
  const company = signal<{ id: number } | null>({ id: 1 });
  let granted: Set<string>;
  let confirm: ReturnType<typeof vi.fn>;
  beforeEach(() => {
    company.set({ id: 1 });
    granted = new Set(['SAL.RETURN.VIEW', 'SAL.RETURN.SUBMIT', 'SAL.RETURN.DELETE']);
    confirm = vi.fn(() => of(true));
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([]),
      ...provideTestTranslations('en'), provideNzIcons(APP_ICONS),
      { provide: ANIMATION_MODULE_TYPE, useValue: 'NoopAnimations' },
      { provide: ActivatedRoute, useValue: { snapshot: { queryParamMap: convertToParamMap({}) } } },
      { provide: CompanyContextService, useValue: { current: company } },
      { provide: ErpDialogService, useValue: { confirm } },
      { provide: PermissionService, useValue: { has: (code: string) => granted.has(code),
        hasAny: (codes: string[]) => codes.some(code => granted.has(code)) } }] });
  });
  afterEach(() => TestBed.inject(HttpTestingController).verify());

  const isList = (r: HttpRequest<unknown>) => r.url === API && r.method === 'GET';
  function open(status: string, version = 2) {
    const fixture = TestBed.createComponent(SalesReturnPage);
    TestBed.tick();
    const http = TestBed.inject(HttpTestingController);
    http.expectOne(isList).flush(summary(status, version));
    fixture.detectChanges();
    const element = fixture.nativeElement as HTMLElement;
    const by = (id: string) => element.querySelector<HTMLElement>(`[data-testid="${id}"]`);
    const text = (id: string) => by(id)?.textContent?.replace(/\s+/g, ' ').trim() ?? null;
    by('sd-return-open-5')!.click();
    http.expectOne(`${API}/5`).flush(detail(status, version));
    fixture.detectChanges();
    const render = () => fixture.detectChanges();
    const disabled = (id: string) => (by(id) as HTMLButtonElement).disabled;
    return { fixture, http, element, by, text, render, disabled };
  }

  it('SD-RETURN-ACT-001 submits a Draft with the loaded version; a Pending reply says nothing was received or credited yet', () => {
    const { http, by, text, render } = open('Draft');
    by('sd-return-submit')!.click();
    expect(confirm.mock.calls[0][0]).toMatchObject({ titleKey: 'sd.return.action.submitTitle', params: { number: 'SR001' },
      okKey: 'sd.return.action.submit' });
    const request = http.expectOne(`${API}/5/submit`);
    expect(request.request.method).toBe('POST');
    expect(request.request.body).toBe('{"version":2}');
    request.flush(detail('Pending', 3));
    http.expectOne(`${API}/5`).flush(detail('Pending', 3));
    render();
    expect(text('sd-return-action-notice')).toContain('SR001 was submitted and is pending approval (version 3)');
    expect(text('sd-return-action-notice')).toContain('No goods were received');
    expect(text('sd-return-row-5')).toContain('Pending approval');
    expect(text('sd-return-detail-status')).toBe('Pending approval');
    expect(by('sd-return-submit')).toBeNull();
    expect(by('sd-return-delete')).toBeNull();
  });

  it('SD-RETURN-ACT-002 a zero-step submission that comes back Posted says goods were received and invoiced amounts credited', () => {
    const { http, by, text, render } = open('Draft');
    by('sd-return-submit')!.click();
    http.expectOne(`${API}/5/submit`).flush(detail('Posted', 3));
    http.expectOne(`${API}/5`).flush(detail('Posted', 3));
    render();
    expect(text('sd-return-action-notice')).toContain('needed no approval and was posted');
    expect(text('sd-return-action-notice')).toContain('received into stock');
    expect(text('sd-return-inv-11')).toBe('901');
    expect(by('sd-return-submit')).toBeNull();
  });

  it('SD-RETURN-ACT-003 cancelling either confirmation sends nothing', () => {
    const { http, by, element } = open('Draft');
    confirm.mockReturnValue(of(false));
    by('sd-return-submit')!.click();
    by('sd-return-delete')!.click();
    http.expectNone(`${API}/5/submit`);
    http.expectNone(r => r.method === 'DELETE');
    expect(element.querySelector('[data-testid="sd-return-action-notice"]')).toBeNull();
  });

  it('SD-RETURN-ACT-004 buttons follow status and permission; a Rejected return explains it must be revised first', () => {
    let page = open('Pending');
    expect(page.by('sd-return-submit')).toBeNull();
    expect(page.by('sd-return-delete')).toBeNull();
    expect(page.by('sd-return-rejected-hint')).toBeNull();
    page.fixture.destroy();
    page = open('Rejected');
    expect(page.by('sd-return-submit')).toBeNull();
    expect(page.by('sd-return-delete')).toBeNull();
    expect(page.text('sd-return-rejected-hint')).toContain('must be revised and saved');
    page.fixture.destroy();
    page = open('Posted');
    expect(page.by('sd-return-submit')).toBeNull();
    expect(page.by('sd-return-delete')).toBeNull();
    page.fixture.destroy();
    granted = new Set(['SAL.RETURN.VIEW', 'SAL.RETURN.DELETE']);
    page = open('Draft');
    expect(page.by('sd-return-submit')).toBeNull();
    expect(page.by('sd-return-delete')).not.toBeNull();
    page.fixture.destroy();
    granted = new Set(['SAL.RETURN.VIEW']);
    page = open('Draft');
    expect(page.by('sd-return-submit')).toBeNull();
    expect(page.by('sd-return-delete')).toBeNull();
    page.fixture.destroy();
    page = open('Rejected');
    expect(page.by('sd-return-rejected-hint')).toBeNull();
  });

  it('SD-RETURN-ACT-005 a reply that is not this return at a newer version is never success; actions pause until Refresh', async () => {
    const { http, by, text, render, disabled, element } = open('Draft');
    by('sd-return-submit')!.click();
    http.expectOne(`${API}/5/submit`).flush(detail('Pending', 2));
    render();
    await new Promise(resolve => setTimeout(resolve));
    expect(by('sd-return-action-notice')).toBeNull();
    expect(text('sd-return-action-stale')).toContain('could not confirm the result');
    expect(disabled('sd-return-submit')).toBe(true);
    expect(disabled('sd-return-delete')).toBe(true);
    expect(element.ownerDocument.activeElement).toBe(by('sd-return-action-refresh'));
    by('sd-return-action-refresh')!.click();
    http.expectOne(`${API}/5`).flush(detail('Draft', 2));
    render();
    expect(by('sd-return-action-stale')).toBeNull();
    expect(disabled('sd-return-submit')).toBe(false);
    by('sd-return-submit')!.click();
    http.expectOne(`${API}/5/submit`).flush(detail('Draft', 3, 'SR999'));
    render();
    expect(by('sd-return-action-notice')).toBeNull();
    expect(text('sd-return-action-stale')).toContain('could not confirm the result');
  });

  it('SD-RETURN-ACT-006 SNAPSHOT_STALE explains the sources changed, says nothing was posted and keeps actions available', () => {
    const { http, by, text, render, disabled } = open('Draft');
    by('sd-return-submit')!.click();
    const failure = problem(409, { errorCode: 'SAL.RETURN.SNAPSHOT_STALE', traceId: 't-stale' });
    http.expectOne(`${API}/5/submit`).flush(failure.body, failure.opts);
    render();
    expect(text('sd-return-action-error')).toContain('no longer match their sources');
    expect(text('sd-return-action-unchanged')).toContain('still Draft');
    expect(by('sd-return-action-stale')).toBeNull();
    expect(disabled('sd-return-submit')).toBe(false);
    expect(text('sd-return-detail-status')).toBe('Draft');
  });

  it('SD-RETURN-ACT-007 a version conflict, a missing return and a changed status pause actions with their own reason', () => {
    let page = open('Draft');
    page.by('sd-return-submit')!.click();
    let failure = problem(409, { errorCode: 'COMMON.CONCURRENCY.CONFLICT' });
    page.http.expectOne(`${API}/5/submit`).flush(failure.body, failure.opts);
    page.render();
    expect(page.text('sd-return-action-stale')).toContain('Someone else changed this return');
    expect(page.by('sd-return-action-unchanged')).toBeNull();
    page.fixture.destroy();
    page = open('Draft');
    page.by('sd-return-submit')!.click();
    failure = problem(404, { errorCode: 'SAL.RETURN.NOT_FOUND' });
    page.http.expectOne(`${API}/5/submit`).flush(failure.body, failure.opts);
    page.render();
    expect(page.text('sd-return-action-stale')).toContain('no longer exists or is outside your data scope');
    page.fixture.destroy();
    page = open('Draft');
    page.by('sd-return-submit')!.click();
    failure = problem(422, { errorCode: 'SAL.RETURN.INVALID_STATUS' });
    page.http.expectOne(`${API}/5/submit`).flush(failure.body, failure.opts);
    page.render();
    expect(page.text('sd-return-action-stale')).toContain('status has changed');
    expect(page.disabled('sd-return-submit')).toBe(true);
  });

  it('SD-RETURN-ACT-008 a 422 with lineNo marks that saved line and names the reason under it', () => {
    const { http, by, text, render } = open('Draft');
    by('sd-return-submit')!.click();
    const failure = problem(422, { errorCode: 'MD.WAREHOUSE.INACTIVE', lineNo: 2, traceId: 't-422' });
    http.expectOne(`${API}/5/submit`).flush(failure.body, failure.opts);
    render();
    expect(text('sd-return-action-error')).toContain('line 2');
    expect(by('sd-return-line-12')!.classList.contains('failed')).toBe(true);
    expect(text('sd-return-line-error-12')!.length).toBeGreaterThan(10);
    expect(by('sd-return-line-error-11')).toBeNull();
    expect(text('sd-return-action-unchanged')).toContain('still Draft');
    // An unknown line number is not marked; the reason is still shown.
    by('sd-return-submit')!.click();
    const other = problem(422, { errorCode: 'SAL.RETURN.RETURN_EXCEEDED', lineNo: 7 });
    http.expectOne(`${API}/5/submit`).flush(other.body, other.opts);
    render();
    expect(by('sd-return-line-error-12')).toBeNull();
    expect(text('sd-return-action-error')).toContain('exceeds what can still be returned');
  });

  it('SD-RETURN-ACT-009 network loss and an uncoded 500 are uncertain; a coded FI 500 rolled back and shows its trace ID', () => {
    let page = open('Draft');
    page.by('sd-return-submit')!.click();
    page.http.expectOne(`${API}/5/submit`).error(new ProgressEvent('error'), { status: 0, statusText: 'Unknown' });
    page.render();
    expect(page.text('sd-return-action-stale')).toContain('could not confirm the result');
    page.fixture.destroy();
    page = open('Draft');
    page.by('sd-return-submit')!.click();
    let failure = problem(500, { traceId: 't-500' });
    page.http.expectOne(`${API}/5/submit`).flush(failure.body, failure.opts);
    page.render();
    expect(page.text('sd-return-action-stale')).toContain('could not confirm the result');
    expect(page.text('sd-return-action-error')).toContain('t-500');
    page.fixture.destroy();
    page = open('Draft');
    page.by('sd-return-submit')!.click();
    failure = problem(500, { errorCode: 'SAL.RETURN.FI_POSTING_FAILED', traceId: 't-fi' });
    page.http.expectOne(`${API}/5/submit`).flush(failure.body, failure.opts);
    page.render();
    expect(page.by('sd-return-action-stale')).toBeNull();
    expect(page.text('sd-return-action-error')).toContain('Posting the return credit to finance failed');
    expect(page.text('sd-return-action-error')).toContain('t-fi');
    expect(page.text('sd-return-action-unchanged')).toContain('still Draft');
  });

  it('SD-RETURN-ACT-010 deletes a Draft after a danger confirmation, says what was not affected and reloads the list', () => {
    const { http, by, text, render } = open('Draft');
    by('sd-return-delete')!.click();
    expect(confirm.mock.calls[0][0]).toMatchObject({ titleKey: 'sd.return.action.deleteTitle', params: { number: 'SR001' },
      danger: true });
    const request = http.expectOne(r => r.method === 'DELETE');
    expect(request.request.url).toBe(`${API}/5`);
    expect(request.request.params.get('version')).toBe('2');
    request.flush(null, { status: 204, statusText: 'No Content' });
    http.expectOne(isList).flush(JSON.stringify({ items: [], totalCount: 0, page: 1, pageSize: 20 }));
    render();
    expect(text('sd-return-action-notice')).toContain('Draft SR001 was deleted');
    expect(text('sd-return-action-notice')).toContain('No stock or receivable was affected');
    expect(by('sd-return-detail')).toBeNull();
  });

  it('SD-RETURN-ACT-011 a refused delete says nothing was deleted; a conflict pauses', () => {
    const { http, by, text, render } = open('Draft');
    by('sd-return-delete')!.click();
    http.expectOne(r => r.method === 'DELETE').flush({ errorCode: 'COMMON.PERMISSION.DENIED', traceId: 't-403' },
      { status: 403, statusText: 'Forbidden' });
    render();
    expect(text('sd-return-action-error')).toBeTruthy();
    expect(text('sd-return-action-unchanged')).toContain('Nothing was deleted');
    by('sd-return-delete')!.click();
    http.expectOne(r => r.method === 'DELETE').flush({ errorCode: 'COMMON.CONCURRENCY.CONFLICT' },
      { status: 409, statusText: 'Conflict' });
    render();
    expect(text('sd-return-action-stale')).toContain('Someone else changed this return');
    expect(by('sd-return-detail')).not.toBeNull();
  });

  it('SD-RETURN-ACT-012 opening another return or switching company discards a pending action reply', () => {
    const { fixture, http, by, render } = open('Draft');
    by('sd-return-submit')!.click();
    const pending = http.expectOne(`${API}/5/submit`);
    company.set({ id: 2 });
    TestBed.tick();
    http.expectOne(isList).flush(summary('Draft'));
    pending.flush(detail('Pending', 3));
    render();
    expect(by('sd-return-action-notice')).toBeNull();
    expect(by('sd-return-detail')).toBeNull();
    expect(fixture.componentInstance.acting()).toBeNull();
  });

  it('SD-RETURN-ACT-013 a Draft detail links to the edit page for SAL.RETURN.EDIT', () => {
    granted.add('SAL.RETURN.EDIT');
    const { by } = open('Draft');
    expect(by('sd-return-edit')!.getAttribute('href')).toBe('/sal/returns/5/edit');
    expect(by('sd-return-edit')!.textContent!.trim()).toBe('Edit');
  });

  it('SD-RETURN-ACT-014 a Rejected detail offers the edit link next to the revise hint', () => {
    granted.add('SAL.RETURN.EDIT');
    const { by, text } = open('Rejected');
    expect(by('sd-return-edit')!.getAttribute('href')).toBe('/sal/returns/5/edit');
    expect(text('sd-return-rejected-hint')).toContain('must be revised and saved');
  });

  it('SD-RETURN-ACT-015 no edit link without SAL.RETURN.EDIT', () => {
    expect(open('Draft').by('sd-return-edit')).toBeNull();
  });

  it('SD-RETURN-ACT-016 Pending and Posted returns have no edit link even with SAL.RETURN.EDIT', () => {
    granted.add('SAL.RETURN.EDIT');
    expect(open('Pending').by('sd-return-edit')).toBeNull();
  });
});
