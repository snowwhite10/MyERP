import { HttpRequest, provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ANIMATION_MODULE_TYPE, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideNzIcons } from 'ng-zorro-antd/icon';
import enErrors from '../../../../public/i18n/en/errors.json';
import enFi from '../../../../public/i18n/en/fi.json';
import enLayout from '../../../../public/i18n/en/layout.json';
import { provideTestTranslations } from '../../../testing/i18n-fixtures';
import { CompanyContextService } from '../../core/company/company-context.service';
import { APP_ICONS } from '../../core/icons';
import { SalesSourcePage } from './sales-source-page';

const API = '/api/v1/ar/sales-sources';
const row = { id: 12, customerId: 4, sourceType: 'Invoice', sourceId: 9, sourceVersion: 2,
  businessDate: '2026-11-25', currency: 'TWD', baseCurrency: 'TWD', grossAmount: 420,
  baseGrossAmount: 420, glJournalId: 28, receivableId: 7, openBaseAmount: 315 };
const list = (items: object[]) => JSON.stringify({ items, totalCount: items.length, page: 1, pageSize: 20 });

describe('FI-SRC-UI-001/002/007 source trace screen', () => {
  const current = signal(1);
  beforeEach(() => TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting(),
    ...provideTestTranslations('en'), provideNzIcons(APP_ICONS),
    { provide: ANIMATION_MODULE_TYPE, useValue: 'NoopAnimations' },
    { provide: CompanyContextService, useValue: { current } }] }));
  afterEach(() => TestBed.inject(HttpTestingController).verify());
  const isList = (r: HttpRequest<unknown>) => r.url === API && r.method === 'GET';
  function create(items: object[] = [row]) {
    const fixture = TestBed.createComponent(SalesSourcePage); TestBed.tick();
    const http = TestBed.inject(HttpTestingController);
    http.expectOne(isList).flush(list(items)); fixture.detectChanges();
    return { fixture, page: fixture.componentInstance, http,
      text: (id: string) => (fixture.nativeElement as HTMLElement).querySelector(`[data-testid="${id}"]`)?.textContent?.trim() ?? '' };
  }

  it('renders the saved source, current open amount, journal and linked credit allocation', () => {
    const { page, http, fixture, text } = create();
    expect(text('fi-source-row-12')).toContain('420');
    page.view(12);
    http.expectOne(`${API}/12`).flush(JSON.stringify({ source: row, netAmount: 400, taxAmount: 20,
      baseNetAmount: 400, baseTaxAmount: 20, exchangeRate: 1,
      allocations: [{ id: 31, creditSourceId: 14, receivableId: 7, originalSourceId: 9,
        invoiceLineId: 18, quantity: 1, grossAmount: 105, baseGrossAmount: 105 }] }));
    fixture.detectChanges();
    expect(text('fi-source-open')).toContain('315');
    expect(text('fi-source-allocations')).toContain('105');
    expect(text('fi-source-detail')).toContain('#28');
  });

  it('refuses invalid customer/date/source key locally, and a failed read never claims the list is empty', () => {
    const { page, http, fixture, text } = create([]);
    page.customerId = '0'; page.search(); fixture.detectChanges();
    expect(text('fi-source-validation')).toContain('positive whole');
    http.expectNone(isList);
    page.customerId = ''; page.from = '2026-12-01'; page.to = '2026-11-01'; page.search();
    expect(text('fi-source-validation')).toBeTruthy(); http.expectNone(isList);
    page.lookupId = '0'; page.lookup(); http.expectNone(r => r.url.includes('/by-key/'));
    page.reset(); http.expectOne(isList).flush({ errorCode: 'SERVER', traceId: 'trace-31' }, { status: 500, statusText: 'Error' });
    fixture.detectChanges(); expect(text('fi-source-error')).toContain('trace-31');
    expect(text('fi-source-table')).toContain('read failed');
  });

  it('discards a late detail after a company switch and rejects IDs that JavaScript would round', () => {
    const { page, fixture, http, text } = create();
    page.view(12);
    const pending = http.expectOne(`${API}/12`);
    current.set(2); TestBed.tick();
    http.expectOne(isList).flush(list([]));
    pending.flush(JSON.stringify({ source: row, netAmount: 400, taxAmount: 20,
      baseNetAmount: 400, baseTaxAmount: 20, exchangeRate: 1, allocations: [] }));
    fixture.detectChanges();
    expect(text('fi-source-detail')).toBe('');
    page.lookupId = '9007199254740993'; page.lookup(); fixture.detectChanges();
    expect(text('fi-source-validation')).toContain('positive whole');
    http.expectNone(r => r.url.includes('/by-key/'));
    current.set(1);
  });

  it('has translations for every source label in all three languages', () => {
    for (const dict of [enFi, requireFi('hant'), requireFi('hans')]) {
      expect(Object.keys(dict.salesSource).sort()).toEqual(Object.keys(enFi.salesSource).sort());
    }
    expect(enLayout.menu.salesSources).toBeTruthy(); expect(enErrors.http.network).toBeTruthy();
  });
});

function requireFi(which: 'hant' | 'hans') {
  return which === 'hant' ? hantFi : hansFi;
}
import hantFi from '../../../../public/i18n/zh-Hant/fi.json';
import hansFi from '../../../../public/i18n/zh-Hans/fi.json';
