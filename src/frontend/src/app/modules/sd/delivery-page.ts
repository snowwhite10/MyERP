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
import { SalesDeliveryDetail, SalesDeliveryQuery, SalesDeliveryService, SalesDeliveryStatus, SalesDeliverySummary,
  SalesDeliveryTracking } from './delivery.service';

const STATUSES: SalesDeliveryStatus[] = ['Draft', 'Pending', 'Rejected', 'Posted'];
const SORTS = ['documentNo', 'businessDate'];
const DEFAULT_SORT = '-businessDate';
function validDate(value: string): boolean {
  if (!/^\d{4}-\d{2}-\d{2}$/.test(value)) return false;
  const [year, month, day] = value.split('-').map(Number);
  return year >= 1 && month >= 1 && month <= 12 && day >= 1 &&
    day <= new Date(Date.UTC(year, month, 0)).getUTCDate();
}

/** Sales Outbound View Interface 2.7 (REQ_F4112.3.1, REQ_F4112.3.4): saved deliveries only, no mutation. */
@Component({
  selector: 'erp-sales-delivery-page',
  imports: [FormsModule, TranslatePipe, NzAlertModule, NzButtonModule, NzTableModule, NzTagModule, ApiErrorDisplay],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <section class="delivery-page">
      <h1>{{ 'sd.delivery.title' | translate }}</h1>
      <form class="filters" (ngSubmit)="search()">
        <label>{{ 'sd.delivery.keyword' | translate }} <input name="keyword" autocomplete="off" maxlength="100" [(ngModel)]="keyword" data-testid="sd-delivery-keyword" /></label>
        <label>{{ 'sd.delivery.from' | translate }} <input type="date" name="from" [(ngModel)]="from" data-testid="sd-delivery-from" /></label>
        <label>{{ 'sd.delivery.to' | translate }} <input type="date" name="to" [(ngModel)]="to" data-testid="sd-delivery-to" /></label>
        <label>{{ 'sd.delivery.status' | translate }} <select name="status" [(ngModel)]="status" data-testid="sd-delivery-status">
          <option value="">{{ 'sd.delivery.all' | translate }}</option>
          @for (value of statuses; track value) { <option [value]="value">{{ statusKey(value) | translate }}</option> }
        </select></label>
        <button nz-button nzType="primary" type="submit" data-testid="sd-delivery-search">{{ 'sd.delivery.search' | translate }}</button>
        <button nz-button type="button" (click)="reset()" data-testid="sd-delivery-reset">{{ 'sd.delivery.reset' | translate }}</button>
      </form>
      @if (validation()) { <p role="alert" data-testid="sd-delivery-validation">{{ validation()! | translate }}</p> }
      @if (error()) { <erp-api-error-display [messageKey]="error()!" [traceId]="traceId()" data-testid="sd-delivery-error" />
        <button nz-button type="button" (click)="load()" data-testid="sd-delivery-retry">{{ 'sd.delivery.retry' | translate }}</button> }
      <div class="scroll"><nz-table #table [nzData]="rows()" [nzLoading]="loading()" [nzFrontPagination]="false"
        [nzTotal]="total()" [nzPageIndex]="page()" [nzPageSize]="pageSize()" nzShowSizeChanger
        [nzPageSizeOptions]="pageSizeOptions" (nzQueryParams)="onQuery($event)"
        [nzNoResult]="(error() ? 'sd.delivery.loadFailed' : filtered() ? 'sd.delivery.noResults' : 'sd.delivery.empty') | translate"
        data-testid="sd-delivery-table">
        <thead><tr><th nzColumnKey="documentNo" [nzSortFn]="true" [nzSortOrder]="sortOrderOf(sort(), 'documentNo')">{{ 'sd.delivery.number' | translate }}</th>
          <th>{{ 'sd.delivery.order' | translate }}</th><th>{{ 'sd.delivery.customer' | translate }}</th>
          <th nzColumnKey="businessDate" [nzSortFn]="true" [nzSortOrder]="sortOrderOf(sort(), 'businessDate')">{{ 'sd.delivery.date' | translate }}</th>
          <th>{{ 'sd.delivery.status' | translate }}</th><th class="number">{{ 'sd.delivery.lineCount' | translate }}</th>
          <th class="number">{{ 'sd.delivery.totalQuantity' | translate }}</th></tr></thead>
        <tbody>@for (row of table.data; track row.id) { <tr [attr.data-testid]="'sd-delivery-row-' + row.id">
          <td><button nz-button nzType="link" type="button" (click)="open(row.id)" [attr.data-testid]="'sd-delivery-open-' + row.id" translate="no">{{ row.documentNo }}</button></td>
          <td>{{ row.orderDocumentNo }}</td><td>{{ row.customerName }}</td><td>{{ row.businessDate }}</td>
          <td><nz-tag [nzColor]="statusColor(row.status)">{{ statusKey(row.status) | translate }}</nz-tag></td>
          <td class="number">{{ row.lineCount }}</td><td class="number">{{ decimal(row.totalQuantity) }}</td>
        </tr> }</tbody>
      </nz-table></div>
      @if (detailLoading()) { <p role="status">{{ 'sd.delivery.loadingDetail' | translate }}</p> }
      @if (detailError()) { <erp-api-error-display [messageKey]="detailError()!" [traceId]="detailTraceId()" data-testid="sd-delivery-detail-error" />
        <button nz-button type="button" (click)="retryDetail()" data-testid="sd-delivery-detail-retry">{{ 'sd.delivery.retry' | translate }}</button> }
      @if (detail(); as saved) { <section class="detail" data-testid="sd-delivery-detail">
        <header><h2 translate="no">{{ saved.documentNo }}</h2><button nz-button type="button" (click)="close()" data-testid="sd-delivery-close">{{ 'sd.delivery.close' | translate }}</button></header>
        <p><strong>{{ 'sd.delivery.status' | translate }}:</strong> <nz-tag [nzColor]="statusColor(saved.status)" data-testid="sd-delivery-detail-status">{{ statusKey(saved.status) | translate }}</nz-tag></p>
        <nz-alert nzType="info" nzShowIcon [nzMessage]="stateKey(saved.status) | translate" data-testid="sd-delivery-state" />
        <p>{{ 'sd.delivery.order' | translate }}: {{ saved.orderDocumentNo }} · {{ 'sd.delivery.customer' | translate }}: {{ saved.customerCode }} · {{ saved.customerName }}</p>
        <p>{{ 'sd.delivery.date' | translate }}: {{ saved.businessDate }} · {{ 'sd.delivery.version' | translate }}: {{ saved.version }}
          @if (saved.postedAt) { · {{ 'sd.delivery.postedAt' | translate }}: <span data-testid="sd-delivery-posted-at">{{ time(saved.postedAt) }}</span> }</p>
        @if (saved.remark) { <p class="remark">{{ 'sd.delivery.remark' | translate }}: {{ saved.remark }}</p> }
        <h3>{{ 'sd.delivery.lines' | translate }}</h3>
        <div class="scroll"><table><thead><tr><th>#</th><th>{{ 'sd.delivery.orderLine' | translate }}</th>
          <th>{{ 'sd.delivery.item' | translate }}</th><th>{{ 'sd.delivery.warehouse' | translate }}</th>
          <th>{{ 'sd.delivery.location' | translate }}</th><th class="number">{{ 'sd.delivery.quantity' | translate }}</th>
          <th>{{ 'sd.delivery.tracking' | translate }}</th><th>{{ 'sd.delivery.invMovement' | translate }}</th></tr></thead><tbody>
          @for (line of saved.lines; track line.id) { <tr [attr.data-testid]="'sd-delivery-line-' + line.id"><td>{{ line.lineNo }}</td>
            <td>{{ line.orderLineNo }}</td><td>{{ line.itemCode }} · {{ line.itemName }}</td>
            <td>{{ line.warehouseId }}</td><td>{{ line.locationId ?? '—' }}</td>
            <td class="number">{{ decimal(line.quantity) }}</td>
            <td [attr.data-testid]="'sd-delivery-tracking-' + line.id">@for (pick of line.tracking; track pick.seqNo) {
              <span class="pick">{{ trackingKey(pick) | translate: { value: pick.lotNo ?? pick.serialNo } }} × {{ decimal(pick.quantity) }}</span>
            } @empty { {{ 'sd.delivery.noTracking' | translate }} }</td>
            <td [attr.data-testid]="'sd-delivery-movement-' + line.id">{{ line.invMovementId ?? ('sd.delivery.notPosted' | translate) }}</td></tr> }
        </tbody></table></div>
        <section class="history" data-testid="sd-delivery-approval-history">
          <h3>{{ 'sd.delivery.history' | translate }}</h3>
          @for (round of saved.approvalHistory ?? []; track round.instanceId) { <section class="round">
            <h4>{{ 'sd.delivery.round' | translate }} {{ round.instanceId }} · {{ statusKey(round.status) | translate }}</h4>
            <p>{{ 'sd.delivery.submittedAt' | translate }}: {{ time(round.submittedAt) }}</p>
            @for (step of round.steps; track step.stepNo) { <p>{{ step.stepNo }}. {{ step.name }} · {{ statusKey(step.status) | translate }}
              @if (step.comment) { — {{ step.comment }} }</p> }
          </section> } @empty { <p>{{ 'sd.delivery.noHistory' | translate }}</p> }
        </section>
      </section> }
    </section>`,
  styles: `.delivery-page{display:grid;gap:16px;color:var(--erp-color-text);min-width:0}h1,h2,h3,h4{margin:0}.filters,.detail header{display:flex;gap:12px;flex-wrap:wrap;align-items:end}.filters label{display:grid;gap:4px}.filters input,.filters select{min-height:32px;padding:4px;background:var(--erp-color-bg-container);color:var(--erp-color-text);border:1px solid var(--erp-color-border);border-radius:4px}.scroll{overflow-x:auto;min-width:0}.scroll nz-table{display:block;min-width:760px}.detail{border:1px solid var(--erp-color-border);padding:16px;display:grid;gap:12px;min-width:0}.detail p,.detail td{overflow-wrap:break-word}.detail table{width:100%;min-width:860px;border-collapse:collapse}.detail th,.detail td{padding:8px;border-bottom:1px solid var(--erp-color-border);text-align:left;vertical-align:top}.pick{display:block}.history{display:grid;gap:8px}.round{border-top:1px solid var(--erp-color-border);padding-top:8px}.number{text-align:right!important;font-variant-numeric:tabular-nums}button:focus-visible,input:focus-visible,select:focus-visible{outline:2px solid var(--erp-color-primary);outline-offset:2px}`,
})
export class SalesDeliveryPage {
  private readonly api = inject(SalesDeliveryService);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly company = inject(CompanyContextService);
  private readonly destroy = inject(DestroyRef);
  readonly statuses = STATUSES;
  readonly pageSizeOptions = [...PAGE_SIZE_OPTIONS];
  readonly sortOrderOf = sortOrderOf;
  readonly rows = signal<SalesDeliverySummary[]>([]);
  readonly detail = signal<SalesDeliveryDetail | null>(null);
  readonly loading = signal(false); readonly detailLoading = signal(false);
  readonly total = signal(0); readonly page = signal(1); readonly pageSize = signal(20);
  readonly sort = signal(DEFAULT_SORT); readonly filtered = signal(false);
  readonly validation = signal<string | null>(null); readonly error = signal<string | null>(null);
  readonly traceId = signal<string | null>(null); readonly detailError = signal<string | null>(null);
  readonly detailTraceId = signal<string | null>(null);
  keyword = ''; from = ''; to = ''; status = '';
  private applied: Partial<SalesDeliveryQuery> = {};
  private listEpoch = 0; private detailEpoch = 0; private selectedDetailId: number | null = null; private first = true;
  constructor() {
    const params = this.route.snapshot.queryParamMap;
    this.keyword = params.get('keyword') ?? ''; this.from = params.get('from') ?? ''; this.to = params.get('to') ?? '';
    const state = params.get('status') ?? '';
    this.status = STATUSES.includes(state as SalesDeliveryStatus) ? state : '';
    const orderId = Number(params.get('orderId'));
    if (Number.isSafeInteger(orderId) && orderId > 0) this.applied.orderId = orderId;
    const page = Number(params.get('page'));
    if (Number.isSafeInteger(page) && page > 0) this.page.set(page);
    const pageSize = Number(params.get('pageSize'));
    if (PAGE_SIZE_OPTIONS.some(value => value === pageSize)) this.pageSize.set(pageSize);
    const sort = params.get('sort') ?? '';
    if (SORTS.includes(sort.replace(/^-/, ''))) this.sort.set(sort);
    this.destroy.onDestroy(() => { this.listEpoch++; this.detailEpoch++; });
    effect(() => { this.company.current(); untracked(() => {
      this.listEpoch++; this.detailEpoch++; this.close(); this.rows.set([]); this.total.set(0);
      if (!this.first) { this.page.set(1); this.applied.orderId = undefined; }
      this.first = false; this.search(false);
    }); });
  }
  statusKey(status: string): string { return `sd.delivery.${status.toLowerCase()}`; }
  stateKey(status: SalesDeliveryStatus): string { return `sd.delivery.state${status}`; }
  statusColor(status: SalesDeliveryStatus): string {
    return status === 'Posted' ? 'green' : status === 'Pending' ? 'blue' : status === 'Rejected' ? 'red' : 'default';
  }
  trackingKey(pick: SalesDeliveryTracking): string { return pick.serialNo != null ? 'sd.delivery.serial' : 'sd.delivery.lot'; }
  /** Quantities: digit grouping on the server's exact text, trailing zeros dropped, never rounded. */
  decimal(value: string): string { return formatDecimalText(String(value)); }
  time(value: string): string { return value.replace('T', ' ').slice(0, 16); }
  search(resetPage = true): void {
    if ((this.from && !validDate(this.from)) || (this.to && !validDate(this.to)) ||
        (this.from && this.to && this.from > this.to)) {
      this.validation.set('sd.delivery.invalidDates'); return;
    }
    this.validation.set(null);
    this.applied = { keyword: this.keyword.trim() || undefined, from: this.from || undefined, to: this.to || undefined,
      status: this.status as SalesDeliveryStatus || undefined, orderId: this.applied.orderId };
    this.filtered.set(!!(this.applied.keyword || this.applied.from || this.applied.to || this.applied.status || this.applied.orderId));
    if (resetPage) this.page.set(1);
    this.updateUrl(); this.load();
  }
  reset(): void { this.keyword = ''; this.from = ''; this.to = ''; this.status = ''; this.applied.orderId = undefined; this.search(); }
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
        this.error.set(failure instanceof TimeoutError ? 'sd.delivery.timeout' : apiErrorKey(failure, []));
        this.traceId.set(apiErrorTraceId(failure)); this.loading.set(false); },
    });
  }
  open(id: number): void {
    const epoch = ++this.detailEpoch;
    this.selectedDetailId = id;
    this.detail.set(null); this.detailError.set(null); this.detailTraceId.set(null); this.detailLoading.set(true);
    this.api.get(id).subscribe({ next: saved => { if (epoch !== this.detailEpoch) return;
      this.detail.set(saved); this.detailLoading.set(false); },
    error: failure => { if (epoch !== this.detailEpoch) return;
      this.detailError.set(failure instanceof TimeoutError ? 'sd.delivery.timeout' :
        apiErrorKey(failure, ['SAL.DELIVERY.NOT_FOUND']));
      this.detailTraceId.set(apiErrorTraceId(failure)); this.detailLoading.set(false); } });
  }
  retryDetail(): void { if (this.selectedDetailId !== null) this.open(this.selectedDetailId); }
  close(): void { this.detailEpoch++; this.selectedDetailId = null; this.detail.set(null); this.detailError.set(null); this.detailLoading.set(false); }
  private updateUrl(): void {
    void this.router.navigate([], { relativeTo: this.route, replaceUrl: true, queryParams: {
      keyword: this.applied.keyword ?? null, from: this.applied.from ?? null, to: this.applied.to ?? null,
      status: this.applied.status ?? null, orderId: this.applied.orderId ?? null,
      page: this.page() > 1 ? this.page() : null, pageSize: this.pageSize() !== 20 ? this.pageSize() : null,
      sort: this.sort() !== DEFAULT_SORT ? this.sort() : null,
    } });
  }
}
