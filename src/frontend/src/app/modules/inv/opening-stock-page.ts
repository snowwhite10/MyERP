import { SlicePipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, effect, inject, signal, untracked } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { TranslatePipe } from '@ngx-translate/core';
import { NzAlertModule } from 'ng-zorro-antd/alert';
import { NzButtonModule } from 'ng-zorro-antd/button';
import { NzTableModule, NzTableQueryParams } from 'ng-zorro-antd/table';
import { forkJoin, of } from 'rxjs';
import { HasPermissionDirective } from '../../core/auth/has-permission.directive';
import { CompanyContextService } from '../../core/company/company-context.service';
import { fetchAllPages, OPTION_PAGE_SIZE } from '../../shared/list-page/fetch-all-pages';
import { Item, ItemService } from '../md/item.service';
import { StorageLocation, Warehouse, WarehouseService } from '../md/warehouse.service';
import { OpeningDetail, OpeningFilters, OpeningStockService, OpeningSummary } from './opening-stock.service';
import { stockCsvCell } from './stock-query.service';

/** 物料期初庫存批次列表、詳情、列印與防公式注入 CSV（ADR-056、ADR-059）。批次一經提交即過帳，不提供修改或刪除。 */
@Component({
  selector: 'erp-opening-stock-page',
  imports: [SlicePipe, FormsModule, RouterLink, TranslatePipe, NzAlertModule, NzButtonModule, NzTableModule, HasPermissionDirective],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <section class="openings"><header><h1>{{ 'inv.opening.title' | translate }}</h1>
      <a nz-button nzType="primary" routerLink="new" *erpHasPermission="'INV.OPENING_STOCK.CREATE'" data-testid="inv-opening-new">{{ 'inv.opening.new' | translate }}</a></header>
      @if (posted()) { <nz-alert nzType="success" nzShowIcon [nzMessage]="(replayed() ? 'inv.opening.replayed' : 'inv.opening.posted') | translate" data-testid="inv-opening-posted" /> }
      <div class="filters">
        <input type="date" [(ngModel)]="from" [attr.aria-label]="'inv.opening.from' | translate" data-testid="inv-opening-from" />
        <input type="date" [(ngModel)]="to" [attr.aria-label]="'inv.opening.to' | translate" data-testid="inv-opening-to" />
        <select [(ngModel)]="itemId" [attr.aria-label]="'inv.opening.item' | translate" data-testid="inv-opening-item"><option [ngValue]="null">{{ 'inv.opening.allItems' | translate }}</option>
          @for (item of items(); track item.id) { <option [ngValue]="item.id">{{ item.code }} · {{ item.name }}</option> }</select>
        <select [(ngModel)]="warehouseId" [attr.aria-label]="'inv.opening.warehouse' | translate"><option [ngValue]="null">{{ 'inv.opening.allWarehouses' | translate }}</option>
          @for (wh of warehouses(); track wh.id) { <option [ngValue]="wh.id">{{ wh.code }} · {{ wh.name }}</option> }</select>
        <input type="search" [(ngModel)]="keyword" [attr.aria-label]="'inv.opening.keyword' | translate" [placeholder]="'inv.opening.keyword' | translate" (keyup.enter)="search()" />
        <button nz-button (click)="search()" data-testid="inv-opening-search">{{ 'inv.opening.search' | translate }}</button>
        <button nz-button (click)="exportCsv()" [disabled]="exporting()" data-testid="inv-opening-export">{{ 'inv.opening.export' | translate }}</button>
      </div>
      @if (error()) { <nz-alert nzType="error" nzShowIcon [nzMessage]="error()! | translate" /> }
      <nz-table #table [nzData]="rows()" [nzLoading]="loading()" [nzFrontPagination]="false"
        [nzTotal]="total()" [nzPageIndex]="page()" [nzPageSize]="pageSize()" (nzQueryParams)="onQuery($event)"
        [nzNoResult]="'inv.opening.empty' | translate" data-testid="inv-opening-table">
        <thead><tr><th>{{ 'inv.opening.date' | translate }}</th><th>{{ 'inv.opening.lines' | translate }}</th>
          <th>{{ 'inv.opening.quantity' | translate }}</th><th>{{ 'inv.opening.amount' | translate }}</th>
          <th>{{ 'inv.opening.remark' | translate }}</th><th>{{ 'inv.opening.postedAt' | translate }}</th><th></th></tr></thead>
        <tbody>@for (row of table.data; track row.id) { <tr [attr.data-testid]="'inv-opening-row-' + row.id">
          <td><button nz-button nzType="link" (click)="view(row)">{{ row.openingDate }}</button></td>
          <td>{{ row.lineCount }}</td><td>{{ row.totalQuantity }}</td><td>{{ row.totalAmount }}</td>
          <td>{{ row.remark }}</td><td>{{ row.postedAt | slice: 0 : 16 }}</td>
          <td><button nz-button nzType="link" (click)="print(row)">{{ 'inv.opening.print' | translate }}</button></td>
        </tr>}</tbody>
      </nz-table>
      @if (detail(); as d) { <section class="detail" data-testid="inv-opening-detail"><button nz-button (click)="detail.set(null)" [attr.aria-label]="'inv.opening.close' | translate">×</button>
        <h2>{{ 'inv.opening.title' | translate }} · {{ d.openingDate }}</h2><p>{{ d.remark }} · {{ 'inv.opening.amount' | translate }} {{ d.totalAmount }}</p>
        <table><thead><tr><th>#</th><th>{{ 'inv.opening.item' | translate }}</th><th>{{ 'inv.opening.warehouse' | translate }}</th>
          <th>{{ 'inv.opening.location' | translate }}</th><th>{{ 'inv.opening.quantity' | translate }}</th><th>{{ 'inv.opening.unitCost' | translate }}</th><th>{{ 'inv.opening.amount' | translate }}</th></tr></thead>
          <tbody>@for (line of d.lines; track line.id) { <tr><td>{{ line.lineNo }}</td><td>{{ itemName(line.itemId) }}</td><td>{{ warehouseName(line.warehouseId) }}</td><td>{{ locationName(line.warehouseId, line.locationId) }}</td>
            <td>{{ line.quantity }}</td><td>{{ line.unitCost }}</td><td>{{ line.amount }}</td></tr> }</tbody></table></section> }
    </section>
  `,
  styles: `.openings{display:grid;gap:16px;color:var(--erp-color-text)}header,.filters{display:flex;gap:8px;align-items:center;flex-wrap:wrap}header{justify-content:space-between}h1{font-size:20px;margin:0}select,input{min-width:130px}.detail{padding:16px;border:1px solid var(--erp-color-border)}.detail table{width:100%}@media print{header,.filters,nz-table,nz-alert,.detail>button{display:none!important}.detail{border:0}.detail td,.detail th{border:1px solid #777;padding:4px}}`,
})
export class OpeningStockPage {
  private readonly api = inject(OpeningStockService);
  private readonly itemApi = inject(ItemService);
  private readonly warehouseApi = inject(WarehouseService);
  private readonly company = inject(CompanyContextService);
  private readonly route = inject(ActivatedRoute);
  readonly rows = signal<OpeningSummary[]>([]);
  readonly detail = signal<OpeningDetail | null>(null);
  readonly total = signal(0); readonly page = signal(1); readonly pageSize = signal(20);
  readonly loading = signal(false); readonly exporting = signal(false);
  readonly error = signal<string | null>(null);
  readonly posted = signal(!!this.route.snapshot.queryParamMap.get('posted'));
  readonly replayed = signal(this.route.snapshot.queryParamMap.get('replayed') === '1');
  readonly items = signal<Item[]>([]); readonly warehouses = signal<Warehouse[]>([]);
  readonly locationMap = signal<Record<number, StorageLocation[]>>({});
  from = ''; to = ''; keyword = ''; itemId: number | null = null; warehouseId: number | null = null;
  private generation = 0;
  constructor() {
    effect(() => { this.company.current(); untracked(() => { this.itemId = null; this.warehouseId = null; this.detail.set(null);
      this.locationMap.set({}); this.loadNames(); this.search(); }); });
  }
  private loadNames(): void {
    forkJoin({ items: fetchAllPages(page => this.itemApi.list({ page, pageSize: OPTION_PAGE_SIZE })),
      warehouses: fetchAllPages(page => this.warehouseApi.list({ page, pageSize: OPTION_PAGE_SIZE })) }).subscribe({
      next: x => { this.items.set(x.items); this.warehouses.set(x.warehouses); },
      error: e => this.error.set(this.api.errorKey(e)),
    });
  }
  itemName(id: number): string { const item = this.items().find(x => x.id === id); return item ? `${item.code} · ${item.name}` : String(id); }
  warehouseName(id: number): string { const wh = this.warehouses().find(x => x.id === id); return wh ? `${wh.code} · ${wh.name}` : String(id); }
  locationName(warehouseId: number, locationId: number | null): string {
    if (!locationId) return '—';
    const loc = this.locationMap()[warehouseId]?.find(x => x.id === locationId);
    return loc ? `${loc.code} · ${loc.name}` : String(locationId);
  }
  onQuery(query: NzTableQueryParams): void { if (query.pageIndex !== this.page() || query.pageSize !== this.pageSize()) { this.page.set(query.pageIndex); this.pageSize.set(query.pageSize); this.load(); } }
  search(): void {
    if (this.from && this.to && this.from > this.to) { this.error.set('inv.opening.invalidDates'); return; }
    this.page.set(1); this.load();
  }
  private filters(page: number, pageSize: number): OpeningFilters {
    return { page, pageSize, from: this.from, to: this.to, keyword: this.keyword, itemId: this.itemId ?? undefined, warehouseId: this.warehouseId ?? undefined };
  }
  load(): void {
    const current = ++this.generation; this.loading.set(true); this.error.set(null);
    this.api.list(this.filters(this.page(), this.pageSize())).subscribe({ next: result => {
      if (current !== this.generation) return; this.rows.set(result.items); this.total.set(result.totalCount); this.loading.set(false);
    }, error: e => { if (current === this.generation) { this.error.set(this.api.errorKey(e)); this.loading.set(false); } } });
  }
  /** 取得詳情並補齊明細引用倉庫的儲位名稱，完成後才顯示（列印時保證名稱已載入）。 */
  private open(row: OpeningSummary, then?: () => void): void {
    this.api.get(row.id).subscribe({ next: d => {
      const ids = [...new Set(d.lines.filter(x => x.locationId).map(x => x.warehouseId))].filter(id => !this.locationMap()[id]);
      const locations = ids.length ? forkJoin(ids.map(id => fetchAllPages(page => this.warehouseApi.listLocations(id, { page, pageSize: OPTION_PAGE_SIZE })))) : of([] as StorageLocation[][]);
      locations.subscribe({ next: groups => {
        this.locationMap.update(current => ({ ...current, ...Object.fromEntries(ids.map((id, i) => [id, groups[i]])) }));
        this.detail.set(d); then?.();
      }, error: e => this.error.set(this.api.errorKey(e)) });
    }, error: e => this.error.set(this.api.errorKey(e)) });
  }
  view(row: OpeningSummary): void { this.open(row); }
  print(row: OpeningSummary): void { this.open(row, () => setTimeout(() => window.print())); }
  exportCsv(): void {
    if (this.exporting()) return;
    if (this.from && this.to && this.from > this.to) { this.error.set('inv.opening.invalidDates'); return; }
    this.exporting.set(true); this.error.set(null);
    fetchAllPages(page => this.api.list(this.filters(page, OPTION_PAGE_SIZE))).subscribe({ next: rows => {
      const data: (string | number | null)[][] = [['Opening Date', 'Lines', 'Quantity', 'Amount', 'Remark', 'Posted At', 'Request Key'],
        ...rows.map(r => [r.openingDate, r.lineCount, r.totalQuantity, r.totalAmount, r.remark ?? '', r.postedAt, r.requestKey])];
      const blob = new Blob(['﻿', data.map(line => line.map(stockCsvCell).join(',')).join('\r\n')], { type: 'text/csv;charset=utf-8' });
      const url = URL.createObjectURL(blob); const link = document.createElement('a'); link.href = url;
      link.download = 'opening-stocks.csv'; link.click(); URL.revokeObjectURL(url); this.exporting.set(false);
    }, error: e => { this.error.set(this.api.errorKey(e)); this.exporting.set(false); } });
  }
}
