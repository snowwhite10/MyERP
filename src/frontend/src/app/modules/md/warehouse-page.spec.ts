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
import { WarehousePage } from './warehouse-page';

const warehouse = { id: 3, code: 'MAIN', name: 'Main', address: 'Taipei', contactName: 'Amy', phone: '02-1',
  isActive: true, version: 2 };
const API = '/api/v1/md/warehouses';

describe('MD-WH-UI warehouse page', () => {
  const confirm = vi.fn(() => of(true));
  let granted: Set<string>;
  beforeEach(() => {
    confirm.mockClear();
    granted = new Set(['MD.WAREHOUSE.VIEW', 'MD.WAREHOUSE.CREATE', 'MD.WAREHOUSE.EDIT', 'MD.WAREHOUSE.DELETE']);
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting(),
      ...provideTestTranslations('en'), provideNzIcons(APP_ICONS),
      { provide: ANIMATION_MODULE_TYPE, useValue: 'NoopAnimations' },
      { provide: ErpDialogService, useValue: { confirm } },
      { provide: PermissionService, useValue: { has: (code: string) => granted.has(code),
        hasAny: (codes: string[]) => codes.some((code) => granted.has(code)) } }] });
  });
  afterEach(() => TestBed.inject(HttpTestingController).verify());

  function open() {
    const fixture = TestBed.createComponent(WarehousePage);
    TestBed.tick();
    const http = TestBed.inject(HttpTestingController);
    http.expectOne((r) => r.url === API).flush({ items: [warehouse], totalCount: 1 });
    fixture.detectChanges();
    return { page: fixture.componentInstance, http, fixture };
  }

  it('lists warehouses, validates fields and sends create and versioned update', () => {
    const { page, http, fixture } = open();
    expect((fixture.nativeElement as HTMLElement).querySelector('[data-testid="md-warehouse-row-MAIN"]')?.textContent).toContain('Taipei');
    page.openCreate(); page.form.patchValue({ code: 'bad code', name: 'Spare' }); page.save();
    http.expectNone((r) => r.method === 'POST');
    page.form.patchValue({ code: 'spare', phone: '9'.repeat(51) }); page.save();
    expect(page.detailsInvalid()).toBe(true);
    http.expectNone((r) => r.method === 'POST');
    page.form.patchValue({ phone: ' 03 ', address: '' }); page.save();
    const post = http.expectOne((r) => r.url === API && r.method === 'POST');
    expect(post.request.body).toEqual({ code: 'SPARE', name: 'Spare', address: null, contactName: null, phone: '03' });
    post.flush({ ...warehouse, id: 4, code: 'SPARE' });
    http.expectOne((r) => r.url === API && r.method === 'GET').flush({ items: [warehouse], totalCount: 1 });
    expect(page.dialogOpen()).toBe(false);

    page.openEdit(warehouse); expect(page.form.controls.code.disabled).toBe(true);
    page.form.patchValue({ isActive: false }); page.save();
    const put = http.expectOne(`${API}/3`);
    expect(put.request.body).toMatchObject({ name: 'Main', address: 'Taipei', isActive: false, version: 2 });
    put.flush({ errorCode: 'COMMON.CONCURRENCY.CONFLICT' }, { status: 409, statusText: 'Conflict' });
    expect(page.dialogOpen()).toBe(true);
    expect(page.saveError()).toBe('errors.COMMON.CONCURRENCY.CONFLICT');
    http.expectOne((r) => r.url === API && r.method === 'GET').flush({ items: [{ ...warehouse, version: 3 }], totalCount: 1 });
    expect(page.rows()[0].version).toBe(3);
  });

  it('MD-F05 MD-F06 ignores a second delete while one is in flight and reloads after the row was deleted elsewhere', () => {
    const { page, http, fixture } = open();
    page.confirmDelete(warehouse);
    page.confirmDelete(warehouse);
    expect(confirm).toHaveBeenCalledTimes(1);
    expect(page.deleting()).toBe(true);
    fixture.detectChanges();
    expect((fixture.nativeElement as HTMLElement).querySelector<HTMLButtonElement>('[data-testid="md-warehouse-delete-MAIN"]')!.disabled).toBe(true);
    http.expectOne((r) => r.method === 'DELETE').flush({ errorCode: 'MD.WAREHOUSE.NOT_FOUND' }, { status: 404, statusText: 'Not Found' });
    expect(page.deleting()).toBe(false);
    expect(page.error()).toBe('errors.MD.WAREHOUSE.NOT_FOUND');
    http.expectOne((r) => r.url === API && r.method === 'GET').flush({ items: [], totalCount: 0 });
    expect(page.rows()).toEqual([]);
    expect(page.error()).toBe('errors.MD.WAREHOUSE.NOT_FOUND');
  });

  it('confirms delete with version, shows HAS_LOCATIONS and opens the location dialog', () => {
    const { page, http, fixture } = open();
    page.confirmDelete(warehouse);
    expect(confirm).toHaveBeenCalledWith(expect.objectContaining({ titleKey: 'md.warehouse.deleteConfirm', params: { code: 'MAIN' } }));
    const del = http.expectOne((r) => r.method === 'DELETE');
    expect(del.request.params.get('version')).toBe('2');
    del.flush({ errorCode: 'MD.WAREHOUSE.HAS_LOCATIONS' }, { status: 409, statusText: 'Conflict' });
    expect(page.error()).toBe('errors.MD.WAREHOUSE.HAS_LOCATIONS');

    (fixture.nativeElement as HTMLElement).querySelector<HTMLButtonElement>('[data-testid="md-warehouse-locations-MAIN"]')!.click();
    TestBed.tick();
    http.expectOne((r) => r.url === `${API}/3/locations`).flush({ items: [], totalCount: 0 });
    expect(page.locationWarehouse()).toEqual(warehouse);
  });

  it('hides create, edit and delete without permissions', () => {
    granted = new Set(['MD.WAREHOUSE.VIEW']);
    const { fixture } = open();
    const element = fixture.nativeElement as HTMLElement;
    expect(element.querySelector('[data-testid="md-warehouse-create"]')).toBeNull();
    expect(element.querySelector('[data-testid="md-warehouse-edit-MAIN"]')).toBeNull();
    expect(element.querySelector('[data-testid="md-warehouse-delete-MAIN"]')).toBeNull();
    expect(element.querySelector('[data-testid="md-warehouse-locations-MAIN"]')).not.toBeNull();
  });

  it('submits a committed keyword, separates empty results and offers a retry with trace id', () => {
    const { page, http, fixture } = open();
    page.searchControl.setValue('  spare  ');
    page.reload();
    const original = http.expectOne((r) => r.url === API);
    expect(original.request.params.get('keyword')).toBeNull();
    original.flush({ items: [warehouse], totalCount: 1 });
    page.search();
    const searched = http.expectOne((r) => r.url === API);
    expect(searched.request.params.get('keyword')).toBe('spare');
    searched.flush({ items: [], totalCount: 0 });
    fixture.detectChanges();
    expect((fixture.nativeElement as HTMLElement).textContent).toContain('No matching warehouses');
    page.reload();
    http.expectOne((r) => r.url === API).flush({ errorCode: 'COMMON.INTERNAL_ERROR', traceId: 'trace-wh' }, { status: 500, statusText: 'Error' });
    fixture.detectChanges();
    expect((fixture.nativeElement as HTMLElement).querySelector('[data-testid="md-warehouse-trace-id"]')?.textContent).toContain('trace-wh');
    (fixture.nativeElement as HTMLElement).querySelector<HTMLButtonElement>('[data-testid="md-warehouse-retry"]')!.click();
    http.expectOne((r) => r.url === API).flush({ items: [warehouse], totalCount: 1 });
  });
});
