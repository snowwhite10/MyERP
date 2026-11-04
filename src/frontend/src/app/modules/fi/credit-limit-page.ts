import { ChangeDetectionStrategy, ChangeDetectorRef, Component, DestroyRef, effect, HostListener, inject, signal, untracked } from '@angular/core';
import { FormControl, FormGroup, FormsModule, ReactiveFormsModule, Validators } from '@angular/forms';
import { ActivatedRoute, Router } from '@angular/router';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import { NzAlertModule } from 'ng-zorro-antd/alert';
import { NzButtonModule } from 'ng-zorro-antd/button';
import { NzInputModule } from 'ng-zorro-antd/input';
import { NzModalModule } from 'ng-zorro-antd/modal';
import { NzTableModule, NzTableQueryParams } from 'ng-zorro-antd/table';
import { NzTagModule } from 'ng-zorro-antd/tag';
import { HasPermissionDirective } from '../../core/auth/has-permission.directive';
import { CompanyContextService } from '../../core/company/company-context.service';
import { apiErrorTraceId } from '../../core/http/api-error';
import { ApiErrorDisplay } from '../../shared/api-error-display';
import { ErpDialogService } from '../../shared/dialogs/dialog.service';
import { HasUnsavedChanges } from '../../shared/document-page/unsaved-changes.guard';
import { isMissingRowError, isStaleRowError } from '../../shared/list-page/stale-row';
import { nextTableState, PAGE_SIZE_OPTIONS, sortOrderOf } from '../../shared/list-page/table-query';
import { Customer } from '../md/customer.service';
import {
  CREDIT_AMOUNT_PATTERN, CREDIT_LIMIT_SORTS, CREDIT_LIMIT_STATUSES, CreditLimitDetail, CreditLimitDraft,
  CreditLimitService, CreditLimitStatus, CreditLimitSummary, DEFAULT_CREDIT_LIMIT_SORT, formatCreditAmount,
} from './credit-limit.service';

const DATE = /^\d{4}-\d{2}-\d{2}$/;
type CustomerLabel = Pick<Customer, 'id' | 'code' | 'name'>;
type FormField = keyof CreditLimitDraft;

function validDate(value: string): boolean {
  if (!DATE.test(value)) return false;
  const parsed = new Date(`${value}T00:00:00Z`);
  return !Number.isNaN(parsed.getTime()) && parsed.toISOString().slice(0, 10) === value;
}

/** 客戶信用額度查看／新增／修改（介面 6.17–6.19，REQ_F4116.7.1，WU-FI-001c）。 */
@Component({
  selector: 'erp-credit-limit-page',
  imports: [FormsModule, ReactiveFormsModule, TranslatePipe, NzAlertModule, NzButtonModule, NzInputModule,
    NzModalModule, NzTableModule, NzTagModule, HasPermissionDirective, ApiErrorDisplay],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <section class="credit-page">
      <header class="credit-head"><h1>{{ 'fi.creditLimit.title' | translate }}</h1>
        <button nz-button nzType="primary" type="button" data-testid="fi-credit-create" *erpHasPermission="'AR.CREDIT.CREATE'"
          (click)="openCreate()">{{ 'fi.creditLimit.create' | translate }}</button></header>
      <p class="hint">{{ 'fi.creditLimit.intro' | translate }}</p>
      <form class="filters" (ngSubmit)="search()" data-testid="fi-credit-filters">
        <div class="field"><span id="fi-credit-filter-customer">{{ 'fi.creditLimit.customer' | translate }}</span>
          <div class="picker" role="group" aria-labelledby="fi-credit-filter-customer">
            <span data-testid="fi-credit-filter-customer">{{ filterCustomer() ? customerText(filterCustomer()!.id) : ('fi.creditLimit.allCustomers' | translate) }}</span>
            <button nz-button type="button" data-testid="fi-credit-filter-customer-pick" (click)="pickFilterCustomer()">{{ 'fi.creditLimit.chooseCustomer' | translate }}</button>
            @if (filterCustomer()) { <button nz-button nzType="link" type="button" data-testid="fi-credit-filter-customer-clear"
              (click)="filterCustomer.set(null)">{{ 'fi.creditLimit.clearCustomer' | translate }}</button> }
          </div></div>
        <label class="field">{{ 'fi.creditLimit.status' | translate }}
          <select name="status" [(ngModel)]="status" data-testid="fi-credit-status">
            <option value="">{{ 'fi.creditLimit.allStatuses' | translate }}</option>
            @for (value of statuses; track value) { <option [value]="value">{{ statusKey(value) | translate }}</option> }
          </select></label>
        <label class="field">{{ 'fi.creditLimit.windowFrom' | translate }}
          <input type="date" name="from" [(ngModel)]="from" data-testid="fi-credit-from" /></label>
        <label class="field">{{ 'fi.creditLimit.windowTo' | translate }}
          <input type="date" name="to" [(ngModel)]="to" data-testid="fi-credit-to" /></label>
        <div class="filter-actions"><button nz-button nzType="primary" type="submit" data-testid="fi-credit-search">{{ 'fi.creditLimit.search' | translate }}</button>
          <button nz-button type="button" data-testid="fi-credit-reset" (click)="reset()">{{ 'fi.creditLimit.reset' | translate }}</button></div>
      </form>
      @if (validation()) { <p class="field-error" role="alert" data-testid="fi-credit-validation">{{ validation()! | translate }}</p> }
      <div class="live" role="status" aria-live="polite">@if (notice(); as message) { <nz-alert nzType="success" nzShowIcon nzCloseable data-testid="fi-credit-notice"
        [nzMessage]="message.key | translate: message.params" (nzOnClose)="notice.set(null)" /> }</div>
      @if (actionError()) { <erp-api-error-display data-testid="fi-credit-action-error" [messageKey]="actionError()!" [traceId]="actionTraceId()" /> }
      @if (error()) { <erp-api-error-display data-testid="fi-credit-error" [messageKey]="error()!" [traceId]="traceId()" />
        <button nz-button type="button" data-testid="fi-credit-retry" (click)="load()">{{ 'fi.creditLimit.retry' | translate }}</button> }
      <nz-table #table data-testid="fi-credit-table" nzSize="middle" [nzData]="rows()" [nzLoading]="loading()" [nzFrontPagination]="false"
        [nzTotal]="total()" [nzPageIndex]="page()" [nzPageSize]="pageSize()" nzShowSizeChanger [nzPageSizeOptions]="pageSizeOptions"
        [nzNoResult]="(error() ? 'fi.creditLimit.loadFailed' : filtered() ? 'fi.creditLimit.noResults' : 'fi.creditLimit.empty') | translate"
        (nzQueryParams)="onQuery($event)">
        <thead><tr>
          <th nzColumnKey="customerId" [nzSortFn]="true" [nzSortOrder]="sortOrderOf(sort(), 'customerId')">{{ 'fi.creditLimit.customer' | translate }}</th>
          <th nzColumnKey="effectiveFrom" [nzSortFn]="true" [nzSortOrder]="sortOrderOf(sort(), 'effectiveFrom')">{{ 'fi.creditLimit.effectiveFrom' | translate }}</th>
          <th nzColumnKey="effectiveTo" [nzSortFn]="true" [nzSortOrder]="sortOrderOf(sort(), 'effectiveTo')">{{ 'fi.creditLimit.effectiveTo' | translate }}</th>
          <th nzColumnKey="amount" [nzSortFn]="true" [nzSortOrder]="sortOrderOf(sort(), 'amount')" class="number">{{ 'fi.creditLimit.amount' | translate }}</th>
          <th>{{ 'fi.creditLimit.status' | translate }}</th><th>{{ 'fi.creditLimit.version' | translate }}</th>
          <th>{{ 'fi.creditLimit.actions' | translate }}</th></tr></thead>
        <tbody>@for (row of table.data; track row.id) { <tr [attr.data-testid]="'fi-credit-row-' + row.id">
          <td [attr.data-testid]="'fi-credit-customer-' + row.id">{{ customerText(row.customerId) }}</td>
          <td>{{ row.effectiveFrom }}</td><td>{{ row.effectiveTo }}</td>
          <td class="number" [attr.data-testid]="'fi-credit-amount-' + row.id">{{ amount(row.amountText) }} {{ row.baseCurrency }}</td>
          <td><nz-tag [nzColor]="row.status === 'Active' ? 'success' : 'default'" [attr.data-testid]="'fi-credit-state-' + row.id">{{ statusKey(row.status) | translate }}</nz-tag></td>
          <td>{{ row.version }}</td>
          <td class="actions">@if (row.status === 'Active') {
              <button nz-button nzType="link" type="button" *erpHasPermission="'AR.CREDIT.EDIT'" [attr.data-testid]="'fi-credit-edit-' + row.id"
                (click)="openEdit(row)">{{ 'fi.creditLimit.edit' | translate }}</button>
              <button nz-button nzType="link" nzDanger type="button" *erpHasPermission="'AR.CREDIT.DEACTIVATE'" [disabled]="deactivating()"
                [attr.data-testid]="'fi-credit-deactivate-' + row.id" (click)="confirmDeactivate(row)">{{ 'fi.creditLimit.deactivate' | translate }}</button>
            } @else { <span class="muted" [attr.data-testid]="'fi-credit-history-' + row.id">{{ 'fi.creditLimit.historyOnly' | translate }}</span> }</td>
        </tr> }</tbody>
      </nz-table>
    </section>
    <nz-modal [nzVisible]="dialogOpen()" [nzTitle]="(editing() ? 'fi.creditLimit.editTitle' : 'fi.creditLimit.createTitle') | translate"
      [nzOkText]="'fi.creditLimit.save' | translate" [nzCancelText]="'fi.creditLimit.cancel' | translate" [nzOkLoading]="saving()"
      (nzOnOk)="save()" (nzOnCancel)="requestCloseDialog()">
      <ng-container *nzModalContent>
        @if (saveError()) { <erp-api-error-display data-testid="fi-credit-save-error" [messageKey]="saveError()!" [traceId]="saveTraceId()" /> }
        <form class="credit-form" [formGroup]="form" (ngSubmit)="save()" data-testid="fi-credit-form" novalidate>
          <span id="fi-credit-form-customer">{{ 'fi.creditLimit.customer' | translate }} <span class="required" aria-hidden="true" data-testid="fi-credit-required-customer">*</span></span>
          <div class="picker" role="group" aria-labelledby="fi-credit-form-customer">
            <span data-testid="fi-credit-form-customer">{{ form.controls.customerId.value ? customerText(form.controls.customerId.value) : ('fi.creditLimit.noCustomer' | translate) }}</span>
            @if (!editing()) { <button nz-button type="button" aria-required="true" data-testid="fi-credit-form-customer-pick" (click)="pickFormCustomer()">{{ 'fi.creditLimit.chooseCustomer' | translate }}</button> }
          </div>
          @if (editing()) { <p class="muted" data-testid="fi-credit-customer-locked">{{ 'fi.creditLimit.customerLocked' | translate }}</p> }
          @if (invalid('customerId')) { <p class="field-error" role="alert" data-testid="fi-credit-customer-error">{{ 'fi.creditLimit.customerRequired' | translate }}</p> }
          <label for="fi-credit-effective-from">{{ 'fi.creditLimit.effectiveFrom' | translate }} <span class="required" aria-hidden="true" data-testid="fi-credit-required-from">*</span></label>
          <input nz-input id="fi-credit-effective-from" type="date" name="effectiveFrom" autocomplete="off" formControlName="effectiveFrom" data-testid="fi-credit-effective-from"
            required [attr.aria-invalid]="invalid('effectiveFrom')" />
          @if (invalid('effectiveFrom')) { <p class="field-error" role="alert" data-testid="fi-credit-from-error">{{ 'fi.creditLimit.dateRequired' | translate }}</p> }
          <label for="fi-credit-effective-to">{{ 'fi.creditLimit.effectiveTo' | translate }} <span class="required" aria-hidden="true" data-testid="fi-credit-required-to">*</span></label>
          <input nz-input id="fi-credit-effective-to" type="date" name="effectiveTo" autocomplete="off" formControlName="effectiveTo" data-testid="fi-credit-effective-to"
            required [attr.aria-invalid]="invalid('effectiveTo')" />
          @if (invalid('effectiveTo')) { <p class="field-error" role="alert" data-testid="fi-credit-to-error">{{ 'fi.creditLimit.dateOrder' | translate }}</p> }
          <label for="fi-credit-amount">{{ 'fi.creditLimit.amountWithCurrency' | translate }} <span class="required" aria-hidden="true" data-testid="fi-credit-required-amount">*</span></label>
          <input nz-input id="fi-credit-amount" name="amount" inputmode="decimal" autocomplete="off" spellcheck="false" formControlName="amount" maxlength="20"
            required data-testid="fi-credit-amount" [attr.aria-invalid]="invalid('amount')" />
          <p class="muted">{{ 'fi.creditLimit.amountHint' | translate }}</p>
          @if (invalid('amount')) { <p class="field-error" role="alert" data-testid="fi-credit-amount-error">{{ 'fi.creditLimit.amountInvalid' | translate }}</p> }
        </form>
      </ng-container>
    </nz-modal>`,
  styles: `.live{display:contents}.credit-page{display:grid;gap:16px;color:var(--erp-color-text);min-width:0}h1{margin:0}.credit-head{display:flex;justify-content:space-between;gap:12px;flex-wrap:wrap;align-items:center}.hint,.muted{margin:0;color:var(--erp-color-text-secondary)}.filters{display:flex;gap:12px;flex-wrap:wrap;align-items:end}.field{display:grid;gap:4px}.picker{display:flex;gap:8px;align-items:center;flex-wrap:wrap;min-height:32px}.picker span{overflow-wrap:anywhere}.filters input,.filters select{min-height:32px;padding:4px;background:var(--erp-color-bg-container);color:var(--erp-color-text);border:1px solid var(--erp-color-border);border-radius:4px}.filter-actions{display:flex;gap:8px}.number{text-align:right!important;font-variant-numeric:tabular-nums;white-space:nowrap}.actions{white-space:nowrap}.credit-form{display:grid;gap:8px}.required,.field-error{color:var(--erp-status-error,#cf1322)}.field-error{margin:0}button:focus-visible,input:focus-visible,select:focus-visible{outline:2px solid var(--erp-color-primary);outline-offset:2px}`,
})
export class CreditLimitPage implements HasUnsavedChanges {
  private readonly api = inject(CreditLimitService);
  private readonly dialogs = inject(ErpDialogService);
  private readonly company = inject(CompanyContextService);
  private readonly translate = inject(TranslateService);
  private readonly changeDetector = inject(ChangeDetectorRef);
  private readonly route = inject(ActivatedRoute, { optional: true });
  private readonly router = inject(Router, { optional: true });
  readonly statuses = CREDIT_LIMIT_STATUSES;
  readonly pageSizeOptions = [...PAGE_SIZE_OPTIONS];
  readonly sortOrderOf = sortOrderOf;
  readonly rows = signal<CreditLimitSummary[]>([]);
  readonly total = signal(0);
  readonly page = signal(1);
  readonly pageSize = signal(20);
  readonly sort = signal(DEFAULT_CREDIT_LIMIT_SORT);
  readonly loading = signal(false);
  readonly filtered = signal(false);
  readonly validation = signal<string | null>(null);
  readonly error = signal<string | null>(null);
  readonly traceId = signal<string | null>(null);
  readonly notice = signal<{ key: string; params: Record<string, string> } | null>(null);
  readonly actionError = signal<string | null>(null);
  readonly actionTraceId = signal<string | null>(null);
  readonly filterCustomer = signal<CustomerLabel | null>(null);
  /** 客戶代碼／名稱：以伺服器回應的 MD 標籤為準，MD 選擇器的選取結果為後備（ADR-076、ADR-077）。 */
  readonly customers = signal<ReadonlyMap<number, CustomerLabel>>(new Map());
  readonly serverCustomers = signal<ReadonlyMap<number, CustomerLabel>>(new Map());
  readonly dialogOpen = signal(false);
  readonly editing = signal<CreditLimitSummary | null>(null);
  readonly saving = signal(false);
  readonly saveError = signal<string | null>(null);
  readonly saveTraceId = signal<string | null>(null);
  readonly serverFields = signal<Partial<Record<FormField, true>>>({});
  readonly deactivating = signal(false);
  status = ''; from = ''; to = '';
  readonly form = new FormGroup({
    customerId: new FormControl<number | null>(null, { validators: [Validators.required] }),
    effectiveFrom: new FormControl('', { nonNullable: true, validators: [Validators.required] }),
    effectiveTo: new FormControl('', { nonNullable: true, validators: [Validators.required] }),
    amount: new FormControl('', { nonNullable: true, validators: [Validators.required, Validators.pattern(CREDIT_AMOUNT_PATTERN)] }),
  });
  private applied: { customerId?: number; status?: CreditLimitStatus; from?: string; to?: string } = {};
  private listEpoch = 0;
  private saveEpoch = 0;
  private dialogEpoch = 0;
  private first = true;

  constructor() {
    const params = this.route?.snapshot.queryParamMap;
    if (params) {
      const customerId = Number(params.get('customerId'));
      if (Number.isSafeInteger(customerId) && customerId > 0) this.filterCustomer.set({ id: customerId, code: '', name: '' });
      const state = params.get('status') ?? '';
      this.status = CREDIT_LIMIT_STATUSES.includes(state as CreditLimitStatus) ? state : '';
      this.from = validDate(params.get('from') ?? '') ? params.get('from')! : '';
      this.to = validDate(params.get('to') ?? '') ? params.get('to')! : '';
      const page = Number(params.get('page'));
      if (Number.isSafeInteger(page) && page > 0 && page <= 100_000) this.page.set(page);
      const pageSize = Number(params.get('pageSize'));
      if (PAGE_SIZE_OPTIONS.some(value => value === pageSize)) this.pageSize.set(pageSize);
      const sort = params.get('sort') ?? '';
      if ((CREDIT_LIMIT_SORTS as readonly string[]).includes(sort.replace(/^-/, ''))) this.sort.set(sort);
    }
    inject(DestroyRef).onDestroy(() => { this.listEpoch++; this.saveEpoch++; });
    this.form.valueChanges.subscribe(() => { if (Object.keys(this.serverFields()).length) this.serverFields.set({}); });
    effect(() => { this.company.current(); untracked(() => {
      this.listEpoch++; this.saveEpoch++; this.closeDialog(); this.notice.set(null); this.actionError.set(null);
      if (!this.first) { this.page.set(1); this.filterCustomer.set(null); this.customers.set(new Map()); this.serverCustomers.set(new Map()); }
      this.first = false; this.search(false);
    }); });
  }

  statusKey(status: string): string { return `fi.creditLimit.status${status}`; }
  amount(text: string): string { return formatCreditAmount(text, this.translate.getCurrentLang() || undefined); }
  customerText(id: number): string {
    const known = this.serverCustomers().get(id) ?? this.customers().get(id);
    return known ? `${known.code} · ${known.name}` : this.translate.instant('fi.creditLimit.customerId', { id });
  }

  search(resetPage = true): void {
    if ((this.from && !validDate(this.from)) || (this.to && !validDate(this.to)) || (this.from && this.to && this.from > this.to)) {
      this.validation.set('fi.creditLimit.invalidWindow'); return;
    }
    this.validation.set(null);
    this.applied = { customerId: this.filterCustomer()?.id, status: (this.status || undefined) as CreditLimitStatus | undefined,
      from: this.from || undefined, to: this.to || undefined };
    this.filtered.set(Object.values(this.applied).some(value => value !== undefined));
    if (resetPage) this.page.set(1);
    this.updateUrl(); this.load();
  }

  reset(): void { this.filterCustomer.set(null); this.status = ''; this.from = ''; this.to = ''; this.search(); }

  onQuery(params: NzTableQueryParams): void {
    const next = nextTableState(params, { page: this.page(), pageSize: this.pageSize(), sort: this.sort() },
      [...CREDIT_LIMIT_SORTS], DEFAULT_CREDIT_LIMIT_SORT);
    if (!next) return;
    this.page.set(next.page); this.pageSize.set(next.pageSize); this.sort.set(next.sort);
    this.updateUrl(); this.load();
  }

  load(): void {
    const epoch = ++this.listEpoch;
    this.loading.set(true); this.error.set(null); this.traceId.set(null);
    this.api.list({ ...this.applied, page: this.page(), pageSize: this.pageSize(), sort: this.sort() }).subscribe({
      next: result => { if (epoch !== this.listEpoch) return;
        this.remember(result.items); this.rows.set(result.items); this.total.set(result.totalCount); this.loading.set(false); },
      error: (failure: unknown) => { if (epoch !== this.listEpoch) return;
        this.rows.set([]); this.total.set(0);
        this.error.set(this.api.errorKey(failure)); this.traceId.set(apiErrorTraceId(failure)); this.loading.set(false); },
    });
  }

  pickFilterCustomer(): void {
    this.chooseCustomer(customer => this.filterCustomer.set(customer));
  }

  pickFormCustomer(): void {
    const epoch = this.dialogEpoch;
    this.chooseCustomer(customer => { if (epoch !== this.dialogEpoch || this.editing()) return;
      this.form.controls.customerId.setValue(customer.id); this.form.controls.customerId.markAsTouched();
      this.changeDetector.markForCheck(); });
  }

  openCreate(): void {
    this.dialogEpoch++; this.editing.set(null);
    this.form.reset({ customerId: this.filterCustomer()?.id ?? null, effectiveFrom: '', effectiveTo: '', amount: '' });
    this.openDialog();
  }

  openEdit(row: CreditLimitSummary): void {
    this.dialogEpoch++; this.editing.set(row);
    this.form.reset({ customerId: row.customerId, effectiveFrom: row.effectiveFrom, effectiveTo: row.effectiveTo, amount: row.amountText });
    this.openDialog();
  }

  closeDialog(): void {
    this.dialogEpoch++; this.saveEpoch++; this.dialogOpen.set(false); this.saving.set(false);
    this.saveError.set(null); this.saveTraceId.set(null); this.serverFields.set({});
  }

  hasUnsavedChanges(): boolean { return this.dialogOpen() && this.form.dirty; }

  requestCloseDialog(): void {
    if (this.saving()) return;
    if (!this.hasUnsavedChanges()) { this.closeDialog(); return; }
    this.dialogs.confirm({ titleKey: 'common.document.leaveTitle', contentKey: 'common.document.leaveContent',
      okKey: 'common.document.leave', cancelKey: 'common.document.stay', danger: true }).subscribe(leave => {
      if (leave) this.closeDialog();
    });
  }

  @HostListener('window:beforeunload', ['$event'])
  beforeUnload(event: BeforeUnloadEvent): void {
    if (this.hasUnsavedChanges()) { event.preventDefault(); event.returnValue = ''; }
  }

  invalid(field: FormField): boolean {
    const control = this.form.controls[field];
    if (this.serverFields()[field]) return true;
    if (field === 'effectiveTo') return control.touched && (control.invalid || this.reversed());
    if (field === 'effectiveFrom') {
      const value = this.form.controls.effectiveFrom.value;
      return control.touched && (control.invalid || (!!value && !validDate(value)));
    }
    return control.touched && control.invalid;
  }

  save(): void {
    if (this.saving()) return;
    this.form.markAllAsTouched();
    const value = this.form.getRawValue();
    if (this.form.invalid || !validDate(value.effectiveFrom) || !validDate(value.effectiveTo) || this.reversed()) {
      this.focusFirstError(); return;
    }
    const draft: CreditLimitDraft = { customerId: value.customerId!, effectiveFrom: value.effectiveFrom,
      effectiveTo: value.effectiveTo, amount: value.amount };
    const row = this.editing();
    const epoch = ++this.saveEpoch;
    this.saving.set(true); this.saveError.set(null); this.saveTraceId.set(null); this.serverFields.set({});
    (row ? this.api.update(row.id, row.version, draft) : this.api.create(draft)).subscribe({
      next: saved => { if (epoch !== this.saveEpoch) return;
        this.saving.set(false); this.closeDialog();
        this.announce(row ? 'fi.creditLimit.updated' : 'fi.creditLimit.created', saved); this.load(); },
      error: (failure: unknown) => { if (epoch !== this.saveEpoch) return;
        this.saving.set(false);
        if (row && (isStaleRowError(failure) || isNotActive(failure))) {
          // The held version can never succeed again: close, explain above the list and show fresh saved data.
          this.closeDialog();
          this.actionError.set(this.api.errorKey(failure)); this.actionTraceId.set(apiErrorTraceId(failure));
          this.load(); return;
        }
        this.saveError.set(this.api.errorKey(failure)); this.saveTraceId.set(apiErrorTraceId(failure));
        this.serverFields.set(this.api.fieldErrors(failure)); },
    });
  }

  confirmDeactivate(row: CreditLimitSummary): void {
    if (this.deactivating()) return;
    this.dialogs.confirm({ titleKey: 'fi.creditLimit.deactivateConfirm', contentKey: 'fi.creditLimit.deactivateContent',
      params: { customer: this.customerText(row.customerId), from: row.effectiveFrom, to: row.effectiveTo },
      okKey: 'fi.creditLimit.deactivate', danger: true }).subscribe(ok => {
      if (!ok || this.deactivating()) return;
      this.deactivating.set(true); this.actionError.set(null); this.actionTraceId.set(null); this.notice.set(null);
      this.api.deactivate(row.id, row.version).subscribe({
        next: saved => { this.deactivating.set(false); this.announce('fi.creditLimit.deactivated', saved); this.load(); },
        error: (failure: unknown) => { this.deactivating.set(false);
          this.actionError.set(this.api.errorKey(failure)); this.actionTraceId.set(apiErrorTraceId(failure));
          if (isStaleRowError(failure) || isMissingRowError(failure) || isNotActive(failure)) this.load(); },
      });
    });
  }

  /** 驗證失敗時把焦點移到第一個錯誤欄位（客戶未選時移到選擇按鈕）。 */
  private focusFirstError(): void {
    setTimeout(() => {
      const form = typeof document === 'undefined' ? null : document.querySelector('[data-testid="fi-credit-form"]');
      const target = this.invalid('customerId')
        ? form?.querySelector<HTMLElement>('[data-testid="fi-credit-form-customer-pick"]')
        : form?.querySelector<HTMLElement>('[aria-invalid="true"]');
      target?.focus();
    });
  }

  private openDialog(): void {
    this.saveError.set(null); this.saveTraceId.set(null); this.serverFields.set({}); this.notice.set(null);
    this.actionError.set(null); this.dialogOpen.set(true);
  }

  private reversed(): boolean {
    const { effectiveFrom, effectiveTo } = this.form.getRawValue();
    return !!effectiveFrom && !!effectiveTo && (!validDate(effectiveTo) || effectiveTo < effectiveFrom);
  }

  /** 成功訊息只用伺服器回傳的保存值。 */
  private announce(key: string, saved: CreditLimitDetail): void {
    this.remember([saved]);
    this.notice.set({ key, params: { customer: this.customerText(saved.customerId), from: saved.effectiveFrom,
      to: saved.effectiveTo, amount: this.amount(saved.amountText), currency: saved.baseCurrency, version: String(saved.version) } });
  }

  /** 伺服器標籤優先；null 撤銷舊伺服器值，再退回選擇器快取或「客戶 #id」。 */
  private remember(rows: readonly CreditLimitSummary[]): void {
    if (!rows.length) return;
    this.serverCustomers.update(map => {
      const next = new Map(map);
      for (const row of rows) {
        if (row.customerCode && row.customerName != null)
          next.set(row.customerId, { id: row.customerId, code: row.customerCode, name: row.customerName });
        else next.delete(row.customerId);
      }
      return next;
    });
  }

  private chooseCustomer(apply: (customer: CustomerLabel) => void): void {
    this.dialogs.select<Customer>({ titleKey: 'fi.creditLimit.chooseCustomer',
      columns: [{ key: 'code', labelKey: 'fi.creditLimit.customerCode', value: row => row.code },
        { key: 'name', labelKey: 'fi.creditLimit.customerName', value: row => row.name }],
      load: query => this.api.customerOptions(query), rowKey: row => row.id,
      errorCodes: ['COMMON.PERMISSION.DENIED'] }).subscribe(rows => {
      const customer = rows?.[0];
      if (!customer) return;
      const label = { id: customer.id, code: customer.code, name: customer.name };
      this.customers.update(map => new Map(map).set(label.id, label));
      apply(label);
    });
  }

  private updateUrl(): void {
    if (!this.route || !this.router) return;
    void this.router.navigate([], { relativeTo: this.route, replaceUrl: true, queryParams: {
      customerId: this.applied.customerId ?? null, status: this.applied.status ?? null,
      from: this.applied.from ?? null, to: this.applied.to ?? null, page: this.page() > 1 ? this.page() : null,
      pageSize: this.pageSize() !== 20 ? this.pageSize() : null,
      sort: this.sort() !== DEFAULT_CREDIT_LIMIT_SORT ? this.sort() : null,
    } });
  }
}

function isNotActive(error: unknown): boolean {
  return (error as { error?: { errorCode?: unknown } } | null)?.error?.errorCode === 'FI.CREDIT.LIMIT_NOT_ACTIVE';
}
