import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting, TestRequest } from '@angular/common/http/testing';
import { ANIMATION_MODULE_TYPE } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideNzIcons } from 'ng-zorro-antd/icon';
import { of } from 'rxjs';
import { provideTestTranslations } from '../../../testing/i18n-fixtures';
import { PermissionService } from '../../core/auth/permission.service';
import { UNKNOWN_ERROR_KEY } from '../../core/http/api-error';
import { APP_ICONS } from '../../core/icons';
import { ErpDialogService } from '../../shared/dialogs/dialog.service';
import { PROCESS_PAGE_KIND, ProcessPage, SortedMasterKind } from './process-page';
import { Process, PROCESS_API, PROCESS_CATEGORY_API, ProcessCategory } from './process.service';
import { UOM_API } from './uom.service';
import { ITEM_ATTRIBUTE_API, ItemAttribute } from './item-attribute.service';

const paged = <T>(items: T[]) => ({ items, page: 1, pageSize: 20, totalCount: items.length });
const asm: ProcessCategory = { id: 1, code: 'ASM', name: 'Assembly', sortOrder: 1, processCount: 2, isActive: true, version: 1 };
const old: ProcessCategory = { id: 2, code: 'OLD', name: 'Old', sortOrder: 2, processCount: 1, isActive: false, version: 1 };
const cut: Process = { id: 5, code: 'CUT', name: 'Cutting', categoryId: 2, uomId: 9, standardRate: 1.5, sortOrder: 3, isActive: true, version: 4 };
const uoms = [{ id: 8, code: 'PCS', name: 'Pieces', decimalPlaces: 0, isActive: true, version: 1 },
  { id: 9, code: 'BOX', name: 'Box', decimalPlaces: 0, isActive: false, version: 1 }];
const color: ItemAttribute = { id: 1, code: 'COLOR', name: 'Color', sortOrder: 1, itemCount: 7, isActive: true, version: 3 };
const isGet =(url: string) => (r: { url: string; method: string }) => r.url === url && r.method === 'GET';

describe('MD-PROC-UI process pages', () => {
  const confirm = vi.fn(() => of(true));
  let granted = new Set<string>();
  function setup(kind: SortedMasterKind, permissions: string[]) {
    confirm.mockClear();
    granted = new Set(permissions);
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting(),
      ...provideTestTranslations('en'), provideNzIcons(APP_ICONS),
      { provide: ANIMATION_MODULE_TYPE, useValue: 'NoopAnimations' },
      { provide: PROCESS_PAGE_KIND, useValue: kind },
      { provide: ErpDialogService, useValue: { confirm } },
      { provide: PermissionService, useValue: { has: (code: string) => granted.has(code),
        hasAny: (codes: string[]) => codes.some((code) => granted.has(code)) } }] });
    const fixture = TestBed.createComponent(ProcessPage);
    TestBed.tick();
    return { fixture, page: fixture.componentInstance, http: TestBed.inject(HttpTestingController),
      element: fixture.nativeElement as HTMLElement };
  }
  afterEach(() => TestBed.inject(HttpTestingController).verify());

  it('lists categories by sort order with process counts and saves sort order', () => {
    const { fixture, page, http, element } = setup('category', ['MD.PROCESS_CATEGORY.VIEW', 'MD.PROCESS_CATEGORY.CREATE', 'MD.PROCESS_CATEGORY.EDIT']);
    const list = http.expectOne(isGet(PROCESS_CATEGORY_API));
    expect(list.request.params.get('sort')).toBe('sortOrder');
    list.flush(paged([asm, old]));
    http.expectNone(isGet(UOM_API));
    fixture.detectChanges();
    expect(element.querySelector('[data-testid="md-process-row-ASM"]')?.textContent).toContain('2');
    expect(element.querySelector('[data-testid="md-process-filter"]')).toBeNull();
    page.openCreate();
    page.form.patchValue({ code: 'pnt', name: 'Paint', sortOrder: 10000 });
    page.save();
    http.expectNone(r => r.method === 'POST');
    page.form.controls.sortOrder.setValue(5);
    page.save();
    expect(http.expectOne(r => r.method === 'POST').request.body).toEqual({ code: 'PNT', name: 'Paint', sortOrder: 5 });
  });

  it('shows the HAS_PROCESSES error when deleting a category in use', () => {
    const { fixture, page, http, element } = setup('category', ['MD.PROCESS_CATEGORY.VIEW', 'MD.PROCESS_CATEGORY.DELETE']);
    http.expectOne(isGet(PROCESS_CATEGORY_API)).flush(paged([asm]));
    page.confirmDelete(asm);
    const del = http.expectOne(r => r.method === 'DELETE');
    expect(del.request.params.get('version')).toBe('1');
    del.flush({ errorCode: 'MD.PROCESS_CATEGORY.HAS_PROCESSES' }, { status: 409, statusText: 'Conflict' });
    fixture.detectChanges();
    expect(page.error()).toBe('errors.MD.PROCESS_CATEGORY.HAS_PROCESSES');
    expect(element.textContent).toContain('still has processes');
    expect(element.querySelector('[data-testid="md-process-create"]')).toBeNull();
  });

  it('loads category and unit options, offers only active ones and keeps the current inactive values on edit', () => {
    const { fixture, page, http } = setup('process', ['MD.PROCESS.VIEW', 'MD.PROCESS.CREATE', 'MD.PROCESS.EDIT',
      'MD.PROCESS_CATEGORY.VIEW', 'MD.UOM.VIEW']);
    http.expectOne(isGet(PROCESS_API)).flush(paged([cut]));
    http.expectOne(isGet(PROCESS_CATEGORY_API)).flush(paged([asm, old]));
    http.expectOne(isGet(UOM_API)).flush(paged(uoms));
    fixture.detectChanges();
    expect(page.categoryName(2)).toBe('OLD — Old');
    expect(page.uomName(9)).toBe('BOX');
    page.openCreate();
    expect(page.categoryOptions().map(x => x.code)).toEqual(['ASM']);
    expect(page.uomOptions().map(x => x.code)).toEqual(['PCS']);
    page.openEdit(cut);
    expect(page.categoryOptions().map(x => x.code)).toEqual(['ASM', 'OLD']);
    expect(page.uomOptions().map(x => x.code)).toEqual(['PCS', 'BOX']);
    expect(page.form.controls.code.disabled).toBe(true);
    expect(page.form.controls.standardRate.value).toBe('1.5');
    page.form.controls.standardRate.setValue('1.1234567');
    page.save();
    http.expectNone(r => r.method === 'PUT');
    page.form.patchValue({ standardRate: '2.25', categoryId: 1 });
    page.save();
    const put = http.expectOne(r => r.method === 'PUT');
    expect(put.request.body).toEqual({ name: 'Cutting', categoryId: 1, uomId: 9, standardRate: 2.25, sortOrder: 3, isActive: true, version: 4 });
    put.flush({ errorCode: 'MD.PROCESS.INVALID_UOM' }, { status: 422, statusText: 'Unprocessable' });
    expect(page.saveError()).toBe('errors.MD.PROCESS.INVALID_UOM');
    expect(page.dialogOpen()).toBe(true);
  });

  it('filters by category and presets the filtered category on create', () => {
    const { page, http } = setup('process', ['MD.PROCESS.VIEW', 'MD.PROCESS.CREATE', 'MD.PROCESS_CATEGORY.VIEW']);
    http.expectOne(isGet(PROCESS_API)).flush(paged([]));
    http.expectOne(isGet(PROCESS_CATEGORY_API)).flush(paged([asm, old]));
    http.expectNone(isGet(UOM_API));
    page.filterControl.setValue(1);
    page.search();
    const list: TestRequest = http.expectOne(isGet(PROCESS_API));
    expect(list.request.params.get('categoryId')).toBe('1');
    list.flush(paged([]));
    page.openCreate();
    expect(page.form.controls.categoryId.value).toBe(1);
    page.filterControl.setValue(2);
    page.openCreate();
    expect(page.form.controls.categoryId.value).toBeNull();
  });

  it('MD-ATTR-UI lists item attribute definitions with item counts, saves and reports IN_USE on delete', () => {
    const { fixture, page, http, element } = setup('attribute', ['MD.ITEM_ATTRIBUTE.VIEW', 'MD.ITEM_ATTRIBUTE.CREATE',
      'MD.ITEM_ATTRIBUTE.EDIT', 'MD.ITEM_ATTRIBUTE.DELETE', 'MD.UOM.VIEW', 'MD.PROCESS_CATEGORY.VIEW']);
    const list = http.expectOne(isGet(ITEM_ATTRIBUTE_API));
    expect(list.request.params.get('sort')).toBe('sortOrder');
    list.flush(paged([color]));
    http.expectNone(isGet(UOM_API));
    http.expectNone(isGet(PROCESS_CATEGORY_API));
    fixture.detectChanges();
    expect(element.querySelector('[data-testid="md-process-title"]')?.textContent).toContain('Item attributes');
    expect(element.textContent).toContain('Items');
    expect(element.querySelector('[data-testid="md-process-row-COLOR"]')?.textContent).toContain('7');
    expect(element.querySelector('[data-testid="md-process-filter"]')).toBeNull();
    page.openCreate();
    page.form.patchValue({ code: 'size', name: ' Size ', sortOrder: 2 });
    page.save();
    const post = http.expectOne(r => r.method === 'POST' && r.url === ITEM_ATTRIBUTE_API);
    expect(post.request.body).toEqual({ code: 'SIZE', name: 'Size', sortOrder: 2 });
    post.flush({ errorCode: 'MD.ITEM_ATTRIBUTE.DUPLICATE_CODE' }, { status: 409, statusText: 'Conflict' });
    expect(page.saveError()).toBe('errors.MD.ITEM_ATTRIBUTE.DUPLICATE_CODE');
    page.openEdit(color);
    page.form.patchValue({ isActive: false });
    page.save();
    const put = http.expectOne(r => r.method === 'PUT' && r.url === `${ITEM_ATTRIBUTE_API}/1`);
    expect(put.request.body).toEqual({ name: 'Color', sortOrder: 1, isActive: false, version: 3 });
    put.flush({ ...color, isActive: false, version: 4 });
    http.expectOne(isGet(ITEM_ATTRIBUTE_API)).flush(paged([color]));
    page.confirmDelete(color);
    const del = http.expectOne(r => r.method === 'DELETE' && r.url === `${ITEM_ATTRIBUTE_API}/1`);
    expect(del.request.params.get('version')).toBe('3');
    del.flush({ errorCode: 'MD.ITEM_ATTRIBUTE.IN_USE' }, { status: 409, statusText: 'Conflict' });
    fixture.detectChanges();
    expect(element.textContent).toContain('Items use this attribute');
  });

  it('does not read reference APIs without view permission and reports list errors', () => {
    const { page, http } = setup('process', ['MD.PROCESS.VIEW']);
    http.expectOne(isGet(PROCESS_API)).flush({}, { status: 500, statusText: 'Server Error' });
    http.expectNone(isGet(PROCESS_CATEGORY_API));
    http.expectNone(isGet(UOM_API));
    expect(page.rows()).toEqual([]);
    expect(page.error()).toBe(UNKNOWN_ERROR_KEY);
    expect(page.categoryName(3)).toBe('3');
    expect(page.uomName(null)).toBe('');
  });
});
