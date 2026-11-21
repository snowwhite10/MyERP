import { ChangeDetectionStrategy, Component, DestroyRef, ElementRef, Injector, afterNextRender, effect, inject, signal, untracked } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { HasPermissionDirective } from '../../core/auth/has-permission.directive';
import { TranslatePipe } from '@ngx-translate/core';
import { TimeoutError } from 'rxjs';
import { NzAlertModule } from 'ng-zorro-antd/alert';
import { NzButtonModule } from 'ng-zorro-antd/button';
import { NzTableModule, NzTableQueryParams } from 'ng-zorro-antd/table';
import { NzTagModule } from 'ng-zorro-antd/tag';
import { PermissionService } from '../../core/auth/permission.service';
import { CompanyContextService } from '../../core/company/company-context.service';
import { apiErrorKey, apiErrorTraceId } from '../../core/http/api-error';
import { ApiErrorDisplay } from '../../shared/api-error-display';
import { ErpDialogService } from '../../shared/dialogs/dialog.service';
import { formatDecimalText } from '../../shared/decimal-text';
import { nextTableState, PAGE_SIZE_OPTIONS, sortOrderOf } from '../../shared/list-page/table-query';
import { deliveryErrorLineNos } from './delivery-draft-form';
import { SalesDeliveryDetail, SalesDeliveryQuery, SalesDeliveryService, SalesDeliveryStatus, SalesDeliverySummary,
  SalesDeliveryTracking, referenceLabel } from './delivery.service';

const STATUSES: SalesDeliveryStatus[] = ['Draft', 'Pending', 'Rejected', 'Posted'];
const SORTS = ['documentNo', 'businessDate'];
const DEFAULT_SORT = '-businessDate';
/** INV refusals at zero-step posting: the outbound stays Draft; the user checks warehouse/location/lot stock (ADR-150). */
const STOCK_CODES = ['INV.STOCK.INSUFFICIENT', 'INV.LOT.INSUFFICIENT', 'INV.LOT.NOT_FOUND', 'INV.SERIAL.NOT_AVAILABLE',
  'INV.SERIAL.NOT_FOUND', 'INV.TRACKING.REQUIRED', 'INV.TRACKING.MODE_MISMATCH'];
/** Codes a submit/withdraw/delete refusal is explained with; anything else is the generic message plus trace ID. */
export const DELIVERY_ACTION_ERROR_CODES = ['SAL.DELIVERY.INVALID_STATUS', 'SAL.DELIVERY.NOT_FOUND', 'SAL.DELIVERY.ORDER_NOT_APPROVED',
  'SAL.DELIVERY.ORDER_LINE_INVALID', 'SAL.DELIVERY.ORDER_BALANCE_EXCEEDED', 'SAL.DELIVERY.TRACKING_REQUIRED',
  'SAL.DELIVERY.TRACKING_MODE_MISMATCH', 'SAL.DELIVERY.SERIAL_DUPLICATE', 'MD.WAREHOUSE.NOT_FOUND', 'MD.WAREHOUSE.INACTIVE',
  'MD.STORAGE_LOCATION.NOT_FOUND', 'MD.STORAGE_LOCATION.INACTIVE', 'MD.ITEM.NOT_FOUND', 'MD.ITEM.INACTIVE',
  'COMMON.PERMISSION.DENIED', ...STOCK_CODES];
type DeliveryAction = 'submit' | 'withdraw' | 'delete';
function validDate(value: string): boolean {
  if (!/^\d{4}-\d{2}-\d{2}$/.test(value)) return false;
  const [year, month, day] = value.split('-').map(Number);
  return year >= 1 && month >= 1 && month <= 12 && day >= 1 &&
    day <= new Date(Date.UTC(year, month, 0)).getUTCDate();
}

/**
 * Sales Outbound View Interface 2.7 (REQ_F4112.3.1, REQ_F4112.3.4, REQ_F4112.3.6): saved deliveries, plus the detail's
 * submit (Draft), withdraw (Pending) and delete (Draft) actions, each by the loaded version (REQ_IO_A2.7.9–12, ADR-150).
 */
@Component({
  selector: 'erp-sales-delivery-page',
  imports: [FormsModule, RouterLink, HasPermissionDirective, TranslatePipe, NzAlertModule, NzButtonModule, NzTableModule, NzTagModule, ApiErrorDisplay],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <section class="delivery-page">
      <header class="head"><h1>{{ 'sd.delivery.title' | translate }}</h1>
        <a nz-button nzType="primary" routerLink="/sal/deliveries/new" *erpHasPermission="'SAL.DELIVERY.CREATE'" data-testid="sd-delivery-new">{{ 'sd.delivery.create' | translate }}</a></header>
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
      @if (actionNotice(); as notice) { <nz-alert nzType="success" nzShowIcon role="status" data-testid="sd-delivery-action-notice" [nzMessage]="notice.key | translate: notice.params" /> }
      @if (actionStale()) { <nz-alert nzType="warning" nzShowIcon role="status" data-testid="sd-delivery-action-stale" [nzMessage]="actionStale()! | translate" />
        <div class="actions"><button nz-button type="button" (click)="refreshDetail()" data-testid="sd-delivery-action-refresh">{{ 'sd.delivery.action.refresh' | translate }}</button></div> }
      @if (actionError()) { @if (actionLine(); as failed) { <p class="action-error" role="alert" data-testid="sd-delivery-action-error">{{ failed.lineNos.length === 1 ? ('sd.delivery.action.lineRejected' | translate: { number: failed.lineNos[0] }) : ('sd.delivery.action.linesRejected' | translate: { numbers: failed.lineNos.join(('sd.delivery.action.lineListSeparator' | translate)) }) }}</p> }
        @else { <erp-api-error-display data-testid="sd-delivery-action-error" [messageKey]="actionError()!" [traceId]="actionTraceId()" /> } }
      @if (actionUnchanged()) { <p class="hint" role="status" data-testid="sd-delivery-action-unchanged">{{ actionUnchanged()! | translate }}</p> }
      @if (stockHint()) { <p class="hint" role="status" data-testid="sd-delivery-action-stock-hint">{{ 'sd.delivery.action.stockHint' | translate }}</p> }
      @if (detail(); as saved) { <section class="detail" data-testid="sd-delivery-detail">
        <header><h2 translate="no">{{ saved.documentNo }}</h2>
          @if (saved.status === 'Draft' || saved.status === 'Rejected') { <a nz-button nzType="primary" [routerLink]="['/sal/deliveries', saved.id, 'edit']" *erpHasPermission="'SAL.DELIVERY.EDIT'" data-testid="sd-delivery-edit-link">{{ 'sd.delivery.edit' | translate }}</a> }
          @if (saved.status === 'Draft' && permissions.has('SAL.DELIVERY.SUBMIT')) { <button nz-button type="button" (click)="submit()" [nzLoading]="acting() === 'submit'" [disabled]="actionLocked()" data-testid="sd-delivery-submit">{{ 'sd.delivery.action.submit' | translate }}</button> }
          @if (saved.status === 'Pending' && permissions.has('SAL.DELIVERY.SUBMIT')) { <button nz-button type="button" (click)="withdraw()" [nzLoading]="acting() === 'withdraw'" [disabled]="actionLocked()" data-testid="sd-delivery-withdraw">{{ 'sd.delivery.action.withdraw' | translate }}</button> }
          @if (saved.status === 'Draft' && permissions.has('SAL.DELIVERY.DELETE')) { <button nz-button nzDanger type="button" (click)="remove()" [nzLoading]="acting() === 'delete'" [disabled]="actionLocked()" data-testid="sd-delivery-delete">{{ 'sd.delivery.action.delete' | translate }}</button> }
          <button nz-button type="button" (click)="close()" data-testid="sd-delivery-close">{{ 'sd.delivery.close' | translate }}</button></header>
        <p><strong>{{ 'sd.delivery.status' | translate }}:</strong> <nz-tag [nzColor]="statusColor(saved.status)" data-testid="sd-delivery-detail-status">{{ statusKey(saved.status) | translate }}</nz-tag></p>
        <nz-alert nzType="info" nzShowIcon [nzMessage]="stateKey(saved.status) | translate" data-testid="sd-delivery-state" />
        @if (saved.status === 'Rejected' && permissions.has('SAL.DELIVERY.SUBMIT')) { <p class="hint" role="status" data-testid="sd-delivery-rejected-hint">{{ 'sd.delivery.action.rejectedHint' | translate }}</p> }
        <p>{{ 'sd.delivery.order' | translate }}: {{ saved.orderDocumentNo }} · {{ 'sd.delivery.customer' | translate }}: {{ saved.customerCode }} · {{ saved.customerName }}</p>
        <p>{{ 'sd.delivery.date' | translate }}: {{ saved.businessDate }} · {{ 'sd.delivery.version' | translate }}: {{ saved.version }}
          @if (saved.postedAt) { · {{ 'sd.delivery.postedAt' | translate }}: <span data-testid="sd-delivery-posted-at">{{ time(saved.postedAt) }}</span> }</p>
        @if (saved.remark) { <p class="remark">{{ 'sd.delivery.remark' | translate }}: {{ saved.remark }}</p> }
        <h3>{{ 'sd.delivery.lines' | translate }}</h3>
        <div class="scroll"><table><thead><tr><th>#</th><th>{{ 'sd.delivery.orderLine' | translate }}</th>
          <th>{{ 'sd.delivery.item' | translate }}</th><th>{{ 'sd.delivery.warehouse' | translate }}</th>
          <th>{{ 'sd.delivery.location' | translate }}</th><th class="number">{{ 'sd.delivery.quantity' | translate }}</th>
          <th>{{ 'sd.delivery.tracking' | translate }}</th><th>{{ 'sd.delivery.invMovement' | translate }}</th></tr></thead><tbody>
          @for (line of saved.lines; track line.id) { <tr [class.failed]="failedLine(line.lineNo)" [attr.data-testid]="'sd-delivery-line-' + line.id"><td>{{ line.lineNo }}</td>
            <td>{{ line.orderLineNo }}</td><td>{{ line.itemCode }} · {{ line.itemName }}</td>
            <td [attr.data-testid]="'sd-delivery-warehouse-' + line.id">{{ referenceLabel(line.warehouseCode, line.warehouseName) || ('sd.delivery.deletedWarehouse' | translate: { id: line.warehouseId }) }}@if (line.warehouseActive === false) {<span class="inactive">{{ 'sd.delivery.inactiveReference' | translate }}</span>}</td>
            <td [attr.data-testid]="'sd-delivery-location-' + line.id">@if (line.locationId == null) { — } @else { {{ referenceLabel(line.locationCode, line.locationName) || ('sd.delivery.deletedLocation' | translate: { id: line.locationId }) }}@if (line.locationActive === false) {<span class="inactive">{{ 'sd.delivery.inactiveReference' | translate }}</span>} }</td>
            <td class="number">{{ decimal(line.quantity) }}</td>
            <td [attr.data-testid]="'sd-delivery-tracking-' + line.id">@for (pick of line.tracking; track pick.seqNo) {
              <span class="pick">{{ trackingKey(pick) | translate: { value: pick.lotNo ?? pick.serialNo } }} × {{ decimal(pick.quantity) }}</span>
            } @empty { {{ 'sd.delivery.noTracking' | translate }} }</td>
            <td [attr.data-testid]="'sd-delivery-movement-' + line.id">{{ line.invMovementId ?? ('sd.delivery.notPosted' | translate) }}</td></tr>
            @if (failedLine(line.lineNo)) { <tr class="failed"><td colspan="8" class="line-error" [attr.role]="actionLine()!.lineNos[0] === line.lineNo ? 'alert' : null" [attr.data-testid]="'sd-delivery-line-error-' + line.id">{{ actionLine()!.key | translate }}</td></tr> } }
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
  styles: `.head{display:flex;justify-content:space-between;gap:12px;align-items:center;flex-wrap:wrap}.delivery-page{display:grid;gap:16px;color:var(--erp-color-text);min-width:0}h1,h2,h3,h4{margin:0}.filters,.detail header{display:flex;gap:12px;flex-wrap:wrap;align-items:end}.filters label{display:grid;gap:4px}.filters input,.filters select{min-height:32px;padding:4px;background:var(--erp-color-bg-container);color:var(--erp-color-text);border:1px solid var(--erp-color-border);border-radius:4px}.scroll{overflow-x:auto;min-width:0}.scroll nz-table{display:block;min-width:760px}.detail{border:1px solid var(--erp-color-border);padding:16px;display:grid;gap:12px;min-width:0}.detail p,.detail td{overflow-wrap:break-word}.detail table{width:100%;min-width:860px;border-collapse:collapse}.detail th,.detail td{padding:8px;border-bottom:1px solid var(--erp-color-border);text-align:left;vertical-align:top}.pick{display:block}.inactive{color:var(--erp-color-text-secondary)}.history{display:grid;gap:8px}.round{border-top:1px solid var(--erp-color-border);padding-top:8px}.number{text-align:right!important;font-variant-numeric:tabular-nums}.hint{margin:0;color:var(--erp-color-text-secondary)}.actions{display:flex;gap:8px}.action-error,.line-error{margin:0;color:var(--erp-color-error-text)}.detail tr.failed td{border-bottom-color:var(--erp-color-error-text)}.detail tr.failed td:first-child{box-shadow:inset 3px 0 0 var(--erp-color-error-text)}button:focus-visible,input:focus-visible,select:focus-visible{outline:2px solid var(--erp-color-primary);outline-offset:2px}`,
})
export class SalesDeliveryPage {
  private readonly api = inject(SalesDeliveryService);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly company = inject(CompanyContextService);
  private readonly destroy = inject(DestroyRef);
  private readonly dialogs = inject(ErpDialogService);
  private readonly host: ElementRef<HTMLElement> = inject(ElementRef);
  private readonly injector = inject(Injector);
  readonly permissions = inject(PermissionService);
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
  /** Detail action outcome (ADR-150): success only from a matching newer reply; failures never clear the saved view. */
  readonly acting = signal<DeliveryAction | null>(null);
  readonly actionNotice = signal<{ key: string; params: Record<string, unknown> } | null>(null);
  readonly actionStale = signal<string | null>(null); readonly actionError = signal<string | null>(null);
  readonly actionTraceId = signal<string | null>(null); readonly actionLine = signal<{ lineNos: number[]; key: string } | null>(null);
  readonly actionUnchanged = signal<string | null>(null); readonly stockHint = signal(false);
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
  readonly referenceLabel = referenceLabel;
  /** Quantities: digit grouping on the server's exact text, trailing zeros dropped, never rounded. */
  decimal(value: string): string { return formatDecimalText(String(value)); }
  time(value: string): string { return value.replace('T', ' ').slice(0, 16); }
  search(resetPage = true): void {
    this.actionNotice.set(null);
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
  /** A user-chosen row: any earlier action outcome is cleared. */
  open(id: number): void { this.clearAction(); this.actionNotice.set(null); this.fetch(id); }
  /** Reload the saved delivery after a stale or uncertain action outcome. */
  refreshDetail(): void { if (this.selectedDetailId === null) return; this.clearAction(); this.fetch(this.selectedDetailId); }
  private fetch(id: number): void {
    const epoch = ++this.detailEpoch;
    this.selectedDetailId = id;
    this.detail.set(null); this.detailError.set(null); this.detailTraceId.set(null); this.detailLoading.set(true);
    this.api.get(id).subscribe({ next: saved => { if (epoch !== this.detailEpoch) return;
      this.detail.set(saved); this.detailLoading.set(false); this.syncRow(saved); },
    error: failure => { if (epoch !== this.detailEpoch) return;
      this.detailError.set(failure instanceof TimeoutError ? 'sd.delivery.timeout' :
        apiErrorKey(failure, ['SAL.DELIVERY.NOT_FOUND']));
      this.detailTraceId.set(apiErrorTraceId(failure)); this.detailLoading.set(false); } });
  }
  retryDetail(): void { if (this.selectedDetailId !== null) this.fetch(this.selectedDetailId); }
  close(): void { this.clearAction(); this.detailEpoch++; this.selectedDetailId = null; this.detail.set(null); this.detailError.set(null); this.detailLoading.set(false); }
  actionLocked(): boolean { return this.acting() !== null || !!this.actionStale() || this.detailLoading(); }
  submit(): void { this.command('submit'); }
  withdraw(): void { this.command('withdraw'); }
  /** Submit (Draft → Pending, or Posted when SYS has no step) or withdraw (Pending → Draft) after a confirmation. */
  private command(action: 'submit' | 'withdraw'): void {
    const saved = this.detail();
    if (!saved || saved.status !== (action === 'submit' ? 'Draft' : 'Pending') || this.actionLocked()) return;
    const epoch = this.detailEpoch, version = saved.version;
    this.dialogs.confirm({ titleKey: `sd.delivery.action.${action}Title`, contentKey: `sd.delivery.action.${action}Confirm`,
      params: { number: saved.documentNo }, okKey: `sd.delivery.action.${action}` }).subscribe(ok => {
      if (!ok || epoch !== this.detailEpoch || this.actionLocked()) return;
      this.clearAction(); this.actionNotice.set(null); this.acting.set(action);
      const request = action === 'submit' ? this.api.submit(saved.id, version) : this.api.withdraw(saved.id, version);
      request.subscribe({
        next: reply => { if (epoch !== this.detailEpoch) return; this.acting.set(null);
          const expected: SalesDeliveryStatus[] = action === 'submit' ? ['Pending', 'Posted'] : ['Draft'];
          // Success only when the reply is this delivery, a newer version and in a state the action can produce.
          if (reply?.id !== saved.id || reply.documentNo !== saved.documentNo || !(reply.version > version) || !expected.includes(reply.status)) {
            this.pause('sd.delivery.action.unknown'); return; }
          const key = action === 'withdraw' ? 'withdrawn' : reply.status === 'Posted' ? 'posted' : 'submitted';
          this.actionNotice.set({ key: `sd.delivery.action.${key}`, params: { number: reply.documentNo, version: reply.version } });
          this.syncRow(reply); this.fetch(saved.id);
        },
        error: failure => { if (epoch !== this.detailEpoch) return; this.acting.set(null); this.fail(failure, action); },
      });
    });
  }
  /** Delete a Draft (needs only SAL.DELIVERY.DELETE, not EDIT) after a danger confirmation; the list then reloads. */
  remove(): void {
    const saved = this.detail();
    if (!saved || saved.status !== 'Draft' || this.actionLocked()) return;
    const epoch = this.detailEpoch, version = saved.version;
    this.dialogs.confirm({ titleKey: 'sd.delivery.action.deleteTitle', contentKey: 'sd.delivery.action.deleteConfirm',
      params: { number: saved.documentNo }, okKey: 'sd.delivery.action.delete', danger: true }).subscribe(ok => {
      if (!ok || epoch !== this.detailEpoch || this.actionLocked()) return;
      this.clearAction(); this.actionNotice.set(null); this.acting.set('delete');
      this.api.remove(saved.id, version).subscribe({
        next: () => { if (epoch !== this.detailEpoch) return; this.acting.set(null); this.load();
          this.actionNotice.set({ key: 'sd.delivery.action.deleted', params: { number: saved.documentNo } }); },
        error: failure => { if (epoch !== this.detailEpoch) return; this.acting.set(null); this.fail(failure, 'delete'); },
      });
    });
  }
  /** 409/404/changed status/uncertain → pause actions until a refresh; other refusals name the cause (and line) and say nothing changed. */
  private fail(failure: unknown, action: DeliveryAction): void {
    const status = (failure as { status?: number })?.status;
    this.actionTraceId.set(apiErrorTraceId(failure));
    if (failure instanceof TimeoutError || status === 0 || (status != null && status >= 500)) {
      this.pause('sd.delivery.action.unknown');
      if (status != null && status >= 500) this.actionError.set('sd.delivery.action.serverError');
      return;
    }
    if (status === 409) { this.pause('sd.delivery.action.conflict'); return; }
    if (status === 404) { this.pause('sd.delivery.action.unavailable'); return; }
    const key = apiErrorKey(failure, DELIVERY_ACTION_ERROR_CODES);
    if (key === 'errors.SAL.DELIVERY.INVALID_STATUS') { this.pause('sd.delivery.action.statusChanged'); return; }
    // ADR-154: INV stock refusals name every delivery line of the short balance key (lineNos); SD errors name one.
    const saved = new Set(this.detail()?.lines.map(line => line.lineNo) ?? []);
    const lineNos = deliveryErrorLineNos(failure).filter(lineNo => saved.has(lineNo));
    if (lineNos.length > 0) this.actionLine.set({ lineNos, key });
    this.actionError.set(key);
    this.actionUnchanged.set(`sd.delivery.action.${action}Unchanged`);
    this.stockHint.set(action === 'submit' && STOCK_CODES.some(code => key === `errors.${code}`));
  }
  /** Whether the last refused action named this saved line. */
  failedLine(lineNo: number): boolean { return this.actionLine()?.lineNos.includes(lineNo) ?? false; }
  /** Actions pause until a refresh; the disabled button loses focus, so keyboard users land on Refresh. */
  private pause(key: string): void {
    this.actionStale.set(key);
    afterNextRender(() => this.host.nativeElement.querySelector<HTMLElement>('[data-testid="sd-delivery-action-refresh"]')?.focus(), { injector: this.injector });
  }
  private clearAction(): void {
    this.acting.set(null); this.actionStale.set(null); this.actionError.set(null); this.actionTraceId.set(null);
    this.actionLine.set(null); this.actionUnchanged.set(null); this.stockHint.set(false);
  }
  /** Keep the list row's status and version in step with what the server last returned for it. */
  private syncRow(saved: SalesDeliveryDetail): void {
    this.rows.update(rows => rows.map(row => row.id === saved.id ? { ...row, status: saved.status, version: saved.version } : row));
  }
  private updateUrl(): void {
    void this.router.navigate([], { relativeTo: this.route, replaceUrl: true, queryParams: {
      keyword: this.applied.keyword ?? null, from: this.applied.from ?? null, to: this.applied.to ?? null,
      status: this.applied.status ?? null, orderId: this.applied.orderId ?? null,
      page: this.page() > 1 ? this.page() : null, pageSize: this.pageSize() !== 20 ? this.pageSize() : null,
      sort: this.sort() !== DEFAULT_SORT ? this.sort() : null,
    } });
  }
}
