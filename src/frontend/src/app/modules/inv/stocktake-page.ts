import { ChangeDetectionStrategy, Component, effect, inject, signal, untracked } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { TranslatePipe } from '@ngx-translate/core';
import { forkJoin, from, map, mergeMap, Observable, of, toArray } from 'rxjs';
import { NzAlertModule } from 'ng-zorro-antd/alert';
import { NzButtonModule } from 'ng-zorro-antd/button';
import { NzTableModule, NzTableQueryParams } from 'ng-zorro-antd/table';
import { CompanyContextService } from '../../core/company/company-context.service';
import { HasPermissionDirective } from '../../core/auth/has-permission.directive';
import { PermissionService } from '../../core/auth/permission.service';
import { ErpDialogService } from '../../shared/dialogs/dialog.service';
import { fetchAllPages, OPTION_PAGE_SIZE } from '../../shared/list-page/fetch-all-pages';
import { Item, ItemService } from '../md/item.service';
import { StorageLocation, Warehouse, WarehouseService } from '../md/warehouse.service';
import { csvCell } from './transfer-page';
import { StocktakeDetail, StocktakeService, StocktakeSummary, StocktakeVariance } from './stocktake.service';

/** 介面 5.24：盤點列表與已保存的差異報告。 */
@Component({ selector: 'erp-stocktake-page',
  imports: [FormsModule, RouterLink, TranslatePipe, NzAlertModule, NzButtonModule, NzTableModule, HasPermissionDirective],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `<section class="stocktakes"><header><h1>{{ 'inv.stocktake.title' | translate }}</h1>
    <a nz-button nzType="primary" routerLink="new" *erpHasPermission="'INV.STOCKTAKE.CREATE'">{{ 'inv.stocktake.new' | translate }}</a></header>
    <div class="filters"><input type="search" [(ngModel)]="keyword" [attr.aria-label]="'inv.stocktake.keyword' | translate" data-testid="inv-stocktake-search" (keyup.enter)="search()" />
      <input type="date" [(ngModel)]="from" [attr.aria-label]="'inv.stocktake.from' | translate" />
      <input type="date" [(ngModel)]="to" [attr.aria-label]="'inv.stocktake.to' | translate" />
      <select [(ngModel)]="status" [attr.aria-label]="'inv.stocktake.statusLabel' | translate"><option value="">{{ 'inv.stocktake.all' | translate }}</option>
        <option value="Draft">{{ 'inv.stocktake.status.draft' | translate }}</option><option value="PendingApproval">{{ 'inv.stocktake.status.pendingApproval' | translate }}</option>
        <option value="Rejected">{{ 'inv.stocktake.status.rejected' | translate }}</option><option value="Posted">{{ 'inv.stocktake.status.posted' | translate }}</option></select>
      <button nz-button (click)="search()">{{ 'inv.stocktake.filter' | translate }}</button>
      <button nz-button (click)="exportCsv()" [disabled]="exporting()" data-testid="inv-stocktake-export">{{ 'inv.stocktake.export' | translate }}</button></div>
    @if (error()) { <nz-alert nzType="error" [nzMessage]="error()! | translate" nzShowIcon data-testid="inv-stocktake-error" /> }
    <nz-table #table [nzData]="rows()" [nzLoading]="loading()" [nzFrontPagination]="false" [nzTotal]="total()"
      [nzPageIndex]="page()" [nzPageSize]="pageSize()" (nzQueryParams)="onQuery($event)" [nzNoResult]="'inv.stocktake.empty' | translate" data-testid="inv-stocktake-table">
      <thead><tr><th>{{ 'inv.stocktake.numberLabel' | translate }}</th><th>{{ 'inv.stocktake.date' | translate }}</th><th>{{ 'inv.stocktake.statusLabel' | translate }}</th><th>{{ 'inv.stocktake.lines' | translate }}</th><th>{{ 'inv.stocktake.counted' | translate }}</th><th>{{ 'inv.stocktake.remark' | translate }}</th><th></th></tr></thead>
      <tbody>@for (row of table.data; track row.id) { <tr [attr.data-testid]="'inv-stocktake-row-' + row.id">
        <td><button nz-button nzType="link" (click)="view(row)">{{ row.documentNo }}</button></td><td>{{ row.businessDate }}</td>
        <td>{{ 'inv.stocktake.status.' + statusKey(row.status) | translate }}</td><td>{{ row.lineCount }}</td><td>{{ row.countedLineCount }}</td><td>{{ row.remark }}</td><td>
          @if (row.status === 'Draft' || row.status === 'Rejected') { <a [routerLink]="[row.id, 'count']" *erpHasPermission="'INV.STOCKTAKE.EDIT'">{{ 'inv.stocktake.count' | translate }}</a> }
          @if (row.status === 'Draft') { <button nz-button nzType="link" *erpHasPermission="'INV.STOCKTAKE.SUBMIT'" [disabled]="busy()" (click)="submit(row)">{{ 'inv.stocktake.submit' | translate }}</button>
            <button nz-button nzType="link" nzDanger *erpHasPermission="'INV.STOCKTAKE.DELETE'" [disabled]="busy()" (click)="remove(row)">{{ 'inv.stocktake.delete' | translate }}</button> }
          <button nz-button nzType="link" (click)="print(row)">{{ 'inv.stocktake.print' | translate }}</button></td></tr> }</tbody></nz-table>
    @if (detail(); as d) { <section class="detail" data-testid="inv-stocktake-detail"><button nz-button (click)="detail.set(null); variance.set(null)" [attr.aria-label]="'inv.stocktake.close' | translate">×</button>
      <h2>{{ d.documentNo }} · {{ 'inv.stocktake.status.' + statusKey(d.status) | translate }}</h2><p>{{ d.businessDate }} · {{ d.remark }}</p>
      @if (d.approval; as approval) { <p>{{ 'inv.stocktake.approval' | translate }}: {{ approval.status }}</p> }
      <label><input type="checkbox" [ngModel]="differenceOnly()" (ngModelChange)="toggleDifference($event)" />{{ 'inv.stocktake.differenceOnly' | translate }}</label>
      @if (variance(); as report) { <p>{{ 'inv.stocktake.gain' | translate }}: {{ report.gainQuantity }} ({{ report.gainLineCount }}) · {{ 'inv.stocktake.loss' | translate }}: {{ report.lossQuantity }} ({{ report.lossLineCount }}) · {{ 'inv.stocktake.match' | translate }}: {{ report.matchLineCount }} · {{ 'inv.stocktake.uncounted' | translate }}: {{ report.uncountedLineCount }}</p>
        <table><thead><tr><th>#</th><th>{{ 'inv.stocktake.item' | translate }}</th><th>{{ 'inv.stocktake.warehouse' | translate }}</th><th>{{ 'inv.stocktake.location' | translate }}</th><th>{{ 'inv.stocktake.book' | translate }}</th><th>{{ 'inv.stocktake.counted' | translate }}</th><th>{{ 'inv.stocktake.difference' | translate }}</th><th>{{ 'inv.stocktake.gain' | translate }}</th><th>{{ 'inv.stocktake.loss' | translate }}</th><th>{{ 'inv.stocktake.type' | translate }}</th><th>{{ 'inv.stocktake.tracking' | translate }}</th></tr></thead>
          <tbody>@for (line of report.lines; track line.lineId) { <tr><td>{{ line.lineNo }}</td><td>{{ itemName(line.itemId) }}</td><td>{{ warehouseName(line.warehouseId) }}</td><td>{{ locationName(line.warehouseId, line.locationId) }}</td><td>{{ line.bookQuantity }}</td><td>{{ line.countedQuantity ?? '—' }}</td><td>{{ line.difference ?? '—' }}</td><td>{{ line.gainQuantity }}</td><td>{{ line.lossQuantity }}</td><td>{{ 'inv.stocktake.varianceType.' + typeKey(line.type) | translate }}</td><td><ul>@for (number of line.tracking; track $index) { <li>{{ number.lotNo ?? number.serialNo }}: {{ number.bookQuantity }} → {{ number.countedQuantity ?? '—' }} ({{ number.difference ?? '—' }})</li> }</ul></td></tr> }</tbody></table> }
    </section> }
  </section>`,
  styles: `.stocktakes{display:grid;gap:16px;color:var(--erp-color-text)}header,.filters{display:flex;gap:8px;align-items:center;flex-wrap:wrap}header{justify-content:space-between}h1{font-size:20px;margin:0}.detail{padding:16px;border:1px solid var(--erp-color-border);overflow:auto}.detail table{width:100%;border-collapse:collapse}.detail th,.detail td{padding:8px;border-bottom:1px solid var(--erp-color-border);text-align:left}.detail ul{margin:0;padding-left:18px}@media print{header,.filters,nz-table,.detail>button,.detail label{display:none!important}.detail{border:0}}` })
export class StocktakePage {
  private readonly api = inject(StocktakeService); private readonly company = inject(CompanyContextService);
  private readonly itemApi = inject(ItemService); private readonly warehouseApi = inject(WarehouseService);
  private readonly permissions = inject(PermissionService); private readonly dialogs = inject(ErpDialogService);
  readonly rows = signal<StocktakeSummary[]>([]); readonly detail = signal<StocktakeDetail | null>(null);
  readonly variance = signal<StocktakeVariance | null>(null); readonly differenceOnly = signal(false);
  readonly items = signal<Item[]>([]); readonly warehouses = signal<Warehouse[]>([]);
  readonly locationMap = signal<Record<number, StorageLocation[]>>({});
  readonly page = signal(1); readonly pageSize = signal(20); readonly total = signal(0);
  readonly loading = signal(false); readonly busy = signal(false); readonly exporting = signal(false);
  readonly error = signal<string | null>(null); keyword = ''; from = ''; to = ''; status = '';
  private generation = 0;
  readonly statusKey = (status: string) => status[0].toLowerCase() + status.slice(1);
  readonly typeKey = (type: string) => type[0].toLowerCase() + type.slice(1);
  constructor() { effect(() => { this.company.current(); untracked(() => { this.detail.set(null); this.locationMap.set({}); this.search(); }); }); }
  private query(page: number, pageSize: number) { return { page, pageSize, keyword: this.keyword, from: this.from, to: this.to, status: this.status }; }
  onQuery(query: NzTableQueryParams): void { if (query.pageIndex !== this.page() || query.pageSize !== this.pageSize()) { this.page.set(query.pageIndex); this.pageSize.set(query.pageSize); this.load(); } }
  search(): void { if (this.from && this.to && this.to < this.from) { this.error.set('inv.stocktake.invalidDates'); return; } this.page.set(1); this.load(); }
  load(): void { const current = ++this.generation; this.loading.set(true); this.error.set(null);
    this.api.list(this.query(this.page(), this.pageSize())).subscribe({ next: result => { if (current !== this.generation) return;
      this.rows.set(result.items); this.total.set(result.totalCount); this.loading.set(false); },
    error: e => { if (current === this.generation) { this.error.set(this.api.errorKey(e)); this.loading.set(false); } } }); }
  itemName(id: number): string { const item = this.items().find(x => x.id === id); return item ? `${item.code} · ${item.name}` : String(id); }
  warehouseName(id: number): string { const wh = this.warehouses().find(x => x.id === id); return wh ? `${wh.code} · ${wh.name}` : String(id); }
  locationName(warehouseId: number, locationId: number | null): string { if (locationId === null) return '—';
    const loc = this.locationMap()[warehouseId]?.find(x => x.id === locationId); return loc ? `${loc.code} · ${loc.name}` : String(locationId); }
  private names(lines: { warehouseId: number; locationId: number | null }[]): Observable<void> {
    const warehouseIds = [...new Set(lines.filter(x => x.locationId !== null).map(x => x.warehouseId))].filter(id => !this.locationMap()[id]);
    const locations = warehouseIds.length ? forkJoin(warehouseIds.map(id => fetchAllPages(page => this.warehouseApi.listLocations(id, { page, pageSize: OPTION_PAGE_SIZE })))) : of([] as StorageLocation[][]);
    return forkJoin({ items: fetchAllPages(page => this.itemApi.list({ page, pageSize: OPTION_PAGE_SIZE })),
      warehouses: fetchAllPages(page => this.warehouseApi.list({ page, pageSize: OPTION_PAGE_SIZE })), locations }).pipe(map(data => {
      this.items.set(data.items); this.warehouses.set(data.warehouses);
      this.locationMap.update(current => ({ ...current, ...Object.fromEntries(warehouseIds.map((id, index) => [id, data.locations[index]])) }));
    }));
  }
  view(row: StocktakeSummary, print = false): void { this.error.set(null); this.differenceOnly.set(false);
    this.variance.set(null); this.api.get(row.id).subscribe({ next: detail => this.names(detail.lines).subscribe({ next: () => {
      this.detail.set(detail); this.loadVariance(detail.id, print); }, error: e => this.error.set(this.api.errorKey(e)) }),
    error: e => this.error.set(this.api.errorKey(e)) }); }
  private loadVariance(id: number, print = false): void { this.api.variance(id, this.differenceOnly()).subscribe({ next: report => {
    if (this.detail()?.id !== id) return; this.variance.set(report); if (print) setTimeout(() => window.print());
  }, error: e => this.error.set(this.api.errorKey(e)) }); }
  toggleDifference(value: boolean): void { this.differenceOnly.set(value); if (this.detail()) this.loadVariance(this.detail()!.id); }
  print(row: StocktakeSummary): void { this.view(row, true); }
  submit(row: StocktakeSummary): void { if (this.busy()) return; this.busy.set(true);
    this.api.submit(row.id, row.version).subscribe({ next: () => { this.busy.set(false); this.load(); },
      error: e => { this.busy.set(false); this.load(); this.error.set(this.api.errorKey(e)); } }); }
  remove(row: StocktakeSummary): void { if (this.busy()) return;
    this.dialogs.confirm({ titleKey: 'inv.stocktake.confirmDelete', danger: true, okKey: 'inv.stocktake.delete' }).subscribe(confirmed => {
      if (!confirmed) return; this.busy.set(true); this.api.remove(row.id, row.version).subscribe({
        next: () => { this.busy.set(false); this.load(); }, error: e => { this.busy.set(false); this.load(); this.error.set(this.api.errorKey(e)); } }); }); }
  exportCsv(): void { if (this.exporting() || !this.permissions.has('INV.STOCKTAKE.VIEW')) return;
    if (this.from && this.to && this.to < this.from) { this.error.set('inv.stocktake.invalidDates'); return; }
    this.exporting.set(true); this.error.set(null); const fail = (e: unknown) => { this.error.set(this.api.errorKey(e)); this.exporting.set(false); };
    fetchAllPages(page => this.api.list(this.query(page, OPTION_PAGE_SIZE))).subscribe({ next: rows => {
      from(rows.map((row, index) => ({ row, index }))).pipe(mergeMap(({ row, index }) =>
        this.api.variance(row.id, false).pipe(map(report => ({ index, report }))), 4), toArray()).subscribe({ next: reports => {
        this.names(reports.flatMap(x => x.report.lines)).subscribe({ next: () => {
        const data = [['Document No', 'Business Date', 'Status', 'Line', 'Item', 'Warehouse', 'Location', 'Book Quantity', 'Counted Quantity', 'Difference', 'Gain Quantity', 'Loss Quantity', 'Type', 'Tracking', 'Remark'],
          ...reports.sort((a, b) => a.index - b.index).flatMap(({ index, report }) => report.lines.map(line => [
            report.documentNo, report.businessDate, report.status, line.lineNo, this.itemName(line.itemId),
            this.warehouseName(line.warehouseId), this.locationName(line.warehouseId, line.locationId), line.bookQuantity,
            line.countedQuantity ?? '', line.difference ?? '', line.gainQuantity, line.lossQuantity, line.type,
            line.tracking.map(t => `${t.lotNo ?? t.serialNo}: ${t.bookQuantity} -> ${t.countedQuantity ?? ''} (${t.difference ?? ''})`).join('; '), rows[index].remark ?? ''
          ]))];
        const blob = new Blob(['\ufeff', data.map(line => line.map(csvCell).join(',')).join('\r\n')], { type: 'text/csv;charset=utf-8' });
        const url = URL.createObjectURL(blob); const link = document.createElement('a'); link.href = url; link.download = 'inventory-stocktakes.csv'; link.click(); URL.revokeObjectURL(url); this.exporting.set(false);
        }, error: fail });
      }, error: fail }); }, error: fail }); }
}
