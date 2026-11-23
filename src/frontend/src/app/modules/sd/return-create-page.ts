import { HttpErrorResponse } from '@angular/common/http';
import { ChangeDetectionStrategy, Component, DestroyRef, ElementRef, HostListener, Injector, afterNextRender, effect, inject, signal,
  untracked } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import { TimeoutError } from 'rxjs';
import { NzAlertModule } from 'ng-zorro-antd/alert';
import { NzButtonModule } from 'ng-zorro-antd/button';
import { PermissionService } from '../../core/auth/permission.service';
import { CompanyContextService } from '../../core/company/company-context.service';
import { UNKNOWN_ERROR_KEY, apiErrorKey, apiErrorTraceId } from '../../core/http/api-error';
import { ApiErrorDisplay } from '../../shared/api-error-display';
import { formatDecimalText } from '../../shared/decimal-text';
import { ErpDialogService } from '../../shared/dialogs/dialog.service';
import { referenceLabel } from './delivery.service';
import { invoiceMoney } from './invoice.service';
import { RETURN_REMARK_MAX, ReturnDraftForm, ReturnLineForm, ReturnProblems, billedKey, draftFromSource, earliestReturnDate,
  lineTotal, matchesReturnReceipt, mergeDraft, returnRefusal, returnRequest, sourceLine, totalKey, unbilledKey,
  validateReturnDraft } from './return-form';
import { SalesReturnDetail, SalesReturnDraftRequest, SalesReturnService, SalesReturnSourceLine, SalesReturnSourceOrder,
  SalesReturnSourceOrderSummary, newReturnRequestKey } from './return.service';

/** Error codes the create form explains in the user's language (others fall back to the generic message). */
export const RETURN_CREATE_ERROR_CODES = ['SAL.RETURN.SOURCE_NOT_FOUND', 'SAL.RETURN.SOURCE_MIXED',
  'SAL.RETURN.ORDER_NOT_APPROVED', 'SAL.RETURN.DELIVERY_NOT_POSTED', 'SAL.RETURN.INVOICE_LINE_INVALID',
  'SAL.RETURN.DATE_BEFORE_DELIVERY', 'SAL.RETURN.DATE_BEFORE_INVOICE', 'SAL.RETURN.TRACKING_UNSUPPORTED',
  'SAL.RETURN.RETURN_EXCEEDED', 'SAL.RETURN.UNBILLED_EXCEEDED', 'SAL.RETURN.BILLED_EXCEEDED',
  'SAL.RETURN.REQUEST_CONFLICT', 'SAL.RETURN.SOURCE_INVALID', 'MD.WAREHOUSE.NOT_FOUND', 'MD.WAREHOUSE.INACTIVE',
  'MD.STORAGE_LOCATION.NOT_FOUND', 'MD.STORAGE_LOCATION.INACTIVE', 'COMMON.PERMISSION.DENIED'] as const;
const SOURCE_ERROR_CODES = ['SAL.RETURN.SOURCE_NOT_FOUND', 'SAL.RETURN.ORDER_NOT_APPROVED', 'COMMON.PERMISSION.DENIED'];
/** Refusals where re-reading the advisory balances helps. */
const RELOADABLE = ['RETURN_EXCEEDED', 'UNBILLED_EXCEEDED', 'BILLED_EXCEEDED', 'DATE_BEFORE_DELIVERY', 'DATE_BEFORE_INVOICE',
  'DELIVERY_NOT_POSTED', 'SOURCE_NOT_FOUND', 'INVOICE_LINE_INVALID'].map(code => `errors.SAL.RETURN.${code}`);

const blank = (): ReturnDraftForm => ({ orderId: null, businessDate: '', remark: '', lines: [] });

/**
 * Sales Return Create Interface 2.17 (REQ_F4112.7.1, WU-SD-008b3a): pick an Approved order with returnable Posted
 * delivery lines, choose lines, split each returned quantity into an uninvoiced part and parts credited against Posted
 * invoice lines, and save a Draft. Nothing is received or credited until the return is submitted and finally approved.
 * The request key is kept while the outcome is unknown, so resending cannot create twice (ADR-185).
 */
@Component({
  selector: 'erp-sales-return-create-page',
  imports: [FormsModule, RouterLink, TranslatePipe, NzAlertModule, NzButtonModule, ApiErrorDisplay],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <section class="create" data-testid="sd-return-create">
      <header><h1>{{ 'sd.returnCreate.title' | translate }}</h1>
        @if (permissions.has('SAL.RETURN.VIEW')) { <a routerLink="/sal/returns" data-testid="sd-return-create-back">{{ 'sd.returnCreate.back' | translate }}</a> }</header>
      <nz-alert nzType="info" nzShowIcon [nzMessage]="'sd.returnCreate.intro' | translate" data-testid="sd-return-create-intro" />
      @if (saved(); as value) { <nz-alert nzType="success" nzShowIcon data-testid="sd-return-create-success"
          [nzMessage]="(replayed() ? 'sd.returnCreate.replayed' : 'sd.returnCreate.saved') | translate: { number: value.documentNo }" />
        <p data-testid="sd-return-create-receipt" translate="no">{{ value.documentNo }} · {{ value.orderDocumentNo }} · {{ value.currencyCode }} {{ money(value.originalGross) }}</p>
        <div class="actions"><button nz-button nzType="primary" type="button" (click)="reset()" data-testid="sd-return-create-another">{{ 'sd.returnCreate.another' | translate }}</button></div> }
      @if (uncertain()) { <nz-alert nzType="warning" nzShowIcon role="alert" data-testid="sd-return-create-uncertain"
          [nzMessage]="'sd.returnCreate.uncertain' | translate" [nzDescription]="'sd.returnCreate.uncertainHint' | translate" />
        @if (traceId()) { <p class="hint" data-testid="sd-return-create-uncertain-trace">{{ 'sd.returnCreate.traceId' | translate: { id: traceId() } }}</p> }
        <div class="actions"><button nz-button nzType="primary" type="button" [nzLoading]="saving()" (click)="resend()" data-testid="sd-return-create-resend">{{ 'sd.returnCreate.resend' | translate }}</button>
          @if (permissions.has('SAL.RETURN.VIEW')) { <a nz-button routerLink="/sal/returns" data-testid="sd-return-create-check">{{ 'sd.returnCreate.checkList' | translate }}</a> }</div> }
      @if (error()) { <erp-api-error-display data-testid="sd-return-create-error" [messageKey]="error()!" [traceId]="traceId()" />
        @if (canReload()) { <button nz-button type="button" (click)="reloadBalance()" data-testid="sd-return-create-reload">{{ 'sd.returnCreate.reload' | translate }}</button> } }
      @if (!saved()) {
        <div class="field"><span id="sd-return-create-order-label">{{ 'sd.returnCreate.order' | translate }} <span aria-hidden="true">*</span></span><div class="picker">
          <span data-testid="sd-return-create-order" translate="no">{{ source() ? source()!.documentNo + ' · ' + source()!.customerCode + ' · ' + source()!.customerName : ('sd.returnCreate.noOrder' | translate) }}</span>
          <button nz-button type="button" (click)="pickOrder()" [disabled]="locked()" aria-describedby="sd-return-create-order-label" data-testid="sd-return-create-order-pick">{{ 'sd.returnCreate.chooseOrder' | translate }}</button></div>
          @if (problem('orderId')) { <small role="alert">{{ 'sd.returnCreate.validation.order' | translate }}</small> }</div>
        @if (orderLoading()) { <p role="status">{{ 'sd.returnCreate.loadingOrder' | translate }}</p> }
        @if (orderError()) { <erp-api-error-display [messageKey]="orderError()!" [traceId]="orderTraceId()" data-testid="sd-return-create-order-error" />
          @if (draft.orderId) { <button nz-button type="button" (click)="reloadBalance()" data-testid="sd-return-create-order-retry">{{ 'sd.returnCreate.retry' | translate }}</button> } }
        @if (source(); as order) {
          <dl class="facts" data-testid="sd-return-create-source">
            <div><dt>{{ 'sd.return.customer' | translate }}</dt><dd>{{ order.customerCode }} · {{ order.customerName }}</dd></div>
            <div><dt>{{ 'sd.return.currency' | translate }}</dt><dd>{{ order.currencyCode }} → {{ order.baseCurrencyCode }}</dd></div>
            <div><dt>{{ 'sd.return.exchangeRate' | translate }}</dt><dd data-testid="sd-return-create-rate">{{ decimal(order.exchangeRate) }}</dd></div>
          </dl>
          @if (!order.lines.length && !orderLoading()) { <nz-alert nzType="warning" nzShowIcon data-testid="sd-return-create-no-lines" [nzMessage]="'sd.returnCreate.noOpenLines' | translate" /> }
          @if (order.lines.length) { <form (ngSubmit)="save()" novalidate data-testid="sd-return-create-form">
            <div class="row">
              <label class="field date"><span>{{ 'sd.return.date' | translate }} <span aria-hidden="true">*</span></span>
                <input type="date" name="businessDate" autocomplete="off" [(ngModel)]="draft.businessDate" (ngModelChange)="revalidate()" [disabled]="locked()"
                  [attr.aria-invalid]="problem('businessDate') ? 'true' : null" data-testid="sd-return-create-date" />
                @if (earliestDate()) { <small class="hint" data-testid="sd-return-create-date-hint">{{ 'sd.returnCreate.dateHint' | translate: { date: earliestDate() } }}</small> }
                @if (problem('businessDate'); as reason) { <small role="alert" data-testid="sd-return-create-date-error">{{ ('sd.returnCreate.validation.' + reason) | translate: { date: earliestDate() } }}</small> }</label>
              <label class="field remark"><span>{{ 'sd.return.remark' | translate }}</span>
                <textarea name="remark" rows="2" autocomplete="off" [maxlength]="remarkMax" [(ngModel)]="draft.remark" (ngModelChange)="revalidate()" [disabled]="locked()"
                  [attr.aria-invalid]="problem('remark') ? 'true' : null" data-testid="sd-return-create-remark"></textarea>
                @if (problem('remark')) { <small role="alert">{{ 'sd.returnCreate.validation.remarkLength' | translate: { max: remarkMax } }}</small> }</label>
            </div>
            <p class="hint">{{ 'sd.returnCreate.balanceHint' | translate }}</p>
            <div class="scroll"><table class="lines" data-testid="sd-return-create-lines"><caption class="sr-only">{{ 'sd.return.lines' | translate }}</caption>
              <thead><tr><th scope="col"><span class="sr-only">{{ 'sd.returnCreate.select' | translate }}</span></th>
                <th scope="col">{{ 'sd.return.delivery' | translate }}</th><th scope="col">{{ 'sd.return.orderLine' | translate }}</th>
                <th scope="col">{{ 'sd.return.item' | translate }}</th><th scope="col">{{ 'sd.return.destination' | translate }}</th>
                <th scope="col" class="number">{{ 'sd.returnCreate.returnable' | translate }}</th>
                <th scope="col">{{ 'sd.returnCreate.split' | translate }}</th><th scope="col" class="number">{{ 'sd.return.quantity' | translate }}</th></tr></thead>
              <tbody>@for (line of draft.lines; track line.deliveryLineId) { @let facts = factsOf(line.deliveryLineId);
                <tr [class.failed]="lineError()?.deliveryLineId === line.deliveryLineId" [attr.data-testid]="'sd-return-create-line-' + line.deliveryLineId">
                  <td><input type="checkbox" [name]="'selected-' + line.deliveryLineId" [(ngModel)]="line.selected" (ngModelChange)="revalidate()"
                    [disabled]="locked() || !!facts?.trackingUnsupported"
                    [attr.aria-label]="'sd.returnCreate.selectLine' | translate: { delivery: facts?.deliveryDocumentNo, line: facts?.deliveryLineNo }"
                    [attr.aria-describedby]="facts?.trackingUnsupported ? 'sd-return-create-tracking-' + line.deliveryLineId : null"
                    [attr.data-testid]="'sd-return-create-select-' + line.deliveryLineId" /></td>
                  <td class="nowrap"><span translate="no">{{ facts?.deliveryDocumentNo }} #{{ facts?.deliveryLineNo }}</span><span class="sub">{{ facts?.deliveryDate }}</span></td>
                  <td>{{ facts?.orderLineNo }}</td>
                  <td><span translate="no">{{ facts?.itemCode }}</span> · {{ facts?.itemName }}
                    @if (facts?.trackingUnsupported) { <small class="hint" [id]="'sd-return-create-tracking-' + line.deliveryLineId" [attr.data-testid]="'sd-return-create-tracking-' + line.deliveryLineId">{{ 'sd.returnCreate.trackingUnsupported' | translate }}</small> }</td>
                  <td [attr.data-testid]="'sd-return-create-destination-' + line.deliveryLineId">@if (facts) { <span class="sub">{{ destination(facts) }}</span>
                    @if (facts.warehouseActive === false || facts.locationActive === false || !facts.warehouseCode) { <small class="warning" [attr.data-testid]="'sd-return-create-destination-warning-' + line.deliveryLineId">{{ 'sd.returnCreate.destinationUnavailable' | translate }}</small> } }</td>
                  <td class="number" [attr.data-testid]="'sd-return-create-returnable-' + line.deliveryLineId">{{ facts ? decimal(facts.returnableQuantity) : '' }}</td>
                  <td class="split">
                    <label class="portion"><span>{{ 'sd.returnCreate.unbilled' | translate: { remaining: facts ? decimal(facts.unbilledRemainingQuantity) : '' } }}</span>
                      <input inputmode="decimal" autocomplete="off" spellcheck="false" placeholder="0" [name]="'unbilled-' + line.deliveryLineId" [(ngModel)]="line.unbilledQuantity" (ngModelChange)="revalidate()"
                        [disabled]="locked() || !line.selected" [attr.aria-invalid]="problem(unbilledKey(line.deliveryLineId)) ? 'true' : null"
                        [attr.data-testid]="'sd-return-create-unbilled-' + line.deliveryLineId" /></label>
                    @if (line.selected && problem(unbilledKey(line.deliveryLineId)); as reason) { <small role="alert" [attr.data-testid]="'sd-return-create-unbilled-error-' + line.deliveryLineId">{{ ('sd.returnCreate.validation.' + reason) | translate: { remaining: facts ? decimal(facts.unbilledRemainingQuantity) : '' } }}</small> }
                    @for (portion of line.billed; track portion.invoiceLineId) { @let invoice = invoiceOf(facts, portion.invoiceLineId);
                      <label class="portion"><span>{{ 'sd.returnCreate.billedFrom' | translate: { invoice: invoice?.invoiceDocumentNo, date: invoice?.invoiceDate, remaining: invoice ? decimal(invoice.remainingQuantity) : '' } }}</span>
                        <input inputmode="decimal" autocomplete="off" spellcheck="false" placeholder="0" [name]="'billed-' + line.deliveryLineId + '-' + portion.invoiceLineId" [(ngModel)]="portion.quantity" (ngModelChange)="revalidate()"
                          [disabled]="locked() || !line.selected" [attr.aria-invalid]="problem(billedKey(line.deliveryLineId, portion.invoiceLineId)) ? 'true' : null"
                          [attr.data-testid]="'sd-return-create-billed-' + line.deliveryLineId + '-' + portion.invoiceLineId" /></label>
                      @if (line.selected && problem(billedKey(line.deliveryLineId, portion.invoiceLineId)); as reason) { <small role="alert" [attr.data-testid]="'sd-return-create-billed-error-' + line.deliveryLineId + '-' + portion.invoiceLineId">{{ ('sd.returnCreate.validation.' + reason) | translate: { remaining: invoice ? decimal(invoice.remainingQuantity) : '' } }}</small> } }
                    @if (lineError()?.deliveryLineId === line.deliveryLineId) { <small role="alert" [attr.data-testid]="'sd-return-create-line-error-' + line.deliveryLineId">{{ lineError()!.key | translate: { remaining: lineError()!.remaining } }}</small> }</td>
                  <td class="number" [attr.data-testid]="'sd-return-create-total-' + line.deliveryLineId">{{ line.selected ? total(line) : '' }}
                    @if (line.selected && problem(totalKey(line.deliveryLineId)); as reason) { <small role="alert" [attr.data-testid]="'sd-return-create-total-error-' + line.deliveryLineId">{{ ('sd.returnCreate.validation.' + reason) | translate: { remaining: facts ? decimal(facts.returnableQuantity) : '' } }}</small> }</td>
                </tr> }</tbody></table></div>
            <p role="status" data-testid="sd-return-create-count">{{ 'sd.returnCreate.selectedCount' | translate: { count: selectedCount(), total: draft.lines.length } }}</p>
            @if (problem('lines'); as reason) { <p role="alert" data-testid="sd-return-create-lines-error">{{ ('sd.returnCreate.validation.' + reason) | translate }}</p> }
            @if (submitted() && count()) { <p role="alert" data-testid="sd-return-create-errors">{{ 'sd.returnCreate.errors' | translate: { count: count() } }}</p> }
            <div class="actions"><button nz-button nzType="primary" type="submit" [nzLoading]="saving()" [disabled]="locked()" data-testid="sd-return-create-submit">{{ 'sd.returnCreate.submit' | translate }}</button></div>
          </form> }
        }
      }
    </section>`,
  styles: `.create{display:grid;gap:16px;min-width:0;color:var(--erp-color-text)}header{display:flex;justify-content:space-between;gap:12px;align-items:center;flex-wrap:wrap}h1{margin:0}form{display:grid;gap:12px;min-width:0}.row{display:flex;gap:16px;flex-wrap:wrap;align-items:start}.field{display:grid;gap:4px;min-width:0}.field.date{width:240px}.field.remark{flex:1 1 320px}.picker,.actions{display:flex;gap:8px;align-items:center;flex-wrap:wrap}.picker span,.facts dd{overflow-wrap:anywhere}input:not([type=checkbox]),textarea{width:100%;min-height:32px;padding:4px 8px;background:var(--erp-color-bg-container);color:var(--erp-color-text);border:1px solid var(--erp-color-border);border-radius:4px}input[type=checkbox]{width:16px;height:16px}input:disabled,textarea:disabled{opacity:.55;cursor:not-allowed}.facts{display:grid;grid-template-columns:repeat(auto-fill,minmax(180px,1fr));gap:8px 16px;margin:0}.facts dt{color:var(--erp-color-text-secondary)}.facts dd{margin:0}.scroll{overflow-x:auto;min-width:0}table.lines{width:100%;min-width:1080px;border-collapse:collapse}.lines th,.lines td{padding:8px;border-bottom:1px solid var(--erp-color-border);text-align:left;vertical-align:top}.lines th,.lines td.nowrap,.lines td.number{white-space:nowrap}.split{min-width:260px;display:grid;gap:6px}.portion{display:grid;gap:2px}.portion span,.sub{display:block;color:var(--erp-color-text-secondary)}tr.failed td{background:var(--erp-color-error-bg,transparent)}.number{text-align:right!important;font-variant-numeric:tabular-nums}small,p[role=alert]{color:var(--erp-color-error-text)}small{display:block;margin-top:4px;white-space:normal}small.hint,.hint{color:var(--erp-color-text-secondary);margin:0}.sr-only{position:absolute;width:1px;height:1px;overflow:hidden;clip:rect(0 0 0 0);white-space:nowrap}p{margin:0}button:focus-visible,input:focus-visible,textarea:focus-visible,a:focus-visible{outline:2px solid var(--erp-color-primary);outline-offset:2px}`,
})
export class SalesReturnCreatePage {
  private readonly returns = inject(SalesReturnService);
  private readonly dialogs = inject(ErpDialogService);
  private readonly company = inject(CompanyContextService);
  private readonly router = inject(Router);
  readonly permissions = inject(PermissionService);
  private readonly translate = inject(TranslateService);
  private readonly destroy = inject(DestroyRef);
  private readonly host: ElementRef<HTMLElement> = inject(ElementRef);
  private readonly injector = inject(Injector);
  readonly remarkMax = RETURN_REMARK_MAX;
  readonly unbilledKey = unbilledKey; readonly billedKey = billedKey; readonly totalKey = totalKey;
  draft = blank();
  readonly source = signal<SalesReturnSourceOrder | null>(null);
  readonly problems = signal<ReturnProblems>({}); readonly submitted = signal(false);
  readonly orderLoading = signal(false); readonly orderError = signal<string | null>(null); readonly orderTraceId = signal<string | null>(null);
  readonly saving = signal(false); readonly saved = signal<SalesReturnDetail | null>(null); readonly replayed = signal(false);
  readonly uncertain = signal(false); readonly error = signal<string | null>(null); readonly traceId = signal<string | null>(null);
  readonly lineError = signal<{ deliveryLineId: number; key: string; remaining: string } | null>(null);
  readonly canReload = signal(false);
  /** Bumped by every redraw-relevant change of `draft` (a plain object bound with ngModel). */
  private readonly tick = signal(0);
  private epoch = 0;
  private requestKey = newReturnRequestKey();
  /** The exact body of the last POST; resent unchanged while the outcome is unknown. */
  private sent: SalesReturnDraftRequest | null = null;
  constructor() {
    this.destroy.onDestroy(() => this.epoch++);
    effect(() => { this.company.current(); untracked(() => this.reset()); });
  }
  canLeave(): boolean { return !this.dirty() || window.confirm(this.translate.instant('sd.returnCreate.unsaved')); }
  @HostListener('window:beforeunload', ['$event']) beforeUnload(event: BeforeUnloadEvent): void { if (this.dirty()) event.preventDefault(); }
  private dirty(): boolean { return !this.saved() && (this.draft.orderId != null || this.uncertain()); }
  locked(): boolean { return this.saving() || this.uncertain() || !!this.saved() || this.orderLoading(); }
  problem(name: string) { return this.problems()[name]; }
  count(): number { return Object.keys(this.problems()).length; }
  decimal(value: string): string { return formatDecimalText(String(value), this.translate.getCurrentLang() || undefined); }
  money(value: string): string { return invoiceMoney(value); }
  factsOf(deliveryLineId: number): SalesReturnSourceLine | undefined { return sourceLine(this.source(), deliveryLineId); }
  invoiceOf(facts: SalesReturnSourceLine | undefined, invoiceLineId: number) {
    return facts?.invoiceLines.find(invoice => invoice.invoiceLineId === invoiceLineId);
  }
  /** The default destination (the delivery's warehouse / location); a soft-deleted one shows its ID. */
  destination(facts: SalesReturnSourceLine): string {
    const warehouse = referenceLabel(facts.warehouseCode, facts.warehouseName)
      || this.translate.instant('sd.return.deletedWarehouse', { id: facts.warehouseId });
    const inactive = this.translate.instant('sd.return.inactiveReference');
    let text = warehouse + (facts.warehouseActive === false ? inactive : '');
    if (facts.locationId != null)
      text += ' / ' + (referenceLabel(facts.locationCode, facts.locationName)
        || this.translate.instant('sd.return.deletedLocation', { id: facts.locationId })) + (facts.locationActive === false ? inactive : '');
    return text;
  }
  total(line: ReturnLineForm): string { this.tick(); const value = lineTotal(line); return value == null ? '—' : this.decimal(value); }
  earliestDate(): string | null { this.tick(); return earliestReturnDate(this.draft, this.source()); }
  selectedCount(): number { this.tick(); return this.draft.lines.filter(line => line.selected).length; }
  /** Inline messages follow edits once the user has tried to save. */
  revalidate(): void {
    this.tick.update(value => value + 1);
    if (this.submitted()) this.problems.set(validateReturnDraft(this.draft, this.source()));
  }
  reset(): void {
    this.epoch++; this.draft = blank(); this.source.set(null); this.problems.set({}); this.submitted.set(false);
    this.orderLoading.set(false); this.orderError.set(null); this.orderTraceId.set(null); this.saving.set(false);
    this.saved.set(null); this.replayed.set(false); this.uncertain.set(false); this.clearOutcome();
    this.requestKey = newReturnRequestKey(); this.sent = null; this.tick.update(value => value + 1);
  }
  private clearOutcome(): void { this.error.set(null); this.traceId.set(null); this.lineError.set(null); this.canReload.set(false); }
  pickOrder(): void {
    if (this.locked()) return;
    const epoch = this.epoch;
    this.dialogs.select<SalesReturnSourceOrderSummary>({ titleKey: 'sd.returnCreate.chooseOrder',
      descriptionKey: 'sd.returnCreate.chooseOrderHint',
      columns: [{ key: 'documentNo', labelKey: 'sd.return.order', value: row => row.documentNo },
        { key: 'customerName', labelKey: 'sd.return.customer', value: row => `${row.customerCode} · ${row.customerName}` },
        { key: 'businessDate', labelKey: 'sd.returnCreate.orderDate', value: row => row.businessDate },
        { key: 'currencyCode', labelKey: 'sd.return.currency', value: row => row.currencyCode },
        { key: 'openLineCount', labelKey: 'sd.returnCreate.openLines', value: row => String(row.openLineCount) }],
      load: query => this.returns.sourceOrders(query), rowKey: row => row.id,
      errorCodes: ['COMMON.PERMISSION.DENIED'], timeoutKey: 'sd.return.timeout' }).subscribe(rows => {
      if (epoch !== this.epoch || !rows?.[0]) return;
      const { businessDate, remark } = this.draft;
      this.reset(); this.draft.businessDate = businessDate; this.draft.remark = remark; this.draft.orderId = rows[0].id;
      this.loadOrder(rows[0].id, false);
    });
  }
  /** Reread the advisory balances; keeps the entries of lines that are still returnable. */
  reloadBalance(): void { if (this.draft.orderId && !this.orderLoading() && !this.saving() && !this.uncertain()) this.loadOrder(this.draft.orderId, true); }
  private loadOrder(orderId: number, keep: boolean): void {
    const epoch = ++this.epoch;
    this.orderLoading.set(true); this.orderError.set(null); this.orderTraceId.set(null);
    this.returns.sourceOrder(orderId).subscribe({
      next: order => { if (epoch !== this.epoch) return; this.orderLoading.set(false);
        if (order?.id !== orderId || !Array.isArray(order.lines) || !order.lines.every(line => Array.isArray(line.invoiceLines))) {
          this.source.set(null); this.draft.lines = []; this.orderError.set(UNKNOWN_ERROR_KEY); return; }
        this.source.set(order);
        this.draft = keep && this.draft.lines.length ? mergeDraft(this.draft, order)
          : draftFromSource(order, this.draft.businessDate, this.draft.remark);
        if (keep) this.clearOutcome();
        this.revalidate();
      },
      error: failure => { if (epoch !== this.epoch) return; this.orderLoading.set(false);
        this.source.set(null); this.draft.lines = [];
        this.orderError.set(failure instanceof TimeoutError ? 'sd.return.timeout' : apiErrorKey(failure, SOURCE_ERROR_CODES));
        this.orderTraceId.set(apiErrorTraceId(failure)); },
    });
  }
  save(): void {
    if (this.locked()) return;
    this.submitted.set(true);
    const problems = validateReturnDraft(this.draft, this.source());
    this.problems.set(problems);
    if (Object.keys(problems).length) { this.focusFirstProblem(Object.keys(problems)[0]); return; }
    this.send(returnRequest(this.draft, this.requestKey));
  }
  /** Same key, same body: a saved return comes back as 200 without a second Draft. */
  resend(): void { if (this.sent && this.uncertain() && !this.saving()) this.send(this.sent); }
  private send(request: SalesReturnDraftRequest): void {
    const epoch = this.epoch;
    this.sent = request; this.saving.set(true); this.clearOutcome();
    this.returns.create(request).subscribe({
      next: result => { if (epoch !== this.epoch) return; this.saving.set(false);
        if (!matchesReturnReceipt(result.saved, request)) { this.uncertain.set(true); return; }
        this.uncertain.set(false); this.replayed.set(result.replayed); this.saved.set(result.saved);
        this.dialogs.success(result.replayed ? 'sd.returnCreate.replayed' : 'sd.returnCreate.saved', { number: result.saved.documentNo });
        if (this.permissions.has('SAL.RETURN.VIEW'))
          void this.router.navigate(['/sal/returns'], { queryParams: { open: result.saved.id } });
      },
      error: failure => { if (epoch !== this.epoch) return; this.saving.set(false); this.refused(failure); },
    });
  }
  private refused(failure: unknown): void {
    const status = failure instanceof HttpErrorResponse ? failure.status : null;
    const key = apiErrorKey(failure, RETURN_CREATE_ERROR_CODES);
    this.traceId.set(apiErrorTraceId(failure));
    // A 5xx without a code the server raises before commit, a network failure or a timeout leaves the outcome unknown.
    if (failure instanceof TimeoutError || status === 0 || (status != null && status >= 500 && key === UNKNOWN_ERROR_KEY)) {
      this.uncertain.set(true); return; }
    // Definite refusal: nothing was saved, so the next attempt (possibly with other content) uses a new key.
    this.uncertain.set(false); this.requestKey = newReturnRequestKey(); this.sent = null;
    const { lineNo, invoiceLineId, remaining } = returnRefusal(failure);
    const dateKey = key === 'errors.SAL.RETURN.DATE_BEFORE_DELIVERY' || key === 'errors.SAL.RETURN.DATE_BEFORE_INVOICE';
    const line = lineNo != null ? this.draft.lines.filter(candidate => candidate.selected)[lineNo - 1] : undefined;
    if (line && !dateKey) {
      const exceeded = remaining != null && ['errors.SAL.RETURN.RETURN_EXCEEDED', 'errors.SAL.RETURN.UNBILLED_EXCEEDED',
        'errors.SAL.RETURN.BILLED_EXCEEDED'].includes(key);
      this.lineError.set({ deliveryLineId: line.deliveryLineId, remaining: remaining != null ? this.decimal(remaining) : '',
        key: exceeded ? 'sd.returnCreate.lineBalance' : key });
      const portion = invoiceLineId != null && line.billed.some(candidate => candidate.invoiceLineId === invoiceLineId);
      this.focus(portion ? `sd-return-create-billed-${line.deliveryLineId}-${invoiceLineId}`
        : `sd-return-create-unbilled-${line.deliveryLineId}`);
    } else if (dateKey) this.focus('sd-return-create-date');
    this.canReload.set(RELOADABLE.includes(key));
    this.error.set(status === 422 && key === UNKNOWN_ERROR_KEY ? 'sd.returnCreate.rejected' : key);
  }
  private focusFirstProblem(name: string): void {
    const target = name === 'orderId' ? 'sd-return-create-order-pick' : name === 'businessDate' ? 'sd-return-create-date'
      : name === 'remark' ? 'sd-return-create-remark' : name === 'lines' ? `sd-return-create-select-${this.firstSelectable()}`
      : name.startsWith('line-') ? `sd-return-create-unbilled-${name.slice('line-'.length)}`
      : `sd-return-create-${name.startsWith('unbilled-') ? 'unbilled-' + name.slice('unbilled-'.length) : 'billed-' + name.slice('billed-'.length)}`;
    this.focus(target);
  }
  private firstSelectable(): number | undefined {
    return this.draft.lines.find(line => !this.factsOf(line.deliveryLineId)?.trackingUnsupported)?.deliveryLineId;
  }
  /**
   * After a refusal the inputs are re-enabled by the next render, and ngModel applies `[disabled]` in a microtask after
   * that render; focusing earlier hits a still-disabled input and is ignored (found by SD-INV-E2E-004).
   */
  private focus(testId: string): void {
    afterNextRender(() => queueMicrotask(() =>
      this.host.nativeElement.querySelector<HTMLElement>(`[data-testid="${testId}"]`)?.focus()), { injector: this.injector });
  }
}
