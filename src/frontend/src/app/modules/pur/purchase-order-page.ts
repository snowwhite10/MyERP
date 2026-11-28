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
import { displayTime } from '../../shared/date';
import { formatDecimalText } from '../../shared/decimal-text';
import { checkCsvExport, saveBlob } from '../../shared/export/csv-export';
import { DocumentStatus, StatusTag } from '../../shared/status-tag/status-tag';
import {
  PURCHASE_ORDER_EXPORT_FILE, PURCHASE_ORDER_SORTS, PURCHASE_ORDER_STATUSES, PurchaseOrderDetail, PurchaseOrderExportFailure,
  PurchaseOrderFilter, PurchaseOrderFilterIssue, PurchaseOrderService, PurchaseOrderStatus, PurchaseOrderSummary,
  purchaseOrderExportFailure, purchaseOrderFilterIssue,
} from './purchase-order.service';

/** Shared tag tone: pending approval uses the pending tone; a cancelled order is muted like a voided document. */
const TAG_STATUS: Record<PurchaseOrderStatus, DocumentStatus> = {
  Draft: 'Draft', PendingApproval: 'Submitted', Approved: 'Approved', Rejected: 'Rejected', Cancelled: 'Voided',
};
const EMPTY_FILTER: PurchaseOrderFilter = { keyword: '', from: '', to: '', status: '', supplierId: null, requisitionLineId: null };

/**
 * 介面 4.1 Purchase Order View Interface (WU-PUR-006e4): read-only list, saved detail with sources and SYS approval history,
 * server CSV of the applied filters and sort, and print that re-reads the saved detail first. Lifecycle buttons belong to 006f.
 */
@Component({
  selector: 'erp-purchase-order-page',
  imports: [FormsModule, TranslatePipe, ApiErrorDisplay, HasPermissionDirective, StatusTag, NzAlertModule, NzButtonModule, NzTableModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <section class="orders"><header><h1>{{ 'pur.order.title' | translate }}</h1></header>
      <form class="filters" (ngSubmit)="search()" data-testid="pur-order-filters">
        <label>{{ 'pur.order.keyword' | translate }} <input type="search" name="poKeyword" autocomplete="off" [(ngModel)]="filter.keyword"
          data-testid="pur-order-keyword" /></label>
        <label>{{ 'pur.order.from' | translate }} <input type="date" name="poFrom" autocomplete="off" [(ngModel)]="filter.from"
          data-testid="pur-order-from" /></label>
        <label>{{ 'pur.order.to' | translate }} <input type="date" name="poTo" autocomplete="off" [(ngModel)]="filter.to"
          data-testid="pur-order-to" /></label>
        <label>{{ 'pur.order.status' | translate }} <select name="poStatus" autocomplete="off" [(ngModel)]="filter.status" data-testid="pur-order-status">
          <option value="">{{ 'pur.order.allStatuses' | translate }}</option>
          @for (s of statuses; track s) { <option [value]="s">{{ statusLabel(s) | translate }}</option> }</select></label>
        <label>{{ 'pur.order.supplierId' | translate }} <input type="number" name="poSupplier" min="1" step="1" inputmode="numeric" autocomplete="off"
          [(ngModel)]="filter.supplierId" data-testid="pur-order-supplier" /></label>
        <label>{{ 'pur.order.sourceLine' | translate }} <input type="number" name="poSource" min="1" step="1" inputmode="numeric" autocomplete="off"
          [(ngModel)]="filter.requisitionLineId" data-testid="pur-order-source" /></label>
        <label>{{ 'pur.order.sort' | translate }} <select name="poSort" autocomplete="off" [(ngModel)]="sort" data-testid="pur-order-sort">
          @for (s of sorts; track s) { <option [value]="s">{{ sortLabel(s) | translate }}</option> }</select></label>
        <button nz-button nzType="primary" type="submit" data-testid="pur-order-search">{{ 'pur.order.search' | translate }}</button>
        <button nz-button type="button" (click)="reset()" data-testid="pur-order-reset">{{ 'pur.order.reset' | translate }}</button>
      </form>
      @if (filterIssue(); as issue) { <p class="field-error" role="alert" data-testid="pur-order-filter-error">{{ 'pur.order.filterIssue.' + issue | translate }}</p> }
      @if (error()) { <erp-api-error-display data-testid="pur-order-error" [messageKey]="error()!" [traceId]="traceId()" /> }
      <div class="export" *erpHasPermission="'PUR.ORDER.EXPORT'">
        <button nz-button type="button" [disabled]="!!exportBlockedKey()" [nzLoading]="exporting()" (click)="exportCsv()"
          aria-describedby="pur-order-export-hint" data-testid="pur-order-export">{{ (exporting() ? 'pur.order.exporting' : 'pur.order.export') | translate }}</button>
        <span id="pur-order-export-hint" class="hint" data-testid="pur-order-export-hint">{{ (exportBlockedKey() ?? 'pur.order.exportScope') | translate }}</span>
      </div>
      <div class="export-result" aria-live="polite" *erpHasPermission="'PUR.ORDER.EXPORT'">
        @if (exportNotice(); as notice) { <nz-alert [nzType]="notice.type" nzShowIcon
          [nzMessage]="notice.key | translate: { count: notice.count.toLocaleString() }" data-testid="pur-order-export-notice" /> }
        @if (exportFailure(); as failure) { <erp-api-error-display [messageKey]="failure.key" [traceId]="failure.traceId"
          [details]="failure.details" data-testid="pur-order-export-error" /> }
      </div>
      <nz-table #table [nzData]="rows()" [nzLoading]="loading()" [nzFrontPagination]="false" [nzTotal]="total()"
        [nzPageIndex]="page()" [nzPageSize]="pageSize()" [nzShowSizeChanger]="true" (nzQueryParams)="onQuery($event)"
        [nzNoResult]="(listFailed() ? 'pur.order.loadFailed' : filtered() ? 'pur.order.searchEmpty' : 'pur.order.empty') | translate"
        data-testid="pur-order-table">
        <thead><tr><th>{{ 'pur.order.number' | translate }}</th><th>{{ 'pur.order.businessDate' | translate }}</th>
          <th>{{ 'pur.order.supplierId' | translate }}</th><th>{{ 'pur.order.status' | translate }}</th>
          <th>{{ 'pur.order.currency' | translate }}</th><th class="num">{{ 'pur.order.baseGross' | translate }}</th>
          <th class="num">{{ 'pur.order.lineCount' | translate }}</th></tr></thead>
        <tbody>@for (row of table.data; track row.id) { <tr [attr.data-testid]="'pur-order-row-' + row.id">
          <td><button nz-button nzType="link" type="button" (click)="view(row)">{{ row.documentNo ?? ('pur.order.unnumbered' | translate) }}</button></td>
          <td>{{ row.businessDate ?? '—' }}</td><td>{{ row.supplierId }}</td>
          <td><erp-status-tag [status]="tag(row.status)" [labelKey]="statusLabel(row.status)" /></td>
          <td>{{ row.currency }}</td><td class="num">{{ money(row.baseGross) }}</td><td class="num">{{ row.lineCount }}</td></tr> }</tbody>
      </nz-table>
      @if (detail(); as d) { <section class="detail" data-testid="pur-order-detail" aria-labelledby="pur-order-detail-title">
        <div class="detail-head"><h2 id="pur-order-detail-title">{{ d.documentNo ?? ('pur.order.unnumbered' | translate) }}</h2>
          <erp-status-tag [status]="tag(d.status)" [labelKey]="statusLabel(d.status)" testId="pur-order-detail-status" />
          <span class="spacer"></span>
          <button nz-button type="button" [disabled]="printing()" [nzLoading]="printing()" (click)="print(d)" data-testid="pur-order-print">{{ (printing() ? 'pur.order.printing' : 'pur.order.print') | translate }}</button>
          <button nz-button type="button" (click)="closeDetail()" data-testid="pur-order-close">{{ 'pur.order.close' | translate }}</button></div>
        <dl class="facts">
          <div><dt>{{ 'pur.order.businessDate' | translate }}</dt><dd>{{ d.businessDate ?? '—' }}</dd></div>
          <div><dt>{{ 'pur.order.supplierId' | translate }}</dt><dd>{{ d.supplierId }}</dd></div>
          <div><dt>{{ 'pur.order.currency' | translate }}</dt><dd>{{ d.currency }}</dd></div>
          <div><dt>{{ 'pur.order.exchangeRate' | translate }}</dt><dd>{{ d.exchangeRate ?? '—' }}{{ d.baseCurrencyCode ? ' (' + d.baseCurrencyCode + ')' : '' }}</dd></div>
          <div><dt>{{ 'pur.order.baseNet' | translate }}</dt><dd>{{ money(d.baseNet) }}</dd></div>
          <div><dt>{{ 'pur.order.baseTax' | translate }}</dt><dd>{{ money(d.baseTax) }}</dd></div>
          <div><dt>{{ 'pur.order.baseGross' | translate }}</dt><dd data-testid="pur-order-detail-gross">{{ money(d.baseGross) }}</dd></div>
          <div><dt>{{ 'pur.order.rfq' | translate }}</dt><dd data-testid="pur-order-detail-rfq">{{ d.rfqDocumentNo ?? ('pur.order.manual' | translate) }}</dd></div>
          <div><dt>{{ 'pur.order.version' | translate }}</dt><dd>{{ d.version }}</dd></div>
          @if (d.remark) { <div class="wide"><dt>{{ 'pur.order.remark' | translate }}</dt><dd>{{ d.remark }}</dd></div> }
        </dl>
        <h3>{{ 'pur.order.lines' | translate }}</h3>
        <div class="scroll"><table data-testid="pur-order-lines"><thead><tr><th>#</th><th>{{ 'pur.order.item' | translate }}</th>
          <th class="num">{{ 'pur.order.quantity' | translate }}</th><th class="num">{{ 'pur.order.unitPrice' | translate }}</th>
          <th class="num">{{ 'pur.order.taxRate' | translate }}</th><th class="num">{{ 'pur.order.lineGross' | translate }}</th>
          <th>{{ 'pur.order.award' | translate }}</th><th>{{ 'pur.order.requisition' | translate }}</th>
          <th class="num">{{ 'pur.order.requisitionQuantity' | translate }}</th><th class="num">{{ 'pur.order.orderedQuantity' | translate }}</th>
          <th class="num">{{ 'pur.order.remainingQuantity' | translate }}</th></tr></thead>
          <tbody>@for (line of d.lines; track line.id) { <tr [attr.data-testid]="'pur-order-line-' + line.lineNo">
            <td>{{ line.lineNo }}</td><td>{{ line.itemId }}</td><td class="num">{{ line.quantity }}</td>
            <td class="num">{{ line.unitPrice }}</td><td class="num">{{ line.taxRatePercent }}%</td><td class="num">{{ money(line.originalGross) }}</td>
            <td>{{ line.awardId ?? '—' }}</td>
            <td>{{ line.requisitionDocumentNo ? line.requisitionDocumentNo + ' #' + line.requisitionLineNo : '—' }}</td>
            <td class="num">{{ line.requisitionQuantity ?? '—' }}</td><td class="num">{{ line.requisitionOrderedQuantity ?? '—' }}</td>
            <td class="num">{{ line.requisitionRemainingQuantity ?? '—' }}</td></tr> }</tbody></table></div>
        <h3>{{ 'pur.order.approvalHistory' | translate }}</h3>
        @if (d.approvalHistory.length === 0) { <p class="hint" data-testid="pur-order-no-history">{{ 'pur.order.noHistory' | translate }}</p> }
        @for (round of d.approvalHistory; track round.instanceId) { <div class="round" [attr.data-testid]="'pur-order-round-' + round.instanceId">
          <p><strong>{{ roundLabel(round.status) | translate }}</strong> · {{ 'pur.order.submittedAt' | translate }} {{ time(round.submittedAt) }}</p>
          <ol>@for (step of round.steps; track step.stepNo) { <li>{{ step.name }} — {{ 'pur.requisition.step.' + step.status.toLowerCase() | translate }}
            @if (step.actedAt) { · {{ time(step.actedAt) }} } @if (step.comment) { · “{{ step.comment }}” }</li> }</ol></div> }
      </section> }
    </section>`,
  styles: `.orders{display:grid;gap:16px;color:var(--erp-color-text)}header{display:flex;gap:8px;align-items:center}h1{font-size:20px;margin:0;text-wrap:balance}
    .filters{display:flex;gap:8px;align-items:end;flex-wrap:wrap}.filters label{display:grid;gap:4px;font-size:12px}
    .filters input,.filters select{min-height:32px;background:var(--erp-color-bg-container);color:var(--erp-color-text);border:1px solid var(--erp-color-border);border-radius:4px;padding:0 6px}
    .filters input[type=number]{width:120px}.field-error{color:var(--erp-color-error-text);margin:0}
    .export{display:flex;gap:8px;align-items:center;flex-wrap:wrap}.hint{color:var(--erp-color-text-secondary);font-size:12px;margin:0}
    .num{text-align:right;font-variant-numeric:tabular-nums}
    .detail{padding:16px;border:1px solid var(--erp-color-border);border-radius:8px;display:grid;gap:12px;min-width:0}
    .detail-head{display:flex;gap:8px;align-items:center;flex-wrap:wrap}.detail-head h2{margin:0;font-size:18px}.spacer{flex:1}
    .facts{display:grid;grid-template-columns:repeat(auto-fill,minmax(180px,1fr));gap:8px 16px;margin:0}.facts dt{font-size:12px;color:var(--erp-color-text-secondary)}
    .facts dd{margin:0;overflow-wrap:anywhere;font-variant-numeric:tabular-nums}.facts .wide{grid-column:1/-1}h3{margin:0;font-size:15px}
    .scroll{overflow-x:auto}.detail table{width:100%;border-collapse:collapse}.detail th,.detail td{padding:6px;text-align:left;border-bottom:1px solid var(--erp-color-border)}
    .detail th.num,.detail td.num{text-align:right}.round p{margin:0}.round ol{margin:4px 0 0;padding-left:20px}
    @media print{.orders>header,.filters,.field-error,.export,.export-result,nz-table,erp-api-error-display,.detail-head button{display:none!important}
      .orders{display:block;color:#000;background:#fff}.detail{border:0;padding:0}.detail *{color:#000!important;background:#fff!important;border-color:#bbb!important}.scroll{overflow:visible}}`,
})
export class PurchaseOrderPage {
  private readonly api = inject(PurchaseOrderService);
  private readonly company = inject(CompanyContextService);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  readonly statuses = PURCHASE_ORDER_STATUSES;
  readonly sorts = PURCHASE_ORDER_SORTS;
  readonly rows = signal<PurchaseOrderSummary[]>([]);
  readonly detail = signal<PurchaseOrderDetail | null>(null);
  readonly total = signal(0); readonly page = signal(1); readonly pageSize = signal(20);
  readonly loading = signal(false); readonly listFailed = signal(false); readonly filtered = signal(false);
  readonly error = signal<string | null>(null); readonly traceId = signal<string | null>(null);
  readonly filterIssue = signal<PurchaseOrderFilterIssue | null>(null);
  readonly printing = signal(false);
  readonly exporting = signal(false);
  readonly exportNotice = signal<{ type: 'success' | 'warning'; key: string; count: number } | null>(null);
  readonly exportFailure = signal<PurchaseOrderExportFailure | null>(null);
  /** Why export is unavailable right now (shown as the button hint); null = ready. */
  readonly exportBlockedKey = computed(() => this.exporting() ? 'pur.order.exportBusy' : this.loading() ? 'pur.order.exportWaitLoad' :
    this.listFailed() ? 'pur.order.exportNeedsList' : this.total() === 0 ? (this.filtered() ? 'pur.order.exportNoRows' : 'pur.order.exportNoOrders') : null);
  filter: PurchaseOrderFilter = { ...EMPTY_FILTER };
  sort: string = PURCHASE_ORDER_SORTS[0];
  private applied: PurchaseOrderFilter & { sort: string } = { ...EMPTY_FILTER, sort: PURCHASE_ORDER_SORTS[0] };
  private generation = 0; private detailGeneration = 0; private exportEpoch = 0;
  private exportRequest?: Subscription;
  private firstLoad = true;

  constructor() {
    const params = this.route.snapshot.queryParamMap;
    const id = (key: string) => { const n = Number(params.get(key)); return Number.isSafeInteger(n) && n > 0 ? n : null; };
    const status = params.get('status') ?? '';
    const sort = params.get('sort') ?? '';
    this.filter = { keyword: params.get('keyword') ?? '', from: params.get('from') ?? '', to: params.get('to') ?? '',
      status: (PURCHASE_ORDER_STATUSES as readonly string[]).includes(status) ? status : '',
      supplierId: id('supplierId'), requisitionLineId: id('requisitionLineId') };
    if ((PURCHASE_ORDER_SORTS as readonly string[]).includes(sort)) this.sort = sort;
    const page = id('page'); if (page) this.page.set(page);
    effect(() => { this.company.current(); untracked(() => { this.search(!this.firstLoad); this.firstLoad = false; }); });
    inject(DestroyRef).onDestroy(() => { this.generation++; this.detailGeneration++; this.stopExport(); });
  }

  tag(status: PurchaseOrderStatus): DocumentStatus { return TAG_STATUS[status] ?? 'Draft'; }
  statusLabel(status: string): string { return `pur.order.state.${status.charAt(0).toLowerCase()}${status.slice(1)}`; }
  /** SYS round statuses use the same words as the order status tag (Pending reads as pending approval). */
  roundLabel(status: string): string { return this.statusLabel(status === 'Pending' ? 'PendingApproval' : status); }
  sortLabel(sort: string): string { return 'pur.order.sortBy.' + (sort.startsWith('-') ? sort.slice(1) + 'Desc' : sort + 'Asc'); }
  money(value: string | null): string { return value === null ? '—' : formatDecimalText(value, 'en-US'); }
  time(value: string | null): string { return displayTime(value); }

  /** Applies the typed filters; invalid input is explained per field and nothing is requested. */
  search(resetPage = true): void {
    const issue = purchaseOrderFilterIssue(this.filter);
    this.filterIssue.set(issue);
    if (issue) return;
    this.clearExport();
    this.applied = { keyword: this.filter.keyword.trim(), from: this.filter.from, to: this.filter.to, status: this.filter.status,
      supplierId: this.filter.supplierId, requisitionLineId: this.filter.requisitionLineId, sort: this.sort };
    const a = this.applied;
    this.filtered.set(!!(a.keyword || a.from || a.to || a.status || a.supplierId || a.requisitionLineId));
    if (resetPage) this.page.set(1);
    this.updateUrl(); this.load();
  }
  reset(): void { this.filter = { ...EMPTY_FILTER }; this.sort = PURCHASE_ORDER_SORTS[0]; this.search(); }
  onQuery(query: NzTableQueryParams): void {
    if (query.pageIndex === this.page() && query.pageSize === this.pageSize()) return;
    this.page.set(query.pageIndex); this.pageSize.set(query.pageSize); this.updateUrl(); this.load();
  }
  private updateUrl(): void {
    const a = this.applied;
    void this.router.navigate([], { relativeTo: this.route, replaceUrl: true, queryParams: {
      keyword: a.keyword || null, from: a.from || null, to: a.to || null, status: a.status || null, supplierId: a.supplierId,
      requisitionLineId: a.requisitionLineId, sort: a.sort === PURCHASE_ORDER_SORTS[0] ? null : a.sort,
      page: this.page() > 1 ? this.page() : null } });
  }
  load(keepError = false): void {
    const current = ++this.generation; ++this.detailGeneration;
    this.detail.set(null); this.printing.set(false); this.loading.set(true); this.listFailed.set(false);
    if (!keepError) this.clearError();
    this.api.list({ ...this.applied, page: this.page(), pageSize: this.pageSize() }).subscribe({
      next: result => { if (current !== this.generation) return;
        this.rows.set(result.items); this.total.set(result.totalCount); this.loading.set(false); },
      error: failure => { if (current !== this.generation) return;
        this.rows.set([]); this.total.set(0); this.listFailed.set(true); this.showError(failure); this.loading.set(false); },
    });
  }
  view(row: PurchaseOrderSummary): void { this.read(row.id, false); }
  closeDetail(): void { ++this.detailGeneration; this.detail.set(null); this.printing.set(false); }
  /** Print always re-reads the saved order first; a failed read shows the reason and never opens the print dialog. */
  print(d: PurchaseOrderDetail): void { if (!this.printing()) this.read(d.id, true); }

  exportCsv(): void {
    if (this.exportBlockedKey()) return;
    const epoch = ++this.exportEpoch;
    this.exporting.set(true); this.exportNotice.set(null); this.exportFailure.set(null);
    this.exportRequest = this.api.export(this.applied).subscribe({
      next: response => void checkCsvExport(response).then(outcome => {
        if (epoch !== this.exportEpoch) return;
        this.exporting.set(false);
        if (outcome.kind === 'invalid') { this.exportFailure.set({ key: 'pur.order.exportIncomplete', traceId: null, details: [] }); return; }
        if (outcome.kind === 'empty') { this.exportNotice.set({ type: 'warning', key: 'pur.order.exportEmpty', count: 0 }); return; }
        saveBlob(outcome.blob, PURCHASE_ORDER_EXPORT_FILE);
        this.exportNotice.set({ type: 'success', key: 'pur.order.exported', count: outcome.count });
      }),
      error: failure => { if (epoch !== this.exportEpoch) return;
        this.exporting.set(false); this.exportFailure.set(purchaseOrderExportFailure(failure)); },
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
