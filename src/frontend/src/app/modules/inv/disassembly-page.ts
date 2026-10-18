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
import { DisassemblyDetail, DisassemblyService, DisassemblyStatus, DisassemblySummary } from './disassembly.service';
import { disassemblyValidationDetails } from './disassembly-validation';
import { InventorySavedReferences } from './selection-options.service';
import { trackingLabel } from './tracking';
import { csvCell } from './transfer-page';

/** 介面 5.12：目前公司拆卸列表、已保存詳情、審批動作與跨頁安全 CSV。 */
@Component({
  selector: 'erp-disassembly-page',
  imports: [FormsModule, RouterLink, TranslatePipe, DatePipe, ApiErrorDisplay, NzButtonModule, NzTableModule, NzTagModule, HasPermissionDirective],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <section class="assemblies"><header><h1>{{ 'inv.disassembly.title' | translate }}</h1>
      <a nz-button nzType="primary" routerLink="new" *erpHasPermission="'INV.DISASSEMBLY.CREATE'" data-testid="inv-disassembly-new">{{ 'inv.disassembly.new' | translate }}</a></header>
      <div class="filters">
        <label>{{ 'inv.disassembly.keyword' | translate }} <input type="search" name="disassemblyKeyword" autocomplete="off" [(ngModel)]="keyword" data-testid="inv-disassembly-search" (keyup.enter)="search()" /></label>
        <label>{{ 'inv.disassembly.from' | translate }} <input type="date" name="disassemblyFrom" autocomplete="off" [(ngModel)]="from" /></label>
        <label>{{ 'inv.disassembly.to' | translate }} <input type="date" name="disassemblyTo" autocomplete="off" [(ngModel)]="to" /></label>
        <label>{{ 'inv.disassembly.finishedId' | translate }} <input type="number" min="1" step="1" name="disassemblyFinishedItemId" [(ngModel)]="finishedItemId" data-testid="inv-disassembly-finished-id" /></label>
        <label>{{ 'inv.disassembly.status' | translate }} <select name="disassemblyStatus" [(ngModel)]="status" data-testid="inv-disassembly-status">
          <option value="">{{ 'inv.disassembly.all' | translate }}</option>
          <option value="Draft">{{ 'inv.disassembly.draft' | translate }}</option><option value="PendingApproval">{{ 'inv.disassembly.pending' | translate }}</option>
          <option value="Rejected">{{ 'inv.disassembly.rejected' | translate }}</option><option value="Posted">{{ 'inv.disassembly.posted' | translate }}</option>
        </select></label>
        <button nz-button (click)="search()" data-testid="inv-disassembly-filter">{{ 'inv.disassembly.search' | translate }}</button>
        <button nz-button [disabled]="exporting()" [attr.title]="exporting() ? ('inv.disassembly.busy' | translate) : null"
          (click)="exportCsv()" data-testid="inv-disassembly-export">{{ 'inv.disassembly.export' | translate }}</button>
      </div>
      @if (error()) { <erp-api-error-display [messageKey]="error()!" [traceId]="errorTrace()" [details]="errorDetails()" data-testid="inv-disassembly-error" /> }
      <nz-table #table [nzData]="rows()" [nzLoading]="loading()" [nzFrontPagination]="false" [nzTotal]="total()"
        [nzPageIndex]="page()" [nzPageSize]="pageSize()" (nzQueryParams)="onQuery($event)"
        [nzNoResult]="(loadFailed() ? 'inv.disassembly.loadFailed' : filtered() ? 'inv.disassembly.noResults' : 'inv.disassembly.empty') | translate" data-testid="inv-disassembly-table">
        <thead><tr><th>{{ 'inv.disassembly.number' | translate }}</th><th>{{ 'inv.disassembly.date' | translate }}</th>
          <th>{{ 'inv.disassembly.finished' | translate }}</th><th>{{ 'inv.disassembly.quantity' | translate }}</th>
          <th>{{ 'inv.disassembly.components' | translate }}</th><th>{{ 'inv.disassembly.status' | translate }}</th><th></th></tr></thead>
        <tbody>@for (row of table.data; track row.id) { <tr [attr.data-testid]="'inv-disassembly-row-' + row.id">
          <td><button nz-button nzType="link" (click)="view(row)">{{ row.documentNo }}</button></td>
          <td>{{ row.businessDate }}</td><td>{{ row.finishedItemCode && row.finishedItemName ? row.finishedItemCode + ' · ' + row.finishedItemName : ('inv.disassembly.itemNumber' | translate) + ' ' + row.finishedItemId }}</td>
          <td>{{ row.finishedQuantity }}</td><td>{{ row.componentCount }}</td>
          <td><nz-tag [nzColor]="statusColor(row.status)">{{ statusKey(row.status) | translate }}</nz-tag></td><td>
            @if (row.status === 'Draft' || row.status === 'Rejected') {
              <a nz-button nzType="link" *erpHasPermission="'INV.DISASSEMBLY.EDIT'" [routerLink]="[row.id, 'edit']">{{ 'inv.disassembly.editAction' | translate }}</a>
            }
            @if (row.status === 'Draft') {
              <button nz-button nzType="link" *erpHasPermission="'INV.DISASSEMBLY.SUBMIT'" [disabled]="busy()"
                [attr.title]="busy() ? ('inv.disassembly.busy' | translate) : null" (click)="submit(row)">{{ 'inv.disassembly.submit' | translate }}</button>
              <button nz-button nzType="link" nzDanger *erpHasPermission="'INV.DISASSEMBLY.DELETE'" [disabled]="busy()"
                [attr.title]="busy() ? ('inv.disassembly.busy' | translate) : null" (click)="remove(row)">{{ 'inv.disassembly.delete' | translate }}</button>
            }
            <button nz-button nzType="link" (click)="print(row)">{{ 'inv.disassembly.print' | translate }}</button>
          </td></tr>}</tbody>
      </nz-table>
      @if (detail(); as d) { <section class="detail" data-testid="inv-disassembly-detail">
        <button nz-button (click)="detail.set(null)" [attr.aria-label]="'inv.disassembly.close' | translate">×</button>
        <h2>{{ d.documentNo }} · {{ statusKey(d.status) | translate }}</h2>
        <p>{{ d.businessDate }} · {{ d.remark }}</p>
        @if (d.approval; as approval) { <p>{{ 'inv.disassembly.approval' | translate }}: {{ approvalStatusKey(approval.status) | translate }}</p> }
        @if (d.approvalHistory?.length) { <section class="approval-history" data-testid="inv-disassembly-approval-history">
          <h3>{{ 'inv.disassembly.approvalHistory' | translate }}</h3>
          @for (attempt of d.approvalHistory; track attempt.instanceId) {
            <div class="approval-attempt"><strong>{{ approvalStatusKey(attempt.status) | translate }}</strong>
              <span> · {{ attempt.submittedAt | date:'yyyy-MM-dd HH:mm' }}</span>
              @for (step of attempt.steps; track step.stepNo) {
                <p>{{ 'inv.disassembly.approvalStep' | translate:{ number: step.stepNo } }} · {{ step.name }}
                  · {{ approvalStepKey(step.status) | translate }}
                  @if (step.actedAt) { · {{ step.actedAt | date:'yyyy-MM-dd HH:mm' }} }
                  @if (step.comment) { <span class="approval-comment"> · {{ 'inv.disassembly.approvalComment' | translate }}：{{ step.comment }}</span> }
                </p>
              }
            </div>
          }
        </section> }
        <table><thead><tr><th>#</th><th>{{ 'inv.disassembly.role' | translate }}</th><th>{{ 'inv.disassembly.item' | translate }}</th>
          <th>{{ 'inv.disassembly.warehouse' | translate }}</th><th>{{ 'inv.disassembly.location' | translate }}</th>
          <th>{{ 'inv.disassembly.quantity' | translate }}</th><th>{{ 'inv.disassembly.allocationWeight' | translate }}</th><th>{{ 'inv.disassembly.unitCost' | translate }}</th>
          <th>{{ 'inv.disassembly.tracking' | translate }}</th></tr></thead>
          <tbody>@for (line of d.lines; track line.id) { <tr><td>{{ line.lineNo }}</td>
            <td>{{ (line.role === 'Finished' ? 'inv.disassembly.finished' : 'inv.disassembly.component') | translate }}</td>
            <td>{{ name(references()?.items, line.itemId) }}</td><td>{{ name(references()?.warehouses, line.warehouseId) }}</td>
            <td>{{ name(references()?.locations, line.locationId) }}</td><td>{{ line.quantity }}</td><td>{{ line.allocationWeight ?? '—' }}</td>
            <td>{{ line.postedUnitCost ?? '—' }}</td><td>{{ trackingLabel(line.tracking) }}</td></tr> }</tbody></table>
      </section> }
    </section>
  `,
  styles: `.assemblies{display:grid;gap:16px;color:var(--erp-color-text)}header,.filters{display:flex;gap:8px;align-items:center;flex-wrap:wrap}header{justify-content:space-between}h1{font-size:20px;margin:0;text-wrap:balance}.filters label{display:grid;gap:4px;font-size:12px}.filters input,.filters select{min-height:32px;background:var(--erp-color-bg-container);color:var(--erp-color-text);border:1px solid var(--erp-color-border);border-radius:4px}.filters input[type=number]{width:100px}.detail{padding:16px;border:1px solid var(--erp-color-border);overflow-x:auto}.detail table{width:100%}.detail td,.detail th{padding:6px;text-align:left;border-bottom:1px solid var(--erp-color-border);font-variant-numeric:tabular-nums}.detail p,.approval-attempt{overflow-wrap:anywhere}.approval-history{margin:12px 0;padding:12px;border:1px solid var(--erp-color-border);border-radius:4px}.approval-history h3{font-size:14px}.approval-attempt+ .approval-attempt{border-top:1px solid var(--erp-color-border);padding-top:8px}.approval-comment{white-space:pre-wrap}@media print{header,.filters,nz-table,.detail>button{display:none!important}.detail{border:0}}`,
})
export class DisassemblyPage {
  readonly trackingLabel = trackingLabel;
  private readonly api = inject(DisassemblyService); private readonly company = inject(CompanyContextService);
  private readonly translate = inject(TranslateService);
  private readonly dialogs = inject(ErpDialogService);
  readonly rows = signal<DisassemblySummary[]>([]); readonly detail = signal<DisassemblyDetail | null>(null);
  readonly references = signal<InventorySavedReferences | null>(null);
  readonly total = signal(0); readonly page = signal(1); readonly pageSize = signal(20);
  readonly loading = signal(false); readonly busy = signal(false); readonly exporting = signal(false);
  readonly error = signal<string | null>(null); readonly errorTrace = signal<string | null>(null);
  readonly errorDetails = signal<string[]>([]); readonly filtered = signal(false); readonly loadFailed = signal(false);
  keyword = ''; from = ''; to = ''; status = ''; finishedItemId: number | null = null;
  private generation = 0; private detailGeneration = 0;
  constructor() { effect(() => { this.company.current(); untracked(() => { this.detail.set(null); this.references.set(null); this.search(); }); }); }
  statusKey(status: DisassemblyStatus): string { return 'inv.disassembly.' + ({ Draft: 'draft', PendingApproval: 'pending', Rejected: 'rejected', Posted: 'posted' }[status] ?? 'draft'); }
  statusColor(status: DisassemblyStatus): string { return { Draft: 'default', PendingApproval: 'processing', Rejected: 'error', Posted: 'success' }[status] ?? 'default'; }
  approvalStatusKey(status: string): string { return 'inv.disassembly.approvalStatuses.' +
    ({ Pending: 'pending', Approved: 'approved', Rejected: 'rejected', Cancelled: 'cancelled' }[status] ?? 'pending'); }
  approvalStepKey(status: string): string { return 'inv.disassembly.approvalStepStatuses.' +
    ({ Waiting: 'waiting', Pending: 'pending', Approved: 'approved', Rejected: 'rejected', Skipped: 'skipped' }[status] ?? 'waiting'); }
  name(options: { id: number; code: string; name: string }[] | undefined, id: number | null): string {
    if (!id) return '—'; const value = options?.find(x => x.id === id);
    return value ? `${value.code} · ${value.name}` : `${id}`;
  }
  private showError(error: unknown): void { this.error.set(this.api.errorKey(error));
    this.errorTrace.set(apiErrorTraceId(error));
    this.errorDetails.set(disassemblyValidationDetails(error, (key, params) => this.translate.instant(key, params))); }
  onQuery(query: NzTableQueryParams): void { if (query.pageIndex !== this.page() || query.pageSize !== this.pageSize()) {
    this.page.set(query.pageIndex); this.pageSize.set(query.pageSize); this.load(); } }
  search(): void { if (this.from && this.to && this.to < this.from) { this.error.set('inv.disassembly.invalidDates'); return; }
    if (this.finishedItemId !== null && (!Number.isSafeInteger(this.finishedItemId) || this.finishedItemId <= 0)) {
      this.error.set('inv.disassembly.invalidItemId'); return;
    }
    this.filtered.set(!!(this.keyword.trim() || this.from || this.to || this.status || this.finishedItemId)); this.page.set(1); this.load(); }
  private query(page: number, pageSize: number) { return { page, pageSize, keyword: this.keyword, from: this.from,
    to: this.to, status: this.status, finishedItemId: this.finishedItemId ?? undefined }; }
  load(preserveError = false): void { const current = ++this.generation; ++this.detailGeneration; this.loading.set(true);
    if (!preserveError) { this.error.set(null); this.errorTrace.set(null); this.errorDetails.set([]); }
    this.loadFailed.set(false);
    this.api.list(this.query(this.page(), this.pageSize())).subscribe({ next: result => { if (current !== this.generation) return;
      this.rows.set(result.items); this.total.set(result.totalCount); this.loading.set(false);
    }, error: e => { if (current === this.generation) { this.rows.set([]); this.total.set(0); this.loadFailed.set(true);
      this.showError(e); this.loading.set(false); } } }); }
  view(row: DisassemblySummary, print = false): void { const current = ++this.detailGeneration; this.error.set(null);
    this.detail.set(null); this.references.set(null);
    forkJoin({ detail: this.api.get(row.id), refs: this.api.savedReferences(row.id) }).subscribe({ next: ({ detail, refs }) => {
      if (current !== this.detailGeneration) return; this.detail.set(detail); this.references.set(refs);
      if (print) setTimeout(() => window.print());
    }, error: e => { if (current === this.detailGeneration) this.showError(e); } }); }
  print(row: DisassemblySummary): void { this.view(row, true); }
  submit(row: DisassemblySummary): void { if (this.busy()) return; this.busy.set(true); const current = this.generation;
    this.api.submit(row.id, row.version).subscribe({ next: () => { this.busy.set(false); if (current === this.generation) this.load(); },
      error: e => { this.busy.set(false); if (current !== this.generation) return;
        this.showError(e); this.load(true); } }); }
  remove(row: DisassemblySummary): void { if (this.busy()) return;
    this.dialogs.confirm({ titleKey: 'inv.disassembly.confirmDelete', danger: true, okKey: 'inv.disassembly.delete' }).subscribe(confirmed => {
      if (!confirmed) return; this.busy.set(true); const current = this.generation;
      this.api.remove(row.id, row.version).subscribe({ next: () => { this.busy.set(false); if (current === this.generation) this.load(); },
        error: e => { this.busy.set(false); if (current !== this.generation) return;
          this.showError(e); this.load(true); } });
    }); }
  exportCsv(): void { if (this.exporting()) return;
    if (this.from && this.to && this.to < this.from) { this.error.set('inv.disassembly.invalidDates'); return; }
    this.exporting.set(true); this.error.set(null); const current = this.generation;
    const fail = (e: unknown) => { if (current === this.generation) this.showError(e); this.exporting.set(false); };
    fetchAllPages(page => this.api.list(this.query(page, OPTION_PAGE_SIZE))).subscribe({ next: rows => {
      if (current !== this.generation) { this.exporting.set(false); return; }
      from(rows.map((row, index) => ({ row, index }))).pipe(mergeMap(({ row, index }) =>
        this.api.get(row.id).pipe(map(detail => ({ index, detail }))), 4), toArray()).subscribe({ next: details => {
        if (current !== this.generation) { this.exporting.set(false); return; }
        const headers = ['number', 'date', 'status', 'lineNo', 'role', 'itemId', 'warehouseId', 'locationId',
          'quantity', 'allocationWeight', 'unitCost', 'tracking', 'remark']
          .map(key => this.translate.instant(`inv.disassembly.${key}`));
        const data = [headers, ...details.sort((a, b) => a.index - b.index).flatMap(({ detail: d }) =>
          d.lines.map(line => [d.documentNo, d.businessDate, this.translate.instant(this.statusKey(d.status)), line.lineNo,
            this.translate.instant(line.role === 'Finished' ? 'inv.disassembly.finished' : 'inv.disassembly.component'), line.itemId, line.warehouseId,
            line.locationId ?? '', line.quantity, line.allocationWeight ?? '', line.postedUnitCost ?? '', trackingLabel(line.tracking), d.remark ?? '']))];
        const blob = new Blob(['\uFEFF', data.map(line => line.map(csvCell).join(',')).join('\r\n')], { type: 'text/csv;charset=utf-8' });
        const url = URL.createObjectURL(blob); const link = document.createElement('a'); link.href = url;
        link.download = 'inventory-disassemblies.csv'; link.click(); URL.revokeObjectURL(url); this.exporting.set(false);
      }, error: fail });
    }, error: fail });
  }
}
