import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ANIMATION_MODULE_TYPE } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideNzIcons } from 'ng-zorro-antd/icon';
import { provideTestTranslations } from '../../../testing/i18n-fixtures';
import { APP_ICONS } from '../../core/icons';
import { CUSTOMER_CATEGORY_API } from './customer-category.service';
import { CustomerList } from './customer-list';
import { CUSTOMER_API } from './customer.service';

const row = { id: 7, code: 'ACME', name: 'Acme', categoryId: 3, isActive: true, version: 2 };
const page = (items: unknown[]) => ({ items, page: 1, pageSize: 20, totalCount: items.length });
const isList = (r: { url: string; method: string }) => r.url === CUSTOMER_API && r.method === 'GET';

describe('MD-CUS-002 customer page', () => {
  beforeEach(() => TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting(),
    ...provideTestTranslations('en'), provideNzIcons(APP_ICONS),
    { provide: ANIMATION_MODULE_TYPE, useValue: 'NoopAnimations' }] }));
  afterEach(() => TestBed.inject(HttpTestingController).verify());

  function create() {
    const fixture = TestBed.createComponent(CustomerList);
    TestBed.tick();
    const http = TestBed.inject(HttpTestingController);
    http.expectOne(isList).flush(page([row]));
    return { list: fixture.componentInstance, http };
  }

  it('skips categories without permission and posts a normalized customer only when the form is valid', () => {
    const { list, http } = create();
    http.expectNone((r) => r.url === CUSTOMER_CATEGORY_API);
    expect(list.rows()).toEqual([row]);
    expect(list.categoryName(3)).toBe('');

    list.openCreate();
    list.form.patchValue({ code: 'bad code', name: 'Acme' });
    list.save();
    list.form.patchValue({ code: 'new-1', name: '   ' });
    list.save();
    http.expectNone((r) => r.method === 'POST');

    list.form.patchValue({ name: ' New customer ' });
    list.save();
    const post = http.expectOne((r) => r.url === CUSTOMER_API && r.method === 'POST');
    expect(post.request.body).toEqual({ code: 'NEW-1', name: 'New customer', categoryId: null });
    post.flush({ ...row, id: 8, code: 'NEW-1' });
    expect(list.dialogOpen()).toBe(false);
    http.expectOne(isList).flush(page([row]));
  });

  it('updates with the row version and keeps the dialog open on conflict', () => {
    const { list, http } = create();
    list.categories.set([{ id: 3, code: 'RETAIL', name: 'Retail', isActive: false, version: 1 }]);
    expect(list.categoryName(3)).toBe('Retail');

    list.openEdit(row);
    expect(list.form.controls.code.disabled).toBe(true);
    list.form.patchValue({ isActive: false });
    list.save();
    list.save();
    const put = http.expectOne(`${CUSTOMER_API}/7`);
    expect(put.request.body).toEqual({ name: 'Acme', categoryId: 3, isActive: false, version: 2 });
    put.flush({ errorCode: 'COMMON.CONCURRENCY.CONFLICT' }, { status: 409, statusText: 'Conflict' });
    expect(list.saving()).toBe(false);
    expect(list.dialogOpen()).toBe(true);
    expect(list.saveError()).toBe('errors.COMMON.CONCURRENCY.CONFLICT');
  });

  it('shows the list error and resets paging on search', () => {
    const fixture = TestBed.createComponent(CustomerList);
    TestBed.tick();
    const http = TestBed.inject(HttpTestingController);
    http.expectOne(isList).flush({ errorCode: 'COMMON.PERMISSION.DENIED' }, { status: 403, statusText: 'Forbidden' });
    const list = fixture.componentInstance;
    expect(list.error()).toBe('errors.COMMON.PERMISSION.DENIED');
    expect(list.rows()).toEqual([]);

    list.page.set(3);
    list.searchControl.setValue(' acme ');
    list.search();
    const query = http.expectOne(isList);
    expect(query.request.params.get('page')).toBe('1');
    expect(query.request.params.get('keyword')).toBe('acme');
    query.flush(page([]));
    expect(list.error()).toBeNull();
  });
});
