// 介面 8.16（REQ_F4118.45.5–6）：物料屬性對話框的查看、整批儲存、選項過濾與伺服器列錯誤（ADR-045）。
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ANIMATION_MODULE_TYPE } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideNzIcons } from 'ng-zorro-antd/icon';
import { provideTestTranslations } from '../../../testing/i18n-fixtures';
import { PermissionService } from '../../core/auth/permission.service';
import { APP_ICONS } from '../../core/icons';
import { ItemAttributeDialog } from './item-attribute-dialog';
import { ITEM_ATTRIBUTE_API, ItemAttribute, ItemAttributeValue, ItemAttributeValueService, MAX_ATTRIBUTES_PER_ITEM } from './item-attribute.service';

const item = { id: 3, code: 'BOLT', name: 'Bolt', specification: null, baseUomId: 1, brandId: null, categoryId: null, isActive: true, version: 2 };
const VALUES_API = '/api/v1/md/items/3/attributes';
const color: ItemAttribute = { id: 1, code: 'COLOR', name: 'Color', sortOrder: 1, itemCount: 1, isActive: true, version: 1 };
const size: ItemAttribute = { id: 2, code: 'SIZE', name: 'Size', sortOrder: 2, itemCount: 0, isActive: true, version: 1 };
const legacy: ItemAttribute = { id: 3, code: 'LEGACY', name: 'Legacy', sortOrder: 3, itemCount: 1, isActive: false, version: 1 };
const unused: ItemAttribute = { id: 4, code: 'GONE', name: 'Gone', sortOrder: 4, itemCount: 0, isActive: false, version: 1 };
const values: ItemAttributeValue[] = [
  { attributeId: 1, attributeCode: 'COLOR', attributeName: 'Color', sortOrder: 1, isAttributeActive: true, value: 'Red' },
  { attributeId: 3, attributeCode: 'LEGACY', attributeName: 'Legacy', sortOrder: 3, isAttributeActive: false, value: 'X' },
];
const paged = <T>(items: T[]) => ({ items, page: 1, pageSize: 200, totalCount: items.length });

describe('MD-ATTR-UI item attribute dialog', () => {
  let granted: Set<string>;
  function open(permissions: string[]) {
    granted = new Set(permissions);
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting(),
      ...provideTestTranslations('en'), provideNzIcons(APP_ICONS),
      { provide: ANIMATION_MODULE_TYPE, useValue: 'NoopAnimations' },
      { provide: PermissionService, useValue: { has: (code: string) => granted.has(code),
        hasAny: (codes: string[]) => codes.some((code) => granted.has(code)) } }] });
    const fixture = TestBed.createComponent(ItemAttributeDialog);
    const closed = vi.fn();
    fixture.componentInstance.closed.subscribe(closed);
    fixture.componentRef.setInput('item', item);
    TestBed.tick();
    return { fixture, dialog: fixture.componentInstance, http: TestBed.inject(HttpTestingController), closed };
  }
  const el = (testId: string) => document.querySelector(`[data-testid="${testId}"]`);
  afterEach(() => TestBed.inject(HttpTestingController).verify());

  it('lists values read-only without edit permission and does not load definitions', () => {
    const { fixture, http } = open(['MD.ITEM.VIEW', 'MD.ITEM_ATTRIBUTE.VIEW']);
    http.expectOne(VALUES_API).flush(values);
    http.expectNone(r => r.url === ITEM_ATTRIBUTE_API);
    fixture.detectChanges();
    expect(el('md-item-attribute-value-COLOR')?.textContent).toContain('Red');
    expect(el('md-item-attribute-name-LEGACY')?.textContent).toContain('Inactive');
    expect(el('md-item-attribute-form')).toBeNull();
  });

  it('offers active attributes plus existing inactive ones, excludes those chosen in other rows and saves the full set', () => {
    const { fixture, dialog, http, closed } = open(['MD.ITEM.VIEW', 'MD.ITEM.EDIT', 'MD.ITEM_ATTRIBUTE.VIEW']);
    http.expectOne(VALUES_API).flush(values);
    const list = http.expectOne(r => r.url === ITEM_ATTRIBUTE_API);
    expect(list.request.params.get('sort')).toBe('sortOrder');
    list.flush(paged([color, size, legacy, unused]));
    fixture.detectChanges();
    expect(dialog.rows.length).toBe(2);
    expect(dialog.optionsFor(0).map(x => x.code)).toEqual(['COLOR', 'SIZE']);
    expect(dialog.optionsFor(1).map(x => x.code)).toEqual(['SIZE', 'LEGACY']);
    dialog.addRow();
    expect(dialog.optionsFor(2).map(x => x.code)).toEqual(['SIZE']);
    dialog.save();
    http.expectNone(r => r.method === 'PUT');
    fixture.detectChanges();
    expect(el('md-item-attribute-row-error-2')?.textContent).toContain('Choose an active attribute');
    dialog.rows.at(2).setValue({ attributeId: 2, value: '  M  ' });
    dialog.removeRow(1);
    dialog.save();
    const put = http.expectOne(r => r.method === 'PUT' && r.url === VALUES_API);
    expect(put.request.body).toEqual({ attributes: [{ attributeId: 1, value: 'Red' }, { attributeId: 2, value: 'M' }] });
    put.flush([values[0]]);
    expect(closed).toHaveBeenCalled();
  });

  it('marks the row named by a server field error and shows other errors as an alert', () => {
    const { fixture, dialog, http } = open(['MD.ITEM.VIEW', 'MD.ITEM.EDIT']);
    http.expectOne(VALUES_API).flush(values);
    http.expectNone(r => r.url === ITEM_ATTRIBUTE_API);
    fixture.detectChanges();
    expect(el('md-item-attribute-no-definitions')).not.toBeNull();
    expect(dialog.optionsFor(0).map(x => x.code)).toEqual(['COLOR']);
    dialog.save();
    http.expectOne(r => r.method === 'PUT').flush({ errorCode: 'COMMON.REQUEST.VALIDATION_FAILED',
      errors: { 'attributes[1].attributeId': ['Attribute must be active.'] } }, { status: 400, statusText: 'Bad Request' });
    fixture.detectChanges();
    expect(dialog.rowError(1)).toBe('md.itemAttributeValue.invalidAttribute');
    expect(dialog.error()).toBeNull();
    dialog.rows.at(1).controls.value.setValue('Y');
    expect(dialog.rowError(1)).toBeNull();
    dialog.save();
    http.expectOne(r => r.method === 'PUT').flush({ errorCode: 'MD.ITEM.NOT_FOUND' }, { status: 404, statusText: 'Not Found' });
    fixture.detectChanges();
    expect(el('md-item-attribute-error')?.textContent).toContain('Item not found');
  });

  it('limits rows to the maximum and trims values in the service', () => {
    const { dialog, http } = open(['MD.ITEM.VIEW', 'MD.ITEM.EDIT']);
    http.expectOne(VALUES_API).flush([]);
    for (let i = 0; i < MAX_ATTRIBUTES_PER_ITEM + 3; i++) dialog.addRow();
    expect(dialog.rows.length).toBe(MAX_ATTRIBUTES_PER_ITEM);
    TestBed.inject(ItemAttributeValueService).replace(3, []).subscribe();
    expect(http.expectOne(r => r.method === 'PUT').request.body).toEqual({ attributes: [] });
  });

  it('shows a load error', () => {
    const { fixture, http } = open(['MD.ITEM.VIEW']);
    http.expectOne(VALUES_API).flush({ errorCode: 'MD.ITEM.NOT_FOUND' }, { status: 404, statusText: 'Not Found' });
    fixture.detectChanges();
    expect(el('md-item-attribute-error')?.textContent).toContain('Item not found');
  });

  it('ignores a save response after switching to another item', () => {
    const { fixture, dialog, http, closed } = open(['MD.ITEM.VIEW', 'MD.ITEM.EDIT']);
    http.expectOne(VALUES_API).flush(values);
    dialog.save();
    const oldSave = http.expectOne(r => r.method === 'PUT' && r.url === VALUES_API);
    fixture.componentRef.setInput('item', { ...item, id: 4, code: 'NUT' });
    TestBed.tick();
    http.expectOne('/api/v1/md/items/4/attributes').flush([]);
    oldSave.flush(values);
    expect(closed).not.toHaveBeenCalled();
    expect(dialog.values()).toEqual([]);
    expect(dialog.saving()).toBe(false);
  });
});
