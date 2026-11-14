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
import { PermissionService } from '../../core/auth/permission.service';
import { CompanyContextService } from '../../core/company/company-context.service';
import { apiErrorTraceId } from '../../core/http/api-error';
import { ApiErrorDisplay } from '../../shared/api-error-display';
import { displayTime } from '../../shared/date';
import { formatDecimalText } from '../../shared/decimal-text';
import { ErpDialogService } from '../../shared/dialogs/dialog.service';
import { DocumentStatus, StatusTag } from '../../shared/status-tag/status-tag';
import {
  COLLECTION_RECEIPT_STATUSES, CollectionReceiptDetail, CollectionReceiptService, CollectionReceiptStatus, CollectionReceiptSummary,
} from './collection-receipt.service';

type Notice = { type: 'success' | 'warning' | 'info'; key: string; params: Record<string, string> };

/** Status tag colour and label: Pending uses the shared "pending approval" tone. */
const TAG_STATUS: Record<CollectionReceiptStatus, DocumentStatus> = {
  Draft: 'Draft', Pending: 'Submitted', Rejected: 'Rejected', Approved: 'Approved', Posted: 'Posted',
};

/**
 * Sales collection receipts (BlueWhale 4.4.1.6.1 view, WU-FI-005c1, REQ_F4116.1.13): list with keyword / status / date filters,
 * paging and sorting, read-only detail and the Draft / Rejected / Pending actions allowed by the API (ADR-129). A posted receipt
 * only records unapplied cash; the screen never says an invoice was paid — applying cash to invoices is the settlement screen.
 */
@Component({
  selector: 'erp-collection-receipt-page',
  imports: [FormsModule, RouterLink, TranslatePipe, NzAlertModule, NzButtonModule, NzInputModule, NzTableModule,
    HasPermissionDirective, ApiErrorDisplay, StatusTag],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <section class="receipts">
      <header><h1>{{ 'fi.collectionReceipt.title' | translate }}</h1>
        <a nz-button nzType="primary" routerLink="new" *erpHasPermission="'AR.COLLECTION.CREATE'" data-testid="fi-receipt-new">{{ 'fi.collectionReceipt.new' | translate }}</a></header>
      <p class="hint">{{ 'fi.collectionReceipt.intro' | translate }}</p>
      <form class="filters" (ngSubmit)="search()" data-testid="fi-receipt-filters">
        <input nz-input type="search" name="keyword" autocomplete="off" spellcheck="false" [(ngModel)]="keyword" maxlength="50" data-testid="fi-receipt-keyword"
          [placeholder]="'fi.collectionReceipt.keywordHint' | translate" [attr.aria-label]="'fi.collectionReceipt.keyword' | translate" />
        <label>{{ 'fi.collectionReceipt.status' | translate }}
          <select name="status" [(ngModel)]="status" data-testid="fi-receipt-status-filter">
            <option value="">{{ 'fi.collectionReceipt.allStatuses' | translate }}</option>
            @for (value of statuses; track value) { <option [value]="value">{{ statusLabel(value) | translate }}</option> }
          </select></label>
        <label>{{ 'fi.collectionReceipt.from' | translate }} <input type="date" name="from" [(ngModel)]="from" data-testid="fi-receipt-from" /></label>
        <label>{{ 'fi.collectionReceipt.to' | translate }} <input type="date" name="to" [(ngModel)]="to" data-testid="fi-receipt-to" /></label>
        <button nz-button nzType="primary" type="submit" data-testid="fi-receipt-search">{{ 'fi.collectionReceipt.search' | translate }}</button>
        <button nz-button type="button" data-testid="fi-receipt-reset" (click)="reset()">{{ 'fi.collectionReceipt.reset' | translate }}</button>
      </form>
      @if (rangeInvalid()) { <p class="field-error" role="alert" data-testid="fi-receipt-range-error">{{ 'fi.collectionReceipt.rangeInvalid' | translate }}</p> }
      <div class="live" role="status" aria-live="polite">@if (notice(); as message) { <nz-alert [nzType]="message.type" nzShowIcon nzCloseable
        data-testid="fi-receipt-notice" [attr.data-type]="message.type" [nzMessage]="message.key | translate: message.params" (nzOnClose)="notice.set(null)" /> }</div>
      @if (error()) { <erp-api-error-display data-testid="fi-receipt-error" [messageKey]="error()!" [traceId]="traceId()" /> }
      <nz-table #table [nzData]="rows()" [nzLoading]="loading()" [nzFrontPagination]="false" [nzTotal]="total()"
        [nzPageIndex]="page()" [nzPageSize]="pageSize()" [nzShowSizeChanger]="true" (nzQueryParams)="onQuery($event)"
        [nzNoResult]="(filtered() ? 'fi.collectionReceipt.searchEmpty' : 'fi.collectionReceipt.empty') | translate"
        data-testid="fi-receipt-table">
        <thead><tr>
          <th nzColumnKey="documentNo" [nzSortFn]="true">{{ 'fi.collectionReceipt.documentNo' | translate }}</th>
          <th nzColumnKey="businessDate" [nzSortFn]="true" nzSortOrder="descend">{{ 'fi.collectionReceipt.businessDate' | translate }}</th>
          <th>{{ 'fi.collectionReceipt.customer' | translate }}</th>
          <th>{{ 'fi.collectionReceipt.fundAccount' | translate }}</th>
          <th>{{ 'fi.collectionReceipt.bankReference' | translate }}</th>
          <th nzColumnKey="amount" [nzSortFn]="true" nzAlign="right">{{ 'fi.collectionReceipt.amount' | translate }}</th>
          <th nzAlign="right">{{ 'fi.collectionReceipt.unapplied' | translate }}</th>
          <th>{{ 'fi.collectionReceipt.status' | translate }}</th>
          <th>{{ 'fi.collectionReceipt.actions' | translate }}</th>
        </tr></thead>
        <tbody>@for (row of table.data; track row.id) {
          <tr [attr.data-testid]="'fi-receipt-row-' + row.id">
            <td><button nz-button nzType="link" class="doc-link" type="button" [attr.data-testid]="'fi-receipt-view-' + row.id" (click)="view(row)">{{ row.documentNo }}</button></td>
            <td class="date">{{ row.businessDate }}</td>
            <td>{{ partyLabel(row.customerCode, row.customerName, row.customerId) }}</td>
            <td>{{ partyLabel(row.bankAccountCode, row.bankAccountName, row.bankAccountId) }}</td>
            <td class="ref">{{ row.bankReference }}</td>
            <td class="num" [attr.data-testid]="'fi-receipt-amount-' + row.id">{{ money(row.amount) }} {{ row.currency }}</td>
            <td class="num" [attr.data-testid]="'fi-receipt-unapplied-' + row.id">@if (row.unappliedAmount !== null) { {{ money(row.unappliedAmount) }} } @else { <span class="muted" [attr.title]="'fi.collectionReceipt.unappliedNotPosted' | translate">—</span> }</td>
            <td><erp-status-tag [status]="tagStatus(row.status)" [labelKey]="statusLabel(row.status)" [testId]="'fi-receipt-status-' + row.id" /></td>
            <td class="actions">
              @if (row.status === 'Draft' || row.status === 'Rejected') {
                <a nz-button nzType="link" *erpHasPermission="'AR.COLLECTION.EDIT'" [routerLink]="[row.id, 'edit']" [attr.data-testid]="'fi-receipt-edit-' + row.id">{{ 'fi.collectionReceipt.edit' | translate }}</a>
              }
              @if (row.status === 'Draft') {
                <button nz-button nzType="link" type="button" *erpHasPermission="'AR.COLLECTION.SUBMIT'" [disabled]="busy()" [attr.data-testid]="'fi-receipt-submit-' + row.id" (click)="submit(row)">{{ 'fi.collectionReceipt.submit' | translate }}</button>
              }
              @if (row.status === 'Pending') {
                <button nz-button nzType="link" type="button" *erpHasPermission="'AR.COLLECTION.SUBMIT'" [disabled]="busy()" [attr.data-testid]="'fi-receipt-withdraw-' + row.id" (click)="withdraw(row)">{{ 'fi.collectionReceipt.withdraw' | translate }}</button>
              }
              @if (row.status === 'Draft' || row.status === 'Rejected') {
                <button nz-button nzType="link" nzDanger type="button" *erpHasPermission="'AR.COLLECTION.DELETE'" [disabled]="busy()" [attr.data-testid]="'fi-receipt-delete-' + row.id" (click)="remove(row)">{{ 'fi.collectionReceipt.delete' | translate }}</button>
              }
            </td>
          </tr>
        }</tbody>
      </nz-table>
      @if (detail(); as d) {
        <section class="detail" data-testid="fi-receipt-detail" [attr.aria-label]="d.documentNo">
          <header><h2>{{ d.documentNo }} <erp-status-tag [status]="tagStatus(d.status)" [labelKey]="statusLabel(d.status)" testId="fi-receipt-detail-status" /></h2>
            <button nz-button type="button" data-testid="fi-receipt-detail-close" (click)="detail.set(null)">{{ 'fi.collectionReceipt.close' | translate }}</button></header>
          <p class="state-hint" data-testid="fi-receipt-state-hint">{{ stateHint(d.status) | translate }}</p>
          <dl>
            <dt>{{ 'fi.collectionReceipt.customer' | translate }}</dt><dd>{{ partyLabel(d.customerCode, d.customerName, d.customerId) }}</dd>
            <dt>{{ 'fi.collectionReceipt.businessDate' | translate }}</dt><dd>{{ d.businessDate }}</dd>
            <dt>{{ 'fi.collectionReceipt.fundAccount' | translate }}</dt><dd>{{ partyLabel(d.bankAccountCode, d.bankAccountName, d.bankAccountId) }}</dd>
            <dt>{{ 'fi.collectionReceipt.bankReference' | translate }}</dt><dd class="ref">{{ d.bankReference }}</dd>
            <dt>{{ 'fi.collectionReceipt.amount' | translate }}</dt><dd data-testid="fi-receipt-detail-amount">{{ money(d.amount) }} {{ d.currency }}</dd>
            <dt>{{ 'fi.collectionReceipt.unapplied' | translate }}</dt><dd data-testid="fi-receipt-detail-unapplied">{{ d.unappliedAmount !== null ? money(d.unappliedAmount) + ' ' + d.currency : ('fi.collectionReceipt.unappliedNotPosted' | translate) }}</dd>
            @if (d.journalId) { <dt>{{ 'fi.collectionReceipt.journal' | translate }}</dt><dd data-testid="fi-receipt-detail-journal">@if (canSeeJournals) { <a [routerLink]="['/gl/journals']" [queryParams]="{ journal: d.journalId }">#{{ d.journalId }}</a> } @else { #{{ d.journalId }} }</dd> }
            <dt>{{ 'fi.collectionReceipt.remark' | translate }}</dt><dd class="ref">{{ d.remark || '—' }}</dd>
            <dt>{{ 'fi.collectionReceipt.lastChanged' | translate }}</dt><dd>{{ time(d.updatedAt ?? d.createdAt) }}</dd>
          </dl>
        </section>
      }
    </section>`,
  styles: `:host{display:block;min-width:0}.live{display:contents}.receipts{display:grid;gap:16px;color:var(--erp-color-text);min-width:0}header{display:flex;gap:8px;align-items:center;justify-content:space-between;flex-wrap:wrap}h1,h2{margin:0}h1{font-size:20px}h2{font-size:16px;display:flex;gap:8px;align-items:center}.hint,.muted,.state-hint{margin:0;color:var(--erp-color-text-secondary)}.filters{display:flex;gap:8px;align-items:center;flex-wrap:wrap}.filters input[type=search]{max-width:260px}.filters label{display:flex;gap:4px;align-items:center}.filters select,.filters input[type=date]{background-color:var(--erp-color-bg-container);color:var(--erp-color-text);border:1px solid var(--erp-color-border);border-radius:4px}.field-error{margin:0;color:var(--erp-color-error-text)}.num{text-align:right;font-variant-numeric:tabular-nums;overflow-wrap:anywhere;min-width:96px}.ref{overflow-wrap:anywhere}.date{white-space:nowrap}.actions{min-width:120px}.actions a,.actions button{padding:0 4px}.doc-link{padding:0}.detail{display:grid;gap:12px;padding:16px;border:1px solid var(--erp-color-border);border-radius:8px;background:var(--erp-color-bg-container)}.detail dl{display:grid;grid-template-columns:minmax(120px,180px) 1fr;gap:8px;margin:0}.detail dt{font-weight:600}.detail dd{margin:0;min-width:0}button:focus-visible,a:focus-visible,input:focus-visible,select:focus-visible{outline:2px solid var(--erp-color-primary);outline-offset:2px}@media (max-width:600px){.detail dl{grid-template-columns:1fr}}`,
})
export class CollectionReceiptPage {
  private readonly api = inject(CollectionReceiptService);
  private readonly company = inject(CompanyContextService);
  private readonly dialogs = inject(ErpDialogService);
  readonly canSeeJournals = inject(PermissionService).has('GL.JOURNAL.VIEW');
  readonly statuses = COLLECTION_RECEIPT_STATUSES;
  readonly rows = signal<CollectionReceiptSummary[]>([]);
  readonly detail = signal<CollectionReceiptDetail | null>(null);
  readonly total = signal(0); readonly page = signal(1); readonly pageSize = signal(20);
  readonly loading = signal(false); readonly busy = signal(false);
  readonly error = signal<string | null>(null); readonly traceId = signal<string | null>(null);
  readonly notice = signal<Notice | null>(null);
  readonly filtered = signal(false); readonly rangeInvalid = signal(false);
  keyword = ''; status = ''; from = ''; to = '';
  private sort = '';
  private generation = 0;

  constructor() {
    // The form returns here with what it saved; shown once, cleared by a company switch.
    const saved = inject(Router).currentNavigation()?.extras.state?.['collectionReceiptSaved'] as { no: string; created: boolean } | undefined;
    let arrival: Notice | null = saved?.no ? { type: 'success', key: saved.created ? 'fi.collectionReceipt.notice.created'
      : 'fi.collectionReceipt.notice.updated', params: { no: saved.no } } : null;
    effect(() => {
      this.company.current();
      untracked(() => { this.detail.set(null); this.notice.set(arrival); arrival = null; this.reset(); });
    });
  }

  tagStatus(status: CollectionReceiptStatus): DocumentStatus { return TAG_STATUS[status] ?? 'Draft'; }
  statusLabel(status: CollectionReceiptStatus | string): string { return `fi.collectionReceipt.state.${status.toLowerCase()}`; }
  stateHint(status: CollectionReceiptStatus): string { return `fi.collectionReceipt.stateHint.${status.toLowerCase()}`; }
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
    const current = ++this.generation; this.loading.set(true); this.error.set(null); this.traceId.set(null);
    this.api.list({ page: this.page(), pageSize: this.pageSize(), keyword: this.keyword, status: this.status, from: this.from,
      to: this.to, sort: this.sort }).subscribe({
      next: result => { if (current !== this.generation) return; this.rows.set(result.items); this.total.set(result.totalCount); this.loading.set(false); },
      error: e => { if (current !== this.generation) return; this.fail(e); this.rows.set([]); this.total.set(0); this.loading.set(false); },
    });
  }
  view(row: CollectionReceiptSummary): void {
    const current = this.generation;
    this.api.get(row.id).subscribe({ next: d => { if (current === this.generation) this.detail.set(d); },
      error: e => { this.load(); this.fail(e); } });
  }
  submit(row: CollectionReceiptSummary): void {
    this.act(row, this.api.submit(row.id, row.version), (result): Notice => result.status === 'Posted' && result.posting
      ? { type: 'success', key: 'fi.collectionReceipt.notice.posted',
        params: { no: result.documentNo, amount: this.money(result.posting.unappliedAmount), currency: result.posting.baseCurrency || row.currency } }
      : { type: 'success', key: 'fi.collectionReceipt.notice.submitted', params: { no: result.documentNo } });
  }
  withdraw(row: CollectionReceiptSummary): void {
    this.act(row, this.api.withdraw(row.id, row.version),
      (result): Notice => ({ type: 'success', key: 'fi.collectionReceipt.notice.withdrawn', params: { no: result.documentNo } }));
  }
  remove(row: CollectionReceiptSummary): void {
    if (this.busy()) return;
    this.dialogs.confirm({ titleKey: 'fi.collectionReceipt.confirmDelete', params: { no: row.documentNo }, danger: true,
      okKey: 'fi.collectionReceipt.delete' }).subscribe(confirmed => {
      if (!confirmed) return;
      this.act(row, this.api.remove(row.id, row.version),
        (): Notice => ({ type: 'success', key: 'fi.collectionReceipt.notice.deleted', params: { no: row.documentNo } }));
    });
  }

  private act<T>(row: CollectionReceiptSummary, request: Observable<T>, notice: (result: T) => Notice): void {
    if (this.busy()) return;
    this.busy.set(true); this.notice.set(null); this.error.set(null); this.traceId.set(null);
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
        // The row may be stale (409 / 422 / 404): show the reason, then the current saved state.
        this.load(); this.fail(e);
        if (this.detail()?.id === row.id) this.detail.set(null);
      },
    });
  }
  private fail(error: unknown): void { this.error.set(this.api.errorKey(error)); this.traceId.set(apiErrorTraceId(error)); }
}
