import { ChangeDetectionStrategy, Component, computed, effect, inject, signal, untracked } from '@angular/core';
import { FormControl, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { TranslatePipe } from '@ngx-translate/core';
import { NzAlertModule } from 'ng-zorro-antd/alert';
import { NzButtonModule } from 'ng-zorro-antd/button';
import { NzInputModule } from 'ng-zorro-antd/input';
import { NzModalModule } from 'ng-zorro-antd/modal';
import { NzTableModule, NzTableQueryParams } from 'ng-zorro-antd/table';
import { NzTagModule } from 'ng-zorro-antd/tag';
import { HasPermissionDirective } from '../../core/auth/has-permission.directive';
import { CompanyContextService } from '../../core/company/company-context.service';
import { ErpDialogService } from '../../shared/dialogs/dialog.service';
import { latestRequest } from '../../shared/list-page/latest-request';
import { PAGE_SIZE_OPTIONS } from '../../shared/list-page/table-query';
import { Currency, CurrencyService } from './currency.service';
import { ExchangeRateDialog } from './exchange-rate-dialog';

/** 幣別維護（介面 8.79–8.81）與匯率（介面 8.82，ADR-037）；本位幣以旗標標示。 */
@Component({
  selector: 'erp-currency-page',
  imports: [ReactiveFormsModule, TranslatePipe, NzAlertModule, NzButtonModule, NzInputModule,
    NzModalModule, NzTableModule, NzTagModule, HasPermissionDirective, ExchangeRateDialog],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <section class="md-currencies">
      <header><h1 data-testid="md-currency-title">{{ 'md.currency.title' | translate }}</h1>
        <button nz-button nzType="primary" data-testid="md-currency-create" *erpHasPermission="'MD.CURRENCY.CREATE'"
          (click)="openCreate()">{{ 'common.action.add' | translate }}</button></header>
      <p class="hint">{{ 'md.currency.hint' | translate }}</p>
      <div class="filters"><input nz-input type="search" data-testid="md-currency-search" [formControl]="searchControl"
        [placeholder]="'md.currency.search' | translate" (keyup.enter)="search()" />
        <button nz-button data-testid="md-currency-search-button" (click)="search()">{{ 'common.action.search' | translate }}</button></div>
      @if (error()) { <nz-alert nzType="error" nzShowIcon data-testid="md-currency-error" [nzMessage]="error()! | translate" /> }
      <nz-table #table data-testid="md-currency-table" nzSize="middle" [nzData]="rows()" [nzLoading]="loading()"
        [nzFrontPagination]="false" [nzTotal]="total()" [nzPageIndex]="page()" [nzPageSize]="pageSize()"
        nzShowSizeChanger [nzPageSizeOptions]="pageSizeOptions" [nzNoResult]="'md.currency.empty' | translate"
        (nzQueryParams)="onQueryParams($event)">
        <thead><tr>
          <th nzColumnKey="code" [nzSortFn]="true" [nzSortOrder]="sortOrder('code')">{{ 'md.currency.code' | translate }}</th>
          <th nzColumnKey="name" [nzSortFn]="true" [nzSortOrder]="sortOrder('name')">{{ 'md.currency.name' | translate }}</th>
          <th>{{ 'md.currency.symbol' | translate }}</th><th>{{ 'md.currency.decimalPlaces' | translate }}</th>
          <th>{{ 'md.currency.status' | translate }}</th><th>{{ 'md.currency.actions' | translate }}</th>
        </tr></thead>
        <tbody>@for (row of table.data; track row.id) {
          <tr [attr.data-testid]="'md-currency-row-' + row.code"><td>{{ row.code }}
              @if (row.isBase) { <nz-tag nzColor="blue" [attr.data-testid]="'md-currency-base-' + row.code">{{ 'md.currency.base' | translate }}</nz-tag> }</td>
            <td>{{ row.name }}</td><td>{{ row.symbol }}</td><td>{{ row.decimalPlaces }}</td>
            <td><nz-tag [nzColor]="row.isActive ? 'success' : 'default'">{{ (row.isActive ? 'md.currency.active' : 'md.currency.inactive') | translate }}</nz-tag></td>
            <td>@if (!row.isBase) { <button nz-button nzType="link" [attr.data-testid]="'md-currency-rates-' + row.code"
                (click)="rateCurrency.set(row)">{{ 'md.currency.rates' | translate }}</button> }
              <button nz-button nzType="link" *erpHasPermission="'MD.CURRENCY.EDIT'"
                [attr.data-testid]="'md-currency-edit-' + row.code" (click)="openEdit(row)">{{ 'common.action.edit' | translate }}</button>
              @if (!row.isBase) { <button nz-button nzType="link" nzDanger *erpHasPermission="'MD.CURRENCY.DELETE'"
                [attr.data-testid]="'md-currency-delete-' + row.code" (click)="confirmDelete(row)">{{ 'common.action.delete' | translate }}</button> }</td></tr>
        }</tbody>
      </nz-table>
    </section>
    <nz-modal [nzVisible]="dialogOpen()" [nzTitle]="'md.currency.formTitle' | translate"
      [nzOkText]="'common.action.save' | translate" [nzCancelText]="'common.action.cancel' | translate"
      [nzOkLoading]="saving()" (nzOnOk)="save()" (nzOnCancel)="closeDialog()">
      <ng-container *nzModalContent>
        @if (saveError()) { <nz-alert nzType="error" nzShowIcon data-testid="md-currency-save-error" [nzMessage]="saveError()! | translate" /> }
        <form class="currency-form" [formGroup]="form" (ngSubmit)="save()" data-testid="md-currency-form">
          <label for="currency-code">{{ 'md.currency.code' | translate }}</label>
          <input nz-input id="currency-code" data-testid="md-currency-code" formControlName="code" maxlength="3" />
          @if (form.controls.code.touched && form.controls.code.invalid) { <p role="alert">{{ 'md.currency.invalidCode' | translate }}</p> }
          <label for="currency-name">{{ 'md.currency.name' | translate }}</label>
          <input nz-input id="currency-name" data-testid="md-currency-name" formControlName="name" maxlength="200" />
          @if (form.controls.name.touched && form.controls.name.invalid) { <p role="alert">{{ 'md.currency.invalidName' | translate }}</p> }
          <label for="currency-symbol">{{ 'md.currency.symbol' | translate }}</label>
          <input nz-input id="currency-symbol" data-testid="md-currency-symbol" formControlName="symbol" maxlength="10" />
          <label for="currency-decimals">{{ 'md.currency.decimalPlaces' | translate }}</label>
          <input nz-input id="currency-decimals" type="number" min="0" max="4" step="1" data-testid="md-currency-decimals" formControlName="decimalPlaces" />
          @if (form.controls.decimalPlaces.touched && form.controls.decimalPlaces.invalid) { <p role="alert">{{ 'md.currency.invalidDecimals' | translate }}</p> }
          <label><input type="checkbox" formControlName="isBase" data-testid="md-currency-is-base" />{{ 'md.currency.setBase' | translate }}</label>
          @if (editing()?.isBase) { <small data-testid="md-currency-base-locked">{{ 'md.currency.baseHint' | translate }}</small> }
          @if (editing()) { <label><input type="checkbox" formControlName="isActive" data-testid="md-currency-active" />{{ 'md.currency.active' | translate }}</label> }
        </form>
      </ng-container>
    </nz-modal>
    <erp-exchange-rate-dialog [currency]="rateCurrency()" (closed)="rateCurrency.set(null)" />
  `,
  styles: `
    .md-currencies { display: grid; gap: 16px; color: var(--erp-color-text); }
    header { display: flex; justify-content: space-between; align-items: center; }
    h1 { margin: 0; font-size: 20px; }
    .hint { margin: 0; color: var(--erp-color-text-secondary); }
    .filters { display: flex; gap: 8px; } .filters input { max-width: 320px; }
    .currency-form { display: grid; gap: 8px; } .currency-form p { color: var(--erp-status-error); margin: 0; }
    .currency-form small { color: var(--erp-color-text-secondary); }
  `,
})
export class CurrencyPage {
  readonly pageSizeOptions = [...PAGE_SIZE_OPTIONS];
  private readonly api = inject(CurrencyService);
  private readonly dialogs = inject(ErpDialogService);
  private readonly company = inject(CompanyContextService);
  private readonly listRequest = latestRequest();
  private readonly companyId = computed(() => this.company.current()?.id ?? null);
  readonly rows = signal<Currency[]>([]);
  readonly total = signal(0);
  readonly page = signal(1);
  readonly pageSize = signal(20);
  readonly sort = signal('code');
  readonly loading = signal(false);
  readonly error = signal<string | null>(null);
  readonly dialogOpen = signal(false);
  readonly editing = signal<Currency | null>(null);
  readonly rateCurrency = signal<Currency | null>(null);
  readonly saving = signal(false);
  readonly saveError = signal<string | null>(null);
  readonly searchControl = new FormControl('', { nonNullable: true });
  readonly form = new FormGroup({
    code: new FormControl('', { nonNullable: true, validators: [Validators.required, Validators.pattern(/^\s*[A-Za-z]{3}\s*$/)] }),
    name: new FormControl('', { nonNullable: true, validators: [Validators.required, Validators.pattern(/.*\S.*/), Validators.maxLength(200)] }),
    symbol: new FormControl('', { nonNullable: true, validators: [Validators.maxLength(10)] }),
    decimalPlaces: new FormControl(2, { nonNullable: true,
      validators: [Validators.required, Validators.min(0), Validators.max(4), Validators.pattern(/^\d$/)] }),
    isBase: new FormControl(false, { nonNullable: true }),
    isActive: new FormControl(true, { nonNullable: true }),
  });
  constructor() {
    effect(() => { this.companyId(); untracked(() => {
      this.closeDialog(); this.rateCurrency.set(null); this.page.set(1); this.reload();
    }); });
  }
  sortOrder(field: 'code' | 'name'): 'ascend' | 'descend' | null {
    return this.sort() === field ? 'ascend' : this.sort() === `-${field}` ? 'descend' : null;
  }
  search(): void { this.page.set(1); this.reload(); }
  onQueryParams(params: NzTableQueryParams): void {
    const active = params.sort.find((x) => x.value !== null);
    const sort = active ? `${active.value === 'descend' ? '-' : ''}${active.key}` : 'code';
    if (params.pageIndex === this.page() && params.pageSize === this.pageSize() && sort === this.sort()) return;
    this.page.set(params.pageIndex); this.pageSize.set(params.pageSize); this.sort.set(sort); this.reload();
  }
  reload(): void {
    this.loading.set(true); this.error.set(null);
    this.listRequest.run(this.api.list({ page: this.page(), pageSize: this.pageSize(),
      keyword: this.searchControl.value, sort: this.sort() }), {
      next: (result) => { this.rows.set(result.items); this.total.set(result.totalCount); this.loading.set(false); },
      error: (error: unknown) => { this.rows.set([]); this.total.set(0); this.error.set(this.api.errorKey(error)); this.loading.set(false); },
    });
  }
  openCreate(): void {
    this.editing.set(null);
    this.form.reset({ code: '', name: '', symbol: '', decimalPlaces: 2, isBase: false, isActive: true });
    this.form.controls.code.enable(); this.form.controls.isBase.enable(); this.saveError.set(null); this.dialogOpen.set(true);
  }
  openEdit(row: Currency): void {
    this.editing.set(row);
    this.form.reset({ code: row.code, name: row.name, symbol: row.symbol ?? '', decimalPlaces: row.decimalPlaces,
      isBase: row.isBase, isActive: row.isActive });
    this.form.controls.code.disable();
    // 本位幣不可直接取消，只能把另一個幣別設為本位幣（ADR-037）。
    if (row.isBase) this.form.controls.isBase.disable(); else this.form.controls.isBase.enable();
    this.saveError.set(null); this.dialogOpen.set(true);
  }
  closeDialog(): void { this.dialogOpen.set(false); this.saveError.set(null); }
  save(): void {
    if (this.saving()) return;
    this.form.markAllAsTouched(); if (this.form.invalid) return;
    const draft = this.form.getRawValue(); const row = this.editing();
    const body = { name: draft.name, symbol: draft.symbol, decimalPlaces: Number(draft.decimalPlaces), isBase: draft.isBase };
    const request = row ? this.api.update(row.id, { ...body, isActive: draft.isActive, version: row.version })
      : this.api.create({ ...body, code: draft.code });
    this.saving.set(true); this.saveError.set(null);
    request.subscribe({
      next: () => { this.saving.set(false); this.closeDialog(); this.reload(); },
      error: (error: unknown) => { this.saving.set(false); this.saveError.set(this.api.errorKey(error)); },
    });
  }
  confirmDelete(row: Currency): void {
    this.dialogs.confirm({ titleKey: 'md.currency.deleteConfirm', contentKey: 'common.confirm.irreversible',
      params: { code: row.code }, okKey: 'common.action.delete', danger: true }).subscribe((ok) => {
      if (!ok) return;
      this.error.set(null);
      this.api.remove(row.id, row.version).subscribe({
        next: () => this.reload(),
        error: (error: unknown) => this.error.set(this.api.errorKey(error)),
      });
    });
  }
}
