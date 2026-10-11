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
import { StorageLocationDialog } from './storage-location-dialog';

const warehouse = { id: 3, code: 'MAIN', name: 'Main', address: null, contactName: null, phone: null, isActive: true, version: 2 };
const shelf = { id: 8, warehouseId: 3, code: 'A-01', name: 'Shelf A1', isActive: true, version: 4 };
const API = '/api/v1/md/warehouses/3/locations';

describe('MD-WH-LOC-UI storage location dialog', () => {
  const confirm = vi.fn(() => of(true));
  let granted: Set<string>;
  beforeEach(() => {
    confirm.mockClear();
    granted = new Set(['MD.WAREHOUSE.VIEW', 'MD.WAREHOUSE.EDIT']);
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting(),
      ...provideTestTranslations('en'), provideNzIcons(APP_ICONS),
      { provide: ANIMATION_MODULE_TYPE, useValue: 'NoopAnimations' },
      { provide: ErpDialogService, useValue: { confirm } },
      { provide: PermissionService, useValue: { has: (code: string) => granted.has(code),
        hasAny: (codes: string[]) => codes.some((code) => granted.has(code)) } }] });
  });
  afterEach(() => TestBed.inject(HttpTestingController).verify());

  function open(target = warehouse) {
    const fixture = TestBed.createComponent(StorageLocationDialog);
    fixture.componentRef.setInput('warehouse', target);
    TestBed.tick();
    const http = TestBed.inject(HttpTestingController);
    const list = http.expectOne((r) => r.url === API);
    expect(list.request.params.get('page')).toBe('1');
    list.flush({ items: [shelf], totalCount: 11 });
    fixture.detectChanges();
    return { dialog: fixture.componentInstance, http, fixture };
  }

  it('lists, searches and pages locations, then creates, edits and deletes with versions', () => {
    const { dialog, http } = open();
    expect(document.body.querySelector('[data-testid="md-location-row-A-01"]')?.textContent).toContain('Shelf A1');
    dialog.searchControl.setValue(' shelf '); dialog.search();
    expect(http.expectOne((r) => r.url === API).request.params.get('keyword')).toBe('shelf');
    dialog.onQueryParams({ pageIndex: 2, pageSize: 10, sort: [], filter: [] });
    http.expectOne((r) => r.url === API && r.params.get('page') === '2').flush({ items: [], totalCount: 11 });

    dialog.form.patchValue({ code: 'a 1', name: 'X' }); dialog.save();
    http.expectNone((r) => r.method === 'POST');
    dialog.form.patchValue({ code: 'a-02', name: ' Shelf A2 ' }); dialog.save();
    const post = http.expectOne((r) => r.url === API && r.method === 'POST');
    expect(post.request.body).toEqual({ code: 'A-02', name: 'Shelf A2' });
    post.flush({ errorCode: 'MD.STORAGE_LOCATION.DUPLICATE_CODE' }, { status: 409, statusText: 'Conflict' });
    expect(dialog.error()).toBe('errors.MD.STORAGE_LOCATION.DUPLICATE_CODE');

    dialog.startEdit(shelf);
    expect(dialog.form.controls.code.disabled).toBe(true);
    dialog.form.patchValue({ name: 'Cold shelf', isActive: false }); dialog.save();
    const put = http.expectOne(`${API}/8`);
    expect(put.request.body).toEqual({ name: 'Cold shelf', isActive: false, version: 4 });
    put.flush({ ...shelf, name: 'Cold shelf', isActive: false, version: 5 });
    http.expectOne((r) => r.url === API).flush({ items: [{ ...shelf, version: 5 }], totalCount: 1 });
    expect(dialog.editing()).toBeNull();

    dialog.confirmDelete({ ...shelf, version: 5 });
    expect(confirm).toHaveBeenCalledWith(expect.objectContaining({ titleKey: 'md.storageLocation.deleteConfirm', params: { code: 'A-01' } }));
    const del = http.expectOne((r) => r.method === 'DELETE');
    expect(del.request.params.get('version')).toBe('5');
    del.flush(null);
    http.expectOne((r) => r.url === API).flush({ items: [], totalCount: 0 });
    expect(dialog.rows()).toEqual([]);
  });

  it('hides the add form for an inactive warehouse and editing without permission; resets when closed', () => {
    const { dialog, fixture } = open({ ...warehouse, isActive: false });
    expect(document.body.querySelector('[data-testid="md-location-inactive"]')).not.toBeNull();
    expect(document.body.querySelector('[data-testid="md-location-form"]')).toBeNull();
    dialog.startEdit(shelf); fixture.detectChanges();
    expect(document.body.querySelector('[data-testid="md-location-form"]')).not.toBeNull();
    fixture.componentRef.setInput('warehouse', null); TestBed.tick();
    expect(dialog.rows()).toEqual([]);
    expect(dialog.editing()).toBeNull();
  });

  it('read-only users see locations without edit controls', () => {
    granted.delete('MD.WAREHOUSE.EDIT');
    open();
    expect(document.body.querySelector('[data-testid="md-location-row-A-01"]')).not.toBeNull();
    expect(document.body.querySelector('[data-testid="md-location-form"]')).toBeNull();
    expect(document.body.querySelector('[data-testid="md-location-edit-A-01"]')).toBeNull();
  });
});
