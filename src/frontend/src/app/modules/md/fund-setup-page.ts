import { ChangeDetectionStrategy, Component, computed, effect, inject, InjectionToken, signal, untracked } from '@angular/core';
import { FormControl, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { TranslatePipe } from '@ngx-translate/core';
import { NzAlertModule } from 'ng-zorro-antd/alert';
import { NzButtonModule } from 'ng-zorro-antd/button';
import { NzInputModule } from 'ng-zorro-antd/input';
import { NzModalModule } from 'ng-zorro-antd/modal';
import { NzTableModule, NzTableQueryParams } from 'ng-zorro-antd/table';
import { NzTagModule } from 'ng-zorro-antd/tag';
import { expand, reduce, Observable, EMPTY } from 'rxjs';
import { HasPermissionDirective } from '../../core/auth/has-permission.directive';
import { PermissionService } from '../../core/auth/permission.service';
import { CompanyContextService } from '../../core/company/company-context.service';
import { ErpDialogService } from '../../shared/dialogs/dialog.service';
import { latestRequest } from '../../shared/list-page/latest-request';
import { PAGE_SIZE_OPTIONS, PagedResult } from '../../shared/list-page/table-query';
import { Account, AccountService } from './account.service';
import { Currency, CurrencyService } from './currency.service';
import { eligibleAccounts, FundAccount, FundAccountService, FundDraft, FundQuery, FundType,
  IncomeExpenseCategory, IncomeExpenseCategoryService, IncomeExpenseDirection, IncomeExpenseDraft } from './fund-setup.service';

type Row = FundAccount | IncomeExpenseCategory;
export const FUND_SETUP_KIND = new InjectionToken<'fund' | 'category'>('FUND_SETUP_KIND');

/** 介面 8.20–8.22、8.42–8.44：兩種設定共用列表及表單，依用途限制參照科目。 */
@Component({
  selector: 'erp-fund-setup-page',
  imports: [ReactiveFormsModule, TranslatePipe, NzAlertModule, NzButtonModule, NzInputModule,
    NzModalModule, NzTableModule, NzTagModule, HasPermissionDirective],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <section class="fund-setup">
      <header><h1 data-testid="md-fund-title">{{ key('title') | translate }}</h1>
        <button nz-button nzType="primary" *erpHasPermission="permission + '.CREATE'" data-testid="md-fund-create"
          (click)="openCreate()">{{ 'common.action.add' | translate }}</button></header>
      <div class="filters"><input nz-input type="search" [formControl]="searchControl" data-testid="md-fund-search"
          [placeholder]="key('search') | translate" (keyup.enter)="search()" />
        <select [formControl]="filterControl" data-testid="md-fund-filter" (change)="search()">
          <option value="">{{ key('allTypes') | translate }}</option>
          @for (type of types; track type) { <option [value]="type">{{ key('type' + type) | translate }}</option> }
        </select><button nz-button (click)="search()">{{ 'common.action.search' | translate }}</button></div>
      @if (error()) { <nz-alert nzType="error" nzShowIcon [nzMessage]="error()! | translate" /> }
      <nz-table #table data-testid="md-fund-table" nzSize="middle" [nzData]="rows()" [nzLoading]="loading()"
        [nzFrontPagination]="false" [nzTotal]="total()" [nzPageIndex]="page()" [nzPageSize]="pageSize()"
        nzShowSizeChanger [nzPageSizeOptions]="pageSizeOptions" [nzNoResult]="key('empty') | translate"
        (nzQueryParams)="onQueryParams($event)">
        <thead><tr><th nzColumnKey="code" [nzSortFn]="true" [nzSortOrder]="sortOrder('code')">{{ key('code') | translate }}</th>
          <th nzColumnKey="name" [nzSortFn]="true" [nzSortOrder]="sortOrder('name')">{{ key('name') | translate }}</th>
          <th>{{ key('type') | translate }}</th>
          @if (kind === 'fund') { <th>{{ key('currency') | translate }}</th><th>{{ key('bankName') | translate }}</th> }
          <th>{{ key('ledgerAccount') | translate }}</th><th>{{ key('status') | translate }}</th><th>{{ key('actions') | translate }}</th></tr></thead>
        <tbody>@for (row of table.data; track row.id) { <tr [attr.data-testid]="'md-fund-row-' + row.code">
          <td>{{ row.code }}</td><td>{{ row.name }}</td><td>{{ key('type' + rowType(row)) | translate }}</td>
          @if (kind === 'fund') { <td>{{ currencyName(asFund(row).currencyId) }}</td><td>{{ asFund(row).bankName }}</td> }
          <td>{{ accountName(row.ledgerAccountId) }}</td>
          <td><nz-tag [nzColor]="row.isActive ? 'success' : 'default'">{{ (row.isActive ? key('active') : key('inactive')) | translate }}</nz-tag></td>
          <td><button nz-button nzType="link" *erpHasPermission="permission + '.EDIT'" (click)="openEdit(row)">{{ 'common.action.edit' | translate }}</button>
            <button nz-button nzType="link" nzDanger *erpHasPermission="permission + '.DELETE'" (click)="confirmDelete(row)">{{ 'common.action.delete' | translate }}</button></td>
        </tr>}</tbody>
      </nz-table>
    </section>
    <nz-modal [nzVisible]="dialogOpen()" [nzTitle]="key('formTitle') | translate"
      [nzOkText]="'common.action.save' | translate" [nzCancelText]="'common.action.cancel' | translate"
      [nzOkLoading]="saving()" (nzOnOk)="save()" (nzOnCancel)="closeDialog()">
      <ng-container *nzModalContent>
        @if (saveError()) { <nz-alert nzType="error" nzShowIcon [nzMessage]="saveError()! | translate" /> }
        <form class="fund-form" [formGroup]="form" (ngSubmit)="save()" data-testid="md-fund-form">
          <label for="fund-code">{{ key('code') | translate }}</label>
          <input nz-input id="fund-code" formControlName="code" maxlength="30" data-testid="md-fund-code" />
          @if (form.controls.code.touched && form.controls.code.invalid) { <p role="alert">{{ key('invalidCode') | translate }}</p> }
          <label for="fund-name">{{ key('name') | translate }}</label>
          <input nz-input id="fund-name" formControlName="name" maxlength="200" data-testid="md-fund-name" />
          @if (form.controls.name.touched && form.controls.name.invalid) { <p role="alert">{{ key('invalidName') | translate }}</p> }
          <label for="fund-type">{{ key('type') | translate }}</label>
          <select id="fund-type" formControlName="type" data-testid="md-fund-type">
            @for (type of types; track type) { <option [value]="type">{{ key('type' + type) | translate }}</option> }
          </select>
          @if (kind === 'fund') {
            <label for="fund-currency">{{ key('currency') | translate }}</label>
            <select id="fund-currency" formControlName="currencyId" data-testid="md-fund-currency">
              <option [ngValue]="null">{{ key('selectCurrency') | translate }}</option>
              @for (currency of currencyOptions(); track currency.id) { <option [ngValue]="currency.id">{{ currency.code }} — {{ currency.name }}</option> }
            </select>
            @if (form.controls.currencyId.touched && form.controls.currencyId.invalid) { <p role="alert">{{ key('invalidCurrency') | translate }}</p> }
            @if (form.controls.type.value === 'Bank') {
              <label for="fund-bank-name">{{ key('bankName') | translate }}</label>
              <input nz-input id="fund-bank-name" formControlName="bankName" maxlength="200" data-testid="md-fund-bank-name" />
              <label for="fund-bank-no">{{ key('bankAccountNo') | translate }}</label>
              <input nz-input id="fund-bank-no" formControlName="bankAccountNo" maxlength="50" data-testid="md-fund-bank-no" />
              @if (form.controls.bankAccountNo.touched && form.controls.bankAccountNo.invalid) { <p role="alert">{{ key('invalidBankAccountNo') | translate }}</p> }
            }
          }
          <label for="fund-ledger">{{ key('ledgerAccount') | translate }}</label>
          <select id="fund-ledger" formControlName="ledgerAccountId" data-testid="md-fund-ledger">
            <option [ngValue]="null">{{ key('noLedgerAccount') | translate }}</option>
            @for (account of ledgerOptions(); track account.id) { <option [ngValue]="account.id">{{ account.code }} — {{ account.name }}</option> }
          </select>
          @if (editing()) { <label><input type="checkbox" formControlName="isActive" />{{ key('active') | translate }}</label> }
        </form>
      </ng-container>
    </nz-modal>
  `,
  styles: `.fund-setup{display:grid;gap:16px;color:var(--erp-color-text)}header{display:flex;justify-content:space-between;align-items:center}h1{margin:0;font-size:20px}.filters{display:flex;gap:8px}.filters input{max-width:320px}.fund-form{display:grid;gap:8px}.fund-form p[role=alert]{color:var(--erp-status-error);margin:0}`,
})
export class FundSetupPage {
  readonly kind = inject(FUND_SETUP_KIND);
  readonly permission = this.kind === 'fund' ? 'MD.FUND_ACCOUNT' : 'MD.INCOME_EXPENSE_CATEGORY';
  readonly types: readonly string[] = this.kind === 'fund' ? ['Cash', 'Bank'] : ['Income', 'Expense'];
  readonly pageSizeOptions = [...PAGE_SIZE_OPTIONS];
  private readonly fundApi = inject(FundAccountService);
  private readonly categoryApi = inject(IncomeExpenseCategoryService);
  private readonly accountApi = inject(AccountService);
  private readonly currencyApi = inject(CurrencyService);
  private readonly permissions = inject(PermissionService);
  private readonly dialogs = inject(ErpDialogService);
  private readonly company = inject(CompanyContextService);
  private readonly listRequest = latestRequest();
  private readonly accountRequest = latestRequest();
  private readonly currencyRequest = latestRequest();
  private readonly companyId = computed(() => this.company.current()?.id ?? null);
  readonly rows = signal<Row[]>([]); readonly total = signal(0); readonly page = signal(1);
  readonly pageSize = signal(20); readonly sort = signal('code'); readonly loading = signal(false);
  readonly error = signal<string | null>(null); readonly dialogOpen = signal(false);
  readonly editing = signal<Row | null>(null); readonly saving = signal(false);
  readonly saveError = signal<string | null>(null); readonly accounts = signal<Account[]>([]);
  readonly currencies = signal<Currency[]>([]);
  private readonly selectedType = signal(this.types[0]);
  readonly searchControl = new FormControl('', { nonNullable: true });
  readonly filterControl = new FormControl('', { nonNullable: true });
  readonly form = new FormGroup({
    code: new FormControl('', { nonNullable: true, validators: [Validators.required, Validators.pattern(/^[A-Za-z0-9_-]{1,30}$/)] }),
    name: new FormControl('', { nonNullable: true, validators: [Validators.required, Validators.pattern(/.*\S.*/), Validators.maxLength(200)] }),
    type: new FormControl('', { nonNullable: true, validators: Validators.required }),
    currencyId: new FormControl<number | null>(null), bankName: new FormControl(''),
    bankAccountNo: new FormControl('', { validators: Validators.pattern(/^[A-Za-z0-9 -]{0,50}$/) }),
    ledgerAccountId: new FormControl<number | null>(null),
    isActive: new FormControl(true, { nonNullable: true }),
  });
  readonly ledgerOptions = computed(() => {
    const selected = this.selectedType();
    const options = eligibleAccounts(this.accounts(), this.kind === 'fund' ? 'fund' : selected as IncomeExpenseDirection);
    const current = this.editing()?.ledgerAccountId;
    const old = this.accounts().find(x => x.id === current);
    return old && !options.some(x => x.id === old.id) ? [old, ...options] : options;
  });
  readonly currencyOptions = computed(() => this.currencies().filter(x => x.isActive ||
    (this.editing() as FundAccount | null)?.currencyId === x.id));
  constructor() {
    effect(() => { this.companyId(); untracked(() => { this.closeDialog(); this.page.set(1); this.reload(); this.loadReferences(); }); });
    this.form.controls.type.valueChanges.subscribe(type => {
      this.selectedType.set(type); this.form.controls.ledgerAccountId.setValue(null);
      if (type === 'Cash') { this.form.controls.bankName.setValue(''); this.form.controls.bankAccountNo.setValue(''); }
    });
  }
  key(name: string): string { return `md.${this.kind === 'fund' ? 'fundAccount' : 'incomeExpenseCategory'}.${name}`; }
  asFund(row: Row): FundAccount { return row as FundAccount; }
  rowType(row: Row): string { return this.kind === 'fund' ? (row as FundAccount).fundType : (row as IncomeExpenseCategory).direction; }
  accountName(id: number | null): string { const x = this.accounts().find(a => a.id === id); return x ? `${x.code} — ${x.name}` : ''; }
  currencyName(id: number): string { const x = this.currencies().find(a => a.id === id); return x ? `${x.code} — ${x.name}` : String(id); }
  sortOrder(field: 'code' | 'name'): 'ascend' | 'descend' | null {
    return this.sort() === field ? 'ascend' : this.sort() === `-${field}` ? 'descend' : null;
  }
  search(): void { this.page.set(1); this.reload(); }
  onQueryParams(params: NzTableQueryParams): void {
    const active = params.sort.find(x => x.value !== null);
    const sort = active ? `${active.value === 'descend' ? '-' : ''}${active.key}` : 'code';
    if (params.pageIndex === this.page() && params.pageSize === this.pageSize() && sort === this.sort()) return;
    this.page.set(params.pageIndex); this.pageSize.set(params.pageSize); this.sort.set(sort); this.reload();
  }
  reload(): void {
    this.loading.set(true); this.error.set(null);
    const query: FundQuery = { page: this.page(), pageSize: this.pageSize(), keyword: this.searchControl.value, sort: this.sort(),
      ...(this.kind === 'fund' ? { fundType: this.filterControl.value as FundType | '' } :
        { direction: this.filterControl.value as IncomeExpenseDirection | '' }) };
    const request: Observable<PagedResult<Row>> = this.kind === 'fund' ? this.fundApi.list(query) : this.categoryApi.list(query);
    this.listRequest.run(request, {
      next: result => { this.rows.set(result.items); this.total.set(result.totalCount); this.loading.set(false); },
      error: error => { this.rows.set([]); this.total.set(0); this.error.set(this.errorKey(error)); this.loading.set(false); },
    });
  }
  private loadReferences(): void {
    this.accountRequest.cancel(); this.currencyRequest.cancel();
    this.accounts.set([]); this.currencies.set([]);
    if (this.permissions.has('MD.ACCOUNT.VIEW')) {
      this.accountRequest.run(this.accountApi.list(), {
        next: rows => this.accounts.set(rows), error: () => this.accounts.set([]),
      });
    }
    if (this.kind === 'fund' && this.permissions.has('MD.CURRENCY.VIEW')) {
      this.currencyRequest.run(this.allCurrencies(1), {
        next: rows => this.currencies.set(rows), error: () => this.currencies.set([]),
      });
    }
  }
  private allCurrencies(page: number): Observable<Currency[]> {
    return this.currencyApi.list({ page, pageSize: 200, sort: 'code' }).pipe(
      expand(result => result.page * result.pageSize < result.totalCount ?
        this.currencyApi.list({ page: result.page + 1, pageSize: 200, sort: 'code' }) : EMPTY),
      reduce((rows, result) => [...rows, ...result.items], [] as Currency[]));
  }
  openCreate(): void {
    this.editing.set(null); this.form.reset({ code: '', name: '', type: this.types[0], currencyId: null,
      bankName: '', bankAccountNo: '', ledgerAccountId: null, isActive: true });
    this.selectedType.set(this.types[0]);
    this.form.controls.code.enable(); this.form.controls.type.enable(); this.form.controls.currencyId.enable();
    if (this.kind === 'fund') this.form.controls.currencyId.addValidators(Validators.required);
    this.saveError.set(null); this.dialogOpen.set(true);
  }
  openEdit(row: Row): void {
    this.editing.set(row); this.form.reset({ code: row.code, name: row.name, type: this.rowType(row),
      currencyId: this.kind === 'fund' ? (row as FundAccount).currencyId : null,
      bankName: this.kind === 'fund' ? (row as FundAccount).bankName : '',
      bankAccountNo: this.kind === 'fund' ? (row as FundAccount).bankAccountNo : '',
      ledgerAccountId: row.ledgerAccountId, isActive: row.isActive });
    this.selectedType.set(this.rowType(row)); this.form.controls.ledgerAccountId.setValue(row.ledgerAccountId);
    this.form.controls.code.disable(); this.form.controls.currencyId.disable();
    if (this.kind === 'category') this.form.controls.type.disable(); else this.form.controls.type.enable();
    this.saveError.set(null); this.dialogOpen.set(true);
  }
  closeDialog(): void { this.dialogOpen.set(false); this.saveError.set(null); }
  save(): void {
    if (this.saving()) return;
    this.form.markAllAsTouched(); if (this.form.invalid) return;
    const value = this.form.getRawValue(), row = this.editing();
    const request: Observable<Row> = this.kind === 'fund' ? this.saveFund(value, row as FundAccount | null) :
      this.saveCategory(value, row as IncomeExpenseCategory | null);
    this.saving.set(true); this.saveError.set(null);
    request.subscribe({ next: () => { this.saving.set(false); this.closeDialog(); this.reload(); },
      error: error => { this.saving.set(false); this.saveError.set(this.errorKey(error)); } });
  }
  private saveFund(value: ReturnType<typeof this.form.getRawValue>, row: FundAccount | null): Observable<FundAccount> {
    const draft: FundDraft = { code: value.code, name: value.name, fundType: value.type as FundType,
      currencyId: value.currencyId!, bankName: value.bankName, bankAccountNo: value.bankAccountNo,
      ledgerAccountId: value.ledgerAccountId };
    return row ? this.fundApi.update(row.id, { ...draft, isActive: value.isActive, version: row.version }) : this.fundApi.create(draft);
  }
  private saveCategory(value: ReturnType<typeof this.form.getRawValue>, row: IncomeExpenseCategory | null): Observable<IncomeExpenseCategory> {
    const draft: IncomeExpenseDraft = { code: value.code, name: value.name, direction: value.type as IncomeExpenseDirection,
      ledgerAccountId: value.ledgerAccountId };
    return row ? this.categoryApi.update(row.id, { ...draft, isActive: value.isActive, version: row.version }) : this.categoryApi.create(draft);
  }
  confirmDelete(row: Row): void {
    this.dialogs.confirm({ titleKey: this.key('deleteConfirm'), contentKey: 'common.confirm.irreversible',
      params: { code: row.code }, okKey: 'common.action.delete', danger: true }).subscribe(ok => {
      if (!ok) return;
      const request = this.kind === 'fund' ? this.fundApi.remove(row.id, row.version) : this.categoryApi.remove(row.id, row.version);
      request.subscribe({ next: () => this.reload(), error: error => this.error.set(this.errorKey(error)) });
    });
  }
  private errorKey(error: unknown): string { return this.kind === 'fund' ? this.fundApi.errorKey(error) : this.categoryApi.errorKey(error); }
}
