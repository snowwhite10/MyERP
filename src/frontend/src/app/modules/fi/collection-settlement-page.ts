import { ChangeDetectionStrategy, Component, effect, inject, signal, untracked } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { TranslatePipe } from '@ngx-translate/core';
import { Observable } from 'rxjs';
import { NzAlertModule } from 'ng-zorro-antd/alert';
import { NzButtonModule } from 'ng-zorro-antd/button';
import { NzInputModule } from 'ng-zorro-antd/input';
import { NzTableModule, NzTableQueryParams } from 'ng-zorro-antd/table';
import { HasPermissionDirective } from '../../core/auth/has-permission.directive';
import { CompanyContextService } from '../../core/company/company-context.service';
import { apiErrorTraceId } from '../../core/http/api-error';
import { ApiErrorDisplay } from '../../shared/api-error-display';
import { displayTime } from '../../shared/date';
import { formatDecimalText } from '../../shared/decimal-text';
import { ErpDialogService } from '../../shared/dialogs/dialog.service';
import { DocumentStatus, StatusTag } from '../../shared/status-tag/status-tag';
import {
  COLLECTION_SETTLEMENT_STATUSES, CollectionSettlementDetail, CollectionSettlementService, CollectionSettlementStatus,
  CollectionSettlementSummary, SettlementLineError, settlementLineErrors, settlementLineReasonKey,
} from './collection-settlement.service';

type Notice = { type: 'success' | 'warning' | 'info'; key: string; params: Record<string, string> };

/** Status tag colour and label: Pending uses the shared "pending approval" tone. */
const TAG_STATUS: Record<CollectionSettlementStatus, DocumentStatus> = {
  Draft: 'Draft', Pending: 'Submitted', Rejected: 'Rejected', Approved: 'Approved', Posted: 'Posted',
};

/**
 * Collection settlements (interface 6.29, WU-FI-005c2b1, REQ_F4116.5.10): list with keyword / status / date filters,
 * paging and sorting, read-only detail with the allocation lines and the Draft / Rejected / Pending actions allowed by the API
 * (ADR-130). Only a posted settlement reduces receivables; a refused submit names every bad line and its reason (BR-FI-040).
 * Draft / Rejected rows link to the allocation form (interface 6.30, WU-FI-005c2b2), which returns here with what it saved.
 */
@Component({
  selector: 'erp-collection-settlement-page',
  imports: [FormsModule, RouterLink, TranslatePipe, NzAlertModule, NzButtonModule, NzInputModule, NzTableModule,
    HasPermissionDirective, ApiErrorDisplay, StatusTag],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <section class="settlements">
      <header><h1>{{ 'fi.collectionSettlement.title' | translate }}</h1>
        <a nz-button nzType="primary" routerLink="new" *erpHasPermission="'AR.SETTLEMENT.CREATE'" data-testid="fi-settle-new">{{ 'fi.collectionSettlement.new' | translate }}</a></header>
      <p class="hint">{{ 'fi.collectionSettlement.intro' | translate }}</p>
      <form class="filters" (ngSubmit)="search()" data-testid="fi-settle-filters">
        <input nz-input type="search" name="keyword" autocomplete="off" spellcheck="false" [(ngModel)]="keyword" maxlength="50" data-testid="fi-settle-keyword"
          [placeholder]="'fi.collectionSettlement.keywordHint' | translate" [attr.aria-label]="'fi.collectionSettlement.keyword' | translate" />
        <label>{{ 'fi.collectionSettlement.status' | translate }}
          <select name="status" [(ngModel)]="status" data-testid="fi-settle-status-filter">
            <option value="">{{ 'fi.collectionSettlement.allStatuses' | translate }}</option>
            @for (value of statuses; track value) { <option [value]="value">{{ statusLabel(value) | translate }}</option> }
          </select></label>
        <label>{{ 'fi.collectionSettlement.from' | translate }} <input type="date" name="from" [(ngModel)]="from" data-testid="fi-settle-from" /></label>
        <label>{{ 'fi.collectionSettlement.to' | translate }} <input type="date" name="to" [(ngModel)]="to" data-testid="fi-settle-to" /></label>
        <button nz-button nzType="primary" type="submit" data-testid="fi-settle-search">{{ 'fi.collectionSettlement.search' | translate }}</button>
        <button nz-button type="button" data-testid="fi-settle-reset" (click)="reset()">{{ 'fi.collectionSettlement.reset' | translate }}</button>
      </form>
      @if (rangeInvalid()) { <p class="field-error" role="alert" data-testid="fi-settle-range-error">{{ 'fi.collectionSettlement.rangeInvalid' | translate }}</p> }
      <div class="live" role="status" aria-live="polite">@if (notice(); as message) { <nz-alert [nzType]="message.type" nzShowIcon nzCloseable
        data-testid="fi-settle-notice" [attr.data-type]="message.type" [nzMessage]="message.key | translate: message.params" (nzOnClose)="notice.set(null)" /> }</div>
      @if (error()) {
        <erp-api-error-display data-testid="fi-settle-error" [messageKey]="error()!" [traceId]="traceId()" />
        @if (lineErrors().length) {
          <ul class="line-errors" role="alert" data-testid="fi-settle-line-errors" [attr.aria-label]="'fi.collectionSettlement.lineErrorsTitle' | translate">
            @for (e of lineErrors(); track $index) {
              <li [attr.data-testid]="'fi-settle-line-error-' + e.line">{{ 'fi.collectionSettlement.lineError' | translate: { line: e.line, receivable: e.receivableId } }}{{ lineReason(e.reason) | translate }}</li>
            }
          </ul>
        }
      }
      <nz-table #table [nzData]="rows()" [nzLoading]="loading()" [nzFrontPagination]="false" [nzTotal]="total()"
        [nzPageIndex]="page()" [nzPageSize]="pageSize()" [nzShowSizeChanger]="true" (nzQueryParams)="onQuery($event)"
        [nzNoResult]="(listFailed() ? 'fi.collectionSettlement.loadFailed' : filtered() ? 'fi.collectionSettlement.searchEmpty' : 'fi.collectionSettlement.empty') | translate"
        data-testid="fi-settle-table">
        <thead><tr>
          <th nzColumnKey="documentNo" [nzSortFn]="true">{{ 'fi.collectionSettlement.documentNo' | translate }}</th>
          <th nzColumnKey="businessDate" [nzSortFn]="true" nzSortOrder="descend">{{ 'fi.collectionSettlement.businessDate' | translate }}</th>
          <th>{{ 'fi.collectionSettlement.customer' | translate }}</th>
          <th>{{ 'fi.collectionSettlement.receipt' | translate }}</th>
          <th nzAlign="right">{{ 'fi.collectionSettlement.lineCount' | translate }}</th>
          <th nzColumnKey="totalAmount" [nzSortFn]="true" nzAlign="right">{{ 'fi.collectionSettlement.totalAmount' | translate }}</th>
          <th>{{ 'fi.collectionSettlement.status' | translate }}</th>
          <th>{{ 'fi.collectionSettlement.actions' | translate }}</th>
        </tr></thead>
        <tbody>@for (row of table.data; track row.id) {
          <tr [attr.data-testid]="'fi-settle-row-' + row.id">
            <td><button nz-button nzType="link" class="doc-link" type="button" [attr.data-testid]="'fi-settle-view-' + row.id" (click)="view(row)">{{ row.documentNo }}</button></td>
            <td class="date">{{ row.businessDate }}</td>
            <td>{{ partyLabel(row.customerCode, row.customerName, row.customerId) }}</td>
            <td class="doc" [attr.data-testid]="'fi-settle-receipt-' + row.id">{{ row.receiptDocumentNo ?? '#' + row.receiptId }}</td>
            <td class="count">{{ row.lineCount }}</td>
            <td class="num" [attr.data-testid]="'fi-settle-total-' + row.id"><span class="amt">{{ money(row.totalAmount) }}</span> {{ row.currency }}</td>
            <td><erp-status-tag [status]="tagStatus(row.status)" [labelKey]="statusLabel(row.status)" [testId]="'fi-settle-status-' + row.id" /></td>
            <td class="actions">
              @if (row.status === 'Draft' || row.status === 'Rejected') {
                <a nz-button nzType="link" *erpHasPermission="'AR.SETTLEMENT.EDIT'" [routerLink]="[row.id, 'edit']" [attr.data-testid]="'fi-settle-edit-' + row.id">{{ 'fi.collectionSettlement.edit' | translate }}</a>
              }
              @if (row.status === 'Draft') {
                <button nz-button nzType="link" type="button" *erpHasPermission="'AR.SETTLEMENT.SUBMIT'" [disabled]="busy()" [attr.data-testid]="'fi-settle-submit-' + row.id" (click)="submit(row)">{{ 'fi.collectionSettlement.submit' | translate }}</button>
              }
              @if (row.status === 'Pending') {
                <button nz-button nzType="link" type="button" *erpHasPermission="'AR.SETTLEMENT.SUBMIT'" [disabled]="busy()" [attr.data-testid]="'fi-settle-withdraw-' + row.id" (click)="withdraw(row)">{{ 'fi.collectionSettlement.withdraw' | translate }}</button>
              }
              @if (row.status === 'Draft' || row.status === 'Rejected') {
                <button nz-button nzType="link" nzDanger type="button" *erpHasPermission="'AR.SETTLEMENT.DELETE'" [disabled]="busy()" [attr.data-testid]="'fi-settle-delete-' + row.id" (click)="remove(row)">{{ 'fi.collectionSettlement.delete' | translate }}</button>
              }
            </td>
          </tr>
        }</tbody>
      </nz-table>
      @if (detail(); as d) {
        <section class="detail" data-testid="fi-settle-detail" [attr.aria-label]="d.documentNo">
          <header><h2>{{ d.documentNo }} <erp-status-tag [status]="tagStatus(d.status)" [labelKey]="statusLabel(d.status)" testId="fi-settle-detail-status" /></h2>
            <button nz-button type="button" data-testid="fi-settle-detail-close" (click)="detail.set(null)">{{ 'fi.collectionSettlement.close' | translate }}</button></header>
          <p class="state-hint" data-testid="fi-settle-state-hint">{{ stateHint(d.status) | translate }}</p>
          <dl>
            <dt>{{ 'fi.collectionSettlement.customer' | translate }}</dt><dd>{{ partyLabel(d.customerCode, d.customerName, d.customerId) }}</dd>
            <dt>{{ 'fi.collectionSettlement.businessDate' | translate }}</dt><dd>{{ d.businessDate }}</dd>
            <dt>{{ 'fi.collectionSettlement.receipt' | translate }}</dt><dd class="ref">{{ d.receiptDocumentNo ?? '#' + d.receiptId }}</dd>
            <dt>{{ 'fi.collectionSettlement.receiptUnapplied' | translate }}</dt><dd data-testid="fi-settle-detail-unapplied">{{ d.receiptUnappliedAmount !== null ? money(d.receiptUnappliedAmount) + ' ' + d.currency : ('fi.collectionSettlement.receiptNotPosted' | translate) }}</dd>
            <dt>{{ 'fi.collectionSettlement.totalAmount' | translate }}</dt><dd data-testid="fi-settle-detail-total">{{ money(d.totalAmount) }} {{ d.currency }}</dd>
            <dt>{{ 'fi.collectionSettlement.remark' | translate }}</dt><dd class="ref">{{ d.remark || '—' }}</dd>
            <dt>{{ 'fi.collectionSettlement.lastChanged' | translate }}</dt><dd>{{ time(d.updatedAt ?? d.createdAt) }}</dd>
          </dl>
          <div class="lines-wrap">
            <table class="lines" data-testid="fi-settle-detail-lines">
              <caption>{{ 'fi.collectionSettlement.linesCaption' | translate }}</caption>
              <thead><tr>
                <th scope="col">{{ 'fi.collectionSettlement.lineNo' | translate }}</th>
                <th scope="col">{{ 'fi.collectionSettlement.receivable' | translate }}</th>
                <th scope="col" class="num">{{ 'fi.collectionSettlement.lineAmount' | translate }}</th>
                <th scope="col" class="num">{{ 'fi.collectionSettlement.openNow' | translate }}</th>
              </tr></thead>
              <tbody>@for (line of d.lines; track line.lineNo) {
                <tr [attr.data-testid]="'fi-settle-line-' + line.lineNo">
                  <td class="count">{{ line.lineNo }}</td>
                  <td>#{{ line.receivableId }}</td>
                  <td class="num" [attr.data-testid]="'fi-settle-line-amount-' + line.lineNo">{{ money(line.amount) }}</td>
                  <td class="num" [attr.data-testid]="'fi-settle-line-open-' + line.lineNo">{{ money(line.receivableOpenAmount) }}</td>
                </tr>
              }</tbody>
            </table>
          </div>
          <p class="muted">{{ 'fi.collectionSettlement.openNowHint' | translate }}</p>
        </section>
      }
    </section>`,
  styles: `:host{display:block;min-width:0}.live{display:contents}.settlements{display:grid;gap:16px;color:var(--erp-color-text);min-width:0}header{display:flex;gap:8px;align-items:center;justify-content:space-between;flex-wrap:wrap}h1,h2{margin:0}h1{font-size:20px}h2{font-size:16px;display:flex;gap:8px;align-items:center}.hint,.muted,.state-hint{margin:0;color:var(--erp-color-text-secondary)}.muted{font-size:12px}.filters{display:flex;gap:8px;align-items:center;flex-wrap:wrap}.filters input[type=search]{max-width:260px}.filters label{display:flex;gap:4px;align-items:center}.filters select,.filters input[type=date]{background-color:var(--erp-color-bg-container);color:var(--erp-color-text);border:1px solid var(--erp-color-border);border-radius:4px}.field-error{margin:0;color:var(--erp-color-error-text)}.line-errors{margin:0;padding-left:20px;color:var(--erp-color-error-text)}.num{text-align:right;font-variant-numeric:tabular-nums;overflow-wrap:anywhere;min-width:96px}.count{text-align:right;font-variant-numeric:tabular-nums}.ref{overflow-wrap:anywhere}.date,.doc,.amt{white-space:nowrap}.actions{min-width:160px}.actions button,.actions a{padding:0 4px}.doc-link{padding:0}.detail{display:grid;gap:12px;padding:16px;border:1px solid var(--erp-color-border);border-radius:8px;background:var(--erp-color-bg-container);min-width:0}.detail dl{display:grid;grid-template-columns:minmax(120px,180px) 1fr;gap:8px;margin:0}.detail dt{font-weight:600}.detail dd{margin:0;min-width:0}.lines-wrap{overflow-x:auto;min-width:0}.lines{width:100%;border-collapse:collapse}.lines caption{caption-side:top;text-align:left;font-weight:600;padding-bottom:4px;color:var(--erp-color-text)}.lines th,.lines td{padding:6px 8px;border-bottom:1px solid var(--erp-color-border)}.lines th{font-weight:600;text-align:left}.lines th.num{text-align:right}button:focus-visible,input:focus-visible,select:focus-visible{outline:2px solid var(--erp-color-primary);outline-offset:2px}@media (max-width:600px){.detail dl{grid-template-columns:1fr}}`,
})
export class CollectionSettlementPage {
  private readonly api = inject(CollectionSettlementService);
  private readonly company = inject(CompanyContextService);
  private readonly dialogs = inject(ErpDialogService);
  readonly statuses = COLLECTION_SETTLEMENT_STATUSES;
  readonly rows = signal<CollectionSettlementSummary[]>([]);
  readonly detail = signal<CollectionSettlementDetail | null>(null);
  readonly total = signal(0); readonly page = signal(1); readonly pageSize = signal(20);
  readonly loading = signal(false); readonly busy = signal(false);
  readonly error = signal<string | null>(null); readonly traceId = signal<string | null>(null);
  readonly lineErrors = signal<SettlementLineError[]>([]);
  readonly notice = signal<Notice | null>(null);
  readonly filtered = signal(false); readonly rangeInvalid = signal(false);
  /** The last list read failed: the table says so instead of claiming there are no settlements. */
  readonly listFailed = signal(false);
  keyword = ''; status = ''; from = ''; to = '';
  private sort = '';
  private generation = 0;

  constructor() {
    // The form returns here with what it saved (a Draft: nothing applied yet); shown once, cleared by a company switch.
    const saved = inject(Router).currentNavigation()?.extras.state?.['collectionSettlementSaved'] as
      { no: string; created: boolean; total: string; currency: string; count: number } | undefined;
    let arrival: Notice | null = saved?.no ? { type: 'success', key: saved.created ? 'fi.collectionSettlement.notice.created'
      : 'fi.collectionSettlement.notice.updated', params: { no: saved.no, amount: this.money(String(saved.total)),
        currency: saved.currency, count: String(saved.count) } } : null;
    effect(() => {
      this.company.current();
      untracked(() => { this.detail.set(null); this.notice.set(arrival); arrival = null; this.reset(); });
    });
  }

  tagStatus(status: CollectionSettlementStatus): DocumentStatus { return TAG_STATUS[status] ?? 'Draft'; }
  statusLabel(status: CollectionSettlementStatus | string): string { return `fi.collectionSettlement.state.${status.toLowerCase()}`; }
  stateHint(status: CollectionSettlementStatus): string { return `fi.collectionSettlement.stateHint.${status.toLowerCase()}`; }
  lineReason(reason: string): string { return settlementLineReasonKey(reason); }
  partyLabel(code: string | null, name: string | null, id: number): string { return code ? `${code} · ${name ?? ''}` : `#${id}`; }
  money(value: string): string { return formatDecimalText(value, 'en-US'); }
  time(value: string | null): string { return displayTime(value); }

  reset(): void { this.keyword = ''; this.status = ''; this.from = ''; this.to = ''; this.search(); }
  search(): void {
    this.rangeInvalid.set(!!this.from && !!this.to && this.from > this.to);
    if (this.rangeInvalid()) return;
    this.filtered.set(!!(this.keyword.trim() || this.status || this.from || this.to));
    this.page.set(1); this.load();
  }
  onQuery(query: NzTableQueryParams): void {
    const sorted = query.sort.find(x => x.value);
    const sort = sorted ? (sorted.value === 'descend' ? '-' : '') + sorted.key : '';
    if (query.pageIndex === this.page() && query.pageSize === this.pageSize() && sort === this.sort) return;
    this.page.set(query.pageIndex); this.pageSize.set(query.pageSize); this.sort = sort; this.load();
  }
  load(): void {
    const current = ++this.generation; this.loading.set(true); this.clearError(); this.listFailed.set(false);
    this.api.list({ page: this.page(), pageSize: this.pageSize(), keyword: this.keyword, status: this.status, from: this.from,
      to: this.to, sort: this.sort }).subscribe({
      next: result => { if (current !== this.generation) return; this.rows.set(result.items); this.total.set(result.totalCount); this.loading.set(false); },
      error: e => { if (current !== this.generation) return; this.fail(e); this.listFailed.set(true); this.rows.set([]); this.total.set(0); this.loading.set(false); },
    });
  }
  view(row: CollectionSettlementSummary): void {
    const current = this.generation;
    this.api.get(row.id).subscribe({ next: d => { if (current === this.generation) this.detail.set(d); },
      error: e => { this.load(); this.fail(e); } });
  }
  submit(row: CollectionSettlementSummary): void {
    this.act(row, this.api.submit(row.id, row.version), (result): Notice => result.status === 'Posted' && result.posting
      ? { type: 'success', key: 'fi.collectionSettlement.notice.posted', params: { no: result.documentNo,
        amount: this.money(result.posting.totalAmount), currency: result.posting.baseCurrency || row.currency,
        count: String(result.posting.allocations.length), receipt: row.receiptDocumentNo ?? `#${row.receiptId}`,
        unapplied: this.money(result.posting.receiptUnappliedAmount) } }
      : { type: 'success', key: 'fi.collectionSettlement.notice.submitted', params: { no: result.documentNo } });
  }
  withdraw(row: CollectionSettlementSummary): void {
    this.act(row, this.api.withdraw(row.id, row.version),
      (result): Notice => ({ type: 'success', key: 'fi.collectionSettlement.notice.withdrawn', params: { no: result.documentNo } }));
  }
  remove(row: CollectionSettlementSummary): void {
    if (this.busy()) return;
    this.dialogs.confirm({ titleKey: 'fi.collectionSettlement.confirmDelete', params: { no: row.documentNo }, danger: true,
      okKey: 'fi.collectionSettlement.delete' }).subscribe(confirmed => {
      if (!confirmed) return;
      this.act(row, this.api.remove(row.id, row.version),
        (): Notice => ({ type: 'success', key: 'fi.collectionSettlement.notice.deleted', params: { no: row.documentNo } }));
    });
  }

  private act<T>(row: CollectionSettlementSummary, request: Observable<T>, notice: (result: T) => Notice): void {
    if (this.busy()) return;
    this.busy.set(true); this.notice.set(null); this.clearError();
    const current = this.generation;
    request.subscribe({
      next: result => {
        this.busy.set(false);
        if (current !== this.generation) return;
        this.notice.set(notice(result));
        if (this.detail()?.id === row.id) this.detail.set(null);
        this.load();
      },
      error: e => {
        this.busy.set(false);
        if (current !== this.generation) return;
        // The row may be stale (409 / 422 / 404): show the reason (and every refused line), then the current saved state.
        this.load(); this.fail(e);
        if (this.detail()?.id === row.id) this.detail.set(null);
      },
    });
  }
  private clearError(): void { this.error.set(null); this.traceId.set(null); this.lineErrors.set([]); }
  private fail(error: unknown): void {
    this.error.set(this.api.errorKey(error)); this.traceId.set(apiErrorTraceId(error)); this.lineErrors.set(settlementLineErrors(error));
  }
}
