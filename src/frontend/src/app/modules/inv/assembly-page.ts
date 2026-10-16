import { ChangeDetectionStrategy, Component, effect, inject, signal, untracked } from '@angular/core';
import { DatePipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import { NzButtonModule } from 'ng-zorro-antd/button';
import { NzTableModule, NzTableQueryParams } from 'ng-zorro-antd/table';
import { NzTagModule } from 'ng-zorro-antd/tag';
import { forkJoin, from, map, mergeMap, toArray } from 'rxjs';
import { HasPermissionDirective } from '../../core/auth/has-permission.directive';
import { CompanyContextService } from '../../core/company/company-context.service';
import { apiErrorTraceId } from '../../core/http/api-error';
import { ErpDialogService } from '../../shared/dialogs/dialog.service';
import { ApiErrorDisplay } from '../../shared/api-error-display';
import { fetchAllPages, OPTION_PAGE_SIZE } from '../../shared/list-page/fetch-all-pages';
import { AssemblyDetail, AssemblyService, AssemblyStatus, AssemblySummary } from './assembly.service';
import { assemblyValidationDetails } from './assembly-validation';
import { InventorySavedReferences } from './selection-options.service';
import { trackingLabel } from './tracking';
import { csvCell } from './transfer-page';

/** 介面 5.9：目前公司組裝列表、已保存詳情、審批動作與跨頁安全 CSV。 */
@Component({
  selector: 'erp-assembly-page',
  imports: [FormsModule, RouterLink, TranslatePipe, DatePipe, ApiErrorDisplay, NzButtonModule, NzTableModule, NzTagModule, HasPermissionDirective],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <section class="assemblies"><header><h1>{{ 'inv.assembly.title' | translate }}</h1>
      <a nz-button nzType="primary" routerLink="new" *erpHasPermission="'INV.ASSEMBLY.CREATE'" data-testid="inv-assembly-new">{{ 'inv.assembly.new' | translate }}</a></header>
      <div class="filters">
        <label>{{ 'inv.assembly.keyword' | translate }} <input type="search" name="assemblyKeyword" autocomplete="off" [(ngModel)]="keyword" data-testid="inv-assembly-search" (keyup.enter)="search()" /></label>
        <label>{{ 'inv.assembly.from' | translate }} <input type="date" name="assemblyFrom" autocomplete="off" [(ngModel)]="from" /></label>
        <label>{{ 'inv.assembly.to' | translate }} <input type="date" name="assemblyTo" autocomplete="off" [(ngModel)]="to" /></label>
        <label>{{ 'inv.assembly.finishedId' | translate }} <input type="number" min="1" step="1" name="assemblyFinishedItemId" [(ngModel)]="finishedItemId" data-testid="inv-assembly-finished-id" /></label>
        <label>{{ 'inv.assembly.status' | translate }} <select name="assemblyStatus" [(ngModel)]="status" data-testid="inv-assembly-status">
          <option value="">{{ 'inv.assembly.all' | translate }}</option>
          <option value="Draft">{{ 'inv.assembly.draft' | translate }}</option><option value="PendingApproval">{{ 'inv.assembly.pending' | translate }}</option>
          <option value="Rejected">{{ 'inv.assembly.rejected' | translate }}</option><option value="Posted">{{ 'inv.assembly.posted' | translate }}</option>
        </select></label>
        <button nz-button (click)="search()" data-testid="inv-assembly-filter">{{ 'inv.assembly.search' | translate }}</button>
        <button nz-button [disabled]="exporting()" [attr.title]="exporting() ? ('inv.assembly.busy' | translate) : null"
          (click)="exportCsv()" data-testid="inv-assembly-export">{{ 'inv.assembly.export' | translate }}</button>
      </div>
      @if (error()) { <erp-api-error-display [messageKey]="error()!" [traceId]="errorTrace()" [details]="errorDetails()" data-testid="inv-assembly-error" /> }
      <nz-table #table [nzData]="rows()" [nzLoading]="loading()" [nzFrontPagination]="false" [nzTotal]="total()"
        [nzPageIndex]="page()" [nzPageSize]="pageSize()" (nzQueryParams)="onQuery($event)"
        [nzNoResult]="(loadFailed() ? 'inv.assembly.loadFailed' : filtered() ? 'inv.assembly.noResults' : 'inv.assembly.empty') | translate" data-testid="inv-assembly-table">
        <thead><tr><th>{{ 'inv.assembly.number' | translate }}</th><th>{{ 'inv.assembly.date' | translate }}</th>
          <th>{{ 'inv.assembly.finished' | translate }}</th><th>{{ 'inv.assembly.quantity' | translate }}</th>
          <th>{{ 'inv.assembly.components' | translate }}</th><th>{{ 'inv.assembly.status' | translate }}</th><th></th></tr></thead>
        <tbody>@for (row of table.data; track row.id) { <tr [attr.data-testid]="'inv-assembly-row-' + row.id">
          <td><button nz-button nzType="link" (click)="view(row)">{{ row.documentNo }}</button></td>
          <td>{{ row.businessDate }}</td><td>{{ row.finishedItemCode && row.finishedItemName ? row.finishedItemCode + ' · ' + row.finishedItemName : ('inv.assembly.itemNumber' | translate) + ' ' + row.finishedItemId }}</td>
          <td>{{ row.finishedQuantity }}</td><td>{{ row.componentCount }}</td>
          <td><nz-tag [nzColor]="statusColor(row.status)">{{ statusKey(row.status) | translate }}</nz-tag></td><td>
            @if (row.status === 'Draft' || row.status === 'Rejected') {
              <a nz-button nzType="link" *erpHasPermission="'INV.ASSEMBLY.EDIT'" [routerLink]="[row.id, 'edit']">{{ 'inv.assembly.editAction' | translate }}</a>
            }
            @if (row.status === 'Draft') {
              <button nz-button nzType="link" *erpHasPermission="'INV.ASSEMBLY.SUBMIT'" [disabled]="busy()"
                [attr.title]="busy() ? ('inv.assembly.busy' | translate) : null" (click)="submit(row)">{{ 'inv.assembly.submit' | translate }}</button>
              <button nz-button nzType="link" nzDanger *erpHasPermission="'INV.ASSEMBLY.DELETE'" [disabled]="busy()"
                [attr.title]="busy() ? ('inv.assembly.busy' | translate) : null" (click)="remove(row)">{{ 'inv.assembly.delete' | translate }}</button>
            }
            <button nz-button nzType="link" (click)="print(row)">{{ 'inv.assembly.print' | translate }}</button>
          </td></tr>}</tbody>
      </nz-table>
      @if (detail(); as d) { <section class="detail" data-testid="inv-assembly-detail">
        <button nz-button (click)="detail.set(null)" [attr.aria-label]="'inv.assembly.close' | translate">×</button>
        <h2>{{ d.documentNo }} · {{ statusKey(d.status) | translate }}</h2>
        <p>{{ d.businessDate }} · {{ d.remark }}</p>
        @if (d.approval; as approval) { <p>{{ 'inv.assembly.approval' | translate }}: {{ approvalStatusKey(approval.status) | translate }}</p> }
        @if (d.approvalHistory?.length) { <section class="approval-history" data-testid="inv-assembly-approval-history">
          <h3>{{ 'inv.assembly.approvalHistory' | translate }}</h3>
          @for (attempt of d.approvalHistory; track attempt.instanceId) {
            <div class="approval-attempt"><strong>{{ approvalStatusKey(attempt.status) | translate }}</strong>
              <span> · {{ attempt.submittedAt | date:'yyyy-MM-dd HH:mm' }}</span>
              @for (step of attempt.steps; track step.stepNo) {
                <p>{{ 'inv.assembly.approvalStep' | translate:{ number: step.stepNo } }} · {{ step.name }}
                  · {{ approvalStepKey(step.status) | translate }}
                  @if (step.actedAt) { · {{ step.actedAt | date:'yyyy-MM-dd HH:mm' }} }
                  @if (step.comment) { <span class="approval-comment"> · {{ 'inv.assembly.approvalComment' | translate }}：{{ step.comment }}</span> }
                </p>
              }
            </div>
          }
        </section> }
        <table><thead><tr><th>#</th><th>{{ 'inv.assembly.role' | translate }}</th><th>{{ 'inv.assembly.item' | translate }}</th>
          <th>{{ 'inv.assembly.warehouse' | translate }}</th><th>{{ 'inv.assembly.location' | translate }}</th>
          <th>{{ 'inv.assembly.quantity' | translate }}</th><th>{{ 'inv.assembly.unitCost' | translate }}</th>
          <th>{{ 'inv.assembly.tracking' | translate }}</th></tr></thead>
          <tbody>@for (line of d.lines; track line.id) { <tr><td>{{ line.lineNo }}</td>
            <td>{{ (line.role === 'Finished' ? 'inv.assembly.finished' : 'inv.assembly.component') | translate }}</td>
            <td>{{ name(references()?.items, line.itemId) }}</td><td>{{ name(references()?.warehouses, line.warehouseId) }}</td>
            <td>{{ name(references()?.locations, line.locationId) }}</td><td>{{ line.quantity }}</td>
            <td>{{ line.postedUnitCost ?? '—' }}</td><td>{{ trackingLabel(line.tracking) }}</td></tr> }</tbody></table>
      </section> }
    </section>
  `,
  styles: `.assemblies{display:grid;gap:16px;color:var(--erp-color-text)}header,.filters{display:flex;gap:8px;align-items:center;flex-wrap:wrap}header{justify-content:space-between}h1{font-size:20px;margin:0;text-wrap:balance}.filters label{display:grid;gap:4px;font-size:12px}.filters input,.filters select{min-height:32px;background:var(--erp-color-bg-container);color:var(--erp-color-text);border:1px solid var(--erp-color-border);border-radius:4px}.filters input[type=number]{width:100px}.detail{padding:16px;border:1px solid var(--erp-color-border);overflow-x:auto}.detail table{width:100%}.detail td,.detail th{padding:6px;text-align:left;border-bottom:1px solid var(--erp-color-border);font-variant-numeric:tabular-nums}.detail p,.approval-attempt{overflow-wrap:anywhere}.approval-history{margin:12px 0;padding:12px;border:1px solid var(--erp-color-border);border-radius:4px}.approval-history h3{font-size:14px}.approval-attempt+ .approval-attempt{border-top:1px solid var(--erp-color-border);padding-top:8px}.approval-comment{white-space:pre-wrap}@media print{header,.filters,nz-table,.detail>button{display:none!important}.detail{border:0}}`,
})
export class AssemblyPage {
  readonly trackingLabel = trackingLabel;
  private readonly api = inject(AssemblyService); private readonly company = inject(CompanyContextService);
  private readonly translate = inject(TranslateService);
  private readonly dialogs = inject(ErpDialogService);
  readonly rows = signal<AssemblySummary[]>([]); readonly detail = signal<AssemblyDetail | null>(null);
  readonly references = signal<InventorySavedReferences | null>(null);
  readonly total = signal(0); readonly page = signal(1); readonly pageSize = signal(20);
  readonly loading = signal(false); readonly busy = signal(false); readonly exporting = signal(false);
  readonly error = signal<string | null>(null); readonly errorTrace = signal<string | null>(null);
  readonly errorDetails = signal<string[]>([]); readonly filtered = signal(false); readonly loadFailed = signal(false);
  keyword = ''; from = ''; to = ''; status = ''; finishedItemId: number | null = null;
  private generation = 0;
  constructor() { effect(() => { this.company.current(); untracked(() => { this.detail.set(null); this.references.set(null); this.search(); }); }); }
  statusKey(status: AssemblyStatus): string { return 'inv.assembly.' + ({ Draft: 'draft', PendingApproval: 'pending', Rejected: 'rejected', Posted: 'posted' }[status] ?? 'draft'); }
  statusColor(status: AssemblyStatus): string { return { Draft: 'default', PendingApproval: 'processing', Rejected: 'error', Posted: 'success' }[status] ?? 'default'; }
  approvalStatusKey(status: string): string { return 'inv.assembly.approvalStatuses.' +
    ({ Pending: 'pending', Approved: 'approved', Rejected: 'rejected', Cancelled: 'cancelled' }[status] ?? 'pending'); }
  approvalStepKey(status: string): string { return 'inv.assembly.approvalStepStatuses.' +
    ({ Waiting: 'waiting', Pending: 'pending', Approved: 'approved', Rejected: 'rejected', Skipped: 'skipped' }[status] ?? 'waiting'); }
  name(options: { id: number; code: string; name: string }[] | undefined, id: number | null): string {
    if (!id) return '—'; const value = options?.find(x => x.id === id);
    return value ? `${value.code} · ${value.name}` : `${id}`;
  }
  private showError(error: unknown): void { this.error.set(this.api.errorKey(error));
    this.errorTrace.set(apiErrorTraceId(error));
    this.errorDetails.set(assemblyValidationDetails(error, (key, params) => this.translate.instant(key, params))); }
  onQuery(query: NzTableQueryParams): void { if (query.pageIndex !== this.page() || query.pageSize !== this.pageSize()) {
    this.page.set(query.pageIndex); this.pageSize.set(query.pageSize); this.load(); } }
  search(): void { if (this.from && this.to && this.to < this.from) { this.error.set('inv.assembly.invalidDates'); return; }
    if (this.finishedItemId !== null && (!Number.isSafeInteger(this.finishedItemId) || this.finishedItemId <= 0)) {
      this.error.set('inv.assembly.invalidItemId'); return;
    }
    this.filtered.set(!!(this.keyword.trim() || this.from || this.to || this.status || this.finishedItemId)); this.page.set(1); this.load(); }
  private query(page: number, pageSize: number) { return { page, pageSize, keyword: this.keyword, from: this.from,
    to: this.to, status: this.status, finishedItemId: this.finishedItemId ?? undefined }; }
  load(): void { const current = ++this.generation; this.loading.set(true);
    this.error.set(null); this.errorTrace.set(null); this.errorDetails.set([]); this.loadFailed.set(false);
    this.api.list(this.query(this.page(), this.pageSize())).subscribe({ next: result => { if (current !== this.generation) return;
      this.rows.set(result.items); this.total.set(result.totalCount); this.loading.set(false);
    }, error: e => { if (current === this.generation) { this.rows.set([]); this.total.set(0); this.loadFailed.set(true);
      this.showError(e); this.loading.set(false); } } }); }
  view(row: AssemblySummary, print = false): void { const current = this.generation; this.error.set(null);
    this.detail.set(null); this.references.set(null);
    forkJoin({ detail: this.api.get(row.id), refs: this.api.savedReferences(row.id) }).subscribe({ next: ({ detail, refs }) => {
      if (current !== this.generation) return; this.detail.set(detail); this.references.set(refs);
      if (print) setTimeout(() => window.print());
    }, error: e => { if (current === this.generation) this.showError(e); } }); }
  print(row: AssemblySummary): void { this.view(row, true); }
  submit(row: AssemblySummary): void { if (this.busy()) return; this.busy.set(true); const current = this.generation;
    this.api.submit(row.id, row.version).subscribe({ next: () => { this.busy.set(false); if (current === this.generation) this.load(); },
      error: e => { this.busy.set(false); if (current !== this.generation) return;
        this.load(); this.showError(e); } }); }
  remove(row: AssemblySummary): void { if (this.busy()) return;
    this.dialogs.confirm({ titleKey: 'inv.assembly.confirmDelete', danger: true, okKey: 'inv.assembly.delete' }).subscribe(confirmed => {
      if (!confirmed) return; this.busy.set(true); const current = this.generation;
      this.api.remove(row.id, row.version).subscribe({ next: () => { this.busy.set(false); if (current === this.generation) this.load(); },
        error: e => { this.busy.set(false); if (current !== this.generation) return;
          this.load(); this.showError(e); } });
    }); }
  exportCsv(): void { if (this.exporting()) return;
    if (this.from && this.to && this.to < this.from) { this.error.set('inv.assembly.invalidDates'); return; }
    this.exporting.set(true); this.error.set(null); const current = this.generation;
    const fail = (e: unknown) => { if (current === this.generation) this.showError(e); this.exporting.set(false); };
    fetchAllPages(page => this.api.list(this.query(page, OPTION_PAGE_SIZE))).subscribe({ next: rows => {
      if (current !== this.generation) { this.exporting.set(false); return; }
      from(rows.map((row, index) => ({ row, index }))).pipe(mergeMap(({ row, index }) =>
        this.api.get(row.id).pipe(map(detail => ({ index, detail }))), 4), toArray()).subscribe({ next: details => {
        if (current !== this.generation) { this.exporting.set(false); return; }
        const data = [['Document No', 'Business Date', 'Status', 'Line', 'Role', 'Item ID', 'Warehouse ID', 'Location ID',
          'Quantity', 'Posted Unit Cost', 'Tracking', 'Remark'], ...details.sort((a, b) => a.index - b.index).flatMap(({ detail: d }) =>
          d.lines.map(line => [d.documentNo, d.businessDate, d.status, line.lineNo, line.role, line.itemId, line.warehouseId,
            line.locationId ?? '', line.quantity, line.postedUnitCost ?? '', trackingLabel(line.tracking), d.remark ?? '']))];
        const blob = new Blob(['\uFEFF', data.map(line => line.map(csvCell).join(',')).join('\r\n')], { type: 'text/csv;charset=utf-8' });
        const url = URL.createObjectURL(blob); const link = document.createElement('a'); link.href = url;
        link.download = 'inventory-assemblies.csv'; link.click(); URL.revokeObjectURL(url); this.exporting.set(false);
      }, error: fail });
    }, error: fail });
  }
}
