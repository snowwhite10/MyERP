import { ChangeDetectionStrategy, Component, effect, inject, signal, untracked } from '@angular/core';
import { DatePipe } from '@angular/common';
import { HttpErrorResponse } from '@angular/common/http';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router } from '@angular/router';
import { TranslatePipe } from '@ngx-translate/core';
import { NzButtonModule } from 'ng-zorro-antd/button';
import { NzTableModule, NzTableQueryParams } from 'ng-zorro-antd/table';
import { NzTagModule } from 'ng-zorro-antd/tag';
import { CompanyContextService } from '../../core/company/company-context.service';
import { apiErrorTraceId, apiValidationDetails } from '../../core/http/api-error';
import { ApiErrorDisplay } from '../../shared/api-error-display';
import { RfqDetail, RfqService, RfqStatus, RfqSummary } from './rfq.service';

/** 介面 4.11 的唯讀列表與保存詳情。寫入動作由後續切片交付。 */
@Component({
  selector: 'erp-rfq-page',
  imports: [DatePipe, FormsModule, TranslatePipe, ApiErrorDisplay, NzButtonModule, NzTableModule, NzTagModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <section class="rfqs"><h1>{{ 'pur.rfq.title' | translate }}</h1>
      <div class="filters">
        <label>{{ 'pur.rfq.keyword' | translate }} <input type="search" name="rfqKeyword" autocomplete="off" [(ngModel)]="keyword" (keyup.enter)="search()" data-testid="pur-rfq-search" /></label>
        <label>{{ 'pur.rfq.from' | translate }} <input type="date" name="rfqFrom" autocomplete="off" [(ngModel)]="from" /></label>
        <div class="filter-field"><label>{{ 'pur.rfq.to' | translate }} <input type="date" name="rfqTo" autocomplete="off" [(ngModel)]="to" /></label>
          @if (error() === 'pur.rfq.invalidDates') { <small role="alert">{{ 'pur.rfq.invalidDates' | translate }}</small> }</div>
        <label>{{ 'pur.rfq.status' | translate }} <select name="rfqStatus" autocomplete="off" [(ngModel)]="status"><option value="">{{ 'pur.rfq.all' | translate }}</option>
          <option value="Draft">{{ 'pur.rfq.draft' | translate }}</option><option value="Sent">{{ 'pur.rfq.sent' | translate }}</option>
          <option value="Closed">{{ 'pur.rfq.closed' | translate }}</option></select></label>
        <div class="filter-field"><label>{{ 'pur.rfq.source' | translate }} <input type="number" name="rfqSource" autocomplete="off" min="1" step="1" [(ngModel)]="requisitionLineId" /></label>
          @if (error() === 'pur.rfq.invalidSource') { <small role="alert">{{ 'pur.rfq.invalidSource' | translate }}</small> }</div>
        <button nz-button (click)="search()" data-testid="pur-rfq-filter">{{ 'pur.rfq.search' | translate }}</button>
        <button nz-button (click)="reset()" data-testid="pur-rfq-reset">{{ 'pur.rfq.reset' | translate }}</button>
      </div>
      @if (error()) { <erp-api-error-display [messageKey]="error()!" [traceId]="errorTrace()"
        [details]="errorDetails()" data-testid="pur-rfq-error" /> }
      <nz-table #table [nzData]="rows()" [nzLoading]="loading()" [nzFrontPagination]="false"
        [nzTotal]="total()" [nzPageIndex]="page()" [nzPageSize]="pageSize()" (nzQueryParams)="onQuery($event)"
        [nzNoResult]="(loadFailed() ? 'pur.rfq.loadFailed' : filtered() ? 'pur.rfq.noResults' : 'pur.rfq.empty') | translate"
        data-testid="pur-rfq-table">
        <thead><tr><th>{{ 'pur.rfq.number' | translate }}</th><th>{{ 'pur.rfq.date' | translate }}</th>
          <th>{{ 'pur.rfq.deadline' | translate }}</th><th>{{ 'pur.rfq.status' | translate }}</th>
          <th>{{ 'pur.rfq.lines' | translate }}</th><th>{{ 'pur.rfq.suppliers' | translate }}</th></tr></thead>
        <tbody>@for (row of table.data; track row.id) { <tr [attr.data-testid]="'pur-rfq-row-' + row.id">
          <td><button nz-button nzType="link" (click)="view(row)">{{ row.documentNo }}</button></td>
          <td>{{ row.businessDate }}</td><td>{{ row.replyDeadline }}</td>
          <td><nz-tag [nzColor]="statusColor(row.status)">{{ statusKey(row.status) | translate }}</nz-tag></td>
          <td>{{ row.lineCount }}</td><td>{{ row.invitationCount }}</td></tr> }</tbody>
      </nz-table>
      @if (detail(); as d) { <section class="detail" data-testid="pur-rfq-detail">
        <button nz-button (click)="detail.set(null)" [attr.aria-label]="'pur.rfq.closeDetail' | translate">×</button>
        <h2>{{ d.documentNo }} · {{ statusKey(d.status) | translate }}</h2>
        <p>{{ 'pur.rfq.date' | translate }}: {{ d.businessDate }} · {{ 'pur.rfq.deadline' | translate }}: {{ d.replyDeadline }}</p>
        @if (d.remark) { <p>{{ 'pur.rfq.remark' | translate }}: {{ d.remark }}</p> }
        <h3>{{ 'pur.rfq.lines' | translate }}</h3>
        <table><thead><tr><th>#</th><th>{{ 'pur.rfq.item' | translate }}</th><th>{{ 'pur.rfq.source' | translate }}</th>
          <th>{{ 'pur.rfq.requiredDate' | translate }}</th><th>{{ 'pur.rfq.quantity' | translate }}</th></tr></thead>
          <tbody>@for (line of d.lines; track line.id) { <tr><td>{{ line.lineNo }}</td><td>{{ line.itemId }}</td>
            <td>{{ line.requisitionLineId ?? '—' }}</td><td>{{ line.requiredDate }}</td><td>{{ line.quantity }}</td></tr> }</tbody></table>
        <h3>{{ 'pur.rfq.suppliers' | translate }}</h3>
        <table><thead><tr><th>{{ 'pur.rfq.supplierCode' | translate }}</th><th>{{ 'pur.rfq.supplierName' | translate }}</th>
          <th>{{ 'pur.rfq.sentAt' | translate }}</th></tr></thead><tbody>
          @for (supplier of d.invitations; track supplier.id) { <tr><td>{{ supplier.supplierCodeSnapshot ?? supplier.supplierId }}</td>
            <td>{{ supplier.supplierNameSnapshot ?? '—' }}</td><td>{{ supplier.sentAt ? (supplier.sentAt | date:'yyyy-MM-dd HH:mm') : '—' }}</td></tr> }</tbody></table>
      </section> }
    </section>`,
  styles: `.rfqs{display:grid;gap:16px;color:var(--erp-color-text)}h1{font-size:20px;margin:0;text-wrap:balance}.filters{display:flex;gap:8px;align-items:end;flex-wrap:wrap}.filters label,.filters .filter-field{display:grid;gap:4px;font-size:12px}.filters small{color:var(--erp-color-error,#cf1322);max-width:180px}.filters input,.filters select{min-height:32px;background:var(--erp-color-bg-container);color:var(--erp-color-text);border:1px solid var(--erp-color-border);border-radius:4px}.filters input[type=number]{width:120px}.detail{padding:16px;border:1px solid var(--erp-color-border);overflow-x:auto}.detail table{width:100%}.detail th,.detail td{padding:6px;text-align:left;border-bottom:1px solid var(--erp-color-border);font-variant-numeric:tabular-nums}.detail td{overflow-wrap:anywhere}`,
})
export class RfqPage {
  private readonly api = inject(RfqService);
  private readonly company = inject(CompanyContextService);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  readonly rows = signal<RfqSummary[]>([]); readonly detail = signal<RfqDetail | null>(null);
  readonly total = signal(0); readonly page = signal(1); readonly pageSize = signal(20);
  readonly loading = signal(false); readonly loadFailed = signal(false); readonly filtered = signal(false);
  readonly error = signal<string | null>(null); readonly errorTrace = signal<string | null>(null);
  readonly errorDetails = signal<string[]>([]);
  keyword = ''; from = ''; to = ''; status = ''; requisitionLineId: number | null = null;
  private applied = { keyword: '', from: '', to: '', status: '', requisitionLineId: null as number | null };
  private generation = 0; private detailGeneration = 0;
  private firstLoad = true;
  constructor() {
    const params = this.route.snapshot.queryParamMap;
    this.keyword = params.get('keyword') ?? '';
    this.from = params.get('from') ?? '';
    this.to = params.get('to') ?? '';
    const status = params.get('status') ?? '';
    this.status = ['Draft', 'Sent', 'Closed'].includes(status) ? status : '';
    const source = Number(params.get('requisitionLineId'));
    this.requisitionLineId = Number.isSafeInteger(source) && source > 0 ? source : null;
    const page = Number(params.get('page'));
    if (Number.isSafeInteger(page) && page > 0) this.page.set(page);
    effect(() => { this.company.current(); untracked(() => { this.search(!this.firstLoad); this.firstLoad = false; }); });
  }
  statusKey(status: RfqStatus): string { return 'pur.rfq.' + ({ Draft: 'draft', Sent: 'sent', Closed: 'closed' }[status]); }
  statusColor(status: RfqStatus): string { return { Draft: 'default', Sent: 'processing', Closed: 'default' }[status]; }
  private clearError(): void { this.error.set(null); this.errorTrace.set(null); this.errorDetails.set([]); }
  private showError(error: unknown): void { this.error.set(this.api.errorKey(error)); this.errorTrace.set(apiErrorTraceId(error));
    this.errorDetails.set(apiValidationDetails(error)); }
  onQuery(query: NzTableQueryParams): void { if (query.pageIndex !== this.page() || query.pageSize !== this.pageSize()) {
    this.page.set(query.pageIndex); this.pageSize.set(query.pageSize); this.updateUrl(); this.load(); } }
  private updateUrl(): void {
    void this.router.navigate([], { relativeTo: this.route, replaceUrl: true, queryParams: {
      keyword: this.applied.keyword || null, from: this.applied.from || null, to: this.applied.to || null,
      status: this.applied.status || null, requisitionLineId: this.applied.requisitionLineId,
      page: this.page() > 1 ? this.page() : null
    } });
  }
  search(resetPage = true): void {
    if (this.from && this.to && this.from > this.to) { this.error.set('pur.rfq.invalidDates'); return; }
    if (this.requisitionLineId !== null && (!Number.isSafeInteger(this.requisitionLineId) || this.requisitionLineId <= 0)) {
      this.error.set('pur.rfq.invalidSource'); return;
    }
    this.applied = { keyword: this.keyword.trim(), from: this.from, to: this.to, status: this.status,
      requisitionLineId: this.requisitionLineId };
    this.filtered.set(!!(this.applied.keyword || this.applied.from || this.applied.to || this.applied.status || this.applied.requisitionLineId));
    if (resetPage) this.page.set(1);
    this.updateUrl(); this.load();
  }
  reset(): void { this.keyword = ''; this.from = ''; this.to = ''; this.status = ''; this.requisitionLineId = null;
    this.search(); }
  load(preserveError = false): void { const current = ++this.generation; ++this.detailGeneration; this.detail.set(null); this.loading.set(true);
    this.loadFailed.set(false); if (!preserveError) this.clearError();
    this.api.list({ page: this.page(), pageSize: this.pageSize(), keyword: this.applied.keyword,
      from: this.applied.from, to: this.applied.to, status: this.applied.status as RfqStatus,
      requisitionLineId: this.applied.requisitionLineId ?? undefined }).subscribe({
      next: result => { if (current !== this.generation) return; this.rows.set(result.items);
        this.total.set(result.totalCount); this.loading.set(false); },
      error: error => { if (current !== this.generation) return; this.rows.set([]); this.total.set(0);
        this.loadFailed.set(true); this.showError(error); this.loading.set(false); }
    });
  }
  view(row: RfqSummary): void { const current = ++this.detailGeneration; this.detail.set(null); this.clearError();
    this.api.get(row.id).subscribe({ next: detail => { if (current === this.detailGeneration) this.detail.set(detail); },
      error: error => { if (current !== this.detailGeneration) return; this.showError(error);
        if (error instanceof HttpErrorResponse && error.status === 404) this.load(true); } }); }
}
