import { ChangeDetectionStrategy, Component, effect, inject, signal, untracked } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { TranslatePipe } from '@ngx-translate/core';
import { NzButtonModule } from 'ng-zorro-antd/button';
import { NzInputModule } from 'ng-zorro-antd/input';
import { NzTableModule, NzTableQueryParams } from 'ng-zorro-antd/table';
import { PermissionService } from '../../core/auth/permission.service';
import { CompanyContextService } from '../../core/company/company-context.service';
import { apiErrorTraceId } from '../../core/http/api-error';
import { ApiErrorDisplay } from '../../shared/api-error-display';
import { formatDecimalText } from '../../shared/decimal-text';
import { DocumentStatus, StatusTag } from '../../shared/status-tag/status-tag';
import { OTHER_PAYMENT_STATUSES, OtherPaymentMapRow, OtherPaymentService, OtherPaymentStatus, OtherPaymentSummary } from './other-payment.service';

const TAG: Record<OtherPaymentStatus, DocumentStatus> = {
  Draft: 'Draft', Pending: 'Submitted', Rejected: 'Rejected', Approved: 'Approved', Posted: 'Posted',
};

/** Company-scoped read-only other-payment list. Mapping visibility is a separate GL permission. */
@Component({
  selector: 'erp-other-payment-page',
  imports: [FormsModule, TranslatePipe, NzButtonModule, NzInputModule, NzTableModule, ApiErrorDisplay, StatusTag],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <section class="payments">
      <header><h1>{{ 'fi.otherPayment.title' | translate }}</h1></header>
      <p class="hint">{{ 'fi.otherPayment.nonAp' | translate }}</p>
      <form class="filters" (ngSubmit)="search()" data-testid="fi-op-filters">
        <input nz-input type="search" name="keyword" autocomplete="off" spellcheck="false" maxlength="50" [(ngModel)]="keyword"
          [attr.aria-label]="'fi.otherPayment.keyword' | translate" [placeholder]="'fi.otherPayment.keywordHint' | translate" data-testid="fi-op-keyword" />
        <label>{{ 'fi.otherPayment.status' | translate }}
          <select name="status" [(ngModel)]="status" data-testid="fi-op-status-filter">
            <option value="">{{ 'fi.otherPayment.allStatuses' | translate }}</option>
            @for (value of statuses; track value) { <option [value]="value">{{ statusKey(value) | translate }}</option> }
          </select></label>
        <label>{{ 'fi.otherPayment.from' | translate }} <input type="date" name="from" [(ngModel)]="from" data-testid="fi-op-from" /></label>
        <label>{{ 'fi.otherPayment.to' | translate }} <input type="date" name="to" [(ngModel)]="to" data-testid="fi-op-to" /></label>
        <button nz-button nzType="primary" type="submit" data-testid="fi-op-search">{{ 'fi.otherPayment.search' | translate }}</button>
        <button nz-button type="button" (click)="reset()" data-testid="fi-op-reset">{{ 'fi.otherPayment.reset' | translate }}</button>
      </form>
      @if (rangeInvalid()) { <p class="field-error" role="alert" data-testid="fi-op-range-error">{{ 'fi.otherPayment.rangeInvalid' | translate }}</p> }
      @if (error()) { <erp-api-error-display [messageKey]="error()!" [traceId]="traceId()" data-testid="fi-op-error" />
        <button nz-button type="button" (click)="load()" data-testid="fi-op-retry">{{ 'fi.otherPayment.retry' | translate }}</button> }
      <p class="scroll-hint">{{ 'fi.otherPayment.scrollHint' | translate }}</p>
      <div class="table-scroll"><nz-table #table [nzData]="rows()" [nzLoading]="loading()" [nzFrontPagination]="false" [nzTotal]="total()"
        [nzPageIndex]="page()" [nzPageSize]="pageSize()" [nzShowSizeChanger]="true" (nzQueryParams)="onQuery($event)"
        [nzNoResult]="(listFailed() ? 'fi.otherPayment.loadFailed' : filtered() ? 'fi.otherPayment.searchEmpty' : 'fi.otherPayment.empty') | translate"
        data-testid="fi-op-table">
        <thead><tr>
          <th nzColumnKey="documentNo" [nzSortFn]="true">{{ 'fi.otherPayment.documentNo' | translate }}</th>
          <th nzColumnKey="businessDate" [nzSortFn]="true" nzSortOrder="descend">{{ 'fi.otherPayment.businessDate' | translate }}</th>
          <th>{{ 'fi.otherPayment.payee' | translate }}</th><th>{{ 'fi.otherPayment.category' | translate }}</th>
          <th>{{ 'fi.otherPayment.mapping' | translate }}</th><th>{{ 'fi.otherPayment.fundAccount' | translate }}</th>
          <th>{{ 'fi.otherPayment.bankReference' | translate }}</th>
          <th nzColumnKey="amount" [nzSortFn]="true" nzAlign="right">{{ 'fi.otherPayment.amount' | translate }}</th>
          <th>{{ 'fi.otherPayment.status' | translate }}</th><th>{{ 'fi.otherPayment.journal' | translate }}</th>
        </tr></thead>
        <tbody>@for (row of table.data; track row.id) {
          <tr [attr.data-testid]="'fi-op-row-' + row.id">
            <td translate="no">{{ row.documentNo }}</td><td>{{ row.businessDate }}</td>
            <td class="wrap" [title]="row.payeeDescription">{{ row.payeeDescription }}</td>
            <td>{{ label(row.expenseCategoryCode, row.expenseCategoryName, row.expenseCategoryId) }}</td>
            <td class="mapping" aria-live="polite" [attr.data-testid]="'fi-op-map-' + row.id">{{ mappingKey(row) | translate }}</td>
            <td>{{ label(row.bankAccountCode, row.bankAccountName, row.bankAccountId) }}</td>
            <td class="wrap" [title]="row.bankReference">{{ row.bankReference }}</td>
            <td class="num">{{ money(row.amount) }} {{ row.currency }}</td>
            <td><erp-status-tag [status]="tag(row.status)" [labelKey]="statusKey(row.status)" /></td>
            <td translate="no">{{ row.journalId ? '#' + row.journalId : '—' }}</td>
          </tr>
        }</tbody>
      </nz-table></div>
    </section>`,
  styles: `:host{display:block;min-width:0}.payments{display:grid;gap:16px;min-width:0;color:var(--erp-color-text)}header{display:flex;align-items:center;justify-content:space-between}h1{font-size:20px;margin:0;text-wrap:balance}.hint,.scroll-hint{margin:0;color:var(--erp-color-text-secondary)}.scroll-hint{display:none}@media(max-width:1500px){.scroll-hint{display:block}}.filters{display:flex;align-items:center;gap:8px;flex-wrap:wrap}.filters input[type=search]{max-width:260px}.filters label{display:flex;gap:4px;align-items:center}.filters select,.filters input[type=date]{background:var(--erp-color-bg-container);color:var(--erp-color-text);border:1px solid var(--erp-color-border);border-radius:4px}.field-error{margin:0;color:var(--erp-color-error-text)}.table-scroll{min-width:0;overflow-x:auto;overscroll-behavior-inline:contain;scrollbar-gutter:stable}.table-scroll nz-table{display:block;min-width:1300px}.table-scroll th{white-space:nowrap}.table-scroll th:nth-child(2),.table-scroll td:nth-child(2){min-width:130px;white-space:nowrap}.table-scroll th:nth-child(5),.table-scroll td:nth-child(5){min-width:220px}.wrap{overflow-wrap:anywhere;min-width:9em}.num{text-align:right;font-variant-numeric:tabular-nums;white-space:nowrap}button:focus-visible,input:focus-visible,select:focus-visible{outline:2px solid var(--erp-color-primary);outline-offset:2px}`,
})
export class OtherPaymentPage {
  private readonly api = inject(OtherPaymentService);
  private readonly company = inject(CompanyContextService);
  readonly canViewMaps = inject(PermissionService).has('GL.OTHER_PAYMENT_MAP.VIEW');
  readonly statuses = OTHER_PAYMENT_STATUSES;
  readonly rows = signal<OtherPaymentSummary[]>([]);
  readonly maps = signal<Map<number, OtherPaymentMapRow>>(new Map());
  readonly mapFailed = signal(false); readonly mapLoading = signal(false);
  readonly total = signal(0); readonly page = signal(1); readonly pageSize = signal(20);
  readonly loading = signal(false); readonly error = signal<string | null>(null); readonly traceId = signal<string | null>(null);
  readonly listFailed = signal(false); readonly filtered = signal(false); readonly rangeInvalid = signal(false);
  keyword = ''; status = ''; from = ''; to = '';
  private sort = ''; private generation = 0;

  constructor() { effect(() => { this.company.current(); untracked(() => this.reset()); }); }
  statusKey(value: string): string { return `fi.otherPayment.state.${value.toLowerCase()}`; }
  tag(value: OtherPaymentStatus): DocumentStatus { return TAG[value] ?? 'Draft'; }
  label(code: string | null, name: string | null, id: number): string { return code ? `${code} · ${name ?? ''}` : `#${id}`; }
  money(value: string): string { return formatDecimalText(value, 'en-US'); }
  mappingKey(row: OtherPaymentSummary): string {
    if (!this.canViewMaps) return 'fi.otherPayment.map.hidden';
    if (this.mapFailed()) return 'fi.otherPayment.map.unavailable';
    if (this.mapLoading()) return 'fi.otherPayment.map.loading';
    const map = this.maps().get(row.expenseCategoryId);
    if (!map?.configured) return 'fi.otherPayment.map.missing';
    if (!map.expenseCategoryActive) return 'fi.otherPayment.map.categoryInactive';
    if (!map.expenseAccount?.isUsable) return 'fi.otherPayment.map.accountInvalid';
    return 'fi.otherPayment.map.ready';
  }
  reset(): void { this.keyword = ''; this.status = ''; this.from = ''; this.to = ''; this.search(); }
  search(): void {
    this.rangeInvalid.set(!!this.from && !!this.to && this.from > this.to);
    if (this.rangeInvalid()) return;
    this.filtered.set(!!(this.keyword.trim() || this.status || this.from || this.to));
    this.page.set(1); this.load();
  }
  onQuery(query: NzTableQueryParams): void {
    const sorted = query.sort.find(item => item.value);
    const sort = sorted ? (sorted.value === 'descend' ? '-' : '') + sorted.key : '';
    if (query.pageIndex === this.page() && query.pageSize === this.pageSize() && sort === this.sort) return;
    this.page.set(query.pageIndex); this.pageSize.set(query.pageSize); this.sort = sort; this.load();
  }
  load(): void {
    const current = ++this.generation;
    this.loading.set(true); this.error.set(null); this.traceId.set(null); this.listFailed.set(false);
    this.maps.set(new Map()); this.mapFailed.set(false); this.mapLoading.set(this.canViewMaps);
    this.api.list({ page: this.page(), pageSize: this.pageSize(), keyword: this.keyword, status: this.status, from: this.from,
      to: this.to, sort: this.sort }).subscribe({
      next: result => { if (current !== this.generation) return; this.rows.set(result.items); this.total.set(result.totalCount); this.loading.set(false); },
      error: e => { if (current !== this.generation) return; this.error.set(this.api.errorKey(e)); this.traceId.set(apiErrorTraceId(e));
        this.listFailed.set(true); this.rows.set([]); this.total.set(0); this.loading.set(false); },
    });
    if (this.canViewMaps) this.api.maps().subscribe({
      next: result => { if (current === this.generation) { this.maps.set(new Map(result.items.map(row => [row.expenseCategoryId, row]))); this.mapLoading.set(false); } },
      error: () => { if (current === this.generation) { this.mapFailed.set(true); this.mapLoading.set(false); } },
    });
  }
}
