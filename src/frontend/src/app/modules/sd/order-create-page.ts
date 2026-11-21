import { ChangeDetectionStrategy, Component, DestroyRef, ElementRef, HostListener, effect, inject, signal, untracked } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import { TimeoutError } from 'rxjs';
import { NzAlertModule } from 'ng-zorro-antd/alert';
import { NzButtonModule } from 'ng-zorro-antd/button';
import { CompanyContextService } from '../../core/company/company-context.service';
import { PermissionService } from '../../core/auth/permission.service';
import { UNKNOWN_ERROR_KEY, apiErrorKey, apiErrorTraceId, apiValidationDetails } from '../../core/http/api-error';
import { ApiErrorDisplay } from '../../shared/api-error-display';
import { ErpDialogService } from '../../shared/dialogs/dialog.service';
import { SalesDocumentOption, SalesDocumentOptionsService, SalesOutputTaxOption } from './document-options.service';
import { compareDecimalText, DocumentPricingFailure, LinePricing, linePricingNeedsOverride, pricingFailureOf, quoteFailureState, trimDecimalText } from './document-pricing';
import { matchesOrderReceipt, orderEditDraft, OrderDraftForm, OrderDraftIssue, OrderDraftLineForm, validateOrderDraft } from './order-draft-form';
import { SalesPriceRuleService } from './price-rule.service';
import { SalesOrderDetail, SalesOrderEditableDetail, SalesOrderService } from './order.service';
import { SalesQuotationService, SalesQuotationSummary } from './quotation.service';

const DATE = /^\d{4}-\d{2}-\d{2}$/;
const NO_PRICING: LinePricing = { kind: 'none' };
function emptyLine(lineNo: number): OrderDraftLineForm {
  return { lineNo, itemId: null, quantity: '1', unitPrice: '0', discountRate: '0', taxRateId: null,
    pricingRuleId: null, pricingRuleVersion: null, overrideReason: '' };
}
function blank(): OrderDraftForm {
  return { customerId: null, businessDate: '', currencyCode: 'TWD', remark: '', lines: [emptyLine(1)] };
}

@Component({
  selector: 'erp-sales-order-create-page',
  imports: [FormsModule, RouterLink, TranslatePipe, NzAlertModule, NzButtonModule, ApiErrorDisplay],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <section class="create" data-testid="sd-order-create">
      <header><h1>{{ (editId ? 'sd.orderEdit.title' : 'sd.orderCreate.title') | translate }}</h1>@if (permissions.has('SAL.ORDER.VIEW')) { <a routerLink="/sal/orders">{{ 'sd.orderCreate.back' | translate }}</a> }</header>
      <p>{{ (editId ? 'sd.orderEdit.intro' : 'sd.orderCreate.intro') | translate }}</p>
      @if (loading()) { <p role="status">{{ 'sd.orderEdit.loading' | translate }}</p> }
      @if (loadError()) { <erp-api-error-display [messageKey]="loadError()!" [traceId]="traceId()" data-testid="sd-order-edit-load-error" />
        <button nz-button type="button" (click)="loadSaved()" data-testid="sd-order-edit-retry">{{ 'sd.order.retry' | translate }}</button> }
      @if (editId && documentNo()) { <p data-testid="sd-order-edit-identity">{{ documentNo() }} · {{ 'sd.order.version' | translate }} {{ version() }}</p> }
      @if (blocked()) { <nz-alert nzType="warning" nzShowIcon data-testid="sd-order-edit-blocked" [nzMessage]="blockedReason() | translate" /> }
      @if (conflict()) { <nz-alert nzType="warning" nzShowIcon data-testid="sd-order-edit-conflict" [nzMessage]="'sd.orderEdit.conflict' | translate" />
        <button nz-button type="button" (click)="loadSaved()" data-testid="sd-order-edit-refresh">{{ 'sd.orderEdit.refresh' | translate }}</button> }
      @if (editUnavailable()) { <nz-alert nzType="warning" nzShowIcon data-testid="sd-order-edit-unavailable" [nzMessage]="'sd.orderEdit.unavailable' | translate" />
        <button nz-button type="button" (click)="loadSaved()" data-testid="sd-order-edit-refresh">{{ 'sd.orderEdit.refresh' | translate }}</button> }
      @if (noChange()) { <nz-alert nzType="warning" nzShowIcon data-testid="sd-order-edit-no-change" [nzMessage]="'sd.orderEdit.noChange' | translate" /> }
      @if (saved(); as value) { <nz-alert nzType="success" nzShowIcon data-testid="sd-order-create-success"
        [nzMessage]="(editId ? 'sd.orderEdit.saved' : 'sd.orderCreate.saved') | translate: { number: value.documentNo }" />
        <p data-testid="sd-order-create-receipt">{{ value.documentNo }} · {{ value.customerName }} · {{ value.lines.length }} {{ 'sd.orderCreate.savedLines' | translate }}</p>
        @if (!editId) { <button nz-button type="button" (click)="reset()">{{ 'sd.orderCreate.another' | translate }}</button> } }
      @if (uncertain()) { <nz-alert nzType="warning" nzShowIcon data-testid="sd-order-create-uncertain"
        [nzMessage]="(editId ? 'sd.orderEdit.unknown' : permissions.has('SAL.ORDER.VIEW') ? 'sd.orderCreate.unknown' : 'sd.orderCreate.unknownCreateOnly') | translate" />
        @if (editId) { <button nz-button type="button" (click)="loadSaved()" data-testid="sd-order-edit-refresh">{{ 'sd.orderEdit.refresh' | translate }}</button> } }
      @if (error()) { <erp-api-error-display data-testid="sd-order-create-error" [messageKey]="error()!" [traceId]="traceId()" [details]="errorDetails()" /> }
      @if (!editId || (documentNo() && !blocked() && !loadError())) { <form (ngSubmit)="save()" novalidate data-testid="sd-order-create-form">
        <div class="field"><span>{{ 'sd.orderCreate.customer' | translate }} <span aria-hidden="true">*</span></span><div class="picker">
          <span data-testid="sd-order-create-customer">{{ customerLabel() || ('sd.orderCreate.none' | translate) }}</span>
          <button nz-button type="button" (click)="pickCustomer()" [disabled]="locked()" data-testid="sd-order-create-customer-pick">{{ 'sd.orderCreate.chooseCustomer' | translate }}</button></div>
          @if (issue('customerId')) { <small role="alert">{{ 'sd.quotationCreate.validation.customerId' | translate }}</small> }</div>
        <label class="field"><span>{{ 'sd.orderCreate.date' | translate }} <span aria-hidden="true">*</span></span><input type="date" name="businessDate" autocomplete="off" [(ngModel)]="draft.businessDate" (ngModelChange)="dateChanged()" [disabled]="locked()" [attr.aria-invalid]="issue('businessDate') ? 'true' : null" data-testid="sd-order-create-date" />
          @if (issue('businessDate')) { <small role="alert">{{ 'sd.quotationCreate.validation.businessDate' | translate }}</small> }</label>
        <label class="field">{{ 'sd.orderCreate.currency' | translate }}<input name="currency" maxlength="3" autocomplete="off" [(ngModel)]="draft.currencyCode" (ngModelChange)="currencyChanged()" [disabled]="locked()" data-testid="sd-order-create-currency" />
          @if (issue('currencyCode')) { <small role="alert">{{ 'sd.quotationCreate.validation.currencyCode' | translate }}</small> }</label>
        <label class="field wide">{{ 'sd.orderCreate.remark' | translate }}<textarea name="remark" maxlength="501" autocomplete="off" [(ngModel)]="draft.remark" [disabled]="locked()" data-testid="sd-order-create-remark"></textarea>
          @if (issue('remark')) { <small role="alert">{{ 'sd.quotationCreate.validation.remark' | translate }}</small> }</label>
        <section class="wide"><h2>{{ 'sd.orderCreate.lines' | translate }}</h2>
          @for (line of draft.lines; track $index; let i = $index) { <fieldset><legend>{{ 'sd.quotationCreate.line' | translate: { number: i + 1 } }}</legend><div class="line">
            <div class="field"><span>{{ 'sd.orderCreate.item' | translate }} <span aria-hidden="true">*</span></span><div class="picker"><span>{{ itemLabels().get(i) || ('sd.quotationCreate.noItem' | translate) }}</span>
              <button nz-button type="button" (click)="pickItem(i)" [disabled]="locked()" [attr.data-testid]="'sd-order-create-item-pick-' + i">{{ 'sd.quotationCreate.chooseItem' | translate }}</button></div>
              @if (issue(field(i, 'itemId'))) { <small role="alert">{{ 'sd.quotationCreate.validation.itemId' | translate }}</small> }</div>
            <label class="field">{{ 'sd.orderCreate.quantity' | translate }}<input inputmode="decimal" autocomplete="off" [name]="'quantity-' + i" [(ngModel)]="line.quantity" [disabled]="locked()" [attr.data-testid]="'sd-order-create-quantity-' + i" />
              @if (issue(field(i, 'quantity'))) { <small role="alert">{{ 'sd.quotationCreate.validation.quantity' | translate }}</small> }</label>
            <label class="field">{{ 'sd.orderCreate.price' | translate }}<input inputmode="decimal" autocomplete="off" [name]="'price-' + i" [(ngModel)]="line.unitPrice" (ngModelChange)="revalidate()" [disabled]="locked()" [attr.data-testid]="'sd-order-create-price-' + i" />
              @if (issue(field(i, 'unitPrice'))) { <small role="alert">{{ 'sd.quotationCreate.validation.unitPrice' | translate }}</small> }</label>
            <label class="field">{{ 'sd.orderCreate.discount' | translate }}<input inputmode="decimal" autocomplete="off" [name]="'discount-' + i" [(ngModel)]="line.discountRate" (ngModelChange)="revalidate()" [disabled]="locked()" [attr.data-testid]="'sd-order-create-discount-' + i" />
              @if (issue(field(i, 'discountRate'))) { <small role="alert">{{ 'sd.quotationCreate.validation.discountRate' | translate }}</small> }</label>
            <div class="field"><span>{{ 'sd.orderCreate.tax' | translate }} <span aria-hidden="true">*</span></span><div class="picker"><span>{{ taxLabels().get(i) || ('sd.quotationCreate.noTaxRate' | translate) }}</span>
              <button nz-button type="button" (click)="pickTax(i)" [disabled]="locked() || !taxDateReady()" [attr.aria-describedby]="!locked() && !taxDateReady() ? 'order-create-tax-hint-' + i : null" [attr.data-testid]="'sd-order-create-tax-pick-' + i">{{ 'sd.quotationCreate.chooseTaxRate' | translate }}</button></div>
              @if (!locked() && !taxDateReady()) { <small class="hint" [id]="'order-create-tax-hint-' + i" [attr.data-testid]="'sd-order-create-tax-hint-' + i">{{ 'sd.documentOptions.taxNeedsDate' | translate }}</small> }
              @if (issue(field(i, 'taxRateId'))) { <small role="alert">{{ 'sd.orderCreate.taxRequired' | translate }}</small> }</div>
          </div>
          <div class="pricing" [attr.data-testid]="'sd-order-pricing-' + i" aria-live="polite">
            @switch (pricingOf(line).kind) {
              @case ('loading') { <p role="status">{{ 'sd.pricing.loading' | translate }}</p> }
              @case ('rule') { <p>{{ ruleText(line) }}</p> }
              @case ('notFound') { <p>{{ 'sd.pricing.notFound' | translate }}</p> }
              @case ('legacy') { <p>{{ 'sd.pricing.legacy' | translate }}</p> }
              @case ('duplicate') { <p class="error">{{ 'sd.pricing.duplicate' | translate: { ids: duplicateIds(line) } }}</p> }
              @case ('stale') { <p class="error">{{ 'sd.pricing.stale' | translate }}</p> }
              @case ('failed') { <p class="error">{{ failedKey(line) | translate }}@if (failedTrace(line); as trace) { <span class="trace"> Trace ID: {{ trace }}</span> }</p> }
              @default { <p class="muted">{{ 'sd.pricing.none' | translate }}</p> }
            }
            @if (needsOverride(i)) { <p class="warning" [attr.data-testid]="'sd-order-override-needed-' + i">
              {{ (pricingOf(line).kind === 'rule' ? 'sd.pricing.deviates' : 'sd.pricing.overrideNeeded') | translate }}
              @if (!canOverride()) { {{ 'sd.pricing.noOverride' | translate }} }</p> }
            @if (lineError(line); as message) { <p class="error" role="alert" [attr.data-testid]="'sd-order-line-error-' + i">{{ message | translate }}</p> }
            @if ((pricingOf(line).kind === 'rule' && needsOverride(i)) || canRequote(i)) { <div class="pricing-actions">
              @if (pricingOf(line).kind === 'rule' && needsOverride(i)) { <button nz-button type="button" [disabled]="locked()"
                [attr.data-testid]="'sd-order-use-rule-' + i" (click)="useRulePrice(i)">{{ 'sd.pricing.useRule' | translate }}</button> }
              @if (canRequote(i)) { <button nz-button type="button" [disabled]="locked() || pricingOf(line).kind === 'loading'"
                [attr.data-testid]="'sd-order-requote-' + i" (click)="requote(i)">{{ 'sd.pricing.requote' | translate }}</button> }
            </div> }
            @if (showReason(i)) { <label class="field">{{ 'sd.pricing.reason' | translate }}
              <textarea [name]="'reason-' + i" autocomplete="off" rows="2" [(ngModel)]="line.overrideReason" (ngModelChange)="revalidate()"
                [disabled]="locked()" [attr.data-testid]="'sd-order-reason-' + i" [attr.aria-invalid]="issue(field(i, 'overrideReason')) ? 'true' : null"></textarea>
              @if (issue(field(i, 'overrideReason')); as problem) { <small role="alert">{{ (problem === 'tooLong' ? 'sd.pricing.reasonTooLong' : 'sd.pricing.reasonRequired') | translate }}</small> }
            </label> }
          </div><button nz-button type="button" (click)="removeLine(i)" [disabled]="locked() || draft.lines.length === 1">{{ 'sd.quotationCreate.removeLine' | translate }}</button>
          @if (draft.lines.length === 1) { <small class="hint" role="status">{{ 'sd.orderCreate.oneLineMinimum' | translate }}</small> }</fieldset> }
          @if (issue('lines')) { <small role="alert">{{ 'sd.quotationCreate.validation.lines' | translate }}</small> }
          <button nz-button type="button" (click)="addLine()" [disabled]="locked() || draft.lines.length >= 1000" data-testid="sd-order-create-add-line">{{ 'sd.quotationCreate.addLine' | translate }}</button>
          @if (draft.lines.length >= 1000) { <small class="hint" role="status" data-testid="sd-order-create-line-limit">{{ 'sd.orderCreate.lineLimit' | translate }}</small> }
        </section>
        @if (submitted() && count()) { <p class="wide" role="alert" data-testid="sd-order-create-errors">{{ 'sd.quotationCreate.errors' | translate: { count: count() } }}</p> }
        <button nz-button nzType="primary" type="submit" [nzLoading]="saving()" [disabled]="locked()" data-testid="sd-order-create-save">{{ (editId ? 'sd.orderEdit.save' : 'sd.orderCreate.save') | translate }}</button>
      </form> }
      @if (!editId) { <section class="convert"><h2>{{ 'sd.orderCreate.convert' | translate }}</h2><p>{{ 'sd.orderCreate.convertHint' | translate }}</p>
        <button nz-button type="button" (click)="pickQuotation()" [disabled]="locked()">{{ 'sd.orderCreate.chooseQuotation' | translate }}</button>
        @if (quoteLabel()) { <p data-testid="sd-order-create-quotation">{{ quoteLabel() }}</p> }
        <button nz-button nzType="primary" type="button" (click)="convert()" [disabled]="locked() || !quotationId()" data-testid="sd-order-create-convert">{{ 'sd.orderCreate.convertSave' | translate }}</button>
        @if (!quotationId() && !locked()) { <small class="hint" role="status" data-testid="sd-order-create-convert-reason">{{ 'sd.orderCreate.selectQuoteFirst' | translate }}</small> }
      </section> }
    </section>`,
  styles: `.create{display:grid;gap:16px;min-width:0;color:var(--erp-color-text)}header{display:flex;justify-content:space-between;gap:12px;align-items:center}h1,h2{margin:0}form{display:grid;grid-template-columns:repeat(2,minmax(0,1fr));gap:16px}.wide{grid-column:1/-1}.field{display:grid;gap:4px;min-width:0}.picker{display:flex;gap:8px;align-items:center;flex-wrap:wrap}.picker span{overflow-wrap:anywhere}input,textarea{width:100%;min-height:32px;padding:4px 8px;background:var(--erp-color-bg-container);color:var(--erp-color-text);border:1px solid var(--erp-color-border);border-radius:4px}fieldset{border:1px solid var(--erp-color-border);border-radius:8px;margin:12px 0;padding:16px}.line{display:grid;grid-template-columns:repeat(3,minmax(0,1fr));gap:12px;margin-bottom:12px}small{color:var(--erp-color-error-text)}small.hint{color:var(--erp-color-text-secondary);display:block;margin-top:8px}.pricing{display:grid;gap:8px;margin-bottom:12px;padding:8px 12px;border-left:3px solid var(--erp-color-border);overflow-wrap:anywhere}.pricing p{margin:0}.pricing .muted{color:var(--erp-color-text-secondary)}.pricing .error{color:var(--erp-color-error-text)}.warning{color:var(--erp-color-text);font-weight:600}.pricing-actions{display:flex;gap:8px;flex-wrap:wrap}.convert{border-top:1px solid var(--erp-color-border);padding-top:16px;display:grid;gap:12px;justify-items:start}button:focus-visible,input:focus-visible,textarea:focus-visible,a:focus-visible{outline:2px solid var(--erp-color-primary);outline-offset:2px}@media(max-width:767px){form,.line{grid-template-columns:1fr}}`,
})
export class SalesOrderCreatePage {
  private readonly orders = inject(SalesOrderService);
  private readonly quotations = inject(SalesQuotationService);
  private readonly options = inject(SalesDocumentOptionsService);
  private readonly dialogs = inject(ErpDialogService);
  private readonly company = inject(CompanyContextService);
  readonly permissions = inject(PermissionService);
  private readonly translate = inject(TranslateService);
  private readonly destroy = inject(DestroyRef);
  private readonly host: ElementRef<HTMLElement> = inject(ElementRef);
  private readonly route = inject(ActivatedRoute);
  private readonly prices = inject(SalesPriceRuleService);
  readonly editId = this.route.snapshot.paramMap.has('id') ? (Number(this.route.snapshot.paramMap.get('id')) || -1) : 0;
  draft = blank();
  readonly customerLabel = signal(''); readonly itemLabels = signal(new Map<number, string>());
  readonly taxLabels = signal(new Map<number, string>()); readonly quotationId = signal<number | null>(null);
  readonly quoteLabel = signal(''); readonly issues = signal<Record<string, OrderDraftIssue>>({});
  readonly submitted = signal(false); readonly saving = signal(false); readonly saved = signal<SalesOrderDetail | null>(null);
  readonly uncertain = signal(false); readonly error = signal<string | null>(null); readonly traceId = signal<string | null>(null);
  readonly errorDetails = signal<readonly string[]>([]);
  readonly loading = signal(false); readonly loadError = signal<string | null>(null);
  readonly blocked = signal(false); readonly blockedReason = signal('sd.orderEdit.blocked');
  readonly conflict = signal(false); readonly editUnavailable = signal(false); readonly noChange = signal(false);
  readonly documentNo = signal(''); readonly version = signal(0);
  /** Keyed by line object, so removing a line never shifts another line's price source (ADR-164/165). */
  readonly pricing = signal(new Map<OrderDraftLineForm, LinePricing>());
  readonly lineErrors = signal(new Map<OrderDraftLineForm, string>());
  private readonly reasonDemanded = new Set<OrderDraftLineForm>();
  private readonly quoteTokens = new WeakMap<OrderDraftLineForm, number>();
  private shownPricingFailure: DocumentPricingFailure | null = null;
  private baseline = '';
  private epoch = 0;
  private validationFailure: unknown = null;
  constructor() {
    this.destroy.onDestroy(() => this.epoch++);
    const language = this.translate.onLangChange.subscribe(() => {
      if (this.validationFailure) this.errorDetails.set(this.localisedDetails(this.validationFailure));
      else if (this.shownPricingFailure) this.errorDetails.set(this.pricingDetails(this.shownPricingFailure));
    });
    this.destroy.onDestroy(() => language.unsubscribe());
    effect(() => { this.company.current(); untracked(() => { this.reset(); if (this.editId) this.loadSaved(); }); });
  }
  canLeave(): boolean { return !this.dirty() || window.confirm(this.translate.instant('sd.orderCreate.unsaved')); }
  @HostListener('window:beforeunload', ['$event']) beforeUnload(event: BeforeUnloadEvent): void { if (this.dirty()) event.preventDefault(); }
  private dirty(): boolean { return !this.saved() && (JSON.stringify(this.draft) !== (this.editId ? this.baseline : JSON.stringify(blank())) || !!this.quotationId()); }
  locked(): boolean { return this.saving() || this.uncertain() || !!this.saved() || this.loading() || !!this.loadError() || this.blocked() || this.conflict() || this.editUnavailable(); }
  field(i: number, name: string): string { return `lines[${i}].${name}`; }
  issue(name: string): OrderDraftIssue | undefined { return this.issues()[name]; }
  count(): number { return Object.keys(this.issues()).length; }
  revalidate(): void { if (this.submitted()) this.issues.set(validateOrderDraft(this.draft, this.overrideFlags())); }
  canOverride(): boolean { return this.permissions.has('SAL.PRICING.OVERRIDE'); }
  pricingOf(line: OrderDraftLineForm | undefined): LinePricing { return (line && this.pricing().get(line)) || NO_PRICING; }
  lineError(line: OrderDraftLineForm): string | null { return this.lineErrors().get(line) ?? null; }
  needsOverride(index: number): boolean {
    const line = this.draft.lines[index];
    return !!line && linePricingNeedsOverride(this.pricingOf(line), line.unitPrice, line.discountRate);
  }
  showReason(index: number): boolean {
    const line = this.draft.lines[index];
    return !!line && (this.needsOverride(index) || this.pricingOf(line).kind === 'legacy' || this.reasonDemanded.has(line) ||
      !!line.overrideReason?.trim());
  }
  canRequote(index: number): boolean {
    const kind = this.pricingOf(this.draft.lines[index]).kind;
    return kind !== 'none' && kind !== 'loading' && !!this.quoteQuery(index);
  }
  ruleText(line: OrderDraftLineForm): string {
    const state = this.pricingOf(line);
    if (state.kind !== 'rule') return '';
    return this.translate.instant(state.fromSaved ? 'sd.pricing.savedRule' : 'sd.pricing.rule', { id: state.ruleId,
      version: state.ruleVersion, price: trimDecimalText(state.unitPrice), ceiling: trimDecimalText(state.discountCeiling),
      validTo: state.validTo ?? '' });
  }
  duplicateIds(line: OrderDraftLineForm): string {
    const state = this.pricingOf(line);
    return state.kind === 'duplicate' ? state.ruleIds.map(id => '#' + id).join(', ') : '';
  }
  failedKey(line: OrderDraftLineForm): string {
    const state = this.pricingOf(line);
    return state.kind === 'failed' ? state.messageKey : '';
  }
  failedTrace(line: OrderDraftLineForm): string | null {
    const state = this.pricingOf(line);
    return state.kind === 'failed' ? state.traceId : null;
  }
  private overrideFlags(): boolean[] {
    return this.draft.lines.map((line, i) => this.needsOverride(i) || this.reasonDemanded.has(line));
  }
  private setPricing(line: OrderDraftLineForm, state: LinePricing): void {
    this.pricing.update(old => new Map(old).set(line, state));
  }
  /** The order currency is a header field, so every line is quoted in it (ADR-165). */
  private quoteQuery(index: number) {
    const line = this.draft.lines[index];
    if (!line || !this.draft.customerId || !line.itemId || !/^[A-Z]{3}$/.test(this.draft.currencyCode) ||
        !DATE.test(this.draft.businessDate)) return null;
    return { itemId: line.itemId, customerId: this.draft.customerId, currency: this.draft.currencyCode,
      businessDate: this.draft.businessDate };
  }
  /** Re-reads the dated price of one line (ADR-159) and applies a found rule price; only the latest reply counts. */
  requote(index: number): void {
    const line = this.draft.lines[index];
    if (!line) return;
    const token = (this.quoteTokens.get(line) ?? 0) + 1;
    this.quoteTokens.set(line, token);
    line.pricingRuleId = null; line.pricingRuleVersion = null;
    const query = this.quoteQuery(index);
    if (!query) { this.setPricing(line, NO_PRICING); this.revalidate(); return; }
    this.setPricing(line, { kind: 'loading' });
    const epoch = this.epoch;
    const current = () => epoch === this.epoch && this.quoteTokens.get(line) === token;
    this.prices.quote(query).subscribe({
      next: quote => { if (!current()) return;
        line.unitPrice = trimDecimalText(quote.unitPrice);
        line.pricingRuleId = quote.ruleId; line.pricingRuleVersion = quote.ruleVersion;
        this.setPricing(line, { kind: 'rule', ruleId: quote.ruleId, ruleVersion: quote.ruleVersion,
          unitPrice: quote.unitPrice, discountCeiling: quote.discountCeiling, validTo: quote.validTo, fromSaved: false });
        this.revalidate(); },
      error: failure => { if (!current()) return;
        this.setPricing(line, quoteFailureState(failure)); this.revalidate(); },
    });
  }
  private requoteAll(): void { this.draft.lines.forEach((_, i) => this.requote(i)); }
  dateChanged(): void { this.revalidate(); if (DATE.test(this.draft.businessDate)) this.requoteAll(); }
  currencyChanged(): void { this.revalidate(); if (/^[A-Z]{3}$/.test(this.draft.currencyCode)) this.requoteAll(); }
  /** Restores the quoted price and caps the discount at the ceiling, so no override is needed. */
  useRulePrice(index: number): void {
    const line = this.draft.lines[index];
    const state = this.pricingOf(line);
    if (!line || state.kind !== 'rule') return;
    line.unitPrice = trimDecimalText(state.unitPrice);
    if (compareDecimalText(line.discountRate, state.discountCeiling) > 0) line.discountRate = trimDecimalText(state.discountCeiling);
    this.revalidate();
  }
  reset(): void { this.epoch++; this.draft = blank(); this.customerLabel.set(''); this.itemLabels.set(new Map()); this.taxLabels.set(new Map());
    this.quotationId.set(null); this.quoteLabel.set(''); this.issues.set({}); this.submitted.set(false); this.saving.set(false);
    this.saved.set(null); this.uncertain.set(false); this.error.set(null); this.traceId.set(null); this.errorDetails.set([]); this.validationFailure = null;
    this.pricing.set(new Map()); this.lineErrors.set(new Map()); this.reasonDemanded.clear(); this.shownPricingFailure = null;
    this.loading.set(false); this.loadError.set(null); this.blocked.set(false); this.blockedReason.set('sd.orderEdit.blocked'); this.conflict.set(false);
    this.editUnavailable.set(false); this.noChange.set(false); this.documentNo.set(''); this.version.set(0); this.baseline = JSON.stringify(this.draft); }
  loadSaved(): void {
    if (!Number.isSafeInteger(this.editId) || this.editId <= 0 || this.loading()) {
      if (this.editId) this.loadError.set('SAL.ORDER.NOT_FOUND');
      return;
    }
    const epoch = ++this.epoch;
    this.loading.set(true); this.loadError.set(null); this.conflict.set(false); this.editUnavailable.set(false); this.uncertain.set(false);
    this.error.set(null); this.noChange.set(false); this.traceId.set(null);
    this.orders.getEditable(this.editId).subscribe({
      next: saved => { if (epoch !== this.epoch) return; this.loading.set(false); this.applySaved(saved); },
      error: failure => { if (epoch !== this.epoch) return; this.loading.set(false);
        this.loadError.set(failure instanceof TimeoutError ? 'sd.order.timeout' : apiErrorKey(failure, ['SAL.ORDER.NOT_FOUND', 'COMMON.PERMISSION.DENIED']));
        this.traceId.set(apiErrorTraceId(failure)); },
    });
  }
  private applySaved(saved: SalesOrderEditableDetail): void {
    const mapped = orderEditDraft(saved);
    this.documentNo.set(saved.documentNo ?? ''); this.version.set(saved.version ?? 0);
    this.blocked.set(!mapped);
    if (!mapped) {
      const reason = saved.status !== 'Draft' && saved.status !== 'Rejected'
        ? ['Pending', 'Approved', 'Cancelled'].includes(saved.status) ? saved.status : ''
        : saved.sourceQuotationId != null ? 'Converted'
          : Array.isArray(saved.lines) && saved.lines.some(line => line.deliveredQuantity !== 0 || line.pendingQuantity !== 0) ? 'Delivered' : '';
      this.blockedReason.set(reason ? `sd.orderEdit.blocked${reason}` : 'sd.orderEdit.blocked');
      return;
    }
    this.blockedReason.set('sd.orderEdit.blocked');
    this.draft = mapped.draft;
    this.baseline = JSON.stringify(mapped.draft);
    // Saved provenance explains the price; the saved rule pair is resent as the hint (changed rule → 409 per line).
    this.pricing.set(new Map(this.draft.lines.map((line, i) => [line, this.savedPricing(saved.lines[i])])));
    this.lineErrors.set(new Map()); this.reasonDemanded.clear(); this.shownPricingFailure = null;
    this.customerLabel.set(`${saved.customerCode} · ${saved.customerName}`);
    this.itemLabels.set(new Map(saved.lines.map((line, i) => [i, `${line.itemCode} · ${line.itemName}`])));
    this.taxLabels.set(new Map(saved.lines.map((line, i) => [i,
      Number.isFinite(line.taxRatePercent) ? `${line.taxRatePercent}%` : `#${line.taxRateId}`])));
    this.issues.set({}); this.submitted.set(false);
  }
  private savedPricing(line: SalesOrderEditableDetail['lines'][number]): LinePricing {
    if (line.pricingRuleId != null && line.pricingRuleVersion != null && line.ruleUnitPrice != null &&
        line.ruleDiscountCeiling != null)
      return { kind: 'rule', ruleId: line.pricingRuleId, ruleVersion: line.pricingRuleVersion,
        unitPrice: String(line.ruleUnitPrice), discountCeiling: String(line.ruleDiscountCeiling), validTo: null, fromSaved: true };
    return line.pricingMode === 'Override' ? { kind: 'notFound' } : { kind: 'legacy' };
  }
  addLine(): void { if (this.draft.lines.length < 1000) this.draft.lines.push(emptyLine(this.draft.lines.length + 1)); this.revalidate(); }
  removeLine(index: number): void { if (this.draft.lines.length < 2) return; this.draft.lines.splice(index, 1);
    this.draft.lines.forEach((line, i) => line.lineNo = i + 1);
    this.itemLabels.set(new Map([...this.itemLabels()].filter(([i]) => i !== index).map(([i, label]) => [i > index ? i - 1 : i, label])));
    this.taxLabels.set(new Map([...this.taxLabels()].filter(([i]) => i !== index).map(([i, label]) => [i > index ? i - 1 : i, label]))); }
  /** ADR-167: customer/item/tax choices come from SD option endpoints (order writer permission, no MD view). */
  pickCustomer(): void { const epoch = this.epoch; this.dialogs.select<SalesDocumentOption>({ titleKey: 'sd.quotationCreate.chooseCustomer',
    descriptionKey: 'sd.documentOptions.customerScope',
    columns: [{ key: 'code', labelKey: 'sd.quotationCreate.code', value: row => row.code }, { key: 'name', labelKey: 'sd.quotationCreate.name', value: row => row.name }],
    load: query => this.options.customers(query), rowKey: row => row.id }).subscribe(rows => {
      if (epoch !== this.epoch || !rows?.[0]) return; const row = rows[0]; this.draft.customerId = row.id;
      this.customerLabel.set(`${row.code} · ${row.name}`); this.requoteAll(); }); }
  pickItem(index: number): void { const epoch = this.epoch; this.dialogs.select<SalesDocumentOption>({ titleKey: 'sd.quotationCreate.chooseItem',
    descriptionKey: 'sd.documentOptions.itemScope',
    columns: [{ key: 'code', labelKey: 'sd.quotationCreate.code', value: row => row.code }, { key: 'name', labelKey: 'sd.quotationCreate.name', value: row => row.name }],
    load: query => this.options.items(query), rowKey: row => row.id }).subscribe(rows => {
      if (epoch !== this.epoch || !rows?.[0] || !this.draft.lines[index]) return; const row = rows[0];
      this.draft.lines[index].itemId = row.id; this.itemLabels.update(map => new Map(map).set(index, `${row.code} · ${row.name}`)); this.requote(index); }); }
  /** True when the business date is a full date, so the output tax rates effective on it can be listed. */
  taxDateReady(): boolean { return DATE.test(this.draft.businessDate); }
  /** ADR-167: one dialog of the output tax rates effective on the business date (code, name, rate). */
  pickTax(index: number): void { const epoch = this.epoch; const date = this.draft.businessDate; if (!DATE.test(date)) return;
    this.dialogs.select<SalesOutputTaxOption>({ titleKey: 'sd.quotationCreate.chooseTaxRate', descriptionKey: 'sd.documentOptions.taxScope',
      columns: [{ key: 'code', labelKey: 'sd.quotationCreate.code', value: row => row.code }, { key: 'name', labelKey: 'sd.quotationCreate.name', value: row => row.name },
        { key: 'rate', labelKey: 'sd.quotationCreate.rate', value: row => `${row.ratePercent}%` }],
      load: query => this.options.outputTaxRates(date, query), rowKey: row => row.id }).subscribe(rates => {
        if (epoch !== this.epoch || !rates?.[0] || !this.draft.lines[index]) return; const rate = rates[0];
        this.draft.lines[index].taxRateId = rate.id; this.taxLabels.update(map => new Map(map).set(index, `${rate.code} · ${rate.ratePercent}%`)); }); }
  pickQuotation(): void { const epoch = this.epoch; this.dialogs.select<SalesQuotationSummary>({ titleKey: 'sd.orderCreate.chooseQuotation',
    columns: [{ key: 'documentNo', labelKey: 'sd.order.number', value: row => row.documentNo },
      { key: 'customerName', labelKey: 'sd.order.customer', value: row => row.customerName }],
    load: query => this.quotations.list({ ...query, status: 'Approved' }), rowKey: row => row.id }).subscribe(rows => {
      if (epoch !== this.epoch || !rows?.[0]) return; this.quotationId.set(rows[0].id);
      this.quoteLabel.set(`${rows[0].documentNo} · ${rows[0].customerName}`); }); }
  save(): void { if (this.locked()) return; this.submitted.set(true); const issues = validateOrderDraft(this.draft, this.overrideFlags()); this.issues.set(issues);
    if (Object.keys(issues).length) { this.focusFirstIssue(Object.keys(issues)[0]); return; }
    this.noChange.set(false);
    if (this.editId) {
      if (JSON.stringify(this.draft) === this.baseline) { this.noChange.set(true); return; }
      this.persistEdit(); return;
    }
    this.persist(this.orders.create(this.draft), { draft: this.draft }); }
  private persistEdit(): void {
    const epoch = this.epoch, version = this.version(), expected = structuredClone(this.draft), number = this.documentNo();
    this.saving.set(true); this.error.set(null); this.errorDetails.set([]); this.validationFailure = null;
    this.lineErrors.set(new Map()); this.shownPricingFailure = null;
    this.orders.replace(this.editId, version, expected).subscribe({
      next: response => { if (epoch !== this.epoch) return;
        if (response?.id !== this.editId) { this.saving.set(false); this.uncertain.set(true); return; }
        this.orders.getEditable(this.editId).subscribe({ next: saved => { if (epoch !== this.epoch) return;
          this.saving.set(false);
          const mapped = orderEditDraft(saved);
          if (!mapped || saved.id !== this.editId || saved.documentNo !== number || saved.version <= version ||
              saved.status !== 'Draft' || !this.sameDraft(mapped.draft, expected)) { this.uncertain.set(true); return; }
          this.version.set(saved.version); this.saved.set(saved as unknown as SalesOrderDetail);
        }, error: failure => { if (epoch !== this.epoch) return; this.saving.set(false); this.uncertain.set(true);
          this.traceId.set(apiErrorTraceId(failure)); } }); },
      error: failure => { if (epoch !== this.epoch) return; this.saving.set(false);
        const status = (failure as { status?: number })?.status;
        this.traceId.set(apiErrorTraceId(failure));
        const pricingFailure = pricingFailureOf(failure);
        if (pricingFailure) { this.showPricingFailure(pricingFailure); return; }
        if (status === 409) { this.conflict.set(true); return; }
        if (status === 404) { this.editUnavailable.set(true); return; }
        if (failure instanceof TimeoutError || status === 0 || (status != null && status >= 500)) {
          this.uncertain.set(true); if (status != null && status >= 500) this.error.set('sd.orderCreate.serverUncertain'); return;
        }
        if (status === 422 && apiErrorKey(failure, ['SAL.ORDER.INVALID_STATUS']) === 'errors.SAL.ORDER.INVALID_STATUS') {
          const pending = apiValidationDetails(failure).some(detail => detail === 'status: Order is pending approval');
          this.conflict.set(true);
          this.error.set(pending ? 'sd.orderEdit.blockedPending' : 'sd.orderEdit.blocked');
          return;
        }
        this.validationFailure = failure; this.errorDetails.set(this.localisedDetails(failure));
        if (status === 400) this.demandReasons(failure);
        const key = apiErrorKey(failure, ['SAL.ORDER.NOT_FOUND', 'MD.CUSTOMER.NOT_FOUND', 'MD.CUSTOMER.NOT_APPROVED', 'MD.ITEM.NOT_FOUND',
          'MD.TAX_RATE.NOT_FOUND', 'COMMON.PERMISSION.DENIED', 'COMMON.REQUEST.VALIDATION_FAILED']);
        this.error.set(status === 422 && key === UNKNOWN_ERROR_KEY ? 'sd.orderEdit.rejected' : key);
      },
    });
  }
  private sameDraft(actual: OrderDraftForm, expected: OrderDraftForm): boolean {
    const numeric = (value: string) => value.replace(/(?:\.0+|(?<=\.[0-9]*[1-9])0+)$/, '').replace(/\.$/, '');
    return actual.customerId === expected.customerId && actual.businessDate === expected.businessDate &&
      actual.currencyCode === expected.currencyCode && actual.remark === expected.remark.trim() &&
      actual.lines.length === expected.lines.length && actual.lines.every((line, i) => {
        const wanted = expected.lines[i];
        return line.lineNo === wanted.lineNo && line.itemId === wanted.itemId && line.taxRateId === wanted.taxRateId &&
          numeric(line.quantity) === numeric(wanted.quantity) && numeric(line.unitPrice) === numeric(wanted.unitPrice) &&
          numeric(line.discountRate) === numeric(wanted.discountRate);
      });
  }
  private focusFirstIssue(field: string): void {
    const topLevel: Record<string, string> = { customerId: 'customer-pick', businessDate: 'date',
      currencyCode: 'currency', remark: 'remark', lines: 'add-line' };
    const match = /^lines\[(\d+)\]\.(\w+)$/.exec(field);
    if (match?.[2] === 'overrideReason') {
      queueMicrotask(() => this.host.nativeElement.querySelector<HTMLElement>(`[data-testid="sd-order-reason-${match[1]}"]`)?.focus());
      return;
    }
    const lineControl: Record<string, string> = { itemId: 'item-pick', quantity: 'quantity', unitPrice: 'price',
      discountRate: 'discount', taxRateId: 'tax-pick', lineNo: 'item-pick' };
    const target = `sd-order-create-${match ? `${lineControl[match[2]] ?? 'item-pick'}-${match[1]}` : topLevel[field] ?? 'save'}`;
    queueMicrotask(() => this.host.nativeElement.querySelector<HTMLElement>(`[data-testid="${target}"]`)?.focus());
  }
  /** 403/409 pricing rejections name one line (ADR-160 `lineNo`); never an edit version conflict or a success. */
  private showPricingFailure(failure: DocumentPricingFailure): void {
    const index = this.draft.lines.findIndex(line => line.lineNo === failure.lineNo);
    const line = this.draft.lines[index];
    this.shownPricingFailure = failure;
    this.error.set(`errors.${failure.code}`);
    this.errorDetails.set(this.pricingDetails(failure));
    if (!line) return;
    this.lineErrors.update(old => new Map(old).set(line, this.pricingLineKey(failure)));
    if (failure.code === 'SAL.PRICING.STALE_QUOTE') this.requote(index);
    if (failure.code === 'SAL.PRICING.DUPLICATE_ACTIVE_RULE') {
      line.pricingRuleId = null; line.pricingRuleVersion = null;
      this.setPricing(line, { kind: 'duplicate', ruleIds: failure.ruleIds });
    }
  }
  private pricingLineKey(failure: DocumentPricingFailure): string {
    return failure.code === 'SAL.PRICING.OVERRIDE_REQUIRED' ? 'sd.pricing.orderLineOverrideRequired'
      : failure.code === 'SAL.PRICING.STALE_QUOTE' ? 'sd.pricing.orderLineStale' : 'sd.pricing.orderLineDuplicate';
  }
  private pricingDetails(failure: DocumentPricingFailure): string[] {
    const index = this.draft.lines.findIndex(line => line.lineNo === failure.lineNo);
    return index < 0 ? [] : [this.translate.instant('sd.quotationCreate.line', { number: index + 1 }) + ': ' +
      this.translate.instant(this.pricingLineKey(failure))];
  }
  /** A 400 `lines[i].overrideReason` shows and requires that line's reason field. */
  private demandReasons(failure: unknown): void {
    const errors = (failure as { error?: { errors?: unknown } } | null)?.error?.errors;
    if (!errors || typeof errors !== 'object' || Array.isArray(errors)) return;
    for (const path of Object.keys(errors)) {
      const match = /^lines\[(\d+)\]\.overrideReason$/.exec(path);
      const line = match ? this.draft.lines[Number(match[1])] : undefined;
      if (line) this.reasonDemanded.add(line);
    }
    this.issues.set(validateOrderDraft(this.draft, this.overrideFlags()));
  }
  private localisedDetails(failure: unknown): string[] {
    return apiValidationDetails(failure).map(detail => {
      const separator = detail.indexOf(': ');
      if (separator < 0) return this.translate.instant('sd.quotationCreate.validation.serverField');
      const field = detail.slice(0, separator);
      const reasonLine = /^lines\[(\d+)\]\.overrideReason$/.exec(field);
      if (reasonLine) return this.translate.instant('sd.quotationCreate.line', { number: Number(reasonLine[1]) + 1 }) + ': ' +
        this.translate.instant('sd.quotationCreate.validation.overrideReason');
      const reason = detail.slice(separator + 2);
      const fieldKey = field === 'quotationId' ? 'chooseQuotation' : field === 'customerId' ? 'customer' :
        field === 'businessDate' ? 'date' : field === 'currencyCode' ? 'currency' :
        field.includes('itemId') ? 'item' : field.includes('taxRateId') ? 'tax' :
        field.includes('quantity') ? 'quantity' : field.includes('unitPrice') ? 'price' : 'lines';
      const reasonKey = reason === 'Customer is inactive' ? 'customerInactive' :
        reason === 'Quotation is unavailable' ? 'quotationUnavailable' : 'serverField';
      const colon = (this.translate.getCurrentLang() ?? '').startsWith('zh') ? '：' : ': ';
      return `${this.translate.instant(`sd.orderCreate.${fieldKey}`)}${colon}${this.translate.instant(`sd.orderCreate.${reasonKey}`)}`;
    });
  }
  convert(): void { const id = this.quotationId(); if (!id || this.locked()) return; this.persist(this.orders.convertQuotation(id), { quotationId: id }); }
  private persist(request: ReturnType<SalesOrderService['create']>, source: { draft: OrderDraftForm } | { quotationId: number }): void { const epoch = this.epoch; this.saving.set(true); this.error.set(null); this.errorDetails.set([]); this.validationFailure = null;
    this.lineErrors.set(new Map()); this.shownPricingFailure = null;
    request.subscribe({ next: created => { if (epoch !== this.epoch) return;
      if (!Number.isSafeInteger(created?.id) || created.id <= 0) { this.saving.set(false); this.uncertain.set(true); return; }
      this.orders.getCreated(created.id).subscribe({ next: saved => { if (epoch !== this.epoch) return;
        this.saving.set(false);
        if (saved?.id !== created.id || !matchesOrderReceipt(saved, source)) { this.uncertain.set(true); return; }
        this.saved.set(saved); }, error: failure => { if (epoch !== this.epoch) return;
          this.saving.set(false); this.uncertain.set(true); this.traceId.set(apiErrorTraceId(failure));
          const status = (failure as { status?: number })?.status;
          if (status != null && status >= 500) this.error.set('sd.orderCreate.serverUncertain'); } }); },
      error: failure => { if (epoch !== this.epoch) return; this.saving.set(false);
        const status = (failure as { status?: number })?.status;
        const pricingFailure = 'draft' in source ? pricingFailureOf(failure) : null;
        if (pricingFailure) { this.traceId.set(apiErrorTraceId(failure)); this.showPricingFailure(pricingFailure); return; }
        if (status === 400 && 'draft' in source) this.demandReasons(failure);
        this.uncertain.set(failure instanceof TimeoutError || status === 0 || (status != null && status >= 500));
        this.traceId.set(apiErrorTraceId(failure));
        if (status != null && status >= 500) this.error.set('sd.orderCreate.serverUncertain');
        if (!this.uncertain()) { const details = this.localisedDetails(failure); this.validationFailure = failure; this.errorDetails.set(details);
          const key = apiErrorKey(failure, [
            'SAL.QUOTATION.ALREADY_CONVERTED', 'SAL.QUOTATION.NOT_FOUND', 'SAL.QUOTATION.INVALID_STATUS',
            'SAL.QUOTATION.EXPIRED', 'MD.CUSTOMER.NOT_FOUND', 'MD.CUSTOMER.NOT_APPROVED', 'MD.ITEM.NOT_FOUND', 'MD.TAX_RATE.NOT_FOUND',
            'COMMON.PERMISSION.DENIED', 'SYS.NUMBER_RULE.NOT_CONFIGURED']);
          this.error.set(status === 422 && (details.length > 0 || key === UNKNOWN_ERROR_KEY) ? 'sd.orderCreate.rejected' : key); } } }); }
}
