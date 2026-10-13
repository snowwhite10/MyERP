import { ChangeDetectionStrategy, Component, effect, inject, signal, untracked } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { TranslatePipe } from '@ngx-translate/core';
import { NzAlertModule } from 'ng-zorro-antd/alert';
import { NzButtonModule } from 'ng-zorro-antd/button';
import { NzTableModule, NzTableQueryParams } from 'ng-zorro-antd/table';
import { forkJoin, map, Observable, of, switchMap } from 'rxjs';
import { PermissionService } from '../../core/auth/permission.service';
import { CompanyContextService } from '../../core/company/company-context.service';
import { fetchAllPages, OPTION_PAGE_SIZE } from '../../shared/list-page/fetch-all-pages';
import { PagedResult } from '../../shared/list-page/table-query';
import { Item, ItemService } from '../md/item.service';
import { StorageLocation, Warehouse, WarehouseService } from '../md/warehouse.service';
import { BalanceRow, MovementRow, StockFilters, StockQueryService, stockCsvCell } from './stock-query.service';

type Tab = 'balances' | 'movements';

@Component({
  selector: 'erp-stock-query-page',
  imports: [FormsModule, TranslatePipe, NzAlertModule, NzButtonModule, NzTableModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <section class="stock-query" data-testid="inv-stock-query"><h1>{{ 'inv.query.title' | translate }}</h1>
      <nav aria-label="Stock query">
        @if (canBalances()) { <button nz-button [nzType]="tab() === 'balances' ? 'primary' : 'default'" (click)="selectTab('balances')" data-testid="inv-query-balances">{{ 'inv.query.balances' | translate }}</button> }
        @if (canMovements()) { <button nz-button [nzType]="tab() === 'movements' ? 'primary' : 'default'" (click)="selectTab('movements')" data-testid="inv-query-movements">{{ 'inv.query.movements' | translate }}</button> }
      </nav>
      <div class="filters">
        <select [(ngModel)]="itemId" [attr.aria-label]="'inv.query.item' | translate" data-testid="inv-query-item"><option [ngValue]="null">{{ 'inv.query.allItems' | translate }}</option>@for (item of items(); track item.id) { <option [ngValue]="item.id">{{ item.code }} · {{ item.name }}</option> }</select>
        <select [(ngModel)]="warehouseId" (ngModelChange)="onWarehouseChange()" [attr.aria-label]="'inv.query.warehouse' | translate" data-testid="inv-query-warehouse"><option [ngValue]="null">{{ 'inv.query.allWarehouses' | translate }}</option>@for (warehouse of warehouses(); track warehouse.id) { <option [ngValue]="warehouse.id">{{ warehouse.code }} · {{ warehouse.name }}</option> }</select>
        <select [(ngModel)]="locationId" [disabled]="!warehouseId" [attr.aria-label]="'inv.query.location' | translate"><option [ngValue]="null">{{ 'inv.query.allLocations' | translate }}</option>@for (location of locations(); track location.id) { <option [ngValue]="location.id">{{ location.code }} · {{ location.name }}</option> }</select>
        @if (tab() === 'movements') {
          <input type="date" [(ngModel)]="from" [attr.aria-label]="'inv.query.from' | translate" />
          <input type="date" [(ngModel)]="to" [attr.aria-label]="'inv.query.to' | translate" />
          <select [(ngModel)]="direction" [attr.aria-label]="'inv.query.direction' | translate"><option value="">{{ 'inv.query.allDirections' | translate }}</option><option value="In">{{ 'inv.query.in' | translate }}</option><option value="Out">{{ 'inv.query.out' | translate }}</option></select>
        }
        <button nz-button (click)="search()" data-testid="inv-query-search">{{ 'inv.query.search' | translate }}</button>
        <button nz-button (click)="print()" [disabled]="exporting()">{{ 'inv.query.print' | translate }}</button>
        <button nz-button (click)="exportCsv()" [disabled]="exporting()" data-testid="inv-query-export">{{ 'inv.query.export' | translate }}</button>
      </div>
      @if (error()) { <nz-alert nzType="error" nzShowIcon [nzMessage]="error()! | translate" /> }
      <nz-table #table [nzData]="rows()" [nzLoading]="loading()" [nzFrontPagination]="false" [nzTotal]="total()"
        [nzPageIndex]="page()" [nzPageSize]="pageSize()" (nzQueryParams)="onQuery($event)" [nzNoResult]="'inv.query.empty' | translate" data-testid="inv-query-table">
        <thead><tr><th>{{ 'inv.query.item' | translate }}</th><th>{{ 'inv.query.warehouse' | translate }}</th><th>{{ 'inv.query.location' | translate }}</th>
          @if (tab() === 'movements') { <th>{{ 'inv.query.date' | translate }}</th><th>{{ 'inv.query.source' | translate }}</th><th>{{ 'inv.query.direction' | translate }}</th> }
          <th>{{ 'inv.query.quantity' | translate }}</th>@if (tab() === 'movements') { <th>{{ 'inv.query.unitCost' | translate }}</th> }</tr></thead>
        <tbody>@for (row of table.data; track tab() === 'balances' ? row.itemId + '-' + row.warehouseId + '-' + row.locationId : movement(row).id) {
          <tr><td>{{ itemName(row.itemId) }}</td><td>{{ warehouseName(row.warehouseId) }}</td><td>{{ locationName(row.warehouseId, row.locationId) }}</td>
          @if (tab() === 'movements') { <td>{{ movement(row).businessDate }}</td><td>{{ movement(row).sourceType }} #{{ movement(row).sourceId }}/{{ movement(row).sourceLineNo }}</td><td>{{ directionKey(movement(row).direction) | translate }}</td> }
          <td>{{ row.quantity }}</td>@if (tab() === 'movements') { <td>{{ movement(row).unitCost ?? '—' }}</td> }</tr>
        }</tbody>
      </nz-table>
      @if (printRows(); as printable) { <section class="print-sheet"><h2>{{ titleKey() | translate }}</h2><table><thead><tr><th>{{ 'inv.query.item' | translate }}</th><th>{{ 'inv.query.warehouse' | translate }}</th><th>{{ 'inv.query.location' | translate }}</th><th>{{ 'inv.query.date' | translate }}</th><th>{{ 'inv.query.source' | translate }}</th><th>{{ 'inv.query.direction' | translate }}</th><th>{{ 'inv.query.quantity' | translate }}</th></tr></thead><tbody>@for (row of printable; track $index) { <tr><td>{{ itemName(row.itemId) }}</td><td>{{ warehouseName(row.warehouseId) }}</td><td>{{ locationName(row.warehouseId, row.locationId) }}</td><td>{{ isMovement(row) ? row.businessDate : '' }}</td><td>{{ isMovement(row) ? row.sourceType + ' #' + row.sourceId : '' }}</td><td>{{ isMovement(row) ? (directionKey(row.direction) | translate) : '' }}</td><td>{{ row.quantity }}</td></tr> }</tbody></table></section> }
    </section>
  `,
  styles: `.stock-query{display:grid;gap:16px;color:var(--erp-color-text)}h1{font-size:20px;margin:0}nav,.filters{display:flex;gap:8px;flex-wrap:wrap}select,input{min-width:130px}.print-sheet{display:none}@media print{.stock-query>h1,nav,.filters,nz-table,nz-alert{display:none!important}.print-sheet{display:block}.print-sheet table{width:100%;border-collapse:collapse}.print-sheet td,.print-sheet th{border:1px solid #777;padding:4px}}`,
})
export class StockQueryPage {
  private readonly api = inject(StockQueryService);
  private readonly itemApi = inject(ItemService);
  private readonly warehouseApi = inject(WarehouseService);
  private readonly permissions = inject(PermissionService);
  private readonly company = inject(CompanyContextService);
  readonly canBalances = () => this.permissions.has('INV.INVENTORY.VIEW');
  readonly canMovements = () => this.permissions.has('INV.MOVEMENT.VIEW');
  readonly tab = signal<Tab>(this.canBalances() ? 'balances' : 'movements');
  readonly rows = signal<(BalanceRow | MovementRow)[]>([]);
  readonly printRows = signal<(BalanceRow | MovementRow)[] | null>(null);
  readonly items = signal<Item[]>([]);
  readonly warehouses = signal<Warehouse[]>([]);
  readonly locations = signal<StorageLocation[]>([]);
  readonly locationMap = signal<Record<number, StorageLocation[]>>({});
  readonly page = signal(1); readonly pageSize = signal(20); readonly total = signal(0);
  readonly loading = signal(false); readonly exporting = signal(false); readonly error = signal<string | null>(null);
  itemId: number | null = null; warehouseId: number | null = null; locationId: number | null = null;
  from = ''; to = ''; direction = '';
  private generation = 0;

  constructor() {
    effect(() => { this.company.current(); untracked(() => { this.itemId = null; this.warehouseId = null; this.locationId = null;
      this.items.set([]); this.warehouses.set([]); this.locations.set([]); this.locationMap.set({}); this.page.set(1); this.loadNames(); this.load(); }); });
  }
  titleKey(): string { return this.tab() === 'balances' ? 'inv.query.balances' : 'inv.query.movements'; }
  directionKey(direction: 'In' | 'Out'): string { return direction === 'In' ? 'inv.query.in' : 'inv.query.out'; }
  isMovement(row: BalanceRow | MovementRow): row is MovementRow { return 'sourceType' in row; }
  movement(row: BalanceRow | MovementRow): MovementRow { return row as MovementRow; }
  itemName(id: number): string { const value = this.items().find(x => x.id === id); return value ? `${value.code} · ${value.name}` : String(id); }
  warehouseName(id: number): string { const value = this.warehouses().find(x => x.id === id); return value ? `${value.code} · ${value.name}` : String(id); }
  locationName(warehouseId: number, locationId: number | null): string {
    if (!locationId) return '—'; const value = this.locationMap()[warehouseId]?.find(x => x.id === locationId);
    return value ? `${value.code} · ${value.name}` : String(locationId);
  }
  private loadNames(): void {
    forkJoin({ items: fetchAllPages(page => this.itemApi.list({ page, pageSize: OPTION_PAGE_SIZE })),
      warehouses: fetchAllPages(page => this.warehouseApi.list({ page, pageSize: OPTION_PAGE_SIZE })) }).subscribe({
      next: names => { this.items.set(names.items); this.warehouses.set(names.warehouses); },
      error: e => this.error.set(this.api.errorKey(e)),
    });
  }
  onWarehouseChange(): void { this.locationId = null; this.locations.set([]); if (this.warehouseId) this.loadLocations(this.warehouseId); }
  private loadLocations(id: number): void {
    if (this.locationMap()[id]) { if (this.warehouseId === id) this.locations.set(this.locationMap()[id]); return; }
    fetchAllPages(page => this.warehouseApi.listLocations(id, { page, pageSize: OPTION_PAGE_SIZE })).subscribe({
      next: rows => { this.locationMap.update(map => ({ ...map, [id]: rows })); if (this.warehouseId === id) this.locations.set(rows); },
      error: e => this.error.set(this.api.errorKey(e)),
    });
  }
  private loadRowLocations(rows: (BalanceRow | MovementRow)[]): void {
    for (const id of new Set(rows.filter(x => x.locationId).map(x => x.warehouseId))) this.loadLocations(id);
  }
  selectTab(tab: Tab): void { if (tab === 'balances' && !this.canBalances() || tab === 'movements' && !this.canMovements()) return;
    this.tab.set(tab); this.page.set(1); this.printRows.set(null); this.load(); }
  onQuery(query: NzTableQueryParams): void { if (query.pageIndex !== this.page() || query.pageSize !== this.pageSize()) {
    this.page.set(query.pageIndex); this.pageSize.set(query.pageSize); this.load(); } }
  search(): void { if (this.from && this.to && this.from > this.to && this.tab() === 'movements') { this.error.set('inv.query.invalidDates'); return; }
    this.page.set(1); this.load(); }
  private filters(page: number, pageSize: number): StockFilters { return { page, pageSize,
    itemId: this.itemId ?? undefined, warehouseId: this.warehouseId ?? undefined, locationId: this.locationId ?? undefined,
    from: this.from || undefined, to: this.to || undefined, direction: (this.direction || undefined) as 'In' | 'Out' | undefined }; }
  private request(tab: Tab, page: number, size: number): Observable<PagedResult<BalanceRow | MovementRow>> {
    return tab === 'balances' ? this.api.balances(this.filters(page, size)) : this.api.movements(this.filters(page, size));
  }
  load(): void { const generation = ++this.generation; const tab = this.tab(); this.loading.set(true); this.error.set(null);
    this.request(tab, this.page(), this.pageSize()).subscribe({ next: result => {
      if (generation !== this.generation) return; this.rows.set(result.items); this.total.set(result.totalCount); this.loading.set(false); this.loadRowLocations(result.items);
    }, error: e => { if (generation === this.generation) { this.error.set(this.api.errorKey(e)); this.loading.set(false); } } }); }
  private allRows(): Observable<(BalanceRow | MovementRow)[]> { const tab = this.tab();
    const filters = this.filters(1, OPTION_PAGE_SIZE);
    return fetchAllPages(page => tab === 'balances' ? this.api.balances({ ...filters, page }) : this.api.movements({ ...filters, page })); }
  private snapshot(): Observable<(BalanceRow | MovementRow)[]> {
    return forkJoin({ rows: this.allRows(),
      items: fetchAllPages(page => this.itemApi.list({ page, pageSize: OPTION_PAGE_SIZE })),
      warehouses: fetchAllPages(page => this.warehouseApi.list({ page, pageSize: OPTION_PAGE_SIZE })) }).pipe(
      switchMap(({ rows, items, warehouses }) => {
        const ids = [...new Set(rows.filter(row => row.locationId).map(row => row.warehouseId))];
        const locations = ids.length ? forkJoin(ids.map(id => fetchAllPages(page => this.warehouseApi.listLocations(id, { page, pageSize: OPTION_PAGE_SIZE })))) : of([] as StorageLocation[][]);
        return locations.pipe(map(groups => ({ rows, items, warehouses, ids, groups })));
      }),
      map(({ rows, items, warehouses, ids, groups }) => {
        this.items.set(items); this.warehouses.set(warehouses);
        this.locationMap.set(Object.fromEntries(ids.map((id, index) => [id, groups[index]])));
        return rows;
      }),
    );
  }
  print(): void { if (this.exporting()) return; this.exporting.set(true); this.error.set(null);
    this.snapshot().subscribe({ next: rows => { this.printRows.set(rows);
      // Give Angular a rendering turn after all MD names are available.
      setTimeout(() => { window.print(); this.exporting.set(false); });
    }, error: e => { this.error.set(this.api.errorKey(e)); this.exporting.set(false); } }); }
  exportCsv(): void { if (this.exporting()) return; this.exporting.set(true); this.error.set(null);
    const tab = this.tab(); this.snapshot().subscribe({ next: rows => {
      const header = ['Item', 'Warehouse', 'Location', 'Quantity'];
      if (tab === 'movements') header.push('Business Date', 'Source Type', 'Source ID', 'Source Line', 'Direction', 'Unit Cost');
      const data = [header, ...rows.map(row => { const base: (string | number | null)[] = [this.itemName(row.itemId), this.warehouseName(row.warehouseId), this.locationName(row.warehouseId, row.locationId), row.quantity];
        if (this.isMovement(row)) base.push(row.businessDate, row.sourceType, row.sourceId, row.sourceLineNo, row.direction, row.unitCost);
        return base; })];
      const blob = new Blob(['\ufeff', data.map(line => line.map(stockCsvCell).join(',')).join('\r\n')], { type: 'text/csv;charset=utf-8' });
      const url = URL.createObjectURL(blob); const link = document.createElement('a'); link.href = url;
      link.download = `inventory-${tab}.csv`; link.click(); URL.revokeObjectURL(url); this.exporting.set(false);
    }, error: e => { this.error.set(this.api.errorKey(e)); this.exporting.set(false); } }); }
}
