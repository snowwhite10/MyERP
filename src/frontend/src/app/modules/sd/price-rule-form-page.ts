import { ChangeDetectionStrategy, Component, DestroyRef, ElementRef, HostListener, effect, inject, signal, untracked } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import { TimeoutError } from 'rxjs';
import { NzAlertModule } from 'ng-zorro-antd/alert';
import { NzButtonModule } from 'ng-zorro-antd/button';
import { CompanyContextService } from '../../core/company/company-context.service';
import { apiErrorKey, apiErrorTraceId } from '../../core/http/api-error';
import { ApiErrorDisplay } from '../../shared/api-error-display';
import { ErpDialogService } from '../../shared/dialogs/dialog.service';
import { emptyPriceRuleDraft, PriceRuleDraft, priceRuleDraftFromRule, PriceRuleField, priceRuleSaveFailure,
  validatePriceRuleDraft } from './price-rule-form';
import { PricingCurrencyOption, PricingOption, SalesPriceRule, SalesPriceRuleService } from './price-rule.service';

type Outcome =
  | { kind: 'saved'; id: number; version: number }
  | { kind: 'overlap'; ids: number[] }
  | { kind: 'busy' | 'stale' | 'inactive' | 'missing' | 'uncertain' };

/**
 * WU-SD-005b1b2 interfaces 2.12 (create) and 2.13 (edit) for REQ_F4112.4.1 (ADR-157/158/162): one dated rule per
 * item, currency and company default or customer. Choices come from the pricing option endpoints (no MD permission).
 * Success is shown only after the server reply; an unknown outcome (network, timeout, 5xx) locks the form so the
 * user checks the list instead of creating a second rule.
 */
@Component({
  selector: 'erp-sales-price-rule-form-page',
  imports: [FormsModule, RouterLink, TranslatePipe, NzAlertModule, NzButtonModule, ApiErrorDisplay],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <section class="rule-form-page" data-testid="sd-price-rule-form">
      <header><h1>{{ (editId ? 'sd.priceRuleForm.editTitle' : 'sd.priceRuleForm.createTitle') | translate: { id: editId } }}</h1>
        <a routerLink="/sal/price-rules" data-testid="sd-price-rule-form-back">{{ 'sd.priceRuleForm.back' | translate }}</a></header>
      <p class="hint">{{ (editId ? 'sd.priceRuleForm.editIntro' : 'sd.priceRuleForm.createIntro') | translate }}</p>
      @if (loading()) { <p role="status" data-testid="sd-price-rule-form-loading">{{ 'sd.priceRuleForm.loading' | translate }}</p> }
      @if (loadError()) { <erp-api-error-display [messageKey]="loadError()!" [traceId]="traceId()" data-testid="sd-price-rule-form-load-error" />
        <button nz-button type="button" (click)="loadSaved()" data-testid="sd-price-rule-form-reload">{{ 'sd.priceRuleForm.retry' | translate }}</button> }
      @if (inactive()) { <nz-alert nzType="warning" nzShowIcon data-testid="sd-price-rule-form-inactive"
        [nzMessage]="'sd.priceRuleForm.inactiveRule' | translate: { id: editId }" /> }
      <div aria-live="polite" class="outcome">
        @if (outcome(); as shown) {
          @switch (shown.kind) {
            @case ('saved') { <nz-alert nzType="success" nzShowIcon data-testid="sd-price-rule-form-success"
              [nzMessage]="(editId ? 'sd.priceRuleForm.updated' : 'sd.priceRuleForm.created') | translate: { id: $any(shown).id, version: $any(shown).version }" /> }
            @case ('overlap') { <nz-alert nzType="warning" nzShowIcon data-testid="sd-price-rule-form-overlap"
              [nzMessage]="'sd.priceRuleForm.overlap' | translate" [nzDescription]="overlapDetail" />
              <ng-template #overlapDetail><span>{{ ($any(shown).ids.length ? 'sd.priceRuleForm.overlapIds' : 'sd.priceRuleForm.overlapNoIds') | translate: { ids: idList($any(shown).ids) } }}</span>
                <a routerLink="/sal/price-rules" [queryParams]="overlapQuery()" data-testid="sd-price-rule-form-overlap-list">{{ 'sd.priceRuleForm.overlapOpen' | translate }}</a></ng-template> }
            @case ('busy') { <nz-alert nzType="warning" nzShowIcon data-testid="sd-price-rule-form-busy"
              [nzMessage]="'sd.priceRuleForm.busy' | translate" /> }
            @case ('stale') { <nz-alert nzType="warning" nzShowIcon data-testid="sd-price-rule-form-stale"
              [nzMessage]="'sd.priceRuleForm.stale' | translate" /> }
            @case ('inactive') { <nz-alert nzType="warning" nzShowIcon data-testid="sd-price-rule-form-became-inactive"
              [nzMessage]="'sd.priceRuleForm.becameInactive' | translate" /> }
            @case ('missing') { <nz-alert nzType="error" nzShowIcon data-testid="sd-price-rule-form-missing"
              [nzMessage]="'sd.priceRuleForm.missing' | translate" /> }
            @case ('uncertain') { <nz-alert nzType="warning" nzShowIcon data-testid="sd-price-rule-form-uncertain"
              [nzMessage]="(editId ? 'sd.priceRuleForm.updateUnknown' : 'sd.priceRuleForm.createUnknown') | translate"
              [nzDescription]="traceId() ? 'Trace ID: ' + traceId() : null" /> }
          }
        }
      </div>
      @if (outcome()?.kind === 'saved' || outcome()?.kind === 'uncertain') { <div class="after">
        <a nz-button routerLink="/sal/price-rules" data-testid="sd-price-rule-form-to-list">{{ 'sd.priceRuleForm.toList' | translate }}</a>
        @if (!editId && outcome()?.kind === 'saved') { <button nz-button type="button" (click)="startAnother()" data-testid="sd-price-rule-form-another">{{ 'sd.priceRuleForm.another' | translate }}</button> }
      </div> }
      @if (['stale', 'inactive'].includes(outcome()?.kind ?? '') || (editId && outcome()?.kind === 'uncertain')) {
        <button nz-button type="button" (click)="loadSaved()" data-testid="sd-price-rule-form-refresh">{{ 'sd.priceRuleForm.refresh' | translate }}</button> }
      @if (saveError()) { <erp-api-error-display [messageKey]="saveError()!" [traceId]="traceId()" data-testid="sd-price-rule-form-error" /> }
      @if (!loadError() && !(editId && loading())) {
      <form (ngSubmit)="save()" novalidate class="form" data-testid="sd-price-rule-form-form">
        <div class="field"><span id="price-rule-form-item-label">{{ 'sd.priceRule.item' | translate }}</span>
          <div class="picker" role="group" tabindex="-1" aria-labelledby="price-rule-form-item-label" [attr.aria-invalid]="!!issue('itemId')">
            <span data-testid="sd-price-rule-form-item">{{ itemLabel() || ('sd.priceRuleForm.noItem' | translate) }}</span>
            <button nz-button type="button" [disabled]="!editable()" (click)="pickItem()" data-testid="sd-price-rule-form-item-pick">{{ 'sd.priceRule.chooseItem' | translate }}</button>
          </div>
          @if (issue('itemId')) { <span class="error" role="alert" data-testid="sd-price-rule-form-item-error">{{ issue('itemId')! | translate }}</span> }
        </div>
        <label class="field">{{ 'sd.priceRule.scope' | translate }}
          <select name="scope" [(ngModel)]="draft.scope" (ngModelChange)="changed()" [disabled]="!editable()" data-testid="sd-price-rule-form-scope">
            <option value="default">{{ 'sd.priceRuleForm.scopeDefault' | translate }}</option>
            <option value="customer">{{ 'sd.priceRuleForm.scopeCustomer' | translate }}</option>
          </select>
          <span class="hint">{{ (draft.scope === 'default' ? 'sd.priceRuleForm.scopeDefaultHint' : 'sd.priceRuleForm.scopeCustomerHint') | translate }}</span>
        </label>
        @if (draft.scope === 'customer') { <div class="field"><span id="price-rule-form-customer-label">{{ 'sd.priceRule.customer' | translate }}</span>
          <div class="picker" role="group" tabindex="-1" aria-labelledby="price-rule-form-customer-label" [attr.aria-invalid]="!!issue('customerId')">
            <span data-testid="sd-price-rule-form-customer">{{ customerLabel() || ('sd.priceRule.noCustomer' | translate) }}</span>
            <button nz-button type="button" [disabled]="!editable()" (click)="pickCustomer()" data-testid="sd-price-rule-form-customer-pick">{{ 'sd.priceRule.chooseCustomer' | translate }}</button>
          </div>
          @if (issue('customerId')) { <span class="error" role="alert" data-testid="sd-price-rule-form-customer-error">{{ issue('customerId')! | translate }}</span> }
        </div> }
        <label class="field">{{ 'sd.priceRule.currency' | translate }}
          <select name="currency" [(ngModel)]="draft.currencyCode" (ngModelChange)="changed()" [disabled]="!editable() || currenciesLoading()"
            [attr.aria-invalid]="!!issue('currencyCode')" data-testid="sd-price-rule-form-currency">
            <option value="">{{ (currenciesLoading() ? 'sd.priceRuleForm.loadingCurrencies' : 'sd.priceRuleForm.chooseCurrency') | translate }}</option>
            @for (option of currencies(); track option.code) { <option [value]="option.code">{{ option.code }} · {{ option.name }}</option> }
            @if (draft.currencyCode && !currencyOption()) { <option [value]="draft.currencyCode">{{ 'sd.priceRuleForm.currencyNotListed' | translate: { code: draft.currencyCode } }}</option> }
          </select>
          @if (currencyOption(); as chosen) { <span class="hint" data-testid="sd-price-rule-form-currency-hint">{{ 'sd.priceRuleForm.currencyHint' | translate: { code: chosen.code, places: chosen.decimalPlaces } }}</span> }
          @if (currencyError()) { <span class="error" role="alert" data-testid="sd-price-rule-form-currency-load-error">{{ currencyError()! | translate }}
            <button nz-button nzType="link" type="button" (click)="loadCurrencies()" data-testid="sd-price-rule-form-currency-retry">{{ 'sd.priceRuleForm.retry' | translate }}</button></span> }
          @if (issue('currencyCode')) { <span class="error" role="alert" data-testid="sd-price-rule-form-currency-error">{{ issue('currencyCode')! | translate }}</span> }
        </label>
        <label class="field">{{ 'sd.priceRule.validFrom' | translate }}
          <input type="date" name="validFrom" autocomplete="off" [(ngModel)]="draft.validFrom" (ngModelChange)="changed()" [disabled]="!editable()"
            [attr.aria-invalid]="!!issue('validFrom')" data-testid="sd-price-rule-form-from" />
          @if (issue('validFrom')) { <span class="error" role="alert" data-testid="sd-price-rule-form-from-error">{{ issue('validFrom')! | translate }}</span> }
        </label>
        <label class="field">{{ 'sd.priceRule.validTo' | translate }}
          <input type="date" name="validTo" autocomplete="off" [(ngModel)]="draft.validTo" (ngModelChange)="changed()" [disabled]="!editable()"
            [attr.aria-invalid]="!!issue('validTo')" data-testid="sd-price-rule-form-to" />
          <span class="hint">{{ 'sd.priceRuleForm.inclusiveHint' | translate }}</span>
          @if (issue('validTo')) { <span class="error" role="alert" data-testid="sd-price-rule-form-to-error">{{ issue('validTo')! | translate }}</span> }
        </label>
        <label class="field">{{ 'sd.priceRule.unitPrice' | translate }}
          <input inputmode="decimal" autocomplete="off" name="unitPrice" [(ngModel)]="draft.unitPrice" (ngModelChange)="changed()" [disabled]="!editable()"
            [attr.aria-invalid]="!!issue('unitPrice')" data-testid="sd-price-rule-form-price" />
          @if (issue('unitPrice')) { <span class="error" role="alert" data-testid="sd-price-rule-form-price-error">{{ issue('unitPrice')! | translate }}</span> }
        </label>
        <label class="field">{{ 'sd.priceRuleForm.discountCeiling' | translate }}
          <input inputmode="decimal" autocomplete="off" name="discountCeiling" [(ngModel)]="draft.discountCeiling" (ngModelChange)="changed()" [disabled]="!editable()"
            [attr.aria-invalid]="!!issue('discountCeiling')" data-testid="sd-price-rule-form-ceiling" />
          <span class="hint">{{ 'sd.priceRuleForm.ceilingHint' | translate }}</span>
          @if (issue('discountCeiling')) { <span class="error" role="alert" data-testid="sd-price-rule-form-ceiling-error">{{ issue('discountCeiling')! | translate }}</span> }
        </label>
        @if (submitted() && issueCount()) { <p class="error summary" role="alert" data-testid="sd-price-rule-form-errors">
          {{ 'sd.priceRuleForm.errors' | translate: { count: issueCount() } }}</p> }
        <div class="submit"><button nz-button nzType="primary" type="submit" [nzLoading]="saving()" [disabled]="!editable()"
          data-testid="sd-price-rule-form-save">{{ (editId ? 'sd.priceRuleForm.saveEdit' : 'sd.priceRuleForm.saveCreate') | translate }}</button>
          @if (lockReason()) { <span class="hint" data-testid="sd-price-rule-form-locked">{{ lockReason()! | translate }}</span> }</div>
      </form> }
    </section>`,
  styles: `.rule-form-page{display:grid;gap:16px;min-width:0;max-width:960px;color:var(--erp-color-text)}header{display:flex;justify-content:space-between;align-items:center;gap:12px;flex-wrap:wrap}h1{margin:0}.hint{color:var(--erp-color-text-secondary);margin:0}.outcome:empty{display:none}.after{display:flex;gap:8px;flex-wrap:wrap}.form{display:grid;grid-template-columns:repeat(2,minmax(0,1fr));gap:16px}.field{display:grid;align-content:start;gap:4px;min-width:0}.picker{display:flex;align-items:center;gap:8px;flex-wrap:wrap;min-height:32px}.picker span{overflow-wrap:anywhere}input,select{width:100%;min-height:32px;padding:4px 8px;background:var(--erp-color-bg-container);color:var(--erp-color-text);border:1px solid var(--erp-color-border);border-radius:4px}input[inputmode=decimal]{font-variant-numeric:tabular-nums}input:disabled,select:disabled{opacity:.55;cursor:not-allowed}input[aria-invalid=true],select[aria-invalid=true],.picker[aria-invalid=true] button{border-color:var(--erp-color-error-text)}.error{color:var(--erp-color-error-text);margin:0}.summary,.submit{grid-column:1/-1}.submit{display:flex;gap:12px;align-items:center;flex-wrap:wrap}.rule-form-page>button{justify-self:start}button:focus-visible,input:focus-visible,select:focus-visible,a:focus-visible,.picker:focus-visible{outline:2px solid var(--erp-color-primary);outline-offset:2px}@media(max-width:767px){.form{grid-template-columns:1fr}}`,
})
export class SalesPriceRuleFormPage {
  private readonly api = inject(SalesPriceRuleService);
  private readonly dialogs = inject(ErpDialogService);
  private readonly company = inject(CompanyContextService);
  private readonly host = inject(ElementRef<HTMLElement>);
  private readonly translate = inject(TranslateService);
  private readonly route = inject(ActivatedRoute);
  // An invalid edit URL must never fall through to the create API.
  readonly editId = this.route.snapshot.paramMap.has('id') ? (Number(this.route.snapshot.paramMap.get('id')) || -1) : 0;
  draft: PriceRuleDraft = emptyPriceRuleDraft();
  readonly itemLabel = signal(''); readonly customerLabel = signal('');
  readonly currencies = signal<PricingCurrencyOption[]>([]);
  readonly currenciesLoading = signal(false); readonly currencyError = signal<string | null>(null);
  readonly issues = signal<Partial<Record<PriceRuleField, string>>>({});
  readonly submitted = signal(false); readonly saving = signal(false);
  readonly loading = signal(false); readonly loadError = signal<string | null>(null);
  readonly inactive = signal(false);
  readonly outcome = signal<Outcome | null>(null);
  readonly saveError = signal<string | null>(null);
  readonly traceId = signal<string | null>(null);
  private version = 0; private baseline = JSON.stringify(emptyPriceRuleDraft()); private epoch = 0; private currencyEpoch = 0;

  constructor() {
    inject(DestroyRef).onDestroy(() => { this.epoch++; this.currencyEpoch++; });
    effect(() => { this.company.current(); untracked(() => {
      this.startAnother(); this.loadCurrencies(); if (this.editId) this.loadSaved();
    }); });
  }

  issue(field: PriceRuleField): string | undefined { return this.issues()[field]; }
  issueCount(): number { return Object.keys(this.issues()).length; }
  currencyOption(): PricingCurrencyOption | undefined { return this.currencies().find(c => c.code === this.draft.currencyCode); }
  idList(ids: number[]): string { return ids.map(id => `#${id}`).join(', '); }
  overlapQuery(): Record<string, unknown> {
    return { itemId: this.draft.itemId, currency: this.draft.currencyCode, active: true,
      ...(this.draft.scope === 'customer' ? { customerId: this.draft.customerId } : { defaultOnly: true }) };
  }

  /** Why Save is unavailable, shown next to the disabled button (MASTER_PROMPT 20.1). */
  lockReason(): string | null {
    if (this.saving() || this.loading()) return null;
    const kind = this.outcome()?.kind;
    if (kind === 'saved') return 'sd.priceRuleForm.lockedSaved';
    if (kind === 'uncertain') return 'sd.priceRuleForm.lockedUnknown';
    if (kind === 'stale' || kind === 'inactive' || kind === 'missing') return 'sd.priceRuleForm.lockedReload';
    return this.inactive() ? 'sd.priceRuleForm.lockedInactive' : null;
  }
  editable(): boolean {
    return !this.saving() && !this.loading() && !this.loadError() && !this.inactive() && this.lockReason() === null;
  }

  canLeave(): boolean { return !this.dirty() || window.confirm(this.translate.instant('sd.priceRuleForm.unsaved')); }
  @HostListener('window:beforeunload', ['$event'])
  beforeUnload(event: BeforeUnloadEvent): void { if (this.dirty()) event.preventDefault(); }
  private dirty(): boolean { return this.outcome()?.kind !== 'saved' && JSON.stringify(this.draft) !== this.baseline; }

  changed(): void {
    if (this.draft.scope === 'default' && this.draft.customerId !== null) { this.draft.customerId = null; this.customerLabel.set(''); }
    if (this.outcome()?.kind === 'overlap' || this.outcome()?.kind === 'busy') this.outcome.set(null);
    this.saveError.set(null);
    if (this.submitted()) this.issues.set(this.clientIssues());
  }

  startAnother(): void {
    this.epoch++;
    this.draft = emptyPriceRuleDraft(); this.baseline = JSON.stringify(this.draft); this.version = 0;
    this.itemLabel.set(''); this.customerLabel.set(''); this.issues.set({}); this.submitted.set(false);
    this.saving.set(false); this.loading.set(false); this.loadError.set(null); this.inactive.set(false);
    this.outcome.set(null); this.saveError.set(null); this.traceId.set(null);
  }

  loadCurrencies(): void {
    const epoch = ++this.currencyEpoch;
    this.currenciesLoading.set(true); this.currencyError.set(null);
    this.api.currencyOptions().subscribe({
      next: list => { if (epoch !== this.currencyEpoch) return; this.currencies.set(list); this.currenciesLoading.set(false); },
      error: failure => { if (epoch !== this.currencyEpoch) return;
        this.currenciesLoading.set(false);
        this.currencyError.set(failure instanceof TimeoutError ? 'sd.priceRuleForm.currencyTimeout' : apiErrorKey(failure, [])); },
    });
  }

  loadSaved(): void {
    if (!this.editId) return;
    const epoch = ++this.epoch;
    this.loading.set(true); this.loadError.set(null); this.outcome.set(null); this.saveError.set(null); this.traceId.set(null);
    if (this.editId < 0) { this.loading.set(false); this.loadError.set('sd.priceRuleForm.missing'); return; }
    this.api.get(this.editId).subscribe({
      next: rule => { if (epoch !== this.epoch) return; this.loading.set(false); this.apply(rule); },
      error: failure => { if (epoch !== this.epoch) return;
        this.loading.set(false);
        const status = (failure as { status?: number })?.status;
        this.loadError.set(failure instanceof TimeoutError ? 'sd.priceRuleForm.loadTimeout' :
          status === 404 ? 'sd.priceRuleForm.missing' : apiErrorKey(failure, []));
        this.traceId.set(apiErrorTraceId(failure)); },
    });
  }

  private apply(rule: SalesPriceRule): void {
    this.version = rule.version; this.inactive.set(!rule.isActive);
    this.draft = priceRuleDraftFromRule(rule); this.baseline = JSON.stringify(this.draft);
    this.itemLabel.set(rule.itemCode ? `${rule.itemCode} · ${rule.itemName ?? ''}`
      : this.translate.instant('sd.priceRule.deletedItem', { id: rule.itemId }));
    this.customerLabel.set(rule.customerId === null ? '' : rule.customerCode ? `${rule.customerCode} · ${rule.customerName ?? ''}`
      : this.translate.instant('sd.priceRule.unknownCustomer', { id: rule.customerId }));
    this.issues.set({}); this.submitted.set(false);
  }

  pickItem(): void {
    const epoch = this.epoch;
    this.dialogs.select<PricingOption>({ titleKey: 'sd.priceRule.chooseItem', descriptionKey: 'sd.priceRuleForm.activeItemsOnly',
      columns: [{ key: 'code', labelKey: 'sd.priceRule.code', value: row => row.code },
        { key: 'name', labelKey: 'sd.priceRule.name', value: row => row.name }],
      load: query => this.api.itemOptions(query), rowKey: row => row.id }).subscribe(rows => {
      if (epoch !== this.epoch || !rows?.[0]) return;
      this.draft.itemId = rows[0].id; this.itemLabel.set(`${rows[0].code} · ${rows[0].name}`); this.changed();
    });
  }

  pickCustomer(): void {
    const epoch = this.epoch;
    this.dialogs.select<PricingOption>({ titleKey: 'sd.priceRule.chooseCustomer', descriptionKey: 'sd.priceRule.activeCustomersOnly',
      columns: [{ key: 'code', labelKey: 'sd.priceRule.code', value: row => row.code },
        { key: 'name', labelKey: 'sd.priceRule.name', value: row => row.name }],
      load: query => this.api.customerOptions(query), rowKey: row => row.id }).subscribe(rows => {
      if (epoch !== this.epoch || !rows?.[0]) return;
      this.draft.customerId = rows[0].id; this.customerLabel.set(`${rows[0].code} · ${rows[0].name}`); this.changed();
    });
  }

  save(): void {
    if (!this.editable()) return;
    this.submitted.set(true);
    const issues = this.clientIssues();
    this.issues.set(issues);
    this.outcome.set(null); this.saveError.set(null); this.traceId.set(null);
    if (Object.keys(issues).length) { this.focusFirstInvalid(); return; }
    const epoch = this.epoch;
    this.saving.set(true);
    const request = this.editId ? this.api.update(this.editId, this.version, this.draft) : this.api.create(this.draft);
    request.subscribe({
      next: rule => { if (epoch !== this.epoch) return;
        this.saving.set(false); this.apply(rule);
        this.outcome.set({ kind: 'saved', id: rule.id, version: rule.version }); },
      error: failure => { if (epoch !== this.epoch) return;
        this.saving.set(false); this.traceId.set(apiErrorTraceId(failure)); this.showFailure(failure); },
    });
  }

  private showFailure(failure: unknown): void {
    const result = priceRuleSaveFailure(failure);
    switch (result.kind) {
      case 'overlap': this.outcome.set({ kind: 'overlap', ids: result.conflictingRuleIds }); break;
      case 'busy': case 'stale': case 'inactive': case 'missing': this.outcome.set({ kind: result.kind }); break;
      case 'unknown': this.outcome.set({ kind: 'uncertain' }); break;
      case 'reference': this.issues.set({ [result.field]: result.key }); this.focusFirstInvalid(); break;
      case 'validation': {
        const fields: Partial<Record<PriceRuleField, string>> = {};
        for (const field of result.fields) fields[field] = 'sd.priceRuleForm.validation.server';
        this.issues.set(fields);
        if (!result.fields.length || result.unknown) this.saveError.set('errors.COMMON.REQUEST.VALIDATION_FAILED');
        this.focusFirstInvalid(); break;
      }
      case 'denied': this.saveError.set('errors.COMMON.PERMISSION.DENIED'); break;
      default: this.saveError.set(apiErrorKey(failure, []) === 'errors.http.unknown' ? 'sd.priceRuleForm.rejected' : apiErrorKey(failure, []));
    }
  }

  private clientIssues(): Partial<Record<PriceRuleField, string>> {
    const issues: Partial<Record<PriceRuleField, string>> = {};
    for (const [field, code] of Object.entries(validatePriceRuleDraft(this.draft)))
      issues[field as PriceRuleField] = `sd.priceRuleForm.validation.${field}.${code}`;
    return issues;
  }

  private focusFirstInvalid(): void {
    setTimeout(() => (this.host.nativeElement as HTMLElement).querySelector<HTMLElement>('[aria-invalid="true"]')?.focus(), 0);
  }
}
