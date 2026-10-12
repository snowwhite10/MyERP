import { provideHttpClient } from '@angular/common/http';
import { HttpErrorResponse } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { firstValueFrom } from 'rxjs';
import { CustomerService } from './customer.service';
import { SupplierService } from './supplier.service';

describe('MD customer API', () => {
  beforeEach(() => TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] }));

  it('sends company-scoped paging, normalized create data and versioned updates', async () => {
    const api = TestBed.inject(CustomerService);
    const http = TestBed.inject(HttpTestingController);
    const list = firstValueFrom(api.list({ page: 2, pageSize: 10, keyword: ' Acme ', sort: '-name' }));
    const listed = http.expectOne((req) => req.url === '/api/v1/md/customers');
    expect(listed.request.params.get('keyword')).toBe('Acme');
    expect(listed.request.params.get('page')).toBe('2');
    expect(listed.request.params.get('sort')).toBe('-name');
    listed.flush({ items: [], page: 2, pageSize: 10, totalCount: 0 });
    await list;

    const created = firstValueFrom(api.create({ code: 'acme', name: ' Acme ', categoryId: 3 }));
    const post = http.expectOne('/api/v1/md/customers');
    expect(post.request.method).toBe('POST');
    expect(post.request.body).toEqual({ code: 'ACME', name: 'Acme', categoryId: 3 });
    post.flush({ id: 1, code: 'ACME', name: 'Acme', categoryId: 3, isActive: true, version: 1 });
    await created;

    const updated = firstValueFrom(api.update(1, { name: ' Other ', categoryId: null, isActive: false, version: 1 }));
    const put = http.expectOne('/api/v1/md/customers/1');
    expect(put.request.body).toEqual({ name: 'Other', categoryId: null, isActive: false, version: 1 });
    put.flush({ id: 1, code: 'ACME', name: 'Other', categoryId: null, isActive: false, version: 2 });
    await updated;
    http.verify();
  });

  it('submits approval and soft deletion with the current version', async () => {
    const api = TestBed.inject(CustomerService);
    const http = TestBed.inject(HttpTestingController);
    const submitted = firstValueFrom(api.submit(12, 3));
    const post = http.expectOne('/api/v1/md/customers/12/submit');
    expect(post.request.method).toBe('POST');
    expect(post.request.body).toEqual({ version: 3 });
    post.flush({ status: 'Pending' });
    expect((await submitted).status).toBe('Pending');

    const deleted = firstValueFrom(api.delete(12, 4));
    const request = http.expectOne((req) => req.url === '/api/v1/md/customers/12');
    expect(request.request.method).toBe('DELETE');
    expect(request.request.params.get('version')).toBe('4');
    request.flush(null);
    await deleted;
    http.verify();
  });
});

describe('MD-SUP-002 supplier API', () => {
  beforeEach(() => TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] }));

  it('uses the supplier route and recognizes only supplier error codes', async () => {
    const api = TestBed.inject(SupplierService);
    const http = TestBed.inject(HttpTestingController);
    const list = firstValueFrom(api.list({ page: 1, pageSize: 20, keyword: '  ' }));
    const listed = http.expectOne((req) => req.url === '/api/v1/md/suppliers');
    expect(listed.request.params.has('keyword')).toBe(false);
    expect(listed.request.params.has('sort')).toBe(false);
    listed.flush({ items: [], page: 1, pageSize: 20, totalCount: 0 });
    await list;

    const updated = firstValueFrom(api.update(5, { name: ' Steel ', categoryId: 2, isActive: true, version: 3 }));
    const put = http.expectOne('/api/v1/md/suppliers/5');
    expect(put.request.method).toBe('PUT');
    expect(put.request.body).toEqual({ name: 'Steel', categoryId: 2, isActive: true, version: 3 });
    put.flush({ id: 5, code: 'STEEL', name: 'Steel', categoryId: 2, isActive: true, version: 4 });
    await updated;
    http.verify();

    const error = (errorCode: string) => new HttpErrorResponse({ status: 409, error: { errorCode } });
    expect(api.errorKey(error('MD.SUPPLIER.DUPLICATE_CODE'))).toBe('errors.MD.SUPPLIER.DUPLICATE_CODE');
    expect(api.errorKey(error('MD.SUPPLIER.NOT_FOUND'))).toBe('errors.MD.SUPPLIER.NOT_FOUND');
    expect(api.errorKey(error('MD.CUSTOMER.DUPLICATE_CODE'))).not.toBe('errors.MD.CUSTOMER.DUPLICATE_CODE');
  });

  it('MD-SUP-REV-002 submits approval and soft deletion on the supplier route', async () => {
    const api = TestBed.inject(SupplierService);
    const http = TestBed.inject(HttpTestingController);
    const submitted = firstValueFrom(api.submit(5, 3));
    const post = http.expectOne('/api/v1/md/suppliers/5/submit');
    expect(post.request.method).toBe('POST');
    expect(post.request.body).toEqual({ version: 3 });
    post.flush({ status: 'Approved' });
    expect((await submitted).status).toBe('Approved');

    const deleted = firstValueFrom(api.delete(5, 4));
    const request = http.expectOne((req) => req.url === '/api/v1/md/suppliers/5');
    expect(request.request.method).toBe('DELETE');
    expect(request.request.params.get('version')).toBe('4');
    request.flush(null);
    await deleted;
    http.verify();

    const error = (errorCode: string) => new HttpErrorResponse({ status: 409, error: { errorCode } });
    for (const code of ['NOT_APPROVED', 'APPROVAL_PENDING', 'ALREADY_APPROVED', 'IN_USE', 'APPROVAL_STATE_CHANGED'])
      expect(api.errorKey(error(`MD.SUPPLIER.${code}`))).toBe(`errors.MD.SUPPLIER.${code}`);
    expect(api.errorKey(error('SYS.APPROVAL.ALREADY_PENDING'))).toBe('errors.SYS.APPROVAL.ALREADY_PENDING');
    expect(api.errorKey(error('MD.CUSTOMER.IN_USE'))).not.toBe('errors.MD.CUSTOMER.IN_USE');
  });
});
