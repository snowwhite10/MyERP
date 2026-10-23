import { ChangeDetectionStrategy, Component, DestroyRef, effect, inject, signal, untracked } from '@angular/core';
import { DatePipe } from '@angular/common';
import { HttpErrorResponse } from '@angular/common/http';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { Subscription, from, map, mergeMap, reduce, switchMap } from 'rxjs';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import { NzButtonModule } from 'ng-zorro-antd/button';
import { NzTableModule, NzTableQueryParams } from 'ng-zorro-antd/table';
import { NzTagModule } from 'ng-zorro-antd/tag';
import { CompanyContextService } from '../../core/company/company-context.service';
import { PermissionService } from '../../core/auth/permission.service';
import { fetchAllPages, OPTION_PAGE_SIZE } from '../../shared/list-page/fetch-all-pages';
import { apiErrorTraceId, apiValidationDetails } from '../../core/http/api-error';
import { ApiErrorDisplay } from '../../shared/api-error-display';
import { HasPermissionDirective } from '../../core/auth/has-permission.directive';
import { ErpDialogService } from '../../shared/dialogs/dialog.service';
import { RfqDetail, RfqService, RfqStatus, RfqSummary } from './rfq.service';
import { RFQ_CSV_LIMIT, rfqCsv, rfqCsvRowCount } from './rfq-csv';

/** 介面 4.11 的列表、保存詳情及發送／關閉生命週期操作。 */
@Component({
  selector: 'erp-rfq-page',
  imports: [DatePipe, FormsModule, RouterLink, TranslatePipe, ApiErrorDisplay, HasPermissionDirective, NzButtonModule, NzTableModule, NzTagModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <section class="rfqs"><header><h1>{{ 'pur.rfq.title' | translate }}</h1>
      <a nz-button nzType="primary" routerLink="/pur/rfqs/new" *erpHasPermission="'PUR.RFQ.CREATE'" data-testid="pur-rfq-create">{{ 'pur.rfq.create' | translate }}</a>
      @if (canExport()) { <button nz-button (click)="exportCsv()" [disabled]="exporting() || loading() || total() === 0"
        [attr.title]="total() === 0 ? ('pur.rfq.noExport' | translate) : exporting() ? ('pur.rfq.exportBusy' | translate) : null"
        data-testid="pur-rfq-export">{{ 'pur.rfq.export' | translate }}</button>
        @if (exporting()) { <button nz-button (click)="cancelExport()" data-testid="pur-rfq-cancel-export">{{ 'pur.rfq.cancelExport' | translate }}</button> } }
      </header>
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
      @if (notice(); as message) { <p role="status" aria-live="polite" data-testid="pur-rfq-notice">{{ message | translate:{ count: exportCount() } }}</p> }
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
        <button nz-button (click)="print(d)" data-testid="pur-rfq-print">{{ 'pur.rfq.print' | translate }}</button>
        <a nz-button [routerLink]="['/pur/rfqs', d.id, 'quotations']" *erpHasPermission="'PUR.QUOTATION.VIEW'"
          data-testid="pur-rfq-quotations">{{ 'pur.quotation.title' | translate }}</a>
        <a nz-button [routerLink]="['/pur/rfqs', d.id, 'comparison']" *erpHasPermission="'PUR.RFQ.COMPARE'"
          data-testid="pur-rfq-comparison">{{ 'pur.comparison.title' | translate }}</a>
        @if (d.status === 'Draft') { <a nz-button [routerLink]="['/pur/rfqs', d.id, 'edit']" *erpHasPermission="'PUR.RFQ.EDIT'" data-testid="pur-rfq-edit">{{ 'pur.rfq.edit' | translate }}</a> }
        @if (d.status === 'Draft') { <button nz-button nzType="primary" *erpHasPermission="'PUR.RFQ.SEND'"
          [disabled]="busy()" [attr.aria-busy]="busy()" (click)="changeStatus(d, 'send')" data-testid="pur-rfq-send">{{ (busy() ? 'pur.rfq.busyAction' : 'pur.rfq.send') | translate }}</button> }
        @if (d.status === 'Sent') { <button nz-button *erpHasPermission="'PUR.RFQ.CLOSE'"
          [disabled]="busy()" [attr.aria-busy]="busy()" (click)="changeStatus(d, 'close')" data-testid="pur-rfq-close">{{ (busy() ? 'pur.rfq.busyAction' : 'pur.rfq.close') | translate }}</button> }
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
  styles: `.rfqs{display:grid;gap:16px;color:var(--erp-color-text)}header{display:flex;gap:8px;align-items:center;flex-wrap:wrap}h1{font-size:20px;margin:0;text-wrap:balance}.filters{display:flex;gap:8px;align-items:end;flex-wrap:wrap}.filters label,.filters .filter-field{display:grid;gap:4px;font-size:12px}.filters small{color:var(--erp-color-error,#cf1322);max-width:180px}.filters input,.filters select{min-height:32px;background:var(--erp-color-bg-container);color:var(--erp-color-text);border:1px solid var(--erp-color-border);border-radius:4px}.filters input[type=number]{width:120px}.detail{padding:16px;border:1px solid var(--erp-color-border);overflow-x:auto}.detail table{width:100%}.detail th,.detail td{padding:6px;text-align:left;border-bottom:1px solid var(--erp-color-border);font-variant-numeric:tabular-nums}.detail td{overflow-wrap:anywhere}@media print{.rfqs>header,.filters,nz-table,.detail>button,.detail>a,.rfqs>p,.rfqs>erp-api-error-display{display:none!important}.rfqs{display:block;color:#000;background:#fff}.detail{border:0;overflow:visible;padding:0;color:#000;background:#fff}.detail *{color:#000!important;background:#fff!important;border-color:#bbb!important}}`,
})
export class RfqPage {
  private readonly api = inject(RfqService);
  private readonly destroyRef = inject(DestroyRef);
  private readonly company = inject(CompanyContextService);
  private readonly permissions = inject(PermissionService);
  private readonly translate = inject(TranslateService);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly dialogs = inject(ErpDialogService);
  readonly rows = signal<RfqSummary[]>([]); readonly detail = signal<RfqDetail | null>(null);
  readonly total = signal(0); readonly page = signal(1); readonly pageSize = signal(20);
  readonly loading = signal(false); readonly loadFailed = signal(false); readonly filtered = signal(false);
  readonly busy = signal(false);
  readonly exporting = signal(false); readonly notice = signal<string | null>(null); readonly exportCount = signal(0);
  readonly error = signal<string | null>(null); readonly errorTrace = signal<string | null>(null);
  readonly errorDetails = signal<string[]>([]);
  keyword = ''; from = ''; to = ''; status = ''; requisitionLineId: number | null = null;
  private applied = { keyword: '', from: '', to: '', status: '', requisitionLineId: null as number | null };
  private generation = 0; private detailGeneration = 0; private mutationGeneration = 0;
  private exportRequest?: Subscription;
  private firstLoad = true;
  constructor() {
    this.destroyRef.onDestroy(() => { this.cancelExport(false); ++this.detailGeneration; });
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
  canExport(): boolean { return this.permissions.has('PUR.RFQ.VIEW') && this.permissions.has('PUR.RFQ.EXPORT'); }
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
    this.cancelExport(false);
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
  load(preserveError = false): void { this.cancelExport(false); const current = ++this.generation; ++this.detailGeneration; ++this.mutationGeneration;
    this.detail.set(null); this.busy.set(false); this.loading.set(true);
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
  print(detail: RfqDetail): void {
    const current = ++this.detailGeneration; this.clearError();
    this.api.get(detail.id).subscribe({ next: saved => {
      if (current !== this.detailGeneration) return;
      this.detail.set(saved);
      setTimeout(() => { if (current === this.detailGeneration) window.print(); });
    }, error: error => { if (current === this.detailGeneration) this.showError(error); } });
  }
  exportCsv(): void {
    if (!this.canExport() || this.exporting() || this.loading() || this.total() === 0) return;
    const query = { page: 1, pageSize: OPTION_PAGE_SIZE, keyword: this.applied.keyword, from: this.applied.from,
      to: this.applied.to, status: this.applied.status as RfqStatus,
      requisitionLineId: this.applied.requisitionLineId ?? undefined };
    const current = this.generation;
    let lineCount = 0;
    this.clearError(); this.notice.set(null); this.exporting.set(true);
    this.exportRequest = this.api.list(query).pipe(
      switchMap(first => fetchAllPages(page => this.api.list({ ...query, page })).pipe(map(rows => {
          if (rows.length !== first.totalCount || new Set(rows.map(row => row.id)).size !== rows.length)
            throw new Error('PUR.RFQ.EXPORT_CHANGED');
          return rows;
        }))),
      switchMap(rows => from(rows.map((row, index) => ({ row, index }))).pipe(
        mergeMap(({ row, index }) => this.api.get(row.id).pipe(map(detail => ({ row, detail, index }))), 4),
        reduce((indexed, entry) => {
          lineCount += entry.detail.lines.length;
          if (lineCount > RFQ_CSV_LIMIT) throw new RangeError('PUR.RFQ.EXPORT_LIMIT');
          indexed.push(entry);
          return indexed;
        }, [] as { row: RfqSummary; detail: RfqDetail; index: number }[]))),
    ).subscribe({ next: indexed => {
      if (current !== this.generation || !this.exporting()) return;
      try {
        if (indexed.some(({ row, detail }) => detail.version !== row.version || detail.id !== row.id))
          throw new Error('PUR.RFQ.EXPORT_CHANGED');
        const details = indexed.sort((a, b) => a.index - b.index).map(item => item.detail);
        const count = rfqCsvRowCount(details);
        if (count === 0) { this.notice.set('pur.rfq.noExport'); return; }
        const headers = ['number', 'date', 'deadline', 'status', 'lineNo', 'item', 'source',
          'requiredDate', 'quantity', 'remark', 'suppliers'].map(key => this.translate.instant(`pur.rfq.${key}`));
        const csv = rfqCsv(details, headers);
        const url = URL.createObjectURL(new Blob([csv], { type: 'text/csv;charset=utf-8' }));
        const link = document.createElement('a'); link.href = url; link.download = 'purchase-rfqs.csv';
        link.click(); URL.revokeObjectURL(url);
        this.exportCount.set(count); this.notice.set('pur.rfq.exported');
      } catch (error) {
        this.error.set(error instanceof RangeError ? 'pur.rfq.exportLimit' :
          error instanceof Error && error.message === 'PUR.RFQ.EXPORT_CHANGED' ? 'pur.rfq.exportChanged' : 'pur.rfq.exportFailed');
      } finally { this.exporting.set(false); }
    }, error: error => {
      if (current !== this.generation) return;
      this.exporting.set(false);
      if (error instanceof RangeError) this.error.set('pur.rfq.exportLimit');
      else if (error instanceof Error && error.message === 'PUR.RFQ.EXPORT_CHANGED') this.error.set('pur.rfq.exportChanged');
      else this.showError(error);
    } });
  }
  cancelExport(showNotice = true): void {
    this.exportRequest?.unsubscribe(); this.exportRequest = undefined;
    if (this.exporting()) { this.exporting.set(false); if (showNotice) this.notice.set('pur.rfq.exportCancelled'); }
    else if (!showNotice) this.notice.set(null);
  }
  changeStatus(detail: RfqDetail, action: 'send' | 'close'): void {
    if (this.busy() || this.detail()?.id !== detail.id || detail.status !== (action === 'send' ? 'Draft' : 'Sent')) return;
    this.dialogs.confirm({ titleKey: action === 'send' ? 'pur.rfq.confirmSend' : 'pur.rfq.confirmClose',
      contentKey: action === 'send' ? 'pur.rfq.sendExplanation' : 'pur.rfq.closeExplanation',
      okKey: action === 'send' ? 'pur.rfq.send' : 'pur.rfq.close' }).subscribe(confirmed => {
      if (!confirmed || this.detail()?.id !== detail.id || this.busy()) return;
      const current = ++this.mutationGeneration;
      this.busy.set(true); this.clearError();
      const request = action === 'send' ? this.api.send(detail.id, detail.version) : this.api.close(detail.id, detail.version);
      request.subscribe({
        next: saved => {
          if (current !== this.mutationGeneration) return;
          this.busy.set(false);
          this.rows.update(rows => rows.map(row => row.id === saved.id ? { ...row, status: saved.status,
            version: saved.version } : row));
          this.detail.set(saved);
          if (this.applied.status && this.applied.status !== saved.status) this.load();
          this.dialogs.success(action === 'send' ? 'pur.rfq.sentSuccess' : 'pur.rfq.closedSuccess',
            { number: saved.documentNo });
        },
        error: error => {
          if (current !== this.mutationGeneration) return;
          this.busy.set(false); this.showError(error);
          if (error instanceof HttpErrorResponse && (error.status === 404 || error.status === 409)) this.load(true);
        }
      });
    });
  }
}
