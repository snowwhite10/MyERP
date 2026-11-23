import { ChangeDetectionStrategy, Component, DestroyRef, effect, inject, signal, untracked } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router } from '@angular/router';
import { TranslatePipe } from '@ngx-translate/core';
import { TimeoutError } from 'rxjs';
import { NzAlertModule } from 'ng-zorro-antd/alert';
import { NzButtonModule } from 'ng-zorro-antd/button';
import { NzTableModule, NzTableQueryParams } from 'ng-zorro-antd/table';
import { NzTagModule } from 'ng-zorro-antd/tag';
import { CompanyContextService } from '../../core/company/company-context.service';
import { apiErrorKey, apiErrorTraceId } from '../../core/http/api-error';
import { ApiErrorDisplay } from '../../shared/api-error-display';
import { formatDecimalText } from '../../shared/decimal-text';
import { nextTableState, PAGE_SIZE_OPTIONS, sortOrderOf } from '../../shared/list-page/table-query';
import { invoiceMoney, SalesInvoiceDetail, SalesInvoiceQuery, SalesInvoiceService, SalesInvoiceSummary } from './invoice.service';

const SORTS = ['documentNo', 'businessDate'];
const DEFAULT_SORT = '-businessDate';
function validDate(value: string): boolean {
  if (!/^\d{4}-\d{2}-\d{2}$/.test(value)) return false;
  const [year, month, day] = value.split('-').map(Number);
  return year >= 1 && month >= 1 && month <= 12 && day >= 1 &&
    day <= new Date(Date.UTC(year, month, 0)).getUTCDate();
}
function positiveId(value: string | null): number | undefined {
  const id = Number(value);
  return value && Number.isSafeInteger(id) && id > 0 ? id : undefined;
}

/**
 * Sales Invoice View Interface 2.14 (REQ_F4112.5.1, WU-SD-007b1): Posted invoices in the SAL.INVOICE.VIEW scope and a
 * read-only saved detail with original/base amounts and FI references (REQ_IO_A2.14.1–9). Issuing is 007b2.
 */
@Component({
  selector: 'erp-sales-invoice-page',
  imports: [FormsModule, TranslatePipe, NzAlertModule, NzButtonModule, NzTableModule, NzTagModule, ApiErrorDisplay],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <section class="invoice-page">
      <header class="head"><h1>{{ 'sd.invoice.title' | translate }}</h1></header>
      <nz-alert nzType="info" nzShowIcon [nzMessage]="'sd.invoice.notice' | translate" data-testid="sd-invoice-notice" />
      <form class="filters" (ngSubmit)="search()">
        <label>{{ 'sd.invoice.keyword' | translate }} <input name="keyword" autocomplete="off" maxlength="100" [(ngModel)]="keyword" data-testid="sd-invoice-keyword" /></label>
        <label>{{ 'sd.invoice.from' | translate }} <input type="date" name="from" autocomplete="off" [(ngModel)]="from" data-testid="sd-invoice-from" /></label>
        <label>{{ 'sd.invoice.to' | translate }} <input type="date" name="to" autocomplete="off" [(ngModel)]="to" data-testid="sd-invoice-to" /></label>
        <button nz-button nzType="primary" type="submit" data-testid="sd-invoice-search">{{ 'sd.invoice.search' | translate }}</button>
        <button nz-button type="button" (click)="reset()" data-testid="sd-invoice-reset">{{ 'sd.invoice.reset' | translate }}</button>
      </form>
      @if (customer(); as chosen) { <p class="chip" data-testid="sd-invoice-customer-filter">{{ 'sd.invoice.customerFilter' | translate: { name: chosen.name || ('#' + chosen.id) } }}
        <button nz-button nzType="link" type="button" (click)="clearCustomer()" data-testid="sd-invoice-customer-clear">{{ 'sd.invoice.clearCustomer' | translate }}</button></p> }
      @if (orderFilter()) { <p class="chip" data-testid="sd-invoice-order-filter">{{ 'sd.invoice.orderFilter' | translate: { id: orderFilter() } }}</p> }
      @if (validation()) { <p role="alert" data-testid="sd-invoice-validation">{{ validation()! | translate }}</p> }
      @if (error()) { <erp-api-error-display [messageKey]="error()!" [traceId]="traceId()" data-testid="sd-invoice-error" />
        <button nz-button type="button" (click)="load()" data-testid="sd-invoice-retry">{{ 'sd.invoice.retry' | translate }}</button> }
      <div class="scroll"><nz-table #table [nzData]="rows()" [nzLoading]="loading()" [nzFrontPagination]="false"
        [nzTotal]="total()" [nzPageIndex]="page()" [nzPageSize]="pageSize()" nzShowSizeChanger
        [nzPageSizeOptions]="pageSizeOptions" (nzQueryParams)="onQuery($event)"
        [nzNoResult]="(error() ? 'sd.invoice.loadFailed' : filtered() ? 'sd.invoice.noResults' : 'sd.invoice.empty') | translate"
        data-testid="sd-invoice-table">
        <thead><tr><th nzColumnKey="documentNo" [nzSortFn]="true" [nzSortOrder]="sortOrderOf(sort(), 'documentNo')">{{ 'sd.invoice.number' | translate }}</th>
          <th>{{ 'sd.invoice.order' | translate }}</th><th>{{ 'sd.invoice.customer' | translate }}</th>
          <th nzColumnKey="businessDate" [nzSortFn]="true" [nzSortOrder]="sortOrderOf(sort(), 'businessDate')">{{ 'sd.invoice.date' | translate }}</th>
          <th class="number">{{ 'sd.invoice.amount' | translate }}</th><th class="number">{{ 'sd.invoice.baseAmount' | translate }}</th>
          <th class="number">{{ 'sd.invoice.lineCount' | translate }}</th><th>{{ 'sd.invoice.fiJournal' | translate }}</th></tr></thead>
        <tbody>@for (row of table.data; track row.id) { <tr [attr.data-testid]="'sd-invoice-row-' + row.id">
          <td><button nz-button nzType="link" type="button" (click)="open(row.id)" [attr.data-testid]="'sd-invoice-open-' + row.id" translate="no">{{ row.documentNo }}</button></td>
          <td translate="no">{{ row.orderDocumentNo }}</td>
          <td><button nz-button nzType="link" type="button" (click)="filterCustomer(row)" [attr.aria-label]="'sd.invoice.filterCustomer' | translate: { name: row.customerName }"
            [attr.data-testid]="'sd-invoice-filter-customer-' + row.id">{{ row.customerName }}</button></td>
          <td>{{ row.businessDate }}</td>
          <td class="number">{{ row.currencyCode }} {{ money(row.originalGross) }}</td>
          <td class="number">{{ row.baseCurrencyCode }} {{ money(row.baseGross) }}</td>
          <td class="number">{{ row.lineCount }}</td><td>{{ row.fiJournalId ?? ('sd.invoice.notRecorded' | translate) }}</td>
        </tr> }</tbody>
      </nz-table></div>
      @if (detailLoading()) { <p role="status">{{ 'sd.invoice.loadingDetail' | translate }}</p> }
      @if (detailError()) { <erp-api-error-display [messageKey]="detailError()!" [traceId]="detailTraceId()" data-testid="sd-invoice-detail-error" />
        <button nz-button type="button" (click)="retryDetail()" data-testid="sd-invoice-detail-retry">{{ 'sd.invoice.retry' | translate }}</button> }
      @if (detail(); as saved) { <section class="detail" data-testid="sd-invoice-detail">
        <header><h2 translate="no">{{ saved.documentNo }}</h2>
          <button nz-button type="button" (click)="close()" data-testid="sd-invoice-close">{{ 'sd.invoice.close' | translate }}</button></header>
        <p><strong>{{ 'sd.invoice.status' | translate }}:</strong> <nz-tag [nzColor]="saved.status === 'Posted' ? 'green' : 'default'" data-testid="sd-invoice-detail-status">{{ statusKey(saved.status) | translate }}</nz-tag></p>
        <dl class="facts">
          <div><dt>{{ 'sd.invoice.order' | translate }}</dt><dd translate="no">{{ saved.orderDocumentNo }}</dd></div>
          <div><dt>{{ 'sd.invoice.customer' | translate }}</dt><dd data-testid="sd-invoice-detail-customer">{{ saved.customerCode }} · {{ saved.customerName }}</dd></div>
          <div><dt>{{ 'sd.invoice.date' | translate }}</dt><dd>{{ saved.businessDate }}</dd></div>
          <div><dt>{{ 'sd.invoice.currency' | translate }}</dt><dd>{{ saved.currencyCode }} → {{ saved.baseCurrencyCode }}</dd></div>
          <div><dt>{{ 'sd.invoice.exchangeRate' | translate }}</dt><dd data-testid="sd-invoice-detail-rate">{{ decimal(saved.exchangeRate) }}</dd></div>
          <div><dt>{{ 'sd.invoice.postedAt' | translate }}</dt><dd>{{ saved.postedAt ? time(saved.postedAt) : ('sd.invoice.notRecorded' | translate) }}</dd></div>
          <div><dt>{{ 'sd.invoice.version' | translate }}</dt><dd>{{ saved.version }}</dd></div>
        </dl>
        <section class="fi" data-testid="sd-invoice-fi">
          <h3>{{ 'sd.invoice.fiTitle' | translate }}</h3>
          <p class="hint">{{ 'sd.invoice.fiHint' | translate }}</p>
          <dl class="facts">
            <div><dt>{{ 'sd.invoice.fiJournal' | translate }}</dt><dd data-testid="sd-invoice-fi-journal">{{ saved.fiJournalId ?? ('sd.invoice.notRecorded' | translate) }}</dd></div>
            <div><dt>{{ 'sd.invoice.fiReceivable' | translate }}</dt><dd data-testid="sd-invoice-fi-receivable">{{ saved.fiReceivableId ?? ('sd.invoice.notRecorded' | translate) }}</dd></div>
            <div><dt>{{ 'sd.invoice.fiSource' | translate }}</dt><dd data-testid="sd-invoice-fi-source">{{ saved.fiSalesSourceId ?? ('sd.invoice.notRecorded' | translate) }}</dd></div>
          </dl>
        </section>
        <div class="scroll"><table class="totals" data-testid="sd-invoice-totals"><thead><tr><th></th>
          <th class="number">{{ 'sd.invoice.net' | translate }}</th><th class="number">{{ 'sd.invoice.tax' | translate }}</th>
          <th class="number">{{ 'sd.invoice.gross' | translate }}</th></tr></thead><tbody>
          <tr><th scope="row">{{ 'sd.invoice.original' | translate }} ({{ saved.currencyCode }})</th><td class="number">{{ money(saved.originalNet) }}</td>
            <td class="number">{{ money(saved.originalTax) }}</td><td class="number">{{ saved.currencyCode }} {{ money(saved.originalGross) }}</td></tr>
          <tr><th scope="row">{{ 'sd.invoice.base' | translate }} ({{ saved.baseCurrencyCode }})</th><td class="number">{{ money(saved.baseNet) }}</td>
            <td class="number">{{ money(saved.baseTax) }}</td><td class="number">{{ saved.baseCurrencyCode }} {{ money(saved.baseGross) }}</td></tr>
        </tbody></table></div>
        <h3>{{ 'sd.invoice.lines' | translate }}</h3>
        <div class="scroll"><table class="lines"><thead><tr><th>#</th><th>{{ 'sd.invoice.delivery' | translate }}</th>
          <th>{{ 'sd.invoice.orderLine' | translate }}</th><th>{{ 'sd.invoice.item' | translate }}</th>
          <th class="number">{{ 'sd.invoice.quantity' | translate }}</th><th class="number">{{ 'sd.invoice.taxRate' | translate }}</th>
          <th class="number">{{ 'sd.invoice.net' | translate }}</th><th class="number">{{ 'sd.invoice.tax' | translate }}</th>
          <th class="number">{{ 'sd.invoice.gross' | translate }} ({{ saved.currencyCode }})</th>
          <th class="number">{{ 'sd.invoice.gross' | translate }} ({{ saved.baseCurrencyCode }})</th></tr></thead><tbody>
          @for (line of saved.lines; track line.id) { <tr [attr.data-testid]="'sd-invoice-line-' + line.id"><td>{{ line.lineNo }}</td>
            <td translate="no">{{ line.deliveryDocumentNo }}</td><td>{{ line.orderLineNo }}</td><td>{{ line.itemCode }} · {{ line.itemName }}</td>
            <td class="number">{{ decimal(line.quantity) }}</td><td class="number">{{ decimal(line.taxRatePercent) }}%</td>
            <td class="number">{{ money(line.originalNet) }}</td><td class="number">{{ money(line.originalTax) }}</td>
            <td class="number">{{ money(line.originalGross) }}</td><td class="number">{{ money(line.baseGross) }}</td></tr> }
        </tbody></table></div>
      </section> }
    </section>`,
  styles: `.head{display:flex;justify-content:space-between;gap:12px;align-items:center;flex-wrap:wrap}.invoice-page{display:grid;gap:16px;color:var(--erp-color-text);min-width:0}h1,h2,h3{margin:0}.filters,.detail header{display:flex;gap:12px;flex-wrap:wrap;align-items:end}.filters label{display:grid;gap:4px}.filters input{min-height:32px;padding:4px;background:var(--erp-color-bg-container);color:var(--erp-color-text);border:1px solid var(--erp-color-border);border-radius:4px}.chip{margin:0;display:flex;gap:8px;align-items:center;flex-wrap:wrap}.scroll{overflow-x:auto;min-width:0}.scroll nz-table{display:block;min-width:860px}.detail{border:1px solid var(--erp-color-border);padding:16px;display:grid;gap:12px;min-width:0}.detail p,.detail td{overflow-wrap:break-word}.facts{display:grid;grid-template-columns:repeat(auto-fill,minmax(180px,1fr));gap:8px 16px;margin:0}.facts dt{color:var(--erp-color-text-secondary)}.facts dd{margin:0}.fi{display:grid;gap:8px}.detail table{width:100%;border-collapse:collapse}.detail table.lines{min-width:960px}.detail th,.detail td{padding:8px;border-bottom:1px solid var(--erp-color-border);text-align:left;vertical-align:top}.number{text-align:right!important;font-variant-numeric:tabular-nums}.hint{margin:0;color:var(--erp-color-text-secondary)}button:focus-visible,input:focus-visible{outline:2px solid var(--erp-color-primary);outline-offset:2px}`,
})
export class SalesInvoicePage {
  private readonly api = inject(SalesInvoiceService);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly company = inject(CompanyContextService);
  private readonly destroy = inject(DestroyRef);
  readonly pageSizeOptions = [...PAGE_SIZE_OPTIONS];
  readonly sortOrderOf = sortOrderOf;
  readonly rows = signal<SalesInvoiceSummary[]>([]);
  readonly detail = signal<SalesInvoiceDetail | null>(null);
  readonly loading = signal(false); readonly detailLoading = signal(false);
  readonly total = signal(0); readonly page = signal(1); readonly pageSize = signal(20);
  readonly sort = signal(DEFAULT_SORT); readonly filtered = signal(false);
  readonly validation = signal<string | null>(null); readonly error = signal<string | null>(null);
  readonly traceId = signal<string | null>(null); readonly detailError = signal<string | null>(null);
  readonly detailTraceId = signal<string | null>(null);
  /** Customer filter (customerId + display name); set from a list row or the URL, cleared on company switch. */
  readonly customer = signal<{ id: number; name: string } | null>(null);
  /** Source order filter accepted from the URL for links from an order; cleared by Reset or company switch. */
  readonly orderFilter = signal<number | undefined>(undefined);
  keyword = ''; from = ''; to = '';
  private applied: Partial<SalesInvoiceQuery> = {};
  private listEpoch = 0; private detailEpoch = 0; private selectedDetailId: number | null = null; private first = true;
  constructor() {
    const params = this.route.snapshot.queryParamMap;
    this.keyword = params.get('keyword') ?? '';
    const from = params.get('from') ?? '', to = params.get('to') ?? '';
    this.from = validDate(from) ? from : ''; this.to = validDate(to) ? to : '';
    const customerId = positiveId(params.get('customerId'));
    if (customerId) this.customer.set({ id: customerId, name: (params.get('customerName') ?? '').slice(0, 200) });
    this.orderFilter.set(positiveId(params.get('orderId')));
    const page = Number(params.get('page'));
    if (Number.isSafeInteger(page) && page > 0) this.page.set(page);
    const pageSize = Number(params.get('pageSize'));
    if (PAGE_SIZE_OPTIONS.some(value => value === pageSize)) this.pageSize.set(pageSize);
    const sort = params.get('sort') ?? '';
    if (SORTS.includes(sort.replace(/^-/, ''))) this.sort.set(sort);
    this.destroy.onDestroy(() => { this.listEpoch++; this.detailEpoch++; });
    effect(() => { this.company.current(); untracked(() => {
      this.listEpoch++; this.close(); this.rows.set([]); this.total.set(0);
      // Another company's customer or order ID means nothing here.
      if (!this.first) { this.page.set(1); this.customer.set(null); this.orderFilter.set(undefined); }
      this.first = false; this.search(false);
    }); });
  }
  statusKey(status: string): string { return `sd.invoice.${status.toLowerCase()}`; }
  /** Quantities and rates: digit grouping on the exact text, trailing zeros dropped, never rounded. */
  decimal(value: string): string { return formatDecimalText(String(value)); }
  money(value: string): string { return invoiceMoney(value); }
  time(value: string): string { return value.replace('T', ' ').slice(0, 16); }
  search(resetPage = true): void {
    if ((this.from && !validDate(this.from)) || (this.to && !validDate(this.to)) ||
        (this.from && this.to && this.from > this.to)) {
      this.validation.set('sd.invoice.invalidDates'); return;
    }
    this.validation.set(null);
    this.applied = { keyword: this.keyword.trim() || undefined, from: this.from || undefined, to: this.to || undefined,
      customerId: this.customer()?.id, orderId: this.orderFilter() };
    this.filtered.set(!!(this.applied.keyword || this.applied.from || this.applied.to || this.applied.customerId || this.applied.orderId));
    if (resetPage) this.page.set(1);
    this.updateUrl(); this.load();
  }
  reset(): void {
    this.keyword = ''; this.from = ''; this.to = ''; this.customer.set(null); this.orderFilter.set(undefined); this.search();
  }
  filterCustomer(row: SalesInvoiceSummary): void { this.customer.set({ id: row.customerId, name: row.customerName }); this.search(); }
  clearCustomer(): void { this.customer.set(null); this.search(); }
  onQuery(params: NzTableQueryParams): void {
    const next = nextTableState(params, { page: this.page(), pageSize: this.pageSize(), sort: this.sort() }, SORTS, DEFAULT_SORT);
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
        this.error.set(failure instanceof TimeoutError ? 'sd.invoice.timeout' : apiErrorKey(failure, []));
        this.traceId.set(apiErrorTraceId(failure)); this.loading.set(false); },
    });
  }
  open(id: number): void { this.fetch(id); }
  retryDetail(): void { if (this.selectedDetailId !== null) this.fetch(this.selectedDetailId); }
  private fetch(id: number): void {
    const epoch = ++this.detailEpoch;
    this.selectedDetailId = id;
    this.detail.set(null); this.detailError.set(null); this.detailTraceId.set(null); this.detailLoading.set(true);
    this.api.get(id).subscribe({ next: saved => { if (epoch !== this.detailEpoch) return;
      this.detail.set(saved); this.detailLoading.set(false); },
    error: failure => { if (epoch !== this.detailEpoch) return;
      this.detailError.set(failure instanceof TimeoutError ? 'sd.invoice.timeout' :
        apiErrorKey(failure, ['SAL.INVOICE.NOT_FOUND']));
      this.detailTraceId.set(apiErrorTraceId(failure)); this.detailLoading.set(false); } });
  }
  close(): void {
    this.detailEpoch++; this.selectedDetailId = null; this.detail.set(null); this.detailError.set(null); this.detailLoading.set(false);
  }
  private updateUrl(): void {
    const customer = this.customer();
    void this.router.navigate([], { relativeTo: this.route, replaceUrl: true, queryParams: {
      keyword: this.applied.keyword ?? null, from: this.applied.from ?? null, to: this.applied.to ?? null,
      customerId: customer?.id ?? null, customerName: customer?.name || null, orderId: this.applied.orderId ?? null,
      page: this.page() > 1 ? this.page() : null, pageSize: this.pageSize() !== 20 ? this.pageSize() : null,
      sort: this.sort() !== DEFAULT_SORT ? this.sort() : null,
    } });
  }
}
