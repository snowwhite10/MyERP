import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ANIMATION_MODULE_TYPE } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideNzIcons } from 'ng-zorro-antd/icon';
import { provideTestTranslations } from '../../../testing/i18n-fixtures';
import { PermissionService } from '../../core/auth/permission.service';
import { APP_ICONS } from '../../core/icons';
import { SalesOrderCreditPanel } from './order-credit-panel';

/** WU-SD-006c3a (ADR-174): read-only credit panel for a saved order on Create 2.5 / Edit 2.6 (REQ_IO_A2.5.21, A2.6.18). */
const CREDIT = (customerId: number) => `/api/v1/sal/credit-checks/${customerId}`;
const credit = (status: 'WithinLimit' | 'OverLimit', excess: number | null = null, orderId = 81) => ({ customerId: 7, orderId,
  status, reasonCode: status === 'OverLimit' ? 'SAL.CREDIT.LIMIT_EXCEEDED' : null, evaluationDate: '2028-02-29',
  baseCurrency: 'TWD', limit: 1000, postedOpenAr: 600, otherCommitments: 200, candidate: 150,
  headroom: status === 'OverLimit' ? -(excess ?? 0) : 50, excess, limitVersion: 4, requiresRecheck: true });
const money = (value: number) => value.toLocaleString(undefined, { minimumFractionDigits: 2, maximumFractionDigits: 4 });

describe('SD-ORDER-CREDIT-UI saved order credit panel', () => {
  let granted: Set<string>;
  beforeEach(() => {
    granted = new Set(['SAL.ORDER.CREATE']);
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting(),
      ...provideTestTranslations('en'), provideNzIcons(APP_ICONS),
      { provide: ANIMATION_MODULE_TYPE, useValue: 'NoopAnimations' },
      { provide: PermissionService, useValue: { has: (code: string) => granted.has(code) } }] });
  });
  afterEach(() => TestBed.inject(HttpTestingController).verify());

  function mount(orderId = 81, customerId = 7) {
    const fixture = TestBed.createComponent(SalesOrderCreditPanel);
    fixture.componentRef.setInput('orderId', orderId);
    fixture.componentRef.setInput('customerId', customerId);
    fixture.componentRef.setInput('documentNo', 'SO-0081');
    fixture.detectChanges();
    const http = TestBed.inject(HttpTestingController);
    const element = fixture.nativeElement as HTMLElement;
    const by = (id: string) => element.querySelector<HTMLElement>(`[data-testid="${id}"]`);
    const text = (id: string) => by(id)?.textContent?.replace(/\s+/g, ' ').trim() ?? null;
    const render = () => fixture.detectChanges();
    const expectCheck = (customerId = 7, orderId = 81) => {
      const request = http.expectOne(r => r.url === CREDIT(customerId));
      expect(request.request.method).toBe('GET');
      expect(request.request.params.get('orderId')).toBe(String(orderId));
      return request;
    };
    return { fixture, http, by, text, render, expectCheck };
  }

  it('SD-ORDER-CREDIT-UI-001 within limit: one request for the saved order, figures and the saved-version notice', () => {
    const { by, text, render, expectCheck } = mount();
    expect(text('sd-order-saved-credit')).toContain('Checking customer credit');
    expectCheck().flush(credit('WithinLimit'));
    render();
    expect(text('sd-order-saved-credit')).toContain('Credit preview for saved order SO-0081');
    expect(text('sd-order-credit-status')).toBe('Within limit');
    expect(text('sd-order-credit-limit')).toBe(money(1000));
    expect(text('sd-order-credit-candidate')).toBe(money(150));
    expect(text('sd-order-credit-headroom')).toBe(money(50));
    expect(text('sd-order-saved-credit-notice')).toContain('Nothing was submitted and no credit data was saved');
    expect(by('sd-order-saved-credit-excess')).toBeNull();
    expect(text('sd-order-saved-credit')).not.toMatch(/success|pending approval|is Approved/);
  });

  it('SD-ORDER-CREDIT-UI-002 over limit: excess warning; submit hint depends on SUBMIT and OVERRIDE grants', () => {
    let view = mount();
    view.expectCheck().flush(credit('OverLimit', 25.5));
    view.render();
    expect(view.text('sd-order-credit-status')).toBe('Over limit');
    expect(view.text('sd-order-saved-credit-excess')).toContain(`exceeds the available credit by ${money(25.5)} TWD`);
    expect(view.by('sd-order-saved-credit-override-hint')).toBeNull();
    view.fixture.destroy();

    granted.add('SAL.ORDER.SUBMIT');
    view = mount();
    view.expectCheck().flush(credit('OverLimit', 25.5));
    view.render();
    expect(view.text('sd-order-saved-credit-override-hint')).toContain('needs the credit override permission (SAL.CREDIT.OVERRIDE)');
    view.fixture.destroy();

    granted.add('SAL.CREDIT.OVERRIDE');
    view = mount();
    view.expectCheck().flush(credit('OverLimit', 25.5));
    view.render();
    expect(view.text('sd-order-saved-credit-override-hint')).toContain('needs a credit override reason');
    view.fixture.destroy();

    view = mount();
    view.expectCheck().flush({ ...credit('WithinLimit'), headroom: 0 });
    view.render();
    expect(view.text('sd-order-credit-status')).toBe('Within limit');
    expect(view.text('sd-order-credit-headroom')).toBe(money(0));
    expect(view.by('sd-order-saved-credit-excess')).toBeNull();
  });

  it('SD-ORDER-CREDIT-UI-003 named blocks, 500 with trace ID, timeout; recheck replaces the error with figures', () => {
    vi.useFakeTimers();
    try {
      const { by, text, render, expectCheck, http } = mount();
      expectCheck().flush({ errorCode: 'SAL.CREDIT.LIMIT_NOT_SET' }, { status: 422, statusText: 'Unprocessable' });
      render();
      expect(by('sd-order-saved-credit-error')).not.toBeNull();
      expect(text('sd-order-saved-credit-error-hint')).toContain('cannot be submitted until the cause is fixed');
      expect(text('sd-order-saved-credit')).not.toContain('Trace ID');

      by('sd-order-saved-credit-recheck')!.click(); render();
      expectCheck().flush({ errorCode: 'X.UNKNOWN', traceId: 'trace-55' }, { status: 500, statusText: 'Server Error' });
      render();
      expect(text('sd-order-saved-credit')).toContain('trace-55');
      expect(text('sd-order-saved-credit-error-hint')).toContain('The order itself is saved');

      by('sd-order-saved-credit-recheck')!.click(); render();
      expectCheck();
      vi.advanceTimersByTime(6001);
      render();
      expect(text('sd-order-saved-credit')).toContain('The credit check timed out');

      by('sd-order-saved-credit-recheck')!.click(); render();
      expect((by('sd-order-saved-credit-recheck') as HTMLButtonElement).disabled).toBe(true);
      expectCheck().flush(credit('WithinLimit'));
      render();
      expect(by('sd-order-saved-credit-error')).toBeNull();
      expect(text('sd-order-credit-status')).toBe('Within limit');
      http.verify();
    } finally { vi.useRealTimers(); }
  });

  it('SD-ORDER-CREDIT-UI-004 a newly saved order reloads the preview and drops the stale request', () => {
    const { fixture, text, render, expectCheck } = mount();
    const first = expectCheck();
    fixture.componentRef.setInput('orderId', 82);
    fixture.componentRef.setInput('customerId', 8);
    render();
    expect(first.cancelled).toBe(true);
    expectCheck(8, 82).flush({ ...credit('WithinLimit', null, 82), customerId: 8, limit: 2000 });
    render();
    expect(text('sd-order-credit-limit')).toBe(money(2000));
  });

  it('SD-ORDER-CREDIT-UI-004b a receipt without a valid customer id sends no request and explains the preview failed', () => {
    const { by, text, http } = mount(81, 0);
    http.expectNone(() => true);
    expect(by('sd-order-saved-credit-error')).not.toBeNull();
    expect(text('sd-order-saved-credit-error-hint')).toContain('The order itself is saved');
  });

  it('SD-ORDER-CREDIT-UI-005 403 says no permission without a trace ID', () => {
    const { text, render, expectCheck } = mount();
    expectCheck().flush({ errorCode: 'COMMON.PERMISSION.DENIED', traceId: 'hidden' }, { status: 403, statusText: 'Forbidden' });
    render();
    expect(text('sd-order-saved-credit-error')).toMatch(/permission/i);
    expect(text('sd-order-saved-credit')).not.toContain('hidden');
    expect(text('sd-order-saved-credit-error-hint')).toContain('The order itself is saved');
  });
});
