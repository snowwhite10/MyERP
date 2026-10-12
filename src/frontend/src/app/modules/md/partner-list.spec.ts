import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ANIMATION_MODULE_TYPE, Provider } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideNzIcons } from 'ng-zorro-antd/icon';
import { provideTestTranslations } from '../../../testing/i18n-fixtures';
import { APP_ICONS } from '../../core/icons';
import { PermissionService } from '../../core/auth/permission.service';
import { PARTNER_PAGE } from './business-partner.service';
import { CUSTOMER_CATEGORY_API } from './customer-category.service';
import { CUSTOMER_API, CUSTOMER_PAGE } from './customer.service';
import { PartnerList } from './partner-list';
import { SUPPLIER_CATEGORY_API } from './supplier-category.service';
import { SUPPLIER_API, SUPPLIER_PAGE } from './supplier.service';

const row = { id: 7, code: 'ACME', name: 'Acme', categoryId: 3, isActive: true, version: 2 };
const page = (items: unknown[]) => ({ items, page: 1, pageSize: 20, totalCount: items.length });

function configure(...providers: Provider[]) {
  TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting(),
    ...provideTestTranslations('en'), provideNzIcons(APP_ICONS),
    { provide: ANIMATION_MODULE_TYPE, useValue: 'NoopAnimations' }, ...providers] });
}

describe('MD-CUS-002 customer page', () => {
  const isList = (r: { url: string; method: string }) => r.url === CUSTOMER_API && r.method === 'GET';
  beforeEach(() => configure({ provide: PARTNER_PAGE, useValue: CUSTOMER_PAGE }));
  afterEach(() => TestBed.inject(HttpTestingController).verify());

  function create() {
    const fixture = TestBed.createComponent(PartnerList);
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

  it('MD-CUS-REV-003 submits and deletes with the row version, shows errors and blocks double actions', () => {
    const { list, http } = create();
    expect(list.reviewColor('Pending')).toBe('processing');
    expect(list.reviewColor('Rejected')).toBe('error');
    expect(list.reviewColor(undefined)).toBe('success');

    const pending = { ...row, reviewStatus: 'Pending' as const };
    list.submitPartner(pending);
    list.submitPartner(pending);
    const submit = http.expectOne(`${CUSTOMER_API}/7/submit`);
    expect(submit.request.body).toEqual({ version: 2 });
    submit.flush({ errorCode: 'SYS.APPROVAL.ALREADY_PENDING' }, { status: 409, statusText: 'Conflict' });
    expect(list.actingId()).toBeNull();
    expect(list.error()).toBe('errors.SYS.APPROVAL.ALREADY_PENDING');

    list.submitPartner(pending);
    http.expectOne(`${CUSTOMER_API}/7/submit`).flush({ status: 'Approved' });
    http.expectOne(isList).flush(page([{ ...row, version: 3, reviewStatus: 'Approved' }]));

    list.deletePartner({ ...row, version: 3 });
    const remove = http.expectOne((r) => r.url === `${CUSTOMER_API}/7` && r.method === 'DELETE');
    expect(remove.request.params.get('version')).toBe('3');
    remove.flush({ errorCode: 'MD.CUSTOMER.IN_USE' }, { status: 409, statusText: 'Conflict' });
    expect(list.error()).toBe('errors.MD.CUSTOMER.IN_USE');

    list.deletePartner({ ...row, version: 3 });
    http.expectOne((r) => r.url === `${CUSTOMER_API}/7` && r.method === 'DELETE').flush(null);
    expect(list.error()).toBeNull();
    http.expectOne(isList).flush(page([]));
  });

  it('opens the contact and address dialog for a customer row', () => {
    const fixture = TestBed.createComponent(PartnerList);
    TestBed.tick();
    const http = TestBed.inject(HttpTestingController);
    http.expectOne(isList).flush(page([row]));
    fixture.detectChanges();
    const button = (fixture.nativeElement as HTMLElement).querySelector<HTMLButtonElement>('[data-testid="md-customer-contacts-ACME"]');
    button!.click();
    TestBed.tick();
    expect(fixture.componentInstance.contactPartner()).toEqual(row);
    http.expectOne(`${CUSTOMER_API}/7/contacts`).flush([]);
    http.expectOne(`${CUSTOMER_API}/7/addresses`).flush([]);
  });

  it('shows the list error and resets paging on search', () => {
    const fixture = TestBed.createComponent(PartnerList);
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

describe('MD-SUP-002 supplier page', () => {
  const isList = (r: { url: string; method: string }) => r.url === SUPPLIER_API && r.method === 'GET';
  const supplier = { ...row, code: 'STEEL', name: 'Steel Co' };
  beforeEach(() => configure({ provide: PARTNER_PAGE, useValue: SUPPLIER_PAGE },
    { provide: PermissionService, useValue: { has: (code: string) => code === 'MD.SUPPLIER_CATEGORY.VIEW',
      hasAny: (codes: readonly string[]) => codes.includes('MD.SUPPLIER_CATEGORY.VIEW') } }));
  afterEach(() => TestBed.inject(HttpTestingController).verify());

  it('loads supplier categories, renders supplier test ids and maps supplier error codes', () => {
    const fixture = TestBed.createComponent(PartnerList);
    TestBed.tick();
    const http = TestBed.inject(HttpTestingController);
    http.expectOne(isList).flush(page([supplier]));
    const categories = http.expectOne((r) => r.url === SUPPLIER_CATEGORY_API);
    expect(categories.request.params.get('sort')).toBe('name');
    categories.flush(page([{ id: 3, code: 'RAW', name: 'Raw material', isActive: true, version: 1 }]));
    fixture.detectChanges();
    const list = fixture.componentInstance;
    const element = fixture.nativeElement as HTMLElement;
    expect(element.querySelector('[data-testid="md-supplier-title"]')?.textContent).toContain('Suppliers');
    expect(element.querySelector('[data-testid="md-supplier-row-STEEL"]')?.textContent).toContain('Raw material');
    expect(list.perm('CREATE')).toBe('MD.SUPPLIER.CREATE');
    // 供應商聯絡人對話框只讀聯絡人，沒有地址（ADR-043）。
    element.querySelector<HTMLButtonElement>('[data-testid="md-supplier-contacts-STEEL"]')!.click();
    TestBed.tick();
    http.expectOne(`${SUPPLIER_API}/7/contacts`).flush([]);
    http.expectNone(`${SUPPLIER_API}/7/addresses`);
    list.contactPartner.set(null);

    list.openCreate();
    list.form.patchValue({ code: 'steel', name: 'Steel', categoryId: 3 });
    list.save();
    const post = http.expectOne((r) => r.url === SUPPLIER_API && r.method === 'POST');
    expect(post.request.body).toEqual({ code: 'STEEL', name: 'Steel', categoryId: 3 });
    post.flush({ errorCode: 'MD.SUPPLIER.DUPLICATE_CODE' }, { status: 409, statusText: 'Conflict' });
    expect(list.dialogOpen()).toBe(true);
    expect(list.saveError()).toBe('errors.MD.SUPPLIER.DUPLICATE_CODE');
  });

  it('MD-SUP-REV-003 shows review status and submits or deletes suppliers with supplier error codes', () => {
    const fixture = TestBed.createComponent(PartnerList);
    TestBed.tick();
    const http = TestBed.inject(HttpTestingController);
    http.expectOne(isList).flush(page([{ ...supplier, reviewStatus: 'Rejected' }]));
    http.expectOne((r) => r.url === SUPPLIER_CATEGORY_API).flush(page([]));
    fixture.detectChanges();
    const list = fixture.componentInstance;
    expect((fixture.nativeElement as HTMLElement).querySelector('[data-testid="md-supplier-row-STEEL"]')?.textContent)
      .toContain('Rejected');

    list.submitPartner({ ...supplier, reviewStatus: 'Rejected' });
    const submit = http.expectOne(`${SUPPLIER_API}/7/submit`);
    expect(submit.request.body).toEqual({ version: 2 });
    submit.flush({ errorCode: 'MD.SUPPLIER.ALREADY_APPROVED' }, { status: 422, statusText: 'Unprocessable' });
    expect(list.error()).toBe('errors.MD.SUPPLIER.ALREADY_APPROVED');

    list.deletePartner(supplier);
    list.deletePartner(supplier);
    const remove = http.expectOne((r) => r.url === `${SUPPLIER_API}/7` && r.method === 'DELETE');
    expect(remove.request.params.get('version')).toBe('2');
    remove.flush({ errorCode: 'MD.SUPPLIER.APPROVAL_PENDING' }, { status: 422, statusText: 'Unprocessable' });
    expect(list.actingId()).toBeNull();
    expect(list.error()).toBe('errors.MD.SUPPLIER.APPROVAL_PENDING');
  });
});
