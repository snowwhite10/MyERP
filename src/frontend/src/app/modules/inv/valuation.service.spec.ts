import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { beforeEach, describe, expect, it } from 'vitest';
import { ValuationService } from './valuation.service';

describe('ValuationService', () => {
  let api: ValuationService;
  let http: HttpTestingController;
  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
    api = TestBed.inject(ValuationService); http = TestBed.inject(HttpTestingController);
  });
  it('passes the posting cutoff only when supplied and preserves unknown value', () => {
    let result: unknown;
    api.item(7, '2026-10-14T09:30:00').subscribe(value => result = value);
    const request = http.expectOne(r => r.url === '/api/v1/inv/valuations/items/7');
    expect(request.request.params.get('cutoffPostedAt')).toBe('2026-10-14T09:30:00');
    request.flush({ itemId: 7, onHand: 2, value: null, averageUnitCost: null,
      unpricedQuantity: 2, status: 'Unpriced', movementCount: 1, provisional: true });
    expect(result).toMatchObject({ value: null, status: 'Unpriced', provisional: true });
    api.item(7).subscribe();
    expect(http.expectOne('/api/v1/inv/valuations/items/7').request.params.has('cutoffPostedAt')).toBe(false);
    http.verify();
  });
  it('loads valuation item options without MD item permission', () => {
    api.items(2, 50).subscribe();
    const request = http.expectOne(r => r.url === '/api/v1/inv/valuations/items');
    expect(request.request.params.get('page')).toBe('2');
    expect(request.request.params.get('pageSize')).toBe('50');
    request.flush({ items: [], page: 2, pageSize: 50, totalCount: 0 });
    http.verify();
  });
});
