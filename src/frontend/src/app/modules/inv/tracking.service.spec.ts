import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { beforeEach, describe, expect, it } from 'vitest';
import { TrackingService } from './tracking.service';

describe('TrackingService', () => {
  let api: TrackingService;
  let http: HttpTestingController;
  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
    api = TestBed.inject(TrackingService); http = TestBed.inject(HttpTestingController);
  });
  it('passes company-safe server filters and retrieves the selected trace', () => {
    api.lots({ page: 2, pageSize: 20, itemId: 7, keyword: ' L-1 ', inStockOnly: true }).subscribe();
    const list = http.expectOne(r => r.url === '/api/v1/inv/tracking/lots');
    expect(list.request.params.get('page')).toBe('2');
    expect(list.request.params.get('itemId')).toBe('7');
    expect(list.request.params.get('keyword')).toBe('L-1');
    expect(list.request.params.get('inStockOnly')).toBe('true');
    list.flush({ items: [], totalCount: 0 });
    api.serial(9).subscribe();
    http.expectOne('/api/v1/inv/tracking/serials/9').flush({ header: {}, locations: [], history: [], historyTruncated: false });
    http.verify();
  });
  it('requests complete lot and serial CSV as blobs with response headers', () => {
    api.export('lots', 8).subscribe(response => expect(response.headers.get('X-Export-Row-Count')).toBe('1001'));
    const lot = http.expectOne('/api/v1/inv/tracking/lots/8/export');
    expect(lot.request.responseType).toBe('blob');
    lot.flush(new Blob(['csv']), { headers: { 'X-Export-Row-Count': '1001' } });
    api.export('serials', 9).subscribe();
    http.expectOne('/api/v1/inv/tracking/serials/9/export').flush(new Blob(['csv']));
    http.verify();
  });
});
