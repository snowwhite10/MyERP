import { ChangeDetectionStrategy, Component, computed, effect, inject, signal, untracked } from '@angular/core';
import { FormControl, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { ActivatedRoute, ParamMap, Router } from '@angular/router';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { skip } from 'rxjs';
import { TranslatePipe } from '@ngx-translate/core';
import { NzAlertModule } from 'ng-zorro-antd/alert';
import { NzButtonModule } from 'ng-zorro-antd/button';
import { NzInputModule } from 'ng-zorro-antd/input';
import { NzModalModule } from 'ng-zorro-antd/modal';
import { NzTableModule, NzTableQueryParams } from 'ng-zorro-antd/table';
import { NzTagModule } from 'ng-zorro-antd/tag';
import { HasPermissionDirective } from '../../core/auth/has-permission.directive';
import { apiErrorTraceId } from '../../core/http/api-error';
import { CompanyContextService } from '../../core/company/company-context.service';
import { ErpDialogService } from '../../shared/dialogs/dialog.service';
import { latestRequest } from '../../shared/list-page/latest-request';
import { isStaleRowError } from '../../shared/list-page/stale-row';
import { ListPage } from '../../shared/list-page/list-page';
import { PAGE_SIZE_OPTIONS, nextTableState, sortOrderOf } from '../../shared/list-page/table-query';
import { DueType, PaymentTerm, PaymentTermService, validPaymentRule } from './payment-term.service';

/** 付款條件維護與已保存規則的到期日試算，介面 8.89–8.91。 */
@Component({
  selector: 'erp-payment-term-page',
  imports: [ReactiveFormsModule, TranslatePipe, NzAlertModule, NzButtonModule, NzInputModule,
    NzModalModule, NzTableModule, NzTagModule, HasPermissionDirective, ListPage],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <erp-list-page titleKey="md.paymentTerm.title" testId="md-payment-term" [resettable]="false"
      [errorKey]="loadError()" [errorTraceId]="loadTraceId()" (querySubmit)="search()" (retryLoad)="reload()">
      <button erpListActions nz-button nzType="primary" data-testid="md-payment-term-create" *erpHasPermission="'MD.PAYMENT_TERM.CREATE'"
        (click)="openCreate()">{{ 'common.action.add' | translate }}</button>
      <input erpListFilters nz-input type="search" name="keyword" autocomplete="off" [formControl]="searchControl" data-testid="md-payment-term-search"
        [attr.aria-label]="'md.paymentTerm.search' | translate" [placeholder]="'md.paymentTerm.search' | translate" (keyup.enter)="search()" />
      @if (error() && !loadError()) { <nz-alert nzType="error" nzShowIcon [nzMessage]="error()! | translate" /> }
      @if (!loadError()) {
      <nz-table #table data-testid="md-payment-term-table" nzSize="middle" [nzData]="rows()" [nzLoading]="loading()"
        [nzFrontPagination]="false" [nzTotal]="total()" [nzPageIndex]="page()" [nzPageSize]="pageSize()"
        nzShowSizeChanger [nzPageSizeOptions]="pageSizeOptions" [nzNoResult]="(appliedKeyword() ? 'md.paymentTerm.noResults' : 'md.paymentTerm.empty') | translate"
        (nzQueryParams)="onQueryParams($event)">
        <thead><tr><th nzColumnKey="code" [nzSortFn]="true" [nzSortOrder]="sortOrder('code')">{{ 'md.paymentTerm.code' | translate }}</th>
          <th nzColumnKey="name" [nzSortFn]="true" [nzSortOrder]="sortOrder('name')">{{ 'md.paymentTerm.name' | translate }}</th>
          <th>{{ 'md.paymentTerm.rule' | translate }}</th><th>{{ 'md.paymentTerm.status' | translate }}</th>
          <th>{{ 'md.paymentTerm.actions' | translate }}</th></tr></thead>
        <tbody>@for (row of table.data; track row.id) { <tr [attr.data-testid]="'md-payment-term-row-' + row.code">
          <td>{{ row.code }}</td><td>{{ row.name }}</td><td>
            @if (row.dueType === 'NetDays') { {{ 'md.paymentTerm.ruleNetDays' | translate:{ days: row.days } }} }
            @else if (row.dueType === 'ClosingDays') { {{ 'md.paymentTerm.ruleClosingDays' | translate:{ closingDay: row.closingDay, days: row.days } }} }
            @else { {{ 'md.paymentTerm.ruleClosingMonthDay' | translate:{ closingDay: row.closingDay, months: row.months, paymentDay: row.paymentDay } }} }
          </td>
          <td><nz-tag [nzColor]="row.isActive ? 'success' : 'default'">{{ (row.isActive ? 'md.paymentTerm.active' : 'md.paymentTerm.inactive') | translate }}</nz-tag></td>
          <td><button nz-button nzType="link" [attr.data-testid]="'md-payment-term-calc-' + row.code"
              (click)="openCalc(row)">{{ 'md.paymentTerm.calculate' | translate }}</button>
            <button nz-button nzType="link" *erpHasPermission="'MD.PAYMENT_TERM.EDIT'" (click)="openEdit(row)">{{ 'common.action.edit' | translate }}</button>
            <button nz-button nzType="link" nzDanger *erpHasPermission="'MD.PAYMENT_TERM.DELETE'" [disabled]="deleting()"
              [attr.data-testid]="'md-payment-term-delete-' + row.code" (click)="confirmDelete(row)">{{ 'common.action.delete' | translate }}</button></td>
        </tr>}</tbody>
      </nz-table>
      }
    </erp-list-page>
    <nz-modal [nzVisible]="dialogOpen()" [nzTitle]="'md.paymentTerm.formTitle' | translate"
      [nzOkText]="'common.action.save' | translate" [nzCancelText]="'common.action.cancel' | translate"
      [nzOkLoading]="saving()" (nzOnOk)="save()" (nzOnCancel)="closeDialog()">
      <ng-container *nzModalContent>
        @if (saveError()) { <nz-alert nzType="error" nzShowIcon [nzMessage]="saveError()! | translate" /> }
        <form class="term-form" [formGroup]="form" (ngSubmit)="save()" data-testid="md-payment-term-form">
          <label for="term-code">{{ 'md.paymentTerm.code' | translate }}</label>
          <input nz-input id="term-code" formControlName="code" maxlength="30" data-testid="md-payment-term-code" />
          @if (form.controls.code.touched && form.controls.code.invalid) { <p role="alert">{{ 'md.paymentTerm.invalidCode' | translate }}</p> }
          <label for="term-name">{{ 'md.paymentTerm.name' | translate }}</label>
          <input nz-input id="term-name" formControlName="name" maxlength="200" data-testid="md-payment-term-name" />
          @if (form.controls.name.touched && form.controls.name.invalid) { <p role="alert">{{ 'md.paymentTerm.invalidName' | translate }}</p> }
          <label for="term-type">{{ 'md.paymentTerm.dueType' | translate }}</label>
          <select id="term-type" formControlName="dueType" data-testid="md-payment-term-type">
            @for (type of dueTypes; track type) { <option [value]="type">{{ 'md.paymentTerm.type' + type | translate }}</option> }
          </select>
          @if (form.controls.dueType.value !== 'ClosingMonthDay') {
            <label for="term-days">{{ 'md.paymentTerm.days' | translate }}</label>
            <input nz-input id="term-days" type="number" min="0" max="365" step="1" formControlName="days" data-testid="md-payment-term-days" />
          }
          @if (form.controls.dueType.value !== 'NetDays') {
            <label for="term-closing">{{ 'md.paymentTerm.closingDay' | translate }}</label>
            <input nz-input id="term-closing" type="number" min="1" max="31" step="1" formControlName="closingDay" data-testid="md-payment-term-closing-day" />
          }
          @if (form.controls.dueType.value === 'ClosingMonthDay') {
            <label for="term-months">{{ 'md.paymentTerm.months' | translate }}</label>
            <input nz-input id="term-months" type="number" min="0" max="12" step="1" formControlName="months" data-testid="md-payment-term-months" />
            <label for="term-payment">{{ 'md.paymentTerm.paymentDay' | translate }}</label>
            <input nz-input id="term-payment" type="number" min="1" max="31" step="1" formControlName="paymentDay" data-testid="md-payment-term-payment-day" />
          }
          @if (ruleInvalid()) { <p role="alert">{{ 'md.paymentTerm.invalidRule' | translate }}</p> }
          @if (editing()) { <label><input type="checkbox" formControlName="isActive" />{{ 'md.paymentTerm.active' | translate }}</label> }
        </form>
      </ng-container>
    </nz-modal>
    <nz-modal [nzVisible]="calcRow() !== null" [nzTitle]="'md.paymentTerm.calcTitle' | translate"
      [nzOkText]="'md.paymentTerm.calculate' | translate" [nzCancelText]="'common.action.cancel' | translate"
      (nzOnOk)="calculate()" (nzOnCancel)="closeCalc()">
      <ng-container *nzModalContent><div class="term-form">
        <p>{{ calcRow()?.code }} — {{ calcRow()?.name }}</p>
        <label for="term-date">{{ 'md.paymentTerm.documentDate' | translate }}</label>
        <input nz-input id="term-date" type="date" [formControl]="documentDate" data-testid="md-payment-term-date" />
        @if (calcError()) { <nz-alert nzType="error" nzShowIcon [nzMessage]="calcError()! | translate" /> }
        @if (dueDate()) { <p data-testid="md-payment-term-due-date">{{ 'md.paymentTerm.dueDateValue' | translate: { date: dueDate() } }}</p> }
      </div></ng-container>
    </nz-modal>
  `,
  styles: `input[erpListFilters]{max-width:320px}.term-form{display:grid;gap:8px}.term-form p[role=alert]{color:var(--erp-status-error);margin:0}`,
})
export class PaymentTermPage {
  readonly pageSizeOptions = [...PAGE_SIZE_OPTIONS];
  readonly dueTypes: readonly DueType[] = ['NetDays', 'ClosingDays', 'ClosingMonthDay'];
  private readonly api = inject(PaymentTermService);
  private readonly dialogs = inject(ErpDialogService);
  private readonly company = inject(CompanyContextService);
  private readonly route = inject(ActivatedRoute, { optional: true });
  private readonly router = inject(Router, { optional: true });
  private readonly listRequest = latestRequest();
  private readonly calcRequest = latestRequest();
  private readonly companyId = computed(() => this.company.current()?.id ?? null);
  readonly rows = signal<PaymentTerm[]>([]);
  readonly total = signal(0);
  readonly page = signal(1);
  readonly pageSize = signal(20);
  readonly sort = signal('code');
  readonly loading = signal(false);
  readonly error = signal<string | null>(null);
  readonly loadError = signal<string | null>(null);
  readonly loadTraceId = signal<string | null>(null);
  readonly appliedKeyword = signal('');
  readonly dialogOpen = signal(false);
  readonly editing = signal<PaymentTerm | null>(null);
  readonly saving = signal(false);
  /** 刪除進行中：避免第二次確認帶舊版本而得到 409（MD-F05）。 */
  readonly deleting = signal(false);
  readonly saveError = signal<string | null>(null);
  readonly ruleInvalid = signal(false);
  readonly calcRow = signal<PaymentTerm | null>(null);
  readonly calcError = signal<string | null>(null);
  readonly dueDate = signal<string | null>(null);
  readonly searchControl = new FormControl('', { nonNullable: true });
  readonly documentDate = new FormControl('', { nonNullable: true });
  readonly form = new FormGroup({
    code: new FormControl('', { nonNullable: true, validators: [Validators.required, Validators.pattern(/^[A-Za-z0-9_-]{1,30}$/)] }),
    name: new FormControl('', { nonNullable: true, validators: [Validators.required, Validators.pattern(/.*\S.*/), Validators.maxLength(200)] }),
    dueType: new FormControl<DueType>('NetDays', { nonNullable: true }),
    days: new FormControl<number | null>(0), closingDay: new FormControl<number | null>(31),
    months: new FormControl<number | null>(1), paymentDay: new FormControl<number | null>(31),
    isActive: new FormControl(true, { nonNullable: true }),
  });
  constructor() {
    if (this.route && this.router) {
      this.applyUrl(this.route.snapshot.queryParamMap);
      this.syncUrl(true);
      this.route.queryParamMap.pipe(skip(1), takeUntilDestroyed()).subscribe(params => {
        if (this.applyUrl(params)) this.reload();
        this.syncUrl(true);
      });
    }
    let companyInitialized = false;
    effect(() => { this.companyId(); untracked(() => {
      this.closeDialog(); this.closeCalc();
      if (companyInitialized) { this.page.set(1); this.syncUrl(true); }
      companyInitialized = true; this.reload();
    }); });
    this.form.controls.dueType.valueChanges.subscribe(() => this.ruleInvalid.set(false));
    this.documentDate.valueChanges.subscribe(() => { this.calcRequest.cancel(); this.dueDate.set(null); this.calcError.set(null); });
  }
  private applyUrl(params: ParamMap): boolean {
    const positiveInt = (value: string | null, fallback: number): number =>
      value && /^[1-9]\d*$/.test(value) && Number.isSafeInteger(Number(value)) ? Number(value) : fallback;
    const keyword = (params.get('keyword') ?? '').trim();
    const size = positiveInt(params.get('pageSize'), 20);
    const pageSize = PAGE_SIZE_OPTIONS.some(option => option === size) ? size : 20;
    const requestedPage = positiveInt(params.get('page'), 1);
    const page = (requestedPage - 1) * pageSize <= 2_147_483_647 ? requestedPage : 1;
    const requestedSort = params.get('sort') ?? 'code';
    const sort = ['code', '-code', 'name', '-name'].includes(requestedSort) ? requestedSort : 'code';
    const changed = keyword !== this.appliedKeyword() || page !== this.page() || pageSize !== this.pageSize() || sort !== this.sort();
    this.searchControl.setValue(keyword, { emitEvent: false });
    this.appliedKeyword.set(keyword); this.page.set(page); this.pageSize.set(pageSize); this.sort.set(sort);
    return changed;
  }
  private syncUrl(replaceUrl = false): void {
    if (!this.route || !this.router) return;
    const queryParams = { keyword: this.appliedKeyword() || null, page: this.page() === 1 ? null : this.page(),
      pageSize: this.pageSize() === 20 ? null : this.pageSize(), sort: this.sort() === 'code' ? null : this.sort(),
      filter: null };
    const current = this.route.snapshot.queryParamMap;
    if (Object.entries(queryParams).every(([key, value]) => current.get(key) === (value === null ? null : String(value)))) return;
    void this.router.navigate([], { relativeTo: this.route, queryParams, queryParamsHandling: 'merge', replaceUrl });
  }
  sortOrder(field: 'code' | 'name'): 'ascend' | 'descend' | null {
    return sortOrderOf(this.sort(), field);
  }
  search(): void { this.appliedKeyword.set(this.searchControl.value.trim()); this.page.set(1); this.syncUrl(); this.reload(); }
  onQueryParams(params: NzTableQueryParams): void {
    const next = nextTableState(params, { page: this.page(), pageSize: this.pageSize(), sort: this.sort() }, ['code', 'name'], 'code');
    if (!next) return;
    this.page.set(next.page); this.pageSize.set(next.pageSize); this.sort.set(next.sort); this.syncUrl(); this.reload();
  }
  reload(): void {
    this.loading.set(true); this.error.set(null); this.loadError.set(null); this.loadTraceId.set(null);
    this.listRequest.run(this.api.list({ page: this.page(), pageSize: this.pageSize(), keyword: this.appliedKeyword(), sort: this.sort() }), {
      next: (result) => { this.rows.set(result.items); this.total.set(result.totalCount); this.loading.set(false); },
      error: (error: unknown) => { this.rows.set([]); this.total.set(0); this.loadError.set(this.api.errorKey(error)); this.loadTraceId.set(apiErrorTraceId(error)); this.loading.set(false); },
    });
  }
  openCreate(): void {
    this.editing.set(null); this.form.reset({ code: '', name: '', dueType: 'NetDays', days: 0,
      closingDay: 31, months: 1, paymentDay: 31, isActive: true });
    this.form.controls.code.enable(); this.ruleInvalid.set(false); this.saveError.set(null); this.dialogOpen.set(true);
  }
  openEdit(row: PaymentTerm): void {
    this.editing.set(row); this.form.reset({ code: row.code, name: row.name, dueType: row.dueType,
      days: row.days, closingDay: row.closingDay, months: row.months, paymentDay: row.paymentDay, isActive: row.isActive });
    this.form.controls.code.disable(); this.ruleInvalid.set(false); this.saveError.set(null); this.dialogOpen.set(true);
  }
  closeDialog(): void { this.dialogOpen.set(false); this.saveError.set(null); }
  save(): void {
    if (this.saving()) return;
    this.form.markAllAsTouched(); if (this.form.invalid) return;
    const draft = this.form.getRawValue();
    if (!validPaymentRule(draft)) { this.ruleInvalid.set(true); return; }
    this.ruleInvalid.set(false); const row = this.editing();
    const request = row ? this.api.update(row.id, { ...draft, version: row.version }) : this.api.create(draft);
    this.saving.set(true); this.saveError.set(null);
    request.subscribe({
      next: () => { this.saving.set(false); this.closeDialog(); this.reload(); },
      error: (error: unknown) => { this.saving.set(false); this.saveError.set(this.api.errorKey(error)); if (isStaleRowError(error)) this.reload(); },
    });
  }
  confirmDelete(row: PaymentTerm): void {
    if (this.deleting()) return;
    this.dialogs.confirm({ titleKey: 'md.paymentTerm.deleteConfirm', contentKey: 'common.confirm.irreversible',
      params: { code: row.code }, okKey: 'common.action.delete', danger: true }).subscribe((ok) => {
      if (!ok || this.deleting()) return;
      this.deleting.set(true); this.error.set(null);
      this.api.remove(row.id, row.version).subscribe({ next: () => { this.deleting.set(false); this.reload(); },
        error: (error: unknown) => { this.deleting.set(false); if (isStaleRowError(error)) this.reload(); this.error.set(this.api.errorKey(error)); } });
    });
  }
  openCalc(row: PaymentTerm): void { this.calcRow.set(row); this.documentDate.setValue(''); this.dueDate.set(null); this.calcError.set(null); }
  closeCalc(): void { this.calcRequest.cancel(); this.calcRow.set(null); this.dueDate.set(null); this.calcError.set(null); }
  calculate(): void {
    const row = this.calcRow(); const date = this.documentDate.value;
    if (!row || !/^\d{4}-\d{2}-\d{2}$/.test(date)) { this.calcError.set('md.paymentTerm.invalidDate'); return; }
    this.calcError.set(null); this.dueDate.set(null);
    this.calcRequest.run(this.api.dueDate(row.code, date), {
      next: (result) => this.dueDate.set(result.dueDate),
      error: (error: unknown) => this.calcError.set(this.api.errorKey(error)),
    });
  }
}
