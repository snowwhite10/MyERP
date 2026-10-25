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
import { TaxCalculatorDialog } from './tax-calculator-dialog';
import { TaxRateDialog } from './tax-rate-dialog';
import { TaxCode, TaxService, TaxType } from './tax.service';

export const TAX_TYPES: readonly TaxType[] = ['Output', 'Input', 'Both'];

/** 稅別維護（介面 8.83–8.84）、稅率期間（8.85）與稅額試算（8.86），ADR-038。 */
@Component({
  selector: 'erp-tax-code-page',
  imports: [ReactiveFormsModule, TranslatePipe, NzAlertModule, NzButtonModule, NzInputModule,
    NzModalModule, NzTableModule, NzTagModule, HasPermissionDirective, TaxRateDialog, TaxCalculatorDialog, ListPage],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <erp-list-page titleKey="md.taxCode.title" testId="md-tax-code" [resettable]="false"
      [errorKey]="loadError()" [errorTraceId]="loadTraceId()" (querySubmit)="search()" (retryLoad)="reload()">
      <button erpListActions nz-button nzType="primary" data-testid="md-tax-code-create" *erpHasPermission="'MD.TAX_CODE.CREATE'"
        (click)="openCreate()">{{ 'common.action.add' | translate }}</button>
      <input erpListFilters nz-input type="search" name="keyword" autocomplete="off" data-testid="md-tax-code-search" [formControl]="searchControl"
        [attr.aria-label]="'md.taxCode.search' | translate" [placeholder]="'md.taxCode.search' | translate" (keyup.enter)="search()" />
      <p class="hint">{{ 'md.taxCode.hint' | translate }}</p>
      @if (error() && !loadError()) { <nz-alert nzType="error" nzShowIcon data-testid="md-tax-code-error" [nzMessage]="actionErrorMessage" />
        <ng-template #actionErrorMessage>{{ error()! | translate }}
          @if (actionTraceId()) { {{ 'common.state.traceId' | translate }}: {{ actionTraceId() }} }
        </ng-template>
      }
      @if (!loadError()) {
      <nz-table #table data-testid="md-tax-code-table" nzSize="middle" [nzData]="rows()" [nzLoading]="loading()"
        [nzFrontPagination]="false" [nzTotal]="total()" [nzPageIndex]="page()" [nzPageSize]="pageSize()"
        nzShowSizeChanger [nzPageSizeOptions]="pageSizeOptions" [nzNoResult]="(appliedKeyword() ? 'md.taxCode.noResults' : 'md.taxCode.empty') | translate"
        (nzQueryParams)="onQueryParams($event)">
        <thead><tr>
          <th nzColumnKey="code" [nzSortFn]="true" [nzSortOrder]="sortOrder('code')">{{ 'md.taxCode.code' | translate }}</th>
          <th nzColumnKey="name" [nzSortFn]="true" [nzSortOrder]="sortOrder('name')">{{ 'md.taxCode.name' | translate }}</th>
          <th>{{ 'md.taxCode.taxType' | translate }}</th><th>{{ 'md.taxCode.currentRate' | translate }}</th>
          <th>{{ 'md.taxCode.status' | translate }}</th><th>{{ 'md.taxCode.actions' | translate }}</th>
        </tr></thead>
        <tbody>@for (row of table.data; track row.id) {
          <tr [attr.data-testid]="'md-tax-code-row-' + row.code"><td>{{ row.code }}</td><td>{{ row.name }}</td>
            <td>{{ 'md.taxCode.type' + row.taxType | translate }}</td>
            <td [attr.data-testid]="'md-tax-code-rate-' + row.code">{{ row.currentRatePercent === null ? ('md.taxCode.noRate' | translate) : row.currentRatePercent + '%' }}</td>
            <td><nz-tag [nzColor]="row.isActive ? 'success' : 'default'">{{ (row.isActive ? 'md.taxCode.active' : 'md.taxCode.inactive') | translate }}</nz-tag></td>
            <td><button nz-button nzType="link" [attr.data-testid]="'md-tax-code-rates-' + row.code"
                (click)="rateTaxCode.set(row)">{{ 'md.taxCode.rates' | translate }}</button>
              <button nz-button nzType="link" [attr.data-testid]="'md-tax-code-calc-' + row.code"
                (click)="calcTaxCode.set(row)">{{ 'md.taxCode.calculate' | translate }}</button>
              <button nz-button nzType="link" *erpHasPermission="'MD.TAX_CODE.EDIT'"
                [attr.data-testid]="'md-tax-code-edit-' + row.code" (click)="openEdit(row)">{{ 'common.action.edit' | translate }}</button>
              <button nz-button nzType="link" nzDanger *erpHasPermission="'MD.TAX_CODE.DELETE'"
                [attr.data-testid]="'md-tax-code-delete-' + row.code" [disabled]="deleting()" (click)="confirmDelete(row)">{{ (deleting() ? 'common.action.processing' : 'common.action.delete') | translate }}</button></td></tr>
        }</tbody>
      </nz-table>
      }
    </erp-list-page>
    <nz-modal [nzVisible]="dialogOpen()" [nzTitle]="'md.taxCode.formTitle' | translate"
      [nzOkText]="'common.action.save' | translate" [nzCancelText]="'common.action.cancel' | translate"
      [nzOkLoading]="saving()" (nzOnOk)="save()" (nzOnCancel)="closeDialog()">
      <ng-container *nzModalContent>
        @if (saveError()) { <nz-alert nzType="error" nzShowIcon data-testid="md-tax-code-save-error" [nzMessage]="saveError()! | translate" /> }
        <form class="tax-code-form" [formGroup]="form" (ngSubmit)="save()" data-testid="md-tax-code-form">
          <label for="tax-code-code">{{ 'md.taxCode.code' | translate }}</label>
          <input nz-input id="tax-code-code" data-testid="md-tax-code-code" formControlName="code" maxlength="30" />
          @if (form.controls.code.touched && form.controls.code.invalid) { <p role="alert">{{ 'md.taxCode.invalidCode' | translate }}</p> }
          <label for="tax-code-name">{{ 'md.taxCode.name' | translate }}</label>
          <input nz-input id="tax-code-name" data-testid="md-tax-code-name" formControlName="name" maxlength="200" />
          @if (form.controls.name.touched && form.controls.name.invalid) { <p role="alert">{{ 'md.taxCode.invalidName' | translate }}</p> }
          <label for="tax-code-type">{{ 'md.taxCode.taxType' | translate }}</label>
          <select id="tax-code-type" data-testid="md-tax-code-type" formControlName="taxType">
            @for (type of taxTypes; track type) { <option [value]="type">{{ 'md.taxCode.type' + type | translate }}</option> }
          </select>
          @if (editing()) { <label><input type="checkbox" formControlName="isActive" data-testid="md-tax-code-active" />{{ 'md.taxCode.active' | translate }}</label> }
        </form>
      </ng-container>
    </nz-modal>
    <erp-tax-rate-dialog [taxCode]="rateTaxCode()" (closed)="rateTaxCode.set(null)" (changed)="reload()" />
    <erp-tax-calculator-dialog [taxCode]="calcTaxCode()" (closed)="calcTaxCode.set(null)" />
  `,
  styles: `
    .hint { margin: 0; color: var(--erp-color-text-secondary); }
    input[erpListFilters] { max-width: 320px; }
    .tax-code-form { display: grid; gap: 8px; } .tax-code-form p { color: var(--erp-status-error); margin: 0; }
  `,
})
export class TaxCodePage {
  readonly pageSizeOptions = [...PAGE_SIZE_OPTIONS];
  readonly taxTypes = TAX_TYPES;
  private readonly api = inject(TaxService);
  private readonly dialogs = inject(ErpDialogService);
  private readonly company = inject(CompanyContextService);
  private readonly listRequest = latestRequest();
  private readonly companyId = computed(() => this.company.current()?.id ?? null);
  readonly rows = signal<TaxCode[]>([]);
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
  readonly editing = signal<TaxCode | null>(null);
  readonly rateTaxCode = signal<TaxCode | null>(null);
  readonly calcTaxCode = signal<TaxCode | null>(null);
  readonly saving = signal(false);
  /** 刪除進行中：避免第二次確認帶舊版本而得到 409（MD-F05）。 */
  readonly deleting = signal(false);
  readonly saveError = signal<string | null>(null);
  readonly searchControl = new FormControl('', { nonNullable: true });
  readonly form = new FormGroup({
    code: new FormControl('', { nonNullable: true, validators: [Validators.required, Validators.pattern(/^\s*[A-Za-z0-9_-]{1,30}\s*$/)] }),
    name: new FormControl('', { nonNullable: true, validators: [Validators.required, Validators.pattern(/.*\S.*/), Validators.maxLength(200)] }),
    taxType: new FormControl<TaxType>('Both', { nonNullable: true }),
    isActive: new FormControl(true, { nonNullable: true }),
  });
  constructor() {
    effect(() => { this.companyId(); untracked(() => {
      this.closeDialog(); this.rateTaxCode.set(null); this.calcTaxCode.set(null); this.page.set(1); this.reload();
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
    this.form.reset({ code: '', name: '', taxType: 'Both', isActive: true });
    this.form.controls.code.enable(); this.saveError.set(null); this.dialogOpen.set(true);
  }
  openEdit(row: TaxCode): void {
    this.editing.set(row);
    this.form.reset({ code: row.code, name: row.name, taxType: row.taxType, isActive: row.isActive });
    this.form.controls.code.disable(); this.saveError.set(null); this.dialogOpen.set(true);
  }
  closeDialog(): void { this.dialogOpen.set(false); this.saveError.set(null); }
  save(): void {
    if (this.saving()) return;
    this.form.markAllAsTouched(); if (this.form.invalid) return;
    const draft = this.form.getRawValue(); const row = this.editing();
    const request = row ? this.api.update(row.id, { name: draft.name, taxType: draft.taxType, isActive: draft.isActive, version: row.version })
      : this.api.create({ code: draft.code, name: draft.name, taxType: draft.taxType });
    this.saving.set(true); this.saveError.set(null);
    request.subscribe({
      next: () => { this.saving.set(false); this.closeDialog(); this.reload(); },
      error: (error: unknown) => { this.saving.set(false); this.saveError.set(this.api.errorKey(error)); if (isStaleRowError(error)) this.reload(); },
    });
  }
  confirmDelete(row: TaxCode): void {
    if (this.deleting()) return;
    this.dialogs.confirm({ titleKey: 'md.taxCode.deleteConfirm', contentKey: 'common.confirm.irreversible',
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
