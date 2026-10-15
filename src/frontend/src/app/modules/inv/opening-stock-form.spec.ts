import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting, TestRequest } from '@angular/common/http/testing';
import { ANIMATION_MODULE_TYPE } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideRouter, Router } from '@angular/router';
import { provideNzIcons } from 'ng-zorro-antd/icon';
import { provideTestTranslations } from '../../../testing/i18n-fixtures';
import { APP_ICONS } from '../../core/icons';
import { OpeningStockForm } from './opening-stock-form';

const API = '/api/v1/inv/opening-stocks';
const item = { id: 1, code: 'A001', name: 'Bolt', isActive: true };
const warehouse = { id: 2, code: 'WH1', name: 'Main', isActive: true };

describe('INV-OPN-007 opening stock form', () => {
  beforeEach(() => TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([]),
    ...provideTestTranslations('en'), provideNzIcons(APP_ICONS), { provide: ANIMATION_MODULE_TYPE, useValue: 'NoopAnimations' }] }));
  afterEach(() => TestBed.inject(HttpTestingController).verify());

  function open() {
    const fixture = TestBed.createComponent(OpeningStockForm);
    TestBed.tick();
    const http = TestBed.inject(HttpTestingController);
    fixture.detectChanges();
    const form = fixture.componentInstance;
    form.draft.openingDate = '2026-10-01';
    form.items.set([{ ...item, trackingMode: 'None' }]); form.warehouses.set([warehouse]);
    Object.assign(form.draft.lines[0], { itemId: 1, warehouseId: 2, quantity: '5', unitCost: '10' });
    return { form, http, fixture, element: fixture.nativeElement as HTMLElement };
  }
  const previewOk = (req: TestRequest) => req.flush({ lines: [{ lineNo: 1, amount: 50, errors: [] }], errorCount: 0, totalQuantity: 5, totalAmount: 50 });
  const submitReq = (http: HttpTestingController) => http.expectOne(r => r.url === API && r.method === 'POST');

  it('rejects invalid drafts before calling the API', () => {
    const { form, http } = open();
    expect(form.items().map(x => x.id)).toEqual([1]);
    form.draft.lines[0].quantity = '0'; form.runPreview();
    http.expectNone(`${API}/preview`);
    expect(form.error()).toBe('inv.opening.invalid');
  });

  it('shows trilingual line errors from preview and blocks submit until a clean preview', () => {
    const { form, http, fixture, element } = open();
    form.runPreview();
    http.expectOne(`${API}/preview`).flush({ lines: [{ lineNo: 1, amount: 50, errors: [
      { field: 'openingDate', code: 'INV.OPENING_STOCK.AFTER_MOVEMENT' }, { field: 'itemId', code: 'MD.ITEM.INACTIVE' }] }],
      errorCount: 2, totalQuantity: 5, totalAmount: 50 });
    fixture.detectChanges();
    const errors = element.querySelector('[data-testid="inv-opening-line-errors-0"]')!.textContent!;
    expect(errors).toContain('later than existing movements');
    expect(errors).toContain('This item is inactive.');
    expect(form.canSubmit()).toBe(false);
    form.submit(); http.expectNone(r => r.url === API && r.method === 'POST');
    form.runPreview(); previewOk(http.expectOne(`${API}/preview`));
    expect(form.canSubmit()).toBe(true);
    form.draft.lines[0].quantity = '6'; form.changed();
    expect(form.canSubmit()).toBe(false);
  });

  it('retries with the same request key after a network failure and navigates after replay', () => {
    const { form, http } = open();
    const navigate = vi.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true);
    form.runPreview(); previewOk(http.expectOne(`${API}/preview`));
    form.submit();
    const first = submitReq(http); const key = first.request.body.requestKey;
    first.error(new ProgressEvent('error'), { status: 0 });
    expect(form.error()).toBe('errors.http.network');
    expect(form.canSubmit()).toBe(true);
    form.submit();
    const retry = submitReq(http);
    expect(retry.request.body.requestKey).toBe(key);
    retry.flush({ id: 7, replayed: true, lines: [] }, { status: 200, statusText: 'OK' });
    expect(navigate).toHaveBeenCalledWith(['/inv/opening-stocks'], { queryParams: { posted: 7, replayed: 1 } });
  });

  it('starts a new request key after 409 and re-previews after 422', () => {
    const { form, http } = open();
    form.runPreview(); previewOk(http.expectOne(`${API}/preview`));
    const key = form.draft.requestKey;
    form.submit();
    submitReq(http).flush({ errorCode: 'INV.OPENING_STOCK.REQUEST_CONFLICT' }, { status: 409, statusText: 'Conflict' });
    expect(form.error()).toBe('errors.INV.OPENING_STOCK.REQUEST_CONFLICT');
    expect(form.draft.requestKey).not.toBe(key);
    expect(form.canSubmit()).toBe(false);
    form.runPreview(); previewOk(http.expectOne(`${API}/preview`));
    form.submit();
    submitReq(http).flush({ errorCode: 'INV.OPENING_STOCK.HAS_ERRORS' }, { status: 422, statusText: 'Unprocessable' });
    expect(form.error()).toBe('errors.INV.OPENING_STOCK.HAS_ERRORS');
    http.expectOne(`${API}/preview`).flush({ lines: [{ lineNo: 1, amount: 50, errors: [{ field: 'line', code: 'INV.OPENING_STOCK.DUPLICATE' }] }],
      errorCount: 1, totalQuantity: 5, totalAmount: 50 });
    expect(form.lineErrors(0)).toEqual([{ field: 'line', code: 'INV.OPENING_STOCK.DUPLICATE' }]);
    expect(form.canSubmit()).toBe(false);
  });
});
