import { ChangeDetectionStrategy, ChangeDetectorRef, Component, effect, ElementRef, inject, signal, untracked } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { TranslatePipe } from '@ngx-translate/core';
import { NzAlertModule } from 'ng-zorro-antd/alert';
import { NzButtonModule } from 'ng-zorro-antd/button';
import { NzInputModule } from 'ng-zorro-antd/input';
import { CompanyContextService } from '../../core/company/company-context.service';
import { apiErrorTraceId, apiValidationDetails } from '../../core/http/api-error';
import { ApiErrorDisplay } from '../../shared/api-error-display';
import { isoDate } from '../../shared/date';
import { formatDecimalText } from '../../shared/decimal-text';
import { ErpDialogService } from '../../shared/dialogs/dialog.service';
import { HasUnsavedChanges } from '../../shared/document-page/unsaved-changes.guard';
import { CollectionReceiptService, CollectionReceiptSummary } from './collection-receipt.service';
import {
  amountUnits, CollectionSettlementDetail, CollectionSettlementService, hasSettlementIssues, OpenReceivable,
  SETTLEMENT_REMARK_MAX, SettlementDraft, SettlementDraftIssues, SettlementDraftLine, settlementDraftIssues,
  settlementFieldErrors, SettlementHeaderField, settlementLineErrors, settlementLineReasonKey, settlementTotalText, unitsText,
} from './collection-settlement.service';

const LIST = '/ar/collection-settlements';
const empty = (): SettlementDraft => ({ receiptId: null, customerId: null, businessDate: '', remark: '', lines: [] });
const noIssues = (): SettlementDraftIssues => ({ header: {}, lines: {} });

/** Collection-level refusals shown under the collection field as well as in the error banner. */
const RECEIPT_CODES = ['FI.COLLECTION_WRITEOFF.RECEIPT_NOT_POSTED', 'FI.COLLECTION_WRITEOFF.RECEIPT_MISMATCH',
  'FI.COLLECTION_WRITEOFF.RECEIPT_INSUFFICIENT'];
const STALE_CODES = ['COMMON.CONCURRENCY.CONFLICT', 'FI.COLLECTION.SETTLEMENT_INVALID_STATUS', 'FI.COLLECTION.SETTLEMENT_NOT_FOUND'];

/**
 * Collection settlement create / edit (interface 6.30, WU-FI-005c2b2, REQ_F4116.5.11). The user chooses a posted collection
 * (its customer decides which receivables may be chosen), then adds that customer's open receivables posted on or before the
 * settlement date and types an exact amount per line. The total is the sum of the lines and may not exceed the collection's
 * unapplied cash. Saving keeps a Draft without reserving anything (BR-FI-157); only Draft / Rejected settlements are editable.
 * 400 `lines[i].*` and 422 `lineErrors` are shown on their line (BR-FI-039/040).
 */
@Component({
  selector: 'erp-collection-settlement-form',
  imports: [FormsModule, RouterLink, TranslatePipe, NzAlertModule, NzButtonModule, NzInputModule, ApiErrorDisplay],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <section class="settle-form">
      <header><h1>{{ (id ? 'fi.collectionSettlement.editTitle' : 'fi.collectionSettlement.createTitle') | translate }}
          @if (documentNo()) { <span class="doc-no" data-testid="fi-settle-form-no" translate="no">{{ documentNo() }}</span> }</h1>
        <a [routerLink]="list" data-testid="fi-settle-back">{{ 'fi.collectionSettlement.back' | translate }}</a></header>
      <p class="hint">{{ 'fi.collectionSettlement.formIntro' | translate }}</p>
      @if (loading()) { <p class="hint" role="status" data-testid="fi-settle-form-loading">{{ 'fi.collectionSettlement.loading' | translate }}</p> }
      @if (lockedKey()) { <nz-alert nzType="warning" nzShowIcon data-testid="fi-settle-form-locked" [nzMessage]="lockedKey()! | translate" /> }
      @if (rejected()) { <nz-alert nzType="info" nzShowIcon data-testid="fi-settle-form-rejected" [nzMessage]="'fi.collectionSettlement.rejectedHint' | translate" /> }
      @if (error()) {
        <erp-api-error-display data-testid="fi-settle-form-error" [messageKey]="error()!" [traceId]="traceId()" [details]="details()" />
        @if (stale()) { <button nz-button type="button" class="reload" data-testid="fi-settle-form-reload" (click)="reload()">{{ 'fi.collectionSettlement.reload' | translate }}</button> }
      }
      @if (!loading() && !loadFailed()) {
        <form class="fields" (ngSubmit)="save()" novalidate data-testid="fi-settle-form">
          <div class="field">
            <span class="label" id="fi-settle-receipt-label">{{ 'fi.collectionSettlement.receipt' | translate }} <span class="required" aria-hidden="true">*</span></span>
            <div class="picker" role="group" aria-labelledby="fi-settle-receipt-label" [attr.aria-invalid]="!!headerIssue('receiptId')">
              <span data-testid="fi-settle-form-receipt" translate="no">{{ receiptLabel() || ('fi.collectionSettlement.noReceipt' | translate) }}</span>
              <button nz-button type="button" data-testid="fi-settle-receipt-pick" [disabled]="!editable()" (click)="pickReceipt()">{{ 'fi.collectionSettlement.chooseReceipt' | translate }}</button>
            </div>
            @if (customerLabel()) { <span class="muted" data-testid="fi-settle-form-customer">{{ 'fi.collectionSettlement.customerOf' | translate: { customer: customerLabel() } }}</span> }
            @if (headerIssue('receiptId'); as key) { <p class="field-error" role="alert" data-testid="fi-settle-issue-receiptId">{{ key | translate }}</p> }
          </div>
          <div class="field">
            <label for="fi-settle-date">{{ 'fi.collectionSettlement.businessDate' | translate }} <span class="required" aria-hidden="true">*</span></label>
            <input nz-input id="fi-settle-date" name="businessDate" type="date" required [ngModel]="draft.businessDate" (ngModelChange)="dateChanged($event)" [disabled]="!editable()"
              data-testid="fi-settle-date" [nzStatus]="headerIssue('businessDate') ? 'error' : ''" [attr.aria-invalid]="!!headerIssue('businessDate')"
              aria-describedby="fi-settle-date-help" />
            <span class="muted" id="fi-settle-date-help">{{ 'fi.collectionSettlement.dateHelp' | translate }}</span>
            @if (headerIssue('businessDate'); as key) { <p class="field-error" role="alert" data-testid="fi-settle-issue-businessDate">{{ key | translate: { date: receiptDate() } }}</p> }
          </div>
          <div class="lines-head">
            <h2 id="fi-settle-lines-title">{{ 'fi.collectionSettlement.linesCaption' | translate }}</h2>
            <button nz-button type="button" data-testid="fi-settle-add-lines" [disabled]="!editable() || !draft.customerId" (click)="pickReceivables()"
              [attr.title]="!draft.customerId ? ('fi.collectionSettlement.addLinesNeedsReceipt' | translate) : null">{{ 'fi.collectionSettlement.addLines' | translate }}</button>
          </div>
          @if (!draft.customerId && editable()) { <p class="muted" data-testid="fi-settle-add-hint">{{ 'fi.collectionSettlement.addLinesNeedsReceipt' | translate }}</p> }
          <div class="lines-wrap">
            <table class="lines" data-testid="fi-settle-form-lines" aria-labelledby="fi-settle-lines-title">
              <thead><tr>
                <th scope="col">{{ 'fi.collectionSettlement.receivable' | translate }}</th>
                <th scope="col">{{ 'fi.collectionSettlement.postingDate' | translate }}</th>
                <th scope="col" class="num">{{ 'fi.collectionSettlement.openNow' | translate }}</th>
                <th scope="col" class="num">{{ 'fi.collectionSettlement.lineAmount' | translate }} <span class="required" aria-hidden="true">*</span></th>
                <th scope="col"><span class="sr-only">{{ 'fi.collectionSettlement.actions' | translate }}</span></th>
              </tr></thead>
              <tbody>
                @for (line of draft.lines; track line.receivableId; let i = $index) {
                  <tr [attr.data-testid]="'fi-settle-form-line-' + line.receivableId" [class.bad]="!!lineIssue(i)">
                    <td><span class="doc" translate="no">#{{ line.receivableId }}</span>
                      @if (line.sourceType) { <span class="muted source"> {{ sourceLabel(line) | translate: { id: line.sourceId } }}</span> }</td>
                    <td class="date">{{ line.postingDate ?? '—' }}</td>
                    <td class="num" [attr.data-testid]="'fi-settle-form-open-' + line.receivableId">{{ line.openAmount !== null ? money(line.openAmount) : '—' }}</td>
                    <td class="amount-cell">
                      <input nz-input type="text" inputmode="decimal" autocomplete="off" [name]="'amount-' + line.receivableId" [ngModel]="line.amount" (ngModelChange)="amountChanged(i, $event)"
                        [disabled]="!editable()" [attr.data-testid]="'fi-settle-form-amount-' + line.receivableId" [nzStatus]="lineIssue(i) ? 'error' : ''"
                        [attr.aria-invalid]="!!lineIssue(i)" [attr.aria-label]="'fi.collectionSettlement.lineAmountFor' | translate: { receivable: line.receivableId }" />
                      @if (lineIssue(i); as key) { <p class="field-error" role="alert" [attr.data-testid]="'fi-settle-line-issue-' + line.receivableId">{{ key | translate }}</p> }
                    </td>
                    <td><button nz-button nzType="link" nzDanger type="button" [disabled]="!editable()" [attr.data-testid]="'fi-settle-form-remove-' + line.receivableId"
                      [attr.aria-label]="'fi.collectionSettlement.removeLineFor' | translate: { receivable: line.receivableId }" (click)="removeLine(i)">{{ 'fi.collectionSettlement.removeLine' | translate }}</button></td>
                  </tr>
                } @empty {
                  <tr><td colspan="5" class="muted empty" data-testid="fi-settle-form-no-lines">{{ 'fi.collectionSettlement.noLines' | translate }}</td></tr>
                }
              </tbody>
            </table>
          </div>
          @if (headerIssue('lines'); as key) { <p class="field-error" role="alert" data-testid="fi-settle-issue-lines">{{ key | translate: totals() }}</p> }
          <dl class="totals" data-testid="fi-settle-form-totals">
            <dt>{{ 'fi.collectionSettlement.formTotal' | translate }}</dt><dd class="num" data-testid="fi-settle-form-total">{{ money(total()) }}</dd>
            <dt>{{ 'fi.collectionSettlement.receiptUnapplied' | translate }}</dt><dd class="num" data-testid="fi-settle-form-unapplied">{{ receiptUnapplied() !== null ? money(receiptUnapplied()!) : '—' }}</dd>
            <dt>{{ 'fi.collectionSettlement.remainingAfter' | translate }}</dt><dd class="num" data-testid="fi-settle-form-remaining" [class.over]="overUnapplied()">{{ remaining() !== null ? money(remaining()!) : '—' }}</dd>
          </dl>
          <p class="muted">{{ 'fi.collectionSettlement.openNowHint' | translate }}</p>
          <div class="field">
            <label for="fi-settle-remark">{{ 'fi.collectionSettlement.remark' | translate }}</label>
            <textarea nz-input id="fi-settle-remark" name="remark" rows="2" [maxlength]="remarkMax" [(ngModel)]="draft.remark" [disabled]="!editable()"
              data-testid="fi-settle-remark" [attr.aria-invalid]="!!headerIssue('remark')"></textarea>
            @if (headerIssue('remark'); as key) { <p class="field-error" role="alert" data-testid="fi-settle-issue-remark">{{ key | translate: { max: remarkMax } }}</p> }
          </div>
          <div class="form-actions">
            <button nz-button nzType="primary" type="submit" data-testid="fi-settle-save" [nzLoading]="saving()" [disabled]="!editable()"
              [attr.title]="lockedKey() ? (lockedKey()! | translate) : null">{{ 'fi.collectionSettlement.save' | translate }}</button>
            <a nz-button [routerLink]="list">{{ 'fi.collectionSettlement.cancel' | translate }}</a>
          </div>
          <p class="muted">{{ 'fi.collectionSettlement.saveHint' | translate }}</p>
        </form>
      }
    </section>`,
  styles: `.settle-form{display:grid;gap:16px;color:var(--erp-color-text);min-width:0;max-width:960px}header{display:flex;gap:8px;align-items:center;justify-content:space-between;flex-wrap:wrap}h1{margin:0;font-size:20px;display:flex;gap:8px;align-items:baseline;flex-wrap:wrap}h2{margin:0;font-size:16px}.doc-no{font-size:14px;color:var(--erp-color-text-secondary);white-space:nowrap}.hint,.muted{margin:0;color:var(--erp-color-text-secondary)}.muted{font-size:12px}.fields{display:grid;gap:12px;padding:16px;border:1px solid var(--erp-color-border);border-radius:8px;background:var(--erp-color-bg-container);min-width:0}.field{display:grid;gap:4px;max-width:520px}.picker{display:flex;gap:8px;align-items:center;flex-wrap:wrap;min-width:0}.picker span{overflow-wrap:anywhere}.required,.field-error{color:var(--erp-color-error-text)}.field-error{margin:0}.lines-head{display:flex;gap:8px;align-items:center;justify-content:space-between;flex-wrap:wrap}.lines-wrap{overflow-x:auto;min-width:0}.lines{width:100%;border-collapse:collapse}.lines th,.lines td{padding:6px 8px;border-bottom:1px solid var(--erp-color-border);vertical-align:top}.lines th{font-weight:600;text-align:left}.lines th.num,.num{text-align:right;font-variant-numeric:tabular-nums;white-space:nowrap}.amount-cell{min-width:180px}.amount-cell input{text-align:right;font-variant-numeric:tabular-nums}.doc,.date{white-space:nowrap}.empty{text-align:center;padding:16px}.totals{display:grid;grid-template-columns:auto minmax(140px,max-content);gap:4px 16px;margin:0;justify-content:end}.totals dt{font-weight:600}.totals dd{margin:0}.over{color:var(--erp-color-error-text)}.sr-only{position:absolute;width:1px;height:1px;overflow:hidden;clip:rect(0 0 0 0);white-space:nowrap}.form-actions{display:flex;gap:8px;flex-wrap:wrap}.reload{justify-self:start}button:focus-visible,a:focus-visible,input:focus-visible,textarea:focus-visible{outline:2px solid var(--erp-color-primary);outline-offset:2px}`,
})
export class CollectionSettlementForm implements HasUnsavedChanges {
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly api = inject(CollectionSettlementService);
  private readonly receipts = inject(CollectionReceiptService);
  private readonly dialogs = inject(ErpDialogService);
  private readonly company = inject(CompanyContextService);
  private readonly cdr = inject(ChangeDetectorRef);
  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef);
  readonly list = LIST;
  readonly remarkMax = SETTLEMENT_REMARK_MAX;
  readonly id = Number(this.route.snapshot.paramMap.get('id')) || null;
  readonly loading = signal(false); readonly loadFailed = signal(false); readonly saving = signal(false);
  readonly error = signal<string | null>(null); readonly traceId = signal<string | null>(null);
  readonly details = signal<readonly string[]>([]); readonly stale = signal(false);
  readonly issues = signal<SettlementDraftIssues>(noIssues());
  readonly receiptLabel = signal(''); readonly customerLabel = signal('');
  /** The chosen collection's unapplied cash when read (exact text); null when unknown or not posted. */
  readonly receiptUnapplied = signal<string | null>(null);
  /** The chosen collection's date (only known after choosing it here). */
  readonly receiptDate = signal<string | null>(null);
  readonly documentNo = signal(''); readonly lockedKey = signal<string | null>(null); readonly rejected = signal(false);
  draft: SettlementDraft = empty();
  private version = 0;
  private saved = JSON.stringify(empty());
  private epoch = 0;
  private skipFirstCompany = true;

  constructor() {
    effect(() => {
      this.company.current();
      untracked(() => {
        // Another company's collection and receivable IDs mean nothing here: a switch while editing returns to the list.
        if (this.skipFirstCompany) { this.skipFirstCompany = false; this.start(); return; }
        this.epoch++; this.saved = JSON.stringify(this.draft); void this.router.navigateByUrl(LIST);
      });
    });
  }

  editable(): boolean { return !this.loading() && !this.lockedKey() && !this.saving(); }
  headerIssue(field: SettlementHeaderField): string | null { return this.issueKey(this.issues().header[field]); }
  lineIssue(index: number): string | null { return this.issueKey(this.issues().lines[index]); }
  hasUnsavedChanges(): boolean { return !this.lockedKey() && JSON.stringify(this.draft) !== this.saved; }
  money(value: string): string { return formatDecimalText(value, 'en-US'); }
  sourceLabel(line: SettlementDraftLine): string {
    return line.sourceType === 'Invoice' ? 'fi.collectionSettlement.sourceInvoice' : 'fi.collectionSettlement.sourceOther';
  }
  total(): string { return settlementTotalText(this.draft); }
  remaining(): string | null {
    const unapplied = amountUnits(this.receiptUnapplied());
    return unapplied === null ? null : unitsText(unapplied - amountUnits(this.total())!);
  }
  overUnapplied(): boolean { return (this.remaining() ?? '').startsWith('-'); }
  totals(): Record<string, string> { return { total: this.money(this.total()), unapplied: this.money(this.receiptUnapplied() ?? '0') }; }

  reload(): void { this.start(); }

  private issueKey(key: string | undefined): string | null {
    if (!key) return null;
    return key.includes('.') ? key : `fi.collectionSettlement.issue.${key}`;
  }

  private start(): void {
    const epoch = ++this.epoch;
    this.error.set(null); this.traceId.set(null); this.details.set([]); this.stale.set(false); this.issues.set(noIssues());
    if (!this.id) { this.draft.businessDate ||= isoDate(new Date()); this.saved = JSON.stringify(this.draft); return; }
    this.loading.set(true); this.loadFailed.set(false);
    this.api.get(this.id).subscribe({
      next: detail => { if (epoch === this.epoch) this.apply(detail); },
      error: e => { if (epoch !== this.epoch) return; this.loading.set(false); this.loadFailed.set(true); this.fail(e); },
    });
  }

  private apply(detail: CollectionSettlementDetail): void {
    this.version = detail.version;
    this.documentNo.set(detail.documentNo);
    this.draft = { receiptId: detail.receiptId, customerId: detail.customerId, businessDate: detail.businessDate,
      remark: detail.remark ?? '', lines: detail.lines.map(line => ({ receivableId: line.receivableId, amount: line.amount,
        openAmount: line.receivableOpenAmount, sourceType: null, sourceId: null, postingDate: null })) };
    this.saved = JSON.stringify(this.draft);
    this.receiptLabel.set(detail.receiptDocumentNo ?? `#${detail.receiptId}`);
    this.customerLabel.set(detail.customerCode ? `${detail.customerCode} · ${detail.customerName ?? ''}` : `#${detail.customerId}`);
    this.receiptUnapplied.set(detail.receiptUnappliedAmount);
    this.receiptDate.set(null);
    const editable = detail.status === 'Draft' || detail.status === 'Rejected';
    this.lockedKey.set(editable ? null : `fi.collectionSettlement.lockedHint.${detail.status.toLowerCase()}`);
    this.rejected.set(detail.status === 'Rejected');
    this.loading.set(false);
    this.cdr.markForCheck();
  }

  pickReceipt(): void {
    const epoch = this.epoch;
    this.dialogs.select<CollectionReceiptSummary>({ titleKey: 'fi.collectionSettlement.chooseReceipt',
      columns: [{ key: 'documentNo', labelKey: 'fi.collectionSettlement.documentNo', value: row => row.documentNo },
        { key: 'customer', labelKey: 'fi.collectionSettlement.customer', value: row => this.party(row) },
        { key: 'businessDate', labelKey: 'fi.collectionSettlement.receiptDate', value: row => row.businessDate },
        { key: 'unapplied', labelKey: 'fi.collectionSettlement.receiptUnapplied', value: row => `${this.money(row.unappliedAmount ?? '0')} ${row.currency}` }],
      load: query => this.receipts.list({ ...query, status: 'Posted' }), rowKey: row => row.id }).subscribe(rows => {
      const receipt = rows?.[0];
      if (epoch !== this.epoch || !receipt) return;
      // Receivables belong to one customer: lines chosen for another customer's collection cannot stay.
      if (this.draft.customerId !== null && this.draft.customerId !== receipt.customerId) this.draft.lines = [];
      this.draft.receiptId = receipt.id; this.draft.customerId = receipt.customerId;
      this.receiptLabel.set(receipt.documentNo); this.customerLabel.set(this.party(receipt));
      this.receiptUnapplied.set(receipt.unappliedAmount); this.receiptDate.set(receipt.businessDate);
      if (this.draft.businessDate && this.draft.businessDate < receipt.businessDate) this.draft.businessDate = receipt.businessDate;
      this.issues.update(current => ({ header: { ...current.header, receiptId: undefined, businessDate: undefined }, lines: current.lines }));
      this.cdr.markForCheck();
    });
  }

  pickReceivables(): void {
    const customerId = this.draft.customerId;
    if (!customerId || !this.editable()) return;
    const epoch = this.epoch;
    const postedOnOrBefore = /^\d{4}-\d{2}-\d{2}$/.test(this.draft.businessDate) ? this.draft.businessDate : '';
    this.dialogs.select<OpenReceivable>({ titleKey: 'fi.collectionSettlement.chooseReceivables', multiple: true,
      columns: [{ key: 'receivableId', labelKey: 'fi.collectionSettlement.receivable', value: row => `#${row.receivableId}` },
        { key: 'source', labelKey: 'fi.collectionSettlement.source', value: row => `${row.sourceType} #${row.sourceId}` },
        { key: 'postingDate', labelKey: 'fi.collectionSettlement.postingDate', value: row => row.postingDate },
        { key: 'openAmount', labelKey: 'fi.collectionSettlement.openNow', value: row => `${this.money(row.openAmount)} ${row.currency}` }],
      load: query => this.api.openReceivables(customerId, postedOnOrBefore, query.page, query.pageSize),
      rowKey: row => row.receivableId }).subscribe(rows => {
      if (epoch !== this.epoch || !rows?.length) return;
      // A new line starts with what can still be applied: its open amount, capped by the collection's unapplied cash left.
      let left = amountUnits(this.remaining());
      for (const row of rows) {
        if (this.draft.lines.some(line => line.receivableId === row.receivableId)) continue;
        const open = amountUnits(row.openAmount) ?? 0n;
        const amount = left === null ? open : (left > 0n ? (open < left ? open : left) : 0n);
        if (left !== null) left -= amount;
        this.draft.lines.push({ receivableId: row.receivableId, amount: amount > 0n ? unitsText(amount) : '', openAmount: row.openAmount,
          sourceType: row.sourceType, sourceId: row.sourceId, postingDate: row.postingDate });
      }
      this.issues.update(current => ({ header: { ...current.header, lines: undefined }, lines: {} }));
      this.cdr.markForCheck();
    });
  }

  /**
   * A changed amount drops that line's reason (screen or server) and re-checks the total against the unapplied cash, so a
   * message never describes a value the user has already corrected (6.30 UX review).
   */
  amountChanged(index: number, value: string): void {
    const line = this.draft.lines[index];
    if (!line) return;
    line.amount = value ?? '';
    this.issues.update(current => {
      const lines = { ...current.lines };
      delete lines[index];
      const header = { ...current.header };
      if (header.lines === 'exceedsUnapplied' || header.lines === 'invalid')
        header.lines = settlementDraftIssues(this.draft, this.receiptUnapplied(), this.receiptDate()).header.lines;
      return { header, lines };
    });
  }

  dateChanged(value: string): void {
    this.draft.businessDate = value ?? '';
    this.issues.update(current => ({ header: { ...current.header, businessDate: undefined }, lines: current.lines }));
  }

  removeLine(index: number): void {
    if (!this.editable()) return;
    this.draft.lines.splice(index, 1);
    this.issues.update(current => ({ header: { ...current.header, lines: undefined }, lines: {} }));
  }

  save(): void {
    if (this.saving() || !this.editable()) return;
    this.error.set(null); this.traceId.set(null); this.details.set([]); this.stale.set(false);
    const issues = settlementDraftIssues(this.draft, this.receiptUnapplied(), this.receiptDate());
    this.issues.set(issues);
    if (hasSettlementIssues(issues)) { this.focusFirstIssue(); return; }
    const epoch = this.epoch;
    this.saving.set(true);
    const request = this.id ? this.api.update(this.id, this.version, this.draft) : this.api.create(this.draft);
    request.subscribe({
      next: saved => {
        if (epoch !== this.epoch) return;
        this.saving.set(false); this.saved = JSON.stringify(this.draft);
        void this.router.navigateByUrl(LIST, { state: { collectionSettlementSaved: { no: saved.documentNo, created: !this.id,
          total: saved.totalAmount, currency: saved.currency, count: saved.lines.length } } });
      },
      error: e => {
        if (epoch !== this.epoch) return;
        this.saving.set(false); this.fail(e);
        this.issues.set(this.serverIssues(e));
        if (settlementLineErrors(e).length) this.refreshOpenAmounts(epoch);
        const code = (e?.error as { errorCode?: string } | null)?.errorCode ?? '';
        this.stale.set(STALE_CODES.includes(code));
        this.focusFirstIssue();
      },
    });
  }

  /** 400 fields and 422 refusals placed on their field or line (lines matched by receivable, as the server orders them by ID). */
  private serverIssues(error: unknown): SettlementDraftIssues {
    const result = noIssues();
    const fields = settlementFieldErrors(error);
    for (const field of fields.header) result.header[field] = 'invalid';
    for (const [index, field] of Object.entries(fields.lines))
      result.lines[Number(index)] = field === 'receivableId' ? 'receivableInvalid' : 'invalid';
    for (const lineError of settlementLineErrors(error)) {
      const index = this.draft.lines.findIndex(line => line.receivableId === lineError.receivableId);
      if (index >= 0) result.lines[index] = settlementLineReasonKey(lineError.reason);
    }
    const code = ((error as { error?: { errorCode?: string } } | null)?.error?.errorCode) ?? '';
    if (RECEIPT_CODES.includes(code)) result.header.receiptId = `errors.${code}`;
    return result;
  }

  /**
   * After the server refused lines, "Open now" is re-read so the user sees the amount that can still be applied (another
   * settlement or credit may have reduced it). Reads the customer's open receivables (any date, 200 per page, at most 5 pages =
   * the 1000-line limit); a line no longer listed has nothing open. Lines are only refreshed, never removed or re-amounted.
   */
  private refreshOpenAmounts(epoch: number, page = 1, found = new Map<number, string>()): void {
    const customerId = this.draft.customerId;
    if (!customerId) return;
    this.api.openReceivables(customerId, '', page, 200).subscribe({
      next: result => {
        if (epoch !== this.epoch) return;
        for (const row of result.items) found.set(row.receivableId, row.openAmount);
        if (page * 200 < result.totalCount && page < 5) { this.refreshOpenAmounts(epoch, page + 1, found); return; }
        for (const line of this.draft.lines) line.openAmount = found.get(line.receivableId) ?? '0';
        this.cdr.markForCheck();
      },
      // The refusal itself is already shown; a failed refresh keeps the amounts read before.
      error: () => undefined,
    });
  }

  private fail(error: unknown): void {
    this.error.set(this.api.errorKey(error)); this.traceId.set(apiErrorTraceId(error)); this.details.set(apiValidationDetails(error));
  }
  private party(row: { customerCode: string | null; customerName: string | null; customerId: number }): string {
    return row.customerCode ? `${row.customerCode} · ${row.customerName ?? ''}` : `#${row.customerId}`;
  }
  private focusFirstIssue(): void {
    const { header, lines } = this.issues();
    let testId: string | null = null;
    if (header.receiptId) testId = 'fi-settle-receipt-pick';
    else if (header.businessDate) testId = 'fi-settle-date';
    else {
      const index = this.draft.lines.findIndex((_, i) => lines[i]);
      if (index >= 0) testId = `fi-settle-form-amount-${this.draft.lines[index].receivableId}`;
      else if (header.lines) testId = 'fi-settle-add-lines';
      else if (header.remark) testId = 'fi-settle-remark';
    }
    if (testId) setTimeout(() => this.host.nativeElement.querySelector<HTMLElement>(`[data-testid="${testId}"]`)?.focus());
  }
}
