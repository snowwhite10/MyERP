import { ChangeDetectionStrategy, Component, DestroyRef, computed, effect, inject, signal, untracked } from '@angular/core';
import { HttpErrorResponse } from '@angular/common/http';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router } from '@angular/router';
import { Subscription } from 'rxjs';
import { TranslatePipe } from '@ngx-translate/core';
import { NzAlertModule } from 'ng-zorro-antd/alert';
import { NzButtonModule } from 'ng-zorro-antd/button';
import { NzTableModule, NzTableQueryParams } from 'ng-zorro-antd/table';
import { HasPermissionDirective } from '../../core/auth/has-permission.directive';
import { CompanyContextService } from '../../core/company/company-context.service';
import { apiErrorTraceId } from '../../core/http/api-error';
import { ApiErrorDisplay } from '../../shared/api-error-display';
import { formatDecimalText } from '../../shared/decimal-text';
import { checkCsvExport, saveBlob } from '../../shared/export/csv-export';
import { DocumentStatus, StatusTag } from '../../shared/status-tag/status-tag';
import {
  PURCHASE_RECEIPT_EXPORT_FILE, PURCHASE_RECEIPT_SORTS, PURCHASE_RECEIPT_STATUSES, PurchaseReceiptDetail,
  PurchaseReceiptExportFailure, PurchaseReceiptFilter, PurchaseReceiptFilterIssue, PurchaseReceiptService, PurchaseReceiptStatus,
  PurchaseReceiptSummary, PurchaseReceiptTracking, purchaseReceiptExportFailure, purchaseReceiptFilterIssue,
} from './purchase-receipt.service';

/** Shared tag tone: pending approval uses the pending tone; Posted (stock received) uses the posted tone. */
const TAG_STATUS: Record<PurchaseReceiptStatus, DocumentStatus> = {
  Draft: 'Draft', PendingApproval: 'Submitted', Rejected: 'Rejected', Posted: 'Posted',
};
const EMPTY_FILTER: PurchaseReceiptFilter = { keyword: '', from: '', to: '', status: '', purchaseOrderId: null };

/**
 * 介面 4.4 Purchase Inbound View Interface (WU-PUR-007e1): read-only list with server paging, saved detail with lines, warehouse,
 * saved base unit cost and lot / serial capture, server CSV of the applied filters and sort, and print that re-reads the saved
 * detail first. Each status explains whether stock has changed. SUBMIT / DELETE buttons and the create / edit forms follow in 007e2+.
 */
@Component({
  selector: 'erp-purchase-receipt-page',
  imports: [FormsModule, TranslatePipe, ApiErrorDisplay, HasPermissionDirective, StatusTag, NzAlertModule, NzButtonModule, NzTableModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <section class="receipts"><header><h1>{{ 'pur.receipt.title' | translate }}</h1></header>
      <form class="filters" (ngSubmit)="search()" data-testid="pur-receipt-filters">
        <label>{{ 'pur.receipt.keyword' | translate }} <input type="search" name="grKeyword" autocomplete="off" [(ngModel)]="filter.keyword"
          data-testid="pur-receipt-keyword" /></label>
        <label>{{ 'pur.receipt.from' | translate }} <input type="date" name="grFrom" autocomplete="off" [(ngModel)]="filter.from"
          data-testid="pur-receipt-from" /></label>
        <label>{{ 'pur.receipt.to' | translate }} <input type="date" name="grTo" autocomplete="off" [(ngModel)]="filter.to"
          data-testid="pur-receipt-to" /></label>
        <label>{{ 'pur.receipt.status' | translate }} <select name="grStatus" autocomplete="off" [(ngModel)]="filter.status" data-testid="pur-receipt-status">
          <option value="">{{ 'pur.receipt.allStatuses' | translate }}</option>
          @for (s of statuses; track s) { <option [value]="s">{{ statusLabel(s) | translate }}</option> }</select></label>
        <label>{{ 'pur.receipt.purchaseOrderId' | translate }} <input type="number" name="grOrder" min="1" step="1" inputmode="numeric" autocomplete="off"
          [(ngModel)]="filter.purchaseOrderId" data-testid="pur-receipt-order" /></label>
        <label>{{ 'pur.receipt.sort' | translate }} <select name="grSort" autocomplete="off" [(ngModel)]="sort" data-testid="pur-receipt-sort">
          @for (s of sorts; track s) { <option [value]="s">{{ sortLabel(s) | translate }}</option> }</select></label>
        <button nz-button nzType="primary" type="submit" data-testid="pur-receipt-search">{{ 'pur.receipt.search' | translate }}</button>
        <button nz-button type="button" (click)="reset()" data-testid="pur-receipt-reset">{{ 'pur.receipt.reset' | translate }}</button>
      </form>
      @if (filterIssue(); as issue) { <p class="field-error" role="alert" data-testid="pur-receipt-filter-error">{{ 'pur.receipt.filterIssue.' + issue | translate }}</p> }
      @if (error()) { <erp-api-error-display data-testid="pur-receipt-error" [messageKey]="error()!" [traceId]="traceId()" /> }
      <div class="export" *erpHasPermission="'PUR.RECEIPT.EXPORT'">
        <button nz-button type="button" [disabled]="!!exportBlockedKey()" [nzLoading]="exporting()" (click)="exportCsv()"
          aria-describedby="pur-receipt-export-hint" data-testid="pur-receipt-export">{{ (exporting() ? 'pur.receipt.exporting' : 'pur.receipt.export') | translate }}</button>
        <span id="pur-receipt-export-hint" class="hint" data-testid="pur-receipt-export-hint">{{ (exportBlockedKey() ?? 'pur.receipt.exportScope') | translate }}</span>
      </div>
      <div class="export-result" aria-live="polite" *erpHasPermission="'PUR.RECEIPT.EXPORT'">
        @if (exportNotice(); as notice) { <nz-alert [nzType]="notice.type" nzShowIcon
          [nzMessage]="notice.key | translate: { count: notice.count.toLocaleString() }" data-testid="pur-receipt-export-notice" /> }
        @if (exportFailure(); as failure) { <erp-api-error-display [messageKey]="failure.key" [traceId]="failure.traceId"
          [details]="failure.details" data-testid="pur-receipt-export-error" /> }
      </div>
      <nz-table #table [nzData]="rows()" [nzLoading]="loading()" [nzFrontPagination]="false" [nzTotal]="total()"
        [nzPageIndex]="page()" [nzPageSize]="pageSize()" [nzShowSizeChanger]="true" (nzQueryParams)="onQuery($event)"
        [nzNoResult]="(listFailed() ? 'pur.receipt.loadFailed' : filtered() ? 'pur.receipt.searchEmpty' : 'pur.receipt.empty') | translate"
        data-testid="pur-receipt-table">
        <thead><tr><th>{{ 'pur.receipt.number' | translate }}</th><th>{{ 'pur.receipt.businessDate' | translate }}</th>
          <th>{{ 'pur.receipt.purchaseOrder' | translate }}</th><th>{{ 'pur.receipt.supplierId' | translate }}</th>
          <th>{{ 'pur.receipt.status' | translate }}</th><th>{{ 'pur.receipt.currency' | translate }}</th>
          <th class="num">{{ 'pur.receipt.lineCount' | translate }}</th><th class="num">{{ 'pur.receipt.totalQuantity' | translate }}</th></tr></thead>
        <tbody>@for (row of table.data; track row.id) { <tr [attr.data-testid]="'pur-receipt-row-' + row.id">
          <td><button nz-button nzType="link" type="button" (click)="view(row)">{{ row.documentNo }}</button></td>
          <td>{{ row.businessDate }}</td><td>{{ row.purchaseOrderDocumentNo ?? ('pur.receipt.orderId' | translate: { id: row.purchaseOrderId }) }}</td>
          <td>{{ row.supplierId }}</td>
          <td><erp-status-tag [status]="tag(row.status)" [labelKey]="statusLabel(row.status)" /></td>
          <td>{{ row.currency }}</td><td class="num">{{ row.lineCount }}</td><td class="num">{{ qty(row.totalQuantity) }}</td></tr> }</tbody>
      </nz-table>
      @if (detail(); as d) { <section class="detail" data-testid="pur-receipt-detail" aria-labelledby="pur-receipt-detail-title">
        <div class="detail-head"><h2 id="pur-receipt-detail-title">{{ d.documentNo }}</h2>
          <erp-status-tag [status]="tag(d.status)" [labelKey]="statusLabel(d.status)" testId="pur-receipt-detail-status" />
          <span class="spacer"></span>
          <button nz-button type="button" [disabled]="printing()" [nzLoading]="printing()" (click)="print(d)" data-testid="pur-receipt-print">{{ (printing() ? 'pur.receipt.printing' : 'pur.receipt.print') | translate }}</button>
          <button nz-button type="button" (click)="closeDetail()" data-testid="pur-receipt-close">{{ 'pur.receipt.close' | translate }}</button></div>
        <p class="hint" data-testid="pur-receipt-stock-hint">{{ 'pur.receipt.stockHint.' + stateKey(d.status) | translate }}</p>
        <dl class="facts">
          <div><dt>{{ 'pur.receipt.businessDate' | translate }}</dt><dd>{{ d.businessDate }}</dd></div>
          <div><dt>{{ 'pur.receipt.purchaseOrder' | translate }}</dt><dd data-testid="pur-receipt-detail-order">{{ orderLabel(d) }}</dd></div>
          <div><dt>{{ 'pur.receipt.supplierId' | translate }}</dt><dd>{{ d.supplierId }}</dd></div>
          <div><dt>{{ 'pur.receipt.currency' | translate }}</dt><dd>{{ d.currency }}</dd></div>
          <div><dt>{{ 'pur.receipt.lineCount' | translate }}</dt><dd>{{ d.lines.length }}</dd></div>
          <div><dt>{{ 'pur.receipt.version' | translate }}</dt><dd>{{ d.version }}</dd></div>
        </dl>
        <h3>{{ 'pur.receipt.lines' | translate }}</h3>
        @if (d.lines.length === 0) { <p class="hint" data-testid="pur-receipt-no-lines">{{ 'pur.receipt.noLines' | translate }}</p> }
        @else { <div class="scroll"><table data-testid="pur-receipt-lines"><thead><tr><th>#</th><th>{{ 'pur.receipt.orderLine' | translate }}</th>
          <th>{{ 'pur.receipt.item' | translate }}</th><th>{{ 'pur.receipt.warehouse' | translate }}</th>
          <th class="num">{{ 'pur.receipt.quantity' | translate }}</th><th class="num">{{ 'pur.receipt.unitCost' | translate }}</th>
          <th>{{ 'pur.receipt.tracking' | translate }}</th></tr></thead>
          <tbody>@for (line of d.lines; track line.id) { <tr [attr.data-testid]="'pur-receipt-line-' + line.lineNumber">
            <td>{{ line.lineNumber }}</td><td>{{ line.purchaseOrderLineId }}</td><td>{{ line.materialId }}</td><td>{{ line.warehouseId }}</td>
            <td class="num">{{ qty(line.quantity) }}</td><td class="num">{{ qty(line.unitCost) }}</td>
            <td>@if (line.tracking.length === 0) { — } @else { <ul class="tracking">@for (t of line.tracking; track t.seqNo) {
              <li>{{ trackingLabel(t) | translate: trackingParams(t) }}</li> }</ul> }</td></tr> }</tbody></table></div>
          <p class="hint">{{ 'pur.receipt.costNote' | translate }}</p> }
      </section> }
    </section>`,
  styles: `.receipts{display:grid;gap:16px;color:var(--erp-color-text)}header{display:flex;gap:8px;align-items:center;justify-content:space-between;flex-wrap:wrap}h1{font-size:20px;margin:0;text-wrap:balance}
    .filters{display:flex;gap:8px;align-items:end;flex-wrap:wrap}.filters label{display:grid;gap:4px;font-size:12px}
    .filters input,.filters select{min-height:32px;background:var(--erp-color-bg-container);color:var(--erp-color-text);border:1px solid var(--erp-color-border);border-radius:4px;padding:0 6px}
    .filters input[type=number]{width:140px}.field-error{color:var(--erp-color-error-text);margin:0}
    .export{display:flex;gap:8px;align-items:center;flex-wrap:wrap}.hint{color:var(--erp-color-text-secondary);font-size:12px;margin:0}
    .num{text-align:right;font-variant-numeric:tabular-nums}
    .detail{padding:16px;border:1px solid var(--erp-color-border);border-radius:8px;display:grid;gap:12px;min-width:0}
    .detail-head{display:flex;gap:8px;align-items:center;flex-wrap:wrap}.detail-head h2{margin:0;font-size:18px}.spacer{flex:1}
    .facts{display:grid;grid-template-columns:repeat(auto-fill,minmax(180px,1fr));gap:8px 16px;margin:0}.facts dt{font-size:12px;color:var(--erp-color-text-secondary)}
    .facts dd{margin:0;overflow-wrap:anywhere;font-variant-numeric:tabular-nums}h3{margin:0;font-size:15px}
    .scroll{overflow-x:auto}.detail table{width:100%;border-collapse:collapse}.detail th,.detail td{padding:6px;text-align:left;border-bottom:1px solid var(--erp-color-border);vertical-align:top}
    .detail th.num,.detail td.num{text-align:right}.tracking{margin:0;padding-left:16px;overflow-wrap:anywhere}
    button:focus-visible{outline:2px solid var(--erp-color-primary);outline-offset:2px}
    @media print{.receipts>header,.filters,.field-error,.export,.export-result,nz-table,erp-api-error-display,.detail-head button{display:none!important}
      .receipts{display:block;color:#000;background:#fff}.detail{border:0;padding:0}.detail *{color:#000!important;background:#fff!important;border-color:#bbb!important}.scroll{overflow:visible}}`,
})
export class PurchaseReceiptPage {
  private readonly api = inject(PurchaseReceiptService);
  private readonly company = inject(CompanyContextService);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  readonly statuses = PURCHASE_RECEIPT_STATUSES;
  readonly sorts = PURCHASE_RECEIPT_SORTS;
  readonly rows = signal<PurchaseReceiptSummary[]>([]);
  readonly detail = signal<PurchaseReceiptDetail | null>(null);
  readonly total = signal(0); readonly page = signal(1); readonly pageSize = signal(20);
  readonly loading = signal(false); readonly listFailed = signal(false); readonly filtered = signal(false);
  readonly error = signal<string | null>(null); readonly traceId = signal<string | null>(null);
  readonly filterIssue = signal<PurchaseReceiptFilterIssue | null>(null);
  readonly printing = signal(false);
  readonly exporting = signal(false);
  readonly exportNotice = signal<{ type: 'success' | 'warning'; key: string; count: number } | null>(null);
  readonly exportFailure = signal<PurchaseReceiptExportFailure | null>(null);
  /** Why export is unavailable right now (shown as the button hint); null = ready. */
  readonly exportBlockedKey = computed(() => this.exporting() ? 'pur.receipt.exportBusy' : this.loading() ? 'pur.receipt.exportWaitLoad' :
    this.listFailed() ? 'pur.receipt.exportNeedsList' : this.total() === 0 ? (this.filtered() ? 'pur.receipt.exportNoRows' : 'pur.receipt.exportNoReceipts') : null);
  filter: PurchaseReceiptFilter = { ...EMPTY_FILTER };
  sort: string = PURCHASE_RECEIPT_SORTS[0];
  private applied: PurchaseReceiptFilter & { sort: string } = { ...EMPTY_FILTER, sort: PURCHASE_RECEIPT_SORTS[0] };
  private generation = 0; private detailGeneration = 0; private exportEpoch = 0;
  private exportRequest?: Subscription;
  private firstLoad = true;
  /** Receipt to open once the first list arrives (`?id=`, e.g. after saving on 介面 4.5); used once, never written back. */
  private openId: number | null = null;

  constructor() {
    const params = this.route.snapshot.queryParamMap;
    const id = (key: string) => { const n = Number(params.get(key)); return Number.isSafeInteger(n) && n > 0 ? n : null; };
    const status = params.get('status') ?? '';
    const sort = params.get('sort') ?? '';
    this.filter = { keyword: params.get('keyword') ?? '', from: params.get('from') ?? '', to: params.get('to') ?? '',
      status: (PURCHASE_RECEIPT_STATUSES as readonly string[]).includes(status) ? status : '', purchaseOrderId: id('purchaseOrderId') };
    if ((PURCHASE_RECEIPT_SORTS as readonly string[]).includes(sort)) this.sort = sort;
    const page = id('page'); if (page) this.page.set(page);
    this.openId = id('id');
    // Switching company re-runs the search: old rows, detail and export results never stay on screen.
    effect(() => { this.company.current(); untracked(() => { this.search(!this.firstLoad); this.firstLoad = false; }); });
    inject(DestroyRef).onDestroy(() => { this.generation++; this.detailGeneration++; this.stopExport(); });
  }

  tag(status: PurchaseReceiptStatus): DocumentStatus { return TAG_STATUS[status] ?? 'Draft'; }
  stateKey(status: string): string { return status.charAt(0).toLowerCase() + status.slice(1); }
  statusLabel(status: string): string { return `pur.receipt.state.${this.stateKey(status)}`; }
  sortLabel(sort: string): string { return 'pur.receipt.sortBy.' + (sort.startsWith('-') ? sort.slice(1) + 'Desc' : sort + 'Asc'); }
  qty(value: string): string { return formatDecimalText(value, 'en-US'); }
  /** The purchase order number from the list row when known; otherwise its ID. */
  orderLabel(d: PurchaseReceiptDetail): string {
    return this.rows().find(r => r.id === d.id)?.purchaseOrderDocumentNo ?? `#${d.purchaseOrderId}`;
  }
  trackingLabel(t: PurchaseReceiptTracking): string {
    return t.serialNo ? 'pur.receipt.serial' : t.expiryDate || t.manufactureDate ? 'pur.receipt.lotDated' : 'pur.receipt.lot';
  }
  trackingParams(t: PurchaseReceiptTracking): Record<string, string> {
    return { serial: t.serialNo ?? '', lot: t.lotNo ?? '', quantity: this.qty(t.quantity),
      made: t.manufactureDate ?? '—', expiry: t.expiryDate ?? '—' };
  }

  /** Applies the typed filters; invalid input is explained and nothing is requested. */
  search(resetPage = true): void {
    const issue = purchaseReceiptFilterIssue(this.filter);
    this.filterIssue.set(issue);
    if (issue) return;
    this.clearExport();
    this.applied = { keyword: this.filter.keyword.trim(), from: this.filter.from, to: this.filter.to, status: this.filter.status,
      purchaseOrderId: this.filter.purchaseOrderId, sort: this.sort };
    const a = this.applied;
    this.filtered.set(!!(a.keyword || a.from || a.to || a.status || a.purchaseOrderId));
    if (resetPage) this.page.set(1);
    this.updateUrl(); this.load();
  }
  reset(): void { this.filter = { ...EMPTY_FILTER }; this.sort = PURCHASE_RECEIPT_SORTS[0]; this.search(); }
  onQuery(query: NzTableQueryParams): void {
    if (query.pageIndex === this.page() && query.pageSize === this.pageSize()) return;
    this.page.set(query.pageIndex); this.pageSize.set(query.pageSize); this.updateUrl(); this.load();
  }
  private updateUrl(): void {
    const a = this.applied;
    void this.router.navigate([], { relativeTo: this.route, replaceUrl: true, queryParams: {
      keyword: a.keyword || null, from: a.from || null, to: a.to || null, status: a.status || null,
      purchaseOrderId: a.purchaseOrderId, sort: a.sort === PURCHASE_RECEIPT_SORTS[0] ? null : a.sort,
      page: this.page() > 1 ? this.page() : null } });
  }
  load(keepError = false): void {
    const current = ++this.generation; ++this.detailGeneration;
    this.detail.set(null); this.printing.set(false); this.loading.set(true); this.listFailed.set(false);
    if (!keepError) this.clearError();
    this.api.list({ ...this.applied, page: this.page(), pageSize: this.pageSize() }).subscribe({
      next: result => { if (current !== this.generation) return;
        this.rows.set(result.items); this.total.set(result.totalCount); this.loading.set(false);
        const open = this.openId; this.openId = null;
        if (open) this.read(open, false); },
      error: failure => { if (current !== this.generation) return;
        this.openId = null; this.rows.set([]); this.total.set(0); this.listFailed.set(true); this.showError(failure); this.loading.set(false); },
    });
  }
  view(row: PurchaseReceiptSummary): void { this.read(row.id, false); }
  closeDetail(): void { ++this.detailGeneration; this.detail.set(null); this.printing.set(false); }
  /** Print always re-reads the saved receipt first; a failed read shows the reason and never opens the print dialog. */
  print(d: PurchaseReceiptDetail): void { if (!this.printing()) this.read(d.id, true); }

  exportCsv(): void {
    if (this.exportBlockedKey()) return;
    const epoch = ++this.exportEpoch;
    this.exporting.set(true); this.exportNotice.set(null); this.exportFailure.set(null);
    this.exportRequest = this.api.export(this.applied).subscribe({
      next: response => void checkCsvExport(response).then(outcome => {
        if (epoch !== this.exportEpoch) return;
        this.exporting.set(false);
        if (outcome.kind === 'invalid') { this.exportFailure.set({ key: 'pur.receipt.exportIncomplete', traceId: null, details: [] }); return; }
        if (outcome.kind === 'empty') { this.exportNotice.set({ type: 'warning', key: 'pur.receipt.exportEmpty', count: 0 }); return; }
        saveBlob(outcome.blob, PURCHASE_RECEIPT_EXPORT_FILE);
        this.exportNotice.set({ type: 'success', key: 'pur.receipt.exported', count: outcome.count });
      }),
      error: failure => { if (epoch !== this.exportEpoch) return;
        this.exporting.set(false); this.exportFailure.set(purchaseReceiptExportFailure(failure)); },
    });
  }

  private read(id: number, thenPrint: boolean): void {
    const current = ++this.detailGeneration;
    this.clearError(); this.printing.set(thenPrint);
    if (!thenPrint) this.detail.set(null);
    this.api.get(id).subscribe({
      next: saved => { if (current !== this.detailGeneration) return;
        this.detail.set(saved);
        if (!thenPrint) return;
        setTimeout(() => { if (current !== this.detailGeneration) return; this.printing.set(false); window.print(); });
      },
      error: failure => { if (current !== this.detailGeneration) return;
        this.printing.set(false); this.showError(failure);
        if (failure instanceof HttpErrorResponse && failure.status === 404) this.load(true); },
    });
  }
  private stopExport(): void { this.exportEpoch++; this.exportRequest?.unsubscribe(); this.exporting.set(false); }
  private clearExport(): void { this.stopExport(); this.exportNotice.set(null); this.exportFailure.set(null); }
  private clearError(): void { this.error.set(null); this.traceId.set(null); }
  private showError(failure: unknown): void { this.error.set(this.api.errorKey(failure)); this.traceId.set(apiErrorTraceId(failure)); }
}
