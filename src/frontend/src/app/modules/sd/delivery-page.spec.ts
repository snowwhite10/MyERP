import { HttpRequest, provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ANIMATION_MODULE_TYPE, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { provideNzIcons } from 'ng-zorro-antd/icon';
import { provideTestTranslations } from '../../../testing/i18n-fixtures';
import { CompanyContextService } from '../../core/company/company-context.service';
import { APP_ICONS } from '../../core/icons';
import { MAIN_MENU } from '../../layout/menu';
import { SalesDeliveryPage } from './delivery-page';
import { SD_ROUTES } from './sd.routes';

const API = '/api/v1/sal/deliveries';
const page = (items: object[], totalCount = items.length) => JSON.stringify({ items, totalCount, page: 1, pageSize: 20 });
const row = { id: 5, documentNo: 'DH001', orderId: 9, orderDocumentNo: 'SO001', customerId: 3, customerName: 'Acme',
  businessDate: '2028-02-29', status: 'Posted', lineCount: 2, totalQuantity: 1234.5, version: 3 };
const posted = '{"id":5,"documentNo":"DH001","orderId":9,"orderDocumentNo":"SO001","customerId":3,"customerCode":"C1",' +
  '"customerName":"Acme","businessDate":"2028-02-29","status":"Posted","remark":null,"approvalInstanceId":4,' +
  '"postedAt":"2028-02-29T10:15:30","version":3,"lines":[' +
  '{"id":11,"lineNo":1,"orderLineId":21,"orderLineNo":1,"itemId":7,"itemCode":"I1","itemName":"Bolt","warehouseId":2,' +
  '"locationId":null,"quantity":1234.000001,"invMovementId":88,"tracking":[{"seqNo":1,"lotNo":"L-1","serialNo":null,"quantity":1234.000001}]},' +
  '{"id":12,"lineNo":2,"orderLineId":22,"orderLineNo":2,"itemId":8,"itemCode":"I2","itemName":"Nut","warehouseId":2,' +
  '"locationId":6,"quantity":0.5,"invMovementId":89,"tracking":[]}],' +
  '"approvalHistory":[{"instanceId":4,"status":"Approved","submittedAt":"2028-02-28T09:00:00","steps":' +
  '[{"stepNo":1,"name":"Manager","status":"Approved","actedBy":2,"actedAt":"2028-02-29T10:15:00","comment":"Ship"}]}]}';

describe('SD-DEL-UI sales outbound view page', () => {
  const company = signal<{ id: number } | null>({ id: 1 });
  beforeEach(() => {
    company.set({ id: 1 });
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([]),
      ...provideTestTranslations('en'), provideNzIcons(APP_ICONS),
      { provide: ANIMATION_MODULE_TYPE, useValue: 'NoopAnimations' },
      { provide: CompanyContextService, useValue: { current: company } }] });
  });
  afterEach(() => TestBed.inject(HttpTestingController).verify());

  const isList = (r: HttpRequest<unknown>) => r.url === API && r.method === 'GET';
  function create(body = page([row])) {
    const fixture = TestBed.createComponent(SalesDeliveryPage);
    TestBed.tick();
    const http = TestBed.inject(HttpTestingController);
    http.expectOne(isList).flush(body);
    fixture.detectChanges();
    const element = fixture.nativeElement as HTMLElement;
    const by = (id: string) => element.querySelector<HTMLElement>(`[data-testid="${id}"]`);
    const text = (id: string) => by(id)?.textContent?.replace(/\s+/g, ' ').trim() ?? null;
    return { fixture, http, element, by, text };
  }

  it('SD-DEL-UI-002 lists saved deliveries with named status and exact grouped total quantity', () => {
    const { text } = create();
    const listed = text('sd-delivery-row-5')!;
    expect(listed).toContain('DH001');
    expect(listed).toContain('SO001');
    expect(listed).toContain('Acme');
    expect(listed).toContain('Posted');
    expect(listed).toContain('1,234.5');
  });

  it('SD-DEL-UI-003 explains an empty list and a filtered no-result list differently', () => {
    const { element, fixture, http, by } = create(page([]));
    expect(element.textContent).toContain('No deliveries yet.');
    fixture.componentInstance.status = 'Pending';
    by('sd-delivery-search')!.click();
    const request = http.expectOne(isList);
    expect(request.request.params.get('status')).toBe('Pending');
    request.flush(page([]));
    fixture.detectChanges();
    expect(element.textContent).toContain('No deliveries match these filters.');
  });

  it('SD-DEL-UI-004 rejects a reversed date range without sending a request', () => {
    const { fixture, by, text } = create();
    const component = fixture.componentInstance;
    component.from = '2028-03-01'; component.to = '2028-02-29';
    component.search();
    fixture.detectChanges();
    expect(text('sd-delivery-validation')).toBe('Enter valid dates with From on or before To.');
    expect(by('sd-delivery-row-5')).not.toBeNull();
  });

  it('SD-DEL-UI-005 shows posted lines with lot/serial picks, INV movement IDs, posting time and SYS history', () => {
    const { by, text, http, fixture } = create();
    by('sd-delivery-open-5')!.click();
    http.expectOne(`${API}/5`).flush(posted);
    fixture.detectChanges();
    expect(text('sd-delivery-detail-status')).toBe('Posted');
    expect(text('sd-delivery-state')).toContain('movement IDs');
    expect(text('sd-delivery-posted-at')).toBe('2028-02-29 10:15');
    expect(text('sd-delivery-line-11')).toContain('1,234.000001');
    expect(text('sd-delivery-tracking-11')).toBe('Lot L-1 × 1,234.000001');
    expect(text('sd-delivery-tracking-12')).toBe('Not tracked');
    expect(text('sd-delivery-movement-11')).toBe('88');
    const history = text('sd-delivery-approval-history')!;
    expect(history).toContain('Approval round 4 · Approved');
    expect(history).toContain('1. Manager · Approved — Ship');
  });

  it('SD-DEL-UI-006 explains that a Pending delivery reserves balance and has no stock movement yet', () => {
    const { by, text, http, fixture } = create();
    by('sd-delivery-open-5')!.click();
    const pending = JSON.parse(posted) as { status: string; postedAt: string | null; approvalHistory: unknown[];
      lines: { invMovementId: number | null }[] };
    pending.status = 'Pending'; pending.postedAt = null; pending.approvalHistory = [];
    pending.lines.forEach(line => line.invMovementId = null);
    http.expectOne(`${API}/5`).flush(JSON.stringify(pending));
    fixture.detectChanges();
    expect(text('sd-delivery-state')).toContain('reserve the order balance');
    expect(by('sd-delivery-posted-at')).toBeNull();
    expect(text('sd-delivery-movement-11')).toBe('Not posted');
    expect(text('sd-delivery-approval-history')).toContain('No approval history yet.');
  });

  it('SD-DEL-UI-007 shows a hidden delivery as a specific 404 and a server error with trace ID and retry', () => {
    const { by, text, http, fixture } = create();
    by('sd-delivery-open-5')!.click();
    http.expectOne(`${API}/5`).flush('{"errorCode":"SAL.DELIVERY.NOT_FOUND"}', { status: 404, statusText: 'Not Found' });
    fixture.detectChanges();
    expect(text('sd-delivery-detail-error')).toContain('Delivery not found');
    by('sd-delivery-detail-retry')!.click();
    http.expectOne(`${API}/5`).flush('{"traceId":"trace-500"}', { status: 500, statusText: 'Server Error' });
    fixture.detectChanges();
    expect(text('sd-delivery-detail-error')).toContain('trace-500');
    expect(by('sd-delivery-detail')).toBeNull();
  });

  it('SD-DEL-UI-008 list failure shows a retry and never the empty-list message as success', () => {
    const { fixture, http, by, text, element } = create();
    by('sd-delivery-retry');
    fixture.componentInstance.load();
    http.expectOne(isList).flush('{"traceId":"trace-list"}', { status: 500, statusText: 'Server Error' });
    fixture.detectChanges();
    expect(text('sd-delivery-error')).toContain('trace-list');
    expect(element.textContent).not.toContain('No deliveries yet.');
    by('sd-delivery-retry')!.click();
    http.expectOne(isList).flush(page([row]));
    fixture.detectChanges();
    expect(by('sd-delivery-row-5')).not.toBeNull();
  });

  it('SD-DEL-UI-009 company switch clears the previous company rows and ignores the old reply', () => {
    const { fixture, http, by } = create();
    fixture.componentInstance.load();
    const stale = http.expectOne(isList);
    company.set({ id: 2 });
    TestBed.tick();
    const fresh = http.match(isList);
    stale.flush(page([row]));
    fresh.forEach(request => request.flush(page([])));
    fixture.detectChanges();
    expect(by('sd-delivery-row-5')).toBeNull();
  });

  it('SD-DEL-UI-010 the route and menu entry require SAL.DELIVERY.VIEW', () => {
    const route = SD_ROUTES.find(r => r.path === 'deliveries');
    expect(route?.canActivate?.length).toBe(1);
    const sales = MAIN_MENU.find(item => item.testId === 'layout-menu-sal');
    expect(sales?.children?.find(item => item.route === '/sal/deliveries')?.permission).toBe('SAL.DELIVERY.VIEW');
  });
});
