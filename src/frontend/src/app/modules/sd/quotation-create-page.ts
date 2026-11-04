import { ChangeDetectionStrategy, Component, DestroyRef, ElementRef, HostListener, effect, inject, signal, untracked } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import { NzAlertModule } from 'ng-zorro-antd/alert';
import { NzButtonModule } from 'ng-zorro-antd/button';
import { CompanyContextService } from '../../core/company/company-context.service';
import { apiErrorKey, apiErrorTraceId } from '../../core/http/api-error';
import { ApiErrorDisplay } from '../../shared/api-error-display';
import { ErpDialogService } from '../../shared/dialogs/dialog.service';
import { Customer, CustomerService } from '../md/customer.service';
import { Item, ItemService } from '../md/item.service';
import { TaxCode, TaxRate, TaxService } from '../md/tax.service';
import { QuotationDraftForm, QuotationDraftIssue, validateQuotationDraft } from './quotation-draft-form';
import { SalesQuotationService } from './quotation.service';

function emptyDraft(): QuotationDraftForm {
  return { customerId: null, businessDate: '', expiryDate: '', remark: '',
    lines: [{ lineNo: 1, itemId: null, quantity: '1', unitPrice: '0', discountRate: '0',
      currencyCode: 'TWD', taxRateId: null }] };
}

/** Create only: the server owns numbering, MD eligibility and saved money snapshots. */
@Component({
  selector: 'erp-sales-quotation-create-page',
  imports: [FormsModule, RouterLink, TranslatePipe, NzAlertModule, NzButtonModule, ApiErrorDisplay],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <section class="create-page" data-testid="sd-quote-create">
      <header><h1>{{ 'sd.quotationCreate.title' | translate }}</h1>
        <a routerLink="/sal/quotations">{{ 'sd.quotationCreate.back' | translate }}</a></header>
      <p class="hint">{{ 'sd.quotationCreate.intro' | translate }}</p>
      @if (savedNo()) { <nz-alert nzType="success" nzShowIcon data-testid="sd-quote-create-success"
        [nzMessage]="'sd.quotationCreate.saved' | translate: { number: savedNo() }" />
        <button nz-button type="button" (click)="startAnother()" data-testid="sd-quote-create-another">{{ 'sd.quotationCreate.another' | translate }}</button> }
      @if (uncertain()) { <nz-alert nzType="warning" nzShowIcon data-testid="sd-quote-create-uncertain"
        [nzMessage]="'sd.quotationCreate.outcomeUnknown' | translate"
        [nzDescription]="traceId() ? 'Trace ID: ' + traceId() : null" /> }
      @if (saveError()) { <erp-api-error-display data-testid="sd-quote-create-error" [messageKey]="saveError()!"
        [traceId]="traceId()" [details]="serverDetails()" /> }
      <form (ngSubmit)="save()" novalidate class="form" data-testid="sd-quote-create-form">
        <div class="field customer-field"><span id="quote-create-customer-label">{{ 'sd.quotationCreate.customer' | translate }}</span>
          <div role="group" aria-labelledby="quote-create-customer-label" class="picker" tabindex="-1"
            [attr.aria-invalid]="!!issue('customerId')">
            <span data-testid="sd-quote-create-customer">{{ customerLabel() || ('sd.quotationCreate.noCustomer' | translate) }}</span>
            <button nz-button type="button" data-testid="sd-quote-create-customer-pick" [disabled]="!!savedNo() || saving()"
              (click)="pickCustomer()">{{ 'sd.quotationCreate.chooseCustomer' | translate }}</button>
          </div>
          @if (issue('customerId')) { <p class="error" role="alert">{{ errorKey('customerId') | translate }}</p> }
        </div>
        <label class="field">{{ 'sd.quotationCreate.businessDate' | translate }}
          <input type="date" name="businessDate" autocomplete="off" [(ngModel)]="draft.businessDate" (ngModelChange)="revalidate()" data-testid="sd-quote-create-date"
            [disabled]="!!savedNo() || saving()" [attr.aria-invalid]="!!issue('businessDate')" />
          @if (issue('businessDate')) { <span class="error" role="alert">{{ errorKey('businessDate') | translate }}</span> }
        </label>
        <label class="field">{{ 'sd.quotationCreate.expiryDate' | translate }}
          <input type="date" name="expiryDate" autocomplete="off" [(ngModel)]="draft.expiryDate" (ngModelChange)="revalidate()" data-testid="sd-quote-create-expiry"
            [disabled]="!!savedNo() || saving()" [attr.aria-invalid]="!!issue('expiryDate')" />
          @if (issue('expiryDate')) { <span class="error" role="alert">{{ errorKey('expiryDate') | translate }}</span> }
        </label>
        <label class="field remark">{{ 'sd.quotationCreate.remark' | translate }}
          <textarea name="remark" autocomplete="off" [(ngModel)]="draft.remark" (ngModelChange)="revalidate()" rows="2" data-testid="sd-quote-create-remark"
            [disabled]="!!savedNo() || saving()" [attr.aria-invalid]="!!issue('remark')"></textarea>
          @if (issue('remark')) { <span class="error" role="alert">{{ errorKey('remark') | translate }}</span> }
        </label>
        <section class="lines" aria-labelledby="quote-create-lines-label">
          <div class="line-head"><h2 id="quote-create-lines-label">{{ 'sd.quotationCreate.lines' | translate }}</h2>
            <button nz-button type="button" (click)="addLine()" [disabled]="!!savedNo() || saving() || draft.lines.length >= 1000"
              data-testid="sd-quote-create-add">{{ 'sd.quotationCreate.addLine' | translate }}</button></div>
          @for (line of draft.lines; track $index; let i = $index) { <fieldset class="line">
            <legend>{{ 'sd.quotationCreate.line' | translate: { number: i + 1 } }}</legend>
            <div class="line-grid">
              <div class="field"><span [id]="'quote-create-item-label-' + i">{{ 'sd.quotationCreate.item' | translate }}</span>
                <div class="picker" role="group" tabindex="-1" [attr.aria-labelledby]="'quote-create-item-label-' + i"
                  [attr.aria-invalid]="!!issue(lineField(i, 'itemId'))">
                  <span>{{ itemLabel(i) || ('sd.quotationCreate.noItem' | translate) }}</span>
                  <button nz-button type="button" [attr.data-testid]="'sd-quote-create-item-pick-' + i"
                    [disabled]="!!savedNo() || saving()" (click)="pickItem(i)">{{ 'sd.quotationCreate.chooseItem' | translate }}</button>
                </div>
                @if (issue(lineField(i, 'itemId'))) { <span class="error" role="alert">{{ errorKey('itemId') | translate }}</span> }
              </div>
              <label class="field">{{ 'sd.quotationCreate.quantity' | translate }}
                <input inputmode="decimal" autocomplete="off" [name]="'quantity-' + i" [(ngModel)]="line.quantity" (ngModelChange)="revalidate()" [disabled]="!!savedNo() || saving()"
                  [attr.data-testid]="'sd-quote-create-quantity-' + i" [attr.aria-invalid]="!!issue(lineField(i, 'quantity'))" />
                @if (issue(lineField(i, 'quantity'))) { <span class="error" role="alert">{{ errorKey('quantity') | translate }}</span> }
              </label>
              <label class="field">{{ 'sd.quotationCreate.unitPrice' | translate }}
                <input inputmode="decimal" autocomplete="off" [name]="'price-' + i" [(ngModel)]="line.unitPrice" (ngModelChange)="revalidate()" [disabled]="!!savedNo() || saving()"
                  [attr.data-testid]="'sd-quote-create-price-' + i" [attr.aria-invalid]="!!issue(lineField(i, 'unitPrice'))" />
                @if (issue(lineField(i, 'unitPrice'))) { <span class="error" role="alert">{{ errorKey('unitPrice') | translate }}</span> }
              </label>
              <label class="field">{{ 'sd.quotationCreate.discount' | translate }}
                <input inputmode="decimal" autocomplete="off" [name]="'discount-' + i" [(ngModel)]="line.discountRate" (ngModelChange)="revalidate()" [disabled]="!!savedNo() || saving()"
                  [attr.aria-invalid]="!!issue(lineField(i, 'discountRate'))" />
                @if (issue(lineField(i, 'discountRate'))) { <span class="error" role="alert">{{ errorKey('discountRate') | translate }}</span> }
              </label>
              <label class="field">{{ 'sd.quotationCreate.currency' | translate }}
                <input [name]="'currency-' + i" autocomplete="off" spellcheck="false" [(ngModel)]="line.currencyCode" (ngModelChange)="revalidate()" maxlength="3" [disabled]="!!savedNo() || saving()"
                  [attr.aria-invalid]="!!issue(lineField(i, 'currencyCode'))" />
                @if (issue(lineField(i, 'currencyCode'))) { <span class="error" role="alert">{{ errorKey('currencyCode') | translate }}</span> }
              </label>
              <div class="field"><span [id]="'quote-create-tax-label-' + i">{{ 'sd.quotationCreate.taxRate' | translate }}</span>
                <div class="picker" role="group" tabindex="-1" [attr.aria-labelledby]="'quote-create-tax-label-' + i"
                  [attr.aria-invalid]="!!issue(lineField(i, 'taxRateId'))">
                  <span>{{ taxLabel(i) || ('sd.quotationCreate.noTaxRate' | translate) }}</span>
                  <button nz-button type="button" [attr.data-testid]="'sd-quote-create-tax-pick-' + i"
                    [disabled]="!!savedNo() || saving()" (click)="pickTaxRate(i)">{{ 'sd.quotationCreate.chooseTaxRate' | translate }}</button>
                </div>
                @if (issue(lineField(i, 'taxRateId'))) { <span class="error" role="alert">{{ errorKey('taxRateId') | translate }}</span> }
              </div>
            </div>
            <button nz-button type="button" (click)="removeLine(i)" [disabled]="!!savedNo() || saving() || draft.lines.length === 1">
              {{ 'sd.quotationCreate.removeLine' | translate }}</button>
          </fieldset> }
          @if (issue('lines')) { <p class="error" role="alert">{{ errorKey('lines') | translate }}</p> }
        </section>
        @if (submitted() && issueCount()) { <p class="error" role="alert" data-testid="sd-quote-create-errors">
          {{ 'sd.quotationCreate.errors' | translate: { count: issueCount() } }}</p> }
        <button nz-button nzType="primary" type="submit" data-testid="sd-quote-create-save" [nzLoading]="saving()"
          [disabled]="!!savedNo() || saving() || uncertain()">{{ 'sd.quotationCreate.save' | translate }}</button>
      </form>
    </section>`,
  styles: `.create-page{display:grid;gap:16px;min-width:0;color:var(--erp-color-text)}header,.line-head{display:flex;justify-content:space-between;align-items:center;gap:12px;flex-wrap:wrap}h1,h2{margin:0}.hint{color:var(--erp-color-text-secondary);margin:0}.form{display:grid;grid-template-columns:repeat(2,minmax(0,1fr));gap:16px}.field{display:grid;align-content:start;gap:4px;min-width:0}.picker{display:flex;align-items:center;gap:8px;flex-wrap:wrap;min-height:32px}.picker span{overflow-wrap:anywhere}.remark,.lines,.form>button,.form>p{grid-column:1/-1}.line{border:1px solid var(--erp-color-border);border-radius:8px;padding:16px;margin:12px 0;min-width:0}.line-grid{display:grid;grid-template-columns:repeat(3,minmax(0,1fr));gap:12px;margin-bottom:12px}input,textarea{width:100%;min-height:32px;padding:4px 8px;background:var(--erp-color-bg-container);color:var(--erp-color-text);border:1px solid var(--erp-color-border);border-radius:4px}.error{color:var(--erp-status-error,#cf1322);margin:0}.form>button{justify-self:start}button:focus-visible,input:focus-visible,textarea:focus-visible,a:focus-visible{outline:2px solid var(--erp-color-primary);outline-offset:2px}@media(max-width:900px){.line-grid{grid-template-columns:repeat(2,minmax(0,1fr))}}@media(max-width:767px){.form,.line-grid{grid-template-columns:1fr}}`,
})
export class SalesQuotationCreatePage {
  private readonly api = inject(SalesQuotationService);
  private readonly customers = inject(CustomerService);
  private readonly items = inject(ItemService);
  private readonly taxes = inject(TaxService);
  private readonly dialogs = inject(ErpDialogService);
  private readonly company = inject(CompanyContextService);
  private readonly destroy = inject(DestroyRef);
  private readonly host = inject(ElementRef<HTMLElement>);
  private readonly translate = inject(TranslateService);
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
  private epoch = 0;
  private serverValidationFailure: unknown = null;
  constructor() {
    this.destroy.onDestroy(() => this.epoch++);
    const language = this.translate.onLangChange.subscribe(() => {
      if (this.serverValidationFailure) this.serverDetails.set(this.validationDetails(this.serverValidationFailure));
    });
    this.destroy.onDestroy(() => language.unsubscribe());
    effect(() => { this.company.current(); untracked(() => this.startAnother()); });
  }
  lineField(index: number, name: string): string { return `lines[${index}].${name}`; }
  canLeave(): boolean { return !this.dirty() || window.confirm(this.translate.instant('sd.quotationCreate.unsaved')); }
  @HostListener('window:beforeunload', ['$event'])
  beforeUnload(event: BeforeUnloadEvent): void {
    if (this.dirty()) event.preventDefault();
  }
  private dirty(): boolean { return !this.savedNo() && JSON.stringify(this.draft) !== JSON.stringify(emptyDraft()); }
  issue(field: string): QuotationDraftIssue | undefined { return this.issues()[field]; }
  issueCount(): number { return Object.keys(this.issues()).length; }
  revalidate(): void { if (this.submitted()) this.issues.set(validateQuotationDraft(this.draft)); }
  errorKey(field: string): string { return `sd.quotationCreate.validation.${field}`; }
  itemLabel(index: number): string { return this.itemLabels().get(index) ?? ''; }
  taxLabel(index: number): string { return this.taxLabels().get(index) ?? ''; }
  startAnother(): void {
    this.epoch++; this.draft = emptyDraft(); this.customerLabel.set(''); this.itemLabels.set(new Map()); this.taxLabels.set(new Map());
    this.issues.set({}); this.submitted.set(false); this.saving.set(false);
    this.savedNo.set(null); this.uncertain.set(false); this.saveError.set(null); this.traceId.set(null);
    this.serverValidationFailure = null; this.serverDetails.set([]);
  }
  addLine(): void {
    if (this.draft.lines.length >= 1000) return;
    this.draft.lines.push({ lineNo: this.draft.lines.length + 1, itemId: null, quantity: '1',
      unitPrice: '0', discountRate: '0', currencyCode: 'TWD', taxRateId: null });
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
  pickCustomer(): void {
    const epoch = this.epoch;
    this.dialogs.select<Customer>({ titleKey: 'sd.quotationCreate.chooseCustomer',
      columns: [{ key: 'code', labelKey: 'sd.quotationCreate.code', value: row => row.code },
        { key: 'name', labelKey: 'sd.quotationCreate.name', value: row => row.name }],
      load: query => this.customers.list(query), rowKey: row => row.id }).subscribe(rows => {
      if (epoch !== this.epoch || !rows?.[0]) return;
      const row = rows[0]; this.draft.customerId = row.id;
      this.customerLabel.set(`${row.code} · ${row.name}`);
      this.revalidate();
    });
  }
  pickItem(index: number): void {
    const epoch = this.epoch;
    this.dialogs.select<Item>({ titleKey: 'sd.quotationCreate.chooseItem',
      columns: [{ key: 'code', labelKey: 'sd.quotationCreate.code', value: row => row.code },
        { key: 'name', labelKey: 'sd.quotationCreate.name', value: row => row.name }],
      load: query => this.items.list(query), rowKey: row => row.id }).subscribe(rows => {
      if (epoch !== this.epoch || !rows?.[0] || !this.draft.lines[index]) return;
      const row = rows[0]; this.draft.lines[index].itemId = row.id;
      this.itemLabels.update(old => new Map(old).set(index, `${row.code} · ${row.name}`));
      this.revalidate();
    });
  }
  pickTaxRate(index: number): void {
    const epoch = this.epoch;
    this.dialogs.select<TaxCode>({ titleKey: 'sd.quotationCreate.chooseTaxCode',
      columns: [{ key: 'code', labelKey: 'sd.quotationCreate.code', value: row => row.code },
        { key: 'name', labelKey: 'sd.quotationCreate.name', value: row => row.name }],
      load: query => this.taxes.list(query), rowKey: row => row.id }).subscribe(codes => {
      if (epoch !== this.epoch || !codes?.[0]) return;
      const code = codes[0];
      this.dialogs.select<TaxRate>({ titleKey: 'sd.quotationCreate.chooseTaxRate',
        columns: [{ key: 'from', labelKey: 'sd.quotationCreate.effectiveFrom', value: row => row.effectiveFrom },
          { key: 'to', labelKey: 'sd.quotationCreate.effectiveTo', value: row => row.effectiveTo ?? '∞' },
          { key: 'rate', labelKey: 'sd.quotationCreate.rate', value: row => `${row.ratePercent}%` }],
        load: query => this.taxes.listRates(code.id, { ...query, on: this.draft.businessDate || null }),
        rowKey: row => row.id }).subscribe(rates => {
        if (epoch !== this.epoch || !rates?.[0] || !this.draft.lines[index]) return;
        const rate = rates[0]; this.draft.lines[index].taxRateId = rate.id;
        this.taxLabels.update(old => new Map(old).set(index, `${code.code} · ${rate.ratePercent}%`));
        this.revalidate();
      });
    });
  }
  save(): void {
    if (this.saving() || this.savedNo() || this.uncertain()) return;
    this.submitted.set(true);
    const issues = validateQuotationDraft(this.draft);
    this.issues.set(issues);
    if (Object.keys(issues).length) {
      setTimeout(() => (this.host.nativeElement as HTMLElement).querySelector<HTMLElement>('[aria-invalid="true"]')?.focus(), 0);
      return;
    }
    const epoch = this.epoch;
    this.saving.set(true); this.saveError.set(null); this.traceId.set(null);
    this.serverValidationFailure = null; this.serverDetails.set([]);
    this.api.create(this.draft).subscribe({
      next: saved => { if (epoch !== this.epoch) return;
        this.saving.set(false); this.savedNo.set(saved.documentNo); },
      error: failure => { if (epoch !== this.epoch) return;
        const status = (failure as { status?: number })?.status;
        const known = ['MD.CUSTOMER.NOT_APPROVED', 'MD.CUSTOMER.NOT_FOUND', 'MD.ITEM.NOT_FOUND',
          'MD.CURRENCY.NOT_FOUND', 'MD.CURRENCY.INACTIVE', 'MD.EXCHANGE_RATE.NO_BASE_CURRENCY',
          'MD.TAX_RATE.NOT_FOUND', 'MD.TAX_RATE.DIRECTION_MISMATCH', 'COMMON.PERMISSION.DENIED'];
        const key = apiErrorKey(failure, known);
        this.uncertain.set(status === 0 || (status != null && status >= 500));
        this.serverValidationFailure = status === 400 ? failure : null;
        this.serverDetails.set(this.serverValidationFailure ? this.validationDetails(failure) : []);
        this.saveError.set(this.uncertain() ? null :
          key !== 'errors.http.unknown' ? key : status === 409 ? 'sd.quotationCreate.conflict' :
            status === 422 ? 'sd.quotationCreate.rejected' : key);
        this.traceId.set(apiErrorTraceId(failure)); this.saving.set(false); },
    });
  }
  private validationDetails(failure: unknown): string[] {
    const errors = (failure as { error?: { errors?: unknown } } | null)?.error?.errors;
    if (!errors || typeof errors !== 'object' || Array.isArray(errors)) return [];
    const known = new Set(['customerId', 'businessDate', 'expiryDate', 'remark', 'lines',
      'lineNo', 'itemId', 'quantity', 'unitPrice', 'discountRate', 'currencyCode', 'taxRateId']);
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
