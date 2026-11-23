import { ChangeDetectionStrategy, Component, DestroyRef, ElementRef, Injector, afterNextRender, effect, inject, signal,
  untracked } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router } from '@angular/router';
import { TranslatePipe } from '@ngx-translate/core';
import { TimeoutError } from 'rxjs';
import { NzAlertModule } from 'ng-zorro-antd/alert';
import { NzButtonModule } from 'ng-zorro-antd/button';
import { NzTableModule, NzTableQueryParams } from 'ng-zorro-antd/table';
import { CompanyContextService } from '../../core/company/company-context.service';
import { apiErrorKey, apiErrorTraceId } from '../../core/http/api-error';
import { ApiErrorDisplay } from '../../shared/api-error-display';
import { formatDecimalText } from '../../shared/decimal-text';
import { nextTableState, PAGE_SIZE_OPTIONS, sortOrderOf } from '../../shared/list-page/table-query';
import { DocumentStatus, StatusTag } from '../../shared/status-tag/status-tag';
import { referenceLabel } from './delivery.service';
import { invoiceMoney } from './invoice.service';
import { SalesReturnDetail, SalesReturnQuery, SalesReturnService, SalesReturnStatus, SalesReturnSummary } from './return.service';

const SORTS = ['documentNo', 'businessDate'];
const DEFAULT_SORT = '-businessDate';
export const RETURN_STATUSES: readonly SalesReturnStatus[] = ['Draft', 'Pending', 'Rejected', 'Posted'];
/** Shared status colours (ui-conventions 4.1): Pending waits for approval like Submitted. */
const TAG_STATUS: Record<SalesReturnStatus, DocumentStatus> = {
  Draft: 'Draft', Pending: 'Submitted', Rejected: 'Rejected', Posted: 'Posted',
};
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
 * Sales Return View Interface 2.16 (REQ_F4112.7.1, WU-SD-008b2): returns in the SAL.RETURN.VIEW scope in every stored
 * state and a read-only saved detail with lines, invoice allocations, INV inbound movements and the FI credit note
 * (REQ_IO_A2.16.1–9). Create / edit / submit / delete are Interface 2.17 (WU-SD-008b3); `?open=<id>` opens a detail.
 */
@Component({
  selector: 'erp-sales-return-page',
  imports: [FormsModule, TranslatePipe, NzAlertModule, NzButtonModule, NzTableModule, ApiErrorDisplay, StatusTag],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <section class="return-page">
      <header class="head"><h1>{{ 'sd.return.title' | translate }}</h1></header>
      <nz-alert nzType="info" nzShowIcon [nzMessage]="'sd.return.notice' | translate" data-testid="sd-return-notice" />
      <form class="filters" (ngSubmit)="search()">
        <label>{{ 'sd.return.keyword' | translate }} <input name="keyword" autocomplete="off" maxlength="100" [(ngModel)]="keyword" data-testid="sd-return-keyword" /></label>
        <label>{{ 'sd.return.from' | translate }} <input type="date" name="from" autocomplete="off" [(ngModel)]="from" [attr.aria-invalid]="validation() ? 'true' : null" data-testid="sd-return-from" /></label>
        <label>{{ 'sd.return.to' | translate }} <input type="date" name="to" autocomplete="off" [(ngModel)]="to" [attr.aria-invalid]="validation() ? 'true' : null" data-testid="sd-return-to" /></label>
        <label>{{ 'sd.return.status' | translate }} <select name="status" [(ngModel)]="status" data-testid="sd-return-status">
          <option value="">{{ 'sd.return.all' | translate }}</option>
          @for (value of statuses; track value) { <option [value]="value">{{ statusKey(value) | translate }}</option> }
        </select></label>
        <button nz-button nzType="primary" type="submit" data-testid="sd-return-search">{{ 'sd.return.search' | translate }}</button>
        <button nz-button type="button" (click)="reset()" data-testid="sd-return-reset">{{ 'sd.return.reset' | translate }}</button>
      </form>
      @if (customer(); as chosen) { <p class="chip" data-testid="sd-return-customer-filter">{{ 'sd.return.customerFilter' | translate: { name: chosen.name || ('#' + chosen.id) } }}
        <button nz-button nzType="link" type="button" (click)="clearCustomer()" data-testid="sd-return-customer-clear">{{ 'sd.return.clearCustomer' | translate }}</button></p> }
      @if (orderFilter(); as chosen) { <p class="chip" data-testid="sd-return-order-filter">{{ 'sd.return.orderFilter' | translate: { number: chosen.number || ('#' + chosen.id) } }}
        <button nz-button nzType="link" type="button" (click)="clearOrder()" data-testid="sd-return-order-clear">{{ 'sd.return.clearOrder' | translate }}</button></p> }
      @if (validation()) { <p role="alert" data-testid="sd-return-validation">{{ validation()! | translate }}</p> }
      @if (error()) { <erp-api-error-display [messageKey]="error()!" [traceId]="traceId()" data-testid="sd-return-error" />
        <button nz-button type="button" (click)="load()" data-testid="sd-return-retry">{{ 'sd.return.retry' | translate }}</button> }
      <div class="scroll"><nz-table #table [nzData]="rows()" [nzLoading]="loading()" [nzFrontPagination]="false"
        [nzTotal]="total()" [nzPageIndex]="page()" [nzPageSize]="pageSize()" nzShowSizeChanger
        [nzPageSizeOptions]="pageSizeOptions" (nzQueryParams)="onQuery($event)"
        [nzNoResult]="(error() ? 'sd.return.loadFailed' : filtered() ? 'sd.return.noResults' : 'sd.return.empty') | translate"
        data-testid="sd-return-table">
        <thead><tr><th nzColumnKey="documentNo" [nzSortFn]="true" [nzSortOrder]="sortOrderOf(sort(), 'documentNo')">{{ 'sd.return.number' | translate }}</th>
          <th>{{ 'sd.return.order' | translate }}</th><th>{{ 'sd.return.customer' | translate }}</th>
          <th nzColumnKey="businessDate" [nzSortFn]="true" [nzSortOrder]="sortOrderOf(sort(), 'businessDate')">{{ 'sd.return.date' | translate }}</th>
          <th>{{ 'sd.return.status' | translate }}</th>
          <th class="number">{{ 'sd.return.amount' | translate }}</th><th class="number">{{ 'sd.return.baseAmount' | translate }}</th>
          <th class="number">{{ 'sd.return.lineCount' | translate }}</th></tr></thead>
        <tbody>@for (row of table.data; track row.id) { <tr [attr.data-testid]="'sd-return-row-' + row.id">
          <td><button nz-button nzType="link" type="button" (click)="open(row.id)" [attr.data-testid]="'sd-return-open-' + row.id" translate="no">{{ row.documentNo }}</button></td>
          <td><button nz-button nzType="link" type="button" (click)="filterOrder(row)" [attr.aria-label]="'sd.return.filterOrder' | translate: { number: row.orderDocumentNo }"
            [attr.data-testid]="'sd-return-filter-order-' + row.id" translate="no">{{ row.orderDocumentNo }}</button></td>
          <td><button nz-button nzType="link" type="button" (click)="filterCustomer(row)" [attr.aria-label]="'sd.return.filterCustomer' | translate: { name: row.customerName }"
            [attr.data-testid]="'sd-return-filter-customer-' + row.id">{{ row.customerName }}</button></td>
          <td class="nowrap">{{ row.businessDate }}</td>
          <td><erp-status-tag [status]="tagStatus(row.status)" [labelKey]="statusKey(row.status)" [testId]="'sd-return-status-' + row.id" /></td>
          <td class="number nowrap">{{ row.currencyCode }} {{ money(row.originalGross) }}</td>
          <td class="number nowrap">{{ row.baseCurrencyCode }} {{ money(row.baseGross) }}</td>
          <td class="number">{{ row.lineCount }}</td>
        </tr> }</tbody>
      </nz-table></div>
      @if (detailLoading()) { <p role="status">{{ 'sd.return.loadingDetail' | translate }}</p> }
      @if (detailError()) { <erp-api-error-display [messageKey]="detailError()!" [traceId]="detailTraceId()" data-testid="sd-return-detail-error" />
        <button nz-button type="button" (click)="retryDetail()" data-testid="sd-return-detail-retry">{{ 'sd.return.retry' | translate }}</button> }
      @if (detail(); as saved) { <section class="detail" data-testid="sd-return-detail">
        <header><h2 translate="no" tabindex="-1" data-testid="sd-return-detail-title">{{ saved.documentNo }}</h2>
          <erp-status-tag [status]="tagStatus(saved.status)" [labelKey]="statusKey(saved.status)" testId="sd-return-detail-status" />
          <button nz-button type="button" (click)="close()" data-testid="sd-return-close">{{ 'sd.return.close' | translate }}</button></header>
        <nz-alert [nzType]="saved.status === 'Rejected' ? 'warning' : 'info'" nzShowIcon [nzMessage]="stateKey(saved.status) | translate" data-testid="sd-return-state" />
        <dl class="facts">
          <div><dt>{{ 'sd.return.order' | translate }}</dt><dd translate="no" data-testid="sd-return-detail-order">{{ saved.orderDocumentNo }}</dd></div>
          <div><dt>{{ 'sd.return.customer' | translate }}</dt><dd data-testid="sd-return-detail-customer">{{ saved.customerCode }} · {{ saved.customerName }}</dd></div>
          <div><dt>{{ 'sd.return.date' | translate }}</dt><dd>{{ saved.businessDate }}</dd></div>
          <div><dt>{{ 'sd.return.currency' | translate }}</dt><dd>{{ saved.currencyCode }} → {{ saved.baseCurrencyCode }}</dd></div>
          <div><dt>{{ 'sd.return.exchangeRate' | translate }}</dt><dd data-testid="sd-return-detail-rate">{{ decimal(saved.exchangeRate) }}</dd></div>
          <div><dt>{{ 'sd.return.postedAt' | translate }}</dt><dd data-testid="sd-return-detail-posted-at">{{ saved.postedAt ? time(saved.postedAt) : ('sd.return.notPosted' | translate) }}</dd></div>
          <div><dt>{{ 'sd.return.version' | translate }}</dt><dd>{{ saved.version }}</dd></div>
          <div class="wide"><dt>{{ 'sd.return.remark' | translate }}</dt><dd data-testid="sd-return-detail-remark">{{ saved.remark || ('sd.return.noRemark' | translate) }}</dd></div>
        </dl>
        <section class="effects" data-testid="sd-return-effects">
          <h3>{{ 'sd.return.effectsTitle' | translate }}</h3>
          <p class="hint">{{ (saved.status === 'Posted' ? 'sd.return.effectsPosted' : 'sd.return.effectsPending') | translate }}</p>
          <dl class="facts">
            <div><dt>{{ 'sd.return.fiCreditNote' | translate }}</dt><dd data-testid="sd-return-fi-credit">{{ saved.fiCreditNoteId ?? ((billed(saved) ? 'sd.return.notRecorded' : 'sd.return.noCreditNeeded') | translate) }}</dd></div>
          </dl>
        </section>
        <div class="scroll"><table class="totals" data-testid="sd-return-totals"><thead><tr><th></th>
          <th class="number">{{ 'sd.return.net' | translate }}</th><th class="number">{{ 'sd.return.tax' | translate }}</th>
          <th class="number">{{ 'sd.return.gross' | translate }}</th></tr></thead><tbody>
          <tr><th scope="row">{{ 'sd.return.original' | translate }} ({{ saved.currencyCode }})</th><td class="number">{{ money(saved.originalNet) }}</td>
            <td class="number">{{ money(saved.originalTax) }}</td><td class="number">{{ saved.currencyCode }} {{ money(saved.originalGross) }}</td></tr>
          <tr><th scope="row">{{ 'sd.return.base' | translate }} ({{ saved.baseCurrencyCode }})</th><td class="number">{{ money(saved.baseNet) }}</td>
            <td class="number">{{ money(saved.baseTax) }}</td><td class="number">{{ saved.baseCurrencyCode }} {{ money(saved.baseGross) }}</td></tr>
        </tbody></table></div>
        <h3>{{ 'sd.return.lines' | translate }}</h3>
        <div class="scroll"><table class="lines"><thead><tr><th>#</th><th>{{ 'sd.return.delivery' | translate }}</th>
          <th>{{ 'sd.return.orderLine' | translate }}</th><th>{{ 'sd.return.item' | translate }}</th>
          <th>{{ 'sd.return.destination' | translate }}</th>
          <th class="number">{{ 'sd.return.quantity' | translate }}</th><th class="number">{{ 'sd.return.unbilled' | translate }}</th>
          <th class="number">{{ 'sd.return.billed' | translate }}</th>
          <th class="number">{{ 'sd.return.gross' | translate }} ({{ saved.currencyCode }})</th>
          <th class="number">{{ 'sd.return.gross' | translate }} ({{ saved.baseCurrencyCode }})</th>
          <th>{{ 'sd.return.invMovement' | translate }}</th></tr></thead><tbody>
          @for (line of saved.lines; track line.id) { <tr [attr.data-testid]="'sd-return-line-' + line.id"><td>{{ line.lineNo }}</td>
            <td translate="no" class="nowrap">{{ line.deliveryDocumentNo }}</td><td>{{ line.orderLineNo }}</td><td>{{ line.itemCode }} · {{ line.itemName }}</td>
            <td [attr.data-testid]="'sd-return-destination-' + line.id"><span class="pick">{{ referenceLabel(line.warehouseCode, line.warehouseName) || ('sd.return.deletedWarehouse' | translate: { id: line.warehouseId }) }}@if (line.warehouseActive === false) {<span class="inactive">{{ 'sd.return.inactiveReference' | translate }}</span>}</span>
              @if (line.locationId != null) { <span class="pick">{{ referenceLabel(line.locationCode, line.locationName) || ('sd.return.deletedLocation' | translate: { id: line.locationId }) }}@if (line.locationActive === false) {<span class="inactive">{{ 'sd.return.inactiveReference' | translate }}</span>}</span> }</td>
            <td class="number">{{ decimal(line.quantity) }}</td><td class="number">{{ decimal(line.unbilledQuantity) }}</td>
            <td class="number" [attr.data-testid]="'sd-return-billed-' + line.id">{{ decimal(line.billedQuantity) }}
              @for (portion of line.allocations; track portion.id) { <span class="pick allocation" [attr.data-testid]="'sd-return-allocation-' + portion.id">{{ 'sd.return.allocation' | translate: { invoice: portion.invoiceDocumentNo, quantity: decimal(portion.quantity), amount: money(portion.originalGross) } }}</span> }</td>
            <td class="number nowrap">{{ money(line.originalGross) }}</td><td class="number nowrap">{{ money(line.baseGross) }}</td>
            <td [attr.data-testid]="'sd-return-inv-' + line.id">{{ line.invMovementId ?? ('sd.return.notReceived' | translate) }}</td></tr> }
        </tbody></table></div>
      </section> }
    </section>`,
  styles: `.head{display:flex;justify-content:space-between;gap:12px;align-items:center;flex-wrap:wrap}.return-page{display:grid;gap:16px;color:var(--erp-color-text);min-width:0}h1,h2,h3{margin:0}.filters,.detail header{display:flex;gap:12px;flex-wrap:wrap;align-items:end}.detail header{align-items:center}.filters label{display:grid;gap:4px}.filters input[aria-invalid=true]{border-color:var(--erp-color-error-text)!important}.filters input,.filters select{min-height:32px;padding:4px;background:var(--erp-color-bg-container);color:var(--erp-color-text);border:1px solid var(--erp-color-border);border-radius:4px}.chip{margin:0;display:flex;gap:8px;align-items:center;flex-wrap:wrap}.scroll{overflow-x:auto;min-width:0}.scroll nz-table{display:block;min-width:900px}.detail{border:1px solid var(--erp-color-border);padding:16px;display:grid;gap:12px;min-width:0}.detail p,.detail td{overflow-wrap:break-word}.facts{display:grid;grid-template-columns:repeat(auto-fill,minmax(180px,1fr));gap:8px 16px;margin:0}.facts .wide{grid-column:1/-1}.facts dt{color:var(--erp-color-text-secondary)}.facts dd{margin:0;white-space:pre-wrap;overflow-wrap:anywhere}.effects{display:grid;gap:8px}.detail table{width:100%;border-collapse:collapse}.detail table.lines{min-width:1080px}.detail th,.detail td{padding:8px;border-bottom:1px solid var(--erp-color-border);text-align:left;vertical-align:top}.detail thead th{white-space:nowrap}.pick{display:block}.allocation{color:var(--erp-color-text-secondary);white-space:nowrap}.inactive{color:var(--erp-color-text-secondary)}.nowrap{white-space:nowrap}.number{text-align:right!important;font-variant-numeric:tabular-nums}.hint{margin:0;color:var(--erp-color-text-secondary)}p[role=alert]{margin:0;color:var(--erp-color-error-text)}button:focus-visible,input:focus-visible,select:focus-visible{outline:2px solid var(--erp-color-primary);outline-offset:2px}`,
})
export class SalesReturnPage {
  private readonly api = inject(SalesReturnService);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly company = inject(CompanyContextService);
  private readonly destroy = inject(DestroyRef);
  private readonly host: ElementRef<HTMLElement> = inject(ElementRef);
  private readonly injector = inject(Injector);
  readonly statuses = RETURN_STATUSES;
  readonly pageSizeOptions = [...PAGE_SIZE_OPTIONS];
  readonly sortOrderOf = sortOrderOf;
  readonly referenceLabel = referenceLabel;
  readonly rows = signal<SalesReturnSummary[]>([]);
  readonly detail = signal<SalesReturnDetail | null>(null);
  readonly loading = signal(false); readonly detailLoading = signal(false);
  readonly total = signal(0); readonly page = signal(1); readonly pageSize = signal(20);
  readonly sort = signal(DEFAULT_SORT); readonly filtered = signal(false);
  readonly validation = signal<string | null>(null); readonly error = signal<string | null>(null);
  readonly traceId = signal<string | null>(null); readonly detailError = signal<string | null>(null);
  readonly detailTraceId = signal<string | null>(null);
  /** Customer filter (customerId + display name); from a list row or the URL, cleared on company switch. */
  readonly customer = signal<{ id: number; name: string } | null>(null);
  /** Source order filter (orderId + number); from a list row or the URL (links from an order), cleared on company switch. */
  readonly orderFilter = signal<{ id: number; number: string } | null>(null);
  keyword = ''; from = ''; to = ''; status: SalesReturnStatus | '' = '';
  private applied: Partial<SalesReturnQuery> = {};
  private listEpoch = 0; private detailEpoch = 0; private selectedDetailId: number | null = null; private first = true;
  private readonly openOnLoad: number | undefined;

  constructor() {
    const params = this.route.snapshot.queryParamMap;
    this.keyword = params.get('keyword') ?? '';
    const from = params.get('from') ?? '', to = params.get('to') ?? '';
    this.from = validDate(from) ? from : ''; this.to = validDate(to) ? to : '';
    const status = params.get('status') ?? '';
    this.status = RETURN_STATUSES.find(value => value === status) ?? '';
    const customerId = positiveId(params.get('customerId'));
    if (customerId) this.customer.set({ id: customerId, name: (params.get('customerName') ?? '').slice(0, 200) });
    const orderId = positiveId(params.get('orderId'));
    if (orderId) this.orderFilter.set({ id: orderId, number: (params.get('orderNo') ?? '').slice(0, 50) });
    this.openOnLoad = positiveId(params.get('open'));
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
      if (!this.first) { this.page.set(1); this.customer.set(null); this.orderFilter.set(null); }
      const opening = this.first ? this.openOnLoad : undefined;
      this.first = false; this.search(false);
      // `?open=<id>` (after saving on 2.17, WU-SD-008b3) opens that return's detail once, alongside the first list read.
      if (opening) this.fetch(opening);
    }); });
  }

  tagStatus(status: SalesReturnStatus): DocumentStatus { return TAG_STATUS[status] ?? 'Draft'; }
  statusKey(status: SalesReturnStatus): string { return `sd.return.state.${status.toLowerCase()}`; }
  stateKey(status: SalesReturnStatus): string { return `sd.return.stateHint.${status.toLowerCase()}`; }
  /** True when part of the return credits invoiced quantity, so FI must issue a credit note on posting. */
  billed(saved: SalesReturnDetail): boolean { return saved.lines.some(line => line.allocations.length > 0); }
  /** Quantities and rates: digit grouping on the exact text, trailing zeros dropped, never rounded. */
  decimal(value: string): string { return formatDecimalText(String(value)); }
  money(value: string): string { return invoiceMoney(value); }
  time(value: string): string { return value.replace('T', ' ').slice(0, 16); }

  search(resetPage = true): void {
    if ((this.from && !validDate(this.from)) || (this.to && !validDate(this.to)) ||
        (this.from && this.to && this.from > this.to)) {
      this.validation.set('sd.return.invalidDates'); return;
    }
    this.validation.set(null);
    this.applied = { keyword: this.keyword.trim() || undefined, from: this.from || undefined, to: this.to || undefined,
      status: this.status || undefined, customerId: this.customer()?.id, orderId: this.orderFilter()?.id };
    this.filtered.set(!!(this.applied.keyword || this.applied.from || this.applied.to || this.applied.status ||
      this.applied.customerId || this.applied.orderId));
    if (resetPage) this.page.set(1);
    this.updateUrl(); this.load();
  }
  reset(): void {
    this.keyword = ''; this.from = ''; this.to = ''; this.status = '';
    this.customer.set(null); this.orderFilter.set(null); this.search();
  }
  filterCustomer(row: SalesReturnSummary): void { this.customer.set({ id: row.customerId, name: row.customerName }); this.search(); }
  clearCustomer(): void { this.customer.set(null); this.search(); }
  filterOrder(row: SalesReturnSummary): void { this.orderFilter.set({ id: row.orderId, number: row.orderDocumentNo }); this.search(); }
  clearOrder(): void { this.orderFilter.set(null); this.search(); }
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
        this.error.set(failure instanceof TimeoutError ? 'sd.return.timeout' : apiErrorKey(failure, []));
        this.traceId.set(apiErrorTraceId(failure)); this.loading.set(false); },
    });
  }
  open(id: number): void { this.fetch(id); }
  retryDetail(): void { if (this.selectedDetailId !== null) this.fetch(this.selectedDetailId); }
  close(): void {
    this.detailEpoch++; this.selectedDetailId = null; this.detail.set(null); this.detailError.set(null); this.detailLoading.set(false);
  }

  private fetch(id: number): void {
    const epoch = ++this.detailEpoch;
    this.selectedDetailId = id;
    this.detail.set(null); this.detailError.set(null); this.detailTraceId.set(null); this.detailLoading.set(true);
    this.api.get(id).subscribe({ next: saved => { if (epoch !== this.detailEpoch) return;
      this.detail.set(saved); this.detailLoading.set(false);
      // Move keyboard and screen-reader focus to the opened detail.
      afterNextRender(() => this.host.nativeElement.querySelector<HTMLElement>('[data-testid="sd-return-detail-title"]')?.focus(),
        { injector: this.injector }); },
    error: failure => { if (epoch !== this.detailEpoch) return;
      this.detailError.set(failure instanceof TimeoutError ? 'sd.return.timeout' :
        apiErrorKey(failure, ['SAL.RETURN.NOT_FOUND']));
      this.detailTraceId.set(apiErrorTraceId(failure)); this.detailLoading.set(false); } });
  }
  private updateUrl(): void {
    const customer = this.customer(), order = this.orderFilter();
    void this.router.navigate([], { relativeTo: this.route, replaceUrl: true, queryParams: {
      keyword: this.applied.keyword ?? null, from: this.applied.from ?? null, to: this.applied.to ?? null,
      status: this.applied.status ?? null, customerId: customer?.id ?? null, customerName: customer?.name || null,
      orderId: order?.id ?? null, orderNo: order?.number || null,
      page: this.page() > 1 ? this.page() : null, pageSize: this.pageSize() !== 20 ? this.pageSize() : null,
      sort: this.sort() !== DEFAULT_SORT ? this.sort() : null,
    } });
  }
}
