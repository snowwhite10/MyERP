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
import { ErpDialogService } from '../../shared/dialogs/dialog.service';
import { HasUnsavedChanges } from '../../shared/document-page/unsaved-changes.guard';
import { Customer, CustomerService } from '../md/customer.service';
import { FundAccount, FundAccountService } from '../md/fund-setup.service';
import {
  BANK_REFERENCE_MAX, COLLECTION_RECEIPT_FIELDS, CollectionReceiptDetail, CollectionReceiptDraft, CollectionReceiptField,
  CollectionReceiptService, receiptDraftIssues, receiptFieldErrors, REMARK_MAX,
} from './collection-receipt.service';

const LIST = '/ar/sales-collections';
const empty = (): CollectionReceiptDraft => ({ customerId: null, businessDate: '', amount: '', bankAccountId: null, bankReference: '', remark: '' });

/** Server refusals that belong to one field; shown under it as well as in the error banner. */
const FIELD_OF_CODE: Record<string, CollectionReceiptField> = {
  'FI.COLLECTION.BANK_REFERENCE_DUPLICATE': 'bankReference',
  'FI.COLLECTION.FUND_ACCOUNT_NOT_FOUND': 'bankAccountId',
  'FI.COLLECTION.FUND_ACCOUNT_INACTIVE': 'bankAccountId',
  'FI.COLLECTION.FUND_ACCOUNT_CURRENCY_MISMATCH': 'bankAccountId',
  'MD.CUSTOMER.NOT_FOUND': 'customerId',
  'MD.CUSTOMER.NOT_APPROVED': 'customerId',
};

/**
 * Sales collection receipt create / edit (BlueWhale 4.4.1.6.2–3, WU-FI-005c1, REQ_F4116.1.13). Customer and fund account come
 * from MD pickers; the amount is kept as typed text and sent exactly. Only Draft / Rejected receipts are editable (saving a
 * Rejected one returns it to Draft); other states show why the form is read-only. Field rules mirror the server (BR-FI-156).
 */
@Component({
  selector: 'erp-collection-receipt-form',
  imports: [FormsModule, RouterLink, TranslatePipe, NzAlertModule, NzButtonModule, NzInputModule, ApiErrorDisplay],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <section class="receipt-form">
      <header><h1>{{ (id ? 'fi.collectionReceipt.editTitle' : 'fi.collectionReceipt.createTitle') | translate }}
          @if (documentNo()) { <span class="doc-no" data-testid="fi-receipt-form-no">{{ documentNo() }}</span> }</h1>
        <a [routerLink]="list" data-testid="fi-receipt-back">{{ 'fi.collectionReceipt.back' | translate }}</a></header>
      <p class="hint">{{ 'fi.collectionReceipt.formIntro' | translate }}</p>
      @if (loading()) { <p class="hint" role="status" data-testid="fi-receipt-form-loading">{{ 'fi.collectionReceipt.loading' | translate }}</p> }
      @if (lockedKey()) { <nz-alert nzType="warning" nzShowIcon data-testid="fi-receipt-form-locked" [nzMessage]="lockedKey()! | translate" /> }
      @if (rejected()) { <nz-alert nzType="info" nzShowIcon data-testid="fi-receipt-form-rejected" [nzMessage]="'fi.collectionReceipt.rejectedHint' | translate" /> }
      @if (error()) {
        <erp-api-error-display data-testid="fi-receipt-form-error" [messageKey]="error()!" [traceId]="traceId()" [details]="details()" />
        @if (stale()) { <button nz-button type="button" class="reload" data-testid="fi-receipt-form-reload" (click)="reload()">{{ 'fi.collectionReceipt.reload' | translate }}</button> }
      }
      @if (!loading() && !loadFailed()) {
        <form class="fields" (ngSubmit)="save()" novalidate data-testid="fi-receipt-form">
          <div class="field">
            <span class="label" id="fi-receipt-customer-label">{{ 'fi.collectionReceipt.customer' | translate }} <span class="required" aria-hidden="true">*</span></span>
            <div class="picker" role="group" aria-labelledby="fi-receipt-customer-label" [attr.aria-invalid]="!!issue('customerId')">
              <span data-testid="fi-receipt-customer">{{ customerLabel() || ('fi.collectionReceipt.noCustomer' | translate) }}</span>
              <button nz-button type="button" data-testid="fi-receipt-customer-pick" [disabled]="!editable()" (click)="pickCustomer()">{{ 'fi.collectionReceipt.chooseCustomer' | translate }}</button>
            </div>
            @if (issue('customerId'); as key) { <p class="field-error" role="alert" data-testid="fi-receipt-issue-customerId">{{ key | translate }}</p> }
          </div>
          <div class="field">
            <label for="fi-receipt-date">{{ 'fi.collectionReceipt.businessDate' | translate }} <span class="required" aria-hidden="true">*</span></label>
            <input nz-input id="fi-receipt-date" name="businessDate" type="date" required [(ngModel)]="draft.businessDate" [disabled]="!editable()"
              data-testid="fi-receipt-date" [nzStatus]="issue('businessDate') ? 'error' : ''" [attr.aria-invalid]="!!issue('businessDate')" />
            @if (issue('businessDate'); as key) { <p class="field-error" role="alert" data-testid="fi-receipt-issue-businessDate">{{ key | translate }}</p> }
          </div>
          <div class="field">
            <label for="fi-receipt-amount">{{ 'fi.collectionReceipt.amount' | translate }} <span class="required" aria-hidden="true">*</span></label>
            <input nz-input id="fi-receipt-amount" name="amount" type="text" inputmode="decimal" autocomplete="off" required [(ngModel)]="draft.amount"
              [disabled]="!editable()" data-testid="fi-receipt-amount" [nzStatus]="issue('amount') ? 'error' : ''" [attr.aria-invalid]="!!issue('amount')"
              aria-describedby="fi-receipt-amount-help" />
            <span class="muted" id="fi-receipt-amount-help">{{ 'fi.collectionReceipt.amountHelp' | translate }}</span>
            @if (issue('amount'); as key) { <p class="field-error" role="alert" data-testid="fi-receipt-issue-amount">{{ key | translate }}</p> }
          </div>
          <div class="field">
            <span class="label" id="fi-receipt-fund-label">{{ 'fi.collectionReceipt.fundAccount' | translate }} <span class="required" aria-hidden="true">*</span></span>
            <div class="picker" role="group" aria-labelledby="fi-receipt-fund-label" [attr.aria-invalid]="!!issue('bankAccountId')">
              <span data-testid="fi-receipt-fund">{{ fundLabel() || ('fi.collectionReceipt.noFundAccount' | translate) }}</span>
              <button nz-button type="button" data-testid="fi-receipt-fund-pick" [disabled]="!editable()" (click)="pickFund()">{{ 'fi.collectionReceipt.chooseFundAccount' | translate }}</button>
            </div>
            <span class="muted">{{ 'fi.collectionReceipt.fundHelp' | translate }}</span>
            @if (issue('bankAccountId'); as key) { <p class="field-error" role="alert" data-testid="fi-receipt-issue-bankAccountId">{{ key | translate }}</p> }
          </div>
          <div class="field">
            <label for="fi-receipt-reference">{{ 'fi.collectionReceipt.bankReference' | translate }} <span class="required" aria-hidden="true">*</span></label>
            <input nz-input id="fi-receipt-reference" name="bankReference" autocomplete="off" spellcheck="false" required [maxlength]="referenceMax"
              [(ngModel)]="draft.bankReference" [disabled]="!editable()" data-testid="fi-receipt-reference"
              [nzStatus]="issue('bankReference') ? 'error' : ''" [attr.aria-invalid]="!!issue('bankReference')" aria-describedby="fi-receipt-reference-help" />
            <span class="muted" id="fi-receipt-reference-help">{{ 'fi.collectionReceipt.referenceHelp' | translate: { max: referenceMax } }}</span>
            @if (issue('bankReference'); as key) { <p class="field-error" role="alert" data-testid="fi-receipt-issue-bankReference">{{ key | translate: { max: referenceMax } }}</p> }
          </div>
          <div class="field">
            <label for="fi-receipt-remark">{{ 'fi.collectionReceipt.remark' | translate }}</label>
            <textarea nz-input id="fi-receipt-remark" name="remark" rows="2" [maxlength]="remarkMax" [(ngModel)]="draft.remark" [disabled]="!editable()"
              data-testid="fi-receipt-remark" [attr.aria-invalid]="!!issue('remark')"></textarea>
            @if (issue('remark'); as key) { <p class="field-error" role="alert" data-testid="fi-receipt-issue-remark">{{ key | translate: { max: remarkMax } }}</p> }
          </div>
          <div class="form-actions">
            <button nz-button nzType="primary" type="submit" data-testid="fi-receipt-save" [nzLoading]="saving()" [disabled]="!editable()"
              [attr.title]="lockedKey() ? (lockedKey()! | translate) : null">{{ 'fi.collectionReceipt.save' | translate }}</button>
            <a nz-button [routerLink]="list">{{ 'fi.collectionReceipt.cancel' | translate }}</a>
          </div>
          <p class="muted">{{ 'fi.collectionReceipt.saveHint' | translate }}</p>
        </form>
      }
    </section>`,
  styles: `.receipt-form{display:grid;gap:16px;color:var(--erp-color-text);min-width:0;max-width:760px}header{display:flex;gap:8px;align-items:center;justify-content:space-between;flex-wrap:wrap}h1{margin:0;font-size:20px;display:flex;gap:8px;align-items:baseline;flex-wrap:wrap}.doc-no{font-size:14px;color:var(--erp-color-text-secondary)}.hint,.muted{margin:0;color:var(--erp-color-text-secondary)}.muted{font-size:12px}.fields{display:grid;gap:12px;padding:16px;border:1px solid var(--erp-color-border);border-radius:8px;background:var(--erp-color-bg-container)}.field{display:grid;gap:4px;max-width:520px}.label{font-weight:400}.picker{display:flex;gap:8px;align-items:center;flex-wrap:wrap;min-width:0}.picker span{overflow-wrap:anywhere}.required,.field-error{color:var(--erp-color-error-text)}.field-error{margin:0}.form-actions{display:flex;gap:8px;flex-wrap:wrap}.reload{justify-self:start}button:focus-visible,a:focus-visible,input:focus-visible,textarea:focus-visible{outline:2px solid var(--erp-color-primary);outline-offset:2px}`,
})
export class CollectionReceiptForm implements HasUnsavedChanges {
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly api = inject(CollectionReceiptService);
  private readonly customers = inject(CustomerService);
  private readonly funds = inject(FundAccountService);
  private readonly dialogs = inject(ErpDialogService);
  private readonly company = inject(CompanyContextService);
  private readonly cdr = inject(ChangeDetectorRef);
  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef);
  readonly list = LIST;
  readonly referenceMax = BANK_REFERENCE_MAX;
  readonly remarkMax = REMARK_MAX;
  readonly id = Number(this.route.snapshot.paramMap.get('id')) || null;
  readonly loading = signal(false); readonly loadFailed = signal(false); readonly saving = signal(false);
  readonly error = signal<string | null>(null); readonly traceId = signal<string | null>(null);
  readonly details = signal<readonly string[]>([]); readonly stale = signal(false);
  readonly issues = signal<Partial<Record<CollectionReceiptField, string>>>({});
  readonly customerLabel = signal(''); readonly fundLabel = signal('');
  readonly documentNo = signal(''); readonly lockedKey = signal<string | null>(null); readonly rejected = signal(false);
  draft: CollectionReceiptDraft = empty();
  private version = 0;
  private saved = JSON.stringify(empty());
  private epoch = 0;
  private skipFirstCompany = true;

  constructor() {
    effect(() => {
      this.company.current();
      untracked(() => {
        // Another company's customer and fund account IDs mean nothing here: a switch while editing returns to the list.
        if (this.skipFirstCompany) { this.skipFirstCompany = false; this.start(); return; }
        this.epoch++; this.saved = JSON.stringify(this.draft); void this.router.navigateByUrl(LIST);
      });
    });
  }

  editable(): boolean { return !this.loading() && !this.lockedKey() && !this.saving(); }
  issue(field: CollectionReceiptField): string | null {
    const key = this.issues()[field];
    if (!key) return null;
    return key.includes('.') ? key : `fi.collectionReceipt.issue.${key}`;
  }
  hasUnsavedChanges(): boolean { return !this.lockedKey() && JSON.stringify(this.draft) !== this.saved; }

  reload(): void { this.start(); }

  private start(): void {
    const epoch = ++this.epoch;
    this.error.set(null); this.traceId.set(null); this.details.set([]); this.stale.set(false); this.issues.set({});
    if (!this.id) { this.draft.businessDate ||= isoDate(new Date()); this.saved = JSON.stringify(this.draft); return; }
    this.loading.set(true); this.loadFailed.set(false);
    this.api.get(this.id).subscribe({
      next: detail => { if (epoch === this.epoch) this.apply(detail); },
      error: e => { if (epoch !== this.epoch) return; this.loading.set(false); this.loadFailed.set(true); this.fail(e); },
    });
  }

  private apply(detail: CollectionReceiptDetail): void {
    this.version = detail.version;
    this.documentNo.set(detail.documentNo);
    this.draft = { customerId: detail.customerId, businessDate: detail.businessDate, amount: detail.amount,
      bankAccountId: detail.bankAccountId, bankReference: detail.bankReference, remark: detail.remark ?? '' };
    this.saved = JSON.stringify(this.draft);
    this.customerLabel.set(detail.customerCode ? `${detail.customerCode} · ${detail.customerName ?? ''}` : `#${detail.customerId}`);
    this.fundLabel.set(detail.bankAccountCode ? `${detail.bankAccountCode} · ${detail.bankAccountName ?? ''}` : `#${detail.bankAccountId}`);
    const editable = detail.status === 'Draft' || detail.status === 'Rejected';
    this.lockedKey.set(editable ? null : `fi.collectionReceipt.lockedHint.${detail.status.toLowerCase()}`);
    this.rejected.set(detail.status === 'Rejected');
    this.loading.set(false);
    this.cdr.markForCheck();
  }

  pickCustomer(): void {
    const epoch = this.epoch;
    this.dialogs.select<Customer>({ titleKey: 'fi.collectionReceipt.chooseCustomer',
      columns: [{ key: 'code', labelKey: 'fi.collectionReceipt.code', value: row => row.code },
        { key: 'name', labelKey: 'fi.collectionReceipt.name', value: row => row.name }],
      load: query => this.customers.list(query), rowKey: row => row.id }).subscribe(rows => {
      if (epoch !== this.epoch || !rows?.[0]) return;
      this.draft.customerId = rows[0].id; this.customerLabel.set(`${rows[0].code} · ${rows[0].name}`);
      this.clearIssue('customerId'); this.cdr.markForCheck();
    });
  }

  pickFund(): void {
    const epoch = this.epoch;
    this.dialogs.select<FundAccount>({ titleKey: 'fi.collectionReceipt.chooseFundAccount',
      columns: [{ key: 'code', labelKey: 'fi.collectionReceipt.code', value: row => row.code },
        { key: 'name', labelKey: 'fi.collectionReceipt.name', value: row => row.name },
        { key: 'bankName', labelKey: 'fi.collectionReceipt.bankName', value: row => row.bankName ?? '' }],
      load: query => this.funds.list(query), rowKey: row => row.id }).subscribe(rows => {
      if (epoch !== this.epoch || !rows?.[0]) return;
      this.draft.bankAccountId = rows[0].id; this.fundLabel.set(`${rows[0].code} · ${rows[0].name}`);
      this.clearIssue('bankAccountId'); this.cdr.markForCheck();
    });
  }

  save(): void {
    if (this.saving() || !this.editable()) return;
    this.error.set(null); this.traceId.set(null); this.details.set([]); this.stale.set(false);
    const issues = receiptDraftIssues(this.draft);
    this.issues.set(issues);
    if (Object.keys(issues).length) { this.focusFirstIssue(); return; }
    const epoch = this.epoch;
    this.saving.set(true);
    const request = this.id ? this.api.update(this.id, this.version, this.draft) : this.api.create(this.draft);
    request.subscribe({
      next: saved => {
        if (epoch !== this.epoch) return;
        this.saving.set(false); this.saved = JSON.stringify(this.draft);
        void this.router.navigateByUrl(LIST, { state: { collectionReceiptSaved: { no: saved.documentNo, created: !this.id } } });
      },
      error: e => {
        if (epoch !== this.epoch) return;
        this.saving.set(false); this.fail(e);
        const code = (e?.error as { errorCode?: string } | null)?.errorCode ?? '';
        const fields = receiptFieldErrors(e);
        const next: Partial<Record<CollectionReceiptField, string>> = {};
        for (const field of fields) next[field] = 'invalid';
        if (FIELD_OF_CODE[code]) next[FIELD_OF_CODE[code]] = `errors.${code}`;
        this.issues.set(next);
        this.stale.set(code === 'COMMON.CONCURRENCY.CONFLICT' || code === 'FI.COLLECTION.RECEIPT_INVALID_STATUS'
          || code === 'FI.COLLECTION.RECEIPT_NOT_FOUND');
        if (Object.keys(next).length) this.focusFirstIssue();
      },
    });
  }

  private clearIssue(field: CollectionReceiptField): void {
    this.issues.update(current => { const next = { ...current }; delete next[field]; return next; });
  }
  private fail(error: unknown): void {
    this.error.set(this.api.errorKey(error)); this.traceId.set(apiErrorTraceId(error)); this.details.set(apiValidationDetails(error));
  }
  private focusFirstIssue(): void {
    const field = COLLECTION_RECEIPT_FIELDS.find(name => this.issues()[name]);
    if (!field) return;
    const testId = { customerId: 'fi-receipt-customer-pick', businessDate: 'fi-receipt-date', amount: 'fi-receipt-amount',
      bankAccountId: 'fi-receipt-fund-pick', bankReference: 'fi-receipt-reference', remark: 'fi-receipt-remark' }[field];
    setTimeout(() => this.host.nativeElement.querySelector<HTMLElement>(`[data-testid="${testId}"]`)?.focus());
  }
}
