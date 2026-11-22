import { HttpRequest, provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ANIMATION_MODULE_TYPE, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { provideNzIcons } from 'ng-zorro-antd/icon';
import { provideTestTranslations } from '../../../testing/i18n-fixtures';
import { PermissionService } from '../../core/auth/permission.service';
import { CompanyContextService } from '../../core/company/company-context.service';
import { APP_ICONS } from '../../core/icons';
import { SalesOrderPage } from './order-page';

/** WU-SD-006c1 (ADR-172): order submit with advisory credit preview and credit override reason (REQ_IO_A2.4.10–15). */
const API = '/api/v1/sal/orders';
const CREDIT = '/api/v1/sal/credit-checks/3';
const summary = (status: string, version = 2) => JSON.stringify({ items: [{ id: 5, documentNo: 'SO001', customerId: 3,
  customerName: 'Acme', businessDate: '2028-02-29', status, originalGross: 150, version }], totalCount: 1, page: 1, pageSize: 20 });
const order = (status: string, version = 2) => ({ id: 5, documentNo: 'SO001', customerId: 3, customerCode: 'C1',
  customerName: 'Acme', businessDate: '2028-02-29', status, originalGross: 150, version, currencyCode: 'TWD',
  baseCurrencyCode: 'TWD', exchangeRate: 1, sourceQuotationId: null, originalNet: 150, originalTax: 0, baseNet: 150,
  baseTax: 0, baseGross: 150, remark: null, approvalInstanceId: null, approvalHistory: [], lines: [] });
const credit = (status: 'WithinLimit' | 'OverLimit', excess: number | null = null) => ({ customerId: 3, orderId: 5, status,
  reasonCode: status === 'OverLimit' ? 'SAL.CREDIT.LIMIT_EXCEEDED' : null, evaluationDate: '2028-02-29', baseCurrency: 'TWD',
  limit: 1000, postedOpenAr: 600, otherCommitments: 200, candidate: 150, headroom: status === 'OverLimit' ? -(excess ?? 0) : 50,
  excess, limitVersion: 4, requiresRecheck: true });
const figures = { status: 'WithinLimit', evaluationDate: '2028-02-29', baseCurrency: 'TWD', limit: 1000, postedOpenAr: 600,
  otherCommitments: 200, candidate: 150, headroom: 50, limitVersion: 4, excess: null };
const fail = (status: number, body: object) => ({ status, statusText: 'Error' as const, body });

describe('SD-ORDER-SUBMIT-UI order submit and credit override', () => {
  const company = signal<{ id: number } | null>({ id: 1 });
  let granted: Set<string>;
  beforeEach(() => {
    granted = new Set(['SAL.ORDER.VIEW', 'SAL.ORDER.SUBMIT']);
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([]),
      ...provideTestTranslations('en'), provideNzIcons(APP_ICONS),
      { provide: ANIMATION_MODULE_TYPE, useValue: 'NoopAnimations' },
      { provide: CompanyContextService, useValue: { current: company } },
      { provide: PermissionService, useValue: { has: (code: string) => granted.has(code),
        hasAny: (codes: string[]) => codes.some(code => granted.has(code)) } }] });
  });
  afterEach(() => TestBed.inject(HttpTestingController).verify());

  const isList = (r: HttpRequest<unknown>) => r.url === API && r.method === 'GET';
  function open(status = 'Draft', version = 2) {
    const fixture = TestBed.createComponent(SalesOrderPage);
    TestBed.tick();
    const http = TestBed.inject(HttpTestingController);
    http.expectOne(isList).flush(JSON.parse(summary(status, version)));
    fixture.detectChanges();
    const element = fixture.nativeElement as HTMLElement;
    const by = <T extends HTMLElement = HTMLElement>(id: string) => element.querySelector<T>(`[data-testid="${id}"]`);
    const text = (id: string) => by(id)?.textContent?.replace(/\s+/g, ' ').trim() ?? null;
    by('sd-order-open-5')!.click();
    http.expectOne(`${API}/5`).flush(order(status, version));
    fixture.detectChanges();
    const render = () => fixture.detectChanges();
    const startWith = (preview: object | { error: ReturnType<typeof fail> }) => {
      by('sd-order-submit')!.click(); render();
      const request = http.expectOne(r => r.url === CREDIT);
      expect(request.request.params.get('orderId')).toBe('5');
      if ('error' in preview) request.flush(preview.error.body, preview.error); else request.flush(preview);
      render();
    };
    const typeReason = (value: string) => {
      const area = by<HTMLTextAreaElement>('sd-order-override-reason')!;
      area.value = value; area.dispatchEvent(new Event('input')); render();
    };
    return { fixture, http, by, text, render, startWith, typeReason };
  }

  it('SD-ORDER-SUBMIT-UI-001 within limit: preview figures, submit {version} only, success from reply, row and detail refreshed', () => {
    const { http, by, text, render, startWith } = open();
    startWith(credit('WithinLimit'));
    expect(text('sd-order-credit-status')).toBe('Within limit');
    expect(text('sd-order-credit-limit')).toBe((1000).toLocaleString(undefined, { minimumFractionDigits: 2 }));
    expect(text('sd-order-credit')).toContain('Preview only. Nothing was saved');
    expect(by('sd-order-override-reason')).toBeNull();
    expect(by('sd-order-submit-notice')).toBeNull();
    by('sd-order-submit-confirm')!.click();
    const request = http.expectOne(`${API}/5/submit`);
    expect(request.request.method).toBe('POST');
    expect(request.request.body).toBe('{"version":2}');
    request.flush({ order: order('Pending', 3), credit: figures, creditOverride: null });
    http.expectOne(`${API}/5`).flush(order('Pending', 3));
    render();
    expect(text('sd-order-submit-notice')).toContain('SO001 was submitted and is pending approval (version 3)');
    expect(by('sd-order-submit-override')).toBeNull();
    expect(text('sd-order-row-status-5')).toBe('Pending approval');
    expect(text('sd-order-detail-status')).toBe('Pending approval');
    expect(by('sd-order-submit')).toBeNull();
  });

  it('SD-ORDER-SUBMIT-UI-002 zero-step approval says Approved, not pending', () => {
    const { http, by, text, render, startWith } = open();
    startWith(credit('WithinLimit'));
    by('sd-order-submit-confirm')!.click();
    http.expectOne(`${API}/5/submit`).flush({ order: order('Approved', 3), credit: figures, creditOverride: null });
    http.expectOne(`${API}/5`).flush(order('Approved', 3));
    render();
    expect(text('sd-order-submit-notice')).toContain('needed no approval step and is Approved (version 3)');
  });

  it('SD-ORDER-SUBMIT-UI-003 over limit with OVERRIDE: reason required, trimmed reason sent, saved override restated', () => {
    granted.add('SAL.CREDIT.OVERRIDE');
    const { http, by, text, render, startWith, typeReason } = open();
    startWith(credit('OverLimit', 30));
    expect(text('sd-order-credit-status')).toBe('Over limit');
    expect(text('sd-order-credit-excess')).toContain('exceeds the available credit by 30.00 TWD');
    by('sd-order-submit-confirm')!.click(); render();
    expect(text('sd-order-override-error')).toBe('Enter the reason for overriding the credit limit.');
    typeReason('   ');
    by('sd-order-submit-confirm')!.click(); render();
    expect(text('sd-order-override-error')).toBe('Enter the reason for overriding the credit limit.');
    http.expectNone(`${API}/5/submit`);
    typeReason('  Key account, CFO agreed  ');
    by('sd-order-submit-confirm')!.click();
    const request = http.expectOne(`${API}/5/submit`);
    expect(JSON.parse(request.request.body as string)).toEqual({ version: 2, creditOverrideReason: 'Key account, CFO agreed' });
    request.flush({ order: order('Pending', 3), credit: { ...figures, status: 'OverLimit', excess: 30, headroom: -30 },
      creditOverride: { id: 9, stage: 'Submit', approvalInstanceId: 77, orderVersion: 2, reason: 'Key account, CFO agreed',
        excess: 30, baseCurrency: 'TWD', overrideBy: 4, overrideAt: '2028-02-29T10:00:00' } });
    http.expectOne(`${API}/5`).flush(order('Pending', 3));
    render();
    expect(text('sd-order-submit-override')).toBe('Credit override saved: exceeded by 30.00 TWD. Reason: Key account, CFO agreed');
  });

  it('SD-ORDER-SUBMIT-UI-004 over limit without OVERRIDE: no reason input and confirm disabled with the reason', () => {
    const { http, by, text, startWith } = open();
    startWith(credit('OverLimit', 30));
    expect(by('sd-order-override-reason')).toBeNull();
    expect(text('sd-order-override-missing')).toContain('SAL.CREDIT.OVERRIDE');
    expect(by<HTMLButtonElement>('sd-order-submit-confirm')!.disabled).toBe(true);
    expect(text('sd-order-submit-hint')).toContain('credit override permission');
    http.expectNone(`${API}/5/submit`);
  });

  it('SD-ORDER-SUBMIT-UI-005 preview within limit but server refuses LIMIT_EXCEEDED: excess shown, reason revealed, nothing saved', () => {
    granted.add('SAL.CREDIT.OVERRIDE');
    const { http, by, text, render, startWith, typeReason } = open();
    startWith(credit('WithinLimit'));
    by('sd-order-submit-confirm')!.click();
    http.expectOne(`${API}/5/submit`).flush({ errorCode: 'SAL.CREDIT.LIMIT_EXCEEDED', excess: 12.5, currency: 'TWD' },
      fail(422, {}));
    render();
    expect(text('sd-order-submit-error')).toContain('exceeds the customer\'s available credit');
    expect(text('sd-order-submit-unchanged')).toContain('still Draft');
    expect(text('sd-order-credit-excess')).toContain('12.50 TWD');
    expect(by('sd-order-override-reason')).not.toBeNull();
    typeReason('Approved by sales director');
    by('sd-order-submit-confirm')!.click();
    expect(JSON.parse(http.expectOne(`${API}/5/submit`).request.body as string).creditOverrideReason).toBe('Approved by sales director');
  });

  it('SD-ORDER-SUBMIT-UI-006 403 OVERRIDE_FORBIDDEN and 400 creditOverrideReason are named', () => {
    granted.add('SAL.CREDIT.OVERRIDE');
    const { http, by, text, render, startWith, typeReason } = open();
    startWith(credit('OverLimit', 5));
    typeReason('x');
    by('sd-order-submit-confirm')!.click();
    http.expectOne(`${API}/5/submit`).flush({ errorCode: 'SAL.CREDIT.OVERRIDE_FORBIDDEN' }, fail(403, {}));
    render();
    expect(text('sd-order-submit-error')).toContain('do not have permission to override the credit limit');
    by('sd-order-submit-confirm')!.click();
    http.expectOne(`${API}/5/submit`).flush({ errors: { creditOverrideReason: ['too long'] } }, fail(400, {}));
    render();
    expect(text('sd-order-override-error')).toContain('1–200 characters');
  });

  it('SD-ORDER-SUBMIT-UI-007 blocked credit (LIMIT_NOT_SET) disables submit; a successful recheck re-enables it', () => {
    const { http, by, text, render, startWith } = open();
    startWith({ error: fail(422, { errorCode: 'SAL.CREDIT.LIMIT_NOT_SET' }) });
    expect(text('sd-order-credit-error')).toContain('no effective credit limit');
    expect(text('sd-order-credit')).toContain('cannot be submitted, even with an override');
    expect(by<HTMLButtonElement>('sd-order-submit-confirm')!.disabled).toBe(true);
    expect(text('sd-order-submit-hint')).toContain('until a recheck succeeds');
    by('sd-order-credit-recheck')!.click(); render();
    expect(text('sd-order-submit-hint')).toContain('Wait for the credit check');
    http.expectOne(r => r.url === CREDIT).flush(credit('WithinLimit'));
    render();
    expect(by<HTMLButtonElement>('sd-order-submit-confirm')!.disabled).toBe(false);
  });

  it('SD-ORDER-SUBMIT-UI-008 preview 500 keeps submit available and shows the trace ID', () => {
    const { by, text, startWith } = open();
    startWith({ error: fail(500, { traceId: 'trace-cc-1' }) });
    expect(text('sd-order-credit-error')).toContain('trace-cc-1');
    expect(text('sd-order-credit')).toContain('You can still submit');
    expect(by<HTMLButtonElement>('sd-order-submit-confirm')!.disabled).toBe(false);
  });

  it('SD-ORDER-SUBMIT-UI-009 409 version conflict pauses until reload; reload refetches the saved order', () => {
    const { http, by, text, render, startWith } = open();
    startWith(credit('WithinLimit'));
    by('sd-order-submit-confirm')!.click();
    http.expectOne(`${API}/5/submit`).flush({ errorCode: 'COMMON.CONCURRENCY.CONFLICT' }, fail(409, {}));
    render();
    expect(text('sd-order-submit-stale')).toContain('Someone else changed this order');
    expect(by<HTMLButtonElement>('sd-order-submit-confirm')!.disabled).toBe(true);
    by('sd-order-submit-refresh')!.click();
    http.expectOne(`${API}/5`).flush(order('Draft', 3));
    render();
    expect(by('sd-order-submit-stale')).toBeNull();
    expect(by('sd-order-submit')).not.toBeNull();
  });

  it('SD-ORDER-SUBMIT-UI-010 409 FI.CREDIT.LOCK_TIMEOUT is retryable and says nothing changed', () => {
    const { http, by, text, render, startWith } = open();
    startWith(credit('WithinLimit'));
    by('sd-order-submit-confirm')!.click();
    http.expectOne(`${API}/5/submit`).flush({ errorCode: 'FI.CREDIT.LOCK_TIMEOUT' }, fail(409, {}));
    render();
    expect(by('sd-order-submit-stale')).toBeNull();
    expect(text('sd-order-submit-error')).toContain('being updated by another user');
    expect(by<HTMLButtonElement>('sd-order-submit-confirm')!.disabled).toBe(false);
  });

  it('SD-ORDER-SUBMIT-UI-011 500 is uncertain: trace ID, paused, no success', () => {
    const { http, by, text, render, startWith } = open();
    startWith(credit('WithinLimit'));
    by('sd-order-submit-confirm')!.click();
    http.expectOne(`${API}/5/submit`).flush({ traceId: 'trace-sub-9' }, fail(500, {}));
    render();
    expect(text('sd-order-submit-stale')).toContain('could not confirm the result');
    expect(text('sd-order-submit-error')).toContain('trace-sub-9');
    // Uncertain outcome must not also claim that nothing was saved.
    expect(by('sd-order-submit-unchanged')).toBeNull();
    expect(by('sd-order-submit-notice')).toBeNull();
  });

  it('SD-ORDER-SUBMIT-UI-012 a mismatched 200 reply (same version) is not shown as success', () => {
    const { http, by, text, render, startWith } = open();
    startWith(credit('WithinLimit'));
    by('sd-order-submit-confirm')!.click();
    http.expectOne(`${API}/5/submit`).flush({ order: order('Pending', 2), credit: figures, creditOverride: null });
    render();
    expect(by('sd-order-submit-notice')).toBeNull();
    expect(text('sd-order-submit-stale')).toContain('could not confirm the result');
  });

  it('SD-ORDER-SUBMIT-UI-013 Rejected order shows the edit-first hint and no submit', () => {
    const page = open('Rejected');
    expect(page.by('sd-order-submit')).toBeNull();
    expect(page.text('sd-order-rejected-hint')).toContain('must be edited and saved');
  });

  it('SD-ORDER-SUBMIT-UI-014 Pending order and VIEW-only user show no submit action', () => {
    const pending = open('Pending');
    expect(pending.by('sd-order-submit')).toBeNull();
    expect(pending.by('sd-order-rejected-hint')).toBeNull();
  });

  it('SD-ORDER-SUBMIT-UI-015 user without SAL.ORDER.SUBMIT sees no submit on a Draft', () => {
    granted.delete('SAL.ORDER.SUBMIT');
    const draft = open('Draft');
    expect(draft.by('sd-order-submit')).toBeNull();
  });

  it('SD-ORDER-SUBMIT-UI-016 422 INVALID_STATUS pauses with status changed; cancel closes the panel', () => {
    const { http, by, text, render, startWith } = open();
    startWith(credit('WithinLimit'));
    by('sd-order-submit-confirm')!.click();
    http.expectOne(`${API}/5/submit`).flush({ errorCode: 'SAL.ORDER.INVALID_STATUS' }, fail(422, {}));
    render();
    expect(text('sd-order-submit-stale')).toContain('status has changed');
    by('sd-order-submit-cancel')!.click(); render();
    expect(by('sd-order-submit-panel')).toBeNull();
  });
});
