import { ChangeDetectionStrategy, Component, DestroyRef, ElementRef, HostListener, effect, inject, signal, untracked } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, RouterLink } from '@angular/router';
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
import { StorageLocation, Warehouse, WarehouseService } from '../md/warehouse.service';
import { DELIVERY_CREATE_ERROR_CODES } from './delivery-create-page';
import { DeliveryDraftForm, DeliveryDraftIssue, DeliverySourceLine, DeliverySourceOrder, deliveryErrorLineNo,
  draftFromDelivery, matchesDeliveryReceipt, validateDeliveryDraft } from './delivery-draft-form';
import { SalesDeliveryDetail, SalesDeliveryLine, SalesDeliveryService } from './delivery.service';

/** Codes the edit page explains in the user's language; the create list plus edit/delete-only outcomes. */
export const DELIVERY_EDIT_ERROR_CODES = [...DELIVERY_CREATE_ERROR_CODES, 'SAL.DELIVERY.NOT_FOUND',
  'SAL.DELIVERY.INVALID_STATUS', 'SAL.DELIVERY.SOURCE_IMMUTABLE'] as const;

const blank = (): DeliveryDraftForm => ({ orderId: null, businessDate: '', lines: [] });

/**
 * Sales Outbound Edit Interface 2.9 (REQ_F4112.3.3, ADR-149): revise a saved Draft/Rejected delivery (PUT replaces every
 * line; saving a Rejected one returns it to Draft) or delete a Draft. Neither reserves order balance nor changes stock.
 */
@Component({
  selector: 'erp-sales-delivery-edit-page',
  imports: [FormsModule, RouterLink, TranslatePipe, NzAlertModule, NzButtonModule, ApiErrorDisplay],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <section class="edit" data-testid="sd-delivery-edit">
      <header><h1>{{ 'sd.deliveryEdit.title' | translate }}</h1>
        <a routerLink="/sal/deliveries" data-testid="sd-delivery-edit-back">{{ 'sd.deliveryCreate.back' | translate }}</a></header>
      <nz-alert nzType="info" nzShowIcon [nzMessage]="'sd.deliveryEdit.intro' | translate" />
      @if (loading()) { <p role="status" data-testid="sd-delivery-edit-loading">{{ 'sd.deliveryEdit.loading' | translate }}</p> }
      @if (loadError()) { <erp-api-error-display data-testid="sd-delivery-edit-load-error" [messageKey]="loadError()!" [traceId]="loadTraceId()" />
        <button nz-button type="button" (click)="loadSaved()" data-testid="sd-delivery-edit-load-retry">{{ 'sd.delivery.retry' | translate }}</button> }
      @if (identity(); as saved) { <p data-testid="sd-delivery-edit-identity"><span translate="no">{{ saved.documentNo }}</span> ·
        {{ 'sd.delivery.order' | translate }} <span translate="no">{{ saved.orderDocumentNo }}</span> · {{ saved.customerCode }} · {{ saved.customerName }} ·
        {{ ('sd.delivery.' + saved.status.toLowerCase()) | translate }} · {{ 'sd.delivery.version' | translate }} {{ version() }}</p> }
      @if (blocked()) { <nz-alert nzType="warning" nzShowIcon role="status" data-testid="sd-delivery-edit-blocked" [nzMessage]="blocked()! | translate" /> }
      @if (identity()?.status === 'Rejected' && !blocked() && !deleted()) { <nz-alert nzType="warning" nzShowIcon role="status" data-testid="sd-delivery-edit-rejected" [nzMessage]="'sd.deliveryEdit.rejectedHint' | translate" /> }
      @if (savedNotice()) { <nz-alert nzType="success" nzShowIcon role="status" data-testid="sd-delivery-edit-success" [nzMessage]="'sd.deliveryEdit.saved' | translate: { number: identity()?.documentNo, version: version() }" /> }
      @if (deleted()) { <nz-alert nzType="success" nzShowIcon role="status" data-testid="sd-delivery-edit-deleted" [nzMessage]="'sd.deliveryEdit.deleted' | translate: { number: identity()?.documentNo }" /> }
      @if (noChange()) { <nz-alert nzType="warning" nzShowIcon role="status" data-testid="sd-delivery-edit-no-change" [nzMessage]="'sd.deliveryEdit.noChange' | translate" /> }
      @if (stale()) { <nz-alert nzType="warning" nzShowIcon role="status" data-testid="sd-delivery-edit-stale" [nzMessage]="stale()! | translate" />
        <div class="actions"><button nz-button type="button" (click)="refresh()" data-testid="sd-delivery-edit-refresh">{{ 'sd.deliveryEdit.refresh' | translate }}</button></div> }
      @if (error()) { <erp-api-error-display data-testid="sd-delivery-edit-error" [messageKey]="error()!" [traceId]="traceId()" [details]="errorDetails()" />
        @if (balanceExceeded() && permissions.has('SAL.ORDER.VIEW')) { <div class="actions"><button nz-button type="button" (click)="loadOrder()" data-testid="sd-delivery-edit-reload">{{ 'sd.deliveryCreate.reloadBalance' | translate }}</button></div> } }
      @if (identity() && !blocked() && !deleted() && draft.lines.length) {
        @if (!permissions.has('SAL.ORDER.VIEW')) { <p class="hint" role="status" data-testid="sd-delivery-edit-order-permission">{{ 'sd.deliveryEdit.needOrderView' | translate }}</p> }
        @if (orderLoading()) { <p role="status">{{ 'sd.deliveryCreate.loadingOrder' | translate }}</p> }
        @if (orderError()) { <erp-api-error-display [messageKey]="orderError()!" [traceId]="orderTraceId()" data-testid="sd-delivery-edit-order-error" />
          <button nz-button type="button" (click)="loadOrder()" data-testid="sd-delivery-edit-order-retry">{{ 'sd.delivery.retry' | translate }}</button> }
        <form (ngSubmit)="save()" novalidate data-testid="sd-delivery-edit-form">
          <label class="field"><span>{{ 'sd.deliveryCreate.date' | translate }} <span aria-hidden="true">*</span></span>
            <input type="date" name="businessDate" autocomplete="off" [(ngModel)]="draft.businessDate" [disabled]="locked()" [attr.aria-invalid]="issue('businessDate') ? 'true' : null" data-testid="sd-delivery-edit-date" />
            @if (issue('businessDate')) { <small role="alert">{{ 'sd.deliveryCreate.validation.businessDate' | translate }}</small> }</label>
          @if (source()) { <p class="hint wide">{{ 'sd.deliveryEdit.balanceHint' | translate }}</p> }
          <section class="wide"><h2>{{ 'sd.deliveryCreate.lines' | translate }}</h2>
            @for (line of draft.lines; track line.lineNo; let i = $index) { @let facts = sourceLine(line.orderLineId); @let item = savedLine(line.lineNo);
              <fieldset [class.failed]="lineError()?.lineNo === line.lineNo" [attr.data-testid]="'sd-delivery-edit-line-' + line.lineNo">
              <legend>{{ 'sd.deliveryCreate.line' | translate: { number: line.lineNo } }}</legend>
              @if (item) { <p class="facts">{{ 'sd.delivery.orderLine' | translate }} {{ item.orderLineNo }} · <span translate="no">{{ item.itemCode }}</span> · {{ item.itemName }}</p> }
              @if (facts) { <dl class="balance" [attr.data-testid]="'sd-delivery-edit-balance-' + line.lineNo">
                  <div><dt>{{ 'sd.deliveryCreate.ordered' | translate }}</dt><dd>{{ decimal(facts.quantity) }}</dd></div>
                  <div><dt>{{ 'sd.deliveryCreate.posted' | translate }}</dt><dd>{{ decimal(facts.deliveredQuantity) }}</dd></div>
                  <div><dt>{{ 'sd.deliveryCreate.pending' | translate }}</dt><dd>{{ decimal(facts.pendingQuantity) }}</dd></div>
                  <div><dt>{{ 'sd.deliveryCreate.remaining' | translate }}</dt><dd>{{ decimal(facts.remainingQuantity) }}</dd></div></dl> }
              <div class="line">
                <label class="field">{{ 'sd.delivery.quantity' | translate }} <input inputmode="decimal" autocomplete="off" spellcheck="false" [name]="'quantity-' + line.lineNo" [(ngModel)]="line.quantity" [disabled]="locked()"
                  [attr.aria-invalid]="issue(field(i, 'quantity')) ? 'true' : null" [attr.data-testid]="'sd-delivery-edit-quantity-' + line.lineNo" />
                  @if (issue(field(i, 'quantity')); as reason) { <small role="alert">{{ ('sd.deliveryCreate.validation.' + reason) | translate }}</small> }</label>
                <div class="field"><span>{{ 'sd.deliveryCreate.warehouse' | translate }} <span aria-hidden="true">*</span></span><div class="picker">
                  <span [attr.data-testid]="'sd-delivery-edit-warehouse-' + line.lineNo">{{ warehouseLabels().get(line.lineNo) || ('sd.deliveryEdit.savedWarehouse' | translate: { id: line.warehouseId }) }}</span>
                  <button nz-button type="button" (click)="pickWarehouse(line.lineNo)" [disabled]="locked() || !permissions.has('MD.WAREHOUSE.VIEW')" [attr.data-testid]="'sd-delivery-edit-warehouse-pick-' + line.lineNo">{{ 'sd.deliveryCreate.chooseWarehouse' | translate }}</button></div>
                  @if (issue(field(i, 'warehouseId'))) { <small role="alert">{{ 'sd.deliveryCreate.validation.warehouseId' | translate }}</small> }</div>
                <div class="field"><span>{{ 'sd.deliveryCreate.location' | translate }}</span><div class="picker">
                  <span [attr.data-testid]="'sd-delivery-edit-location-' + line.lineNo">{{ locationLabels().get(line.lineNo) || (line.locationId ? ('sd.deliveryEdit.savedLocation' | translate: { id: line.locationId }) : ('sd.deliveryCreate.noLocation' | translate)) }}</span>
                  <button nz-button type="button" (click)="pickLocation(line.lineNo)" [disabled]="locked() || !line.warehouseId || !permissions.has('MD.WAREHOUSE.VIEW')" [attr.data-testid]="'sd-delivery-edit-location-pick-' + line.lineNo">{{ 'sd.deliveryCreate.chooseLocation' | translate }}</button>
                  @if (line.locationId) { <button nz-button nzType="link" type="button" (click)="clearLocation(line.lineNo)" [disabled]="locked()" [attr.data-testid]="'sd-delivery-edit-location-clear-' + line.lineNo">{{ 'sd.deliveryCreate.clearLocation' | translate }}</button> }</div></div>
              </div>
              @if (line.tracking?.length) { <div class="tracking" [attr.data-testid]="'sd-delivery-edit-tracking-' + line.lineNo"><span>{{ 'sd.delivery.tracking' | translate }}:</span>
                @for (pick of line.tracking; track $index) { <span class="pick" translate="no">{{ (pick.serialNo ? 'sd.delivery.serial' : 'sd.delivery.lot') | translate: { value: pick.lotNo ?? pick.serialNo } }} × {{ decimal(pick.quantity) }}</span> }
                <small class="hint">{{ 'sd.deliveryEdit.trackingKept' | translate }}</small></div> }
              @if (lineError()?.lineNo === line.lineNo) { <p class="line-error" role="alert" [attr.data-testid]="'sd-delivery-edit-line-error-' + line.lineNo">{{ lineError()!.key | translate }}</p> }
              <button nz-button type="button" (click)="removeLine(line.lineNo)" [disabled]="locked() || draft.lines.length === 1" [attr.data-testid]="'sd-delivery-edit-remove-' + line.lineNo">{{ 'sd.deliveryCreate.removeLine' | translate }}</button>
              @if (draft.lines.length === 1) { <small class="hint" role="status">{{ 'sd.deliveryCreate.oneLineMinimum' | translate }}</small> }
            </fieldset> }
          </section>
          @if (!permissions.has('MD.WAREHOUSE.VIEW')) { <p class="hint wide" role="status" data-testid="sd-delivery-edit-warehouse-permission">{{ 'sd.deliveryCreate.needWarehouseView' | translate }}</p> }
          @if (submitted() && count()) { <p class="wide" role="alert" data-testid="sd-delivery-edit-errors">{{ 'sd.deliveryCreate.errors' | translate: { count: count() } }}</p> }
          <div class="actions wide">
            <button nz-button nzType="primary" type="submit" [nzLoading]="saving()" [disabled]="locked()" data-testid="sd-delivery-edit-save">{{ 'sd.deliveryEdit.save' | translate }}</button>
            @if (permissions.has('SAL.DELIVERY.DELETE')) {
              <button nz-button nzDanger type="button" (click)="remove()" [nzLoading]="deleting()" [disabled]="locked() || identity()?.status !== 'Draft'" data-testid="sd-delivery-edit-delete">{{ 'sd.deliveryEdit.delete' | translate }}</button>
              @if (identity()?.status !== 'Draft') { <small class="hint" role="status" data-testid="sd-delivery-edit-delete-hint">{{ 'sd.deliveryEdit.deleteDraftOnly' | translate }}</small> } }
            @if (stale()) { <small class="hint" role="status" data-testid="sd-delivery-edit-stale-hint">{{ 'sd.deliveryEdit.staleHint' | translate }}</small> }
          </div>
        </form>
      }
    </section>`,
  styles: `.edit{display:grid;gap:16px;min-width:0;color:var(--erp-color-text)}header{display:flex;justify-content:space-between;gap:12px;align-items:center}h1,h2{margin:0}form{display:grid;grid-template-columns:repeat(2,minmax(0,1fr));gap:16px}.wide{grid-column:1/-1}.field{display:grid;gap:4px;min-width:0}.picker,.actions{display:flex;gap:8px;align-items:center;flex-wrap:wrap}.picker span,p{overflow-wrap:anywhere}input{width:100%;min-height:32px;padding:4px 8px;background:var(--erp-color-bg-container);color:var(--erp-color-text);border:1px solid var(--erp-color-border);border-radius:4px}fieldset{border:1px solid var(--erp-color-border);border-radius:8px;margin:12px 0;padding:16px;min-width:0}fieldset.failed{border-color:var(--erp-color-error-text)}.facts{margin:0 0 8px}.balance{display:flex;flex-wrap:wrap;gap:8px 24px;margin:0 0 12px}.balance div{display:grid}.balance dt{color:var(--erp-color-text-secondary)}.balance dd{margin:0;font-variant-numeric:tabular-nums}.line{display:grid;grid-template-columns:repeat(3,minmax(0,1fr));gap:12px;margin-bottom:12px;align-items:start}.tracking{display:flex;flex-wrap:wrap;gap:4px 12px;margin-bottom:12px}.tracking small{flex-basis:100%}small,.line-error{color:var(--erp-color-error-text)}small.hint,.hint{color:var(--erp-color-text-secondary)}.line-error{margin:0 0 8px}button:focus-visible,input:focus-visible,a:focus-visible{outline:2px solid var(--erp-color-primary);outline-offset:2px}@media(max-width:767px){form,.line{grid-template-columns:1fr}}`,
})
export class SalesDeliveryEditPage {
  private readonly deliveries = inject(SalesDeliveryService);
  private readonly warehouses = inject(WarehouseService);
  private readonly dialogs = inject(ErpDialogService);
  private readonly company = inject(CompanyContextService);
  readonly permissions = inject(PermissionService);
  private readonly translate = inject(TranslateService);
  private readonly destroy = inject(DestroyRef);
  private readonly host: ElementRef<HTMLElement> = inject(ElementRef);
  readonly editId = Number(inject(ActivatedRoute).snapshot.paramMap.get('id'));
  draft = blank();
  private baseline = '';
  readonly identity = signal<SalesDeliveryDetail | null>(null); readonly version = signal(0);
  readonly source = signal<DeliverySourceOrder | null>(null);
  readonly warehouseLabels = signal(new Map<number, string>()); readonly locationLabels = signal(new Map<number, string>());
  readonly issues = signal<Record<string, DeliveryDraftIssue>>({}); readonly submitted = signal(false);
  readonly loading = signal(false); readonly loadError = signal<string | null>(null); readonly loadTraceId = signal<string | null>(null);
  readonly blocked = signal<string | null>(null);
  readonly orderLoading = signal(false); readonly orderError = signal<string | null>(null); readonly orderTraceId = signal<string | null>(null);
  readonly saving = signal(false); readonly deleting = signal(false); readonly savedNotice = signal(false); readonly deleted = signal(false);
  readonly noChange = signal(false);
  /** Why the loaded version can no longer be saved (409, 404, status changed, uncertain outcome); input stays until refresh. */
  readonly stale = signal<string | null>(null);
  readonly error = signal<string | null>(null); readonly traceId = signal<string | null>(null); readonly errorDetails = signal<readonly string[]>([]);
  readonly lineError = signal<{ lineNo: number; key: string } | null>(null); readonly balanceExceeded = signal(false);
  private epoch = 0;
  private failure: unknown = null;
  constructor() {
    this.destroy.onDestroy(() => this.epoch++);
    const language = this.translate.onLangChange.subscribe(() => { if (this.failure) this.errorDetails.set(this.localisedDetails(this.failure)); });
    this.destroy.onDestroy(() => language.unsubscribe());
    effect(() => { this.company.current(); untracked(() => this.loadSaved()); });
  }
  canLeave(): boolean { return !this.dirty() || window.confirm(this.translate.instant('sd.deliveryEdit.unsaved')); }
  @HostListener('window:beforeunload', ['$event']) beforeUnload(event: BeforeUnloadEvent): void { if (this.dirty()) event.preventDefault(); }
  private dirty(): boolean { return !this.deleted() && !this.blocked() && !!this.identity() && JSON.stringify(this.draft) !== this.baseline; }
  locked(): boolean { return this.saving() || this.deleting() || this.loading() || !!this.stale() || this.deleted() || !!this.blocked(); }
  field(i: number, name: string): string { return `lines[${i}].${name}`; }
  issue(name: string): DeliveryDraftIssue | undefined { return this.issues()[name]; }
  count(): number { return Object.keys(this.issues()).length; }
  decimal(value: string): string { return formatDecimalText(value, this.translate.getCurrentLang() || undefined); }
  sourceLine(orderLineId: number): DeliverySourceLine | undefined { return this.source()?.lines.find(line => line.id === orderLineId); }
  savedLine(lineNo: number): SalesDeliveryLine | undefined { return this.identity()?.lines.find(line => line.lineNo === lineNo); }

  /** Read the saved delivery (and, with order access, its order's advisory balance); discards local input. */
  loadSaved(): void {
    const epoch = ++this.epoch;
    this.draft = blank(); this.baseline = JSON.stringify(this.draft); this.identity.set(null); this.source.set(null); this.blocked.set(null);
    this.warehouseLabels.set(new Map()); this.locationLabels.set(new Map()); this.issues.set({}); this.submitted.set(false);
    this.clearOutcome(); this.stale.set(null); this.savedNotice.set(false); this.deleted.set(false); this.saving.set(false); this.deleting.set(false);
    this.orderLoading.set(false); this.orderError.set(null); this.orderTraceId.set(null); this.loadError.set(null); this.loadTraceId.set(null);
    if (!Number.isSafeInteger(this.editId) || this.editId <= 0) { this.loadError.set('errors.SAL.DELIVERY.NOT_FOUND'); return; }
    this.loading.set(true);
    this.deliveries.get(this.editId).subscribe({
      next: saved => { if (epoch !== this.epoch) return; this.loading.set(false); this.apply(saved); if (!this.blocked()) this.loadOrder(); },
      error: failure => { if (epoch !== this.epoch) return; this.loading.set(false);
        this.loadError.set(failure instanceof TimeoutError ? 'sd.delivery.timeout' : apiErrorKey(failure, ['SAL.DELIVERY.NOT_FOUND', 'COMMON.PERMISSION.DENIED']));
        this.loadTraceId.set(apiErrorTraceId(failure)); },
    });
  }
  refresh(): void { if (!this.dirty() || this.deleted() || window.confirm(this.translate.instant('sd.deliveryEdit.refreshDiscards'))) this.loadSaved(); }
  private apply(saved: SalesDeliveryDetail): void {
    if (saved?.id !== this.editId || !saved.documentNo) { this.loadError.set(UNKNOWN_ERROR_KEY); return; }
    this.identity.set(saved); this.version.set(saved.version);
    const mapped = draftFromDelivery(saved);
    if (!mapped) {
      this.blocked.set(saved.status === 'Pending' ? 'sd.deliveryEdit.blockedPending' : saved.status === 'Posted'
        ? 'sd.deliveryEdit.blockedPosted' : 'sd.deliveryEdit.blocked');
      return;
    }
    this.draft = mapped; this.baseline = JSON.stringify(mapped);
  }
  loadOrder(): void {
    const orderId = this.draft.orderId;
    if (!orderId || !this.permissions.has('SAL.ORDER.VIEW') || this.orderLoading()) return;
    const epoch = this.epoch;
    this.orderLoading.set(true); this.orderError.set(null); this.orderTraceId.set(null);
    this.deliveries.sourceOrder(orderId).subscribe({
      next: order => { if (epoch !== this.epoch) return; this.orderLoading.set(false);
        if (order?.id !== orderId || !Array.isArray(order.lines)) { this.source.set(null); this.orderError.set(UNKNOWN_ERROR_KEY); return; }
        this.source.set(order.status === 'Approved' ? order : null);
        if (order.status !== 'Approved') this.orderError.set('sd.deliveryEdit.orderNotApproved');
        if (this.balanceExceeded()) { this.balanceExceeded.set(false); this.lineError.set(null); this.error.set(null); }
        if (this.submitted()) this.issues.set(validateDeliveryDraft(this.draft, this.source()));
      },
      error: failure => { if (epoch !== this.epoch) return; this.orderLoading.set(false);
        this.orderError.set(failure instanceof TimeoutError ? 'sd.delivery.timeout' : apiErrorKey(failure, ['SAL.ORDER.NOT_FOUND', 'COMMON.PERMISSION.DENIED']));
        this.orderTraceId.set(apiErrorTraceId(failure)); },
    });
  }
  private clearOutcome(): void {
    this.error.set(null); this.traceId.set(null); this.errorDetails.set([]); this.lineError.set(null); this.balanceExceeded.set(false);
    this.noChange.set(false); this.failure = null;
  }
  removeLine(lineNo: number): void {
    if (this.locked() || this.draft.lines.length < 2) return;
    this.draft.lines = this.draft.lines.filter(line => line.lineNo !== lineNo);
    for (const labels of [this.warehouseLabels, this.locationLabels]) labels.update(map => { const next = new Map(map); next.delete(lineNo); return next; });
    if (this.lineError()?.lineNo === lineNo) this.lineError.set(null);
    if (this.submitted()) this.issues.set(validateDeliveryDraft(this.draft, this.source()));
  }
  pickWarehouse(lineNo: number): void {
    if (this.locked()) return;
    const epoch = this.epoch;
    this.dialogs.select<Warehouse>({ titleKey: 'sd.deliveryCreate.chooseWarehouse',
      columns: [{ key: 'code', labelKey: 'sd.quotationCreate.code', value: row => row.code },
        { key: 'name', labelKey: 'sd.quotationCreate.name', value: row => row.name }],
      load: query => this.warehouses.list(query), rowKey: row => row.id, errorCodes: ['COMMON.PERMISSION.DENIED'] }).subscribe(rows => {
      const line = this.draft.lines.find(candidate => candidate.lineNo === lineNo);
      if (epoch !== this.epoch || !rows?.[0] || !line || this.locked()) return;
      if (line.warehouseId !== rows[0].id) { line.locationId = null; this.locationLabels.update(map => { const next = new Map(map); next.delete(lineNo); return next; }); }
      line.warehouseId = rows[0].id;
      this.warehouseLabels.update(map => new Map(map).set(lineNo, `${rows[0].code} · ${rows[0].name}`));
    });
  }
  pickLocation(lineNo: number): void {
    const warehouseId = this.draft.lines.find(candidate => candidate.lineNo === lineNo)?.warehouseId;
    if (!warehouseId || this.locked()) return;
    const epoch = this.epoch;
    this.dialogs.select<StorageLocation>({ titleKey: 'sd.deliveryCreate.chooseLocation',
      columns: [{ key: 'code', labelKey: 'sd.quotationCreate.code', value: row => row.code },
        { key: 'name', labelKey: 'sd.quotationCreate.name', value: row => row.name }],
      load: query => this.warehouses.listLocations(warehouseId, query), rowKey: row => row.id,
      errorCodes: ['COMMON.PERMISSION.DENIED'] }).subscribe(rows => {
      const line = this.draft.lines.find(candidate => candidate.lineNo === lineNo);
      if (epoch !== this.epoch || !rows?.[0] || !line || line.warehouseId !== warehouseId || this.locked()) return;
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
    this.submitted.set(true); this.savedNotice.set(false); this.clearOutcome();
    for (const line of this.draft.lines) line.quantity = line.quantity.trim();
    const issues = validateDeliveryDraft(this.draft, this.source());
    this.issues.set(issues);
    if (Object.keys(issues).length) { this.focusFirstIssue(Object.keys(issues)[0]); return; }
    if (JSON.stringify(this.draft) === this.baseline) { this.noChange.set(true); return; }
    const epoch = this.epoch, version = this.version(), expected = structuredClone(this.draft), number = this.identity()?.documentNo;
    this.saving.set(true);
    this.deliveries.replace(this.editId, version, expected).subscribe({
      next: saved => { if (epoch !== this.epoch) return; this.saving.set(false);
        // Success only when the reply is this delivery, a newer version, back in Draft and line-for-line what was sent.
        if (saved?.id !== this.editId || saved.documentNo !== number || !(saved.version > version) || !matchesDeliveryReceipt(saved, expected)) {
          this.stale.set('sd.deliveryEdit.unknown'); return; }
        const labels = [this.warehouseLabels(), this.locationLabels()];
        this.apply(saved); this.warehouseLabels.set(labels[0]); this.locationLabels.set(labels[1]);
        this.submitted.set(false); this.issues.set({}); this.savedNotice.set(true);
      },
      error: failure => { if (epoch !== this.epoch) return; this.saving.set(false); this.fail(failure, 'save'); },
    });
  }
  remove(): void {
    const saved = this.identity();
    if (this.locked() || saved?.status !== 'Draft') return;
    const epoch = this.epoch, version = this.version();
    this.dialogs.confirm({ titleKey: 'sd.deliveryEdit.deleteTitle', contentKey: this.dirty() ? 'sd.deliveryEdit.deleteConfirmDirty' : 'sd.deliveryEdit.deleteConfirm',
      params: { number: saved.documentNo }, okKey: 'sd.deliveryEdit.delete', danger: true }).subscribe(ok => {
      if (!ok || epoch !== this.epoch || this.locked()) return;
      this.savedNotice.set(false); this.clearOutcome(); this.deleting.set(true);
      this.deliveries.remove(this.editId, version).subscribe({
        next: () => { if (epoch !== this.epoch) return; this.deleting.set(false); this.deleted.set(true); },
        error: failure => { if (epoch !== this.epoch) return; this.deleting.set(false); this.fail(failure, 'delete'); },
      });
    });
  }
  /** Shared 4xx/5xx handling: the user's input is never cleared by a failed save or delete. */
  private fail(failure: unknown, action: 'save' | 'delete'): void {
    const status = (failure as { status?: number })?.status;
    this.traceId.set(apiErrorTraceId(failure));
    if (failure instanceof TimeoutError || status === 0 || (status != null && status >= 500)) {
      this.stale.set(action === 'save' ? 'sd.deliveryEdit.unknown' : 'sd.deliveryEdit.deleteUnknown');
      if (status != null && status >= 500) this.error.set('sd.deliveryEdit.serverError');
      return;
    }
    if (status === 409) { this.stale.set('sd.deliveryEdit.conflict'); return; }
    if (status === 404) { this.stale.set('sd.deliveryEdit.unavailable'); return; }
    const key = apiErrorKey(failure, DELIVERY_EDIT_ERROR_CODES);
    if (key === 'errors.SAL.DELIVERY.INVALID_STATUS') { this.stale.set('sd.deliveryEdit.statusChanged'); return; }
    const lineNo = deliveryErrorLineNo(failure);
    if (lineNo != null && this.draft.lines.some(line => line.lineNo === lineNo)) { this.lineError.set({ lineNo, key }); this.focusLine(lineNo); }
    this.balanceExceeded.set(key === 'errors.SAL.DELIVERY.ORDER_BALANCE_EXCEEDED');
    this.failure = failure; this.errorDetails.set(this.localisedDetails(failure));
    this.error.set(lineNo != null ? 'sd.deliveryEdit.lineRejected'
      : status === 422 && key === UNKNOWN_ERROR_KEY ? (action === 'save' ? 'sd.deliveryEdit.rejected' : 'sd.deliveryEdit.deleteRejected') : key);
  }
  private focusLine(lineNo: number): void {
    queueMicrotask(() => this.host.nativeElement.querySelector<HTMLElement>(`[data-testid="sd-delivery-edit-quantity-${lineNo}"]`)?.focus());
  }
  private focusFirstIssue(field: string): void {
    const match = /^lines\[(\d+)\]\.(\w+)$/.exec(field);
    const lineNo = match ? this.draft.lines[Number(match[1])]?.lineNo : undefined;
    const target = field === 'businessDate' ? 'sd-delivery-edit-date'
      : match && lineNo != null ? `sd-delivery-edit-${match[2] === 'warehouseId' ? 'warehouse-pick' : 'quantity'}-${lineNo}` : 'sd-delivery-edit-save';
    queueMicrotask(() => this.host.nativeElement.querySelector<HTMLElement>(`[data-testid="${target}"]`)?.focus());
  }
  /** Server field paths (`draft.lines[0].quantity` or `lines[0].quantity`) in the user's own words. */
  private localisedDetails(failure: unknown): string[] {
    const colon = (this.translate.getCurrentLang() ?? '').startsWith('zh') ? '：' : ': ';
    return apiValidationDetails(failure).map(detail => {
      const field = detail.slice(0, Math.max(detail.indexOf(': '), 0)).replace(/^draft\./, '');
      const index = /^lines\[(\d+)\]/.exec(field);
      const lineNo = index ? this.draft.lines[Number(index[1])]?.lineNo : undefined;
      const name = field.includes('tracking') ? 'sd.delivery.tracking' : field.endsWith('quantity') ? 'sd.delivery.quantity'
        : field.includes('warehouseId') ? 'sd.deliveryCreate.warehouse' : field.includes('locationId') ? 'sd.deliveryCreate.location'
          : field === 'businessDate' ? 'sd.deliveryCreate.date' : field === 'version' ? 'sd.delivery.version' : 'sd.deliveryCreate.lines';
      const prefix = lineNo != null ? `${this.translate.instant('sd.deliveryCreate.line', { number: lineNo })} · ` : '';
      return `${prefix}${this.translate.instant(name)}${colon}${this.translate.instant('sd.deliveryCreate.serverField')}`;
    });
  }
}
