import { provideHttpClient } from '@angular/common/http';
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
import { SalesDeliveryEditPage } from './delivery-edit-page';
import { SD_ROUTES } from './sd.routes';

const API = '/api/v1/sal/deliveries/61';
const ORDER = '/api/v1/sal/orders/9';
const order = (status = 'Approved') => `{"id":9,"documentNo":"SO1","status":"${status}","customerCode":"C1","customerName":"Acme","lines":[` +
  '{"id":21,"lineNo":1,"itemCode":"I1","itemName":"Bolt","quantity":10,"deliveredQuantity":4,"pendingQuantity":1,"remainingQuantity":5.000001},' +
  '{"id":23,"lineNo":3,"itemCode":"I3","itemName":"Pin","quantity":9999999999999.999999,"deliveredQuantity":0,"pendingQuantity":0,"remainingQuantity":9999999999999.999999}]}';
const saved = (o: { status?: string; version?: number; quantity1?: string; tracking?: string; location1?: string } = {}) =>
  `{"id":61,"documentNo":"DH0061","orderId":9,"orderDocumentNo":"SO1","customerId":3,"customerCode":"C1","customerName":"Acme",` +
  `"businessDate":"2028-02-29","status":"${o.status ?? 'Draft'}","remark":null,"approvalInstanceId":null,"postedAt":null,` +
  `"version":${o.version ?? 3},"approvalHistory":null,"lines":[` +
  `{"id":71,"lineNo":1,"orderLineId":21,"orderLineNo":1,"itemId":7,"itemCode":"I1","itemName":"Bolt","warehouseId":4,"locationId":${o.location1 ?? '12'},` +
  `"quantity":${o.quantity1 ?? '2.000000'},"tracking":${o.tracking ?? '[{"seqNo":1,"lotNo":"L-1","serialNo":null,"quantity":1.500000}]'},"invMovementId":null},` +
  '{"id":72,"lineNo":2,"orderLineId":23,"orderLineNo":3,"itemId":8,"itemCode":"I3","itemName":"Pin","warehouseId":4,"locationId":null,' +
  '"quantity":9999999999999.999999,"tracking":[],"invMovementId":null}]}';

describe('SD-DEL-ED edit route', () => {
  it('SD-DEL-ED-010 route needs VIEW and EDIT and guards unsaved changes', () => {
    const route = SD_ROUTES.find(candidate => candidate.path === 'deliveries/:id/edit');
    expect(route?.canActivate?.length).toBe(2);
    expect(route?.canDeactivate?.length).toBe(1);
    expect(SD_ROUTES.findIndex(candidate => candidate.path === 'deliveries/:id/edit'))
      .toBeLessThan(SD_ROUTES.findIndex(candidate => candidate.path === 'deliveries'));
  });
});

describe('SD-DEL-ED sales outbound edit page (interface 2.9)', () => {
  const company = signal<{ id: number } | null>({ id: 1 });
  let granted: Set<string>;
  let select: ReturnType<typeof vi.fn>;
  let confirm: ReturnType<typeof vi.fn>;
  function setup(codes = ['SAL.DELIVERY.VIEW', 'SAL.DELIVERY.EDIT', 'SAL.DELIVERY.DELETE', 'SAL.ORDER.VIEW', 'MD.WAREHOUSE.VIEW'], id = '61') {
    company.set({ id: 1 });
    granted = new Set(codes);
    select = vi.fn(); confirm = vi.fn(() => of(true));
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([]),
      ...provideTestTranslations('en'), provideNzIcons(APP_ICONS), { provide: ANIMATION_MODULE_TYPE, useValue: 'NoopAnimations' },
      { provide: ActivatedRoute, useValue: { snapshot: { paramMap: convertToParamMap({ id }) } } },
      { provide: CompanyContextService, useValue: { current: company } },
      { provide: ErpDialogService, useValue: { select, confirm } },
      { provide: PermissionService, useValue: { has: (code: string) => granted.has(code) } }] });
    const fixture = TestBed.createComponent(SalesDeliveryEditPage);
    TestBed.tick();
    fixture.detectChanges();
    const http = TestBed.inject(HttpTestingController);
    const element = fixture.nativeElement as HTMLElement;
    const by = (testId: string) => element.querySelector<HTMLElement>(`[data-testid="${testId}"]`);
    const text = (testId: string) => by(testId)?.textContent?.replace(/\s+/g, ' ').trim() ?? null;
    const detect = () => fixture.detectChanges();
    return { fixture, http, by, text, detect, view: fixture.componentInstance };
  }
  afterEach(() => TestBed.inject(HttpTestingController).verify());

  function loaded(options: Parameters<typeof saved>[0] = {}, codes?: string[]) {
    const context = setup(codes);
    context.http.expectOne(API).flush(saved(options));
    if (granted.has('SAL.ORDER.VIEW') && ['Draft', 'Rejected'].includes(options.status ?? 'Draft')) context.http.expectOne(ORDER).flush(order());
    context.detect();
    return context;
  }
  const put = (context: ReturnType<typeof setup>) => context.http.expectOne(r => r.url === API && r.method === 'PUT');

  it('SD-DEL-ED-011 loads a Draft with exact quantities, saved references, kept tracking and the advisory balance', () => {
    const { view, text, by } = loaded();
    expect(text('sd-delivery-edit-identity')).toContain('DH0061');
    expect(text('sd-delivery-edit-identity')).toContain('Version 3');
    expect(view.draft.lines.map(line => [line.lineNo, line.quantity, line.warehouseId, line.locationId]))
      .toEqual([[1, '2', 4, 12], [2, '9999999999999.999999', 4, null]]);
    expect(text('sd-delivery-edit-warehouse-1')).toBe('Saved warehouse (ID 4)');
    expect(text('sd-delivery-edit-location-1')).toBe('Saved location (ID 12)');
    expect(text('sd-delivery-edit-location-2')).toBe('No location');
    expect(text('sd-delivery-edit-tracking-1')).toContain('Lot L-1 × 1.5');
    expect(text('sd-delivery-edit-tracking-1')).toContain('kept unchanged');
    expect(by('sd-delivery-edit-tracking-2')).toBeNull();
    expect(text('sd-delivery-edit-balance-2')).toContain('9,999,999,999,999.999999');
    expect(by('sd-delivery-edit-rejected')).toBeNull();
    expect(view.canLeave()).toBe(true);
  });

  it('SD-DEL-ED-012 saves changes as PUT {version, draft} keeping tracking, then shows the new version only when the reply matches', () => {
    const context = loaded();
    select.mockReturnValueOnce(of([{ id: 5, code: 'W2', name: 'Annex' }]));
    context.view.pickWarehouse(1);
    context.view.draft.lines[0].quantity = '3.5';
    context.detect();
    expect(context.text('sd-delivery-edit-location-1')).toBe('No location');
    context.view.save();
    const request = put(context);
    expect(request.request.body).toBe('{"version":3,"draft":{"orderId":9,"businessDate":"2028-02-29","lines":[' +
      '{"lineNo":1,"orderLineId":21,"warehouseId":5,"locationId":null,"quantity":3.5,"tracking":[{"lotNo":"L-1","serialNo":null,"quantity":1.5}]},' +
      '{"lineNo":2,"orderLineId":23,"warehouseId":4,"locationId":null,"quantity":9999999999999.999999}]}}');
    request.flush(saved({ version: 4, quantity1: '3.500000', location1: 'null' }).replace('"warehouseId":4,"locationId":null,"quantity":3', '"warehouseId":5,"locationId":null,"quantity":3'));
    context.detect();
    expect(context.text('sd-delivery-edit-success')).toBe('Outbound DH0061 was saved as version 4. It is a Draft; stock has not changed.');
    expect(context.view.version()).toBe(4);
    expect(context.text('sd-delivery-edit-warehouse-1')).toBe('W2 · Annex');
    expect(context.view.canLeave()).toBe(true);
  });

  it('SD-DEL-ED-013 a reply with lost tracking or the same version is not success and asks for a refresh', () => {
    const context = loaded();
    context.view.draft.lines[0].quantity = '2.5';
    context.view.save();
    put(context).flush(saved({ version: 4, quantity1: '2.5', tracking: '[]' }));
    context.detect();
    expect(context.by('sd-delivery-edit-success')).toBeNull();
    expect(context.text('sd-delivery-edit-stale')).toContain('could not confirm whether your changes were saved');
    expect((context.by('sd-delivery-edit-save') as HTMLButtonElement).disabled).toBe(true);
  });

  it('SD-DEL-ED-014 no change sends nothing and warns instead of claiming success', () => {
    const context = loaded();
    context.view.save(); context.detect();
    expect(context.text('sd-delivery-edit-no-change')).toBe('No data changed. Nothing was sent.');
    expect(context.by('sd-delivery-edit-success')).toBeNull();
    context.http.expectNone(r => r.method === 'PUT');
  });

  it('SD-DEL-ED-015 field checks block the PUT: empty quantity, quantity below the saved lot total, above balance', () => {
    const context = loaded();
    context.view.draft.lines[0].quantity = '1.4';
    context.view.draft.businessDate = '';
    context.view.save(); context.detect();
    expect(context.text('sd-delivery-edit-line-1')).toContain('less than the saved lot/serial total');
    expect(context.text('sd-delivery-edit-errors')).toBe('2 field(s) need correcting.');
    context.view.draft.businessDate = '2028-03-01';
    context.view.draft.lines[0].quantity = '5.000002';
    context.view.save(); context.detect();
    expect(context.text('sd-delivery-edit-line-1')).toContain('more than its available balance');
    context.http.expectNone(r => r.method === 'PUT');
  });

  it('SD-DEL-ED-016 409 keeps the input, locks saving and refresh reloads the latest version after confirmation', () => {
    const context = loaded();
    context.view.draft.lines[0].quantity = '4';
    context.view.save();
    put(context).flush({ errorCode: 'COMMON.CONCURRENCY.CONFLICT' }, { status: 409, statusText: 'Conflict' });
    context.detect();
    expect(context.text('sd-delivery-edit-stale')).toContain('Someone else changed this outbound');
    expect(context.view.draft.lines[0].quantity).toBe('4');
    expect((context.by('sd-delivery-edit-save') as HTMLButtonElement).disabled).toBe(true);
    expect(context.text('sd-delivery-edit-stale-hint')).toContain('paused until you refresh');
    const ask = vi.spyOn(window, 'confirm').mockReturnValueOnce(false).mockReturnValueOnce(true);
    context.view.refresh();
    context.http.expectNone(API);
    context.view.refresh();
    context.http.expectOne(API).flush(saved({ version: 5, quantity1: '3' }));
    context.http.expectOne(ORDER).flush(order());
    context.detect();
    expect(ask).toHaveBeenCalledTimes(2);
    expect(context.by('sd-delivery-edit-stale')).toBeNull();
    expect(context.view.draft.lines[0].quantity).toBe('3');
    expect(context.view.version()).toBe(5);
  });

  it('SD-DEL-ED-017 422 line errors are shown on that line; balance exceeded offers a balance reload', () => {
    const context = loaded();
    context.view.draft.lines[1].quantity = '1';
    context.view.save();
    put(context).flush({ errorCode: 'SAL.DELIVERY.ORDER_BALANCE_EXCEEDED', lineNo: 2 }, { status: 422, statusText: 'Unprocessable' });
    context.detect();
    expect(context.text('sd-delivery-edit-line-error-2')).toContain('exceeds the order');
    expect(context.text('sd-delivery-edit-error')).toContain('refused the highlighted line');
    context.by('sd-delivery-edit-reload')!.click();
    context.http.expectOne(ORDER).flush(order());
    context.detect();
    expect(context.by('sd-delivery-edit-line-error-2')).toBeNull();
    expect(context.view.draft.lines[1].quantity).toBe('1');
  });

  it('SD-DEL-ED-018 INVALID_STATUS, 404 and 500 are explained and never shown as success', () => {
    const context = loaded();
    context.view.draft.lines[0].quantity = '4';
    context.view.save();
    put(context).flush({ errorCode: 'SAL.DELIVERY.INVALID_STATUS' }, { status: 422, statusText: 'Unprocessable' });
    context.detect();
    expect(context.text('sd-delivery-edit-stale')).toContain('no longer Draft or Rejected');
    context.view.stale.set(null);
    context.view.save();
    put(context).flush({ errorCode: 'SAL.DELIVERY.NOT_FOUND' }, { status: 404, statusText: 'Not Found' });
    context.detect();
    expect(context.text('sd-delivery-edit-stale')).toContain('was not found');
    context.view.stale.set(null);
    context.view.save();
    put(context).flush({ traceId: 'T-9' }, { status: 500, statusText: 'Server Error' });
    context.detect();
    expect(context.text('sd-delivery-edit-stale')).toContain('could not confirm');
    expect(context.text('sd-delivery-edit-error')).toContain('T-9');
    expect(context.by('sd-delivery-edit-success')).toBeNull();
  });

  it('SD-DEL-ED-019 a Rejected delivery is editable with a resubmit hint but cannot be deleted', () => {
    const context = loaded({ status: 'Rejected' });
    expect(context.text('sd-delivery-edit-rejected')).toContain('Saving returns it to Draft');
    expect((context.by('sd-delivery-edit-delete') as HTMLButtonElement).disabled).toBe(true);
    expect(context.text('sd-delivery-edit-delete-hint')).toContain('Only a Draft can be deleted');
    context.view.remove();
    expect(confirm).not.toHaveBeenCalled();
  });

  it('SD-DEL-ED-020 Pending and Posted deliveries are explained and show no form', () => {
    for (const [status, message] of [['Pending', 'pending approval'], ['Posted', 'is posted']]) {
      TestBed.resetTestingModule();
      const context = loaded({ status });
      expect(context.text('sd-delivery-edit-blocked')).toContain(message);
      expect(context.by('sd-delivery-edit-form')).toBeNull();
      expect(context.view.canLeave()).toBe(true);
    }
  });

  it('SD-DEL-ED-021 deletes a Draft after a danger confirmation with the loaded version and locks the page', () => {
    const context = loaded();
    context.by('sd-delivery-edit-delete')!.click();
    expect(confirm.mock.calls[0][0]).toMatchObject({ danger: true, contentKey: 'sd.deliveryEdit.deleteConfirm', params: { number: 'DH0061' } });
    const request = context.http.expectOne(r => r.url === API && r.method === 'DELETE');
    expect(request.request.params.get('version')).toBe('3');
    request.flush(null, { status: 204, statusText: 'No Content' });
    context.detect();
    expect(context.text('sd-delivery-edit-deleted')).toContain('Outbound DH0061 was deleted');
    expect(context.by('sd-delivery-edit-form')).toBeNull();
    expect(context.view.canLeave()).toBe(true);
  });

  it('SD-DEL-ED-022 cancelled confirmation sends nothing; dirty input is named in the prompt; delete 409 keeps the input', () => {
    const context = loaded();
    confirm.mockReturnValueOnce(of(false));
    context.view.remove();
    context.http.expectNone(r => r.method === 'DELETE');
    context.view.draft.lines[0].quantity = '4';
    context.view.remove();
    expect(confirm.mock.calls[1][0].contentKey).toBe('sd.deliveryEdit.deleteConfirmDirty');
    context.http.expectOne(r => r.method === 'DELETE').flush({}, { status: 409, statusText: 'Conflict' });
    context.detect();
    expect(context.text('sd-delivery-edit-stale')).toContain('Someone else changed');
    expect(context.by('sd-delivery-edit-deleted')).toBeNull();
    expect(context.view.draft.lines[0].quantity).toBe('4');
  });

  it('SD-DEL-ED-023 delete timeout or 500 is uncertain, not success', () => {
    const context = loaded();
    context.view.remove();
    context.http.expectOne(r => r.method === 'DELETE').flush({ traceId: 'T-1' }, { status: 503, statusText: 'Unavailable' });
    context.detect();
    expect(context.text('sd-delivery-edit-stale')).toContain('could not confirm whether the outbound was deleted');
    expect(context.by('sd-delivery-edit-deleted')).toBeNull();
  });

  it('SD-DEL-ED-024 without DELETE there is no delete button; without order view the balance is replaced by a hint', () => {
    const context = loaded({}, ['SAL.DELIVERY.VIEW', 'SAL.DELIVERY.EDIT', 'MD.WAREHOUSE.VIEW']);
    expect(context.by('sd-delivery-edit-delete')).toBeNull();
    expect(context.by('sd-delivery-edit-balance-1')).toBeNull();
    expect(context.text('sd-delivery-edit-order-permission')).toContain('cannot view sales orders');
    expect(context.by('sd-delivery-edit-form')).not.toBeNull();
  });

  it('SD-DEL-ED-025 load 404, invalid id and a no-longer-Approved order are explained', () => {
    const context = setup();
    context.http.expectOne(API).flush({ errorCode: 'SAL.DELIVERY.NOT_FOUND' }, { status: 404, statusText: 'Not Found' });
    context.detect();
    expect(context.text('sd-delivery-edit-load-error')).toContain('Delivery not found');
    expect(context.by('sd-delivery-edit-form')).toBeNull();
    TestBed.resetTestingModule();
    const invalid = setup(undefined, 'abc');
    invalid.detect();
    expect(invalid.text('sd-delivery-edit-load-error')).toContain('Delivery not found');
    TestBed.resetTestingModule();
    const cancelled = setup();
    cancelled.http.expectOne(API).flush(saved());
    cancelled.http.expectOne(ORDER).flush(order('Cancelled'));
    cancelled.detect();
    expect(cancelled.text('sd-delivery-edit-order-error')).toContain('no longer Approved');
    expect(cancelled.by('sd-delivery-edit-balance-1')).toBeNull();
    expect((cancelled.by('sd-delivery-edit-delete') as HTMLButtonElement).disabled).toBe(false);
  });
});
