import { ChangeDetectionStrategy, Component, effect, inject, signal, untracked } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { TranslatePipe } from '@ngx-translate/core';
import { NzButtonModule } from 'ng-zorro-antd/button';
import { NzInputModule } from 'ng-zorro-antd/input';
import { NzTableModule, NzTableQueryParams } from 'ng-zorro-antd/table';
import { CompanyContextService } from '../../core/company/company-context.service';
import { apiErrorTraceId } from '../../core/http/api-error';
import { ApiErrorDisplay } from '../../shared/api-error-display';
import { displayTime } from '../../shared/date';
import { formatDecimalText, isZeroDecimalText } from '../../shared/decimal-text';
import { DocumentStatus, StatusTag } from '../../shared/status-tag/status-tag';
import {
  PURCHASE_INVOICE_STATUSES, PurchaseInvoiceDetail, PurchaseInvoiceService, PurchaseInvoiceStatus, PurchaseInvoiceSummary,
  purchaseInvoiceFilterIssue,
} from './purchase-invoice.service';

/** Shared tag tone: Disputed waits for a decision (pending tone), Matched is ready but not yet posted (approved tone). */
const TAG_STATUS: Record<PurchaseInvoiceStatus, DocumentStatus> = {
  Draft: 'Draft', Matched: 'Approved', Disputed: 'Submitted', Rejected: 'Rejected', Posted: 'Posted',
};

/**
 * 介面 4.19 Purchase Invoice View (WU-PUR-008c1, REQ_F4114.8.1): company-wide list with supplier invoice number / status /
 * date / supplier / order filters, server sorting and paging, and a read-only detail with saved lines and the RESOLVE decision
 * history. Create, revise, MATCH, RESOLVE and export are 008c2／008c3; FI posting is 008b, so a Matched invoice is never
 * described as posted to payables.
 */
@Component({
  selector: 'erp-purchase-invoice-page',
  imports: [FormsModule, TranslatePipe, NzButtonModule, NzInputModule, NzTableModule, ApiErrorDisplay, StatusTag],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <section class="invoices">
      <header><h1>{{ 'pur.invoice.title' | translate }}</h1></header>
      <p class="hint">{{ 'pur.invoice.intro' | translate }}</p>
      <form class="filters" (ngSubmit)="search()" data-testid="pur-invoice-filters">
        <input nz-input type="search" name="keyword" autocomplete="off" spellcheck="false" maxlength="80" [(ngModel)]="keyword"
          data-testid="pur-invoice-keyword" [placeholder]="'pur.invoice.keywordHint' | translate" [attr.aria-label]="'pur.invoice.keyword' | translate" />
        <label>{{ 'pur.invoice.status' | translate }}
          <select name="status" autocomplete="off" [(ngModel)]="status" data-testid="pur-invoice-status-filter">
            <option value="">{{ 'pur.invoice.allStatuses' | translate }}</option>
            @for (value of statuses; track value) { <option [value]="value">{{ statusLabel(value) | translate }}</option> }
          </select></label>
        <label>{{ 'pur.invoice.from' | translate }} <input type="date" name="from" autocomplete="off" [(ngModel)]="from" data-testid="pur-invoice-from" /></label>
        <label>{{ 'pur.invoice.to' | translate }} <input type="date" name="to" autocomplete="off" [(ngModel)]="to" data-testid="pur-invoice-to" /></label>
        <label>{{ 'pur.invoice.supplierId' | translate }} <input type="number" min="1" step="1" inputmode="numeric" name="supplierId" autocomplete="off"
          [(ngModel)]="supplierId" data-testid="pur-invoice-supplier" /></label>
        <label>{{ 'pur.invoice.orderId' | translate }} <input type="number" min="1" step="1" inputmode="numeric" name="purchaseOrderId" autocomplete="off"
          [(ngModel)]="purchaseOrderId" data-testid="pur-invoice-order" /></label>
        <button nz-button nzType="primary" type="submit" data-testid="pur-invoice-search">{{ 'pur.invoice.search' | translate }}</button>
        <button nz-button type="button" data-testid="pur-invoice-reset" (click)="reset()">{{ 'pur.invoice.reset' | translate }}</button>
      </form>
      @if (filterIssue(); as issue) { <p class="field-error" role="alert" data-testid="pur-invoice-filter-error">{{ 'pur.invoice.filterIssue.' + issue | translate }}</p> }
      @if (error()) { <erp-api-error-display data-testid="pur-invoice-error" [messageKey]="error()!" [traceId]="traceId()" /> }
      <nz-table #table [nzData]="rows()" [nzLoading]="loading()" [nzFrontPagination]="false" [nzTotal]="total()"
        [nzPageIndex]="page()" [nzPageSize]="pageSize()" [nzShowSizeChanger]="true" (nzQueryParams)="onQuery($event)"
        [nzNoResult]="(listFailed() ? 'pur.invoice.loadFailed' : filtered() ? 'pur.invoice.searchEmpty' : 'pur.invoice.empty') | translate"
        data-testid="pur-invoice-table">
        <thead><tr>
          <th nzColumnKey="supplierInvoiceNumber" [nzSortFn]="true">{{ 'pur.invoice.number' | translate }}</th>
          <th nzColumnKey="businessDate" [nzSortFn]="true" nzSortOrder="descend">{{ 'pur.invoice.businessDate' | translate }}</th>
          <th>{{ 'pur.invoice.supplier' | translate }}</th>
          <th>{{ 'pur.invoice.order' | translate }}</th>
          <th nzAlign="right">{{ 'pur.invoice.lineCount' | translate }}</th>
          <th nzAlign="right">{{ 'pur.invoice.totalNet' | translate }}</th>
          <th nzAlign="right">{{ 'pur.invoice.totalTax' | translate }}</th>
          <th nzColumnKey="totalGross" [nzSortFn]="true" nzAlign="right">{{ 'pur.invoice.totalGross' | translate }}</th>
          <th>{{ 'pur.invoice.status' | translate }}</th>
        </tr></thead>
        <tbody>@for (row of table.data; track row.id) {
          <tr [attr.data-testid]="'pur-invoice-row-' + row.id">
            <td class="ref"><button nz-button nzType="link" class="doc-link" type="button" [attr.data-testid]="'pur-invoice-view-' + row.id"
              (click)="view(row)">{{ row.supplierInvoiceNumber }}</button></td>
            <td class="date">{{ row.businessDate }}</td>
            <td>#{{ row.supplierId }}</td>
            <td>#{{ row.purchaseOrderId }}</td>
            <td class="num">{{ row.lineCount }}</td>
            <td class="num">{{ money(row.totalNet) }}</td>
            <td class="num">{{ money(row.totalTax) }}</td>
            <td class="num" [attr.data-testid]="'pur-invoice-gross-' + row.id">{{ money(row.totalGross) }} {{ row.currency }}</td>
            <td><erp-status-tag [status]="tagStatus(row.status)" [labelKey]="statusLabel(row.status)" [testId]="'pur-invoice-status-' + row.id" /></td>
          </tr>
        }</tbody>
      </nz-table>
      @if (detail(); as d) {
        <section class="detail" data-testid="pur-invoice-detail" [attr.aria-label]="d.supplierInvoiceNumber">
          <header><h2>{{ d.supplierInvoiceNumber }} <erp-status-tag [status]="tagStatus(d.status)" [labelKey]="statusLabel(d.status)" testId="pur-invoice-detail-status" /></h2>
            <button nz-button type="button" data-testid="pur-invoice-detail-close" (click)="detail.set(null)">{{ 'pur.invoice.close' | translate }}</button></header>
          <p class="state-hint" data-testid="pur-invoice-state-hint">{{ 'pur.invoice.stateHint.' + d.status.toLowerCase() | translate }}</p>
          <dl>
            <dt>{{ 'pur.invoice.supplier' | translate }}</dt><dd>#{{ d.supplierId }}</dd>
            <dt>{{ 'pur.invoice.order' | translate }}</dt><dd>#{{ d.purchaseOrderId }}</dd>
            <dt>{{ 'pur.invoice.businessDate' | translate }}</dt><dd>{{ d.businessDate }}</dd>
            <dt>{{ 'pur.invoice.totalNet' | translate }}</dt><dd>{{ money(d.totalNet) }} {{ d.currency }}</dd>
            <dt>{{ 'pur.invoice.totalTax' | translate }}</dt><dd>{{ money(d.totalTax) }} {{ d.currency }}</dd>
            <dt>{{ 'pur.invoice.totalGross' | translate }}</dt><dd data-testid="pur-invoice-detail-gross">{{ money(d.totalGross) }} {{ d.currency }}</dd>
            <dt>{{ 'pur.invoice.version' | translate }}</dt><dd>{{ d.version }}</dd>
            <dt>{{ 'pur.invoice.lastChanged' | translate }}</dt><dd>{{ time(d.updatedAt ?? d.createdAt) }}</dd>
          </dl>
          <h3>{{ 'pur.invoice.lines' | translate }}</h3>
          <div class="scroll"><table data-testid="pur-invoice-lines"><thead><tr><th>#</th><th>{{ 'pur.invoice.receiptLine' | translate }}</th>
            <th class="num">{{ 'pur.invoice.quantity' | translate }}</th><th class="num">{{ 'pur.invoice.unitPrice' | translate }}</th>
            <th class="num">{{ 'pur.invoice.taxRate' | translate }}</th><th class="num">{{ 'pur.invoice.net' | translate }}</th>
            <th class="num">{{ 'pur.invoice.tax' | translate }}</th><th class="num">{{ 'pur.invoice.gross' | translate }}</th></tr></thead>
            <tbody>@for (line of d.lines; track line.id) { <tr [attr.data-testid]="'pur-invoice-line-' + line.lineNo">
              <td>{{ line.lineNo }}</td><td>#{{ line.receiptLineId }}</td><td class="num">{{ money(line.quantity) }}</td>
              <td class="num">{{ money(line.unitPrice) }}</td><td class="num">{{ money(line.taxRatePercent) }}%</td>
              <td class="num">{{ money(line.net) }}</td><td class="num">{{ money(line.tax) }}</td><td class="num">{{ money(line.gross) }}</td></tr> }
            </tbody></table></div>
          <section class="history" data-testid="pur-invoice-history"><h3>{{ 'pur.invoice.history' | translate }}</h3>
            @if (!d.resolutions.length) { <p class="muted" data-testid="pur-invoice-no-history">{{ 'pur.invoice.noHistory' | translate }}</p> }
            @else { <div class="scroll"><table><thead><tr><th>{{ 'pur.invoice.decidedVersion' | translate }}</th><th>#</th>
              <th>{{ 'pur.invoice.action' | translate }}</th><th>{{ 'pur.invoice.reason' | translate }}</th>
              <th class="num">{{ 'pur.invoice.netDifference' | translate }}</th><th class="num">{{ 'pur.invoice.taxDifference' | translate }}</th>
              <th class="num">{{ 'pur.invoice.grossDifference' | translate }}</th><th>{{ 'pur.invoice.decidedBy' | translate }}</th>
              <th>{{ 'pur.invoice.decidedAt' | translate }}</th></tr></thead>
              <tbody>@for (r of d.resolutions; track r.id) { <tr [attr.data-testid]="'pur-invoice-decision-' + r.id">
                <td>{{ r.invoiceVersion }}</td><td>{{ r.lineNo }}</td>
                <td [class.reject]="r.action === 'Reject'">{{ actionLabel(r.action) | translate }}</td><td class="ref">{{ r.reason }}</td>
                <td class="num">{{ diff(r.netDifference) }}</td><td class="num">{{ diff(r.taxDifference) }}</td>
                <td class="num">{{ diff(r.grossDifference) }}</td><td>#{{ r.decidedBy }}</td><td class="date">{{ time(r.decidedAt) }}</td></tr> }
              </tbody></table></div> }
          </section>
        </section>
      }
    </section>`,
  styles: `:host{display:block;min-width:0}.invoices{display:grid;gap:16px;color:var(--erp-color-text);min-width:0}header{display:flex;gap:8px;align-items:center;justify-content:space-between;flex-wrap:wrap}h1,h2,h3{margin:0}h1{font-size:20px;text-wrap:balance}h2{font-size:16px;display:flex;gap:8px;align-items:center;overflow-wrap:anywhere}h3{font-size:14px}.hint,.muted,.state-hint{margin:0;color:var(--erp-color-text-secondary)}.filters{display:flex;gap:8px;align-items:center;flex-wrap:wrap}.filters input[type=search]{max-width:260px}.filters input[type=number]{width:110px}.filters label{display:flex;gap:4px;align-items:center}.filters select,.filters input[type=date],.filters input[type=number]{min-height:32px;background-color:var(--erp-color-bg-container);color:var(--erp-color-text);border:1px solid var(--erp-color-border);border-radius:4px}.field-error{margin:0;color:var(--erp-color-error-text)}.num{text-align:right;font-variant-numeric:tabular-nums;white-space:nowrap}.ref{overflow-wrap:anywhere;min-width:8em}.date{white-space:nowrap}.doc-link{padding:0;height:auto;white-space:normal;text-align:left}.detail{display:grid;gap:12px;padding:16px;border:1px solid var(--erp-color-border);border-radius:8px;background:var(--erp-color-bg-container);min-width:0}.detail dl{display:grid;grid-template-columns:minmax(120px,180px) 1fr;gap:8px;margin:0}.detail dt{font-weight:600}.detail dd{margin:0;min-width:0}.scroll{overflow-x:auto}.detail table{width:100%;border-collapse:collapse}.detail td,.detail th{padding:6px;text-align:left;border-bottom:1px solid var(--erp-color-border)}.detail th.num,.detail td.num{text-align:right}.reject{color:var(--erp-color-error-text);font-weight:600}button:focus-visible,input:focus-visible,select:focus-visible{outline:2px solid var(--erp-color-primary);outline-offset:2px}@media (max-width:600px){.detail dl{grid-template-columns:1fr}}`,
})
export class PurchaseInvoicePage {
  private readonly api = inject(PurchaseInvoiceService);
  private readonly company = inject(CompanyContextService);
  readonly statuses = PURCHASE_INVOICE_STATUSES;
  readonly rows = signal<PurchaseInvoiceSummary[]>([]);
  readonly detail = signal<PurchaseInvoiceDetail | null>(null);
  readonly total = signal(0); readonly page = signal(1); readonly pageSize = signal(20);
  readonly loading = signal(false); readonly listFailed = signal(false); readonly filtered = signal(false);
  readonly error = signal<string | null>(null); readonly traceId = signal<string | null>(null);
  readonly filterIssue = signal<'range' | 'supplier' | 'order' | null>(null);
  keyword = ''; status = ''; from = ''; to = ''; supplierId: number | null = null; purchaseOrderId: number | null = null;
  private applied = { keyword: '', status: '', from: '', to: '', supplierId: null as number | null, purchaseOrderId: null as number | null };
  private sort = '';
  private generation = 0; private detailGeneration = 0;

  constructor() {
    effect(() => { this.company.current(); untracked(() => this.reset()); });
  }

  tagStatus(status: PurchaseInvoiceStatus): DocumentStatus { return TAG_STATUS[status] ?? 'Draft'; }
  statusLabel(status: string): string { return `pur.invoice.state.${status.toLowerCase()}`; }
  actionLabel(action: string): string { return action === 'Reject' ? 'pur.invoice.reject' : 'pur.invoice.accept'; }
  money(value: string): string { return formatDecimalText(value, 'en-US'); }
  /** A signed difference: positive values carry an explicit "+" so the direction is never ambiguous. */
  diff(value: string): string { return isZeroDecimalText(value) || value.startsWith('-') ? this.money(value) : '+' + this.money(value); }
  time(value: string | null): string { return displayTime(value); }

  reset(): void {
    this.keyword = ''; this.status = ''; this.from = ''; this.to = ''; this.supplierId = null; this.purchaseOrderId = null; this.search();
  }
  search(): void {
    const supplierId = this.supplierId ?? null; const purchaseOrderId = this.purchaseOrderId ?? null;
    const issue = purchaseInvoiceFilterIssue({ from: this.from, to: this.to, supplierId, purchaseOrderId });
    this.filterIssue.set(issue);
    if (issue) return;
    this.applied = { keyword: this.keyword.trim(), status: this.status, from: this.from, to: this.to, supplierId, purchaseOrderId };
    this.filtered.set(!!(this.applied.keyword || this.applied.status || this.applied.from || this.applied.to || supplierId || purchaseOrderId));
    this.page.set(1); this.load();
  }
  onQuery(query: NzTableQueryParams): void {
    const sorted = query.sort.find(x => x.value);
    const sort = sorted ? (sorted.value === 'descend' ? '-' : '') + sorted.key : '';
    if (query.pageIndex === this.page() && query.pageSize === this.pageSize() && sort === this.sort) return;
    this.page.set(query.pageIndex); this.pageSize.set(query.pageSize); this.sort = sort; this.load();
  }
  load(): void {
    const current = ++this.generation; ++this.detailGeneration;
    this.detail.set(null); this.loading.set(true); this.listFailed.set(false); this.clearError();
    this.api.list({ page: this.page(), pageSize: this.pageSize(), ...this.applied, sort: this.sort }).subscribe({
      next: result => { if (current !== this.generation) return; this.rows.set(result.items); this.total.set(result.totalCount); this.loading.set(false); },
      error: e => { if (current !== this.generation) return; this.rows.set([]); this.total.set(0); this.listFailed.set(true); this.fail(e); this.loading.set(false); },
    });
  }
  view(row: PurchaseInvoiceSummary): void {
    const current = ++this.detailGeneration; this.clearError(); this.detail.set(null);
    this.api.get(row.id).subscribe({
      next: d => { if (current === this.detailGeneration) this.detail.set(d); },
      // 404: deleted or moved out of reach since the list loaded — say why, then show the current list.
      error: e => { if (current !== this.detailGeneration) return; this.load(); this.fail(e); },
    });
  }
  private clearError(): void { this.error.set(null); this.traceId.set(null); }
  private fail(error: unknown): void { this.error.set(this.api.errorKey(error)); this.traceId.set(apiErrorTraceId(error)); }
}
