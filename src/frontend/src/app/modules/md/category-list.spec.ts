import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ANIMATION_MODULE_TYPE } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideNzIcons } from 'ng-zorro-antd/icon';
import { of } from 'rxjs';
import { provideTestTranslations } from '../../../testing/i18n-fixtures';
import { APP_ICONS } from '../../core/icons';
import { ErpDialogService } from '../../shared/dialogs/dialog.service';
import { CategoryList } from './category-list';
import { CATEGORY_PAGE } from './master-category.service';
import { SUPPLIER_CATEGORY_API, SUPPLIER_CATEGORY_PAGE } from './supplier-category.service';

const row = { id: 4, code: 'RAW', name: 'Raw material', isActive: true, version: 3 };
const page = (items: unknown[]) => ({ items, page: 1, pageSize: 20, totalCount: items.length });
const isList = (r: { url: string; method: string }) => r.url === SUPPLIER_CATEGORY_API && r.method === 'GET';

describe('MD-SCAT-003 supplier category page', () => {
  const confirm = vi.fn(() => of(true));
  beforeEach(() => {
    confirm.mockClear();
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting(),
      ...provideTestTranslations('en'), provideNzIcons(APP_ICONS),
      { provide: ANIMATION_MODULE_TYPE, useValue: 'NoopAnimations' },
      { provide: ErpDialogService, useValue: { confirm } },
      { provide: CATEGORY_PAGE, useValue: SUPPLIER_CATEGORY_PAGE }] });
  });
  afterEach(() => TestBed.inject(HttpTestingController).verify());

  function create() {
    const fixture = TestBed.createComponent(CategoryList);
    TestBed.tick();
    const http = TestBed.inject(HttpTestingController);
    http.expectOne(isList).flush(page([row]));
    fixture.detectChanges();
    return { list: fixture.componentInstance, http, element: fixture.nativeElement as HTMLElement };
  }

  it('uses the supplier API and texts, and creates only valid categories', () => {
    const { list, http, element } = create();
    expect(element.querySelector('[data-testid="md-category-title"]')?.textContent).toContain('Supplier categories');
    expect(list.rows()).toEqual([row]);

    list.openCreate();
    list.form.patchValue({ code: 'bad code', name: 'Raw' });
    list.save();
    list.form.patchValue({ code: 'svc', name: '  ' });
    list.save();
    http.expectNone((r) => r.method === 'POST');

    list.form.patchValue({ name: ' Services ' });
    list.save();
    list.save();
    const post = http.expectOne((r) => r.url === SUPPLIER_CATEGORY_API && r.method === 'POST');
    expect(post.request.body).toEqual({ code: 'SVC', name: 'Services' });
    post.flush({ ...row, id: 5, code: 'SVC', name: 'Services' });
    expect(list.dialogOpen()).toBe(false);
    http.expectOne(isList).flush(page([row]));
  });

  it('keeps the dialog open with a supplier error and deletes with the row version', () => {
    const { list, http } = create();
    list.openEdit(row);
    expect(list.form.controls.code.disabled).toBe(true);
    list.form.patchValue({ isActive: false });
    list.save();
    const put = http.expectOne(`${SUPPLIER_CATEGORY_API}/4`);
    expect(put.request.body).toEqual({ name: 'Raw material', isActive: false, version: 3 });
    put.flush({ errorCode: 'MD.SUPPLIER_CATEGORY.NOT_FOUND' }, { status: 404, statusText: 'Not Found' });
    expect(list.dialogOpen()).toBe(true);
    expect(list.saveError()).toBe('errors.MD.SUPPLIER_CATEGORY.NOT_FOUND');

    list.closeDialog();
    list.confirmDelete(row);
    expect(confirm).toHaveBeenCalledWith(expect.objectContaining({ titleKey: 'md.supplierCategory.deleteConfirm' }));
    http.expectOne(`${SUPPLIER_CATEGORY_API}/4?version=3`)
      .flush({ errorCode: 'MD.SUPPLIER_CATEGORY.IN_USE' }, { status: 409, statusText: 'Conflict' });
    expect(list.error()).toBe('errors.MD.SUPPLIER_CATEGORY.IN_USE');
    expect(list.busyId()).toBeNull();
  });
});
