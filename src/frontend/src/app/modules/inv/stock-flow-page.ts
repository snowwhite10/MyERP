import { HttpErrorResponse } from '@angular/common/http';
import { ChangeDetectionStrategy, Component, effect, inject, signal, untracked } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { TranslatePipe } from '@ngx-translate/core';
import { NzAlertModule } from 'ng-zorro-antd/alert';
import { NzButtonModule } from 'ng-zorro-antd/button';
import { NzTableModule, NzTableQueryParams } from 'ng-zorro-antd/table';
import { Subscription, forkJoin } from 'rxjs';
import { PermissionService } from '../../core/auth/permission.service';
import { CompanyContextService } from '../../core/company/company-context.service';
import { apiErrorKey, apiErrorTraceId, apiValidationDetails } from '../../core/http/api-error';
import { fetchAllPages, OPTION_PAGE_SIZE } from '../../shared/list-page/fetch-all-pages';
import { InventoryOption, InventorySelectionOptionsService } from './selection-options.service';
import { StockFlowFilters, StockFlowRow, StockFlowService } from './stock-flow.service';

@Component({
  selector: 'erp-stock-flow-page',
  imports: [FormsModule, TranslatePipe, NzAlertModule, NzButtonModule, NzTableModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <section class="stock-flow" data-testid="inv-stock-flow">
      <h1>{{ 'inv.flow.title' | translate }}</h1>
      <p>{{ 'inv.flow.scope' | translate }}</p>
      <div class="filters">
        <label>{{ 'inv.flow.from' | translate }} *<input type="date" name="from" autocomplete="off" [(ngModel)]="from" (ngModelChange)="filtersChanged()" data-testid="inv-flow-from" /></label>
        <label>{{ 'inv.flow.to' | translate }} *<input type="date" name="to" autocomplete="off" [(ngModel)]="to" (ngModelChange)="filtersChanged()" data-testid="inv-flow-to" /></label>
        <label>{{ 'inv.flow.item' | translate }}<select name="item" [(ngModel)]="itemId" (ngModelChange)="filtersChanged()"><option [ngValue]="null">{{ 'inv.flow.all' | translate }}</option>@for (item of items(); track item.id) { <option [ngValue]="item.id">{{ item.code }} · {{ item.name }}</option> }</select></label>
        <label>{{ 'inv.flow.warehouse' | translate }}<select name="warehouse" [(ngModel)]="warehouseId" (ngModelChange)="warehouseChanged()"><option [ngValue]="null">{{ 'inv.flow.all' | translate }}</option>@for (warehouse of warehouses(); track warehouse.id) { <option [ngValue]="warehouse.id">{{ warehouse.code }} · {{ warehouse.name }}</option> }</select></label>
        <label>{{ 'inv.flow.location' | translate }}<select name="location" [(ngModel)]="locationId" (ngModelChange)="filtersChanged()" [disabled]="!warehouseId" [title]="!warehouseId ? ('inv.flow.chooseWarehouse' | translate) : ''"><option [ngValue]="null">{{ 'inv.flow.all' | translate }}</option>@for (location of locations(); track location.id) { <option [ngValue]="location.id">{{ location.code }} · {{ location.name }}</option> }</select></label>
        <button nz-button nzType="primary" [disabled]="loading()" (click)="search()" data-testid="inv-flow-search">{{ 'inv.flow.run' | translate }}</button>
        @if (canExport()) {
          <button nz-button [disabled]="!queriedAt() || exporting() || total() === 0" [title]="total() === 0 ? ('inv.flow.noExport' | translate) : ''" (click)="exportCsv()" data-testid="inv-flow-export">{{ 'inv.flow.export' | translate }}</button>
          @if (exporting()) { <button nz-button (click)="cancelExport()" data-testid="inv-flow-cancel">{{ 'inv.flow.cancel' | translate }}</button> }
        }
      </div>
      @if (errorKey()) { <nz-alert nzType="error" nzShowIcon aria-live="polite" [nzMessage]="errorKey()! | translate" data-testid="inv-flow-error" /> }
      @if (errorDetail()) { <p role="alert">{{ errorDetail() }}</p> }
      @if (traceId()) { <p role="alert">{{ 'inv.flow.trace' | translate }}: {{ traceId() }}</p> }
      @if (noticeKey()) { <nz-alert nzType="warning" nzShowIcon aria-live="polite" [nzMessage]="noticeKey()! | translate: { count: noticeCount() }" data-testid="inv-flow-notice" /> }
      @if (queriedAt()) { <p data-testid="inv-flow-snapshot">{{ 'inv.flow.snapshot' | translate }}: {{ displayTime(queriedAt()) }} · {{ 'inv.flow.total' | translate: { count: total() } }}</p> }
      <nz-table #table [nzData]="rows()" [nzLoading]="loading()" [nzFrontPagination]="false" [nzTotal]="total()"
        [nzPageIndex]="page()" [nzPageSize]="pageSize()" (nzQueryParams)="onQuery($event)" [nzNoResult]="(errorKey() ? 'inv.flow.failed' : queriedAt() ? 'inv.flow.empty' : 'inv.flow.prompt') | translate" data-testid="inv-flow-table">
        <thead><tr><th>{{ 'inv.flow.item' | translate }}</th><th>{{ 'inv.flow.warehouse' | translate }}</th><th>{{ 'inv.flow.location' | translate }}</th><th>{{ 'inv.flow.opening' | translate }}</th><th>{{ 'inv.flow.inbound' | translate }}</th><th>{{ 'inv.flow.outbound' | translate }}</th><th>{{ 'inv.flow.closing' | translate }}</th></tr></thead>
        <tbody>@for (row of table.data; track row.itemId + '-' + row.warehouseId + '-' + row.locationId) {
          <tr [class.negative]="row.negativeClosing"><td>{{ itemName(row.itemId) }}</td><td>{{ warehouseName(row.warehouseId) }}</td><td>{{ locationName(row.locationId) }}</td>
            <td>{{ fixed(row.openingQuantity) }}</td><td>{{ fixed(row.inboundQuantity) }}</td><td>{{ fixed(row.outboundQuantity) }}</td><td>{{ fixed(row.closingQuantity) }} @if (row.negativeClosing) { <strong>{{ 'inv.flow.negative' | translate }}</strong> }</td></tr>
        }</tbody>
      </nz-table>
    </section>
  `,
  styles: `.stock-flow{display:grid;gap:16px;color:var(--erp-color-text)}h1{font-size:20px;margin:0;text-wrap:balance}.stock-flow p{margin:0}.filters{display:flex;gap:10px;align-items:end;flex-wrap:wrap}.filters label{display:grid;gap:4px}select,input{min-width:130px;padding:6px;border:1px solid var(--erp-color-border);border-radius:4px;background:var(--erp-color-bg);color:var(--erp-color-text)}select:focus-visible,input:focus-visible{outline:2px solid var(--erp-color-primary)}td{font-variant-numeric:tabular-nums}.negative{color:var(--erp-color-error,#cf1322)}strong{white-space:nowrap}`,
})
export class StockFlowPage {
  private readonly api = inject(StockFlowService);
  private readonly options = inject(InventorySelectionOptionsService);
  private readonly company = inject(CompanyContextService);
  private readonly permissions = inject(PermissionService);
  readonly canExport = () => this.permissions.has('INV.REPORT.VIEW') && this.permissions.has('INV.REPORT.EXPORT');
  readonly items = signal<InventoryOption[]>([]); readonly warehouses = signal<InventoryOption[]>([]); readonly locations = signal<InventoryOption[]>([]);
  readonly rows = signal<StockFlowRow[]>([]); readonly total = signal(0); readonly page = signal(1); readonly pageSize = signal(20);
  readonly queriedAt = signal(''); readonly loading = signal(false); readonly exporting = signal(false);
  readonly errorKey = signal<string | null>(null); readonly errorDetail = signal(''); readonly traceId = signal(''); readonly noticeKey = signal<string | null>(null); readonly noticeCount = signal(0);
  from = ''; to = ''; itemId: number | null = null; warehouseId: number | null = null; locationId: number | null = null;
  private generation = 0;
  private companyGeneration = 0;
  private exportRequest?: Subscription;
  constructor() { effect(() => { this.company.current(); untracked(() => {
    const companyGeneration = ++this.companyGeneration;
    ++this.generation; this.exportRequest?.unsubscribe(); this.exporting.set(false); this.clearResult();
    this.itemId = this.warehouseId = this.locationId = null;
    this.items.set([]); this.warehouses.set([]); this.locations.set([]);
    forkJoin({ items: fetchAllPages(page => this.options.items({ page, pageSize: OPTION_PAGE_SIZE, keyword: '' })),
      warehouses: fetchAllPages(page => this.options.warehouses({ page, pageSize: OPTION_PAGE_SIZE, keyword: '' })) }).subscribe({
      next: names => { if (companyGeneration === this.companyGeneration) {
        this.items.set(names.items); this.warehouses.set(names.warehouses); } },
      error: e => { if (companyGeneration === this.companyGeneration) this.showError(e); },
    });
  }); }); }
  fixed(value: number): string { return value.toFixed(6); }
  displayTime(value: string): string { return value.slice(0, 19).replace('T', ' '); }
  itemName(id: number): string { const item = this.items().find(x => x.id === id); return item ? `${item.code} · ${item.name}` : String(id); }
  warehouseName(id: number): string { const row = this.warehouses().find(x => x.id === id); return row ? `${row.code} · ${row.name}` : String(id); }
  locationName(id: number | null): string { if (id === null) return '—'; const row = this.locations().find(x => x.id === id); return row ? `${row.code} · ${row.name}` : String(id); }
  warehouseChanged(): void { this.locationId = null; this.locations.set([]); if (this.warehouseId) {
    const id = this.warehouseId;
    const companyGeneration = this.companyGeneration;
    fetchAllPages(page => this.options.locations(id, { page, pageSize: OPTION_PAGE_SIZE, keyword: '' })).subscribe({
      next: rows => { if (companyGeneration === this.companyGeneration && this.warehouseId === id) this.locations.set(rows); },
      error: e => { if (companyGeneration === this.companyGeneration) this.showError(e); },
    });
  } this.filtersChanged(); }
  filtersChanged(): void { ++this.generation; this.exportRequest?.unsubscribe(); this.exporting.set(false); this.loading.set(false); this.clearResult(); }
  private filters(): StockFlowFilters { return { from: this.from, to: this.to, itemId: this.itemId ?? undefined,
    warehouseId: this.warehouseId ?? undefined, locationId: this.locationId ?? undefined }; }
  private clearResult(): void { this.rows.set([]); this.total.set(0); this.queriedAt.set(''); this.errorKey.set(null); this.errorDetail.set(''); this.traceId.set(''); this.noticeKey.set(null); }
  search(): void { this.page.set(1); this.load(); }
  onQuery(params: NzTableQueryParams): void { if (params.pageIndex !== this.page() || params.pageSize !== this.pageSize()) {
    this.page.set(params.pageIndex); this.pageSize.set(params.pageSize); if (this.from && this.to) this.load();
  } }
  load(): void {
    if (!this.from || !this.to || this.from > this.to) { this.clearResult(); this.errorKey.set('inv.flow.invalidDates'); return; }
    const generation = ++this.generation; this.clearResult(); this.loading.set(true);
    this.api.query({ ...this.filters(), page: this.page(), pageSize: this.pageSize() }).subscribe({
      next: data => { if (generation !== this.generation) return; this.rows.set(data.items); this.total.set(data.totalCount);
        this.queriedAt.set(data.queriedAt); this.loading.set(false); if (!data.totalCount) this.noticeKey.set('inv.flow.empty'); },
      error: e => { if (generation === this.generation) { this.loading.set(false); this.showError(e); } },
    });
  }
  exportCsv(): void {
    if (!this.canExport() || !this.queriedAt() || this.exporting() || this.total() === 0) return;
    this.exporting.set(true); this.errorKey.set(null); this.errorDetail.set(''); this.traceId.set(''); this.noticeKey.set(null);
    this.exportRequest = this.api.export(this.filters()).subscribe({
      next: response => {
        this.exporting.set(false); const rawCount = response.headers.get('X-Export-Row-Count');
        const count = rawCount !== null && /^\d+$/.test(rawCount) ? Number(rawCount) : Number.NaN;
        if (!Number.isSafeInteger(count) || count < 0 || !response.body) { this.errorKey.set('inv.flow.invalidExport'); return; }
        this.noticeCount.set(count);
        if (count === 0) { this.noticeKey.set('inv.flow.zeroExport'); return; }
        const url = URL.createObjectURL(response.body); const link = document.createElement('a'); link.href = url;
        link.download = 'stock-flow.csv'; link.click(); URL.revokeObjectURL(url);
        this.noticeKey.set('inv.flow.exported');
      },
      error: e => { this.exporting.set(false); this.showError(e); },
    });
  }
  cancelExport(): void { this.exportRequest?.unsubscribe(); this.exporting.set(false); this.noticeKey.set('inv.flow.cancelled'); }
  private showError(error: unknown): void {
    if (error instanceof HttpErrorResponse && error.error instanceof Blob) {
      error.error.text().then(body => { try { const details = JSON.parse(body) as { errorCode?: string; traceId?: string; errors?: Record<string, string[]> };
        this.errorKey.set(details.errorCode === 'INV.REPORT.EXPORT_LIMIT' ? 'inv.flow.limit' : error.status === 400 ? 'inv.flow.invalidDates' : error.status === 403 ? 'inv.flow.denied' : 'inv.flow.failed');
        this.traceId.set(error.status >= 500 ? details.traceId ?? '' : '');
        this.errorDetail.set(error.status >= 500 ? '' : Object.entries(details.errors ?? {}).map(([field, reasons]) => `${field}: ${reasons.join(', ')}`).join('; '));
      } catch { this.errorKey.set('inv.flow.failed'); } }); return;
    }
    this.errorKey.set(apiErrorKey(error, ['COMMON.PERMISSION.DENIED']));
    this.errorDetail.set(apiValidationDetails(error).join('; ')); this.traceId.set(apiErrorTraceId(error) ?? '');
  }
}
