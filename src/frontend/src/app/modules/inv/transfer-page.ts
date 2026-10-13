import { ChangeDetectionStrategy, Component, effect, inject, signal, untracked } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { TranslatePipe } from '@ngx-translate/core';
import { NzAlertModule } from 'ng-zorro-antd/alert';
import { NzButtonModule } from 'ng-zorro-antd/button';
import { NzTableModule, NzTableQueryParams } from 'ng-zorro-antd/table';
import { NzTagModule } from 'ng-zorro-antd/tag';
import { forkJoin, from, map, mergeMap, Observable, of, toArray } from 'rxjs';
import { HasPermissionDirective } from '../../core/auth/has-permission.directive';
import { PermissionService } from '../../core/auth/permission.service';
import { CompanyContextService } from '../../core/company/company-context.service';
import { ErpDialogService } from '../../shared/dialogs/dialog.service';
import { fetchAllPages, OPTION_PAGE_SIZE } from '../../shared/list-page/fetch-all-pages';
import { Item, ItemService } from '../md/item.service';
import { StorageLocation, Warehouse, WarehouseService } from '../md/warehouse.service';
import { TransferDetail, TransferService, TransferStatus, TransferSummary } from './transfer.service';
import { trackingLabel } from './tracking';

/** 介面 5.16：調撥列表、篩選、詳情、送審／刪除、列印與供 Excel 開啟的安全 UTF-8 CSV（ADR-056、ADR-061）。 */
@Component({
  selector: 'erp-transfer-page',
  imports: [FormsModule, RouterLink, TranslatePipe, NzAlertModule, NzButtonModule, NzTableModule, NzTagModule, HasPermissionDirective],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <section class="transfers"><header><h1>{{ 'inv.transfer.title' | translate }}</h1>
      <a nz-button nzType="primary" routerLink="new" *erpHasPermission="'INV.TRANSFER.CREATE'" data-testid="inv-transfer-new">{{ 'inv.transfer.new' | translate }}</a></header>
      <div class="filters">
        <input type="search" [(ngModel)]="keyword" [attr.aria-label]="'inv.transfer.keyword' | translate" data-testid="inv-transfer-search" (keyup.enter)="search()" />
        <input type="date" [(ngModel)]="from" [attr.aria-label]="'inv.transfer.from' | translate" data-testid="inv-transfer-from" />
        <input type="date" [(ngModel)]="to" [attr.aria-label]="'inv.transfer.to' | translate" data-testid="inv-transfer-to" />
        <select [(ngModel)]="status" [attr.aria-label]="'inv.transfer.status' | translate" data-testid="inv-transfer-status">
          <option value="">{{ 'inv.transfer.all' | translate }}</option>
          <option value="Draft">{{ 'inv.transfer.draft' | translate }}</option><option value="PendingApproval">{{ 'inv.transfer.pending' | translate }}</option>
          <option value="Rejected">{{ 'inv.transfer.rejected' | translate }}</option><option value="Posted">{{ 'inv.transfer.posted' | translate }}</option>
        </select>
        <button nz-button (click)="search()">{{ 'inv.transfer.search' | translate }}</button>
        <button nz-button (click)="exportCsv()" [disabled]="exporting()" data-testid="inv-transfer-export">{{ 'inv.transfer.export' | translate }}</button>
      </div>
      @if (error()) { <nz-alert nzType="error" nzShowIcon [nzMessage]="error()! | translate" data-testid="inv-transfer-error" /> }
      <nz-table #table [nzData]="rows()" [nzLoading]="loading()" [nzFrontPagination]="false"
        [nzTotal]="total()" [nzPageIndex]="page()" [nzPageSize]="pageSize()" (nzQueryParams)="onQuery($event)"
        [nzNoResult]="'inv.transfer.empty' | translate" data-testid="inv-transfer-table">
        <thead><tr><th>{{ 'inv.transfer.number' | translate }}</th><th>{{ 'inv.transfer.date' | translate }}</th>
          <th>{{ 'inv.transfer.status' | translate }}</th><th>{{ 'inv.transfer.quantity' | translate }}</th>
          <th>{{ 'inv.transfer.lines' | translate }}</th><th>{{ 'inv.transfer.remark' | translate }}</th><th></th></tr></thead>
        <tbody>@for (row of table.data; track row.id) { <tr [attr.data-testid]="'inv-transfer-row-' + row.id">
          <td><button nz-button nzType="link" (click)="view(row)">{{ row.documentNo }}</button></td>
          <td>{{ row.businessDate }}</td><td><nz-tag [nzColor]="statusColor(row.status)">{{ statusKey(row.status) | translate }}</nz-tag></td>
          <td>{{ row.totalQuantity }}</td><td>{{ row.lineCount }}</td><td>{{ row.remark }}</td><td>
          @if (row.status === 'Draft' || row.status === 'Rejected') {
            <a nz-button nzType="link" *erpHasPermission="'INV.TRANSFER.EDIT'" [routerLink]="[row.id, 'edit']">{{ 'inv.transfer.edit' | translate }}</a>
          }
          @if (row.status === 'Draft') {
            <button nz-button nzType="link" *erpHasPermission="'INV.TRANSFER.SUBMIT'" [disabled]="busy()" (click)="submit(row)">{{ 'inv.transfer.submit' | translate }}</button>
            <button nz-button nzType="link" nzDanger *erpHasPermission="'INV.TRANSFER.DELETE'" [disabled]="busy()" (click)="remove(row)">{{ 'inv.transfer.delete' | translate }}</button>
          }
          <button nz-button nzType="link" (click)="print(row)">{{ 'inv.transfer.print' | translate }}</button></td>
        </tr>}</tbody>
      </nz-table>
      @if (detail(); as d) { <section class="detail" data-testid="inv-transfer-detail"><button nz-button (click)="detail.set(null)" [attr.aria-label]="'inv.transfer.close' | translate">×</button>
        <h2>{{ d.documentNo }} · {{ statusKey(d.status) | translate }}</h2><p>{{ d.businessDate }} · {{ d.remark }}</p>
        @if (d.approval; as approval) { <p>{{ 'inv.transfer.approval' | translate }}: {{ approval.status }}</p> }
        <table><thead><tr><th>#</th><th>{{ 'inv.transfer.item' | translate }}</th><th>{{ 'inv.transfer.source' | translate }}</th>
          <th>{{ 'inv.transfer.destination' | translate }}</th><th>{{ 'inv.transfer.quantity' | translate }}</th><th>{{ 'inv.trace.title' | translate }}</th></tr></thead>
          <tbody>@for (line of d.lines; track line.id) { <tr><td>{{ line.lineNo }}</td><td>{{ itemName(line.itemId) }}</td>
            <td>{{ warehouseName(line.sourceWarehouseId) }} / {{ locationName(line.sourceWarehouseId, line.sourceLocationId) }}</td>
            <td>{{ warehouseName(line.destinationWarehouseId) }} / {{ locationName(line.destinationWarehouseId, line.destinationLocationId) }}</td>
            <td>{{ line.quantity }}</td><td>{{ trackingLabel(line.tracking) }}</td></tr> }</tbody></table></section> }
    </section>
  `,
  styles: `.transfers{display:grid;gap:16px;color:var(--erp-color-text)}header,.filters{display:flex;gap:8px;align-items:center;flex-wrap:wrap}header{justify-content:space-between}h1{font-size:20px;margin:0}.detail{padding:16px;border:1px solid var(--erp-color-border)}.detail table{width:100%}@media print{header,.filters,nz-table,.detail>button{display:none!important}.detail{border:0}}`,
})
export class TransferPage {
  readonly trackingLabel = trackingLabel;
  private readonly api = inject(TransferService);
  private readonly itemApi = inject(ItemService);
  private readonly warehouseApi = inject(WarehouseService);
  private readonly company = inject(CompanyContextService);
  private readonly permissions = inject(PermissionService);
  private readonly dialogs = inject(ErpDialogService);
  readonly rows = signal<TransferSummary[]>([]);
  readonly detail = signal<TransferDetail | null>(null);
  readonly total = signal(0); readonly page = signal(1); readonly pageSize = signal(20);
  readonly loading = signal(false); readonly busy = signal(false); readonly exporting = signal(false);
  readonly error = signal<string | null>(null);
  readonly items = signal<Item[]>([]); readonly warehouses = signal<Warehouse[]>([]);
  readonly locationMap = signal<Record<number, StorageLocation[]>>({});
  keyword = ''; from = ''; to = ''; status = '';
  private generation = 0;
  constructor() { effect(() => { this.company.current(); untracked(() => { this.detail.set(null); this.locationMap.set({}); this.search(); }); }); }
  itemName(id: number): string { const item = this.items().find(x => x.id === id); return item ? `${item.code} · ${item.name}` : String(id); }
  warehouseName(id: number): string { const wh = this.warehouses().find(x => x.id === id); return wh ? `${wh.code} · ${wh.name}` : String(id); }
  locationName(warehouseId: number, locationId: number | null): string {
    if (!locationId) return '—';
    const loc = this.locationMap()[warehouseId]?.find(x => x.id === locationId);
    return loc ? `${loc.code} · ${loc.name}` : String(locationId);
  }
  statusKey(status: TransferStatus): string { return 'inv.transfer.' + ({ Draft: 'draft', PendingApproval: 'pending', Rejected: 'rejected', Posted: 'posted' }[status] ?? 'draft'); }
  statusColor(status: TransferStatus): string { return { Draft: 'default', PendingApproval: 'processing', Rejected: 'error', Posted: 'success' }[status] ?? 'default'; }
  onQuery(query: NzTableQueryParams): void { if (query.pageIndex !== this.page() || query.pageSize !== this.pageSize()) { this.page.set(query.pageIndex); this.pageSize.set(query.pageSize); this.load(); } }
  search(): void {
    if (this.from && this.to && this.to < this.from) { this.error.set('inv.transfer.invalidDates'); return; }
    this.page.set(1); this.load();
  }
  private query(page: number, pageSize: number) { return { page, pageSize, keyword: this.keyword, from: this.from, to: this.to, status: this.status }; }
  load(): void {
    const current = ++this.generation; this.loading.set(true); this.error.set(null);
    this.api.list(this.query(this.page(), this.pageSize())).subscribe({ next: result => {
      if (current !== this.generation) return; this.rows.set(result.items); this.total.set(result.totalCount); this.loading.set(false);
    }, error: e => { if (current === this.generation) { this.error.set(this.api.errorKey(e)); this.loading.set(false); } } });
  }
  /** 讀取物料、倉庫及詳情兩側有指定儲位的倉庫之儲位名稱；已載入的倉庫儲位不重抓。 */
  private names(detail: TransferDetail): Observable<void> {
    const warehouseIds = [...new Set(detail.lines.flatMap(x => [
      ...(x.sourceLocationId ? [x.sourceWarehouseId] : []), ...(x.destinationLocationId ? [x.destinationWarehouseId] : [])]))]
      .filter(id => !this.locationMap()[id]);
    const locations = warehouseIds.length
      ? forkJoin(warehouseIds.map(id => fetchAllPages(page => this.warehouseApi.listLocations(id, { page, pageSize: OPTION_PAGE_SIZE }))))
      : of([] as StorageLocation[][]);
    return forkJoin({ items: fetchAllPages(page => this.itemApi.list({ page, pageSize: OPTION_PAGE_SIZE })),
      warehouses: fetchAllPages(page => this.warehouseApi.list({ page, pageSize: OPTION_PAGE_SIZE })), locations }).pipe(map(names => {
      this.items.set(names.items); this.warehouses.set(names.warehouses);
      this.locationMap.update(current => ({ ...current, ...Object.fromEntries(warehouseIds.map((id, i) => [id, names.locations[i]])) }));
    }));
  }
  view(row: TransferSummary, print = false): void {
    this.api.get(row.id).subscribe({ next: d => this.names(d).subscribe({
      next: () => { this.detail.set(d); if (print) setTimeout(() => window.print()); },
      error: e => this.error.set(this.api.errorKey(e)),
    }), error: e => this.error.set(this.api.errorKey(e)) });
  }
  print(row: TransferSummary): void { this.view(row, true); }
  submit(row: TransferSummary): void { if (this.busy()) return; this.busy.set(true); this.api.submit(row.id, row.version).subscribe({
    next: () => { this.busy.set(false); this.load(); }, error: e => { this.busy.set(false); this.load(); this.error.set(this.api.errorKey(e)); },
  }); }
  remove(row: TransferSummary): void {
    if (this.busy()) return;
    this.dialogs.confirm({ titleKey: 'inv.transfer.confirmDelete', danger: true, okKey: 'inv.transfer.delete' }).subscribe(confirmed => {
      if (!confirmed) return;
      this.busy.set(true); this.api.remove(row.id, row.version).subscribe({
        next: () => { this.busy.set(false); this.load(); }, error: e => { this.busy.set(false); this.load(); this.error.set(this.api.errorKey(e)); },
      });
    });
  }
  exportCsv(): void {
    if (this.exporting() || !this.permissions.has('INV.TRANSFER.VIEW')) return;
    if (this.from && this.to && this.to < this.from) { this.error.set('inv.transfer.invalidDates'); return; }
    this.exporting.set(true); this.error.set(null);
    const fail = (e: unknown) => { this.error.set(this.api.errorKey(e)); this.exporting.set(false); };
    fetchAllPages(page => this.api.list(this.query(page, OPTION_PAGE_SIZE))).subscribe({ next: rows => {
      from(rows.map((row, index) => ({ row, index }))).pipe(mergeMap(({ row, index }) =>
        this.api.get(row.id).pipe(map(detail => ({ index, detail }))), 4), toArray()).subscribe({ next: details => {
        const data = [['Document No', 'Business Date', 'Status', 'Line', 'Item ID', 'Source Warehouse ID', 'Source Location ID',
          'Destination Warehouse ID', 'Destination Location ID', 'Quantity', 'Tracking', 'Remark'],
          ...details.sort((a, b) => a.index - b.index).flatMap(({ detail: d }) => d.lines.map(line =>
            [d.documentNo, d.businessDate, d.status, line.lineNo, line.itemId, line.sourceWarehouseId, line.sourceLocationId ?? '',
              line.destinationWarehouseId, line.destinationLocationId ?? '', line.quantity, trackingLabel(line.tracking), d.remark ?? '']))];
        const blob = new Blob(['﻿', data.map(line => line.map(csvCell).join(',')).join('\r\n')], { type: 'text/csv;charset=utf-8' });
        const url = URL.createObjectURL(blob); const link = document.createElement('a'); link.href = url;
        link.download = 'inventory-transfers.csv'; link.click(); URL.revokeObjectURL(url); this.exporting.set(false);
      }, error: fail });
    }, error: fail });
  }
}

/** 防公式注入：以 = + - @ 開頭（含前置空白、Tab）的文字前加單引號（ADR-056）。 */
export function csvCell(value: string | number | null): string {
  let text = String(value ?? '');
  if (typeof value === 'string' && /^[\s]*[=+\-@]/.test(text)) text = "'" + text;
  return '"' + text.replaceAll('"', '""') + '"';
}
