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
import { PartnerContactDialog } from './partner-contact-dialog';
import { CustomerContactService, SupplierContactService } from './customer-contact.service';

const customer = { id: 5, code: 'ACME', name: 'Acme', categoryId: null, isActive: true, version: 1 };
const amy = { id: 11, customerId: 5, name: 'Amy', position: 'Buyer', mobile: '0912', phone: null, email: 'amy@acme.test',
  messagingId: null, note: null, isDefault: true, isActive: true, version: 3 };
const dock = { id: 21, customerId: 5, address: '1 Harbour Rd', postalCode: '100', recipient: null, phone: null,
  isDefault: false, isActive: true, version: 2 };
const CONTACTS = '/api/v1/md/customers/5/contacts';
const ADDRESSES = '/api/v1/md/customers/5/addresses';

describe('MD-CONT-UI customer contact dialog', () => {
  const confirm = vi.fn(() => of(true));
  let granted: Set<string>;
  beforeEach(() => {
    confirm.mockClear();
    granted = new Set(['MD.CUSTOMER.VIEW', 'MD.CUSTOMER.EDIT']);
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting(),
      ...provideTestTranslations('en'), provideNzIcons(APP_ICONS),
      { provide: ANIMATION_MODULE_TYPE, useValue: 'NoopAnimations' },
      { provide: ErpDialogService, useValue: { confirm } },
      { provide: PermissionService, useValue: { has: (code: string) => granted.has(code),
        hasAny: (codes: string[]) => codes.some((code) => granted.has(code)) } }] });
  });
  afterEach(() => TestBed.inject(HttpTestingController).verify());

  function open() {
    const fixture = TestBed.createComponent(PartnerContactDialog);
    fixture.componentRef.setInput('partner', customer);
    TestBed.tick();
    const http = TestBed.inject(HttpTestingController);
    http.expectOne(CONTACTS).flush([amy]);
    http.expectOne(ADDRESSES).flush([dock]);
    fixture.detectChanges();
    return { dialog: fixture.componentInstance, http, fixture };
  }

  it('lists both children, adds a contact with trimmed optional fields and reloads after save', () => {
    const { dialog, http } = open();
    expect(document.body.querySelector('[data-testid="md-contact-row-11"]')?.textContent).toContain('amy@acme.test');
    expect(document.body.querySelector('[data-testid="md-contact-default-11"]')).not.toBeNull();
    expect(document.body.querySelector('[data-testid="md-address-row-21"]')?.textContent).toContain('1 Harbour Rd');

    dialog.contactForm.patchValue({ name: '   ' }); dialog.saveContact();
    dialog.contactForm.patchValue({ name: 'Bob', email: 'not an email' }); dialog.saveContact();
    http.expectNone((r) => r.method === 'POST');

    dialog.contactForm.patchValue({ name: ' Bob ', email: '', mobile: ' 0933 ', isDefault: true }); dialog.saveContact();
    dialog.saveContact();
    const post = http.expectOne((r) => r.url === CONTACTS && r.method === 'POST');
    expect(post.request.body).toEqual({ name: 'Bob', position: null, mobile: '0933', phone: null, email: null,
      messagingId: null, note: null, isDefault: true });
    post.flush({ ...amy, id: 12, name: 'Bob' });
    const reload = http.expectOne(CONTACTS);
    reload.flush([{ ...amy, id: 12, name: 'Bob' }, { ...amy, isDefault: false }]);
    expect(dialog.contacts.rows().map((row) => row.isDefault)).toEqual([true, false]);
    expect(dialog.contactForm.getRawValue().name).toBe('');
  });

  it('MD-F07 ignores a save that finishes after switching to another customer', () => {
    const { dialog, http, fixture } = open();
    dialog.contacts.startEdit(amy);
    dialog.contactForm.patchValue({ name: 'Amy Lin' }); dialog.saveContact();
    const put = http.expectOne(`${CONTACTS}/11`);
    expect(dialog.contacts.saving()).toBe(true);

    const other = { ...customer, id: 6, code: 'BETA' };
    fixture.componentRef.setInput('partner', other);
    TestBed.tick();
    expect(dialog.contacts.saving()).toBe(false);
    http.expectOne('/api/v1/md/customers/6/contacts').flush([]);
    http.expectOne('/api/v1/md/customers/6/addresses').flush([]);

    put.flush({ ...amy, name: 'Amy Lin', version: 4 });
    http.expectNone(CONTACTS);
    expect(dialog.contacts.rows()).toEqual([]);

    // 409 回應同樣不可觸發舊客戶的重新查詢或顯示錯誤
    dialog.contacts.startEdit({ ...amy, id: 31 });
    dialog.saveContact();
    const conflict = http.expectOne('/api/v1/md/customers/6/contacts/31');
    fixture.componentRef.setInput('partner', customer);
    TestBed.tick();
    http.expectOne(CONTACTS).flush([amy]);
    http.expectOne(ADDRESSES).flush([dock]);
    conflict.flush({ errorCode: 'COMMON.CONCURRENCY.CONFLICT' }, { status: 409, statusText: 'Conflict' });
    http.expectNone('/api/v1/md/customers/6/contacts');
    expect(dialog.contacts.error()).toBeNull();
    expect(dialog.contacts.rows()).toEqual([amy]);
  });

  it('edits an address with its version, blocks an inactive default and shows conflicts', () => {
    const { dialog, http } = open();
    dialog.addresses.startEdit(dock);
    expect(dialog.addressForm.getRawValue()).toEqual({ address: '1 Harbour Rd', postalCode: '100', recipient: '', phone: '',
      isDefault: false, isActive: true });
    dialog.addressForm.patchValue({ isDefault: true, isActive: false }); dialog.saveAddress();
    expect(dialog.addressForm.hasError('defaultNeedsActive')).toBe(true);
    http.expectNone((r) => r.method === 'PUT');

    dialog.addressForm.patchValue({ isDefault: true, isActive: true, recipient: ' Ann ' }); dialog.saveAddress();
    const put = http.expectOne(`${ADDRESSES}/21`);
    expect(put.request.body).toEqual({ address: '1 Harbour Rd', postalCode: '100', recipient: 'Ann', phone: null,
      isDefault: true, isActive: true, version: 2 });
    put.flush({ errorCode: 'COMMON.CONCURRENCY.CONFLICT' }, { status: 409, statusText: 'Conflict' });
    expect(dialog.addresses.error()).toBe('errors.COMMON.CONCURRENCY.CONFLICT');
    expect(dialog.addresses.editing()).toEqual(dock);
    http.expectOne(ADDRESSES).flush([{ ...dock, version: 3 }]);
    expect(dialog.addresses.rows()[0]!.version).toBe(3);
    expect(dialog.addresses.error()).toBe('errors.COMMON.CONCURRENCY.CONFLICT');

    dialog.addresses.resetForm();
    dialog.addressForm.patchValue({ address: ' ' }); dialog.saveAddress();
    http.expectNone((r) => r.method === 'POST');
    dialog.addressForm.patchValue({ address: '9 Pier St' }); dialog.saveAddress();
    const post = http.expectOne((r) => r.url === ADDRESSES && r.method === 'POST');
    expect(post.request.body).toEqual({ address: '9 Pier St', postalCode: null, recipient: null, phone: null, isDefault: false });
    post.flush({ errorCode: 'MD.CUSTOMER.NOT_FOUND' }, { status: 404, statusText: 'Not Found' });
    expect(dialog.addresses.error()).toBe('errors.MD.CUSTOMER.NOT_FOUND');
    http.expectOne(ADDRESSES).flush({ errorCode: 'MD.CUSTOMER.NOT_FOUND' }, { status: 404, statusText: 'Not Found' });
    expect(dialog.addresses.rows()).toEqual([]);

    dialog.addressForm.patchValue({ address: 'Kept' });
    dialog.saveAddress();
    http.expectOne((r) => r.url === ADDRESSES && r.method === 'POST').flush(null, { status: 500, statusText: 'Server Error' });
    expect(dialog.addresses.error()).toBe('errors.http.unknown');
    expect(dialog.addressForm.getRawValue().address).toBe('Kept');
  });

  it('deletes after confirmation with the version and resets an edit of the deleted row', () => {
    const { dialog, http } = open();
    dialog.contacts.startEdit(amy);
    dialog.confirmDeleteContact(amy);
    expect(confirm).toHaveBeenCalledWith(expect.objectContaining({ titleKey: 'md.customerContact.deleteContactConfirm',
      params: { name: 'Amy' }, danger: true }));
    const del = http.expectOne((r) => r.method === 'DELETE' && r.url === `${CONTACTS}/11`);
    expect(del.request.params.get('version')).toBe('3');
    del.flush(null);
    http.expectOne(CONTACTS).flush([]);
    expect(dialog.contacts.editing()).toBeNull();
    expect(dialog.contacts.rows()).toEqual([]);

    dialog.confirmDeleteAddress(dock);
    const gone = http.expectOne((r) => r.method === 'DELETE' && r.url === `${ADDRESSES}/21`);
    gone.flush({ errorCode: 'MD.CUSTOMER_ADDRESS.NOT_FOUND' }, { status: 404, statusText: 'Not Found' });
    expect(dialog.addresses.error()).toBe('errors.MD.CUSTOMER_ADDRESS.NOT_FOUND');
    http.expectOne(ADDRESSES).flush([]);
    expect(dialog.addresses.rows()).toEqual([]);

    confirm.mockReturnValueOnce(of(false));
    dialog.confirmDeleteAddress(dock);
    http.expectNone((r) => r.method === 'DELETE');
  });

  it('read-only users see children without forms or row actions; closing clears the lists', () => {
    granted.delete('MD.CUSTOMER.EDIT');
    const { dialog, fixture, http } = open();
    expect(document.body.querySelector('[data-testid="md-contact-row-11"]')).not.toBeNull();
    for (const id of ['md-contact-form', 'md-address-form', 'md-contact-edit-11', 'md-address-delete-21']) {
      expect(document.body.querySelector(`[data-testid="${id}"]`)).toBeNull();
    }
    fixture.componentRef.setInput('partner', null); TestBed.tick();
    expect(dialog.contacts.rows()).toEqual([]);
    expect(dialog.addresses.rows()).toEqual([]);
    http.expectNone(CONTACTS);
  });

  it('shows list errors per section', () => {
    const fixture = TestBed.createComponent(PartnerContactDialog);
    fixture.componentRef.setInput('partner', customer);
    TestBed.tick();
    const http = TestBed.inject(HttpTestingController);
    http.expectOne(CONTACTS).flush({ errorCode: 'MD.CUSTOMER.NOT_FOUND' }, { status: 404, statusText: 'Not Found' });
    http.expectOne(ADDRESSES).flush(null, { status: 0, statusText: 'Unknown' });
    const dialog = fixture.componentInstance;
    expect(dialog.contacts.error()).toBe('errors.MD.CUSTOMER.NOT_FOUND');
    expect(dialog.addresses.error()).toBe('errors.http.network');
    expect(dialog.contacts.loading()).toBe(false);
  });
});

describe('MD-SUP-CONT-UI supplier contact dialog', () => {
  const steel = { id: 8, code: 'STEEL', name: 'Steel Co', categoryId: null, isActive: true, version: 1 };
  const sam = { id: 31, supplierId: 8, name: 'Sam', position: 'Sales', mobile: null, phone: '02-1234', email: null,
    messagingId: null, note: null, isDefault: true, isActive: true, version: 2 };
  const SUPPLIER_CONTACTS = '/api/v1/md/suppliers/8/contacts';
  const confirm = vi.fn(() => of(true));
  let granted: Set<string>;
  beforeEach(() => {
    granted = new Set(['MD.SUPPLIER.VIEW', 'MD.SUPPLIER.EDIT']);
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting(),
      ...provideTestTranslations('en'), provideNzIcons(APP_ICONS),
      { provide: ANIMATION_MODULE_TYPE, useValue: 'NoopAnimations' },
      { provide: ErpDialogService, useValue: { confirm } },
      { provide: PermissionService, useValue: { has: (code: string) => granted.has(code),
        hasAny: (codes: string[]) => codes.some((code) => granted.has(code)) } }] });
  });
  afterEach(() => TestBed.inject(HttpTestingController).verify());

  function open() {
    const fixture = TestBed.createComponent(PartnerContactDialog);
    fixture.componentRef.setInput('kind', 'supplier');
    fixture.componentRef.setInput('partner', steel);
    TestBed.tick();
    const http = TestBed.inject(HttpTestingController);
    http.expectOne(SUPPLIER_CONTACTS).flush([sam]);
    http.expectNone((r) => r.url.endsWith('/addresses'));
    fixture.detectChanges();
    return { dialog: fixture.componentInstance, http };
  }

  it('shows only contacts, saves and deletes through the supplier API with supplier permissions', () => {
    const { dialog, http } = open();
    expect(dialog.editPermission()).toBe('MD.SUPPLIER.EDIT');
    expect(document.body.querySelector('[data-testid="md-supplier-contact-dialog"]')).not.toBeNull();
    expect(document.body.querySelector('[data-testid="md-contact-row-31"]')?.textContent).toContain('Sam');
    expect(document.body.querySelector('[data-testid="md-contact-form"]')).not.toBeNull();
    expect(document.body.querySelector('[data-testid="md-address-table"]')).toBeNull();
    expect(document.body.querySelector('[data-testid="md-address-form"]')).toBeNull();

    dialog.contacts.startEdit(sam);
    dialog.contactForm.patchValue({ email: 'sam@steel.test', note: ' Quotes ' }); dialog.saveContact();
    const put = http.expectOne(`${SUPPLIER_CONTACTS}/31`);
    expect(put.request.body).toEqual({ name: 'Sam', position: 'Sales', mobile: null, phone: '02-1234', email: 'sam@steel.test',
      messagingId: null, note: 'Quotes', isDefault: true, isActive: true, version: 2 });
    put.flush({ errorCode: 'MD.SUPPLIER_CONTACT.NOT_FOUND' }, { status: 404, statusText: 'Not Found' });
    expect(dialog.contacts.error()).toBe('errors.MD.SUPPLIER_CONTACT.NOT_FOUND');
    expect(dialog.contacts.editing()).toBeNull();
    http.expectOne(SUPPLIER_CONTACTS).flush([{ ...sam, version: 3 }]);

    dialog.confirmDeleteContact({ ...sam, version: 3 });
    const del = http.expectOne((r) => r.method === 'DELETE' && r.url === `${SUPPLIER_CONTACTS}/31`);
    expect(del.request.params.get('version')).toBe('3');
    del.flush(null);
    http.expectOne(SUPPLIER_CONTACTS).flush([]);
    expect(dialog.contacts.rows()).toEqual([]);
  });

  it('hides the form from users without supplier edit permission', () => {
    granted.delete('MD.SUPPLIER.EDIT');
    granted.add('MD.CUSTOMER.EDIT');
    open();
    expect(document.body.querySelector('[data-testid="md-contact-row-31"]')).not.toBeNull();
    expect(document.body.querySelector('[data-testid="md-contact-form"]')).toBeNull();
    expect(document.body.querySelector('[data-testid="md-contact-edit-31"]')).toBeNull();
  });
});

describe('SupplierContactService', () => {
  beforeEach(() => TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] }));
  afterEach(() => TestBed.inject(HttpTestingController).verify());

  it('uses the supplier contact routes and maps supplier error codes', () => {
    const api = TestBed.inject(SupplierContactService);
    const http = TestBed.inject(HttpTestingController);
    api.createContact(8, { name: ' Sam ', position: '', mobile: null, phone: null, email: null, messagingId: null,
      note: null, isDefault: true }).subscribe();
    expect(http.expectOne('/api/v1/md/suppliers/8/contacts').request.body).toEqual({ name: 'Sam', position: null, mobile: null,
      phone: null, email: null, messagingId: null, note: null, isDefault: true });
    let key = '';
    api.listContacts(8).subscribe({ error: (error: unknown) => (key = api.errorKey(error)) });
    http.expectOne('/api/v1/md/suppliers/8/contacts').flush({ errorCode: 'MD.SUPPLIER.NOT_FOUND' }, { status: 404, statusText: 'Not Found' });
    expect(key).toBe('errors.MD.SUPPLIER.NOT_FOUND');
  });
});

describe('CustomerContactService', () => {
  beforeEach(() => TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] }));
  afterEach(() => TestBed.inject(HttpTestingController).verify());

  it('sends revisions with versions and maps child error codes', () => {
    const api = TestBed.inject(CustomerContactService);
    const http = TestBed.inject(HttpTestingController);
    api.updateContact(5, 11, { name: ' Amy ', position: '', mobile: null, phone: null, email: null, messagingId: null,
      note: ' VIP ', isDefault: false, isActive: false, version: 3 }).subscribe();
    expect(http.expectOne(`${CONTACTS}/11`).request.body).toEqual({ name: 'Amy', position: null, mobile: null, phone: null,
      email: null, messagingId: null, note: 'VIP', isDefault: false, isActive: false, version: 3 });
    api.removeAddress(5, 21, 2).subscribe();
    expect(http.expectOne((r) => r.url === `${ADDRESSES}/21`).request.params.get('version')).toBe('2');
    api.listAddresses(5).subscribe();
    http.expectOne(ADDRESSES).flush([]);
    let key = '';
    api.listContacts(5).subscribe({ error: (error: unknown) => (key = api.errorKey(error)) });
    http.expectOne(CONTACTS).flush({ errorCode: 'MD.CUSTOMER_CONTACT.NOT_FOUND' }, { status: 404, statusText: 'Not Found' });
    expect(key).toBe('errors.MD.CUSTOMER_CONTACT.NOT_FOUND');
  });
});
