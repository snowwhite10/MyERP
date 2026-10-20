import { ChangeDetectionStrategy, Component, effect, inject, signal, untracked } from '@angular/core';
import { DatePipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import { NzButtonModule } from 'ng-zorro-antd/button';
import { NzTableModule, NzTableQueryParams } from 'ng-zorro-antd/table';
import { NzTagModule } from 'ng-zorro-antd/tag';
import { HasPermissionDirective } from '../../core/auth/has-permission.directive';
import { CompanyContextService } from '../../core/company/company-context.service';
import { apiErrorTraceId, apiValidationDetails } from '../../core/http/api-error';
import { ErpDialogService } from '../../shared/dialogs/dialog.service';
import { ApiErrorDisplay } from '../../shared/api-error-display';
import { RequisitionDetail, RequisitionService, RequisitionStatus, RequisitionSummary } from './requisition.service';

/** 介面 4.8：公司範圍內的請購列表、已保存詳情與審批歷史。 */
@Component({
  selector: 'erp-requisition-page',
  imports: [FormsModule, DatePipe, TranslatePipe, ApiErrorDisplay, NzButtonModule, NzTableModule, NzTagModule, HasPermissionDirective],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <section class="requisitions"><header><h1>{{ 'pur.requisition.title' | translate }}</h1></header>
      <div class="filters">
        <label>{{ 'pur.requisition.keyword' | translate }} <input type="search" name="requisitionKeyword" autocomplete="off" [(ngModel)]="keyword" (keyup.enter)="search()" data-testid="pur-requisition-search" /></label>
        <label>{{ 'pur.requisition.from' | translate }} <input type="date" name="requisitionFrom" autocomplete="off" [(ngModel)]="from" /></label>
        <label>{{ 'pur.requisition.to' | translate }} <input type="date" name="requisitionTo" autocomplete="off" [(ngModel)]="to" /></label>
        <label>{{ 'pur.requisition.requester' | translate }} <input type="number" min="1" step="1" name="requisitionRequester" autocomplete="off" [(ngModel)]="requesterId" /></label>
        <label>{{ 'pur.requisition.status' | translate }} <select name="requisitionStatus" autocomplete="off" [(ngModel)]="status">
          <option value="">{{ 'pur.requisition.all' | translate }}</option>
          <option value="Draft">{{ 'pur.requisition.draft' | translate }}</option><option value="PendingApproval">{{ 'pur.requisition.pending' | translate }}</option>
          <option value="Rejected">{{ 'pur.requisition.rejected' | translate }}</option><option value="Approved">{{ 'pur.requisition.approved' | translate }}</option>
          <option value="Closed">{{ 'pur.requisition.closed' | translate }}</option>
        </select></label>
        <button nz-button (click)="search()" data-testid="pur-requisition-filter">{{ 'pur.requisition.search' | translate }}</button>
      </div>
      @if (error()) { <erp-api-error-display [messageKey]="error()!" [traceId]="errorTrace()" [details]="errorDetails()" data-testid="pur-requisition-error" /> }
      <nz-table #table [nzData]="rows()" [nzLoading]="loading()" [nzFrontPagination]="false" [nzTotal]="total()"
        [nzPageIndex]="page()" [nzPageSize]="pageSize()" (nzQueryParams)="onQuery($event)"
        [nzNoResult]="(loadFailed() ? 'pur.requisition.loadFailed' : filtered() ? 'pur.requisition.noResults' : 'pur.requisition.empty') | translate" data-testid="pur-requisition-table">
        <thead><tr><th>{{ 'pur.requisition.number' | translate }}</th><th>{{ 'pur.requisition.date' | translate }}</th>
          <th>{{ 'pur.requisition.requester' | translate }}</th><th>{{ 'pur.requisition.lines' | translate }}</th><th>{{ 'pur.requisition.status' | translate }}</th><th>{{ 'pur.requisition.actions' | translate }}</th></tr></thead>
        <tbody>@for (row of table.data; track row.id) { <tr [attr.data-testid]="'pur-requisition-row-' + row.id">
          <td><button nz-button nzType="link" (click)="view(row)">{{ row.documentNo }}</button></td><td>{{ row.businessDate }}</td>
          <td>{{ row.requesterId }}</td><td>{{ row.lineCount }}</td><td><nz-tag [nzColor]="statusColor(row.status)">{{ statusKey(row.status) | translate }}</nz-tag></td>
          <td>
            @if (canSubmit(row.status)) { <button nz-button nzType="link" *erpHasPermission="'PUR.REQUISITION.SUBMIT'" [disabled]="busy()"
              [attr.title]="busy() ? ('pur.requisition.busy' | translate) : null" (click)="submit(row)">{{ 'pur.requisition.submit' | translate }}</button> }
            @if (canDelete(row.status)) { <button nz-button nzType="link" nzDanger *erpHasPermission="'PUR.REQUISITION.DELETE'" [disabled]="busy()"
              [attr.title]="busy() ? ('pur.requisition.busy' | translate) : null" (click)="remove(row)">{{ 'pur.requisition.delete' | translate }}</button> }
            <button nz-button nzType="link" (click)="print(row)">{{ 'pur.requisition.print' | translate }}</button>
          </td></tr> }</tbody>
      </nz-table>
      @if (detail(); as d) { <section class="detail" data-testid="pur-requisition-detail">
        <button nz-button (click)="detail.set(null)" [attr.aria-label]="'pur.requisition.closeDetail' | translate">×</button>
        <h2>{{ d.documentNo }} · {{ statusKey(d.status) | translate }}</h2>
        <p>{{ 'pur.requisition.date' | translate }}: {{ d.businessDate }} · {{ 'pur.requisition.requester' | translate }}: {{ d.requesterId }}</p>
        @if (d.remark) { <p>{{ 'pur.requisition.remark' | translate }}: {{ d.remark }}</p> }
        <table><thead><tr><th>#</th><th>{{ 'pur.requisition.item' | translate }}</th><th>{{ 'pur.requisition.requiredDate' | translate }}</th>
          <th>{{ 'pur.requisition.quantity' | translate }}</th><th>{{ 'pur.requisition.orderedQuantity' | translate }}</th><th>{{ 'pur.requisition.remark' | translate }}</th></tr></thead>
          <tbody>@for (line of d.lines; track line.id) { <tr><td>{{ line.lineNo }}</td><td>{{ line.itemId }}</td><td>{{ line.requiredDate }}</td>
            <td>{{ line.quantity }}</td><td>{{ line.orderedQuantity }}</td><td>{{ line.remark }}</td></tr> }</tbody></table>
        <section class="history"><h3>{{ 'pur.requisition.approvalHistory' | translate }}</h3>
          @if (!d.approvalHistory.length) { <p>{{ 'pur.requisition.noHistory' | translate }}</p> }
          @for (attempt of d.approvalHistory; track attempt.instanceId) { <div class="attempt">
            <strong>{{ approvalStatusKey(attempt.status) | translate }}</strong> · {{ attempt.submittedAt | date:'yyyy-MM-dd HH:mm' }}
            @for (step of attempt.steps; track step.stepNo) { <p>{{ 'pur.requisition.approvalStep' | translate:{ number: step.stepNo } }} · {{ step.name }}
              · {{ approvalStepKey(step.status) | translate }} @if (step.actedAt) { · {{ step.actedAt | date:'yyyy-MM-dd HH:mm' }} }
              @if (step.comment) { · {{ step.comment }} }</p> }
          </div> }
        </section>
      </section> }
    </section>`,
  styles: `.requisitions{display:grid;gap:16px;color:var(--erp-color-text)}header,.filters{display:flex;gap:8px;align-items:center;flex-wrap:wrap}h1{font-size:20px;margin:0;text-wrap:balance}.filters label{display:grid;gap:4px;font-size:12px}.filters input,.filters select{min-height:32px;background:var(--erp-color-bg-container);color:var(--erp-color-text);border:1px solid var(--erp-color-border);border-radius:4px}.filters input[type=number]{width:110px}.detail{padding:16px;border:1px solid var(--erp-color-border);overflow-x:auto}.detail table{width:100%}.detail td,.detail th{padding:6px;text-align:left;border-bottom:1px solid var(--erp-color-border);font-variant-numeric:tabular-nums}.history{margin-top:16px}.attempt{padding:8px;border-top:1px solid var(--erp-color-border);overflow-wrap:anywhere}@media print{header,.filters,nz-table,.detail>button{display:none!important}.detail{border:0}}`,
})
export class RequisitionPage {
  private readonly api = inject(RequisitionService);
  private readonly company = inject(CompanyContextService);
  private readonly dialogs = inject(ErpDialogService);
  private readonly translate = inject(TranslateService);
  readonly rows = signal<RequisitionSummary[]>([]); readonly detail = signal<RequisitionDetail | null>(null);
  readonly total = signal(0); readonly page = signal(1); readonly pageSize = signal(20);
  readonly loading = signal(false); readonly busy = signal(false); readonly filtered = signal(false); readonly loadFailed = signal(false);
  readonly error = signal<string | null>(null); readonly errorTrace = signal<string | null>(null); readonly errorDetails = signal<string[]>([]);
  keyword = ''; from = ''; to = ''; status = ''; requesterId: number | null = null;
  private generation = 0; private detailGeneration = 0;
  constructor() { effect(() => { this.company.current(); untracked(() => { this.detail.set(null); this.search(); }); }); }
  static canSubmit(status: RequisitionStatus): boolean { return status === 'Draft'; }
  static canEdit(status: RequisitionStatus): boolean { return status === 'Draft' || status === 'Rejected'; }
  static canDelete(status: RequisitionStatus): boolean { return status === 'Draft'; }
  static validFilters(from: string, to: string, requesterId: number | null): boolean {
    return (!from || !to || from <= to) && (requesterId === null || Number.isSafeInteger(requesterId) && requesterId > 0);
  }
  readonly canSubmit = RequisitionPage.canSubmit; readonly canDelete = RequisitionPage.canDelete;
  statusKey(status: RequisitionStatus): string { return 'pur.requisition.' + ({ Draft: 'draft', PendingApproval: 'pending', Rejected: 'rejected', Approved: 'approved', Closed: 'closed' }[status]); }
  statusColor(status: RequisitionStatus): string { return { Draft: 'default', PendingApproval: 'processing', Rejected: 'error', Approved: 'success', Closed: 'default' }[status]; }
  approvalStatusKey(status: string): string { return 'pur.requisition.approval.' + ({ Pending: 'pending', Approved: 'approved', Rejected: 'rejected', Cancelled: 'cancelled' }[status] ?? 'pending'); }
  approvalStepKey(status: string): string { return 'pur.requisition.step.' + ({ Waiting: 'waiting', Pending: 'pending', Approved: 'approved', Rejected: 'rejected', Skipped: 'skipped' }[status] ?? 'waiting'); }
  private clearError(): void { this.error.set(null); this.errorTrace.set(null); this.errorDetails.set([]); }
  private showError(error: unknown): void { this.error.set(this.api.errorKey(error)); this.errorTrace.set(apiErrorTraceId(error));
    this.errorDetails.set(apiValidationDetails(error)); }
  onQuery(query: NzTableQueryParams): void { if (query.pageIndex !== this.page() || query.pageSize !== this.pageSize()) {
    this.page.set(query.pageIndex); this.pageSize.set(query.pageSize); this.load(); } }
  search(): void { if (!RequisitionPage.validFilters(this.from, this.to, this.requesterId)) {
    this.error.set(this.requesterId !== null && (!Number.isSafeInteger(this.requesterId) || this.requesterId <= 0)
      ? 'pur.requisition.invalidRequester' : 'pur.requisition.invalidDates'); return; }
    this.filtered.set(!!(this.keyword.trim() || this.from || this.to || this.status || this.requesterId)); this.page.set(1); this.load(); }
  private query() { return { page: this.page(), pageSize: this.pageSize(), keyword: this.keyword,
    from: this.from, to: this.to, status: this.status as RequisitionStatus, requesterId: this.requesterId ?? undefined }; }
  load(preserveError = false): void { const current = ++this.generation; ++this.detailGeneration; this.detail.set(null); this.loading.set(true);
    if (!preserveError) this.clearError(); this.loadFailed.set(false);
    this.api.list(this.query()).subscribe({ next: result => { if (current !== this.generation) return;
      this.rows.set(result.items); this.total.set(result.totalCount); this.loading.set(false);
    }, error: e => { if (current !== this.generation) return; this.rows.set([]); this.total.set(0);
      this.loadFailed.set(true); this.showError(e); this.loading.set(false); } }); }
  view(row: RequisitionSummary, print = false): void { const current = ++this.detailGeneration; this.clearError(); this.detail.set(null);
    this.api.get(row.id).subscribe({ next: detail => { if (current !== this.detailGeneration) return;
      this.detail.set(detail); if (print) setTimeout(() => window.print()); },
      error: e => { if (current === this.detailGeneration) this.showError(e); } }); }
  print(row: RequisitionSummary): void { this.view(row, true); }
  submit(row: RequisitionSummary): void { if (this.busy() || !RequisitionPage.canSubmit(row.status)) return;
    this.busy.set(true); const current = this.generation;
    this.api.submit(row.id, row.version).subscribe({ next: () => { this.busy.set(false); if (current === this.generation) this.load(); },
      error: e => { this.busy.set(false); if (current === this.generation) { this.showError(e); this.load(true); } } }); }
  remove(row: RequisitionSummary): void { if (this.busy() || !RequisitionPage.canDelete(row.status)) return;
    this.dialogs.confirm({ titleKey: 'pur.requisition.confirmDelete', danger: true, okKey: 'pur.requisition.delete' }).subscribe(confirmed => {
      if (!confirmed) return; this.busy.set(true); const current = this.generation;
      this.api.remove(row.id, row.version).subscribe({ next: () => { this.busy.set(false); if (current === this.generation) this.load(); },
        error: e => { this.busy.set(false); if (current === this.generation) { this.showError(e); this.load(true); } } });
    }); }
}
