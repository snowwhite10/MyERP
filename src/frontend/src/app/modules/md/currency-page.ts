import { ChangeDetectionStrategy, Component, computed, effect, inject, signal, untracked } from '@angular/core';
import { FormControl, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { TranslatePipe } from '@ngx-translate/core';
import { NzAlertModule } from 'ng-zorro-antd/alert';
import { NzButtonModule } from 'ng-zorro-antd/button';
import { NzInputModule } from 'ng-zorro-antd/input';
import { NzModalModule } from 'ng-zorro-antd/modal';
import { NzTableModule, NzTableQueryParams } from 'ng-zorro-antd/table';
import { NzTagModule } from 'ng-zorro-antd/tag';
import { apiErrorTraceId } from '../../core/http/api-error';
import { HasPermissionDirective } from '../../core/auth/has-permission.directive';
import { CompanyContextService } from '../../core/company/company-context.service';
import { ErpDialogService } from '../../shared/dialogs/dialog.service';
import { latestRequest } from '../../shared/list-page/latest-request';
import { isStaleRowError } from '../../shared/list-page/stale-row';
import { ListPage } from '../../shared/list-page/list-page';
import { PAGE_SIZE_OPTIONS, nextTableState, sortOrderOf } from '../../shared/list-page/table-query';
import { Currency, CurrencyService } from './currency.service';
import { ExchangeRateDialog } from './exchange-rate-dialog';

/** 幣別維護（介面 8.79–8.81）與匯率（介面 8.82，ADR-037）；本位幣以旗標標示。 */
@Component({
  selector: 'erp-currency-page',
  imports: [ReactiveFormsModule, TranslatePipe, NzAlertModule, NzButtonModule, NzInputModule,
    NzModalModule, NzTableModule, NzTagModule, HasPermissionDirective, ExchangeRateDialog, ListPage],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <erp-list-page titleKey="md.currency.title" testId="md-currency" [resettable]="false"
      [errorKey]="loadError()" [errorTraceId]="loadTraceId()" (querySubmit)="search()" (retryLoad)="reload()">
      <button erpListActions nz-button nzType="primary" data-testid="md-currency-create" *erpHasPermission="'MD.CURRENCY.CREATE'"
        (click)="openCreate()">{{ 'common.action.add' | translate }}</button>
      <input erpListFilters nz-input type="search" name="keyword" autocomplete="off" data-testid="md-currency-search" [formControl]="searchControl"
        [attr.aria-label]="'md.currency.search' | translate" [placeholder]="'md.currency.search' | translate" (keyup.enter)="search()" />
      <p class="hint">{{ 'md.currency.hint' | translate }}</p>
      @if (error() && !loadError()) { <nz-alert nzType="error" nzShowIcon data-testid="md-currency-error" [nzMessage]="actionErrorMessage" />
        <ng-template #actionErrorMessage>{{ error()! | translate }}
          @if (actionTraceId()) { {{ 'common.state.traceId' | translate }}: {{ actionTraceId() }} }
        </ng-template>
      }
      @if (!loadError()) {
      <nz-table #table data-testid="md-currency-table" nzSize="middle" [nzData]="rows()" [nzLoading]="loading()"
        [nzFrontPagination]="false" [nzTotal]="total()" [nzPageIndex]="page()" [nzPageSize]="pageSize()"
        nzShowSizeChanger [nzPageSizeOptions]="pageSizeOptions" [nzNoResult]="(appliedKeyword() ? 'md.currency.noResults' : 'md.currency.empty') | translate"
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
                [attr.data-testid]="'md-currency-delete-' + row.code" [disabled]="deleting()" (click)="confirmDelete(row)">{{ (deleting() ? 'common.action.processing' : 'common.action.delete') | translate }}</button> }</td></tr>
        }</tbody>
      </nz-table>
      }
    </erp-list-page>
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
    .hint { margin: 0; color: var(--erp-color-text-secondary); }
    input[erpListFilters] { max-width: 320px; }
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
  readonly loadError = signal<string | null>(null);
  readonly loadTraceId = signal<string | null>(null);
  readonly actionTraceId = signal<string | null>(null);
  readonly appliedKeyword = signal('');
  readonly dialogOpen = signal(false);
  readonly editing = signal<Currency | null>(null);
  readonly rateCurrency = signal<Currency | null>(null);
  readonly saving = signal(false);
  /** 刪除進行中：避免第二次確認帶舊版本而得到 409（MD-F05）。 */
  readonly deleting = signal(false);
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
    return sortOrderOf(this.sort(), field);
  }
  search(): void { this.appliedKeyword.set(this.searchControl.value.trim()); this.page.set(1); this.reload(); }
  onQueryParams(params: NzTableQueryParams): void {
    const next = nextTableState(params, { page: this.page(), pageSize: this.pageSize(), sort: this.sort() }, ['code', 'name'], 'code');
    if (!next) return;
    this.page.set(next.page); this.pageSize.set(next.pageSize); this.sort.set(next.sort); this.reload();
  }
  reload(): void {
    this.loading.set(true); this.error.set(null); this.loadError.set(null); this.loadTraceId.set(null); this.actionTraceId.set(null);
    this.listRequest.run(this.api.list({ page: this.page(), pageSize: this.pageSize(),
      keyword: this.appliedKeyword(), sort: this.sort() }), {
      next: (result) => { this.rows.set(result.items); this.total.set(result.totalCount); this.loading.set(false); },
      error: (error: unknown) => { this.rows.set([]); this.total.set(0); this.error.set(this.api.errorKey(error)); this.loadError.set(this.api.errorKey(error));
        this.loadTraceId.set(apiErrorTraceId(error)); this.loading.set(false); },
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
      error: (error: unknown) => { this.saving.set(false); this.saveError.set(this.api.errorKey(error)); if (isStaleRowError(error)) this.reload(); },
    });
  }
  confirmDelete(row: Currency): void {
    if (this.deleting()) return;
    this.dialogs.confirm({ titleKey: 'md.currency.deleteConfirm', contentKey: 'common.confirm.irreversible',
      params: { code: row.code }, okKey: 'common.action.delete', danger: true }).subscribe((ok) => {
      if (!ok || this.deleting()) return;
      this.deleting.set(true); this.error.set(null); this.actionTraceId.set(null);
      this.api.remove(row.id, row.version).subscribe({
        next: () => { this.deleting.set(false); this.reload(); },
        error: (error: unknown) => { this.deleting.set(false); if (isStaleRowError(error)) this.reload(); this.error.set(this.api.errorKey(error)); this.actionTraceId.set(apiErrorTraceId(error)); },
      });
    });
  }
}
