import { ChangeDetectionStrategy, Component, DestroyRef, ElementRef, HostListener, effect, inject, signal, untracked } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import { NzAlertModule } from 'ng-zorro-antd/alert';
import { NzButtonModule } from 'ng-zorro-antd/button';
import { PermissionService } from '../../core/auth/permission.service';
import { CompanyContextService } from '../../core/company/company-context.service';
import { apiErrorKey, apiErrorTraceId } from '../../core/http/api-error';
import { ApiErrorDisplay } from '../../shared/api-error-display';
import { ErpDialogService } from '../../shared/dialogs/dialog.service';
import { SalesDocumentOption, SalesDocumentOptionsService, SalesOutputTaxOption } from './document-options.service';
import { compareDecimalText, DocumentPricingFailure, LinePricing, linePricingNeedsOverride, pricingFailureOf, quoteFailureState, trimDecimalText } from './document-pricing';
import { SalesPriceRuleService } from './price-rule.service';
import { QuotationDraftForm, QuotationDraftIssue, QuotationDraftLineForm, validateQuotationDraft } from './quotation-draft-form';
import { SalesQuotationEditableDetail, SalesQuotationService } from './quotation.service';

const DATE = /^\d{4}-\d{2}-\d{2}$/;
const NO_PRICING: LinePricing = { kind: 'none' };
function emptyLine(lineNo: number): QuotationDraftLineForm {
  return { lineNo, itemId: null, quantity: '1', unitPrice: '0', discountRate: '0', currencyCode: 'TWD', taxRateId: null,
    pricingRuleId: null, pricingRuleVersion: null, overrideReason: '' };
}

function emptyDraft(): QuotationDraftForm {
  return { customerId: null, businessDate: '', expiryDate: '', remark: '',
    lines: [emptyLine(1)] };
}

/** Create and edit share the validated draft controls; the server owns saved snapshots. */
@Component({
  selector: 'erp-sales-quotation-create-page',
  imports: [FormsModule, RouterLink, TranslatePipe, NzAlertModule, NzButtonModule, ApiErrorDisplay],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <section class="create-page" data-testid="sd-quote-create">
      <header><h1>{{ (editId ? 'sd.quotationEdit.title' : 'sd.quotationCreate.title') | translate }}</h1>
        <a routerLink="/sal/quotations">{{ 'sd.quotationCreate.back' | translate }}</a></header>
      <p class="hint">{{ (editId ? 'sd.quotationEdit.intro' : 'sd.quotationCreate.intro') | translate }}</p>
      @if (loading()) { <p role="status">{{ 'sd.quotationEdit.loading' | translate }}</p> }
      @if (loadError()) { <erp-api-error-display [messageKey]="loadError()!" [traceId]="traceId()" />
        <button nz-button type="button" (click)="loadSaved()">{{ 'sd.quotation.retry' | translate }}</button> }
      @if (editId && statusBlocked()) { <nz-alert nzType="warning" nzShowIcon
        [nzMessage]="'sd.quotationEdit.blocked' | translate" /> }
      @if (conflict()) { <nz-alert nzType="warning" nzShowIcon data-testid="sd-quote-edit-conflict"
        [nzMessage]="'sd.quotationEdit.conflict' | translate" />
        <button nz-button type="button" (click)="loadSaved()" data-testid="sd-quote-edit-refresh">{{ 'sd.quotationEdit.refresh' | translate }}</button> }
      @if (savedNo()) { <nz-alert nzType="success" nzShowIcon data-testid="sd-quote-create-success"
        [nzMessage]="(editId ? 'sd.quotationEdit.saved' : 'sd.quotationCreate.saved') | translate: { number: savedNo() }" />
        @if (!editId) { <button nz-button type="button" (click)="startAnother()" data-testid="sd-quote-create-another">{{ 'sd.quotationCreate.another' | translate }}</button> } }
      @if (uncertain()) { <nz-alert nzType="warning" nzShowIcon data-testid="sd-quote-create-uncertain"
        [nzMessage]="(editId ? 'sd.quotationEdit.outcomeUnknown' : 'sd.quotationCreate.outcomeUnknown') | translate"
        [nzDescription]="traceId() ? 'Trace ID: ' + traceId() : null" /> }
      @if (saveError()) { <erp-api-error-display data-testid="sd-quote-create-error" [messageKey]="saveError()!"
        [traceId]="traceId()" [details]="serverDetails()" /> }
      <form (ngSubmit)="save()" novalidate class="form" data-testid="sd-quote-create-form">
        <div class="field customer-field"><span id="quote-create-customer-label">{{ 'sd.quotationCreate.customer' | translate }}</span>
          <div role="group" aria-labelledby="quote-create-customer-label" class="picker" tabindex="-1"
            [attr.aria-invalid]="!!issue('customerId')">
            <span data-testid="sd-quote-create-customer">{{ customerLabel() || ('sd.quotationCreate.noCustomer' | translate) }}</span>
            <button nz-button type="button" data-testid="sd-quote-create-customer-pick" [disabled]="!editable()"
              (click)="pickCustomer()">{{ 'sd.quotationCreate.chooseCustomer' | translate }}</button>
          </div>
          @if (issue('customerId')) { <p class="error" role="alert">{{ errorKey('customerId') | translate }}</p> }
        </div>
        <label class="field">{{ 'sd.quotationCreate.businessDate' | translate }}
          <input type="date" name="businessDate" autocomplete="off" [(ngModel)]="draft.businessDate" (ngModelChange)="dateChanged()" data-testid="sd-quote-create-date"
            [disabled]="!editable()" [attr.aria-invalid]="!!issue('businessDate')" />
          @if (issue('businessDate')) { <span class="error" role="alert">{{ errorKey('businessDate') | translate }}</span> }
        </label>
        <label class="field">{{ 'sd.quotationCreate.expiryDate' | translate }}
          <input type="date" name="expiryDate" autocomplete="off" [(ngModel)]="draft.expiryDate" (ngModelChange)="revalidate()" data-testid="sd-quote-create-expiry"
            [disabled]="!editable()" [attr.aria-invalid]="!!issue('expiryDate')" />
          @if (issue('expiryDate')) { <span class="error" role="alert">{{ errorKey('expiryDate') | translate }}</span> }
        </label>
        <label class="field remark">{{ 'sd.quotationCreate.remark' | translate }}
          <textarea name="remark" autocomplete="off" [(ngModel)]="draft.remark" (ngModelChange)="revalidate()" rows="2" data-testid="sd-quote-create-remark"
            [disabled]="!editable()" [attr.aria-invalid]="!!issue('remark')"></textarea>
          @if (issue('remark')) { <span class="error" role="alert">{{ errorKey('remark') | translate }}</span> }
        </label>
        <section class="lines" aria-labelledby="quote-create-lines-label">
          <div class="line-head"><h2 id="quote-create-lines-label">{{ 'sd.quotationCreate.lines' | translate }}</h2>
            <button nz-button type="button" (click)="addLine()" [disabled]="!editable() || draft.lines.length >= 1000"
              data-testid="sd-quote-create-add">{{ 'sd.quotationCreate.addLine' | translate }}</button></div>
          @for (line of draft.lines; track $index; let i = $index) { <fieldset class="line">
            <legend>{{ 'sd.quotationCreate.line' | translate: { number: i + 1 } }}</legend>
            <div class="line-grid">
              <div class="field"><span [id]="'quote-create-item-label-' + i">{{ 'sd.quotationCreate.item' | translate }}</span>
                <div class="picker" role="group" tabindex="-1" [attr.aria-labelledby]="'quote-create-item-label-' + i"
                  [attr.aria-invalid]="!!issue(lineField(i, 'itemId'))">
                  <span>{{ itemLabel(i) || ('sd.quotationCreate.noItem' | translate) }}</span>
                  <button nz-button type="button" [attr.data-testid]="'sd-quote-create-item-pick-' + i"
                    [disabled]="!editable()" (click)="pickItem(i)">{{ 'sd.quotationCreate.chooseItem' | translate }}</button>
                </div>
                @if (issue(lineField(i, 'itemId'))) { <span class="error" role="alert">{{ errorKey('itemId') | translate }}</span> }
              </div>
              <label class="field">{{ 'sd.quotationCreate.quantity' | translate }}
                <input inputmode="decimal" autocomplete="off" [name]="'quantity-' + i" [(ngModel)]="line.quantity" (ngModelChange)="revalidate()" [disabled]="!editable()"
                  [attr.data-testid]="'sd-quote-create-quantity-' + i" [attr.aria-invalid]="!!issue(lineField(i, 'quantity'))" />
                @if (issue(lineField(i, 'quantity'))) { <span class="error" role="alert">{{ errorKey('quantity') | translate }}</span> }
              </label>
              <label class="field">{{ 'sd.quotationCreate.unitPrice' | translate }}
                <input inputmode="decimal" autocomplete="off" [name]="'price-' + i" [(ngModel)]="line.unitPrice" (ngModelChange)="revalidate()" [disabled]="!editable()"
                  [attr.data-testid]="'sd-quote-create-price-' + i" [attr.aria-invalid]="!!issue(lineField(i, 'unitPrice'))" />
                @if (issue(lineField(i, 'unitPrice'))) { <span class="error" role="alert">{{ errorKey('unitPrice') | translate }}</span> }
              </label>
              <label class="field">{{ 'sd.quotationCreate.discount' | translate }}
                <input inputmode="decimal" autocomplete="off" [name]="'discount-' + i" [(ngModel)]="line.discountRate" (ngModelChange)="revalidate()" [disabled]="!editable()"
                  [attr.data-testid]="'sd-quote-create-discount-' + i" [attr.aria-invalid]="!!issue(lineField(i, 'discountRate'))" />
                @if (issue(lineField(i, 'discountRate'))) { <span class="error" role="alert">{{ errorKey('discountRate') | translate }}</span> }
              </label>
              <label class="field">{{ 'sd.quotationCreate.currency' | translate }}
                <input [name]="'currency-' + i" autocomplete="off" spellcheck="false" [(ngModel)]="line.currencyCode" (ngModelChange)="currencyChanged(i)" maxlength="3" [disabled]="!editable()"
                  [attr.aria-invalid]="!!issue(lineField(i, 'currencyCode'))" />
                @if (issue(lineField(i, 'currencyCode'))) { <span class="error" role="alert">{{ errorKey('currencyCode') | translate }}</span> }
              </label>
              <div class="field"><span [id]="'quote-create-tax-label-' + i">{{ 'sd.quotationCreate.taxRate' | translate }}</span>
                <div class="picker" role="group" tabindex="-1" [attr.aria-labelledby]="'quote-create-tax-label-' + i"
                  [attr.aria-invalid]="!!issue(lineField(i, 'taxRateId'))">
                  <span>{{ taxLabel(i) || ('sd.quotationCreate.noTaxRate' | translate) }}</span>
                  <button nz-button type="button" [attr.data-testid]="'sd-quote-create-tax-pick-' + i"
                    [disabled]="!editable() || !taxDateReady()" [attr.aria-describedby]="editable() && !taxDateReady() ? 'quote-create-tax-hint-' + i : null"
                    (click)="pickTaxRate(i)">{{ 'sd.quotationCreate.chooseTaxRate' | translate }}</button>
                </div>
                @if (editable() && !taxDateReady()) { <span class="hint" [id]="'quote-create-tax-hint-' + i" [attr.data-testid]="'sd-quote-create-tax-hint-' + i">{{ 'sd.documentOptions.taxNeedsDate' | translate }}</span> }
                @if (issue(lineField(i, 'taxRateId'))) { <span class="error" role="alert">{{ errorKey('taxRateId') | translate }}</span> }
              </div>
            </div>
            <div class="pricing" [attr.data-testid]="'sd-quote-pricing-' + i" aria-live="polite">
              @switch (pricingOf(line).kind) {
                @case ('loading') { <p role="status">{{ 'sd.pricing.loading' | translate }}</p> }
                @case ('rule') { <p>{{ ruleText(line) }}</p> }
                @case ('notFound') { <p>{{ 'sd.pricing.notFound' | translate }}</p> }
                @case ('legacy') { <p>{{ 'sd.pricing.legacy' | translate }}</p> }
                @case ('duplicate') { <p class="error">{{ 'sd.pricing.duplicate' | translate: { ids: duplicateIds(line) } }}</p> }
                @case ('stale') { <p class="error">{{ 'sd.pricing.stale' | translate }}</p> }
                @case ('failed') { <p class="error">{{ failedKey(line) | translate }}</p> }
                @default { <p class="hint">{{ 'sd.pricing.none' | translate }}</p> }
              }
              @if (needsOverride(i)) { <p class="warning" [attr.data-testid]="'sd-quote-override-needed-' + i">
                {{ (pricingOf(line).kind === 'rule' ? 'sd.pricing.deviates' : 'sd.pricing.overrideNeeded') | translate }}
                @if (!canOverride()) { {{ 'sd.pricing.noOverride' | translate }} }</p> }
              @if (lineError(line); as message) { <p class="error" role="alert" [attr.data-testid]="'sd-quote-line-error-' + i">{{ message | translate }}</p> }
              @if ((pricingOf(line).kind === 'rule' && needsOverride(i)) || canRequote(i)) { <div class="pricing-actions">
                @if (pricingOf(line).kind === 'rule' && needsOverride(i)) { <button nz-button type="button" [disabled]="!editable()"
                  [attr.data-testid]="'sd-quote-use-rule-' + i" (click)="useRulePrice(i)">{{ 'sd.pricing.useRule' | translate }}</button> }
                @if (canRequote(i)) { <button nz-button type="button" [disabled]="!editable() || pricingOf(line).kind === 'loading'"
                  [attr.data-testid]="'sd-quote-requote-' + i" (click)="requote(i)">{{ 'sd.pricing.requote' | translate }}</button> }
              </div> }
              @if (showReason(i)) { <label class="field">{{ 'sd.pricing.reason' | translate }}
                <textarea [name]="'reason-' + i" autocomplete="off" rows="2" [(ngModel)]="line.overrideReason" (ngModelChange)="revalidate()"
                  [disabled]="!editable()" [attr.data-testid]="'sd-quote-reason-' + i" [attr.aria-invalid]="!!issue(lineField(i, 'overrideReason'))"></textarea>
                @if (issue(lineField(i, 'overrideReason')); as problem) { <span class="error" role="alert">{{ (problem === 'tooLong' ? 'sd.pricing.reasonTooLong' : 'sd.pricing.reasonRequired') | translate }}</span> }
              </label> }
            </div>
            <button nz-button type="button" (click)="removeLine(i)" [disabled]="!editable() || draft.lines.length === 1">
              {{ 'sd.quotationCreate.removeLine' | translate }}</button>
          </fieldset> }
          @if (issue('lines')) { <p class="error" role="alert">{{ errorKey('lines') | translate }}</p> }
        </section>
        @if (submitted() && issueCount()) { <p class="error" role="alert" data-testid="sd-quote-create-errors">
          {{ 'sd.quotationCreate.errors' | translate: { count: issueCount() } }}</p> }
        <button nz-button nzType="primary" type="submit" data-testid="sd-quote-create-save" [nzLoading]="saving()"
          [disabled]="!!savedNo() || saving() || uncertain() || loading() || !!loadError() || statusBlocked() || conflict()">{{ (editId ? 'sd.quotationEdit.save' : 'sd.quotationCreate.save') | translate }}</button>
      </form>
    </section>`,
  styles: `.create-page{display:grid;gap:16px;min-width:0;color:var(--erp-color-text)}header,.line-head{display:flex;justify-content:space-between;align-items:center;gap:12px;flex-wrap:wrap}h1,h2{margin:0}.hint{color:var(--erp-color-text-secondary);margin:0}.form{display:grid;grid-template-columns:repeat(2,minmax(0,1fr));gap:16px}.field{display:grid;align-content:start;gap:4px;min-width:0}.picker{display:flex;align-items:center;gap:8px;flex-wrap:wrap;min-height:32px}.picker span{overflow-wrap:anywhere}.remark,.lines,.form>button,.form>p{grid-column:1/-1}.line{border:1px solid var(--erp-color-border);border-radius:8px;padding:16px;margin:12px 0;min-width:0}.line-grid{display:grid;grid-template-columns:repeat(3,minmax(0,1fr));gap:12px;margin-bottom:12px}input,textarea{width:100%;min-height:32px;padding:4px 8px;background:var(--erp-color-bg-container);color:var(--erp-color-text);border:1px solid var(--erp-color-border);border-radius:4px}.error{color:var(--erp-color-error-text);margin:0}.pricing{display:grid;gap:8px;margin-bottom:12px;padding:8px 12px;border-left:3px solid var(--erp-color-border);overflow-wrap:anywhere}.pricing p{margin:0}.warning{color:var(--erp-color-text);font-weight:600;margin:0}.pricing-actions{display:flex;gap:8px;flex-wrap:wrap}.create-page>button,.form>button{justify-self:start}button:focus-visible,input:focus-visible,textarea:focus-visible,a:focus-visible{outline:2px solid var(--erp-color-primary);outline-offset:2px}@media(max-width:900px){.line-grid{grid-template-columns:repeat(2,minmax(0,1fr))}}@media(max-width:767px){.form,.line-grid{grid-template-columns:1fr}}`,
})
export class SalesQuotationCreatePage {
  private readonly api = inject(SalesQuotationService);
  private readonly options = inject(SalesDocumentOptionsService);
  private readonly dialogs = inject(ErpDialogService);
  private readonly company = inject(CompanyContextService);
  private readonly destroy = inject(DestroyRef);
  private readonly host = inject(ElementRef<HTMLElement>);
  private readonly translate = inject(TranslateService);
  private readonly route = inject(ActivatedRoute);
  private readonly prices = inject(SalesPriceRuleService);
  private readonly permissions = inject(PermissionService);
  // An invalid edit URL must never fall through to the Create API.
  readonly editId = this.route.snapshot.paramMap.has('id')
    ? (Number(this.route.snapshot.paramMap.get('id')) || -1) : 0;
  draft = emptyDraft();
  readonly customerLabel = signal('');
  readonly itemLabels = signal(new Map<number, string>());
  readonly taxLabels = signal(new Map<number, string>());
  readonly issues = signal<Record<string, QuotationDraftIssue>>({});
  readonly submitted = signal(false);
  readonly saving = signal(false);
  readonly savedNo = signal<string | null>(null);
  readonly uncertain = signal(false);
  readonly saveError = signal<string | null>(null);
  readonly serverDetails = signal<string[]>([]);
  readonly traceId = signal<string | null>(null);
  readonly loading = signal(false);
  readonly loadError = signal<string | null>(null);
  readonly statusBlocked = signal(false);
  readonly conflict = signal(false);
  /** Keyed by line object, so removing a line never shifts another line's price source (ADR-164). */
  readonly pricing = signal(new Map<QuotationDraftLineForm, LinePricing>());
  readonly lineErrors = signal(new Map<QuotationDraftLineForm, string>());
  private readonly reasonDemanded = new Set<QuotationDraftLineForm>();
  private readonly quoteTokens = new WeakMap<QuotationDraftLineForm, number>();
  private version = 0;
  private baseline = '';
  private epoch = 0;
  private serverValidationFailure: unknown = null;
  constructor() {
    this.destroy.onDestroy(() => this.epoch++);
    const language = this.translate.onLangChange.subscribe(() => {
      if (this.serverValidationFailure) this.serverDetails.set(this.validationDetails(this.serverValidationFailure));
    });
    this.destroy.onDestroy(() => language.unsubscribe());
    effect(() => { this.company.current(); untracked(() => {
      this.startAnother(); if (this.editId) this.loadSaved();
    }); });
  }
  lineField(index: number, name: string): string { return `lines[${index}].${name}`; }
  editable(): boolean { return !this.savedNo() && !this.saving() && !this.loading() && !this.loadError() && !this.statusBlocked() && !this.conflict() && !this.uncertain(); }
  canLeave(): boolean { return !this.dirty() || window.confirm(this.translate.instant('sd.quotationCreate.unsaved')); }
  @HostListener('window:beforeunload', ['$event'])
  beforeUnload(event: BeforeUnloadEvent): void {
    if (this.dirty()) event.preventDefault();
  }
  private dirty(): boolean { return !this.savedNo() && JSON.stringify(this.draft) !== (this.editId ? this.baseline : JSON.stringify(emptyDraft())); }
  issue(field: string): QuotationDraftIssue | undefined { return this.issues()[field]; }
  issueCount(): number { return Object.keys(this.issues()).length; }
  revalidate(): void { if (this.submitted()) this.issues.set(validateQuotationDraft(this.draft, this.overrideFlags())); }
  canOverride(): boolean { return this.permissions.has('SAL.PRICING.OVERRIDE'); }
  pricingOf(line: QuotationDraftLineForm | undefined): LinePricing { return (line && this.pricing().get(line)) || NO_PRICING; }
  lineError(line: QuotationDraftLineForm): string | null { return this.lineErrors().get(line) ?? null; }
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
  ruleText(line: QuotationDraftLineForm): string {
    const state = this.pricingOf(line);
    if (state.kind !== 'rule') return '';
    return this.translate.instant(state.fromSaved ? 'sd.pricing.savedRule' : 'sd.pricing.rule', { id: state.ruleId,
      version: state.ruleVersion, price: trimDecimalText(state.unitPrice), ceiling: trimDecimalText(state.discountCeiling),
      validTo: state.validTo ?? '' });
  }
  duplicateIds(line: QuotationDraftLineForm): string {
    const state = this.pricingOf(line);
    return state.kind === 'duplicate' ? state.ruleIds.map(id => '#' + id).join(', ') : '';
  }
  failedKey(line: QuotationDraftLineForm): string {
    const state = this.pricingOf(line);
    return state.kind === 'failed' ? state.messageKey : '';
  }
  private overrideFlags(): boolean[] {
    return this.draft.lines.map((line, i) => this.needsOverride(i) || this.reasonDemanded.has(line));
  }
  private setPricing(line: QuotationDraftLineForm, state: LinePricing): void {
    this.pricing.update(old => new Map(old).set(line, state));
  }
  private quoteQuery(index: number) {
    const line = this.draft.lines[index];
    if (!line || !this.draft.customerId || !line.itemId || !/^[A-Z]{3}$/.test(line.currencyCode) ||
        !DATE.test(this.draft.businessDate)) return null;
    return { itemId: line.itemId, customerId: this.draft.customerId, currency: line.currencyCode,
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
  currencyChanged(index: number): void { this.revalidate(); this.requote(index); }
  /** Restores the quoted price and caps the discount at the ceiling, so no override is needed. */
  useRulePrice(index: number): void {
    const line = this.draft.lines[index];
    const state = this.pricingOf(line);
    if (!line || state.kind !== 'rule') return;
    line.unitPrice = trimDecimalText(state.unitPrice);
    if (compareDecimalText(line.discountRate, state.discountCeiling) > 0) line.discountRate = trimDecimalText(state.discountCeiling);
    this.revalidate();
  }
  errorKey(field: string): string { return `sd.quotationCreate.validation.${field}`; }
  itemLabel(index: number): string { return this.itemLabels().get(index) ?? ''; }
  taxLabel(index: number): string { return this.taxLabels().get(index) ?? ''; }
  startAnother(): void {
    this.epoch++; this.draft = emptyDraft(); this.customerLabel.set(''); this.itemLabels.set(new Map()); this.taxLabels.set(new Map());
    this.issues.set({}); this.submitted.set(false); this.saving.set(false);
    this.savedNo.set(null); this.uncertain.set(false); this.saveError.set(null); this.traceId.set(null);
    this.serverValidationFailure = null; this.serverDetails.set([]);
    this.loading.set(false); this.loadError.set(null); this.statusBlocked.set(false); this.conflict.set(false);
    this.pricing.set(new Map()); this.lineErrors.set(new Map()); this.reasonDemanded.clear();
    this.version = 0; this.baseline = JSON.stringify(this.draft);
  }
  loadSaved(): void {
    if (!this.editId || this.loading()) return;
    const epoch = ++this.epoch;
    this.loading.set(true); this.loadError.set(null); this.conflict.set(false); this.traceId.set(null);
    this.api.getEditable(this.editId).subscribe({
      next: saved => { if (epoch !== this.epoch) return;
        this.loading.set(false); this.applySaved(saved); },
      error: failure => { if (epoch !== this.epoch) return;
        this.loading.set(false); this.loadError.set(apiErrorKey(failure, ['SAL.QUOTATION.NOT_FOUND', 'COMMON.PERMISSION.DENIED']));
        this.traceId.set(apiErrorTraceId(failure)); },
    });
  }
  private applySaved(saved: SalesQuotationEditableDetail): void {
    this.statusBlocked.set(saved.status !== 'Draft' && saved.status !== 'Rejected');
    this.version = saved.version;
    this.draft = { customerId: saved.customerId, businessDate: saved.businessDate,
      expiryDate: saved.expiryDate, remark: saved.remark ?? '',
      lines: saved.lines.map(line => ({ lineNo: line.lineNo, itemId: line.itemId,
        quantity: String(line.quantity), unitPrice: String(line.unitPrice),
        discountRate: String(line.discountRate), currencyCode: saved.currencyCode,
        taxRateId: line.taxRateId, pricingRuleId: line.pricingRuleId ?? null,
        pricingRuleVersion: line.pricingRuleId != null ? line.pricingRuleVersion ?? null : null,
        overrideReason: line.pricingMode === 'Override' ? line.overrideReason ?? '' : '' })) };
    // Saved provenance explains the price; the saved rule pair is resent as the hint (changed rule → 409 per line).
    this.pricing.set(new Map(this.draft.lines.map((line, i) => [line, this.savedPricing(saved.lines[i])])));
    this.lineErrors.set(new Map()); this.reasonDemanded.clear();
    this.customerLabel.set(`${saved.customerCode} · ${saved.customerName}`);
    this.itemLabels.set(new Map(saved.lines.map((line, i) => [i, `${line.itemCode} · ${line.itemName}`])));
    this.taxLabels.set(new Map(saved.lines.map((line, i) => [i, `${line.taxRatePercent}%`])));
    this.baseline = JSON.stringify(this.draft);
    this.savedNo.set(null); this.issues.set({}); this.submitted.set(false);
  }
  private savedPricing(line: SalesQuotationEditableDetail['lines'][number]): LinePricing {
    if (line.pricingRuleId != null && line.pricingRuleVersion != null && line.ruleUnitPrice != null &&
        line.ruleDiscountCeiling != null)
      return { kind: 'rule', ruleId: line.pricingRuleId, ruleVersion: line.pricingRuleVersion,
        unitPrice: String(line.ruleUnitPrice), discountCeiling: String(line.ruleDiscountCeiling), validTo: null, fromSaved: true };
    return line.pricingMode === 'Override' ? { kind: 'notFound' } : { kind: 'legacy' };
  }
  addLine(): void {
    if (this.draft.lines.length >= 1000) return;
    const line = emptyLine(this.draft.lines.length + 1);
    const previous = this.draft.lines[this.draft.lines.length - 1];
    if (previous && /^[A-Z]{3}$/.test(previous.currencyCode)) line.currencyCode = previous.currencyCode;
    this.draft.lines.push(line);
    this.revalidate();
  }
  removeLine(index: number): void {
    if (this.draft.lines.length <= 1) return;
    this.draft.lines.splice(index, 1);
    this.draft.lines.forEach((line, i) => line.lineNo = i + 1);
    const labels = new Map<number, string>();
    for (const [i, label] of this.itemLabels()) if (i !== index) labels.set(i > index ? i - 1 : i, label);
    this.itemLabels.set(labels); this.revalidate();
    const taxLabels = new Map<number, string>();
    for (const [i, label] of this.taxLabels()) if (i !== index) taxLabels.set(i > index ? i - 1 : i, label);
    this.taxLabels.set(taxLabels);
  }
  /** ADR-167: choices come from SD option endpoints (quotation/order writer permission, no MD view needed). */
  pickCustomer(): void {
    const epoch = this.epoch;
    this.dialogs.select<SalesDocumentOption>({ titleKey: 'sd.quotationCreate.chooseCustomer',
      descriptionKey: 'sd.documentOptions.customerScope',
      columns: [{ key: 'code', labelKey: 'sd.quotationCreate.code', value: row => row.code },
        { key: 'name', labelKey: 'sd.quotationCreate.name', value: row => row.name }],
      load: query => this.options.customers(query), rowKey: row => row.id }).subscribe(rows => {
      if (epoch !== this.epoch || !rows?.[0]) return;
      const row = rows[0]; this.draft.customerId = row.id;
      this.customerLabel.set(`${row.code} · ${row.name}`);
      this.revalidate(); this.requoteAll();
    });
  }
  pickItem(index: number): void {
    const epoch = this.epoch;
    this.dialogs.select<SalesDocumentOption>({ titleKey: 'sd.quotationCreate.chooseItem',
      descriptionKey: 'sd.documentOptions.itemScope',
      columns: [{ key: 'code', labelKey: 'sd.quotationCreate.code', value: row => row.code },
        { key: 'name', labelKey: 'sd.quotationCreate.name', value: row => row.name }],
      load: query => this.options.items(query), rowKey: row => row.id }).subscribe(rows => {
      if (epoch !== this.epoch || !rows?.[0] || !this.draft.lines[index]) return;
      const row = rows[0]; this.draft.lines[index].itemId = row.id;
      this.itemLabels.update(old => new Map(old).set(index, `${row.code} · ${row.name}`));
      this.revalidate(); this.requote(index);
    });
  }
  /** True when the business date is a full date, so the output tax rates effective on it can be listed. */
  taxDateReady(): boolean { return DATE.test(this.draft.businessDate); }
  /** ADR-167: one dialog of the output tax rates effective on the business date (code, name, rate). */
  pickTaxRate(index: number): void {
    const epoch = this.epoch;
    const date = this.draft.businessDate;
    if (!DATE.test(date)) return;
    this.dialogs.select<SalesOutputTaxOption>({ titleKey: 'sd.quotationCreate.chooseTaxRate',
      descriptionKey: 'sd.documentOptions.taxScope',
      columns: [{ key: 'code', labelKey: 'sd.quotationCreate.code', value: row => row.code },
        { key: 'name', labelKey: 'sd.quotationCreate.name', value: row => row.name },
        { key: 'rate', labelKey: 'sd.quotationCreate.rate', value: row => `${row.ratePercent}%` }],
      load: query => this.options.outputTaxRates(date, query), rowKey: row => row.id }).subscribe(rates => {
      if (epoch !== this.epoch || !rates?.[0] || !this.draft.lines[index]) return;
      const rate = rates[0]; this.draft.lines[index].taxRateId = rate.id;
      this.taxLabels.update(old => new Map(old).set(index, `${rate.code} · ${rate.ratePercent}%`));
      this.revalidate();
    });
  }
  save(): void {
    if (this.saving() || this.savedNo() || this.uncertain() || this.loading() || this.loadError() || this.statusBlocked() || this.conflict()) return;
    this.submitted.set(true);
    const issues = validateQuotationDraft(this.draft, this.overrideFlags());
    this.issues.set(issues);
    if (Object.keys(issues).length) {
      setTimeout(() => (this.host.nativeElement as HTMLElement).querySelector<HTMLElement>('[aria-invalid="true"]')?.focus(), 0);
      return;
    }
    const epoch = this.epoch;
    this.saving.set(true); this.saveError.set(null); this.traceId.set(null);
    this.serverValidationFailure = null; this.serverDetails.set([]); this.lineErrors.set(new Map());
    const request = this.editId ? this.api.replace(this.editId, this.version, this.draft) : this.api.create(this.draft);
    request.subscribe({
      next: saved => { if (epoch !== this.epoch) return;
        this.saving.set(false);
        if (this.editId) {
          this.api.getEditable(this.editId).subscribe({
            next: current => { if (epoch !== this.epoch) return;
              this.applySaved(current); this.savedNo.set(current.documentNo); },
            error: failure => { if (epoch !== this.epoch) return;
              this.uncertain.set(true); this.traceId.set(apiErrorTraceId(failure)); this.loadError.set(null); },
          });
        } else this.savedNo.set(saved.documentNo); },
      error: failure => { if (epoch !== this.epoch) return;
        const status = (failure as { status?: number })?.status;
        const known = ['MD.CUSTOMER.NOT_APPROVED', 'MD.CUSTOMER.NOT_FOUND', 'MD.ITEM.NOT_FOUND',
          'MD.CURRENCY.NOT_FOUND', 'MD.CURRENCY.INACTIVE', 'MD.EXCHANGE_RATE.NO_BASE_CURRENCY',
          'MD.TAX_RATE.NOT_FOUND', 'MD.TAX_RATE.DIRECTION_MISMATCH', 'COMMON.PERMISSION.DENIED'];
        const pricingFailure = pricingFailureOf(failure);
        if (pricingFailure) { this.showPricingFailure(pricingFailure); this.saving.set(false); return; }
        const key = apiErrorKey(failure, known);
        this.uncertain.set(status === 0 || (status != null && status >= 500));
        if (this.editId && status === 409) this.conflict.set(true);
        this.serverValidationFailure = status === 400 ? failure : null;
        if (status === 400) this.demandReasons(failure);
        this.serverDetails.set(this.serverValidationFailure ? this.validationDetails(failure) : []);
        this.saveError.set(this.uncertain() || (this.editId && status === 409) ? null :
          key !== 'errors.http.unknown' ? key : status === 409 ? 'sd.quotationCreate.conflict' :
            status === 422 ? 'sd.quotationCreate.rejected' : key);
        this.traceId.set(apiErrorTraceId(failure)); this.saving.set(false); },
    });
  }
  /** 403/409 pricing rejections name one line (ADR-160 `lineNo`); never treated as an edit version conflict. */
  private showPricingFailure(failure: DocumentPricingFailure): void {
    const index = this.draft.lines.findIndex(line => line.lineNo === failure.lineNo);
    const line = this.draft.lines[index];
    const lineKey = failure.code === 'SAL.PRICING.OVERRIDE_REQUIRED' ? 'sd.pricing.lineOverrideRequired'
      : failure.code === 'SAL.PRICING.STALE_QUOTE' ? 'sd.pricing.lineStale' : 'sd.pricing.lineDuplicate';
    this.saveError.set(`errors.${failure.code}`);
    this.serverDetails.set(line ? [this.translate.instant('sd.quotationCreate.line', { number: index + 1 }) + ': ' +
      this.translate.instant(lineKey)] : []);
    if (!line) return;
    this.lineErrors.update(old => new Map(old).set(line, lineKey));
    if (failure.code === 'SAL.PRICING.STALE_QUOTE') this.requote(index);
    if (failure.code === 'SAL.PRICING.DUPLICATE_ACTIVE_RULE') {
      line.pricingRuleId = null; line.pricingRuleVersion = null;
      this.setPricing(line, { kind: 'duplicate', ruleIds: failure.ruleIds });
    }
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
    this.issues.set(validateQuotationDraft(this.draft, this.overrideFlags()));
  }
  private validationDetails(failure: unknown): string[] {
    const errors = (failure as { error?: { errors?: unknown } } | null)?.error?.errors;
    if (!errors || typeof errors !== 'object' || Array.isArray(errors)) return [];
    const known = new Set(['customerId', 'businessDate', 'expiryDate', 'remark', 'lines',
      'lineNo', 'itemId', 'quantity', 'unitPrice', 'discountRate', 'currencyCode', 'taxRateId', 'overrideReason']);
    return Object.entries(errors).slice(0, 20).map(([path, messages]) => {
      const line = /^lines\[(\d+)\]\.(\w+)$/.exec(path);
      const field = line ? line[2] : path;
      const mixed = field === 'currencyCode' && Array.isArray(messages) &&
        messages.some(message => typeof message === 'string' && /mixed.*currenc/i.test(message));
      const key = mixed ? this.errorKey('mixedCurrency') : known.has(field) ? this.errorKey(field)
        : 'sd.quotationCreate.validation.serverField';
      const prefix = line ? this.translate.instant('sd.quotationCreate.line', { number: Number(line[1]) + 1 }) + ': ' : '';
      return prefix + this.translate.instant(key);
    });
  }
}
