import { ChangeDetectionStrategy, Component, DestroyRef, effect, inject, signal, untracked } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router } from '@angular/router';
import { TranslatePipe } from '@ngx-translate/core';
import { TimeoutError } from 'rxjs';
import { NzButtonModule } from 'ng-zorro-antd/button';
import { NzTableModule, NzTableQueryParams } from 'ng-zorro-antd/table';
import { NzTagModule } from 'ng-zorro-antd/tag';
import { CompanyContextService } from '../../core/company/company-context.service';
import { apiErrorKey, apiErrorTraceId } from '../../core/http/api-error';
import { ApiErrorDisplay } from '../../shared/api-error-display';
import { nextTableState, PAGE_SIZE_OPTIONS, sortOrderOf } from '../../shared/list-page/table-query';
import { SalesOrderDetail, SalesOrderQuery, SalesOrderService, SalesOrderStatus, SalesOrderSummary } from './order.service';

const STATUSES: SalesOrderStatus[] = ['Draft', 'Pending', 'Rejected', 'Approved', 'Cancelled'];
const SORTS = ['documentNo', 'businessDate', 'customerId'];
function validDate(value: string): boolean {
  if (!/^\d{4}-\d{2}-\d{2}$/.test(value)) return false;
  const [year, month, day] = value.split('-').map(Number);
  return year >= 1 && month >= 1 && month <= 12 && day >= 1 &&
    day <= new Date(Date.UTC(year, month, 0)).getUTCDate();
}

@Component({
  selector: 'erp-sales-order-page',
  imports: [FormsModule, TranslatePipe, NzButtonModule, NzTableModule, NzTagModule, ApiErrorDisplay],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <section class="order-page">
      <h1>{{ 'sd.order.title' | translate }}</h1>
      <form class="filters" (ngSubmit)="search()">
        <label>{{ 'sd.order.keyword' | translate }} <input name="keyword" autocomplete="off" maxlength="100" [(ngModel)]="keyword" data-testid="sd-order-keyword" /></label>
        <label>{{ 'sd.order.from' | translate }} <input type="date" name="from" [(ngModel)]="from" /></label>
        <label>{{ 'sd.order.to' | translate }} <input type="date" name="to" [(ngModel)]="to" /></label>
        <label>{{ 'sd.order.status' | translate }} <select name="status" [(ngModel)]="status" data-testid="sd-order-status">
          <option value="">{{ 'sd.order.all' | translate }}</option>
          @for (value of statuses; track value) { <option [value]="value">{{ statusKey(value) | translate }}</option> }
        </select></label>
        <button nz-button nzType="primary" type="submit" data-testid="sd-order-search">{{ 'sd.order.search' | translate }}</button>
        <button nz-button type="button" (click)="reset()">{{ 'sd.order.reset' | translate }}</button>
      </form>
      @if (validation()) { <p role="alert">{{ validation()! | translate }}</p> }
      @if (error()) { <erp-api-error-display [messageKey]="error()!" [traceId]="traceId()" data-testid="sd-order-error" />
        <button nz-button type="button" (click)="load()">{{ 'sd.order.retry' | translate }}</button> }
      <div class="scroll"><nz-table #table [nzData]="rows()" [nzLoading]="loading()" [nzFrontPagination]="false"
        [nzTotal]="total()" [nzPageIndex]="page()" [nzPageSize]="pageSize()" nzShowSizeChanger
        [nzPageSizeOptions]="pageSizeOptions" (nzQueryParams)="onQuery($event)"
        [nzNoResult]="(error() ? 'sd.order.loadFailed' : filtered() ? 'sd.order.noResults' : 'sd.order.empty') | translate"
        data-testid="sd-order-table">
        <thead><tr><th nzColumnKey="documentNo" [nzSortFn]="true" [nzSortOrder]="sortOrderOf(sort(), 'documentNo')">{{ 'sd.order.number' | translate }}</th>
          <th>{{ 'sd.order.customer' | translate }}</th>
          <th nzColumnKey="businessDate" [nzSortFn]="true" [nzSortOrder]="sortOrderOf(sort(), 'businessDate')">{{ 'sd.order.date' | translate }}</th>
          <th>{{ 'sd.order.status' | translate }}</th><th>{{ 'sd.order.amount' | translate }}</th></tr></thead>
        <tbody>@for (row of table.data; track row.id) { <tr [attr.data-testid]="'sd-order-row-' + row.id">
          <td><button nz-button nzType="link" type="button" (click)="open(row.id)">{{ row.documentNo }}</button></td>
          <td>{{ row.customerName }}</td><td>{{ row.businessDate }}</td>
          <td><nz-tag>{{ statusKey(row.status) | translate }}</nz-tag></td>
          <td class="number">{{ row.originalGross.toLocaleString(undefined, { minimumFractionDigits: 2, maximumFractionDigits: 4 }) }}</td>
        </tr> }</tbody>
      </nz-table></div>
      @if (detailLoading()) { <p role="status">{{ 'sd.order.loadingDetail' | translate }}</p> }
      @if (detailError()) { <erp-api-error-display [messageKey]="detailError()!" [traceId]="detailTraceId()" data-testid="sd-order-detail-error" /> }
      @if (detail(); as saved) { <section class="detail" data-testid="sd-order-detail">
        <header><h2>{{ saved.documentNo }}</h2><button nz-button type="button" (click)="close()">{{ 'sd.order.close' | translate }}</button></header>
        <p><strong>{{ 'sd.order.status' | translate }}:</strong> {{ statusKey(saved.status) | translate }}</p>
        <p>{{ 'sd.order.customer' | translate }}: {{ saved.customerCode }} · {{ saved.customerName }}</p>
        <p>{{ 'sd.order.date' | translate }}: {{ saved.businessDate }} · {{ 'sd.order.currency' | translate }}: {{ saved.currencyCode }}</p>
        <p>{{ 'sd.order.amount' | translate }}: {{ saved.originalGross }} · {{ 'sd.order.version' | translate }}: {{ saved.version }}</p>
        @if (saved.sourceQuotationId) { <p>{{ 'sd.order.sourceQuotation' | translate }}: {{ saved.sourceQuotationId }}</p> }
        @if (saved.remark) { <p>{{ 'sd.order.remark' | translate }}: {{ saved.remark }}</p> }
        <h3>{{ 'sd.order.lines' | translate }}</h3>
        <div class="scroll"><table><thead><tr><th>#</th><th>{{ 'sd.order.item' | translate }}</th>
          <th>{{ 'sd.order.quantity' | translate }}</th><th>{{ 'sd.order.unitPrice' | translate }}</th>
          <th>{{ 'sd.order.amount' | translate }}</th></tr></thead><tbody>
          @for (line of saved.lines; track line.id) { <tr><td>{{ line.lineNo }}</td><td>{{ line.itemCode }} · {{ line.itemName }}</td>
            <td class="number">{{ line.quantity }}</td><td class="number">{{ line.unitPrice }}</td><td class="number">{{ line.originalGross }}</td></tr> }
        </tbody></table></div>
      </section> }
    </section>`,
  styles: `.order-page{display:grid;gap:16px;color:var(--erp-color-text);min-width:0}h1,h2,h3{margin:0}.filters,.detail header{display:flex;gap:12px;flex-wrap:wrap;align-items:end}.filters label{display:grid;gap:4px}.filters input,.filters select{min-height:32px;padding:4px;background:var(--erp-color-bg-container);color:var(--erp-color-text);border:1px solid var(--erp-color-border);border-radius:4px}.scroll{overflow-x:auto;min-width:0}.scroll nz-table{display:block;min-width:700px}.detail{border:1px solid var(--erp-color-border);padding:16px;display:grid;gap:12px;min-width:0}.detail p,.detail td{overflow-wrap:break-word}.detail table{width:100%;min-width:560px;border-collapse:collapse}.detail th,.detail td{padding:8px;border-bottom:1px solid var(--erp-color-border);text-align:left}.number{text-align:right!important;font-variant-numeric:tabular-nums}button:focus-visible,input:focus-visible,select:focus-visible{outline:2px solid var(--erp-color-primary);outline-offset:2px}`,
})
export class SalesOrderPage {
  private readonly api = inject(SalesOrderService);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly company = inject(CompanyContextService);
  private readonly destroy = inject(DestroyRef);
  readonly statuses = STATUSES;
  readonly pageSizeOptions = [...PAGE_SIZE_OPTIONS];
  readonly sortOrderOf = sortOrderOf;
  readonly rows = signal<SalesOrderSummary[]>([]);
  readonly detail = signal<SalesOrderDetail | null>(null);
  readonly loading = signal(false); readonly detailLoading = signal(false);
  readonly total = signal(0); readonly page = signal(1); readonly pageSize = signal(20);
  readonly sort = signal('-businessDate'); readonly filtered = signal(false);
  readonly validation = signal<string | null>(null); readonly error = signal<string | null>(null);
  readonly traceId = signal<string | null>(null); readonly detailError = signal<string | null>(null);
  readonly detailTraceId = signal<string | null>(null);
  keyword = ''; from = ''; to = ''; status = '';
  private applied: Partial<SalesOrderQuery> = {};
  private listEpoch = 0; private detailEpoch = 0; private first = true;
  constructor() {
    const params = this.route.snapshot.queryParamMap;
    this.keyword = params.get('keyword') ?? ''; this.from = params.get('from') ?? ''; this.to = params.get('to') ?? '';
    const state = params.get('status') ?? '';
    this.status = STATUSES.includes(state as SalesOrderStatus) ? state : '';
    const page = Number(params.get('page'));
    if (Number.isSafeInteger(page) && page > 0) this.page.set(page);
    const pageSize = Number(params.get('pageSize'));
    if (PAGE_SIZE_OPTIONS.some(value => value === pageSize)) this.pageSize.set(pageSize);
    const sort = params.get('sort') ?? '';
    if (SORTS.includes(sort.replace(/^-/, ''))) this.sort.set(sort);
    this.destroy.onDestroy(() => { this.listEpoch++; this.detailEpoch++; });
    effect(() => { this.company.current(); untracked(() => {
      this.listEpoch++; this.detailEpoch++; this.close(); this.rows.set([]); this.total.set(0);
      if (!this.first) this.page.set(1);
      this.first = false; this.search(false);
    }); });
  }
  statusKey(status: string): string { return `sd.order.${status.toLowerCase()}`; }
  search(resetPage = true): void {
    if ((this.from && !validDate(this.from)) || (this.to && !validDate(this.to)) ||
        (this.from && this.to && this.from > this.to)) {
      this.validation.set('sd.order.invalidDates'); return;
    }
    this.validation.set(null);
    this.applied = { keyword: this.keyword.trim() || undefined, from: this.from || undefined,
      to: this.to || undefined, status: this.status as SalesOrderStatus || undefined };
    this.filtered.set(!!(this.applied.keyword || this.applied.from || this.applied.to || this.applied.status));
    if (resetPage) this.page.set(1);
    this.updateUrl(); this.load();
  }
  reset(): void { this.keyword = ''; this.from = ''; this.to = ''; this.status = ''; this.search(); }
  onQuery(params: NzTableQueryParams): void {
    const next = nextTableState(params, { page: this.page(), pageSize: this.pageSize(), sort: this.sort() }, SORTS, '-businessDate');
    if (!next) return;
    this.page.set(next.page); this.pageSize.set(next.pageSize); this.sort.set(next.sort);
    this.updateUrl(); this.load();
  }
  load(): void {
    const epoch = ++this.listEpoch;
    this.close(); this.loading.set(true); this.error.set(null); this.traceId.set(null);
    this.rows.set([]); this.total.set(0);
    this.api.list({ ...this.applied, page: this.page(), pageSize: this.pageSize(), sort: this.sort() }).subscribe({
      next: result => { if (epoch !== this.listEpoch) return;
        this.rows.set(result.items); this.total.set(result.totalCount); this.loading.set(false); },
      error: failure => { if (epoch !== this.listEpoch) return;
        this.error.set(failure instanceof TimeoutError ? 'sd.order.timeout' :
          apiErrorKey(failure, ['SAL.ORDER.NOT_FOUND', 'COMMON.REQUEST.VALIDATION_FAILED']));
        this.traceId.set(apiErrorTraceId(failure)); this.loading.set(false); },
    });
  }
  open(id: number): void {
    const epoch = ++this.detailEpoch;
    this.detail.set(null); this.detailError.set(null); this.detailTraceId.set(null); this.detailLoading.set(true);
    this.api.get(id).subscribe({ next: saved => { if (epoch !== this.detailEpoch) return;
      this.detail.set(saved); this.detailLoading.set(false); },
    error: failure => { if (epoch !== this.detailEpoch) return;
      this.detailError.set(failure instanceof TimeoutError ? 'sd.order.timeout' :
        apiErrorKey(failure, ['SAL.ORDER.NOT_FOUND']));
      this.detailTraceId.set(apiErrorTraceId(failure)); this.detailLoading.set(false); } });
  }
  close(): void { this.detailEpoch++; this.detail.set(null); this.detailError.set(null); this.detailLoading.set(false); }
  private updateUrl(): void {
    void this.router.navigate([], { relativeTo: this.route, replaceUrl: true, queryParams: {
      keyword: this.applied.keyword ?? null, from: this.applied.from ?? null, to: this.applied.to ?? null,
      status: this.applied.status ?? null, page: this.page() > 1 ? this.page() : null,
      pageSize: this.pageSize() !== 20 ? this.pageSize() : null,
      sort: this.sort() !== '-businessDate' ? this.sort() : null,
    } });
  }
}
