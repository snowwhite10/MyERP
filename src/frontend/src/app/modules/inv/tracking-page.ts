import { ChangeDetectionStrategy, Component, effect, inject, signal, untracked } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { TranslatePipe } from '@ngx-translate/core';
import { NzAlertModule } from 'ng-zorro-antd/alert';
import { NzButtonModule } from 'ng-zorro-antd/button';
import { NzTableModule, NzTableQueryParams } from 'ng-zorro-antd/table';
import { forkJoin, Observable, of } from 'rxjs';
import { CompanyContextService } from '../../core/company/company-context.service';
import { fetchAllPages, OPTION_PAGE_SIZE } from '../../shared/list-page/fetch-all-pages';
import { PagedResult } from '../../shared/list-page/table-query';
import { Item, ItemService } from '../md/item.service';
import { StorageLocation, Warehouse, WarehouseService } from '../md/warehouse.service';
import { LotRow, SerialRow, TrackingFilters, TrackingService, TrackingTrace } from './tracking.service';

@Component({
  selector: 'erp-tracking-page',
  imports: [FormsModule, TranslatePipe, NzAlertModule, NzButtonModule, NzTableModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <section class="tracking-page" data-testid="inv-tracking-page">
      <h1>{{ 'inv.trace.title' | translate }}</h1>
      <nav><button nz-button [nzType]="tab() === 'lots' ? 'primary' : 'default'" (click)="switchTab('lots')" data-testid="inv-tracking-lots">{{ 'inv.trace.lots' | translate }}</button>
        <button nz-button [nzType]="tab() === 'serials' ? 'primary' : 'default'" (click)="switchTab('serials')" data-testid="inv-tracking-serials">{{ 'inv.trace.serials' | translate }}</button></nav>
      <div class="filters">
        <select [(ngModel)]="itemId" [attr.aria-label]="'inv.trace.item' | translate"><option [ngValue]="null">{{ 'inv.trace.allItems' | translate }}</option>
          @for (item of items(); track item.id) { <option [ngValue]="item.id">{{ item.code }} · {{ item.name }}</option> }</select>
        <input type="search" [(ngModel)]="keyword" (keyup.enter)="search()" [placeholder]="'inv.trace.keyword' | translate" [attr.aria-label]="'inv.trace.keyword' | translate" />
        <label><input type="checkbox" [(ngModel)]="inStockOnly" /> {{ 'inv.trace.inStockOnly' | translate }}</label>
        <button nz-button (click)="search()" data-testid="inv-tracking-search">{{ 'inv.trace.search' | translate }}</button>
      </div>
      @if (error()) { <nz-alert nzType="error" nzShowIcon [nzMessage]="error()! | translate" /> }
      <nz-table #table [nzData]="rows()" [nzLoading]="loading()" [nzFrontPagination]="false"
        [nzTotal]="total()" [nzPageIndex]="page()" [nzPageSize]="pageSize()" (nzQueryParams)="onQuery($event)"
        [nzNoResult]="'inv.trace.empty' | translate" data-testid="inv-tracking-table">
        <thead><tr><th>{{ 'inv.trace.item' | translate }}</th><th>{{ (tab() === 'lots' ? 'inv.trace.lotNo' : 'inv.trace.serialNo') | translate }}</th>
          <th>{{ 'inv.trace.stock' | translate }}</th>@if (tab() === 'lots') { <th>{{ 'inv.tracking.manufactureDate' | translate }}</th><th>{{ 'inv.tracking.expiryDate' | translate }}</th> }</tr></thead>
        <tbody>@for (row of table.data; track row.id) { <tr>
          <td>{{ itemName(row.itemId) }}</td><td><button nz-button nzType="link" (click)="open(row)" [attr.data-testid]="'inv-tracking-row-' + row.id">{{ number(row) }}</button></td>
          <td>{{ stock(row) }}</td>@if (tab() === 'lots') { <td>{{ lot(row).manufactureDate }}</td><td>{{ lot(row).expiryDate }}</td> }
        </tr> }</tbody>
      </nz-table>
      @if (detail(); as d) { <section class="detail" data-testid="inv-tracking-detail">
        <button nz-button (click)="detail.set(null)" [attr.aria-label]="'inv.trace.close' | translate">×</button>
        <h2>{{ number(d.header) }}</h2>
        @if (d.historyTruncated) { <nz-alert nzType="warning" nzShowIcon [nzMessage]="'inv.trace.truncated' | translate" /> }
        <h3>{{ 'inv.trace.locations' | translate }}</h3>
        <table><thead><tr><th>{{ 'inv.trace.warehouse' | translate }}</th><th>{{ 'inv.trace.location' | translate }}</th><th>{{ 'inv.tracking.quantity' | translate }}</th></tr></thead>
          <tbody>@for (at of d.locations; track $index) { <tr><td>{{ warehouseName(at.warehouseId) }}</td><td>{{ locationName(at.warehouseId, at.locationId) }}</td><td>{{ at.quantity }}</td></tr> }</tbody></table>
        <h3>{{ 'inv.trace.history' | translate }}</h3>
        <table><thead><tr><th>{{ 'inv.trace.date' | translate }}</th><th>{{ 'inv.trace.source' | translate }}</th><th>{{ 'inv.trace.direction' | translate }}</th>
          <th>{{ 'inv.trace.warehouse' | translate }}</th><th>{{ 'inv.trace.location' | translate }}</th><th>{{ 'inv.tracking.quantity' | translate }}</th></tr></thead>
          <tbody>@for (event of d.history; track event.movementId) { <tr><td>{{ event.businessDate }}</td><td>{{ event.sourceType }} #{{ event.sourceId }} / {{ event.sourceLineNo }}</td>
            <td>{{ event.direction }}</td><td>{{ warehouseName(event.warehouseId) }}</td><td>{{ locationName(event.warehouseId, event.locationId) }}</td><td>{{ event.quantity }}</td></tr> }</tbody></table>
      </section> }
    </section>
  `,
  styles: `.tracking-page{display:grid;gap:16px;color:var(--erp-color-text)}h1{font-size:20px;margin:0}h2{font-size:17px}h3{font-size:15px}nav,.filters{display:flex;gap:8px;align-items:center;flex-wrap:wrap}select,input[type=search]{min-width:170px}.detail{padding:16px;border:1px solid var(--erp-color-border);overflow:auto}.detail table{width:100%;border-collapse:collapse}.detail td,.detail th{border-bottom:1px solid var(--erp-color-border);padding:6px;text-align:left}`,
})
export class TrackingPage {
  private readonly api = inject(TrackingService); private readonly itemApi = inject(ItemService);
  private readonly warehouseApi = inject(WarehouseService); private readonly company = inject(CompanyContextService);
  readonly tab = signal<'lots' | 'serials'>('lots'); readonly rows = signal<(LotRow | SerialRow)[]>([]);
  readonly detail = signal<TrackingTrace<LotRow | SerialRow> | null>(null); readonly items = signal<Item[]>([]);
  readonly warehouses = signal<Warehouse[]>([]); readonly error = signal<string | null>(null); readonly loading = signal(false);
  readonly locations = signal<Record<number, StorageLocation[]>>({});
  readonly page = signal(1); readonly pageSize = signal(20); readonly total = signal(0);
  itemId: number | null = null; keyword = ''; inStockOnly = false;
  private generation = 0;
  constructor() { effect(() => { this.company.current(); untracked(() => {
    this.itemId = null; this.keyword = ''; this.inStockOnly = false; this.detail.set(null); this.locations.set({}); this.loadOptions(); this.search();
  }); }); }
  private loadOptions(): void {
    forkJoin({ items: fetchAllPages(page => this.itemApi.list({ page, pageSize: OPTION_PAGE_SIZE })),
      warehouses: fetchAllPages(page => this.warehouseApi.list({ page, pageSize: OPTION_PAGE_SIZE })) }).subscribe({
      next: data => { this.items.set(data.items); this.warehouses.set(data.warehouses); },
      error: e => this.error.set(this.api.errorKey(e)),
    });
  }
  itemName(id: number): string { const item = this.items().find(x => x.id === id); return item ? `${item.code} · ${item.name}` : String(id); }
  warehouseName(id: number): string { const wh = this.warehouses().find(x => x.id === id); return wh ? `${wh.code} · ${wh.name}` : String(id); }
  locationName(warehouseId: number, locationId: number | null): string {
    if (!locationId) return '—';
    const location = this.locations()[warehouseId]?.find(x => x.id === locationId);
    return location ? `${location.code} · ${location.name}` : String(locationId);
  }
  lot(row: LotRow | SerialRow): LotRow { return row as LotRow; }
  number(row: LotRow | SerialRow): string { return 'lotNo' in row ? row.lotNo : row.serialNo; }
  stock(row: LotRow | SerialRow): string | number { return 'onHand' in row ? row.onHand : row.inStock ? '✓' : '—'; }
  switchTab(tab: 'lots' | 'serials'): void { if (this.tab() === tab) return; this.tab.set(tab); this.detail.set(null); this.search(); }
  onQuery(query: NzTableQueryParams): void { if (query.pageIndex !== this.page() || query.pageSize !== this.pageSize()) {
    this.page.set(query.pageIndex); this.pageSize.set(query.pageSize); this.load(); } }
  search(): void { this.page.set(1); this.detail.set(null); this.load(); }
  private filters(): TrackingFilters { return { page: this.page(), pageSize: this.pageSize(), itemId: this.itemId ?? undefined,
    keyword: this.keyword, inStockOnly: this.inStockOnly }; }
  load(): void {
    const generation = ++this.generation; this.loading.set(true); this.error.set(null);
    const request: Observable<PagedResult<LotRow | SerialRow>> = this.tab() === 'lots' ? this.api.lots(this.filters()) : this.api.serials(this.filters());
    request.subscribe({ next: result => { if (generation !== this.generation) return;
      this.rows.set(result.items); this.total.set(result.totalCount); this.loading.set(false); },
      error: e => { if (generation !== this.generation) return; this.error.set(this.api.errorKey(e)); this.loading.set(false); } });
  }
  open(row: LotRow | SerialRow): void {
    const request: Observable<TrackingTrace<LotRow | SerialRow>> = this.tab() === 'lots' ? this.api.lot(row.id) : this.api.serial(row.id);
    request.subscribe({ next: detail => {
      const warehouseIds = [...new Set([...detail.locations, ...detail.history].filter(x => x.locationId)
        .map(x => x.warehouseId))].filter(id => !this.locations()[id]);
      const locations = warehouseIds.length ? forkJoin(warehouseIds.map(id =>
        fetchAllPages(page => this.warehouseApi.listLocations(id, { page, pageSize: OPTION_PAGE_SIZE })))) : of([] as StorageLocation[][]);
      locations.subscribe({ next: groups => {
        this.locations.update(current => ({ ...current, ...Object.fromEntries(warehouseIds.map((id, index) => [id, groups[index]])) }));
        this.detail.set(detail);
      }, error: e => this.error.set(this.api.errorKey(e)) });
    }, error: e => this.error.set(this.api.errorKey(e)) });
  }
}
