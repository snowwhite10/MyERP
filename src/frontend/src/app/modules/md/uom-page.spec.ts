import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ANIMATION_MODULE_TYPE } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideNzIcons } from 'ng-zorro-antd/icon';
import { of } from 'rxjs';
import { provideTestTranslations } from '../../../testing/i18n-fixtures';
import { APP_ICONS } from '../../core/icons';
import { ErpDialogService } from '../../shared/dialogs/dialog.service';
import { UomPage } from './uom-page';
import { UOM_API, UOM_CONVERSION_API } from './uom.service';

const kg = { id: 1, code: 'KG', name: 'Kilogram', decimalPlaces: 3, isActive: true, version: 2 };
const g = { id: 2, code: 'G', name: 'Gram', decimalPlaces: 0, isActive: true, version: 1 };
const box = { id: 3, code: 'BOX', name: 'Box', decimalPlaces: 0, isActive: false, version: 4 };
const kgToG = { id: 7, fromUomId: 1, fromUomCode: 'KG', toUomId: 2, toUomCode: 'G', factor: 1000, version: 3 };
const page = (items: unknown[]) => ({ items, page: 1, pageSize: 20, totalCount: items.length });
const isUnitList = (r: { url: string; method: string }) => r.url === UOM_API && r.method === 'GET';

describe('MD-UOM-004 unit of measure page', () => {
  const confirm = vi.fn(() => of(true));
  beforeEach(() => {
    confirm.mockClear();
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting(),
      ...provideTestTranslations('en'), provideNzIcons(APP_ICONS),
      { provide: ANIMATION_MODULE_TYPE, useValue: 'NoopAnimations' },
      { provide: ErpDialogService, useValue: { confirm } }] });
  });
  afterEach(() => TestBed.inject(HttpTestingController).verify());

  function create() {
    const fixture = TestBed.createComponent(UomPage);
    TestBed.tick();
    const http = TestBed.inject(HttpTestingController);
    flushUnits(http);
    http.expectOne(UOM_CONVERSION_API).flush([kgToG]);
    fixture.detectChanges();
    return { pageComponent: fixture.componentInstance, http, fixture, element: fixture.nativeElement as HTMLElement };
  }

  function flushUnits(http: HttpTestingController) {
    const requests = http.match(isUnitList);
    expect(requests.length).toBe(2);
    requests.forEach((request) => request.flush(page([kg, g, box])));
  }

  it('lists units and conversions and validates the unit form before saving', () => {
    const { pageComponent, http, element } = create();
    expect(element.querySelector('[data-testid="md-uom-title"]')?.textContent).toContain('Units of measure');
    expect(element.querySelector('[data-testid="md-uom-row-KG"]')?.textContent).toContain('3');
    expect(element.querySelector('[data-testid="md-uom-conversion-row-KG-G"]')?.textContent).toContain('1 KG = 1000 G');
    expect(pageComponent.options().map((x) => x.code)).toEqual(['KG', 'G', 'BOX']);

    pageComponent.openCreate();
    pageComponent.form.patchValue({ code: 'bad code', name: 'Piece' });
    pageComponent.save();
    http.expectNone((r) => r.method === 'POST');
    pageComponent.form.patchValue({ code: 'pcs', decimalPlaces: 2 });
    pageComponent.save();
    pageComponent.save();
    const post = http.expectOne((r) => r.url === UOM_API && r.method === 'POST');
    expect(post.request.body).toEqual({ code: 'PCS', name: 'Piece', decimalPlaces: 2 });
    post.flush({ ...kg, id: 9, code: 'PCS' });
    expect(pageComponent.dialogOpen()).toBe(false);
    flushUnits(http);

    pageComponent.openEdit(kg);
    expect(pageComponent.form.controls.code.disabled).toBe(true);
    pageComponent.form.patchValue({ decimalPlaces: 6, isActive: false });
    pageComponent.save();
    const put = http.expectOne(`${UOM_API}/1`);
    expect(put.request.body).toEqual({ name: 'Kilogram', isActive: false, decimalPlaces: 6, version: 2 });
    put.flush({ errorCode: 'MD.UOM.NOT_FOUND' }, { status: 404, statusText: 'Not Found' });
    expect(pageComponent.dialogOpen()).toBe(true);
    expect(pageComponent.saveError()).toBe('errors.MD.UOM.NOT_FOUND');
  });

  it('creates, edits and deletes conversions and shows path errors', () => {
    const { pageComponent, http } = create();
    pageComponent.openCreateConversion();
    pageComponent.conversionForm.patchValue({ fromUomId: 1, toUomId: 1, factor: '5' });
    pageComponent.saveConversion();
    expect(pageComponent.conversionForm.hasError('sameUnit')).toBe(true);
    pageComponent.conversionForm.patchValue({ toUomId: 2, factor: '0' });
    pageComponent.saveConversion();
    http.expectNone((r) => r.method === 'POST');
    pageComponent.conversionForm.patchValue({ fromUomId: 2, toUomId: 1, factor: ' 0.001 ' });
    pageComponent.saveConversion();
    expect(pageComponent.conversionForm.invalid).toBe(true);
    pageComponent.conversionForm.patchValue({ factor: '0.001' });
    pageComponent.saveConversion();
    const post = http.expectOne((r) => r.url === UOM_CONVERSION_API && r.method === 'POST');
    expect(post.request.body).toEqual({ fromUomId: 2, toUomId: 1, factor: 0.001 });
    post.flush({ errorCode: 'MD.UOM_CONVERSION.PATH_EXISTS' }, { status: 409, statusText: 'Conflict' });
    expect(pageComponent.saveError()).toBe('errors.MD.UOM_CONVERSION.PATH_EXISTS');
    expect(pageComponent.conversionDialogOpen()).toBe(true);

    pageComponent.openEditConversion(kgToG);
    expect(pageComponent.conversionForm.controls.fromUomId.disabled).toBe(true);
    expect(pageComponent.conversionForm.controls.factor.value).toBe('1000');
    pageComponent.conversionForm.patchValue({ factor: '1000.5' });
    pageComponent.saveConversion();
    http.expectOne(`${UOM_CONVERSION_API}/7`).flush({ ...kgToG, factor: 1000.5, version: 4 });
    expect(pageComponent.conversionDialogOpen()).toBe(false);
    http.expectOne(UOM_CONVERSION_API).flush([{ ...kgToG, factor: 1000.5, version: 4 }]);

    pageComponent.confirmDeleteConversion(kgToG);
    expect(confirm).toHaveBeenCalledWith(expect.objectContaining({ titleKey: 'md.uom.deleteConversionConfirm',
      params: { from: 'KG', to: 'G' } }));
    http.expectOne(`${UOM_CONVERSION_API}/7?version=3`).flush(null);
    http.expectOne(UOM_CONVERSION_API).flush([]);
    expect(pageComponent.conversions()).toEqual([]);

    pageComponent.confirmDelete(g);
    http.expectOne(`${UOM_API}/2?version=1`).flush({ errorCode: 'MD.UOM.IN_USE' }, { status: 409, statusText: 'Conflict' });
    expect(pageComponent.error()).toBe('errors.MD.UOM.IN_USE');
    expect(pageComponent.busy()).toBe(false);
  });

  it('converts through the API and reports invalid input or missing paths', () => {
    const { pageComponent, http, fixture, element } = create();
    pageComponent.convert();
    expect(pageComponent.calcError()).toBe('md.uom.invalidCalculation');
    http.expectNone((r) => r.url.endsWith('/convert'));

    pageComponent.calculator.setValue({ quantity: '1.5', fromUomId: 2, toUomId: 1 });
    pageComponent.convert();
    const convert = http.expectOne((r) => r.url === `${UOM_CONVERSION_API}/convert`);
    expect(convert.request.params.get('quantity')).toBe('1.5');
    convert.flush({ fromUomId: 2, toUomId: 1, quantity: 1.5, result: 0.002 });
    fixture.detectChanges();
    expect(element.querySelector('[data-testid="md-uom-calc-result"]')?.textContent).toContain('0.002');

    pageComponent.calculator.patchValue({ toUomId: 3 });
    pageComponent.convert();
    http.expectOne((r) => r.url === `${UOM_CONVERSION_API}/convert`)
      .flush({ errorCode: 'MD.UOM_CONVERSION.NO_PATH' }, { status: 422, statusText: 'Unprocessable' });
    expect(pageComponent.calcResult()).toBeNull();
    expect(pageComponent.calcError()).toBe('errors.MD.UOM_CONVERSION.NO_PATH');
  });
});
