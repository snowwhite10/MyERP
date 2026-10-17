import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { describe, expect, it } from 'vitest';
import { TimeoutError } from 'rxjs';
import { SafetyStockService, validSafetyQuantity } from './safety-stock.service';

describe('SafetyStockService', () => {
  it('accepts the six-decimal quantity bounds and rejects invalid values', () => {
    expect(validSafetyQuantity('0.000001')).toBe(true);
    expect(validSafetyQuantity('9999999999999.999999')).toBe(true);
    for (const value of ['', '0', '-1', '1.0000001', '10000000000000', '1e3'])
      expect(validSafetyQuantity(value)).toBe(false);
  });

  it('passes server paging and version to the API', () => {
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
    const service = TestBed.inject(SafetyStockService);
    const http = TestBed.inject(HttpTestingController);
    expect(service.errorKey(new TimeoutError())).toBe('inv.safety.timeout');
    service.list({ page: 2, pageSize: 20, keyword: 'A', warehouseId: 3, lowOnly: true, sort: '-shortage' }).subscribe();
    const list = http.expectOne(r => r.url.endsWith('/safety-stock-thresholds'));
    expect(list.request.params.get('page')).toBe('2');
    expect(list.request.params.get('warehouseId')).toBe('3');
    expect(list.request.params.get('lowOnly')).toBe('true');
    expect(list.request.params.get('sort')).toBe('-shortage');
    list.flush({ items: [], totalCount: 0, page: 2, pageSize: 20, queriedAt: '' });
    service.update(7, 4, '5.000001').subscribe();
    const update = http.expectOne('/api/v1/inv/safety-stock-thresholds/7');
    expect(update.request.body).toEqual({ version: 4, minimumQuantity: '5.000001' });
    update.flush({ id: 7, version: 5 });
    service.remove(7, 5).subscribe();
    const remove = http.expectOne(r => r.url.endsWith('/safety-stock-thresholds/7'));
    expect(remove.request.params.get('version')).toBe('5');
    remove.flush(null);
    http.verify();
  });
});
