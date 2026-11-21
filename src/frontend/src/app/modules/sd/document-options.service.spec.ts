import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { SalesDocumentOptionsService } from './document-options.service';

describe('SD-DOCOPT-UI-001 quotation/order form options API (WU-SD-FORM-OPTIONS)', () => {
  beforeEach(() => TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] }));
  afterEach(() => TestBed.inject(HttpTestingController).verify());

  it('calls the SD option endpoints (never /md/) with trimmed keyword and paging', () => {
    const service = TestBed.inject(SalesDocumentOptionsService);
    const http = TestBed.inject(HttpTestingController);
    let customers: { items: { code: string }[] } | undefined;
    service.customers({ page: 2, pageSize: 10, keyword: '  acme ' }).subscribe(value => customers = value);
    const customer = http.expectOne(r => r.url === '/api/v1/sal/document-options/customers');
    expect(customer.request.params.keys().sort()).toEqual(['keyword', 'page', 'pageSize']);
    expect(customer.request.params.get('keyword')).toBe('acme');
    expect(customer.request.params.get('page')).toBe('2');
    customer.flush({ items: [{ id: 7, code: 'C-7', name: 'Acme' }], page: 2, pageSize: 10, totalCount: 11 });
    expect(customers!.items[0].code).toBe('C-7');

    service.items({ page: 1, pageSize: 20, keyword: '   ' }).subscribe();
    const item = http.expectOne(r => r.url === '/api/v1/sal/document-options/items');
    expect(item.request.params.keys().sort()).toEqual(['page', 'pageSize']);
    item.flush({ items: [], page: 1, pageSize: 20, totalCount: 0 });

    let rates: { items: { ratePercent: number }[] } | undefined;
    service.outputTaxRates('2028-02-29', { page: 1, pageSize: 20, keyword: 'out' }).subscribe(value => rates = value);
    const tax = http.expectOne(r => r.url === '/api/v1/sal/document-options/output-tax-rates');
    expect(tax.request.params.get('businessDate')).toBe('2028-02-29');
    expect(tax.request.params.get('keyword')).toBe('out');
    tax.flush({ items: [{ id: 4, code: 'OUT', name: 'Output VAT', ratePercent: 5 }], page: 1, pageSize: 20, totalCount: 1 });
    expect(rates!.items[0].ratePercent).toBe(5);
  });
});
