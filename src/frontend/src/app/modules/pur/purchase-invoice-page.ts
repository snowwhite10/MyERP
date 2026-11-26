import { ChangeDetectionStrategy, Component, DestroyRef, ElementRef, Injector, afterNextRender, computed, effect, inject, signal, untracked } from '@angular/core';
import { HttpErrorResponse } from '@angular/common/http';
import { Observable, Subscription } from 'rxjs';
import { FormsModule } from '@angular/forms';
import { TranslatePipe } from '@ngx-translate/core';
import { NzAlertModule } from 'ng-zorro-antd/alert';
import { NzButtonModule } from 'ng-zorro-antd/button';
import { NzInputModule } from 'ng-zorro-antd/input';
import { NzTableModule, NzTableQueryParams } from 'ng-zorro-antd/table';
import { HasPermissionDirective } from '../../core/auth/has-permission.directive';
import { PermissionService } from '../../core/auth/permission.service';
import { CompanyContextService } from '../../core/company/company-context.service';
import { ErpDialogService } from '../../shared/dialogs/dialog.service';
import { apiErrorTraceId } from '../../core/http/api-error';
import { ApiErrorDisplay } from '../../shared/api-error-display';
import { displayTime } from '../../shared/date';
import { formatDecimalText, isZeroDecimalText } from '../../shared/decimal-text';
import { checkCsvExport, saveBlob } from '../../shared/export/csv-export';
import { DocumentStatus, StatusTag } from '../../shared/status-tag/status-tag';
import {
  PURCHASE_INVOICE_RELOAD_CODES, PURCHASE_INVOICE_STATUSES, PurchaseInvoiceAction, PurchaseInvoiceActionResult,
  PurchaseInvoiceDecisionInput, PurchaseInvoiceDetail, PurchaseInvoiceDifference, PurchaseInvoiceService, PurchaseInvoiceStatus,
  PurchaseInvoiceSummary, RESOLVE_REASON_MAX, disputedInvoiceLines, purchaseInvoiceFilterIssue, purchaseInvoiceResolveIssues,
} from './purchase-invoice.service';
import { PURCHASE_INVOICE_EXPORT_FILE, PurchaseInvoiceExportFailure, purchaseInvoiceExportFailure } from './purchase-invoice-export';

/** Shared tag tone: Disputed waits for a decision (pending tone), Matched is ready but not yet posted (approved tone). */
const TAG_STATUS: Record<PurchaseInvoiceStatus, DocumentStatus> = {
  Draft: 'Draft', Matched: 'Approved', Disputed: 'Submitted', Rejected: 'Rejected', Posted: 'Posted',
};

/**
 * 介面 4.19 Purchase Invoice View (WU-PUR-008c1, REQ_F4114.8.1): company-wide list with supplier invoice number / status /
 * date / supplier / order filters, server sorting and paging, and a read-only detail with saved lines and the RESOLVE decision
 * history. WU-PUR-008c3b adds MATCH for a Draft and per-line Accept / Reject with a reason for a Disputed invoice; 008c3c2 adds
 * CSV export of the applied filters (PUR.INVOICE.EXPORT); create and revise are 008c2; FI posting is 008b, so a Matched
 * invoice is never described as posted to payables.
 */
@Component({
  selector: 'erp-purchase-invoice-page',
  imports: [FormsModule, TranslatePipe, NzAlertModule, NzButtonModule, NzInputModule, NzTableModule, ApiErrorDisplay, StatusTag, HasPermissionDirective],
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
      <div class="export" *erpHasPermission="'PUR.INVOICE.EXPORT'">
        <button nz-button type="button" [disabled]="!!exportBlockedKey()" [nzLoading]="exporting()" (click)="exportCsv()"
          aria-describedby="pur-invoice-export-hint" data-testid="pur-invoice-export">{{ (exporting() ? 'pur.invoice.exporting' : 'pur.invoice.export') | translate }}</button>
        <span id="pur-invoice-export-hint" class="hint" data-testid="pur-invoice-export-hint">{{ (exportBlockedKey() ?? 'pur.invoice.exportScope') | translate }}</span>
      </div>
      <div class="export-result" aria-live="polite" *erpHasPermission="'PUR.INVOICE.EXPORT'">
        @if (exportNotice(); as notice) { <nz-alert [nzType]="notice.type" nzShowIcon
          [nzMessage]="notice.key | translate: { count: notice.count.toLocaleString() }" data-testid="pur-invoice-export-notice" /> }
        @if (exportFailure(); as failure) { <erp-api-error-display [messageKey]="failure.key" [traceId]="failure.traceId"
          [details]="failure.details" data-testid="pur-invoice-export-error" /> }
      </div>
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
          @if (d.status === 'Draft') { <div class="actions">
            <button nz-button nzType="primary" type="button" *erpHasPermission="'PUR.INVOICE.MATCH'" data-testid="pur-invoice-match"
              [disabled]="busy()" [attr.aria-busy]="busy()" (click)="match(d)">{{ (busy() ? 'pur.invoice.busy' : 'pur.invoice.match') | translate }}</button>
          </div> }
          @if (actionError() && !(d.status === 'Disputed' && disputed().length && canResolve())) { <erp-api-error-display data-testid="pur-invoice-action-error" [messageKey]="actionError()!" [traceId]="actionTraceId()" /> }
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
          @if (d.status === 'Disputed') {
            <section class="resolve" data-testid="pur-invoice-resolve" aria-labelledby="pur-invoice-resolve-title">
              <h3 id="pur-invoice-resolve-title">{{ 'pur.invoice.differences' | translate }}</h3>
              @if (!disputed().length) { <p class="field-error" role="alert" data-testid="pur-invoice-no-differences">{{ 'pur.invoice.noDifferences' | translate }}</p> }
              @else {
                @if (canResolve()) { <p class="muted">{{ 'pur.invoice.resolveHint' | translate }}</p> }
                @else { <p class="muted" data-testid="pur-invoice-resolve-permission">{{ 'pur.invoice.resolvePermission' | translate }}</p> }
                <form class="decisions" novalidate (ngSubmit)="resolve(d)">
                  @for (line of disputed(); track line.lineNo) {
                    <fieldset class="dispute" [attr.data-testid]="'pur-invoice-dispute-' + line.lineNo">
                      <legend>{{ 'pur.invoice.lineLabel' | translate: { number: line.lineNo } }}</legend>
                      <ul class="diffs">@for (x of line.differences; track x.kind) {
                        <li>{{ kindLabel(x.kind) | translate }} {{ amount(x, x.baseline) }} → {{ amount(x, x.invoice) }} ({{ signed(x) }})</li> }</ul>
                      @if (canResolve()) {
                        <div class="choice" role="radiogroup" [attr.aria-label]="'pur.invoice.action' | translate"
                          [attr.aria-invalid]="issues()[line.lineNo] === 'action'" [attr.aria-describedby]="issues()[line.lineNo] === 'action' ? 'pur-invoice-issue-' + line.lineNo : null">
                          <label><input type="radio" [name]="'decision-' + line.lineNo" value="Accept" [checked]="decision(line.lineNo).action === 'Accept'"
                            [disabled]="busy()" (change)="setAction(line.lineNo, 'Accept')" [attr.data-testid]="'pur-invoice-accept-' + line.lineNo" />
                            {{ 'pur.invoice.acceptAction' | translate }}</label>
                          <label><input type="radio" [name]="'decision-' + line.lineNo" value="Reject" [checked]="decision(line.lineNo).action === 'Reject'"
                            [disabled]="busy()" (change)="setAction(line.lineNo, 'Reject')" [attr.data-testid]="'pur-invoice-reject-' + line.lineNo" />
                            {{ 'pur.invoice.rejectAction' | translate }}</label>
                        </div>
                        <label class="reason">{{ 'pur.invoice.reason' | translate }}
                          <textarea nz-input rows="2" [attr.maxlength]="reasonMax" autocomplete="off" [name]="'reason-' + line.lineNo"
                            [value]="decision(line.lineNo).reason" [disabled]="busy()" (input)="setReason(line.lineNo, $any($event.target).value)"
                            [attr.aria-invalid]="isReasonIssue(line.lineNo)" [attr.aria-describedby]="isReasonIssue(line.lineNo) ? 'pur-invoice-issue-' + line.lineNo : null"
                            [attr.data-testid]="'pur-invoice-reason-' + line.lineNo"></textarea></label>
                        @if (issues()[line.lineNo]; as issue) {
                          <p class="field-error" aria-live="polite" [id]="'pur-invoice-issue-' + line.lineNo" [attr.data-testid]="'pur-invoice-issue-' + line.lineNo">{{ 'pur.invoice.resolveIssue.' + issue | translate }}</p> }
                      }
                    </fieldset>
                  }
                  @if (canResolve()) {
                    <!-- Shown next to the submit button so the reason is seen where the user acted. -->
                    @if (actionError()) { <erp-api-error-display data-testid="pur-invoice-action-error" [messageKey]="actionError()!" [traceId]="actionTraceId()" /> }
                    <div class="actions">
                    <button nz-button nzType="primary" type="submit" data-testid="pur-invoice-resolve-submit" [disabled]="busy()" [attr.aria-busy]="busy()">
                      {{ (busy() ? 'pur.invoice.busy' : 'pur.invoice.resolve') | translate }}</button></div> }
                </form>
              }
            </section>
          }
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
  styles: `:host{display:block;min-width:0}.invoices{display:grid;gap:16px;color:var(--erp-color-text);min-width:0}header{display:flex;gap:8px;align-items:center;justify-content:space-between;flex-wrap:wrap}h1,h2,h3{margin:0}h1{font-size:20px;text-wrap:balance}h2{font-size:16px;display:flex;gap:8px;align-items:center;overflow-wrap:anywhere}h3{font-size:14px}.hint,.muted,.state-hint{margin:0;color:var(--erp-color-text-secondary)}.filters{display:flex;gap:8px;align-items:center;flex-wrap:wrap}.filters input[type=search]{max-width:260px}.filters input[type=number]{width:110px}.filters label{display:flex;gap:4px;align-items:center}.export{display:flex;gap:12px;flex-wrap:wrap;align-items:center}.export .hint{overflow-wrap:anywhere}.export-result:empty{display:none}.filters select,.filters input[type=date],.filters input[type=number]{min-height:32px;background-color:var(--erp-color-bg-container);color:var(--erp-color-text);border:1px solid var(--erp-color-border);border-radius:4px}.field-error{margin:0;color:var(--erp-color-error-text)}.num{text-align:right;font-variant-numeric:tabular-nums;white-space:nowrap}.ref{overflow-wrap:anywhere;min-width:8em}.date{white-space:nowrap}.doc-link{padding:0;height:auto;white-space:normal;text-align:left}.detail{display:grid;gap:12px;padding:16px;border:1px solid var(--erp-color-border);border-radius:8px;background:var(--erp-color-bg-container);min-width:0}.detail dl{display:grid;grid-template-columns:minmax(120px,180px) 1fr;gap:8px;margin:0}.detail dt{font-weight:600}.detail dd{margin:0;min-width:0}.scroll{overflow-x:auto}.detail table{width:100%;border-collapse:collapse}.detail td,.detail th{padding:6px;text-align:left;border-bottom:1px solid var(--erp-color-border)}.detail th.num,.detail td.num{text-align:right}.reject{color:var(--erp-color-error-text);font-weight:600}button:focus-visible,input:focus-visible,select:focus-visible,textarea:focus-visible{outline:2px solid var(--erp-color-primary);outline-offset:2px}.actions{display:flex;gap:8px;flex-wrap:wrap}.resolve,.decisions{display:grid;gap:12px;min-width:0}.dispute{display:grid;gap:8px;margin:0;padding:12px;border:1px solid var(--erp-color-border);border-radius:6px;min-width:0}.dispute legend{font-size:14px;font-weight:600;padding:0 4px;margin:0;width:auto;border:0}.diffs{margin:0;padding-left:20px;display:grid;gap:2px;font-variant-numeric:tabular-nums;overflow-wrap:anywhere}.choice{display:flex;gap:16px;flex-wrap:wrap}.choice label{display:flex;gap:6px;align-items:center;cursor:pointer}.reason{display:grid;gap:4px}.reason textarea{max-width:640px}@media (max-width:600px){.detail dl{grid-template-columns:1fr}}`,
})
export class PurchaseInvoicePage {
  private readonly api = inject(PurchaseInvoiceService);
  private readonly company = inject(CompanyContextService);
  private readonly permissions = inject(PermissionService);
  private readonly dialogs = inject(ErpDialogService);
  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef);
  private readonly injector = inject(Injector);
  readonly reasonMax = RESOLVE_REASON_MAX;
  readonly statuses = PURCHASE_INVOICE_STATUSES;
  readonly rows = signal<PurchaseInvoiceSummary[]>([]);
  readonly detail = signal<PurchaseInvoiceDetail | null>(null);
  readonly total = signal(0); readonly page = signal(1); readonly pageSize = signal(20);
  readonly loading = signal(false); readonly listFailed = signal(false); readonly filtered = signal(false);
  readonly error = signal<string | null>(null); readonly traceId = signal<string | null>(null);
  readonly filterIssue = signal<'range' | 'supplier' | 'order' | null>(null);
  readonly busy = signal(false);
  readonly actionError = signal<string | null>(null); readonly actionTraceId = signal<string | null>(null);
  /** Unsent RESOLVE input by line number; kept across a reload of the same invoice for lines that still differ. */
  readonly decisions = signal<Record<number, PurchaseInvoiceDecisionInput>>({});
  private readonly attempted = signal(false);
  readonly disputed = computed(() => disputedInvoiceLines(this.detail()?.lines ?? []));
  /** Per-line reasons appear only after the first submit attempt, then follow the input live. */
  readonly issues = computed(() => this.attempted() ? purchaseInvoiceResolveIssues(this.disputed(), this.decisions()) : {});
  keyword = ''; status = ''; from = ''; to = ''; supplierId: number | null = null; purchaseOrderId: number | null = null;
  private applied = { keyword: '', status: '', from: '', to: '', supplierId: null as number | null, purchaseOrderId: null as number | null };
  private sort = '';
  private generation = 0; private detailGeneration = 0;
  readonly exporting = signal(false);
  readonly exportNotice = signal<{ type: 'success' | 'warning'; key: string; count: number } | null>(null);
  readonly exportFailure = signal<PurchaseInvoiceExportFailure | null>(null);
  /** Why export is unavailable right now (shown as the button hint); null = ready. */
  readonly exportBlockedKey = computed(() => this.exporting() ? 'pur.invoice.exportBusy' : this.loading() ? 'pur.invoice.exportWaitLoad' :
    this.listFailed() ? 'pur.invoice.exportNeedsList' : this.total() === 0 ? (this.filtered() ? 'pur.invoice.exportNoRows' : 'pur.invoice.exportNoInvoices') : null);
  private exportRequest?: Subscription; private exportEpoch = 0;

  constructor() {
    effect(() => { this.company.current(); untracked(() => this.reset()); });
    inject(DestroyRef).onDestroy(() => { this.generation++; this.detailGeneration++; this.stopExport(); });
  }

  /**
   * Downloads the applied filters and sort as CSV in the current language. The file is saved only when the row count header
   * matches the file; 0 lines warn without a file. One export at a time; applying new filters or switching company cancels it.
   */
  exportCsv(): void {
    if (this.exportBlockedKey()) return;
    const epoch = ++this.exportEpoch;
    this.exporting.set(true); this.exportNotice.set(null); this.exportFailure.set(null);
    this.exportRequest = this.api.export({ ...this.applied, sort: this.sort }).subscribe({
      next: response => void checkCsvExport(response).then(outcome => {
        if (epoch !== this.exportEpoch) return;
        this.exporting.set(false);
        if (outcome.kind === 'invalid') { this.exportFailure.set({ key: 'pur.invoice.exportIncomplete', traceId: null, details: [] }); return; }
        if (outcome.kind === 'empty') { this.exportNotice.set({ type: 'warning', key: 'pur.invoice.exportEmpty', count: 0 }); return; }
        saveBlob(outcome.blob, PURCHASE_INVOICE_EXPORT_FILE);
        this.exportNotice.set({ type: 'success', key: 'pur.invoice.exported', count: outcome.count });
      }),
      error: failure => { if (epoch !== this.exportEpoch) return;
        this.exporting.set(false); this.exportFailure.set(purchaseInvoiceExportFailure(failure)); },
    });
  }
  private stopExport(): void { this.exportEpoch++; this.exportRequest?.unsubscribe(); this.exporting.set(false); }
  private clearExport(): void { this.stopExport(); this.exportNotice.set(null); this.exportFailure.set(null); }

  tagStatus(status: PurchaseInvoiceStatus): DocumentStatus { return TAG_STATUS[status] ?? 'Draft'; }
  statusLabel(status: string): string { return `pur.invoice.state.${status.toLowerCase()}`; }
  actionLabel(action: string): string { return action === 'Reject' ? 'pur.invoice.reject' : 'pur.invoice.accept'; }
  money(value: string): string { return formatDecimalText(value, 'en-US'); }
  /** A signed difference: positive values carry an explicit "+" so the direction is never ambiguous. */
  diff(value: string): string { return isZeroDecimalText(value) || value.startsWith('-') ? this.money(value) : '+' + this.money(value); }
  time(value: string | null): string { return displayTime(value); }
  kindLabel(kind: string): string { return `pur.invoice.kind.${kind}`; }
  /** Tax-rate differences are percentages; every other kind is an amount in the invoice currency. */
  amount(x: PurchaseInvoiceDifference, value: string): string { return this.money(value) + (x.kind === 'TAX_RATE' ? '%' : ''); }
  signed(x: PurchaseInvoiceDifference): string { return this.diff(x.difference) + (x.kind === 'TAX_RATE' ? '%' : ''); }
  isReasonIssue(lineNo: number): boolean { const issue = this.issues()[lineNo]; return issue === 'reason' || issue === 'reasonLength'; }
  canResolve(): boolean { return this.permissions.has('PUR.INVOICE.RESOLVE'); }
  decision(lineNo: number): PurchaseInvoiceDecisionInput { return this.decisions()[lineNo] ?? { action: '', reason: '' }; }
  setAction(lineNo: number, action: PurchaseInvoiceAction): void {
    this.decisions.update(all => ({ ...all, [lineNo]: { ...this.decision(lineNo), action } }));
  }
  setReason(lineNo: number, reason: string): void {
    this.decisions.update(all => ({ ...all, [lineNo]: { ...this.decision(lineNo), reason } }));
  }

  match(d: PurchaseInvoiceDetail): void {
    if (this.busy() || this.detail()?.id !== d.id || d.status !== 'Draft') return;
    this.dialogs.confirm({ titleKey: 'pur.invoice.confirmMatch', contentKey: 'pur.invoice.matchExplanation', okKey: 'pur.invoice.match' })
      .subscribe(confirmed => {
        if (!confirmed || this.busy() || this.detail()?.id !== d.id) return;
        this.act(d, this.api.match(d.id, d.version),
          status => status === 'Matched' ? 'pur.invoice.matchedMatched' : 'pur.invoice.matchedDisputed');
      });
  }
  resolve(d: PurchaseInvoiceDetail): void {
    if (this.busy() || this.detail()?.id !== d.id || d.status !== 'Disputed' || !this.canResolve()) return;
    this.attempted.set(true);
    const lines = this.disputed();
    if (!lines.length) return;
    if (Object.keys(this.issues()).length) {
      // Focus the first line that still needs input so keyboard and screen-reader users land on the missing choice or reason.
      const [lineNo, issue] = Object.entries(this.issues())[0];
      const target = issue === 'action' ? `pur-invoice-accept-${lineNo}` : `pur-invoice-reason-${lineNo}`;
      afterNextRender(() => this.host.nativeElement.querySelector<HTMLElement>(`[data-testid="${target}"]`)?.focus(),
        { injector: this.injector });
      return;
    }
    const body = lines.map(line => ({ lineNo: line.lineNo, action: this.decision(line.lineNo).action as PurchaseInvoiceAction,
      reason: this.decision(line.lineNo).reason.trim() }));
    const rejected = body.filter(x => x.action === 'Reject').length;
    this.dialogs.confirm({ titleKey: 'pur.invoice.confirmResolve',
      contentKey: rejected ? 'pur.invoice.resolveRejectExplanation' : 'pur.invoice.resolveAcceptExplanation',
      params: { accepted: body.length - rejected, rejected }, okKey: 'pur.invoice.resolve' })
      .subscribe(confirmed => {
        if (!confirmed || this.busy() || this.detail()?.id !== d.id) return;
        this.act(d, this.api.resolve(d.id, d.version, body),
          status => status === 'Matched' ? 'pur.invoice.resolvedMatched' : 'pur.invoice.resolvedDraft');
      });
  }

  reset(): void {
    this.keyword = ''; this.status = ''; this.from = ''; this.to = ''; this.supplierId = null; this.purchaseOrderId = null; this.search();
  }
  search(): void {
    const supplierId = this.supplierId ?? null; const purchaseOrderId = this.purchaseOrderId ?? null;
    const issue = purchaseInvoiceFilterIssue({ from: this.from, to: this.to, supplierId, purchaseOrderId });
    this.filterIssue.set(issue);
    if (issue) return;
    this.clearExport();
    this.applied ={ keyword: this.keyword.trim(), status: this.status, from: this.from, to: this.to, supplierId, purchaseOrderId };
    this.filtered.set(!!(this.applied.keyword || this.applied.status || this.applied.from || this.applied.to || supplierId || purchaseOrderId));
    this.page.set(1); this.load();
  }
  onQuery(query: NzTableQueryParams): void {
    const sorted = query.sort.find(x => x.value);
    const sort = sorted ? (sorted.value === 'descend' ? '-' : '') + sorted.key : '';
    if (query.pageIndex === this.page() && query.pageSize === this.pageSize() && sort === this.sort) return;
    if (sort !== this.sort) this.clearExport(); // a new order means a different file; paging does not change the export
    this.page.set(query.pageIndex); this.pageSize.set(query.pageSize); this.sort = sort; this.load();
  }
  load(): void {
    const current = ++this.generation; ++this.detailGeneration;
    this.detail.set(null); this.busy.set(false); this.clearActionError(); this.loading.set(true); this.listFailed.set(false); this.clearError();
    this.api.list({ page: this.page(), pageSize: this.pageSize(), ...this.applied, sort: this.sort }).subscribe({
      next: result => { if (current !== this.generation) return; this.rows.set(result.items); this.total.set(result.totalCount); this.loading.set(false); },
      error: e => { if (current !== this.generation) return; this.rows.set([]); this.total.set(0); this.listFailed.set(true); this.fail(e); this.loading.set(false); },
    });
  }
  view(row: PurchaseInvoiceSummary): void {
    this.clearError(); this.clearActionError(); this.detail.set(null); this.busy.set(false);
    this.read(row.id);
  }
  /** Reads the saved detail; a reply for an invoice no longer on screen is ignored. */
  private read(id: number): void {
    const current = ++this.detailGeneration;
    this.api.get(id).subscribe({
      next: d => { if (current === this.detailGeneration) this.show(d); },
      // 404: deleted or moved out of reach since the list loaded — say why, then show the current list.
      error: e => { if (current !== this.detailGeneration) return; this.load(); this.fail(e); },
    });
  }
  private show(d: PurchaseInvoiceDetail): void {
    const same = this.detail()?.id === d.id;
    this.detail.set(d);
    if (!same) { this.decisions.set({}); this.attempted.set(false); return; }
    const still = new Set(disputedInvoiceLines(d.lines).map(line => line.lineNo));
    this.decisions.update(all => Object.fromEntries(Object.entries(all).filter(([lineNo]) => still.has(Number(lineNo)))));
  }
  /**
   * Runs one MATCH / RESOLVE. Success: honest toast, row state/version updated, saved detail read again. Failure: the reason is
   * shown on the detail; 404 reloads the list; errors meaning the saved invoice changed re-read the detail; lock timeouts and
   * insufficient quantity change nothing, so the input stays as typed.
   */
  private act(d: PurchaseInvoiceDetail, request: Observable<PurchaseInvoiceActionResult>, successKey: (status: string) => string): void {
    const current = this.detailGeneration;
    const live = () => current === this.detailGeneration && this.detail()?.id === d.id;
    this.busy.set(true); this.clearActionError();
    request.subscribe({
      next: saved => {
        if (!live()) return;
        this.busy.set(false); this.attempted.set(false);
        this.rows.update(rows => rows.map(r => r.id === saved.id ? { ...r, status: saved.status, version: saved.version } : r));
        this.dialogs.success(successKey(saved.status), { number: d.supplierInvoiceNumber });
        this.read(d.id);
      },
      error: (e: unknown) => {
        if (!live()) return;
        this.busy.set(false);
        if (e instanceof HttpErrorResponse && e.status === 404) { this.load(); this.fail(e); return; }
        this.actionError.set(this.api.errorKey(e)); this.actionTraceId.set(apiErrorTraceId(e));
        const code = e instanceof HttpErrorResponse ? (e.error as { errorCode?: unknown } | null)?.errorCode : null;
        if ((PURCHASE_INVOICE_RELOAD_CODES as readonly unknown[]).includes(code)) this.read(d.id);
      },
    });
  }
  private clearActionError(): void { this.actionError.set(null); this.actionTraceId.set(null); }
  private clearError(): void { this.error.set(null); this.traceId.set(null); }
  private fail(error: unknown): void { this.error.set(this.api.errorKey(error)); this.traceId.set(apiErrorTraceId(error)); }
}
