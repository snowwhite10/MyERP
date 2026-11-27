import { HttpErrorResponse } from '@angular/common/http';
import { ChangeDetectionStrategy, Component, HostListener, inject, input, OnInit, output, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import { NzButtonModule } from 'ng-zorro-antd/button';
import { NzAlertModule } from 'ng-zorro-antd/alert';
import { NzInputModule } from 'ng-zorro-antd/input';
import { Observable } from 'rxjs';
import { PermissionService } from '../../core/auth/permission.service';
import { apiErrorTraceId } from '../../core/http/api-error';
import { ApiErrorDisplay } from '../../shared/api-error-display';
import { formatDecimalText } from '../../shared/decimal-text';
import { FundAccount, FundAccountService, IncomeExpenseCategory, IncomeExpenseCategoryService } from '../md/fund-setup.service';
import { otherPaymentIssues, OtherPaymentIssues } from './other-payment-draft';
import { OtherPaymentDetail, OtherPaymentDraft, OtherPaymentService, OtherPaymentStatus, OtherPaymentWorkflowResult } from './other-payment.service';

const emptyDraft = (): OtherPaymentDraft => ({ payeeDescription: '', expenseCategoryId: 0, bankAccountId: 0,
  bankReference: '', businessDate: '', amount: '', remark: '' });

const RELOAD_ERRORS = new Set(['fi.otherPayment.error.version', 'fi.otherPayment.error.missing', 'fi.otherPayment.error.status']);

/** One payment's saved detail and version-bound operations; the list owns navigation and refresh. */
@Component({
  selector: 'erp-other-payment-editor',
  imports: [FormsModule, TranslatePipe, NzAlertModule, NzButtonModule, NzInputModule, ApiErrorDisplay],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <section class="editor" [attr.aria-label]="'fi.otherPayment.detailTitle' | translate" data-testid="fi-op-editor">
      <header><h2>{{ (documentId() === null ? 'fi.otherPayment.createTitle' : 'fi.otherPayment.detailTitle') | translate }}</h2>
        <button nz-button type="button" (click)="close()" data-testid="fi-op-close">{{ 'fi.otherPayment.close' | translate }}</button></header>
      @if (loading()) { <p role="status">{{ 'fi.otherPayment.loading' | translate }}</p> }
      @if (busy()) { <p role="status" aria-live="polite">{{ 'fi.otherPayment.working' | translate }}</p> }
      @if (notice(); as message) { <nz-alert [nzType]="message.warning ? 'warning' : 'success'" [nzMessage]="message.key | translate: message.params"
        nzShowIcon role="status" aria-live="polite" data-testid="fi-op-notice" /> }
      @if (error()) { <erp-api-error-display [messageKey]="error()!" [traceId]="traceId()" data-testid="fi-op-write-error" />
        @if (documentId() !== null && reloadHelps()) { <div class="recover"><button nz-button type="button" (click)="reload()" data-testid="fi-op-reload">{{ 'fi.otherPayment.reload' | translate }}</button></div> }
      }
      @if (detail(); as saved) {
        <dl class="summary"><div><dt>{{ 'fi.otherPayment.documentNo' | translate }}</dt><dd translate="no">{{ saved.documentNo }}</dd></div>
          <div><dt>{{ 'fi.otherPayment.status' | translate }}</dt><dd>{{ statusKey(saved.status) | translate }}</dd></div>
          <div><dt>{{ 'fi.otherPayment.journal' | translate }}</dt><dd translate="no">{{ saved.journalId ? '#' + saved.journalId : '—' }}</dd></div>
          <div><dt>{{ 'fi.otherPayment.amount' | translate }}</dt><dd>{{ money(saved.amount) }} {{ saved.currency }}</dd></div></dl>
      }
      @if (!loading() && (documentId() === null || detail())) {
        @if (editing()) {
          <form (ngSubmit)="save()" class="form" novalidate data-testid="fi-op-form">
            <label><span>{{ 'fi.otherPayment.payee' | translate }} <span class="required" aria-hidden="true">*</span></span><input nz-input name="payee" autocomplete="off" maxlength="200" required
              [(ngModel)]="draft.payeeDescription" [attr.aria-invalid]="!!issues().payeeDescription" data-testid="fi-op-payee" />
              @if (issues().payeeDescription) { <small role="alert">{{ 'fi.otherPayment.validation.payeeInvalid' | translate }}</small> }</label>
            <div class="choice"><label for="fi-op-category"><span>{{ 'fi.otherPayment.category' | translate }} <span class="required" aria-hidden="true">*</span></span></label>
              <input nz-input name="categorySearch" autocomplete="off" [(ngModel)]="categorySearch"
                [attr.aria-label]="'fi.otherPayment.categorySearch' | translate" [placeholder]="'fi.otherPayment.categorySearch' | translate" />
              <button nz-button type="button" (click)="loadCategories()">{{ 'fi.otherPayment.find' | translate }}</button>
              <select id="fi-op-category" name="category" [(ngModel)]="draft.expenseCategoryId" data-testid="fi-op-category" required [attr.aria-invalid]="!!issues().expenseCategoryId">
                <option [ngValue]="0">{{ 'fi.otherPayment.chooseCategory' | translate }}</option>
                @if (detail(); as saved) { @if (!hasCategory(saved.expenseCategoryId)) {
                  <option [ngValue]="saved.expenseCategoryId">{{ saved.expenseCategoryCode }} · {{ saved.expenseCategoryName }}</option> } }
                @for (category of categories(); track category.id) { @if (category.isActive) {
                  <option [ngValue]="category.id">{{ category.code }} · {{ category.name }}</option> } }
              </select>
              @if (issues().expenseCategoryId) { <small role="alert">{{ 'fi.otherPayment.validation.categoryRequired' | translate }}</small> }
              @if (categories().length < categoryTotal()) { <button nz-button type="button" (click)="loadCategories(true)">{{ 'fi.otherPayment.more' | translate }}</button> }</div>
            <div class="choice"><label for="fi-op-fund"><span>{{ 'fi.otherPayment.fundAccount' | translate }} <span class="required" aria-hidden="true">*</span></span></label>
              <input nz-input name="fundSearch" autocomplete="off" [(ngModel)]="fundSearch"
                [attr.aria-label]="'fi.otherPayment.fundSearch' | translate" [placeholder]="'fi.otherPayment.fundSearch' | translate" />
              <button nz-button type="button" (click)="loadFunds()">{{ 'fi.otherPayment.find' | translate }}</button>
              <select id="fi-op-fund" name="fund" [(ngModel)]="draft.bankAccountId" data-testid="fi-op-fund" required [attr.aria-invalid]="!!issues().bankAccountId">
                <option [ngValue]="0">{{ 'fi.otherPayment.chooseFund' | translate }}</option>
                @if (detail(); as saved) { @if (!hasFund(saved.bankAccountId)) {
                  <option [ngValue]="saved.bankAccountId">{{ saved.bankAccountCode }} · {{ saved.bankAccountName }}</option> } }
                @for (fund of funds(); track fund.id) { @if (fund.isActive) {
                  <option [ngValue]="fund.id">{{ fund.code }} · {{ fund.name }}</option> } }
              </select>
              @if (issues().bankAccountId) { <small role="alert">{{ 'fi.otherPayment.validation.fundRequired' | translate }}</small> }
              @if (funds().length < fundTotal()) { <button nz-button type="button" (click)="loadFunds(true)">{{ 'fi.otherPayment.more' | translate }}</button> }</div>
            @if (optionError()) { <erp-api-error-display [messageKey]="optionError()!" /> }
            <label><span>{{ 'fi.otherPayment.bankReference' | translate }} <span class="required" aria-hidden="true">*</span></span><input nz-input name="reference" autocomplete="off" spellcheck="false"
              maxlength="50" required [(ngModel)]="draft.bankReference" data-testid="fi-op-reference" [attr.aria-invalid]="!!issues().bankReference" />
              @if (issues().bankReference) { <small role="alert" data-testid="fi-op-reference-error">{{ (issues().bankReference === 'duplicate' ? 'fi.otherPayment.error.reference'
              : 'fi.otherPayment.validation.referenceInvalid') | translate }}</small> }</label>
            <label><span>{{ 'fi.otherPayment.businessDate' | translate }} <span class="required" aria-hidden="true">*</span></span><input name="date" type="date" required [(ngModel)]="draft.businessDate"
              data-testid="fi-op-date" [attr.aria-invalid]="!!issues().businessDate" />
              @if (issues().businessDate) { <small role="alert">{{ 'fi.otherPayment.validation.dateInvalid' | translate }}</small> }</label>
            <label><span>{{ 'fi.otherPayment.amount' | translate }} <span class="required" aria-hidden="true">*</span></span><input nz-input name="amount" type="text" inputmode="decimal" autocomplete="off" required
              [(ngModel)]="draft.amount" data-testid="fi-op-amount" [attr.aria-invalid]="!!issues().amount" />
              @if (issues().amount) { <small role="alert">{{ 'fi.otherPayment.validation.amountInvalid' | translate }}</small> }</label>
            <label>{{ 'fi.otherPayment.remark' | translate }}<textarea nz-input name="remark" autocomplete="off" maxlength="500" [(ngModel)]="draft.remark"></textarea>
              @if (issues().remark) { <small role="alert">{{ 'fi.otherPayment.validation.remarkInvalid' | translate }}</small> }</label>
            <div class="actions"><button nz-button nzType="primary" type="submit" [disabled]="busy()" data-testid="fi-op-save">
              {{ (busy() ? 'fi.otherPayment.saving' : 'fi.otherPayment.save') | translate }}</button>
              @if (documentId() !== null) { <button nz-button type="button" (click)="cancelEdit()" [disabled]="busy()">{{ 'fi.otherPayment.cancel' | translate }}</button> }
            </div>
          </form>
        } @else if (detail(); as saved) {
          @if (!editable(saved.status)) { <p class="hint">{{ 'fi.otherPayment.readOnlyState' | translate }}</p> }
          <dl class="summary"><div><dt>{{ 'fi.otherPayment.payee' | translate }}</dt><dd>{{ saved.payeeDescription }}</dd></div>
            <div><dt>{{ 'fi.otherPayment.category' | translate }}</dt><dd>{{ saved.expenseCategoryCode }} · {{ saved.expenseCategoryName }}</dd></div>
            <div><dt>{{ 'fi.otherPayment.fundAccount' | translate }}</dt><dd>{{ saved.bankAccountCode }} · {{ saved.bankAccountName }}</dd></div>
            <div><dt>{{ 'fi.otherPayment.bankReference' | translate }}</dt><dd translate="no">{{ saved.bankReference }}</dd></div>
            <div><dt>{{ 'fi.otherPayment.businessDate' | translate }}</dt><dd>{{ saved.businessDate }}</dd></div>
            <div><dt>{{ 'fi.otherPayment.remark' | translate }}</dt><dd>{{ saved.remark || '—' }}</dd></div></dl>
          <div class="actions">
            @if (editable(saved.status) && canEdit) { <button nz-button type="button" (click)="edit()" data-testid="fi-op-edit">{{ 'fi.otherPayment.edit' | translate }}</button> }
            @if (editable(saved.status) && canSubmit) { <button nz-button nzType="primary" type="button" (click)="action('submit')" [disabled]="busy()" data-testid="fi-op-submit">{{ 'fi.otherPayment.submit' | translate }}</button> }
            @if (saved.status === 'Pending' && canSubmit) { <button nz-button type="button" (click)="action('withdraw')" [disabled]="busy()" data-testid="fi-op-withdraw">{{ 'fi.otherPayment.withdraw' | translate }}</button> }
            @if (editable(saved.status) && canDelete) { <button nz-button nzDanger type="button" (click)="action('delete')" [disabled]="busy()" data-testid="fi-op-delete">{{ 'fi.otherPayment.delete' | translate }}</button> }
          </div>
        }
      }
    </section>`,
  styles: `:host{display:block;min-width:0}.editor{display:grid;gap:16px;padding:24px;border:1px solid var(--erp-color-border);border-radius:8px;background:var(--erp-color-bg-container);min-width:0}header{display:flex;justify-content:space-between;align-items:center;gap:16px}h2{font-size:18px;margin:0;text-wrap:balance}.summary{display:grid;grid-template-columns:repeat(auto-fit,minmax(180px,1fr));gap:12px;margin:0}.summary div{min-width:0}.summary dt{color:var(--erp-color-text-secondary)}.summary dd{margin:0;overflow-wrap:anywhere}.hint{color:var(--erp-color-text-secondary)}.required{color:var(--erp-color-error-text)}.form{display:grid;grid-template-columns:repeat(2,minmax(0,1fr));gap:8px 16px}.form label,.form .choice{display:grid;gap:4px;min-width:0;align-content:start}.form select,.form input[type=date]{background:var(--erp-color-bg-container);color:var(--erp-color-text);border:1px solid var(--erp-color-border);border-radius:4px;padding:4px}.form small{color:var(--erp-color-error-text)}.recover{display:flex;justify-content:flex-start}.actions{display:flex;flex-wrap:wrap;gap:8px;grid-column:1/-1}button:focus-visible,input:focus-visible,select:focus-visible,textarea:focus-visible{outline:2px solid var(--erp-color-primary);outline-offset:2px}@media(max-width:767px){.form{grid-template-columns:1fr}}`,
})
export class OtherPaymentEditor implements OnInit {
  readonly paymentId = input<number | null>(null);
  readonly changed = output<void>(); readonly dismissed = output<void>();
  /** Emits the deleted document number so the list can confirm the removal after the editor closes. */
  readonly deleted = output<string>();
  private readonly api = inject(OtherPaymentService);
  private readonly categoriesApi = inject(IncomeExpenseCategoryService);
  private readonly fundsApi = inject(FundAccountService);
  private readonly permission = inject(PermissionService);
  private readonly translate = inject(TranslateService);
  readonly canEdit = this.permission.has('AP.OTHER_PAYMENT.EDIT');
  readonly canSubmit = this.permission.has('AP.OTHER_PAYMENT.SUBMIT');
  readonly canDelete = this.permission.has('AP.OTHER_PAYMENT.DELETE');
  readonly detail = signal<OtherPaymentDetail | null>(null);
  readonly documentId = signal<number | null>(null);
  readonly loading = signal(false); readonly busy = signal(false); readonly editing = signal(true);
  readonly error = signal<string | null>(null); readonly traceId = signal<string | null>(null);
  readonly optionError = signal<string | null>(null);
  readonly issues = signal<OtherPaymentIssues>({});
  readonly notice = signal<{ key: string; params?: Record<string, string>; warning: boolean } | null>(null);
  readonly categories = signal<IncomeExpenseCategory[]>([]); readonly categoryTotal = signal(0);
  readonly funds = signal<FundAccount[]>([]); readonly fundTotal = signal(0);
  draft = emptyDraft(); categorySearch = ''; fundSearch = '';
  private categoryPage = 0; private fundPage = 0; private original = '';

  ngOnInit(): void { this.documentId.set(this.paymentId()); this.loadCategories(); this.loadFunds(); if (this.documentId() !== null) this.loadDetail();
    else this.original = JSON.stringify(this.draft); }
  @HostListener('window:beforeunload', ['$event']) beforeUnload(event: BeforeUnloadEvent): void {
    if (this.dirty()) event.preventDefault();
  }
  statusKey(status: OtherPaymentStatus): string { return `fi.otherPayment.state.${status.toLowerCase()}`; }
  money(value: string): string { return formatDecimalText(value, 'en-US'); }
  editable(status: OtherPaymentStatus): boolean { return status === 'Draft' || status === 'Rejected'; }
  hasCategory(id: number): boolean { return this.categories().some(x => x.id === id && x.isActive); }
  hasFund(id: number): boolean { return this.funds().some(x => x.id === id && x.isActive); }
  dirty(): boolean { return this.editing() && JSON.stringify(this.draft) !== this.original; }
  close(): void {
    if (this.dirty() && !window.confirm(this.translate.instant('fi.otherPayment.discard'))) return;
    this.dismissed.emit();
  }
  cancelEdit(): void {
    if (this.dirty() && !window.confirm(this.translate.instant('fi.otherPayment.discard'))) return;
    this.editing.set(false); this.error.set(null); this.issues.set({});
  }
  reload(): void {
    if (this.dirty() && !window.confirm(this.translate.instant('fi.otherPayment.discard'))) return;
    this.loadDetail();
  }
  loadCategories(more = false): void {
    if (!more) { this.categoryPage = 0; this.categories.set([]); }
    this.categoriesApi.list({ page: this.categoryPage + 1, pageSize: 100, keyword: this.categorySearch,
      direction: 'Expense' }).subscribe({ next: result => {
        this.categoryPage++; this.categoryTotal.set(result.totalCount);
        this.categories.set([...this.categories(), ...result.items]); this.optionError.set(null);
      }, error: e => this.optionError.set(this.categoriesApi.errorKey(e)) });
  }
  loadFunds(more = false): void {
    if (!more) { this.fundPage = 0; this.funds.set([]); }
    this.fundsApi.list({ page: this.fundPage + 1, pageSize: 100, keyword: this.fundSearch }).subscribe({ next: result => {
      this.fundPage++; this.fundTotal.set(result.totalCount);
      this.funds.set([...this.funds(), ...result.items]); this.optionError.set(null);
    }, error: e => this.optionError.set(this.fundsApi.errorKey(e)) });
  }
  loadDetail(): void {
    const id = this.documentId(); if (id === null) return;
    this.loading.set(true); this.error.set(null);
    this.api.detail(id).subscribe({ next: saved => { this.detail.set(saved); this.copy(saved); this.editing.set(false);
      this.loading.set(false); }, error: e => { this.handleError(e); this.loading.set(false); } });
  }
  private copy(saved: OtherPaymentDetail): void {
    this.draft = { payeeDescription: saved.payeeDescription, expenseCategoryId: saved.expenseCategoryId,
      bankAccountId: saved.bankAccountId, bankReference: saved.bankReference, businessDate: saved.businessDate,
      amount: saved.amount, remark: saved.remark ?? '' };
    this.original = JSON.stringify(this.draft); this.issues.set({});
  }
  edit(): void { if (this.detail() && this.editable(this.detail()!.status) && this.canEdit) this.editing.set(true); }
  save(): void {
    if (this.busy()) return;
    const issues = otherPaymentIssues(this.draft); this.issues.set(issues);
    if (Object.keys(issues).length) { this.focusFirstIssue(issues); return; }
    this.busy.set(true); this.error.set(null); this.notice.set(null);
    const clean = { ...this.draft, payeeDescription: this.draft.payeeDescription.trim(),
      bankReference: this.draft.bankReference.trim(), amount: this.draft.amount.trim(), remark: this.draft.remark?.trim() || null };
    const saved = this.detail();
    const request = saved ? this.api.update(saved.id, saved.version, clean) : this.api.create(clean);
    request.subscribe({ next: result => {
      this.busy.set(false); this.documentId.set(result.id); this.editing.set(false);
      this.notice.set({ key: 'fi.otherPayment.saved', warning: false }); this.changed.emit(); this.loadDetail();
    }, error: e => { this.busy.set(false); this.handleError(e); } });
  }
  action(kind: 'submit' | 'withdraw' | 'delete'): void {
    const saved = this.detail(); if (!saved || this.busy()) return;
    if (kind === 'delete' && !window.confirm(this.translate.instant('fi.otherPayment.confirmDelete'))) return;
    if (kind === 'submit' && !this.editable(saved.status)) return;
    if (kind === 'withdraw' && saved.status !== 'Pending') return;
    if (kind === 'delete' && !this.editable(saved.status)) return;
    this.busy.set(true); this.error.set(null); this.notice.set(null);
    const request: Observable<unknown> = kind === 'delete' ? this.api.remove(saved.id, saved.version)
      : kind === 'submit' ? this.api.submit(saved.id, saved.version) : this.api.withdraw(saved.id, saved.version);
    request.subscribe({ next: result => { this.busy.set(false); this.changed.emit();
      if (kind === 'delete') { this.deleted.emit(saved.documentNo); this.dismissed.emit(); return; }
      this.notice.set({ key: this.actionNoticeKey(kind, (result as OtherPaymentWorkflowResult | null)?.status),
        params: { no: saved.documentNo }, warning: false });
      this.loadDetail();
    }, error: e => { this.busy.set(false); this.handleError(e); } });
  }
  /** Zero-step workflows approve and post inside the submit call, so the notice follows the returned status. */
  private actionNoticeKey(kind: 'submit' | 'withdraw', status: OtherPaymentStatus | undefined): string {
    if (kind === 'withdraw') return 'fi.otherPayment.withdrawn';
    return status === 'Posted' || status === 'Approved' ? 'fi.otherPayment.submittedPosted' : 'fi.otherPayment.submitted';
  }
  /** Reloading only helps when the saved row moved on or the server failed; input errors are fixed in place. */
  reloadHelps(): boolean {
    const key = this.error() ?? '';
    return !key.startsWith('fi.otherPayment.error.') || RELOAD_ERRORS.has(key);
  }
  private handleError(error: unknown): void {
    this.error.set(this.api.errorKey(error)); this.traceId.set(apiErrorTraceId(error));
    if (this.error() === 'fi.otherPayment.error.reference') {
      const issues: OtherPaymentIssues = { bankReference: 'duplicate' }; this.issues.set(issues); this.focusFirstIssue(issues); return;
    }
    if (error instanceof HttpErrorResponse && error.status === 400) {
      const fields = (error.error as { errors?: Record<string, unknown> } | null)?.errors ?? {};
      const issues: OtherPaymentIssues = {};
      for (const field of Object.keys(fields) as (keyof OtherPaymentDraft)[]) {
        if (field in this.draft) issues[field] = `${field}Invalid`;
      }
      this.issues.set(issues); this.focusFirstIssue(issues);
    }
  }
  private focusFirstIssue(issues: OtherPaymentIssues): void {
    const field = Object.keys(issues)[0];
    const id: Record<string, string> = { payeeDescription: 'fi-op-payee', expenseCategoryId: 'fi-op-category',
      bankAccountId: 'fi-op-fund', bankReference: 'fi-op-reference', businessDate: 'fi-op-date', amount: 'fi-op-amount' };
    if (id[field]) document.querySelector<HTMLElement>(`[data-testid="${id[field]}"]`)?.focus();
  }
}
