import { ChangeDetectionStrategy, Component, effect, inject, signal, untracked } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { TranslatePipe } from '@ngx-translate/core';
import { NzAlertModule } from 'ng-zorro-antd/alert';
import { NzButtonModule } from 'ng-zorro-antd/button';
import { NzTableModule, NzTableQueryParams } from 'ng-zorro-antd/table';
import { NzTagModule } from 'ng-zorro-antd/tag';
import { forkJoin, from, mergeMap, toArray } from 'rxjs';
import { HasPermissionDirective } from '../../core/auth/has-permission.directive';
import { PermissionService } from '../../core/auth/permission.service';
import { CompanyContextService } from '../../core/company/company-context.service';
import { ErpDialogService } from '../../shared/dialogs/dialog.service';
import { fetchAllPages, OPTION_PAGE_SIZE } from '../../shared/list-page/fetch-all-pages';
import { Item, ItemService } from '../md/item.service';
import { StorageLocation, Warehouse, WarehouseService } from '../md/warehouse.service';
import { OtherReceiptService, ReceiptDetail, ReceiptStatus, ReceiptSummary } from './other-receipt.service';
import { trackingLabel } from './tracking';

/** 其他入庫列表、篩選、送審、列印與供 Excel 開啟的 UTF-8 CSV（ADR-056）。 */
@Component({
  selector: 'erp-other-receipt-page',
  imports: [FormsModule, RouterLink, TranslatePipe, NzAlertModule, NzButtonModule, NzTableModule, NzTagModule, HasPermissionDirective],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <section class="receipts"><header><h1>{{ 'inv.receipt.title' | translate }}</h1>
      <a nz-button nzType="primary" routerLink="new" *erpHasPermission="'INV.OTHER_RECEIPT.CREATE'" data-testid="inv-receipt-new">{{ 'inv.receipt.new' | translate }}</a></header>
      <div class="filters">
        <input type="search" [(ngModel)]="keyword" [attr.aria-label]="'inv.receipt.search' | translate" data-testid="inv-receipt-search" (keyup.enter)="search()" />
        <input type="date" [(ngModel)]="from" [attr.aria-label]="'inv.receipt.from' | translate" />
        <input type="date" [(ngModel)]="to" [attr.aria-label]="'inv.receipt.to' | translate" />
        <select [(ngModel)]="status" [attr.aria-label]="'inv.receipt.status' | translate">
          <option value="">{{ 'inv.receipt.all' | translate }}</option>
          <option value="Draft">{{ 'inv.receipt.draft' | translate }}</option><option value="PendingApproval">{{ 'inv.receipt.pending' | translate }}</option>
          <option value="Rejected">{{ 'inv.receipt.rejected' | translate }}</option><option value="Posted">{{ 'inv.receipt.posted' | translate }}</option>
        </select>
        <button nz-button (click)="search()">{{ 'inv.receipt.search' | translate }}</button>
        <button nz-button (click)="exportCsv()" [disabled]="exporting()">{{ 'inv.receipt.export' | translate }}</button>
      </div>
      @if (error()) { <nz-alert nzType="error" nzShowIcon [nzMessage]="error()! | translate" /> }
      <nz-table #table [nzData]="rows()" [nzLoading]="loading()" [nzFrontPagination]="false"
        [nzTotal]="total()" [nzPageIndex]="page()" [nzPageSize]="pageSize()" (nzQueryParams)="onQuery($event)"
        [nzNoResult]="'inv.receipt.empty' | translate" data-testid="inv-receipt-table">
        <thead><tr><th>{{ 'inv.receipt.number' | translate }}</th><th>{{ 'inv.receipt.date' | translate }}</th>
          <th>{{ 'inv.receipt.status' | translate }}</th><th>{{ 'inv.receipt.quantity' | translate }}</th>
          <th>{{ 'inv.receipt.amount' | translate }}</th><th>{{ 'inv.receipt.lines' | translate }}</th><th></th></tr></thead>
        <tbody>@for (row of table.data; track row.id) { <tr [attr.data-testid]="'inv-receipt-row-' + row.id">
          <td><button nz-button nzType="link" (click)="view(row)">{{ row.documentNo }}</button></td>
          <td>{{ row.businessDate }}</td><td><nz-tag [nzColor]="statusColor(row.status)">{{ statusKey(row.status) | translate }}</nz-tag></td>
          <td>{{ row.totalQuantity }}</td><td>{{ row.totalAmount }}</td><td>{{ row.lineCount }}</td><td>
          @if (row.status === 'Draft' || row.status === 'Rejected') {
            <a nz-button nzType="link" *erpHasPermission="'INV.OTHER_RECEIPT.EDIT'" [routerLink]="[row.id, 'edit']">{{ 'inv.receipt.edit' | translate }}</a>
          }
          @if (row.status === 'Draft') {
            <button nz-button nzType="link" *erpHasPermission="'INV.OTHER_RECEIPT.SUBMIT'" [disabled]="busy()" (click)="submit(row)">{{ 'inv.receipt.submit' | translate }}</button>
            <button nz-button nzType="link" nzDanger *erpHasPermission="'INV.OTHER_RECEIPT.DELETE'" [disabled]="busy()" (click)="remove(row)">{{ 'inv.receipt.delete' | translate }}</button>
          }
          <button nz-button nzType="link" (click)="print(row)">{{ 'inv.receipt.print' | translate }}</button></td>
        </tr>}</tbody>
      </nz-table>
      @if (detail(); as d) { <section class="detail" data-testid="inv-receipt-detail"><button nz-button (click)="detail.set(null)">×</button>
        <h2>{{ d.documentNo }} · {{ statusKey(d.status) | translate }}</h2><p>{{ d.businessDate }} · {{ d.remark }}</p>
        <table><thead><tr><th>{{ 'inv.receipt.item' | translate }}</th><th>{{ 'inv.receipt.warehouse' | translate }}</th>
          <th>{{ 'inv.receipt.location' | translate }}</th><th>{{ 'inv.receipt.quantity' | translate }}</th><th>{{ 'inv.receipt.unitCost' | translate }}</th><th>{{ 'inv.receipt.amount' | translate }}</th><th>{{ 'inv.trace.title' | translate }}</th></tr></thead>
          <tbody>@for (line of d.lines; track line.id) { <tr><td>{{ itemName(line.itemId) }}</td><td>{{ warehouseName(line.warehouseId) }}</td><td>{{ locationName(line.warehouseId, line.locationId) }}</td>
            <td>{{ line.quantity }}</td><td>{{ line.unitCost }}</td><td>{{ line.amount }}</td><td>{{ trackingLabel(line.tracking) }}</td></tr> }</tbody></table></section> }
    </section>
  `,
  styles: `.receipts{display:grid;gap:16px;color:var(--erp-color-text)}header,.filters{display:flex;gap:8px;align-items:center;flex-wrap:wrap}header{justify-content:space-between}h1{font-size:20px;margin:0}.detail{padding:16px;border:1px solid var(--erp-color-border)}.detail table{width:100%}@media print{header,.filters,nz-table,.detail>button{display:none!important}.detail{border:0}}`,
})
export class OtherReceiptPage {
  readonly trackingLabel = trackingLabel;
  private readonly api = inject(OtherReceiptService);
  private readonly itemApi = inject(ItemService);
  private readonly warehouseApi = inject(WarehouseService);
  private readonly company = inject(CompanyContextService);
  private readonly permissions = inject(PermissionService);
  private readonly dialogs = inject(ErpDialogService);
  readonly rows = signal<ReceiptSummary[]>([]);
  readonly detail = signal<ReceiptDetail | null>(null);
  readonly total = signal(0); readonly page = signal(1); readonly pageSize = signal(20);
  readonly loading = signal(false); readonly busy = signal(false); readonly exporting = signal(false);
  readonly error = signal<string | null>(null);
  readonly items = signal<Item[]>([]); readonly warehouses = signal<Warehouse[]>([]);
  readonly locationMap = signal<Record<number, StorageLocation[]>>({});
  keyword = ''; from = ''; to = ''; status = '';
  private generation = 0;
  constructor() { effect(() => { this.company.current(); untracked(() => { this.detail.set(null); this.search(); }); }); }
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
  private loadLocations(detail: ReceiptDetail): void {
    for (const id of new Set(detail.lines.map(x => x.warehouseId))) {
      if (this.locationMap()[id]) continue;
      fetchAllPages(page => this.warehouseApi.listLocations(id, { page, pageSize: OPTION_PAGE_SIZE })).subscribe({
        next: rows => this.locationMap.update(map => ({ ...map, [id]: rows })),
        error: e => this.error.set(this.api.errorKey(e)),
      });
    }
  }
  statusKey(status: ReceiptStatus): string { return 'inv.receipt.' + ({ Draft: 'draft', PendingApproval: 'pending', Rejected: 'rejected', Posted: 'posted' }[status] ?? 'draft'); }
  statusColor(status: ReceiptStatus): string { return { Draft: 'default', PendingApproval: 'processing', Rejected: 'error', Posted: 'success' }[status] ?? 'default'; }
  onQuery(query: NzTableQueryParams): void { if (query.pageIndex !== this.page() || query.pageSize !== this.pageSize()) { this.page.set(query.pageIndex); this.pageSize.set(query.pageSize); this.load(); } }
  search(): void { this.page.set(1); this.load(); }
  private query(page: number, pageSize: number) { return { page, pageSize, keyword: this.keyword, from: this.from, to: this.to, status: this.status }; }
  load(): void {
    const current = ++this.generation; this.loading.set(true); this.error.set(null);
    this.api.list(this.query(this.page(), this.pageSize())).subscribe({ next: result => {
      if (current !== this.generation) return; this.rows.set(result.items); this.total.set(result.totalCount); this.loading.set(false);
    }, error: e => { if (current === this.generation) { this.error.set(this.api.errorKey(e)); this.loading.set(false); } } });
  }
  view(row: ReceiptSummary): void { this.api.get(row.id).subscribe({ next: d => { this.detail.set(d); this.loadNames(); this.loadLocations(d); }, error: e => this.error.set(this.api.errorKey(e)) }); }
  print(row: ReceiptSummary): void {
    this.api.get(row.id).subscribe({ next: d => {
      const warehouseIds = [...new Set(d.lines.map(x => x.warehouseId))];
      forkJoin({ items: fetchAllPages(page => this.itemApi.list({ page, pageSize: OPTION_PAGE_SIZE })),
        warehouses: fetchAllPages(page => this.warehouseApi.list({ page, pageSize: OPTION_PAGE_SIZE })),
        locations: forkJoin(warehouseIds.map(id => fetchAllPages(page => this.warehouseApi.listLocations(id, { page, pageSize: OPTION_PAGE_SIZE })))) }).subscribe({
        next: names => { this.items.set(names.items); this.warehouses.set(names.warehouses);
          this.locationMap.set(Object.fromEntries(warehouseIds.map((id, i) => [id, names.locations[i]])));
          this.detail.set(d); setTimeout(() => window.print()); },
        error: e => this.error.set(this.api.errorKey(e)),
      });
    }, error: e => this.error.set(this.api.errorKey(e)) });
  }
  submit(row: ReceiptSummary): void { if (this.busy()) return; this.busy.set(true); this.api.submit(row.id, row.version).subscribe({
    next: () => { this.busy.set(false); this.load(); }, error: e => { this.busy.set(false); this.load(); this.error.set(this.api.errorKey(e)); },
  }); }
  remove(row: ReceiptSummary): void {
    if (this.busy()) return;
    this.dialogs.confirm({ titleKey: 'inv.receipt.confirmDelete', danger: true, okKey: 'inv.receipt.delete' }).subscribe(confirmed => {
      if (!confirmed) return;
      this.busy.set(true); this.api.remove(row.id, row.version).subscribe({
        next: () => { this.busy.set(false); this.load(); }, error: e => { this.busy.set(false); this.load(); this.error.set(this.api.errorKey(e)); },
      });
    });
  }
  exportCsv(): void {
    if (this.exporting() || !this.permissions.has('INV.OTHER_RECEIPT.VIEW')) return;
    this.exporting.set(true); this.error.set(null);
    fetchAllPages(page => this.api.list(this.query(page, OPTION_PAGE_SIZE))).subscribe({ next: rows => {
      from(rows.map((row, index) => ({ row, index }))).pipe(mergeMap(({ row, index }) =>
        this.api.get(row.id).pipe(mergeMap(detail => [{ index, detail }])), 4), toArray()).subscribe({ next: details => {
      const safe = (value: string | number | null): string => {
        let text = String(value ?? '');
        if (typeof value === 'string' && /^[\s]*[=+\-@]/.test(text)) text = "'" + text;
        return '"' + text.replaceAll('"', '""') + '"';
      };
      const data = [['Document No', 'Business Date', 'Status', 'Line', 'Item ID', 'Warehouse ID', 'Location ID', 'Quantity', 'Unit Cost', 'Amount', 'Tracking', 'Remark'],
        ...details.sort((a, b) => a.index - b.index).flatMap(({ detail: d }) => d.lines.map(line =>
          [d.documentNo, d.businessDate, d.status, line.lineNo, line.itemId, line.warehouseId, line.locationId ?? '',
            line.quantity, line.unitCost, line.amount, trackingLabel(line.tracking), d.remark ?? '']))];
      const blob = new Blob(['\ufeff', data.map(line => line.map(safe).join(',')).join('\r\n')], { type: 'text/csv;charset=utf-8' });
      const url = URL.createObjectURL(blob); const link = document.createElement('a'); link.href = url;
      link.download = 'other-receipts.csv'; link.click(); URL.revokeObjectURL(url); this.exporting.set(false);
      }, error: e => { this.error.set(this.api.errorKey(e)); this.exporting.set(false); } });
    }, error: e => { this.error.set(this.api.errorKey(e)); this.exporting.set(false); } });
  }
}
