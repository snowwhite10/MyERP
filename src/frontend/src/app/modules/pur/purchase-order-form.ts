import { ChangeDetectionStrategy, ChangeDetectorRef, Component, effect, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormsModule } from '@angular/forms';
import { HttpErrorResponse } from '@angular/common/http';
import { Router, RouterLink } from '@angular/router';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import { NzButtonModule } from 'ng-zorro-antd/button';
import { CompanyContextService } from '../../core/company/company-context.service';
import { PermissionService } from '../../core/auth/permission.service';
import { apiErrorKey, apiErrorTraceId } from '../../core/http/api-error';
import { ApiErrorDisplay } from '../../shared/api-error-display';
import { validIsoDate } from '../../shared/date';
import { trimDecimalText } from '../../shared/decimal-text';
import { ErpDialogService } from '../../shared/dialogs/dialog.service';
import { HasUnsavedChanges } from '../../shared/document-page/unsaved-changes.guard';
import { PURCHASE_ORDER_ERROR_CODES, PurchaseOrderCreateContext, PurchaseOrderDraft, PurchaseOrderItemOption,
  PurchaseOrderLineDraft, PurchaseOrderRequisitionOption, PurchaseOrderService, PurchaseOrderSupplierOption } from './purchase-order.service';

/** Positive quantity: up to 13 integer digits and 6 decimals (decimal(19,6), BR-PUR-020). */
const QUANTITY = /^(?:0|[1-9]\d{0,12})(?:\.\d{1,6})?$/;
const MAX_LINES = 1000;
const MAX_REMARK = 500;
/** Codes the create screen explains in the user's language (POST 422 / 409 of 006a2 plus MD / tax / rate refusals). */
export const PURCHASE_ORDER_FORM_ERROR_CODES = [...PURCHASE_ORDER_ERROR_CODES, 'PUR.ORDER.CURRENCY_UNAVAILABLE',
  'PUR.ORDER.NO_BASE_CURRENCY', 'PUR.ORDER.BASE_CURRENCY_INACTIVE', 'PUR.ORDER.INVALID_RATE', 'PUR.ORDER.AMOUNT_OVERFLOW',
  'MD.EXCHANGE_RATE.NOT_FOUND', 'MD.TAX_RATE.NOT_FOUND', 'MD.TAX_RATE.INACTIVE', 'MD.TAX_RATE.DIRECTION_MISMATCH'];
/** 422 codes that belong to one header field, so the reason is also shown beside it. */
const FIELD_OF_CODE: Record<string, string> = { 'MD.SUPPLIER.NOT_FOUND': 'supplierId', 'MD.SUPPLIER.NOT_APPROVED': 'supplierId',
  'PUR.ORDER.CURRENCY_UNAVAILABLE': 'currencyCode', 'MD.EXCHANGE_RATE.NOT_FOUND': 'currencyCode' };
const LINE_FIELDS = ['itemId', 'requisitionLineId', 'quantity', 'unitPrice', 'taxRateId'] as const;
const FIELD_LABEL: Record<string, string> = { supplierId: 'supplier', businessDate: 'businessDate', currencyCode: 'currency',
  remark: 'remark', lines: 'lines', itemId: 'item', requisitionLineId: 'source', quantity: 'quantity', unitPrice: 'unitPrice',
  taxRateId: 'taxRate' };
const FOCUS_ID: Record<string, string> = { supplierId: 'supplier', businessDate: 'date', currencyCode: 'currency', remark: 'remark',
  lines: 'add', itemId: 'item', requisitionLineId: 'source', quantity: 'quantity', unitPrice: 'price', taxRateId: 'tax' };

const newLine = (): PurchaseOrderLineDraft => ({ itemId: null, requisitionLineId: null, quantity: '', unitPrice: '', taxRateId: null });
const newDraft = (): PurchaseOrderDraft => ({ supplierId: null, businessDate: '', currencyCode: '', remark: '', lines: [newLine()] });
const positiveId = (value: number | null) => Number.isSafeInteger(value) && (value ?? 0) > 0;

/** Exact comparison of two plain nonnegative decimal texts (never a JavaScript double). */
export function compareDecimalText(a: string, b: string): number {
  const scale = (text: string) => { const [whole, fraction = ''] = text.split('.'); return BigInt(whole + fraction.padEnd(6, '0').slice(0, 6)); };
  const x = scale(a); const y = scale(b);
  return x === y ? 0 : x > y ? 1 : -1;
}

/** Field paths of every local problem in display order (mirrors PurchaseOrderInput.Validate), so all can be explained at once. */
export function purchaseOrderFieldErrors(draft: PurchaseOrderDraft): string[] {
  const errors: string[] = [];
  if (!positiveId(draft.supplierId)) errors.push('supplierId');
  if (!validIsoDate(draft.businessDate)) errors.push('businessDate');
  if (!/^[A-Z]{3}$/.test(draft.currencyCode.trim().toUpperCase())) errors.push('currencyCode');
  if (draft.remark.trim().length > MAX_REMARK) errors.push('remark');
  if (draft.lines.length < 1 || draft.lines.length > MAX_LINES) errors.push('lines');
  draft.lines.forEach((line, index) => {
    const key = `lines.${index}`;
    if (!positiveId(line.itemId)) errors.push(`${key}.itemId`);
    const quantity = line.quantity.trim();
    if (!QUANTITY.test(quantity) || !/[1-9]/.test(quantity)) errors.push(`${key}.quantity`);
    if (!QUANTITY.test(line.unitPrice.trim())) errors.push(`${key}.unitPrice`);
    if (!positiveId(line.taxRateId)) errors.push(`${key}.taxRateId`);
  });
  return errors;
}

/**
 * 介面 4.2 Purchase Order Create Interface (WU-PUR-006f2b, REQ_IO_A4.2.1～3): header supplier / business date / currency /
 * remark, 1–1000 lines with item, optional approved requisition source (requested / converted / remaining shown exactly),
 * quantity, unit price and input tax valid on the business date. Saves the whole draft once; amounts are computed by the server.
 */
@Component({
  selector: 'erp-purchase-order-form',
  imports: [FormsModule, RouterLink, TranslatePipe, NzButtonModule, ApiErrorDisplay],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <section class="po-form" data-testid="pur-po-form">
      <header><h1>{{ 'pur.order.form.createTitle' | translate }}</h1>
        <a routerLink="/pur/purchase-orders">{{ 'pur.order.form.back' | translate }}</a></header>
      @if (error()) { <erp-api-error-display [messageKey]="error()!" [traceId]="errorTrace()" [details]="errorDetails()" data-testid="pur-po-form-error" /> }
      @if (saved(); as s) { <p role="status" class="success" data-testid="pur-po-form-success">{{ 'pur.order.form.saved' | translate:s }}</p> }
      <div class="header-fields">
        <div><span class="label">{{ 'pur.order.form.supplier' | translate }} <span class="required" aria-hidden="true">*</span></span>
          <button nz-button type="button" class="picker" (click)="chooseSupplier()" [disabled]="saving()" data-testid="pur-po-form-supplier"
            [attr.aria-label]="'pur.order.form.chooseSupplier' | translate" [attr.aria-invalid]="fieldError('supplierId')">{{ supplierLabel() }}</button>
          @if (fieldError('supplierId')) { <p class="field-error" role="alert" data-testid="pur-po-form-supplier-error">{{ 'pur.order.form.issue.supplierId' | translate }}</p> }
          @if (serverFieldError('supplierId'); as m) { <p class="field-error" role="alert" data-testid="pur-po-form-server-supplierId">{{ m }}</p> }</div>
        <div><label><span>{{ 'pur.order.form.businessDate' | translate }} <span class="required" aria-hidden="true">*</span></span>
          <input type="date" name="poBusinessDate" autocomplete="off" [ngModel]="draft.businessDate" (ngModelChange)="changeDate($event)"
            [disabled]="saving()" [attr.aria-invalid]="fieldError('businessDate')" data-testid="pur-po-form-date" /></label>
          @if (fieldError('businessDate')) { <p class="field-error" role="alert">{{ 'pur.order.form.issue.businessDate' | translate }}</p> }
          @if (serverFieldError('businessDate'); as m) { <p class="field-error" role="alert">{{ m }}</p> }</div>
        <div><label><span>{{ 'pur.order.form.currency' | translate }} <span class="required" aria-hidden="true">*</span></span>
          <select name="poCurrency" autocomplete="off" [(ngModel)]="draft.currencyCode" [disabled]="saving() || !context()"
            [attr.aria-invalid]="fieldError('currencyCode')" data-testid="pur-po-form-currency">
            <option value="">—</option>
            @for (c of context()?.currencies ?? []; track c.code) { <option [value]="c.code">{{ c.code }} · {{ c.name }}</option> }
          </select></label>
          @if (fieldError('currencyCode')) { <p class="field-error" role="alert">{{ 'pur.order.form.issue.currencyCode' | translate }}</p> }
          @if (serverFieldError('currencyCode'); as m) { <p class="field-error" role="alert" data-testid="pur-po-form-server-currencyCode">{{ m }}</p> }</div>
        <div class="wide"><label>{{ 'pur.order.form.remark' | translate }}
          <input name="poRemark" autocomplete="off" [(ngModel)]="draft.remark" [maxlength]="remarkMax + 1" [disabled]="saving()"
            [attr.aria-invalid]="fieldError('remark')" data-testid="pur-po-form-remark" /></label>
          @if (fieldError('remark')) { <p class="field-error" role="alert">{{ 'pur.order.form.issue.remark' | translate:{ max: remarkMax } }}</p> }
          @if (serverFieldError('remark'); as m) { <p class="field-error" role="alert">{{ m }}</p> }</div>
      </div>
      @if (contextLoading()) { <p role="status" class="hint">{{ 'pur.order.form.contextLoading' | translate }}</p> }
      @else if (contextError(); as e) { <erp-api-error-display [messageKey]="e.key" [traceId]="e.traceId" data-testid="pur-po-form-context-error" /> }
      @else if (!context()) { <p class="hint" data-testid="pur-po-form-context-hint">{{ 'pur.order.form.contextHint' | translate }}</p> }
      @if (staleCurrency()) { <p role="status" class="warning" data-testid="pur-po-form-stale-currency">{{ 'pur.order.form.staleCurrency' | translate }}</p> }
      @if (staleTaxLines(); as lines) { <p role="status" class="warning" data-testid="pur-po-form-stale">{{ 'pur.order.form.staleTax' | translate:{ lines } }}</p> }
      <h2>{{ 'pur.order.form.lines' | translate }}</h2>
      <p class="hint">{{ 'pur.order.form.amountNote' | translate }}</p>
      @if (fieldError('lines')) { <p class="field-error" role="alert">{{ 'pur.order.form.issue.lines' | translate:{ max: maxLines } }}</p> }
      @if (serverFieldError('lines'); as m) { <p class="field-error" role="alert">{{ m }}</p> }
      @for (line of draft.lines; track line; let i = $index) {
        <fieldset class="line" [attr.data-testid]="'pur-po-form-line-' + i">
          <legend>{{ 'pur.order.form.lineNo' | translate:{ number: i + 1 } }}</legend>
          <div><span class="label">{{ 'pur.order.form.item' | translate }} <span class="required" aria-hidden="true">*</span></span>
            <button nz-button type="button" class="picker" (click)="chooseItem(line)" [disabled]="saving()" [attr.data-testid]="'pur-po-form-item-' + i"
              [attr.aria-label]="'pur.order.form.chooseItem' | translate" [attr.aria-invalid]="fieldError('lines.' + i + '.itemId')">{{ itemLabel(line.itemId) }}</button>
            @if (fieldError('lines.' + i + '.itemId')) { <p class="field-error" role="alert">{{ 'pur.order.form.issue.itemId' | translate }}</p> }
            @if (serverFieldError('lines.' + i + '.itemId'); as m) { <p class="field-error" role="alert">{{ m }}</p> }</div>
          <div><span class="label">{{ 'pur.order.form.source' | translate }}</span>
            <span class="source-pick">
              <button nz-button type="button" class="picker" (click)="chooseSource(line)" [disabled]="saving() || !line.itemId"
                [attr.title]="!line.itemId ? ('pur.order.form.sourceNeedsItem' | translate) : null" [attr.data-testid]="'pur-po-form-source-' + i"
                [attr.aria-label]="'pur.order.form.chooseSource' | translate">{{ sourceOf(line)?.documentNo ?? ('pur.order.form.noSource' | translate) }}</button>
              @if (line.requisitionLineId) { <button nz-button nzType="link" type="button" (click)="clearSource(line)" [disabled]="saving()"
                [attr.data-testid]="'pur-po-form-source-clear-' + i">{{ 'pur.order.form.clearSource' | translate }}</button> }</span>
            @if (sourceOf(line); as s) { <p class="hint numeric" [attr.data-testid]="'pur-po-form-source-detail-' + i">{{ 'pur.order.form.sourceDetail' | translate:{
              number: s.documentNo, line: s.lineNo, quantity: plain(s.quantity), ordered: plain(s.orderedQuantity), remaining: plain(s.remainingQuantity) } }}</p>
              @if (overRemaining(line)) { <p class="warning" [attr.data-testid]="'pur-po-form-source-over-' + i">{{ 'pur.order.form.sourceOver' | translate:{ remaining: plain(s.remainingQuantity) } }}</p> } }
            @if (dropped.has(line)) { <p class="warning" role="status" [attr.data-testid]="'pur-po-form-source-dropped-' + i">{{ 'pur.order.form.sourceDropped' | translate }}</p> }
            @if (serverFieldError('lines.' + i + '.requisitionLineId'); as m) { <p class="field-error" role="alert">{{ m }}</p> }</div>
          <div><label><span>{{ 'pur.order.form.quantity' | translate }} <span class="required" aria-hidden="true">*</span></span>
            <input type="text" inputmode="decimal" [name]="'poQty' + i" autocomplete="off" [(ngModel)]="line.quantity" [disabled]="saving()"
              [attr.aria-invalid]="fieldError('lines.' + i + '.quantity')" [attr.data-testid]="'pur-po-form-quantity-' + i" /></label>
            @if (fieldError('lines.' + i + '.quantity')) { <p class="field-error" role="alert" [attr.data-testid]="'pur-po-form-quantity-error-' + i">{{ 'pur.order.form.issue.quantity' | translate }}</p> }
            @if (serverFieldError('lines.' + i + '.quantity'); as m) { <p class="field-error" role="alert" [attr.data-testid]="'pur-po-form-server-quantity-' + i">{{ m }}</p> }</div>
          <div><label><span>{{ 'pur.order.form.unitPrice' | translate }} <span class="required" aria-hidden="true">*</span></span>
            <input type="text" inputmode="decimal" [name]="'poPrice' + i" autocomplete="off" [(ngModel)]="line.unitPrice" [disabled]="saving()"
              [attr.aria-invalid]="fieldError('lines.' + i + '.unitPrice')" [attr.data-testid]="'pur-po-form-price-' + i" /></label>
            @if (fieldError('lines.' + i + '.unitPrice')) { <p class="field-error" role="alert">{{ 'pur.order.form.issue.unitPrice' | translate }}</p> }
            @if (serverFieldError('lines.' + i + '.unitPrice'); as m) { <p class="field-error" role="alert">{{ m }}</p> }</div>
          <div><label><span>{{ 'pur.order.form.taxRate' | translate }} <span class="required" aria-hidden="true">*</span></span>
            <select [name]="'poTax' + i" autocomplete="off" [(ngModel)]="line.taxRateId" [disabled]="saving() || !context()"
              [attr.aria-invalid]="fieldError('lines.' + i + '.taxRateId')" [attr.data-testid]="'pur-po-form-tax-' + i">
              <option [ngValue]="null">—</option>
              @for (r of context()?.taxRates ?? []; track r.id) { <option [ngValue]="r.id">{{ r.code }} · {{ r.name }} ({{ plain(r.ratePercent) }}%)</option> }
            </select></label>
            @if (fieldError('lines.' + i + '.taxRateId')) { <p class="field-error" role="alert">{{ 'pur.order.form.issue.taxRateId' | translate }}</p> }
            @if (serverFieldError('lines.' + i + '.taxRateId'); as m) { <p class="field-error" role="alert">{{ m }}</p> }</div>
          <button nz-button type="button" class="remove" (click)="removeLine(i)" [disabled]="draft.lines.length <= 1 || saving()"
            [attr.title]="draft.lines.length <= 1 ? ('pur.order.form.minLine' | translate) : null" [attr.data-testid]="'pur-po-form-remove-' + i">
            {{ 'pur.order.form.removeLine' | translate }}</button>
        </fieldset>
      }
      <div class="actions">
        <button nz-button type="button" (click)="addLine()" [disabled]="draft.lines.length >= maxLines || saving()"
          [attr.title]="draft.lines.length >= maxLines ? ('pur.order.form.maxLines' | translate:{ max: maxLines }) : null" data-testid="pur-po-form-add">
          {{ 'pur.order.form.addLine' | translate }}</button>
        <button nz-button nzType="primary" type="button" (click)="save()" [disabled]="saving()"
          [attr.title]="saving() ? ('pur.order.actionBusy' | translate) : null" data-testid="pur-po-form-save">
          {{ (saving() ? 'pur.order.actionBusy' : 'pur.order.form.save') | translate }}</button>
      </div>
    </section>`,
  styles: `.po-form{display:grid;gap:16px;color:var(--erp-color-text)}header,.actions{display:flex;gap:16px;align-items:center;flex-wrap:wrap}
    header{justify-content:space-between}h1{margin:0;font-size:20px;text-wrap:balance}h2{margin:0;font-size:16px}
    .header-fields{display:grid;grid-template-columns:repeat(3,minmax(180px,1fr));gap:12px}.header-fields .wide{grid-column:1/-1}
    .header-fields label,.line label,.header-fields>div,.line>div{display:grid;gap:4px;min-width:0;align-content:start}
    .required{color:var(--erp-color-error-text)}
    .line{display:grid;grid-template-columns:minmax(160px,1.2fr) minmax(180px,1.4fr) repeat(3,minmax(120px,1fr)) auto;gap:10px;align-items:start;
      padding:12px;margin:0;border:1px solid var(--erp-color-border);border-radius:4px}
    .line legend{font-size:13px;padding:0 4px;width:auto;border:0;margin:0;color:var(--erp-color-text-secondary,inherit)}
    input,select,.picker{min-height:32px;width:100%;background:var(--erp-color-bg-container);color:var(--erp-color-text);border:1px solid var(--erp-color-border);border-radius:4px}
    input:focus-visible,select:focus-visible{outline:2px solid var(--erp-color-primary)}
    .picker{overflow:hidden;text-overflow:ellipsis;white-space:nowrap;text-align:left}.source-pick{display:flex;gap:4px;align-items:center}
    button:disabled{cursor:not-allowed}input[inputmode=decimal],.numeric{font-variant-numeric:tabular-nums}
    [aria-invalid=true]{border-color:var(--erp-color-error-text)}.field-error{color:var(--erp-color-error-text);margin:2px 0;font-size:12px}
    .hint{margin:0;font-size:12px;color:var(--erp-color-text-secondary,inherit)}.warning{margin:2px 0;font-size:12px;color:var(--erp-color-warning-text,#ad6800)}
    .success{color:var(--erp-color-success);margin:0}.remove{align-self:end}
    @media(max-width:1100px){.line{grid-template-columns:repeat(2,minmax(0,1fr))}.header-fields{grid-template-columns:repeat(2,minmax(0,1fr))}}`,
})
export class PurchaseOrderForm implements HasUnsavedChanges {
  private readonly router = inject(Router);
  private readonly api = inject(PurchaseOrderService);
  private readonly dialogs = inject(ErpDialogService);
  private readonly translate = inject(TranslateService);
  private readonly company = inject(CompanyContextService);
  private readonly permissions = inject(PermissionService);
  private readonly cdr = inject(ChangeDetectorRef);
  readonly maxLines = MAX_LINES; readonly remarkMax = MAX_REMARK;
  readonly error = signal<string | null>(null); readonly errorTrace = signal<string | null>(null);
  readonly errorDetails = signal<string[]>([]); readonly fieldErrors = signal<string[]>([]);
  readonly serverFieldErrors = signal<Record<string, string>>({});
  readonly saving = signal(false);
  readonly saved = signal<{ number: string; count: number } | null>(null);
  readonly context = signal<PurchaseOrderCreateContext | null>(null);
  readonly contextLoading = signal(false);
  readonly contextError = signal<{ key: string; traceId: string | null } | null>(null);
  readonly staleTaxLines = signal<string | null>(null); readonly staleCurrency = signal(false);
  /** Lines whose requisition source was removed because the item changed (explained until a new source is chosen). */
  readonly dropped = new Set<PurchaseOrderLineDraft>();
  draft = newDraft();
  private baseline = ''; private revision = 0; private contextRevision = 0;
  private lastServerError: unknown = null;
  private supplier: PurchaseOrderSupplierOption | null = null;
  private readonly items = new Map<number, PurchaseOrderItemOption>();
  private readonly sources = new WeakMap<PurchaseOrderLineDraft, PurchaseOrderRequisitionOption>();

  constructor() {
    this.translate.onLangChange.pipe(takeUntilDestroyed()).subscribe(() => { if (this.lastServerError) this.explain(this.lastServerError); });
    effect(() => { this.company.current(); this.reset(); });
  }

  /** Clears everything (also on company switch): choices from another company must never be saved. */
  private reset(): void {
    ++this.revision; ++this.contextRevision;
    this.draft = newDraft(); this.supplier = null; this.items.clear(); this.dropped.clear();
    this.context.set(null); this.contextLoading.set(false); this.contextError.set(null);
    this.staleTaxLines.set(null); this.staleCurrency.set(false);
    this.clearErrors(); this.fieldErrors.set([]); this.saving.set(false);
    this.baseline = JSON.stringify(this.draft); this.cdr.markForCheck();
  }

  supplierLabel(): string { const s = this.supplier;
    return s && s.id === this.draft.supplierId ? `${s.code} · ${s.name}` : this.translate.instant('pur.order.form.chooseSupplier'); }
  itemLabel(id: number | null): string { const item = id ? this.items.get(id) : null;
    return item ? `${item.code} · ${item.name}` : this.translate.instant('pur.order.form.chooseItem'); }
  sourceOf(line: PurchaseOrderLineDraft): PurchaseOrderRequisitionOption | null {
    const s = this.sources.get(line); return s && s.id === line.requisitionLineId ? s : null; }
  plain(value: string): string { return trimDecimalText(value); }
  /** Soft warning only: a draft does not reserve demand; SUBMIT refuses quantities above the approved remainder. */
  overRemaining(line: PurchaseOrderLineDraft): boolean {
    const s = this.sourceOf(line); const q = line.quantity.trim();
    return !!s && QUANTITY.test(q) && compareDecimalText(q, s.remainingQuantity) > 0;
  }
  fieldError(path: string): boolean { return this.fieldErrors().includes(path); }
  serverFieldError(path: string): string | null { return this.serverFieldErrors()[path] ?? null; }
  hasUnsavedChanges(): boolean { return JSON.stringify(this.draft) !== this.baseline; }

  chooseSupplier(): void { const revision = this.revision;
    this.dialogs.select<PurchaseOrderSupplierOption>({ titleKey: 'pur.order.form.chooseSupplier',
      columns: [{ key: 'code', labelKey: 'pur.order.form.code', value: row => row.code },
        { key: 'name', labelKey: 'pur.order.form.name', value: row => row.name }],
      load: query => this.api.supplierOptions(query), rowKey: row => row.id, errorCodes: PURCHASE_ORDER_FORM_ERROR_CODES }).subscribe(rows => {
      if (!rows?.[0] || revision !== this.revision) return;
      this.supplier = rows[0]; this.draft.supplierId = rows[0].id; this.cdr.markForCheck();
    }); }

  chooseItem(line: PurchaseOrderLineDraft): void { const revision = this.revision;
    this.dialogs.select<PurchaseOrderItemOption>({ titleKey: 'pur.order.form.chooseItem',
      columns: [{ key: 'code', labelKey: 'pur.order.form.code', value: row => row.code },
        { key: 'name', labelKey: 'pur.order.form.name', value: row => row.name }],
      load: query => this.api.itemOptions(query), rowKey: row => row.id, errorCodes: PURCHASE_ORDER_FORM_ERROR_CODES }).subscribe(rows => {
      if (!rows?.[0] || revision !== this.revision) return;
      const item = rows[0]; this.items.set(item.id, item); line.itemId = item.id;
      const source = this.sourceOf(line);
      if (line.requisitionLineId && (!source || source.itemId !== item.id)) {
        line.requisitionLineId = null; this.sources.delete(line); this.dropped.add(line);
      }
      this.cdr.markForCheck();
    }); }

  chooseSource(line: PurchaseOrderLineDraft): void {
    const itemId = line.itemId; if (!itemId) return;
    const revision = this.revision;
    this.dialogs.select<PurchaseOrderRequisitionOption>({ titleKey: 'pur.order.form.chooseSource',
      columns: [{ key: 'documentNo', labelKey: 'pur.order.form.requisitionNo', value: row => row.documentNo },
        { key: 'lineNo', labelKey: 'pur.order.form.requisitionLine', value: row => String(row.lineNo) },
        { key: 'requiredDate', labelKey: 'pur.order.form.requiredDate', value: row => row.requiredDate },
        { key: 'quantity', labelKey: 'pur.order.requisitionQuantity', value: row => trimDecimalText(row.quantity) },
        { key: 'orderedQuantity', labelKey: 'pur.order.orderedQuantity', value: row => trimDecimalText(row.orderedQuantity) },
        { key: 'remainingQuantity', labelKey: 'pur.order.remainingQuantity', value: row => trimDecimalText(row.remainingQuantity) }],
      load: query => this.api.requisitionOptions(query, itemId), rowKey: row => row.id,
      searchPlaceholderKey: 'pur.order.form.sourceSearch', errorCodes: PURCHASE_ORDER_FORM_ERROR_CODES }).subscribe(rows => {
      if (!rows?.[0] || revision !== this.revision || line.itemId !== itemId) return;
      this.sources.set(line, rows[0]); line.requisitionLineId = rows[0].id; this.dropped.delete(line); this.cdr.markForCheck();
    }); }
  clearSource(line: PurchaseOrderLineDraft): void { line.requisitionLineId = null; this.sources.delete(line); this.dropped.delete(line); }

  /** A valid business date re-reads the currencies and input tax rates valid on that day; choices no longer valid are cleared. */
  changeDate(value: string): void {
    this.draft.businessDate = value ?? '';
    const revision = ++this.contextRevision;
    this.contextError.set(null);
    if (!validIsoDate(this.draft.businessDate)) { this.context.set(null); this.contextLoading.set(false); return; }
    this.contextLoading.set(true);
    this.api.createContext(this.draft.businessDate).subscribe({
      next: context => { if (revision !== this.contextRevision) return;
        this.contextLoading.set(false); this.apply(context); this.cdr.markForCheck(); },
      error: (e: unknown) => { if (revision !== this.contextRevision) return;
        this.contextLoading.set(false); this.context.set(null);
        this.contextError.set({ key: apiErrorKey(e, PURCHASE_ORDER_FORM_ERROR_CODES), traceId: apiErrorTraceId(e) }); this.cdr.markForCheck(); },
    });
  }
  private apply(context: PurchaseOrderCreateContext): void {
    this.context.set(context);
    const codes = context.currencies.map(c => c.code);
    const lostCurrency = !!this.draft.currencyCode && !codes.includes(this.draft.currencyCode);
    if (lostCurrency) this.draft.currencyCode = '';
    if (!this.draft.currencyCode && codes.length === 1) this.draft.currencyCode = codes[0];
    this.staleCurrency.set(lostCurrency && !this.draft.currencyCode);
    const rates = new Set(context.taxRates.map(r => r.id));
    const stale: number[] = [];
    this.draft.lines.forEach((line, index) => {
      if (line.taxRateId !== null && !rates.has(line.taxRateId)) { line.taxRateId = null; stale.push(index + 1); }
    });
    this.staleTaxLines.set(stale.length ? stale.join(', ') : null);
  }

  addLine(): void { if (this.draft.lines.length < MAX_LINES) this.draft.lines.push(newLine()); }
  removeLine(index: number): void {
    if (this.draft.lines.length <= 1) return;
    const [line] = this.draft.lines.splice(index, 1); this.dropped.delete(line);
  }

  save(): void {
    if (this.saving()) return;
    const errors = purchaseOrderFieldErrors(this.draft); this.fieldErrors.set(errors);
    this.clearErrors();
    if (errors.length) { this.error.set('pur.order.form.invalidForm'); this.focus(errors[0]); return; }
    this.saving.set(true);
    const revision = this.revision;
    this.api.create(this.draft).subscribe({
      next: saved => { if (revision !== this.revision) return;
        this.saving.set(false);
        const result = { number: saved.documentNo, count: saved.lines?.length ?? 0 };
        this.baseline = JSON.stringify(this.draft);
        this.dialogs.success('pur.order.form.saved', result);
        if (this.permissions.has('PUR.ORDER.VIEW')) { void this.router.navigateByUrl(`/pur/purchase-orders?id=${saved.id}`); return; }
        this.reset(); this.saved.set(result);
      },
      error: (e: unknown) => { if (revision !== this.revision) return; this.saving.set(false); this.explain(e); this.cdr.markForCheck(); },
    });
  }

  private focus(path: string): void {
    const parts = path.split('.');
    const id = parts.length === 3 ? `pur-po-form-${FOCUS_ID[parts[2]]}-${parts[1]}` : `pur-po-form-${FOCUS_ID[path]}`;
    setTimeout(() => document.querySelector<HTMLElement>(`[data-testid="${id}"]`)?.focus());
  }
  private clearErrors(): void {
    this.error.set(null); this.errorTrace.set(null); this.errorDetails.set([]); this.serverFieldErrors.set({});
    this.lastServerError = null; this.saved.set(null);
  }

  /** Server refusal: message in the user's language, per-field reasons beside the inputs, and a plain "not saved". */
  private explain(error: unknown): void {
    this.lastServerError = error;
    const code = error instanceof HttpErrorResponse ? (error.error as { errorCode?: unknown } | null)?.errorCode : undefined;
    const key = code === 'PUR.ORDER.INVALID_REQUISITION_SOURCE' ? 'pur.order.form.invalidSource'
      : apiErrorKey(error, PURCHASE_ORDER_FORM_ERROR_CODES);
    const t = (k: string, p?: object) => this.translate.instant(k, p) as string;
    const inline: Record<string, string> = {}; const details: string[] = [];
    const fields = error instanceof HttpErrorResponse && error.status === 400 ? (error.error as { errors?: unknown } | null)?.errors : null;
    if (fields && typeof fields === 'object' && !Array.isArray(fields)) {
      for (const path of Object.keys(fields).slice(0, 20)) {
        const match = /^lines\[(\d+)\]\.(\w+)$/i.exec(path);
        const raw = match?.[2] ?? path; const field = raw.charAt(0).toLowerCase() + raw.slice(1);
        // The header currency is copied onto every line, so a line currency error belongs to the header field.
        const local = match && field !== 'currencyCode' && (LINE_FIELDS as readonly string[]).includes(field) ? `lines.${match[1]}.${field}` : field;
        const label = t(`pur.order.form.${FIELD_LABEL[field] ?? 'field'}`);
        const name = match ? t('pur.order.form.lineField', { number: Number(match[1]) + 1, field: label }) : label;
        const reason = t('pur.order.form.serverFieldInvalid');
        inline[local] = reason; details.push(t('pur.order.form.fieldReason', { name, reason }));
      }
    }
    if (typeof code === 'string' && FIELD_OF_CODE[code]) inline[FIELD_OF_CODE[code]] = t(key);
    details.push(t('pur.order.form.notSaved'));
    this.error.set(key); this.errorTrace.set(apiErrorTraceId(error));
    this.errorDetails.set(details); this.serverFieldErrors.set(inline);
  }
}
