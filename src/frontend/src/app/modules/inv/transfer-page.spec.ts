import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ANIMATION_MODULE_TYPE } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { provideNzIcons } from 'ng-zorro-antd/icon';
import { provideTestTranslations } from '../../../testing/i18n-fixtures';
import { APP_ICONS } from '../../core/icons';
import { PermissionService } from '../../core/auth/permission.service';
import { csvCell, TransferPage } from './transfer-page';

const API = '/api/v1/inv/transfers';
const draft = { id: 11, documentNo: 'TR-001', businessDate: '2026-10-14', status: 'Draft', remark: '=cmd', lineCount: 1, totalQuantity: 2, version: 3 };
const posted = { ...draft, id: 12, documentNo: 'TR-002', status: 'Posted', remark: null };

describe('INV-TRF-009 transfer page', () => {
  function open(granted: readonly string[] = ['INV.TRANSFER.VIEW', 'INV.TRANSFER.CREATE', 'INV.TRANSFER.EDIT', 'INV.TRANSFER.SUBMIT', 'INV.TRANSFER.DELETE']) {
    const has = (code: string) => granted.includes(code);
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([]),
      ...provideTestTranslations('en'), provideNzIcons(APP_ICONS), { provide: ANIMATION_MODULE_TYPE, useValue: 'NoopAnimations' },
      { provide: PermissionService, useValue: { has, hasAny: (...codes: string[]) => codes.flat().some(has) } }] });
    const fixture = TestBed.createComponent(TransferPage);
    TestBed.tick();
    const http = TestBed.inject(HttpTestingController);
    http.expectOne(r => r.url === API).flush({ items: [draft, posted], totalCount: 2 });
    fixture.detectChanges();
    return { page: fixture.componentInstance, http, fixture, element: fixture.nativeElement as HTMLElement };
  }
  afterEach(() => TestBed.inject(HttpTestingController).verify());

  it('shows lifecycle buttons only for drafts and granted permissions', () => {
    const { element } = open(['INV.TRANSFER.VIEW', 'INV.TRANSFER.SUBMIT']);
    const draftRow = element.querySelector('[data-testid="inv-transfer-row-11"]')!.textContent!;
    expect(draftRow).toContain('Submit');
    expect(draftRow).not.toContain('Delete');
    expect(draftRow).not.toContain('Edit');
    expect(element.querySelector('[data-testid="inv-transfer-row-12"]')!.textContent).not.toContain('Submit');
    expect(element.querySelector('[data-testid="inv-transfer-new"]')).toBeNull();
  });

  it('rejects an inverted date range before searching or exporting', () => {
    const { page, http } = open();
    page.from = '2026-10-14'; page.to = '2026-10-01';
    page.search(); page.exportCsv();
    http.expectNone(r => r.url === API);
    expect(page.error()).toBe('inv.transfer.invalidDates');
  });

  it('shows both sides with MD names, loading only warehouses that have a location', () => {
    const { page, http, fixture, element } = open();
    page.view(draft as never);
    http.expectOne(`${API}/11`).flush({ ...draft, postedAt: null, approval: null, lines: [
      { id: 1, lineNo: 1, itemId: 3, sourceWarehouseId: 4, sourceLocationId: 5, destinationWarehouseId: 6, destinationLocationId: null,
        quantity: 2, tracking: [{ lotNo: 'L1', serialNo: null, quantity: 2, manufactureDate: null, expiryDate: null }] }] });
    http.expectOne(r => r.url === '/api/v1/md/items').flush({ items: [{ id: 3, code: 'ITEM', name: 'Bolt', isActive: true }], totalCount: 1 });
    http.expectOne(r => r.url === '/api/v1/md/warehouses').flush({ items: [{ id: 4, code: 'WH', name: 'Main' }, { id: 6, code: 'WH2', name: 'Spare' }], totalCount: 2 });
    http.expectOne(r => r.url === '/api/v1/md/warehouses/4/locations').flush({ items: [{ id: 5, code: 'A1', name: 'Shelf' }], totalCount: 1 });
    http.expectNone(r => r.url === '/api/v1/md/warehouses/6/locations');
    fixture.detectChanges();
    const detail = element.querySelector('[data-testid="inv-transfer-detail"]')!.textContent!;
    expect(detail).toContain('ITEM · Bolt');
    expect(detail).toContain('WH · Main / A1 · Shelf');
    expect(detail).toContain('WH2 · Spare / —');
    expect(detail).toContain('L1 × 2');
  });

  it('submits with the row version and shows stock errors after reloading', () => {
    const { page, http } = open();
    page.submit(draft as never);
    const submit = http.expectOne(`${API}/11/submit`);
    expect(submit.request.body).toEqual({ version: 3 });
    submit.flush({ errorCode: 'INV.STOCK.INSUFFICIENT' }, { status: 422, statusText: 'Unprocessable' });
    http.expectOne(r => r.url === API).flush({ items: [draft], totalCount: 1 });
    expect(page.error()).toBe('errors.INV.STOCK.INSUFFICIENT');
  });
});

describe('INV-TRF-009 transfer CSV', () => {
  it('neutralises spreadsheet formulas in CSV cells', () => {
    expect(csvCell('=cmd')).toBe('"\'=cmd"');
    expect(csvCell(' +1')).toBe('"\' +1"');
    expect(csvCell('a"b')).toBe('"a""b"');
    expect(csvCell(-1)).toBe('"-1"');
    expect(csvCell(null)).toBe('""');
  });
});
