import { ChangeDetectionStrategy, Component, DestroyRef, ElementRef, HostListener, effect, inject, signal, untracked } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import { TimeoutError } from 'rxjs';
import { NzAlertModule } from 'ng-zorro-antd/alert';
import { NzButtonModule } from 'ng-zorro-antd/button';
import { CompanyContextService } from '../../core/company/company-context.service';
import { PermissionService } from '../../core/auth/permission.service';
import { apiErrorKey, apiErrorTraceId, apiValidationDetails } from '../../core/http/api-error';
import { ApiErrorDisplay } from '../../shared/api-error-display';
import { ErpDialogService } from '../../shared/dialogs/dialog.service';
import { Customer, CustomerService } from '../md/customer.service';
import { Item, ItemService } from '../md/item.service';
import { TaxCode, TaxRate, TaxService } from '../md/tax.service';
import { OrderDraftForm, OrderDraftIssue, validateOrderDraft } from './order-draft-form';
import { SalesOrderDetail, SalesOrderService } from './order.service';
import { SalesQuotationService, SalesQuotationSummary } from './quotation.service';

function blank(): OrderDraftForm {
  return { customerId: null, businessDate: '', currencyCode: 'TWD', remark: '',
    lines: [{ lineNo: 1, itemId: null, quantity: '1', unitPrice: '0', discountRate: '0', taxRateId: null }] };
}

@Component({
  selector: 'erp-sales-order-create-page',
  imports: [FormsModule, RouterLink, TranslatePipe, NzAlertModule, NzButtonModule, ApiErrorDisplay],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <section class="create" data-testid="sd-order-create">
      <header><h1>{{ 'sd.orderCreate.title' | translate }}</h1>@if (permissions.has('SAL.ORDER.VIEW')) { <a routerLink="/sal/orders">{{ 'sd.orderCreate.back' | translate }}</a> }</header>
      <p>{{ 'sd.orderCreate.intro' | translate }}</p>
      @if (saved(); as value) { <nz-alert nzType="success" nzShowIcon data-testid="sd-order-create-success"
        [nzMessage]="'sd.orderCreate.saved' | translate: { number: value.documentNo }" />
        <p data-testid="sd-order-create-receipt">{{ value.documentNo }} · {{ value.customerName }} · {{ value.lines.length }} {{ 'sd.orderCreate.savedLines' | translate }}</p>
        <button nz-button type="button" (click)="reset()">{{ 'sd.orderCreate.another' | translate }}</button> }
      @if (uncertain()) { <nz-alert nzType="warning" nzShowIcon data-testid="sd-order-create-uncertain"
        [nzMessage]="(permissions.has('SAL.ORDER.VIEW') ? 'sd.orderCreate.unknown' : 'sd.orderCreate.unknownCreateOnly') | translate" /> }
      @if (error()) { <erp-api-error-display data-testid="sd-order-create-error" [messageKey]="error()!" [traceId]="traceId()" [details]="errorDetails()" /> }
      <form (ngSubmit)="save()" novalidate data-testid="sd-order-create-form">
        <div class="field"><span>{{ 'sd.orderCreate.customer' | translate }} <span aria-hidden="true">*</span></span><div class="picker">
          <span data-testid="sd-order-create-customer">{{ customerLabel() || ('sd.orderCreate.none' | translate) }}</span>
          <button nz-button type="button" (click)="pickCustomer()" [disabled]="locked()" data-testid="sd-order-create-customer-pick">{{ 'sd.orderCreate.chooseCustomer' | translate }}</button></div>
          @if (issue('customerId')) { <small role="alert">{{ 'sd.quotationCreate.validation.customerId' | translate }}</small> }</div>
        <label class="field"><span>{{ 'sd.orderCreate.date' | translate }} <span aria-hidden="true">*</span></span><input type="date" name="businessDate" [(ngModel)]="draft.businessDate" [disabled]="locked()" [attr.aria-invalid]="issue('businessDate') ? 'true' : null" data-testid="sd-order-create-date" />
          @if (issue('businessDate')) { <small role="alert">{{ 'sd.quotationCreate.validation.businessDate' | translate }}</small> }</label>
        <label class="field">{{ 'sd.orderCreate.currency' | translate }}<input name="currency" maxlength="3" autocomplete="off" [(ngModel)]="draft.currencyCode" [disabled]="locked()" data-testid="sd-order-create-currency" />
          @if (issue('currencyCode')) { <small role="alert">{{ 'sd.quotationCreate.validation.currencyCode' | translate }}</small> }</label>
        <label class="field wide">{{ 'sd.orderCreate.remark' | translate }}<textarea name="remark" maxlength="501" [(ngModel)]="draft.remark" [disabled]="locked()" data-testid="sd-order-create-remark"></textarea>
          @if (issue('remark')) { <small role="alert">{{ 'sd.quotationCreate.validation.remark' | translate }}</small> }</label>
        <section class="wide"><h2>{{ 'sd.orderCreate.lines' | translate }}</h2>
          @for (line of draft.lines; track $index; let i = $index) { <fieldset><legend>{{ 'sd.quotationCreate.line' | translate: { number: i + 1 } }}</legend><div class="line">
            <div class="field"><span>{{ 'sd.orderCreate.item' | translate }} <span aria-hidden="true">*</span></span><div class="picker"><span>{{ itemLabels().get(i) || ('sd.quotationCreate.noItem' | translate) }}</span>
              <button nz-button type="button" (click)="pickItem(i)" [disabled]="locked()" [attr.data-testid]="'sd-order-create-item-pick-' + i">{{ 'sd.quotationCreate.chooseItem' | translate }}</button></div>
              @if (issue(field(i, 'itemId'))) { <small role="alert">{{ 'sd.quotationCreate.validation.itemId' | translate }}</small> }</div>
            <label class="field">{{ 'sd.orderCreate.quantity' | translate }}<input inputmode="decimal" [name]="'quantity-' + i" [(ngModel)]="line.quantity" [disabled]="locked()" [attr.data-testid]="'sd-order-create-quantity-' + i" />
              @if (issue(field(i, 'quantity'))) { <small role="alert">{{ 'sd.quotationCreate.validation.quantity' | translate }}</small> }</label>
            <label class="field">{{ 'sd.orderCreate.price' | translate }}<input inputmode="decimal" [name]="'price-' + i" [(ngModel)]="line.unitPrice" [disabled]="locked()" [attr.data-testid]="'sd-order-create-price-' + i" />
              @if (issue(field(i, 'unitPrice'))) { <small role="alert">{{ 'sd.quotationCreate.validation.unitPrice' | translate }}</small> }</label>
            <label class="field">{{ 'sd.orderCreate.discount' | translate }}<input inputmode="decimal" [name]="'discount-' + i" [(ngModel)]="line.discountRate" [disabled]="locked()" [attr.data-testid]="'sd-order-create-discount-' + i" />
              @if (issue(field(i, 'discountRate'))) { <small role="alert">{{ 'sd.quotationCreate.validation.discountRate' | translate }}</small> }</label>
            <div class="field"><span>{{ 'sd.orderCreate.tax' | translate }} <span aria-hidden="true">*</span></span><div class="picker"><span>{{ taxLabels().get(i) || ('sd.quotationCreate.noTaxRate' | translate) }}</span>
              <button nz-button type="button" (click)="pickTax(i)" [disabled]="locked()" [attr.data-testid]="'sd-order-create-tax-pick-' + i">{{ 'sd.quotationCreate.chooseTaxRate' | translate }}</button></div>
              @if (issue(field(i, 'taxRateId'))) { <small role="alert">{{ 'sd.orderCreate.taxRequired' | translate }}</small> }</div>
          </div><button nz-button type="button" (click)="removeLine(i)" [disabled]="locked() || draft.lines.length === 1">{{ 'sd.quotationCreate.removeLine' | translate }}</button>
          @if (draft.lines.length === 1) { <small class="hint" role="status">{{ 'sd.orderCreate.oneLineMinimum' | translate }}</small> }</fieldset> }
          @if (issue('lines')) { <small role="alert">{{ 'sd.quotationCreate.validation.lines' | translate }}</small> }
          <button nz-button type="button" (click)="addLine()" [disabled]="locked() || draft.lines.length >= 1000" data-testid="sd-order-create-add-line">{{ 'sd.quotationCreate.addLine' | translate }}</button>
          @if (draft.lines.length >= 1000) { <small class="hint" role="status" data-testid="sd-order-create-line-limit">{{ 'sd.orderCreate.lineLimit' | translate }}</small> }
        </section>
        @if (submitted() && count()) { <p class="wide" role="alert" data-testid="sd-order-create-errors">{{ 'sd.quotationCreate.errors' | translate: { count: count() } }}</p> }
        <button nz-button nzType="primary" type="submit" [nzLoading]="saving()" [disabled]="locked()" data-testid="sd-order-create-save">{{ 'sd.orderCreate.save' | translate }}</button>
      </form>
      <section class="convert"><h2>{{ 'sd.orderCreate.convert' | translate }}</h2><p>{{ 'sd.orderCreate.convertHint' | translate }}</p>
        <button nz-button type="button" (click)="pickQuotation()" [disabled]="locked()">{{ 'sd.orderCreate.chooseQuotation' | translate }}</button>
        @if (quoteLabel()) { <p data-testid="sd-order-create-quotation">{{ quoteLabel() }}</p> }
        <button nz-button nzType="primary" type="button" (click)="convert()" [disabled]="locked() || !quotationId()" data-testid="sd-order-create-convert">{{ 'sd.orderCreate.convertSave' | translate }}</button>
        @if (!quotationId() && !locked()) { <small class="hint" role="status" data-testid="sd-order-create-convert-reason">{{ 'sd.orderCreate.selectQuoteFirst' | translate }}</small> }
      </section>
    </section>`,
  styles: `.create{display:grid;gap:16px;min-width:0;color:var(--erp-color-text)}header{display:flex;justify-content:space-between;gap:12px;align-items:center}h1,h2{margin:0}form{display:grid;grid-template-columns:repeat(2,minmax(0,1fr));gap:16px}.wide{grid-column:1/-1}.field{display:grid;gap:4px;min-width:0}.picker{display:flex;gap:8px;align-items:center;flex-wrap:wrap}.picker span{overflow-wrap:anywhere}input,textarea{width:100%;min-height:32px;padding:4px 8px;background:var(--erp-color-bg-container);color:var(--erp-color-text);border:1px solid var(--erp-color-border);border-radius:4px}fieldset{border:1px solid var(--erp-color-border);border-radius:8px;margin:12px 0;padding:16px}.line{display:grid;grid-template-columns:repeat(3,minmax(0,1fr));gap:12px;margin-bottom:12px}small{color:var(--erp-color-error-text)}small.hint{color:var(--erp-color-text-secondary);display:block;margin-top:8px}.convert{border-top:1px solid var(--erp-color-border);padding-top:16px;display:grid;gap:12px;justify-items:start}button:focus-visible,input:focus-visible,textarea:focus-visible,a:focus-visible{outline:2px solid var(--erp-color-primary);outline-offset:2px}@media(max-width:767px){form,.line{grid-template-columns:1fr}}`,
})
export class SalesOrderCreatePage {
  private readonly orders = inject(SalesOrderService);
  private readonly quotations = inject(SalesQuotationService);
  private readonly customers = inject(CustomerService);
  private readonly items = inject(ItemService);
  private readonly taxes = inject(TaxService);
  private readonly dialogs = inject(ErpDialogService);
  private readonly company = inject(CompanyContextService);
  readonly permissions = inject(PermissionService);
  private readonly translate = inject(TranslateService);
  private readonly destroy = inject(DestroyRef);
  private readonly host: ElementRef<HTMLElement> = inject(ElementRef);
  draft = blank();
  readonly customerLabel = signal(''); readonly itemLabels = signal(new Map<number, string>());
  readonly taxLabels = signal(new Map<number, string>()); readonly quotationId = signal<number | null>(null);
  readonly quoteLabel = signal(''); readonly issues = signal<Record<string, OrderDraftIssue>>({});
  readonly submitted = signal(false); readonly saving = signal(false); readonly saved = signal<SalesOrderDetail | null>(null);
  readonly uncertain = signal(false); readonly error = signal<string | null>(null); readonly traceId = signal<string | null>(null);
  readonly errorDetails = signal<readonly string[]>([]);
  private epoch = 0;
  constructor() {
    this.destroy.onDestroy(() => this.epoch++);
    effect(() => { this.company.current(); untracked(() => this.reset()); });
  }
  canLeave(): boolean { return !this.dirty() || window.confirm(this.translate.instant('sd.orderCreate.unsaved')); }
  @HostListener('window:beforeunload', ['$event']) beforeUnload(event: BeforeUnloadEvent): void { if (this.dirty()) event.preventDefault(); }
  private dirty(): boolean { return !this.saved() && (JSON.stringify(this.draft) !== JSON.stringify(blank()) || !!this.quotationId()); }
  locked(): boolean { return this.saving() || this.uncertain() || !!this.saved(); }
  field(i: number, name: string): string { return `lines[${i}].${name}`; }
  issue(name: string): OrderDraftIssue | undefined { return this.issues()[name]; }
  count(): number { return Object.keys(this.issues()).length; }
  reset(): void { this.epoch++; this.draft = blank(); this.customerLabel.set(''); this.itemLabels.set(new Map()); this.taxLabels.set(new Map());
    this.quotationId.set(null); this.quoteLabel.set(''); this.issues.set({}); this.submitted.set(false); this.saving.set(false);
    this.saved.set(null); this.uncertain.set(false); this.error.set(null); this.traceId.set(null); this.errorDetails.set([]); }
  addLine(): void { if (this.draft.lines.length < 1000) this.draft.lines.push({ lineNo: this.draft.lines.length + 1,
    itemId: null, quantity: '1', unitPrice: '0', discountRate: '0', taxRateId: null }); }
  removeLine(index: number): void { if (this.draft.lines.length < 2) return; this.draft.lines.splice(index, 1);
    this.draft.lines.forEach((line, i) => line.lineNo = i + 1);
    this.itemLabels.set(new Map([...this.itemLabels()].filter(([i]) => i !== index).map(([i, label]) => [i > index ? i - 1 : i, label])));
    this.taxLabels.set(new Map([...this.taxLabels()].filter(([i]) => i !== index).map(([i, label]) => [i > index ? i - 1 : i, label]))); }
  pickCustomer(): void { const epoch = this.epoch; this.dialogs.select<Customer>({ titleKey: 'sd.quotationCreate.chooseCustomer',
    columns: [{ key: 'code', labelKey: 'sd.quotationCreate.code', value: row => row.code }, { key: 'name', labelKey: 'sd.quotationCreate.name', value: row => row.name }],
    load: query => this.customers.list(query), rowKey: row => row.id }).subscribe(rows => {
      if (epoch !== this.epoch || !rows?.[0]) return; const row = rows[0]; this.draft.customerId = row.id;
      this.customerLabel.set(`${row.code} · ${row.name}`); }); }
  pickItem(index: number): void { const epoch = this.epoch; this.dialogs.select<Item>({ titleKey: 'sd.quotationCreate.chooseItem',
    columns: [{ key: 'code', labelKey: 'sd.quotationCreate.code', value: row => row.code }, { key: 'name', labelKey: 'sd.quotationCreate.name', value: row => row.name }],
    load: query => this.items.list(query), rowKey: row => row.id }).subscribe(rows => {
      if (epoch !== this.epoch || !rows?.[0] || !this.draft.lines[index]) return; const row = rows[0];
      this.draft.lines[index].itemId = row.id; this.itemLabels.update(map => new Map(map).set(index, `${row.code} · ${row.name}`)); }); }
  pickTax(index: number): void { const epoch = this.epoch; this.dialogs.select<TaxCode>({ titleKey: 'sd.quotationCreate.chooseTaxCode',
    columns: [{ key: 'code', labelKey: 'sd.quotationCreate.code', value: row => row.code }, { key: 'name', labelKey: 'sd.quotationCreate.name', value: row => row.name }],
    load: query => this.taxes.list(query), rowKey: row => row.id }).subscribe(codes => {
      if (epoch !== this.epoch || !codes?.[0]) return; const code = codes[0];
      this.dialogs.select<TaxRate>({ titleKey: 'sd.quotationCreate.chooseTaxRate',
        columns: [{ key: 'from', labelKey: 'sd.quotationCreate.effectiveFrom', value: row => row.effectiveFrom },
          { key: 'to', labelKey: 'sd.quotationCreate.effectiveTo', value: row => row.effectiveTo ?? '∞' },
          { key: 'rate', labelKey: 'sd.quotationCreate.rate', value: row => `${row.ratePercent}%` }],
        load: query => this.taxes.listRates(code.id, { ...query, on: this.draft.businessDate || null }), rowKey: row => row.id }).subscribe(rates => {
          if (epoch !== this.epoch || !rates?.[0] || !this.draft.lines[index]) return; const rate = rates[0];
          this.draft.lines[index].taxRateId = rate.id; this.taxLabels.update(map => new Map(map).set(index, `${code.code} · ${rate.ratePercent}%`)); }); }); }
  pickQuotation(): void { const epoch = this.epoch; this.dialogs.select<SalesQuotationSummary>({ titleKey: 'sd.orderCreate.chooseQuotation',
    columns: [{ key: 'documentNo', labelKey: 'sd.order.number', value: row => row.documentNo },
      { key: 'customerName', labelKey: 'sd.order.customer', value: row => row.customerName }],
    load: query => this.quotations.list({ ...query, status: 'Approved' }), rowKey: row => row.id }).subscribe(rows => {
      if (epoch !== this.epoch || !rows?.[0]) return; this.quotationId.set(rows[0].id);
      this.quoteLabel.set(`${rows[0].documentNo} · ${rows[0].customerName}`); }); }
  save(): void { if (this.locked()) return; this.submitted.set(true); const issues = validateOrderDraft(this.draft); this.issues.set(issues);
    if (Object.keys(issues).length) { this.focusFirstIssue(Object.keys(issues)[0]); return; } this.persist(this.orders.create(this.draft)); }
  private focusFirstIssue(field: string): void {
    const topLevel: Record<string, string> = { customerId: 'customer-pick', businessDate: 'date',
      currencyCode: 'currency', remark: 'remark', lines: 'add-line' };
    const match = /^lines\[(\d+)\]\.(\w+)$/.exec(field);
    const lineControl: Record<string, string> = { itemId: 'item-pick', quantity: 'quantity', unitPrice: 'price',
      discountRate: 'discount', taxRateId: 'tax-pick', lineNo: 'item-pick' };
    const target = `sd-order-create-${match ? `${lineControl[match[2]] ?? 'item-pick'}-${match[1]}` : topLevel[field] ?? 'save'}`;
    queueMicrotask(() => this.host.nativeElement.querySelector<HTMLElement>(`[data-testid="${target}"]`)?.focus());
  }
  private localisedDetails(failure: unknown): string[] {
    return apiValidationDetails(failure).map(detail => {
      const separator = detail.indexOf(': ');
      if (separator < 0) return this.translate.instant('sd.quotationCreate.validation.serverField');
      const field = detail.slice(0, separator);
      const reason = detail.slice(separator + 2);
      const fieldKey = field === 'quotationId' ? 'chooseQuotation' : field === 'customerId' ? 'customer' :
        field === 'businessDate' ? 'date' : field === 'currencyCode' ? 'currency' :
        field.includes('itemId') ? 'item' : field.includes('taxRateId') ? 'tax' :
        field.includes('quantity') ? 'quantity' : field.includes('unitPrice') ? 'price' : 'lines';
      const reasonKey = reason === 'Customer is inactive' ? 'customerInactive' :
        reason === 'Quotation is unavailable' ? 'quotationUnavailable' : 'serverField';
      return `${this.translate.instant(`sd.orderCreate.${fieldKey}`)}：${this.translate.instant(`sd.orderCreate.${reasonKey}`)}`;
    });
  }
  convert(): void { const id = this.quotationId(); if (!id || this.locked()) return; this.persist(this.orders.convertQuotation(id)); }
  private persist(request: ReturnType<SalesOrderService['create']>): void { const epoch = this.epoch; this.saving.set(true); this.error.set(null); this.errorDetails.set([]);
    request.subscribe({ next: created => { if (epoch !== this.epoch) return;
      if (!Number.isSafeInteger(created?.id) || created.id <= 0) { this.saving.set(false); this.uncertain.set(true); return; }
      this.orders.getCreated(created.id).subscribe({ next: saved => { if (epoch !== this.epoch) return;
        this.saving.set(false);
        if (saved?.id !== created.id || saved.status !== 'Draft' || !saved.documentNo?.trim() ||
          !Array.isArray(saved.lines) || saved.lines.length === 0) { this.uncertain.set(true); return; }
        this.saved.set(saved); }, error: failure => { if (epoch !== this.epoch) return;
          this.saving.set(false); this.uncertain.set(true); this.traceId.set(apiErrorTraceId(failure));
          const status = (failure as { status?: number })?.status;
          if (status != null && status >= 500) this.error.set('sd.orderCreate.serverUncertain'); } }); },
      error: failure => { if (epoch !== this.epoch) return; this.saving.set(false);
        const status = (failure as { status?: number })?.status;
        this.uncertain.set(failure instanceof TimeoutError || status === 0 || (status != null && status >= 500));
        this.traceId.set(apiErrorTraceId(failure));
        if (status != null && status >= 500) this.error.set('sd.orderCreate.serverUncertain');
        if (!this.uncertain()) { this.errorDetails.set(this.localisedDetails(failure));
          this.error.set(status === 422 ? 'sd.orderCreate.rejected' : apiErrorKey(failure, ['SAL.QUOTATION.ALREADY_CONVERTED', 'SAL.QUOTATION.NOT_FOUND',
            'MD.CUSTOMER.NOT_FOUND', 'MD.ITEM.NOT_FOUND', 'MD.TAX_RATE.NOT_FOUND', 'COMMON.PERMISSION.DENIED'])); } } }); }
}
