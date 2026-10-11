import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ANIMATION_MODULE_TYPE } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideNzIcons } from 'ng-zorro-antd/icon';
import { of } from 'rxjs';
import { provideTestTranslations } from '../../../testing/i18n-fixtures';
import { APP_ICONS } from '../../core/icons';
import { PermissionService } from '../../core/auth/permission.service';
import { ErpDialogService } from '../../shared/dialogs/dialog.service';
import { ItemConversionDialog } from './item-conversion-dialog';

const item = { id: 3, code: 'BOLT', name: 'Bolt', specification: null, baseUomId: 1,
  brandId: null, categoryId: null, isActive: true, version: 2 };
const uoms = [
  { id: 1, code: 'PCS', name: 'Piece', decimalPlaces: 0, isActive: true, version: 1 },
  { id: 2, code: 'BOX', name: 'Box', decimalPlaces: 0, isActive: true, version: 1 },
  { id: 5, code: 'OLD', name: 'Old', decimalPlaces: 0, isActive: false, version: 1 },
];
const boxRow = { id: 9, itemId: 3, uomId: 2, uomCode: 'BOX', factor: 12, version: 4 };
const API = '/api/v1/md/items/3/uom-conversions';

describe('MD-ITEM-CONV-UI item conversion dialog', () => {
  const confirm = vi.fn(() => of(true));
  let granted: Set<string>;
  beforeEach(() => {
    confirm.mockClear();
    granted = new Set(['MD.ITEM.VIEW', 'MD.ITEM.EDIT']);
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting(),
      ...provideTestTranslations('en'), provideNzIcons(APP_ICONS),
      { provide: ANIMATION_MODULE_TYPE, useValue: 'NoopAnimations' },
      { provide: ErpDialogService, useValue: { confirm } },
      { provide: PermissionService, useValue: { has: (code: string) => granted.has(code),
        hasAny: (codes: string[]) => codes.some((code) => granted.has(code)) } }] });
  });
  afterEach(() => TestBed.inject(HttpTestingController).verify());

  function open() {
    const fixture = TestBed.createComponent(ItemConversionDialog);
    fixture.componentRef.setInput('uoms', uoms);
    fixture.componentRef.setInput('item', item);
    TestBed.tick();
    const http = TestBed.inject(HttpTestingController);
    http.expectOne(API).flush([boxRow]);
    fixture.detectChanges();
    return { dialog: fixture.componentInstance, http, fixture };
  }

  it('lists rules against the base unit and creates, edits and deletes with versions', () => {
    const { dialog, http } = open();
    const body = document.body;
    expect(body.querySelector('[data-testid="md-item-conversion-row-BOX"]')?.textContent).toContain('1 BOX = 12 PCS');
    expect(dialog.unitOptions().map((x) => x.code)).toEqual(['BOX', 'OLD']);
    dialog.form.setValue({ uomId: 5, factor: '0' }); dialog.save();
    http.expectNone((r) => r.method === 'POST');
    dialog.form.setValue({ uomId: 5, factor: '0.5' }); dialog.save();
    const post = http.expectOne((r) => r.url === API && r.method === 'POST');
    expect(post.request.body).toEqual({ uomId: 5, factor: 0.5 });
    post.flush({ errorCode: 'MD.ITEM_UOM_CONVERSION.DUPLICATE' }, { status: 409, statusText: 'Conflict' });
    expect(dialog.error()).toBe('errors.MD.ITEM_UOM_CONVERSION.DUPLICATE');

    dialog.startEdit(boxRow);
    expect(dialog.form.controls.uomId.disabled).toBe(true);
    dialog.form.controls.factor.setValue('999999999.999999'); dialog.save();
    const put = http.expectOne(`${API}/9`);
    expect(put.request.body).toEqual({ factor: 999999999.999999, version: 4 });
    put.flush({ ...boxRow, factor: 999999999.999999, version: 5 });
    http.expectOne(API).flush([{ ...boxRow, version: 5 }]);
    expect(dialog.editing()).toBeNull();
    expect(dialog.form.controls.uomId.enabled).toBe(true);

    dialog.confirmDelete({ ...boxRow, version: 5 });
    expect(confirm).toHaveBeenCalledWith(expect.objectContaining({ titleKey: 'md.itemConversion.deleteConfirm', params: { unit: 'BOX' } }));
    const del = http.expectOne((r) => r.url === `${API}/9` && r.method === 'DELETE');
    expect(del.request.params.get('version')).toBe('5');
    del.flush(null);
    http.expectOne(API).flush([]);
    expect(dialog.rows()).toEqual([]);
  });

  it('converts on the server with the raw quantity and shows NO_PATH; read-only hides editing', () => {
    granted.delete('MD.ITEM.EDIT');
    const { dialog, http, fixture } = open();
    expect(document.body.querySelector('[data-testid="md-item-conversion-form"]')).toBeNull();
    expect(document.body.querySelector('[data-testid="md-item-conversion-edit-BOX"]')).toBeNull();
    dialog.convert();
    expect(dialog.calcError()).toBe('md.itemConversion.invalidCalculation');
    dialog.calculator.setValue({ quantity: '2.5', fromUomId: 2, toUomId: 1 }); dialog.convert();
    const convert = http.expectOne((r) => r.url === `${API}/convert`);
    expect(convert.request.params.get('quantity')).toBe('2.5');
    expect(convert.request.params.get('fromUomId')).toBe('2');
    convert.flush({ itemId: 3, fromUomId: 2, toUomId: 1, quantity: 2.5, result: 30 });
    expect(dialog.calcResult()).toBe(30);
    dialog.calculator.setValue({ quantity: '1', fromUomId: 5, toUomId: 1 }); dialog.convert();
    http.expectOne((r) => r.url === `${API}/convert`).flush({ errorCode: 'MD.UOM_CONVERSION.NO_PATH' },
      { status: 422, statusText: 'Unprocessable' });
    expect(dialog.calcError()).toBe('errors.MD.UOM_CONVERSION.NO_PATH');
    fixture.componentRef.setInput('item', null); TestBed.tick();
    expect(dialog.calcResult()).toBeNull();
    expect(dialog.rows()).toEqual([]);
  });
});
