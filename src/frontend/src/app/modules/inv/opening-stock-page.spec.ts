import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ANIMATION_MODULE_TYPE } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { ActivatedRoute, convertToParamMap, provideRouter } from '@angular/router';
import { provideNzIcons } from 'ng-zorro-antd/icon';
import { provideTestTranslations } from '../../../testing/i18n-fixtures';
import { APP_ICONS } from '../../core/icons';
import { PermissionService } from '../../core/auth/permission.service';
import { OpeningStockPage } from './opening-stock-page';

const API = '/api/v1/inv/opening-stocks';
const summary = { id: 11, requestKey: 'k', openingDate: '2026-10-01', remark: null, lineCount: 1, totalQuantity: 5, totalAmount: 50,
  postedAt: '2026-10-13T20:00:00', createdAt: '2026-10-13T20:00:00' };

describe('INV-OPN-007 opening stock page', () => {
  function open(query: Record<string, string> = {}) {
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([]),
      ...provideTestTranslations('en'), provideNzIcons(APP_ICONS), { provide: ANIMATION_MODULE_TYPE, useValue: 'NoopAnimations' },
      { provide: ActivatedRoute, useValue: { snapshot: { queryParamMap: convertToParamMap(query) } } },
      { provide: PermissionService, useValue: { has: () => true, hasAny: () => true } }] });
    const fixture = TestBed.createComponent(OpeningStockPage);
    TestBed.tick();
    const http = TestBed.inject(HttpTestingController);
    http.expectOne(r => r.url === '/api/v1/md/items').flush({ items: [{ id: 3, code: 'ITEM', name: 'Bolt', isActive: true }], totalCount: 1 });
    http.expectOne(r => r.url === '/api/v1/md/warehouses').flush({ items: [{ id: 4, code: 'WH', name: 'Main', isActive: true }], totalCount: 1 });
    http.expectOne(r => r.url === API).flush({ items: [summary], totalCount: 1 });
    fixture.detectChanges();
    return { page: fixture.componentInstance, http, fixture, element: fixture.nativeElement as HTMLElement };
  }
  afterEach(() => TestBed.inject(HttpTestingController).verify());

  it('shows the replay notice after a resubmitted batch', () => {
    const { element } = open({ posted: '11', replayed: '1' });
    expect(element.querySelector('[data-testid="inv-opening-posted"]')!.textContent).toContain('not posted twice');
  });

  it('rejects an inverted date range for search and export before calling the API', () => {
    const { page, http } = open();
    page.from = '2026-10-13'; page.to = '2026-10-01';
    page.search(); page.exportCsv();
    http.expectNone(r => r.url === API);
    expect(page.error()).toBe('inv.opening.invalidDates');
  });

  it('loads location names only for warehouses used by located lines before showing detail', () => {
    const { page, http, fixture, element } = open();
    page.view(summary);
    http.expectOne(`${API}/11`).flush({ ...summary, replayed: false, lines: [
      { id: 1, lineNo: 1, itemId: 3, warehouseId: 4, locationId: 5, quantity: 5, unitCost: 10, amount: 50 },
      { id: 2, lineNo: 2, itemId: 3, warehouseId: 9, locationId: null, quantity: 1, unitCost: 0, amount: 0 }] });
    expect(page.detail()).toBeNull();
    http.expectOne(r => r.url === '/api/v1/md/warehouses/4/locations').flush({ items: [{ id: 5, code: 'A1', name: 'Shelf', isActive: true }], totalCount: 1 });
    http.expectNone(r => r.url === '/api/v1/md/warehouses/9/locations');
    fixture.detectChanges();
    const detail = element.querySelector('[data-testid="inv-opening-detail"]')!.textContent!;
    expect(detail).toContain('ITEM · Bolt');
    expect(detail).toContain('A1 · Shelf');
    expect(detail).toContain('—');
  });
});
