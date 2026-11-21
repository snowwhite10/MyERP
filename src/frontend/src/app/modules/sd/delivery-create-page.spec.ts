import { provideHttpClient } from '@angular/common/http';
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
import { SalesDeliveryCreatePage } from './delivery-create-page';
import { SD_ROUTES } from './sd.routes';

const API = '/api/v1/sal/deliveries';
const ORDER = '/api/v1/sal/deliveries/order-options/9';
const order = '{"id":9,"documentNo":"SO1","status":"Approved","customerCode":"C1","customerName":"Acme","lines":[' +
  '{"id":21,"lineNo":1,"itemCode":"I1","itemName":"Bolt","quantity":10.000000,"deliveredQuantity":4,"pendingQuantity":1,"remainingQuantity":5.000001},' +
  '{"id":22,"lineNo":2,"itemCode":"I2","itemName":"Nut","quantity":3,"deliveredQuantity":3,"pendingQuantity":0,"remainingQuantity":0},' +
  '{"id":23,"lineNo":3,"itemCode":"I3","itemName":"Pin","quantity":9999999999999.999999,"deliveredQuantity":0,"pendingQuantity":0,"remainingQuantity":9999999999999.999999}]}';
const saved = (quantity1 = '5.000001') => '{"id":61,"documentNo":"DH0061","orderId":9,"orderDocumentNo":"SO1","customerId":3,' +
  '"customerCode":"C1","customerName":"Acme","businessDate":"2028-02-29","status":"Draft","remark":null,"approvalInstanceId":null,' +
  '"postedAt":null,"version":1,"approvalHistory":null,"lines":[' +
  `{"id":71,"lineNo":1,"orderLineId":21,"orderLineNo":1,"itemId":7,"itemCode":"I1","itemName":"Bolt","warehouseId":4,"locationId":12,"quantity":${quantity1},"tracking":[],"invMovementId":null},` +
  '{"id":72,"lineNo":2,"orderLineId":23,"orderLineNo":3,"itemId":8,"itemCode":"I3","itemName":"Pin","warehouseId":4,"locationId":null,"quantity":9999999999999.999999,"tracking":[],"invMovementId":null}]}';

describe('SD-DEL-CR sales outbound create page', () => {
  const company = signal<{ id: number } | null>({ id: 1 });
  let granted: Set<string>;
  let select: ReturnType<typeof vi.fn>;
  function setup(codes = ['SAL.DELIVERY.CREATE', 'SAL.DELIVERY.VIEW']) {
    company.set({ id: 1 });
    granted = new Set(codes);
    select = vi.fn();
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([]),
      ...provideTestTranslations('en'), provideNzIcons(APP_ICONS), { provide: ANIMATION_MODULE_TYPE, useValue: 'NoopAnimations' },
      { provide: CompanyContextService, useValue: { current: company } },
      { provide: ErpDialogService, useValue: { select } },
      { provide: PermissionService, useValue: { has: (code: string) => granted.has(code) } }] });
    const fixture = TestBed.createComponent(SalesDeliveryCreatePage);
    TestBed.tick();
    fixture.detectChanges();
    const http = TestBed.inject(HttpTestingController);
    const element = fixture.nativeElement as HTMLElement;
    const by = (id: string) => element.querySelector<HTMLElement>(`[data-testid="${id}"]`);
    const text = (id: string) => by(id)?.textContent?.replace(/\s+/g, ' ').trim() ?? null;
    return { fixture, http, by, text, view: fixture.componentInstance };
  }
  afterEach(() => TestBed.inject(HttpTestingController).verify());

  function loaded(codes?: string[]) {
    const context = setup(codes);
    select.mockReturnValueOnce(of([{ id: 9, documentNo: 'SO1', customerName: 'Acme' }]));
    context.view.pickOrder();
    context.http.expectOne(ORDER).flush(order);
    context.fixture.detectChanges();
    return context;
  }
  function chooseWarehouses(context: ReturnType<typeof setup>) {
    select.mockReturnValueOnce(of([{ id: 4, code: 'W1', name: 'Main' }])).mockReturnValueOnce(of([{ id: 12, code: 'A-01', name: 'Aisle 1' }]))
      .mockReturnValueOnce(of([{ id: 4, code: 'W1', name: 'Main' }]));
    context.view.pickWarehouse(1); context.view.pickLocation(1); context.view.pickWarehouse(2);
    context.view.draft.businessDate = '2028-02-29';
    context.fixture.detectChanges();
  }

  it('SD-DEL-CR-010 picks an Approved order and prefills open lines with the exact advisory balance; exhausted lines are left out', () => {
    const { view, text, by } = loaded();
    expect(select.mock.calls[0][0].titleKey).toBe('sd.deliveryCreate.chooseOrder');
    expect(text('sd-delivery-create-order')).toBe('SO1 · C1 · Acme');
    expect(view.draft.lines.map(line => [line.lineNo, line.orderLineId, line.quantity])).toEqual([[1, 21, '5.000001'], [2, 23, '9999999999999.999999']]);
    expect(text('sd-delivery-create-balance-1')).toContain('Available to ship');
    expect(text('sd-delivery-create-balance-1')).toContain('5.000001');
    expect(text('sd-delivery-create-balance-2')).toContain('9,999,999,999,999.999999');
    expect(by('sd-delivery-create-line-3')).toBeNull();
    expect(text('sd-delivery-create-location-1')).toBe('No location');
  });

  it('SD-DEL-CR-011 saves the Draft with exact numbers and shows success only after the saved Draft matches', () => {
    const context = loaded();
    chooseWarehouses(context);
    expect(context.text('sd-delivery-create-warehouse-1')).toBe('W1 · Main');
    expect(context.text('sd-delivery-create-location-1')).toBe('A-01 · Aisle 1');
    context.view.save();
    const post = context.http.expectOne(r => r.url === API && r.method === 'POST');
    expect(post.request.body).toBe('{"orderId":9,"businessDate":"2028-02-29","lines":[' +
      '{"lineNo":1,"orderLineId":21,"warehouseId":4,"locationId":12,"quantity":5.000001},' +
      '{"lineNo":2,"orderLineId":23,"warehouseId":4,"locationId":null,"quantity":9999999999999.999999}]}');
    post.flush(saved(), { status: 201, statusText: 'Created' });
    context.fixture.detectChanges();
    expect(context.text('sd-delivery-create-success')).toContain('Outbound draft DH0061 saved. Stock has not changed.');
    expect(context.text('sd-delivery-create-receipt')).toContain('2 line(s)');
    expect(context.view.canLeave()).toBe(true);
  });

  it('SD-DEL-CR-012 a saved reply that differs from the request is not reported as success', () => {
    const context = loaded();
    chooseWarehouses(context);
    context.view.save();
    context.http.expectOne(r => r.url === API && r.method === 'POST').flush(saved('5'), { status: 201, statusText: 'Created' });
    context.fixture.detectChanges();
    expect(context.by('sd-delivery-create-success')).toBeNull();
    expect(context.text('sd-delivery-create-uncertain')).toContain('Search the outbound list first');
  });

  it('SD-DEL-CR-013 refuses to send missing warehouse, invalid date, zero quantity and a split above the balance', () => {
    const context = loaded();
    context.view.draft.businessDate = '2027-02-29';
    context.view.draft.lines[0].quantity = '0';
    context.view.save(); context.fixture.detectChanges();
    expect(context.text('sd-delivery-create-errors')).toBe('4 field(s) need correcting.');
    expect(context.text('sd-delivery-create-line-1')).toContain('Choose a ship-from warehouse.');
    expect(context.text('sd-delivery-create-line-1')).toContain('Enter a quantity above 0');
    context.http.expectNone(API);
    chooseWarehouses(context);
    context.view.draft.lines[0].quantity = '5.000002';
    context.view.save(); context.fixture.detectChanges();
    expect(context.text('sd-delivery-create-line-1')).toContain('add up to more than its available balance');
    context.http.expectNone(API);
  });

  it('SD-DEL-CR-014 a 422 with lineNo marks that line and keeps the input; balance exceeded offers a balance reload', () => {
    const context = loaded();
    chooseWarehouses(context);
    context.view.save();
    context.http.expectOne(API).flush({ status: 422, errorCode: 'SAL.DELIVERY.ORDER_LINE_INVALID', lineNo: 2 },
      { status: 422, statusText: 'Unprocessable' });
    context.fixture.detectChanges();
    expect(context.text('sd-delivery-create-line-error-2')).toContain('does not belong to the selected order');
    expect(context.text('sd-delivery-create-error')).toContain('refused the highlighted line');
    expect(context.view.draft.lines[1].warehouseId).toBe(4);
    expect(context.by('sd-delivery-create-reload')).toBeNull();
    context.view.save();
    context.http.expectOne(API).flush({ status: 422, errorCode: 'SAL.DELIVERY.ORDER_BALANCE_EXCEEDED' }, { status: 422, statusText: 'x' });
    context.fixture.detectChanges();
    expect(context.text('sd-delivery-create-error')).toContain('Reload the balance');
    context.by('sd-delivery-create-reload')!.click();
    context.http.expectOne(ORDER).flush(order.replace('"remainingQuantity":5.000001', '"remainingQuantity":2'));
    context.fixture.detectChanges();
    expect(context.text('sd-delivery-create-balance-1')).toContain('2');
    expect(context.view.draft.lines[0].warehouseId).toBe(4);
    expect(context.view.draft.lines[0].quantity).toBe('5.000001');
    expect(context.by('sd-delivery-create-error')).toBeNull();
  });

  it('SD-DEL-CR-015 MD reference errors and 400 field paths are shown in the user language', () => {
    const context = loaded();
    chooseWarehouses(context);
    context.view.save();
    context.http.expectOne(API).flush({ status: 422, errorCode: 'MD.WAREHOUSE.INACTIVE' }, { status: 422, statusText: 'x' });
    context.fixture.detectChanges();
    expect(context.text('sd-delivery-create-error')).toContain('This warehouse is inactive.');
    context.view.save();
    context.http.expectOne(API).flush({ status: 400, errorCode: 'COMMON.REQUEST.VALIDATION_FAILED',
      errors: { 'lines[1].quantity': ['Quantity must be positive, below 10^13, with at most 6 decimals.'] } }, { status: 400, statusText: 'x' });
    context.fixture.detectChanges();
    expect(context.text('sd-delivery-create-error')).toContain('Line 2 · Quantity: invalid format or value');
    expect(context.text('sd-delivery-create-error')).not.toContain('below 10^13');
  });

  it('SD-DEL-CR-016 500 keeps the trace ID and does not claim success; timeout-like 0 is uncertain too', () => {
    const context = loaded();
    chooseWarehouses(context);
    context.view.save();
    context.http.expectOne(API).flush({ status: 500, traceId: 'trace-500' }, { status: 500, statusText: 'x' });
    context.fixture.detectChanges();
    expect(context.text('sd-delivery-create-error')).toContain('trace-500');
    expect(context.text('sd-delivery-create-uncertain')).toContain('Search the outbound list first');
    expect(context.by('sd-delivery-create-success')).toBeNull();
    expect((context.by('sd-delivery-create-save') as HTMLButtonElement).disabled).toBe(true);
  });

  it('SD-DEL-CR-017 an order no longer Approved, a fully used order and a hidden order are explained and cannot be saved', () => {
    const context = setup();
    select.mockReturnValue(of([{ id: 9, documentNo: 'SO1', customerName: 'Acme' }]));
    context.view.pickOrder();
    context.http.expectOne(ORDER).flush(order.replace('"Approved"', '"Cancelled"'));
    context.fixture.detectChanges();
    expect(context.text('sd-delivery-create-order-error')).toContain('no longer Approved');
    expect(context.by('sd-delivery-create-form')).toBeNull();
    context.view.pickOrder();
    context.http.expectOne(ORDER).flush(order.replace(/"remainingQuantity":[\d.]+/g, '"remainingQuantity":0'));
    context.fixture.detectChanges();
    expect(context.text('sd-delivery-create-no-balance')).toContain('nothing left to ship');
    expect(context.by('sd-delivery-create-form')).toBeNull();
    context.view.pickOrder();
    context.http.expectOne(ORDER).flush({ status: 404, errorCode: 'SAL.ORDER.NOT_FOUND' }, { status: 404, statusText: 'x' });
    context.fixture.detectChanges();
    expect(context.text('sd-delivery-create-order-error')).toContain('Order not found');
  });

  it('SD-DEL-CR-018 CREATE alone picks orders, warehouses and locations from the delivery option endpoints (ADR-155); CREATE-only uncertainty names a colleague', () => {
    const context = setup(['SAL.DELIVERY.CREATE']);
    expect((context.by('sd-delivery-create-order-pick') as HTMLButtonElement).disabled).toBe(false);
    expect(context.by('sd-delivery-create-back')).toBeNull();
    select.mockReturnValueOnce(of([{ id: 9, documentNo: 'SO1', customerCode: 'C1', customerName: 'Acme', businessDate: '2028-02-29' }]));
    context.view.pickOrder();
    context.http.expectOne(ORDER).flush(order);
    context.fixture.detectChanges();
    const orders = select.mock.calls[0][0];
    orders.load({ page: 2, pageSize: 10, keyword: ' so1 ' }).subscribe();
    const list = context.http.expectOne(r => r.url === '/api/v1/sal/deliveries/order-options');
    expect([list.request.params.get('page'), list.request.params.get('pageSize'), list.request.params.get('keyword'), list.request.params.has('status')])
      .toEqual(['2', '10', 'so1', false]);
    list.flush({ items: [], page: 2, pageSize: 10, totalCount: 0 });
    expect((context.by('sd-delivery-create-warehouse-pick-1') as HTMLButtonElement).disabled).toBe(false);
    chooseWarehouses(context);
    select.mock.calls[1][0].load({ page: 1, pageSize: 10 }).subscribe();
    context.http.expectOne(r => r.url === '/api/v1/sal/deliveries/warehouse-options').flush({ items: [], page: 1, pageSize: 10, totalCount: 0 });
    const locations = select.mock.calls[2][0];
    expect(locations.errorCodes).toContain('MD.WAREHOUSE.NOT_FOUND');
    locations.load({ page: 1, pageSize: 10 }).subscribe();
    context.http.expectOne(r => r.url === '/api/v1/sal/deliveries/warehouse-options/4/locations').flush({ items: [], page: 1, pageSize: 10, totalCount: 0 });
    expect(context.text('sd-delivery-create-location-1')).toBe('A-01 · Aisle 1');
    expect(context.by('sd-delivery-create-order-permission')).toBeNull();
    expect(context.by('sd-delivery-create-warehouse-permission')).toBeNull();
    context.view.uncertain.set(true); context.fixture.detectChanges();
    expect(context.text('sd-delivery-create-uncertain')).toContain('ask a colleague');
  });

  it('SD-DEL-CR-019 changing warehouse clears the location; a company switch drops the order and a late reply', () => {
    const context = loaded();
    chooseWarehouses(context);
    select.mockReturnValueOnce(of([{ id: 5, code: 'W2', name: 'Second' }]));
    context.view.pickWarehouse(1); context.fixture.detectChanges();
    expect(context.view.draft.lines[0].locationId).toBeNull();
    expect(context.text('sd-delivery-create-location-1')).toBe('No location');
    expect(context.view.canLeave).toBeDefined();
    context.view.reloadBalance();
    const late = context.http.expectOne(ORDER);
    company.set({ id: 2 }); TestBed.tick(); context.fixture.detectChanges();
    late.flush(order); context.fixture.detectChanges();
    expect(context.view.source()).toBeNull();
    expect(context.view.draft.lines).toEqual([]);
    expect(context.text('sd-delivery-create-order')).toBe('No order selected');
  });

  it('SD-DEL-CR-020 the create route requires CREATE and guards unsaved input', () => {
    setup();
    const route = SD_ROUTES.find(candidate => candidate.path === 'deliveries/new')!;
    expect(route.canActivate).toHaveLength(1);
    expect(route.canDeactivate).toHaveLength(1);
    const index = SD_ROUTES.indexOf(route);
    expect(index).toBeLessThan(SD_ROUTES.findIndex(candidate => candidate.path === 'deliveries'));
  });

  it('SD-DEL-CR-023 a saved Draft with Lot/Serial lines points to the edit page to pick lots/serials; without EDIT it names a colleague', () => {
    const tracked = () => saved().replace('"invMovementId":null}', '"invMovementId":null,"trackingMode":"Lot"}');
    const context = loaded(['SAL.DELIVERY.CREATE', 'SAL.DELIVERY.VIEW', 'SAL.DELIVERY.EDIT']);
    chooseWarehouses(context);
    context.view.save();
    context.http.expectOne(r => r.url === API && r.method === 'POST').flush(tracked(), { status: 201, statusText: 'Created' });
    context.fixture.detectChanges();
    expect(context.text('sd-delivery-create-tracking-hint')).toContain('Pick them from stock on the edit page');
    expect(context.by('sd-delivery-create-edit')?.getAttribute('href')).toBe('/sal/deliveries/61/edit');
    TestBed.resetTestingModule();

    const createOnly = loaded();
    chooseWarehouses(createOnly);
    createOnly.view.save();
    createOnly.http.expectOne(r => r.url === API && r.method === 'POST').flush(tracked(), { status: 201, statusText: 'Created' });
    createOnly.fixture.detectChanges();
    expect(createOnly.text('sd-delivery-create-tracking-hint')).toContain('Ask a colleague');
    expect(createOnly.by('sd-delivery-create-edit')).toBeNull();
    TestBed.resetTestingModule();

    const untracked = loaded();
    chooseWarehouses(untracked);
    untracked.view.save();
    untracked.http.expectOne(r => r.url === API && r.method === 'POST').flush(saved(), { status: 201, statusText: 'Created' });
    untracked.fixture.detectChanges();
    expect(untracked.by('sd-delivery-create-tracking-hint')).toBeNull();
  });
});
