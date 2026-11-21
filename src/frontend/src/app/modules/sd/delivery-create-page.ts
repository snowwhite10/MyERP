import { ChangeDetectionStrategy, Component, DestroyRef, ElementRef, HostListener, effect, inject, signal, untracked } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import { TimeoutError } from 'rxjs';
import { NzAlertModule } from 'ng-zorro-antd/alert';
import { NzButtonModule } from 'ng-zorro-antd/button';
import { PermissionService } from '../../core/auth/permission.service';
import { CompanyContextService } from '../../core/company/company-context.service';
import { UNKNOWN_ERROR_KEY, apiErrorKey, apiErrorTraceId, apiValidationDetails } from '../../core/http/api-error';
import { ApiErrorDisplay } from '../../shared/api-error-display';
import { formatDecimalText } from '../../shared/decimal-text';
import { ErpDialogService } from '../../shared/dialogs/dialog.service';
import { DeliveryDraftForm, DeliveryDraftIssue, DeliverySourceLine, DeliverySourceOrder, deliveryErrorLineNo,
  draftFromOrder, matchesDeliveryReceipt, validateDeliveryDraft } from './delivery-draft-form';
import { DeliveryOrderOption, DeliveryReferenceOption, SalesDeliveryDetail, SalesDeliveryService } from './delivery.service';

/** Error codes the create page explains in the user's language (others fall back to the generic message). */
export const DELIVERY_CREATE_ERROR_CODES = ['SAL.ORDER.NOT_FOUND', 'SAL.DELIVERY.ORDER_NOT_APPROVED',
  'SAL.DELIVERY.ORDER_LINE_INVALID', 'SAL.DELIVERY.ORDER_BALANCE_EXCEEDED', 'SAL.DELIVERY.TRACKING_MODE_MISMATCH',
  'SAL.DELIVERY.TRACKING_REQUIRED', 'SAL.DELIVERY.SERIAL_DUPLICATE', 'MD.WAREHOUSE.NOT_FOUND', 'MD.WAREHOUSE.INACTIVE',
  'MD.STORAGE_LOCATION.NOT_FOUND', 'MD.STORAGE_LOCATION.INACTIVE', 'MD.ITEM.NOT_FOUND', 'MD.ITEM.INACTIVE',
  'COMMON.PERMISSION.DENIED'] as const;

const blank = (): DeliveryDraftForm => ({ orderId: null, businessDate: '', lines: [] });

/** Sales Outbound Create Interface 2.8 (REQ_F4112.3.2): saves a Draft only; no order reservation, no stock change. */
@Component({
  selector: 'erp-sales-delivery-create-page',
  imports: [FormsModule, RouterLink, TranslatePipe, NzAlertModule, NzButtonModule, ApiErrorDisplay],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <section class="create" data-testid="sd-delivery-create">
      <header><h1>{{ 'sd.deliveryCreate.title' | translate }}</h1>
        @if (permissions.has('SAL.DELIVERY.VIEW')) { <a routerLink="/sal/deliveries" data-testid="sd-delivery-create-back">{{ 'sd.deliveryCreate.back' | translate }}</a> }</header>
      <nz-alert nzType="info" nzShowIcon [nzMessage]="'sd.deliveryCreate.intro' | translate" />
      @if (saved(); as value) { <nz-alert nzType="success" nzShowIcon data-testid="sd-delivery-create-success"
          [nzMessage]="'sd.deliveryCreate.saved' | translate: { number: value.documentNo }" />
        <p data-testid="sd-delivery-create-receipt">{{ value.documentNo }} · {{ value.orderDocumentNo }} · {{ value.lines.length }} {{ 'sd.deliveryCreate.savedLines' | translate }} · {{ 'sd.delivery.draft' | translate }}</p>
        @if (hasTrackedLines(value)) { <nz-alert nzType="info" nzShowIcon role="status" data-testid="sd-delivery-create-tracking-hint"
          [nzMessage]="(canEdit() ? 'sd.deliveryCreate.trackingHint' : 'sd.deliveryCreate.trackingHintNoEdit') | translate" /> }
        <div class="actions">
          @if (hasTrackedLines(value) && canEdit()) { <a nz-button nzType="primary" [routerLink]="['/sal/deliveries', value.id, 'edit']" data-testid="sd-delivery-create-edit">{{ 'sd.deliveryCreate.pickOnEdit' | translate }}</a> }
          @if (permissions.has('SAL.DELIVERY.VIEW')) { <a nz-button [routerLink]="['/sal/deliveries']" [queryParams]="{ keyword: value.documentNo }" data-testid="sd-delivery-create-view">{{ 'sd.deliveryCreate.view' | translate }}</a> }
          <button nz-button type="button" (click)="reset()" data-testid="sd-delivery-create-another">{{ 'sd.deliveryCreate.another' | translate }}</button></div> }
      @if (uncertain()) { <nz-alert nzType="warning" nzShowIcon data-testid="sd-delivery-create-uncertain"
        [nzMessage]="(permissions.has('SAL.DELIVERY.VIEW') ? 'sd.deliveryCreate.unknown' : 'sd.deliveryCreate.unknownCreateOnly') | translate" /> }
      @if (error()) { <erp-api-error-display data-testid="sd-delivery-create-error" [messageKey]="error()!" [traceId]="traceId()" [details]="errorDetails()" />
        @if (balanceExceeded() && source()) { <button nz-button type="button" (click)="reloadBalance()" data-testid="sd-delivery-create-reload">{{ 'sd.deliveryCreate.reloadBalance' | translate }}</button> } }
      @if (!saved()) {
        <div class="field"><span>{{ 'sd.deliveryCreate.order' | translate }} <span aria-hidden="true">*</span></span><div class="picker">
          <span data-testid="sd-delivery-create-order">{{ source() ? source()!.documentNo + ' · ' + source()!.customerCode + ' · ' + source()!.customerName : ('sd.deliveryCreate.noOrder' | translate) }}</span>
          <button nz-button type="button" (click)="pickOrder()" [disabled]="locked()" data-testid="sd-delivery-create-order-pick">{{ 'sd.deliveryCreate.chooseOrder' | translate }}</button></div>
          @if (issue('orderId')) { <small role="alert">{{ 'sd.deliveryCreate.validation.orderId' | translate }}</small> }</div>
        @if (orderLoading()) { <p role="status">{{ 'sd.deliveryCreate.loadingOrder' | translate }}</p> }
        @if (orderError()) { <erp-api-error-display [messageKey]="orderError()!" [traceId]="orderTraceId()" data-testid="sd-delivery-create-order-error" />
          <button nz-button type="button" (click)="reloadBalance()" data-testid="sd-delivery-create-order-retry">{{ 'sd.delivery.retry' | translate }}</button> }
        @if (source() && !draft.lines.length && !orderLoading()) { <nz-alert nzType="warning" nzShowIcon data-testid="sd-delivery-create-no-balance" [nzMessage]="'sd.deliveryCreate.noBalance' | translate" /> }
        @if (source() && draft.lines.length) { <form (ngSubmit)="save()" novalidate data-testid="sd-delivery-create-form">
          <label class="field"><span>{{ 'sd.deliveryCreate.date' | translate }} <span aria-hidden="true">*</span></span>
            <input type="date" name="businessDate" autocomplete="off" [(ngModel)]="draft.businessDate" [disabled]="locked()" [attr.aria-invalid]="issue('businessDate') ? 'true' : null" data-testid="sd-delivery-create-date" />
            @if (issue('businessDate')) { <small role="alert">{{ 'sd.deliveryCreate.validation.businessDate' | translate }}</small> }</label>
          <p class="hint wide">{{ 'sd.deliveryCreate.balanceHint' | translate }}</p>
          <section class="wide"><h2>{{ 'sd.deliveryCreate.lines' | translate }}</h2>
            @for (line of draft.lines; track line.lineNo; let i = $index) { @let facts = sourceLine(line.orderLineId);
              <fieldset [class.failed]="lineError()?.lineNo === line.lineNo" [attr.data-testid]="'sd-delivery-create-line-' + line.lineNo">
              <legend>{{ 'sd.deliveryCreate.line' | translate: { number: line.lineNo } }}</legend>
              @if (facts) { <p class="facts">{{ 'sd.delivery.orderLine' | translate }} {{ facts.lineNo }} · <span translate="no">{{ facts.itemCode }}</span> · {{ facts.itemName }}</p>
                <dl class="balance" [attr.data-testid]="'sd-delivery-create-balance-' + line.lineNo">
                  <div><dt>{{ 'sd.deliveryCreate.ordered' | translate }}</dt><dd>{{ decimal(facts.quantity) }}</dd></div>
                  <div><dt>{{ 'sd.deliveryCreate.posted' | translate }}</dt><dd>{{ decimal(facts.deliveredQuantity) }}</dd></div>
                  <div><dt>{{ 'sd.deliveryCreate.pending' | translate }}</dt><dd>{{ decimal(facts.pendingQuantity) }}</dd></div>
                  <div><dt>{{ 'sd.deliveryCreate.remaining' | translate }}</dt><dd>{{ decimal(facts.remainingQuantity) }}</dd></div></dl> }
              <div class="line">
                <label class="field">{{ 'sd.delivery.quantity' | translate }} <input inputmode="decimal" autocomplete="off" spellcheck="false" [name]="'quantity-' + line.lineNo" [(ngModel)]="line.quantity" [disabled]="locked()"
                  [attr.aria-invalid]="issue(field(i, 'quantity')) ? 'true' : null" [attr.data-testid]="'sd-delivery-create-quantity-' + line.lineNo" />
                  @if (issue(field(i, 'quantity')); as reason) { <small role="alert">{{ ('sd.deliveryCreate.validation.' + reason) | translate }}</small> }</label>
                <div class="field"><span>{{ 'sd.deliveryCreate.warehouse' | translate }} <span aria-hidden="true">*</span></span><div class="picker">
                  <span [attr.data-testid]="'sd-delivery-create-warehouse-' + line.lineNo">{{ warehouseLabels().get(line.lineNo) || ('sd.deliveryCreate.noWarehouse' | translate) }}</span>
                  <button nz-button type="button" (click)="pickWarehouse(line.lineNo)" [disabled]="locked()" [attr.data-testid]="'sd-delivery-create-warehouse-pick-' + line.lineNo">{{ 'sd.deliveryCreate.chooseWarehouse' | translate }}</button></div>
                  @if (issue(field(i, 'warehouseId'))) { <small role="alert">{{ 'sd.deliveryCreate.validation.warehouseId' | translate }}</small> }</div>
                <div class="field"><span>{{ 'sd.deliveryCreate.location' | translate }}</span><div class="picker">
                  <span [attr.data-testid]="'sd-delivery-create-location-' + line.lineNo">{{ locationLabels().get(line.lineNo) || ('sd.deliveryCreate.noLocation' | translate) }}</span>
                  <button nz-button type="button" (click)="pickLocation(line.lineNo)" [disabled]="locked() || !line.warehouseId" [attr.data-testid]="'sd-delivery-create-location-pick-' + line.lineNo">{{ 'sd.deliveryCreate.chooseLocation' | translate }}</button>
                  @if (line.locationId) { <button nz-button nzType="link" type="button" (click)="clearLocation(line.lineNo)" [disabled]="locked()">{{ 'sd.deliveryCreate.clearLocation' | translate }}</button> }</div>
                  @if (!line.warehouseId) { <small class="hint">{{ 'sd.deliveryCreate.locationNeedsWarehouse' | translate }}</small> }</div>
              </div>
              @if (lineError()?.lineNo === line.lineNo) { <p class="line-error" role="alert" [attr.data-testid]="'sd-delivery-create-line-error-' + line.lineNo">{{ lineError()!.key | translate }}</p> }
              <button nz-button type="button" (click)="removeLine(line.lineNo)" [disabled]="locked() || draft.lines.length === 1" [attr.data-testid]="'sd-delivery-create-remove-' + line.lineNo">{{ 'sd.deliveryCreate.removeLine' | translate }}</button>
              @if (draft.lines.length === 1) { <small class="hint" role="status">{{ 'sd.deliveryCreate.oneLineMinimum' | translate }}</small> }
            </fieldset> }
          </section>
          @if (submitted() && count()) { <p class="wide" role="alert" data-testid="sd-delivery-create-errors">{{ 'sd.deliveryCreate.errors' | translate: { count: count() } }}</p> }
          <button nz-button nzType="primary" type="submit" [nzLoading]="saving()" [disabled]="locked()" data-testid="sd-delivery-create-save">{{ 'sd.deliveryCreate.save' | translate }}</button>
        </form> }
      }
    </section>`,
  styles: `.create{display:grid;gap:16px;min-width:0;color:var(--erp-color-text)}header{display:flex;justify-content:space-between;gap:12px;align-items:center}h1,h2{margin:0}form{display:grid;grid-template-columns:repeat(2,minmax(0,1fr));gap:16px}.wide{grid-column:1/-1}.field{display:grid;gap:4px;min-width:0}.picker,.actions{display:flex;gap:8px;align-items:center;flex-wrap:wrap}.picker span{overflow-wrap:anywhere}input{width:100%;min-height:32px;padding:4px 8px;background:var(--erp-color-bg-container);color:var(--erp-color-text);border:1px solid var(--erp-color-border);border-radius:4px}fieldset{border:1px solid var(--erp-color-border);border-radius:8px;margin:12px 0;padding:16px;min-width:0}fieldset.failed{border-color:var(--erp-color-error-text)}.facts{margin:0 0 8px;overflow-wrap:anywhere}.balance{display:flex;flex-wrap:wrap;gap:8px 24px;margin:0 0 12px}.balance div{display:grid}.balance dt{color:var(--erp-color-text-secondary)}.balance dd{margin:0;font-variant-numeric:tabular-nums}.line{display:grid;grid-template-columns:repeat(3,minmax(0,1fr));gap:12px;margin-bottom:12px;align-items:start}small,.line-error{color:var(--erp-color-error-text)}small.hint,.hint{color:var(--erp-color-text-secondary)}small.hint{display:block;margin-top:8px}.line-error{margin:0 0 8px}button:focus-visible,input:focus-visible,a:focus-visible{outline:2px solid var(--erp-color-primary);outline-offset:2px}@media(max-width:767px){form,.line{grid-template-columns:1fr}}`,
})
export class SalesDeliveryCreatePage {
  private readonly deliveries = inject(SalesDeliveryService);
  private readonly dialogs = inject(ErpDialogService);
  private readonly company = inject(CompanyContextService);
  readonly permissions = inject(PermissionService);
  private readonly translate = inject(TranslateService);
  private readonly destroy = inject(DestroyRef);
  private readonly host: ElementRef<HTMLElement> = inject(ElementRef);
  draft = blank();
  readonly source = signal<DeliverySourceOrder | null>(null);
  readonly warehouseLabels = signal(new Map<number, string>()); readonly locationLabels = signal(new Map<number, string>());
  readonly issues = signal<Record<string, DeliveryDraftIssue>>({}); readonly submitted = signal(false);
  readonly orderLoading = signal(false); readonly orderError = signal<string | null>(null); readonly orderTraceId = signal<string | null>(null);
  readonly saving = signal(false); readonly saved = signal<SalesDeliveryDetail | null>(null); readonly uncertain = signal(false);
  readonly error = signal<string | null>(null); readonly traceId = signal<string | null>(null); readonly errorDetails = signal<readonly string[]>([]);
  readonly lineError = signal<{ lineNo: number; key: string } | null>(null); readonly balanceExceeded = signal(false);
  private epoch = 0;
  private failure: unknown = null;
  constructor() {
    this.destroy.onDestroy(() => this.epoch++);
    const language = this.translate.onLangChange.subscribe(() => { if (this.failure) this.errorDetails.set(this.localisedDetails(this.failure)); });
    this.destroy.onDestroy(() => language.unsubscribe());
    effect(() => { this.company.current(); untracked(() => this.reset()); });
  }
  canLeave(): boolean { return !this.dirty() || window.confirm(this.translate.instant('sd.deliveryCreate.unsaved')); }
  @HostListener('window:beforeunload', ['$event']) beforeUnload(event: BeforeUnloadEvent): void { if (this.dirty()) event.preventDefault(); }
  private dirty(): boolean { return !this.saved() && this.draft.orderId != null; }
  /** ADR-152: lots/serials are chosen on the edit page, which needs VIEW and EDIT (route guards). */
  canEdit(): boolean { return this.permissions.has('SAL.DELIVERY.VIEW') && this.permissions.has('SAL.DELIVERY.EDIT'); }
  hasTrackedLines(saved: SalesDeliveryDetail): boolean { return saved.lines.some(line => line.trackingMode === 'Lot' || line.trackingMode === 'Serial'); }
  locked(): boolean { return this.saving() || this.uncertain() || !!this.saved() || this.orderLoading(); }
  field(i: number, name: string): string { return `lines[${i}].${name}`; }
  issue(name: string): DeliveryDraftIssue | undefined { return this.issues()[name]; }
  count(): number { return Object.keys(this.issues()).length; }
  decimal(value: string): string { return formatDecimalText(value, this.translate.getCurrentLang() || undefined); }
  sourceLine(orderLineId: number): DeliverySourceLine | undefined { return this.source()?.lines.find(line => line.id === orderLineId); }
  reset(): void {
    this.epoch++; this.draft = blank(); this.source.set(null); this.warehouseLabels.set(new Map()); this.locationLabels.set(new Map());
    this.clearOutcome(); this.issues.set({}); this.submitted.set(false); this.orderLoading.set(false); this.orderError.set(null);
    this.orderTraceId.set(null); this.saving.set(false); this.saved.set(null); this.uncertain.set(false);
  }
  private clearOutcome(): void {
    this.error.set(null); this.traceId.set(null); this.errorDetails.set([]); this.lineError.set(null); this.balanceExceeded.set(false);
    this.failure = null;
  }
  pickOrder(): void {
    if (this.locked()) return;
    const epoch = this.epoch;
    this.dialogs.select<DeliveryOrderOption>({ titleKey: 'sd.deliveryCreate.chooseOrder',
      columns: [{ key: 'documentNo', labelKey: 'sd.order.number', value: row => row.documentNo },
        { key: 'customerName', labelKey: 'sd.order.customer', value: row => row.customerName },
        { key: 'businessDate', labelKey: 'sd.order.date', value: row => row.businessDate }],
      load: query => this.deliveries.orderOptions(query), rowKey: row => row.id,
      errorCodes: ['COMMON.PERMISSION.DENIED'] }).subscribe(rows => {
      if (epoch !== this.epoch || !rows?.[0]) return;
      const businessDate = this.draft.businessDate;
      this.reset(); this.draft.businessDate = businessDate; this.draft.orderId = rows[0].id; this.loadOrder(rows[0].id, false);
    });
  }
  /** Reread the order's advisory balance; keeps the user's warehouse choices and quantities when the order is the same. */
  reloadBalance(): void { if (this.draft.orderId && !this.orderLoading()) this.loadOrder(this.draft.orderId, true); }
  private loadOrder(orderId: number, keep: boolean): void {
    const epoch = ++this.epoch;
    this.orderLoading.set(true); this.orderError.set(null); this.orderTraceId.set(null);
    this.deliveries.sourceOrder(orderId).subscribe({
      next: order => { if (epoch !== this.epoch) return; this.orderLoading.set(false);
        if (order?.id !== orderId || order.status !== 'Approved' || !Array.isArray(order.lines)) {
          this.source.set(null); this.draft.lines = [];
          this.orderError.set(order?.status && order.status !== 'Approved' ? 'sd.deliveryCreate.orderNotApproved' : UNKNOWN_ERROR_KEY); return; }
        this.source.set(order);
        if (!keep || !this.draft.lines.length) this.draft = { ...draftFromOrder(order, this.draft.businessDate) };
        this.issues.set(this.submitted() ? validateDeliveryDraft(this.draft, order) : {});
        if (keep) { this.lineError.set(null); this.balanceExceeded.set(false); this.error.set(null); }
      },
      error: failure => { if (epoch !== this.epoch) return; this.orderLoading.set(false);
        this.orderError.set(failure instanceof TimeoutError ? 'sd.delivery.timeout'
          : apiErrorKey(failure, ['SAL.ORDER.NOT_FOUND', 'COMMON.PERMISSION.DENIED']));
        this.orderTraceId.set(apiErrorTraceId(failure)); },
    });
  }
  removeLine(lineNo: number): void {
    if (this.locked() || this.draft.lines.length < 2) return;
    this.draft.lines = this.draft.lines.filter(line => line.lineNo !== lineNo);
    this.warehouseLabels.update(map => { const next = new Map(map); next.delete(lineNo); return next; });
    this.locationLabels.update(map => { const next = new Map(map); next.delete(lineNo); return next; });
    if (this.lineError()?.lineNo === lineNo) this.lineError.set(null);
    if (this.submitted()) this.issues.set(validateDeliveryDraft(this.draft, this.source()));
  }
  pickWarehouse(lineNo: number): void {
    const epoch = this.epoch;
    this.dialogs.select<DeliveryReferenceOption>({ titleKey: 'sd.deliveryCreate.chooseWarehouse',
      columns: [{ key: 'code', labelKey: 'sd.quotationCreate.code', value: row => row.code },
        { key: 'name', labelKey: 'sd.quotationCreate.name', value: row => row.name }],
      load: query => this.deliveries.warehouseOptions(query), rowKey: row => row.id, errorCodes: ['COMMON.PERMISSION.DENIED'] }).subscribe(rows => {
      const line = this.draft.lines.find(candidate => candidate.lineNo === lineNo);
      if (epoch !== this.epoch || !rows?.[0] || !line) return;
      if (line.warehouseId !== rows[0].id) { line.locationId = null; this.locationLabels.update(map => { const next = new Map(map); next.delete(lineNo); return next; }); }
      line.warehouseId = rows[0].id;
      this.warehouseLabels.update(map => new Map(map).set(lineNo, `${rows[0].code} · ${rows[0].name}`));
    });
  }
  pickLocation(lineNo: number): void {
    const epoch = this.epoch;
    const warehouseId = this.draft.lines.find(candidate => candidate.lineNo === lineNo)?.warehouseId;
    if (!warehouseId) return;
    this.dialogs.select<DeliveryReferenceOption>({ titleKey: 'sd.deliveryCreate.chooseLocation',
      columns: [{ key: 'code', labelKey: 'sd.quotationCreate.code', value: row => row.code },
        { key: 'name', labelKey: 'sd.quotationCreate.name', value: row => row.name }],
      load: query => this.deliveries.locationOptions(warehouseId, query), rowKey: row => row.id,
      errorCodes: ['COMMON.PERMISSION.DENIED', 'MD.WAREHOUSE.NOT_FOUND'] }).subscribe(rows => {
      const line = this.draft.lines.find(candidate => candidate.lineNo === lineNo);
      if (epoch !== this.epoch || !rows?.[0] || !line || line.warehouseId !== warehouseId) return;
      line.locationId = rows[0].id;
      this.locationLabels.update(map => new Map(map).set(lineNo, `${rows[0].code} · ${rows[0].name}`));
    });
  }
  clearLocation(lineNo: number): void {
    const line = this.draft.lines.find(candidate => candidate.lineNo === lineNo);
    if (!line || this.locked()) return;
    line.locationId = null;
    this.locationLabels.update(map => { const next = new Map(map); next.delete(lineNo); return next; });
  }
  save(): void {
    if (this.locked()) return;
    this.submitted.set(true);
    for (const line of this.draft.lines) line.quantity = line.quantity.trim();
    const issues = validateDeliveryDraft(this.draft, this.source());
    this.issues.set(issues);
    if (Object.keys(issues).length) { this.focusFirstIssue(Object.keys(issues)[0]); return; }
    const epoch = this.epoch, expected = structuredClone(this.draft);
    this.saving.set(true); this.clearOutcome();
    this.deliveries.create(expected).subscribe({
      next: saved => { if (epoch !== this.epoch) return; this.saving.set(false);
        if (!matchesDeliveryReceipt(saved, expected)) { this.uncertain.set(true); return; }
        this.saved.set(saved); },
      error: failure => { if (epoch !== this.epoch) return; this.saving.set(false);
        const status = (failure as { status?: number })?.status;
        this.traceId.set(apiErrorTraceId(failure));
        if (failure instanceof TimeoutError || status === 0 || (status != null && status >= 500)) {
          this.uncertain.set(true); if (status != null && status >= 500) this.error.set('sd.deliveryCreate.serverUncertain'); return; }
        const key = apiErrorKey(failure, DELIVERY_CREATE_ERROR_CODES);
        const lineNo = deliveryErrorLineNo(failure);
        if (lineNo != null && this.draft.lines.some(line => line.lineNo === lineNo)) {
          this.lineError.set({ lineNo, key }); this.focusLine(lineNo); }
        this.balanceExceeded.set(key === 'errors.SAL.DELIVERY.ORDER_BALANCE_EXCEEDED');
        this.failure = failure; this.errorDetails.set(this.localisedDetails(failure));
        this.error.set(lineNo != null ? 'sd.deliveryCreate.lineRejected'
          : status === 422 && key === UNKNOWN_ERROR_KEY ? 'sd.deliveryCreate.rejected' : key);
      },
    });
  }
  private focusLine(lineNo: number): void {
    queueMicrotask(() => this.host.nativeElement.querySelector<HTMLElement>(`[data-testid="sd-delivery-create-quantity-${lineNo}"]`)?.focus());
  }
  private focusFirstIssue(field: string): void {
    const match = /^lines\[(\d+)\]\.(\w+)$/.exec(field);
    const lineNo = match ? this.draft.lines[Number(match[1])]?.lineNo : undefined;
    const target = field === 'orderId' ? 'sd-delivery-create-order-pick' : field === 'businessDate' ? 'sd-delivery-create-date'
      : match && lineNo != null ? `sd-delivery-create-${match[2] === 'warehouseId' ? 'warehouse-pick' : 'quantity'}-${lineNo}`
        : 'sd-delivery-create-save';
    queueMicrotask(() => this.host.nativeElement.querySelector<HTMLElement>(`[data-testid="${target}"]`)?.focus());
  }
  /** Server field paths are shown with the user's own field names, never as raw English validator text. */
  private localisedDetails(failure: unknown): string[] {
    const colon = (this.translate.getCurrentLang() ?? '').startsWith('zh') ? '：' : ': ';
    return apiValidationDetails(failure).map(detail => {
      const field = detail.slice(0, Math.max(detail.indexOf(': '), 0));
      const index = /^lines\[(\d+)\]/.exec(field);
      const lineNo = index ? this.draft.lines[Number(index[1])]?.lineNo : undefined;
      const name = field.endsWith('quantity') ? 'sd.delivery.quantity' : field.includes('warehouseId') ? 'sd.deliveryCreate.warehouse'
        : field.includes('locationId') ? 'sd.deliveryCreate.location' : field === 'businessDate' ? 'sd.deliveryCreate.date'
          : field === 'orderId' ? 'sd.deliveryCreate.order' : 'sd.deliveryCreate.lines';
      const prefix = lineNo != null ? `${this.translate.instant('sd.deliveryCreate.line', { number: lineNo })} · ` : '';
      return `${prefix}${this.translate.instant(name)}${colon}${this.translate.instant('sd.deliveryCreate.serverField')}`;
    });
  }
}
