import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ANIMATION_MODULE_TYPE } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideNzIcons } from 'ng-zorro-antd/icon';
import { of } from 'rxjs';
import { provideTestTranslations } from '../../../testing/i18n-fixtures';
import { APP_ICONS } from '../../core/icons';
import { ErpDialogService } from '../../shared/dialogs/dialog.service';
import { ItemCategoryPage } from './item-category-page';
import { ITEM_CATEGORY_API } from './item-category.service';

const raw = { id: 1, code: 'RAW', name: 'Raw material', parentId: null, isActive: true, version: 2 };
const metal = { id: 2, code: 'METAL', name: 'Metal', parentId: 1, isActive: true, version: 1 };
const pack = { id: 3, code: 'PACK', name: 'Packaging', parentId: null, isActive: false, version: 1 };
const isList = (r: { url: string; method: string }) => r.url === ITEM_CATEGORY_API && r.method === 'GET';

describe('MD-ITEM-CAT-004 item category page', () => {
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
    const fixture = TestBed.createComponent(ItemCategoryPage);
    TestBed.tick();
    const http = TestBed.inject(HttpTestingController);
    http.expectOne(isList).flush([pack, metal, raw]);
    fixture.detectChanges();
    return { pageComponent: fixture.componentInstance, http, fixture, element: fixture.nativeElement as HTMLElement };
  }

  it('shows the tree in order, filters on the client and creates a subcategory', () => {
    const { pageComponent, http, fixture, element } = create();
    expect(element.querySelector('[data-testid="md-item-category-title"]')?.textContent).toContain('Item categories');
    const rows = [...element.querySelectorAll('tr[data-testid^="md-item-category-row-"]')];
    expect(rows.map((row) => row.getAttribute('data-testid'))).toEqual(
      ['md-item-category-row-PACK', 'md-item-category-row-RAW', 'md-item-category-row-METAL']);
    expect(rows[2].getAttribute('aria-level')).toBe('2');
    expect(element.querySelector('[data-testid="md-item-category-add-child-PACK"]')).toBeNull();

    pageComponent.searchControl.setValue('metal');
    pageComponent.search();
    fixture.detectChanges();
    expect(pageComponent.visibleRows().map((x) => x.code)).toEqual(['RAW', 'METAL']);

    pageComponent.openCreate(pageComponent.rows().find((x) => x.code === 'METAL')!);
    expect(pageComponent.form.controls.parentId.value).toBe(2);
    expect(pageComponent.parentChoices().map((x) => x.code)).toEqual(['RAW', 'METAL']);
    pageComponent.form.patchValue({ code: 'bad code', name: 'Steel' });
    pageComponent.save();
    http.expectNone((r) => r.method === 'POST');
    pageComponent.form.patchValue({ code: 'steel' });
    pageComponent.save();
    pageComponent.save();
    const post = http.expectOne((r) => r.url === ITEM_CATEGORY_API && r.method === 'POST');
    expect(post.request.body).toEqual({ code: 'STEEL', name: 'Steel', parentId: 2 });
    post.flush({ ...metal, id: 4, code: 'STEEL', parentId: 2 });
    expect(pageComponent.dialogOpen()).toBe(false);
    http.expectOne(isList).flush([pack, metal, raw, { ...metal, id: 4, code: 'STEEL', parentId: 2 }]);
    expect(pageComponent.rows().find((x) => x.code === 'STEEL')?.level).toBe(2);
  });

  it('uses the shared list shell and reports a distinct no-results state without reloading', () => {
    const { pageComponent, fixture, element, http } = create();
    expect(element.querySelector('erp-list-page [data-testid="md-item-category-page"]')).not.toBeNull();
    expect(element.querySelector('label[for="md-item-category-search"]')).not.toBeNull();
    pageComponent.searchControl.setValue('nothing-matches');
    (element.querySelector('[data-testid="md-item-category-search-button"]') as HTMLButtonElement).click();
    fixture.detectChanges();
    expect(pageComponent.visibleRows()).toEqual([]);
    expect(element.querySelector('[data-testid="md-item-category-table"]')?.textContent).toContain('No matching');
    http.expectNone(isList);
  });

  it('moves a category, keeps the dialog open on tree errors and excludes its own subtree', () => {
    const { pageComponent, http } = create();
    const rawRow = pageComponent.rows().find((x) => x.code === 'RAW')!;
    pageComponent.openEdit(rawRow);
    expect(pageComponent.form.controls.code.disabled).toBe(true);
    expect(pageComponent.parentChoices().map((x) => x.code)).toEqual([]);
    pageComponent.form.patchValue({ parentId: 3, isActive: false });
    pageComponent.save();
    const put = http.expectOne(`${ITEM_CATEGORY_API}/1`);
    expect(put.request.body).toEqual({ name: 'Raw material', isActive: false, parentId: 3, version: 2 });
    put.flush({ errorCode: 'MD.ITEM_CATEGORY.INVALID_PARENT' }, { status: 422, statusText: 'Unprocessable Entity' });
    expect(pageComponent.dialogOpen()).toBe(true);
    expect(pageComponent.saveError()).toBe('errors.MD.ITEM_CATEGORY.INVALID_PARENT');
  });

  it('confirms deletion with the version and shows HAS_CHILDREN', () => {
    const { pageComponent, http } = create();
    pageComponent.confirmDelete(pageComponent.rows().find((x) => x.code === 'RAW')!);
    expect(confirm).toHaveBeenCalledWith(expect.objectContaining({ titleKey: 'md.itemCategory.deleteConfirm', params: { code: 'RAW' } }));
    http.expectOne(`${ITEM_CATEGORY_API}/1?version=2`)
      .flush({ errorCode: 'MD.ITEM_CATEGORY.HAS_CHILDREN' }, { status: 409, statusText: 'Conflict' });
    expect(pageComponent.error()).toBe('errors.MD.ITEM_CATEGORY.HAS_CHILDREN');
    expect(pageComponent.busy()).toBe(false);

    pageComponent.confirmDelete(pageComponent.rows().find((x) => x.code === 'METAL')!);
    http.expectOne(`${ITEM_CATEGORY_API}/2?version=1`).flush(null);
    http.expectOne(isList).flush([pack, raw]);
    expect(pageComponent.rows().map((x) => x.code)).toEqual(['PACK', 'RAW']);
  });

  it('shows the load error and an empty tree', () => {
    const fixture = TestBed.createComponent(ItemCategoryPage);
    TestBed.tick();
    TestBed.inject(HttpTestingController).expectOne(isList)
      .flush({ errorCode: 'COMMON.PERMISSION.DENIED' }, { status: 403, statusText: 'Forbidden' });
    fixture.detectChanges();
    expect(fixture.componentInstance.loadError()).toBe('errors.COMMON.PERMISSION.DENIED');
    expect(fixture.componentInstance.rows()).toEqual([]);
  });

  it('shows a 500 trace ID, hides the stale tree, and retries the list', () => {
    const { fixture, http, element } = create();
    fixture.componentInstance.reload();
    http.expectOne(isList).flush({ traceId: 'category-trace' }, { status: 500, statusText: 'Server Error' });
    fixture.detectChanges();
    expect(element.querySelector('[data-testid="md-item-category-table"]')).toBeNull();
    expect(element.querySelector('[data-testid="md-item-category-trace-id"]')?.textContent).toContain('category-trace');
    (element.querySelector('[data-testid="md-item-category-retry"]') as HTMLButtonElement).click();
    http.expectOne(isList).flush([metal, raw]);
    fixture.detectChanges();
    expect(element.querySelector('[data-testid="md-item-category-row-METAL"]')).not.toBeNull();
  });

  it('keeps the category dialog and shows the trace ID on a save 500', () => {
    const { pageComponent, http, fixture } = create();
    pageComponent.openCreate(null);
    pageComponent.form.patchValue({ code: 'NEW', name: 'New category' });
    pageComponent.save();
    http.expectOne((r) => r.url === ITEM_CATEGORY_API && r.method === 'POST')
      .flush({ traceId: 'category-save-trace' }, { status: 500, statusText: 'Server Error' });
    fixture.detectChanges();
    expect(pageComponent.dialogOpen()).toBe(true);
    expect(pageComponent.saveTraceId()).toBe('category-save-trace');
  });
});
