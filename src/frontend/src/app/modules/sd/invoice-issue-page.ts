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
import { InvoiceIssueDraft, InvoiceIssueProblems, draftFromSource, invoiceRefusal, issueRequest, latestDeliveryDate,
  lineKey, matchesIssueReceipt, mergeDraft, sourceLine, validateInvoiceDraft } from './invoice-issue-form';
import { SalesInvoiceDetail, SalesInvoiceIssueRequest, SalesInvoiceService, SalesInvoiceSourceLine, SalesInvoiceSourceOrder,
  SalesInvoiceSourceOrderSummary, invoiceMoney, newInvoiceRequestKey } from './invoice.service';

/** Error codes the issue form explains in the user's language (others fall back to the generic message). */
export const INVOICE_ISSUE_ERROR_CODES = ['SAL.INVOICE.SOURCE_NOT_FOUND', 'SAL.INVOICE.DELIVERY_NOT_POSTED',
  'SAL.INVOICE.SOURCE_MIXED', 'SAL.INVOICE.ORDER_NOT_APPROVED', 'SAL.INVOICE.DATE_BEFORE_DELIVERY',
  'SAL.INVOICE.BALANCE_EXCEEDED', 'SAL.INVOICE.REQUEST_CONFLICT', 'SAL.INVOICE.SOURCE_INVALID',
  'SAL.INVOICE.FI_RECEIPT_MISMATCH', 'SAL.INVOICE.FI_POSTING_FAILED', 'FI.CREDIT.LOCK_TIMEOUT',
  'FI.SALES_POSTING.INVALID_INPUT', 'FI.SALES_POSTING.SOURCE_NOT_FOUND', 'FI.SALES_POSTING.SOURCE_NOT_POSTABLE',
  'FI.SALES_POSTING.SOURCE_MISMATCH', 'FI.SALES_POSTING.SOURCE_CONFLICT', 'FI.SALES_POSTING.INVALID_AMOUNTS',
  'FI.SALES_POSTING.MAPPING_MISSING', 'FI.SALES_POSTING.ACCOUNT_INVALID', 'FI.SALES_POSTING.SOURCE_CONTRACT_UNAVAILABLE',
  'FI.SALES_POSTING.INCONSISTENT_STATE', 'FI.SALES_POSTING.WRITE_FAILED', 'COMMON.PERMISSION.DENIED'] as const;
const SOURCE_ERROR_CODES = ['SAL.INVOICE.SOURCE_NOT_FOUND', 'SAL.INVOICE.ORDER_NOT_APPROVED', 'COMMON.PERMISSION.DENIED'];

const blank = (): InvoiceIssueDraft => ({ orderId: null, businessDate: '', lines: [] });

/**
 * Sales Invoice Issue Interface 2.15 (REQ_F4112.5.1, WU-SD-007b2): pick an Approved order with open Posted delivery
 * lines, choose lines and quantities, and issue one invoice that is posted to AR and GL at once. The request key is
 * kept while the outcome is unknown, so resending cannot issue twice (ADR-179).
 */
@Component({
  selector: 'erp-sales-invoice-issue-page',
  imports: [FormsModule, RouterLink, TranslatePipe, NzAlertModule, NzButtonModule, ApiErrorDisplay],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <section class="issue" data-testid="sd-invoice-issue">
      <header><h1>{{ 'sd.invoiceIssue.title' | translate }}</h1>
        @if (permissions.has('SAL.INVOICE.VIEW')) { <a routerLink="/sal/invoices" data-testid="sd-invoice-issue-back">{{ 'sd.invoiceIssue.back' | translate }}</a> }</header>
      <nz-alert nzType="info" nzShowIcon [nzMessage]="'sd.invoiceIssue.intro' | translate" data-testid="sd-invoice-issue-intro" />
      @if (saved(); as value) { <nz-alert nzType="success" nzShowIcon data-testid="sd-invoice-issue-success"
          [nzMessage]="(replayed() ? 'sd.invoiceIssue.replayed' : 'sd.invoiceIssue.issued') | translate: { number: value.documentNo }" />
        <p data-testid="sd-invoice-issue-receipt" translate="no">{{ value.documentNo }} · {{ value.orderDocumentNo }} · {{ value.currencyCode }} {{ money(value.originalGross) }} · {{ value.baseCurrencyCode }} {{ money(value.baseGross) }}</p>
        <div class="actions"><button nz-button nzType="primary" type="button" (click)="reset()" data-testid="sd-invoice-issue-another">{{ 'sd.invoiceIssue.another' | translate }}</button></div> }
      @if (uncertain()) { <nz-alert nzType="warning" nzShowIcon role="alert" data-testid="sd-invoice-issue-uncertain"
          [nzMessage]="'sd.invoiceIssue.uncertain' | translate" [nzDescription]="'sd.invoiceIssue.uncertainHint' | translate" />
        @if (traceId()) { <p class="hint" data-testid="sd-invoice-issue-uncertain-trace">{{ 'sd.invoiceIssue.traceId' | translate: { id: traceId() } }}</p> }
        <div class="actions"><button nz-button nzType="primary" type="button" [nzLoading]="saving()" (click)="resend()" data-testid="sd-invoice-issue-resend">{{ 'sd.invoiceIssue.resend' | translate }}</button>
          @if (permissions.has('SAL.INVOICE.VIEW')) { <a nz-button routerLink="/sal/invoices" data-testid="sd-invoice-issue-check">{{ 'sd.invoiceIssue.checkList' | translate }}</a> }</div> }
      @if (error()) { <erp-api-error-display data-testid="sd-invoice-issue-error" [messageKey]="error()!" [traceId]="traceId()" />
        @if (canReload()) { <button nz-button type="button" (click)="reloadBalance()" data-testid="sd-invoice-issue-reload">{{ 'sd.invoiceIssue.reload' | translate }}</button> } }
      @if (!saved()) {
        <div class="field"><span id="sd-invoice-issue-order-label">{{ 'sd.invoiceIssue.order' | translate }} <span aria-hidden="true">*</span></span><div class="picker">
          <span data-testid="sd-invoice-issue-order" translate="no">{{ source() ? source()!.documentNo + ' · ' + source()!.customerCode + ' · ' + source()!.customerName : ('sd.invoiceIssue.noOrder' | translate) }}</span>
          <button nz-button type="button" (click)="pickOrder()" [disabled]="locked()" aria-describedby="sd-invoice-issue-order-label" data-testid="sd-invoice-issue-order-pick">{{ 'sd.invoiceIssue.chooseOrder' | translate }}</button></div>
          @if (problem('orderId')) { <small role="alert">{{ 'sd.invoiceIssue.validation.order' | translate }}</small> }</div>
        @if (orderLoading()) { <p role="status">{{ 'sd.invoiceIssue.loadingOrder' | translate }}</p> }
        @if (orderError()) { <erp-api-error-display [messageKey]="orderError()!" [traceId]="orderTraceId()" data-testid="sd-invoice-issue-order-error" />
          @if (draft.orderId) { <button nz-button type="button" (click)="reloadBalance()" data-testid="sd-invoice-issue-order-retry">{{ 'sd.invoiceIssue.retry' | translate }}</button> } }
        @if (source(); as order) {
          <dl class="facts" data-testid="sd-invoice-issue-source">
            <div><dt>{{ 'sd.invoice.customer' | translate }}</dt><dd>{{ order.customerCode }} · {{ order.customerName }}</dd></div>
            <div><dt>{{ 'sd.invoice.currency' | translate }}</dt><dd>{{ order.currencyCode }} → {{ order.baseCurrencyCode }}</dd></div>
            <div><dt>{{ 'sd.invoice.exchangeRate' | translate }}</dt><dd data-testid="sd-invoice-issue-rate">{{ decimal(order.exchangeRate) }}</dd></div>
          </dl>
          @if (!order.lines.length && !orderLoading()) { <nz-alert nzType="warning" nzShowIcon data-testid="sd-invoice-issue-no-lines" [nzMessage]="'sd.invoiceIssue.noOpenLines' | translate" /> }
          @if (order.lines.length) { <form (ngSubmit)="issue()" novalidate data-testid="sd-invoice-issue-form">
            <label class="field date"><span>{{ 'sd.invoiceIssue.date' | translate }} <span aria-hidden="true">*</span></span>
              <input type="date" name="businessDate" autocomplete="off" [(ngModel)]="draft.businessDate" (ngModelChange)="revalidate()" [disabled]="locked()"
                [attr.aria-invalid]="problem('businessDate') ? 'true' : null" data-testid="sd-invoice-issue-date" />
              @if (latestDate()) { <small class="hint">{{ 'sd.invoiceIssue.dateHint' | translate: { date: latestDate() } }}</small> }
              @if (problem('businessDate'); as reason) { <small role="alert" data-testid="sd-invoice-issue-date-error">{{ ('sd.invoiceIssue.validation.' + reason) | translate: { date: latestDate() } }}</small> }</label>
            <p class="hint">{{ 'sd.invoiceIssue.balanceHint' | translate }}</p>
            <div class="scroll"><table class="lines" data-testid="sd-invoice-issue-lines"><caption class="sr-only">{{ 'sd.invoiceIssue.lines' | translate }}</caption>
              <thead><tr><th scope="col"><input type="checkbox" name="all" [checked]="allSelected()" [disabled]="locked()" (change)="selectAll($any($event.target).checked)"
                  [attr.aria-label]="'sd.invoiceIssue.selectAll' | translate" data-testid="sd-invoice-issue-all" /></th>
                <th scope="col">{{ 'sd.invoice.delivery' | translate }}</th><th scope="col">{{ 'sd.invoiceIssue.deliveryDate' | translate }}</th>
                <th scope="col">{{ 'sd.invoice.orderLine' | translate }}</th><th scope="col">{{ 'sd.invoice.item' | translate }}</th>
                <th scope="col" class="number">{{ 'sd.invoice.taxRate' | translate }}</th><th scope="col" class="number">{{ 'sd.invoiceIssue.posted' | translate }}</th>
                <th scope="col" class="number">{{ 'sd.invoiceIssue.invoiced' | translate }}</th><th scope="col" class="number">{{ 'sd.invoiceIssue.remaining' | translate }}</th>
                <th scope="col">{{ 'sd.invoiceIssue.quantity' | translate }}</th></tr></thead>
              <tbody>@for (line of draft.lines; track line.deliveryLineId) { @let facts = factsOf(line.deliveryLineId);
                <tr [class.failed]="lineError()?.deliveryLineId === line.deliveryLineId" [attr.data-testid]="'sd-invoice-issue-line-' + line.deliveryLineId">
                  <td><input type="checkbox" [name]="'selected-' + line.deliveryLineId" [(ngModel)]="line.selected" (ngModelChange)="revalidate()" [disabled]="locked()"
                    [attr.aria-label]="'sd.invoiceIssue.selectLine' | translate: { delivery: facts?.deliveryDocumentNo, line: facts?.deliveryLineNo }"
                    [attr.data-testid]="'sd-invoice-issue-select-' + line.deliveryLineId" /></td>
                  <td class="nowrap" translate="no">{{ facts?.deliveryDocumentNo }} #{{ facts?.deliveryLineNo }}</td><td class="nowrap">{{ facts?.deliveryDate }}</td>
                  <td>{{ facts?.orderLineNo }}</td><td><span translate="no">{{ facts?.itemCode }}</span> · {{ facts?.itemName }}</td>
                  <td class="number">{{ facts ? decimal(facts.taxRatePercent) : '' }}%</td>
                  <td class="number">{{ facts ? decimal(facts.postedQuantity) : '' }}</td><td class="number">{{ facts ? decimal(facts.invoicedQuantity) : '' }}</td>
                  <td class="number" [attr.data-testid]="'sd-invoice-issue-remaining-' + line.deliveryLineId">{{ facts ? decimal(facts.remainingQuantity) : '' }}</td>
                  <td><input inputmode="decimal" autocomplete="off" spellcheck="false" [name]="'quantity-' + line.deliveryLineId" [(ngModel)]="line.quantity" (ngModelChange)="revalidate()"
                      [disabled]="locked() || !line.selected" [attr.aria-invalid]="problem(key(line.deliveryLineId)) ? 'true' : null"
                      [attr.aria-label]="'sd.invoiceIssue.quantityFor' | translate: { delivery: facts?.deliveryDocumentNo, line: facts?.deliveryLineNo }"
                      [attr.data-testid]="'sd-invoice-issue-quantity-' + line.deliveryLineId" />
                    @if (line.selected && problem(key(line.deliveryLineId)); as reason) { <small role="alert" [attr.data-testid]="'sd-invoice-issue-quantity-error-' + line.deliveryLineId">{{ ('sd.invoiceIssue.validation.' + reason) | translate: { remaining: facts ? decimal(facts.remainingQuantity) : '' } }}</small> }
                    @if (lineError()?.deliveryLineId === line.deliveryLineId) { <small role="alert" [attr.data-testid]="'sd-invoice-issue-line-error-' + line.deliveryLineId">{{ lineError()!.key | translate: { remaining: lineError()!.remaining } }}</small> }</td>
                </tr> }</tbody></table></div>
            <p role="status" data-testid="sd-invoice-issue-count">{{ 'sd.invoiceIssue.selectedCount' | translate: { count: selectedCount(), total: draft.lines.length } }}</p>
            @if (problem('lines'); as reason) { <p role="alert" data-testid="sd-invoice-issue-lines-error">{{ ('sd.invoiceIssue.validation.' + reason) | translate }}</p> }
            @if (submitted() && count()) { <p role="alert" data-testid="sd-invoice-issue-errors">{{ 'sd.invoiceIssue.errors' | translate: { count: count() } }}</p> }
            <div class="actions"><button nz-button nzType="primary" type="submit" [nzLoading]="saving()" [disabled]="locked()" data-testid="sd-invoice-issue-submit">{{ 'sd.invoiceIssue.submit' | translate }}</button></div>
          </form> }
        }
      }
    </section>`,
  styles: `.issue{display:grid;gap:16px;min-width:0;color:var(--erp-color-text)}header{display:flex;justify-content:space-between;gap:12px;align-items:center;flex-wrap:wrap}h1{margin:0}form{display:grid;gap:12px;min-width:0}.field{display:grid;gap:4px;min-width:0}.field.date{max-width:280px}.picker,.actions{display:flex;gap:8px;align-items:center;flex-wrap:wrap}.picker span,.facts dd{overflow-wrap:anywhere}input:not([type=checkbox]){width:100%;min-height:32px;padding:4px 8px;background:var(--erp-color-bg-container);color:var(--erp-color-text);border:1px solid var(--erp-color-border);border-radius:4px}input[type=checkbox]{width:16px;height:16px}input:disabled{opacity:.55;cursor:not-allowed}.facts{display:grid;grid-template-columns:repeat(auto-fill,minmax(180px,1fr));gap:8px 16px;margin:0}.facts dt{color:var(--erp-color-text-secondary)}.facts dd{margin:0}.scroll{overflow-x:auto;min-width:0}table.lines{width:100%;min-width:980px;border-collapse:collapse}.lines th,.lines td{padding:8px;border-bottom:1px solid var(--erp-color-border);text-align:left;vertical-align:top}.lines th,.lines td.nowrap,.lines td.number{white-space:nowrap}.lines td:last-child{min-width:160px}tr.failed td{background:var(--erp-color-error-bg,transparent)}.number{text-align:right!important;font-variant-numeric:tabular-nums}small,p[role=alert]{color:var(--erp-color-error-text)}small{display:block;margin-top:4px}small.hint,.hint{color:var(--erp-color-text-secondary);margin:0}.sr-only{position:absolute;width:1px;height:1px;overflow:hidden;clip:rect(0 0 0 0);white-space:nowrap}p{margin:0}button:focus-visible,input:focus-visible,a:focus-visible{outline:2px solid var(--erp-color-primary);outline-offset:2px}`,
})
export class SalesInvoiceIssuePage {
  private readonly invoices = inject(SalesInvoiceService);
  private readonly dialogs = inject(ErpDialogService);
  private readonly company = inject(CompanyContextService);
  private readonly router = inject(Router);
  readonly permissions = inject(PermissionService);
  private readonly translate = inject(TranslateService);
  private readonly destroy = inject(DestroyRef);
  private readonly host: ElementRef<HTMLElement> = inject(ElementRef);
  private readonly injector = inject(Injector);
  draft = blank();
  readonly source = signal<SalesInvoiceSourceOrder | null>(null);
  readonly problems = signal<InvoiceIssueProblems>({}); readonly submitted = signal(false);
  readonly orderLoading = signal(false); readonly orderError = signal<string | null>(null); readonly orderTraceId = signal<string | null>(null);
  readonly saving = signal(false); readonly saved = signal<SalesInvoiceDetail | null>(null); readonly replayed = signal(false);
  readonly uncertain = signal(false); readonly error = signal<string | null>(null); readonly traceId = signal<string | null>(null);
  readonly lineError = signal<{ deliveryLineId: number; key: string; remaining: string } | null>(null);
  readonly canReload = signal(false);
  /** Bumped by every redraw-relevant change of `draft` (a plain object bound with ngModel). */
  private readonly tick = signal(0);
  private epoch = 0;
  private requestKey = newInvoiceRequestKey();
  /** The exact body of the last POST; resent unchanged while the outcome is unknown. */
  private sent: SalesInvoiceIssueRequest | null = null;
  constructor() {
    this.destroy.onDestroy(() => this.epoch++);
    effect(() => { this.company.current(); untracked(() => this.reset()); });
  }
  canLeave(): boolean { return !this.dirty() || window.confirm(this.translate.instant('sd.invoiceIssue.unsaved')); }
  @HostListener('window:beforeunload', ['$event']) beforeUnload(event: BeforeUnloadEvent): void { if (this.dirty()) event.preventDefault(); }
  private dirty(): boolean { return !this.saved() && (this.draft.orderId != null || this.uncertain()); }
  locked(): boolean { return this.saving() || this.uncertain() || !!this.saved() || this.orderLoading(); }
  key(deliveryLineId: number): string { return lineKey(deliveryLineId); }
  problem(name: string) { return this.problems()[name]; }
  count(): number { return Object.keys(this.problems()).length; }
  decimal(value: string): string { return formatDecimalText(String(value), this.translate.getCurrentLang() || undefined); }
  money(value: string): string { return invoiceMoney(value); }
  factsOf(deliveryLineId: number): SalesInvoiceSourceLine | undefined { return sourceLine(this.source(), deliveryLineId); }
  latestDate(): string | null { this.tick(); return latestDeliveryDate(this.draft, this.source()); }
  selectedCount(): number { this.tick(); return this.draft.lines.filter(line => line.selected).length; }
  allSelected(): boolean { this.tick(); return this.draft.lines.length > 0 && this.draft.lines.every(line => line.selected); }
  selectAll(selected: boolean): void {
    if (this.locked()) return;
    this.draft.lines = this.draft.lines.map(line => ({ ...line, selected }));
    this.revalidate();
  }
  /** Inline messages follow edits once the user has tried to submit; a changed line drops its server refusal. */
  revalidate(): void {
    this.tick.update(value => value + 1);
    if (this.submitted()) this.problems.set(validateInvoiceDraft(this.draft, this.source()));
  }
  reset(): void {
    this.epoch++; this.draft = blank(); this.source.set(null); this.problems.set({}); this.submitted.set(false);
    this.orderLoading.set(false); this.orderError.set(null); this.orderTraceId.set(null); this.saving.set(false);
    this.saved.set(null); this.replayed.set(false); this.uncertain.set(false); this.clearOutcome();
    this.requestKey = newInvoiceRequestKey(); this.sent = null; this.tick.update(value => value + 1);
  }
  private clearOutcome(): void { this.error.set(null); this.traceId.set(null); this.lineError.set(null); this.canReload.set(false); }
  pickOrder(): void {
    if (this.locked()) return;
    const epoch = this.epoch;
    this.dialogs.select<SalesInvoiceSourceOrderSummary>({ titleKey: 'sd.invoiceIssue.chooseOrder',
      descriptionKey: 'sd.invoiceIssue.chooseOrderHint',
      columns: [{ key: 'documentNo', labelKey: 'sd.invoice.order', value: row => row.documentNo },
        { key: 'customerName', labelKey: 'sd.invoice.customer', value: row => `${row.customerCode} · ${row.customerName}` },
        { key: 'businessDate', labelKey: 'sd.invoiceIssue.orderDate', value: row => row.businessDate },
        { key: 'currencyCode', labelKey: 'sd.invoice.currency', value: row => row.currencyCode },
        { key: 'openLineCount', labelKey: 'sd.invoiceIssue.openLines', value: row => String(row.openLineCount) }],
      load: query => this.invoices.sourceOrders(query), rowKey: row => row.id,
      errorCodes: ['COMMON.PERMISSION.DENIED'], timeoutKey: 'sd.invoice.timeout' }).subscribe(rows => {
      if (epoch !== this.epoch || !rows?.[0]) return;
      const businessDate = this.draft.businessDate;
      this.reset(); this.draft.businessDate = businessDate; this.draft.orderId = rows[0].id;
      this.loadOrder(rows[0].id, false);
    });
  }
  /** Reread the advisory balance; keeps the selection and quantities of lines that are still open. */
  reloadBalance(): void { if (this.draft.orderId && !this.orderLoading() && !this.saving() && !this.uncertain()) this.loadOrder(this.draft.orderId, true); }
  private loadOrder(orderId: number, keep: boolean): void {
    const epoch = ++this.epoch;
    this.orderLoading.set(true); this.orderError.set(null); this.orderTraceId.set(null);
    this.invoices.sourceOrder(orderId).subscribe({
      next: order => { if (epoch !== this.epoch) return; this.orderLoading.set(false);
        if (order?.id !== orderId || !Array.isArray(order.lines)) {
          this.source.set(null); this.draft.lines = []; this.orderError.set(UNKNOWN_ERROR_KEY); return; }
        this.source.set(order);
        this.draft = keep && this.draft.lines.length ? mergeDraft(this.draft, order) : draftFromSource(order, this.draft.businessDate);
        if (keep) this.clearOutcome();
        this.revalidate();
      },
      error: failure => { if (epoch !== this.epoch) return; this.orderLoading.set(false);
        this.source.set(null); this.draft.lines = [];
        this.orderError.set(failure instanceof TimeoutError ? 'sd.invoice.timeout' : apiErrorKey(failure, SOURCE_ERROR_CODES));
        this.orderTraceId.set(apiErrorTraceId(failure)); },
    });
  }
  issue(): void {
    if (this.locked()) return;
    this.submitted.set(true);
    const problems = validateInvoiceDraft(this.draft, this.source());
    this.problems.set(problems);
    if (Object.keys(problems).length) { this.focusFirstProblem(Object.keys(problems)[0]); return; }
    this.send(issueRequest(this.draft, this.requestKey));
  }
  /** Same key, same body: a saved invoice comes back as 200 without a second posting. */
  resend(): void { if (this.sent && this.uncertain() && !this.saving()) this.send(this.sent); }
  private send(request: SalesInvoiceIssueRequest): void {
    const epoch = this.epoch;
    this.sent = request; this.saving.set(true); this.clearOutcome();
    this.invoices.issue(request).subscribe({
      next: result => { if (epoch !== this.epoch) return; this.saving.set(false);
        if (!matchesIssueReceipt(result.invoice, request)) { this.uncertain.set(true); return; }
        this.uncertain.set(false); this.replayed.set(result.replayed); this.saved.set(result.invoice);
        this.dialogs.success(result.replayed ? 'sd.invoiceIssue.replayed' : 'sd.invoiceIssue.issued', { number: result.invoice.documentNo });
        if (this.permissions.has('SAL.INVOICE.VIEW'))
          void this.router.navigate(['/sal/invoices'], { queryParams: { open: result.invoice.id } });
      },
      error: failure => { if (epoch !== this.epoch) return; this.saving.set(false); this.refused(failure); },
    });
  }
  private refused(failure: unknown): void {
    const status = failure instanceof HttpErrorResponse ? failure.status : null;
    const key = apiErrorKey(failure, INVOICE_ISSUE_ERROR_CODES);
    this.traceId.set(apiErrorTraceId(failure));
    // A 5xx without a code the server raises before commit, a network failure or a timeout leaves the outcome unknown.
    if (failure instanceof TimeoutError || status === 0 || (status != null && status >= 500 && key === UNKNOWN_ERROR_KEY)) {
      this.uncertain.set(true); return; }
    // Definite refusal: nothing was saved, so the next attempt (possibly with other content) uses a new key.
    this.uncertain.set(false); this.requestKey = newInvoiceRequestKey(); this.sent = null;
    const { lineNo, remaining } = invoiceRefusal(failure);
    const line = lineNo != null ? this.draft.lines.filter(candidate => candidate.selected)[lineNo - 1] : undefined;
    if (line && key !== 'errors.SAL.INVOICE.DATE_BEFORE_DELIVERY') {
      this.lineError.set({ deliveryLineId: line.deliveryLineId, remaining: remaining != null ? this.decimal(remaining) : '',
        key: key === 'errors.SAL.INVOICE.BALANCE_EXCEEDED' && remaining != null ? 'sd.invoiceIssue.lineBalance' : key });
      this.focus(`sd-invoice-issue-quantity-${line.deliveryLineId}`);
    } else if (key === 'errors.SAL.INVOICE.DATE_BEFORE_DELIVERY') this.focus('sd-invoice-issue-date');
    this.canReload.set(['errors.SAL.INVOICE.BALANCE_EXCEEDED', 'errors.SAL.INVOICE.DATE_BEFORE_DELIVERY',
      'errors.SAL.INVOICE.DELIVERY_NOT_POSTED', 'errors.SAL.INVOICE.SOURCE_NOT_FOUND'].includes(key));
    this.error.set(status === 422 && key === UNKNOWN_ERROR_KEY ? 'sd.invoiceIssue.rejected' : key);
  }
  private focusFirstProblem(name: string): void {
    const target = name === 'orderId' ? 'sd-invoice-issue-order-pick' : name === 'businessDate' ? 'sd-invoice-issue-date'
      : name === 'lines' ? 'sd-invoice-issue-all' : `sd-invoice-issue-quantity-${name.slice('line-'.length)}`;
    this.focus(target);
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
