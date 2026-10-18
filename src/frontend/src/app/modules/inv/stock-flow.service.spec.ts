import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { StockFlowService } from './stock-flow.service';

describe('StockFlowService', () => {
  let service: StockFlowService;
  let http: HttpTestingController;
  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
    service = TestBed.inject(StockFlowService);
    http = TestBed.inject(HttpTestingController);
  });
  afterEach(() => http.verify());
  it('queries one page but sends no page limit to the server export', () => {
    const filters = { from: '2024-02-29', to: '2024-03-01', itemId: 7, page: 2, pageSize: 1 };
    service.query(filters).subscribe();
    const page = http.expectOne(req => req.url.endsWith('/stock-flow'));
    expect(page.request.params.get('page')).toBe('2');
    page.flush({ queriedAt: '', totalCount: 0, items: [], page: 2, pageSize: 1 });
    service.export({ from: filters.from, to: filters.to, itemId: filters.itemId }).subscribe();
    const csv = http.expectOne(req => req.url.endsWith('/stock-flow/export'));
    expect(csv.request.params.has('page')).toBe(false);
    expect(csv.request.responseType).toBe('blob');
    csv.flush(new Blob(['csv']), { headers: { 'X-Export-Row-Count': '0' } });
  });
});
