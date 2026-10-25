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
import { apiErrorTraceId } from '../../core/http/api-error';
import { CompanyContextService } from '../../core/company/company-context.service';
import { ErpDialogService } from '../../shared/dialogs/dialog.service';
import { fetchAllPages, OPTION_PAGE_SIZE } from '../../shared/list-page/fetch-all-pages';
import { latestRequest } from '../../shared/list-page/latest-request';
import { isStaleRowError } from '../../shared/list-page/stale-row';
import { ListPage } from '../../shared/list-page/list-page';
import { PAGE_SIZE_OPTIONS, nextTableState, sortOrderOf } from '../../shared/list-page/table-query';
import { FACTOR_PATTERN, QUANTITY_PATTERN, Uom, UomConversion, UomService } from './uom.service';

/** 計量單位維護頁：單位列表（介面 8.45–8.47）、單位換算及換算試算；權限 `MD.UOM.*`。 */
@Component({
  selector: 'erp-uom-page',
  imports: [ReactiveFormsModule, TranslatePipe, NzAlertModule, NzButtonModule, NzInputModule,
    NzModalModule, NzTableModule, NzTagModule, HasPermissionDirective, ListPage],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <erp-list-page titleKey="md.uom.title" testId="md-uom" [resettable]="false"
      [errorKey]="loadError()" [errorTraceId]="loadTraceId()" (querySubmit)="search()" (retryLoad)="reload()">
      <button erpListActions nz-button nzType="primary" data-testid="md-uom-create"
        *erpHasPermission="'MD.UOM.CREATE'" (click)="openCreate()">{{ 'common.action.add' | translate }}</button>
      <input erpListFilters nz-input type="search" name="keyword" autocomplete="off" data-testid="md-uom-search" [formControl]="searchControl"
        [attr.aria-label]="'md.uom.search' | translate" [placeholder]="'md.uom.search' | translate" (keyup.enter)="search()" />
      @if (error() && !loadError()) { <nz-alert nzType="error" nzShowIcon data-testid="md-uom-error" [nzMessage]="actionErrorMessage" />
        <ng-template #actionErrorMessage>{{ error()! | translate }}
          @if (actionTraceId()) { {{ 'common.state.traceId' | translate }}: {{ actionTraceId() }} }
        </ng-template>
      }
      @if (!loadError()) {
      <nz-table #table data-testid="md-uom-table" nzSize="middle" [nzData]="rows()" [nzLoading]="loading()"
        [nzFrontPagination]="false" [nzTotal]="total()" [nzPageIndex]="page()" [nzPageSize]="pageSize()"
        nzShowSizeChanger [nzPageSizeOptions]="pageSizeOptions" [nzNoResult]="(appliedKeyword() ? 'md.uom.noResults' : 'md.uom.empty') | translate"
        (nzQueryParams)="onQueryParams($event)">
        <thead><tr>
          <th nzColumnKey="code" [nzSortFn]="true" [nzSortOrder]="sortOrder('code')">{{ 'md.uom.code' | translate }}</th>
          <th nzColumnKey="name" [nzSortFn]="true" [nzSortOrder]="sortOrder('name')">{{ 'md.uom.name' | translate }}</th>
          <th>{{ 'md.uom.decimalPlaces' | translate }}</th><th>{{ 'md.uom.status' | translate }}</th>
          <th>{{ 'md.uom.actions' | translate }}</th>
        </tr></thead>
        <tbody>@for (row of table.data; track row.id) {
          <tr [attr.data-testid]="'md-uom-row-' + row.code">
            <td>{{ row.code }}</td><td>{{ row.name }}</td><td>{{ row.decimalPlaces }}</td>
            <td><nz-tag [nzColor]="row.isActive ? 'success' : 'default'">
              {{ (row.isActive ? 'md.uom.active' : 'md.uom.inactive') | translate }}
            </nz-tag></td>
            <td>
              <button nz-button nzType="link" *erpHasPermission="'MD.UOM.EDIT'"
                [attr.data-testid]="'md-uom-edit-' + row.code" (click)="openEdit(row)">{{ 'common.action.edit' | translate }}</button>
              <button nz-button nzType="link" nzDanger *erpHasPermission="'MD.UOM.DELETE'"
                [attr.data-testid]="'md-uom-delete-' + row.code" [disabled]="busy()"
                (click)="confirmDelete(row)">{{ (busy() ? 'common.action.processing' : 'common.action.delete') | translate }}</button>
            </td>
          </tr>
        }</tbody>
      </nz-table>
      }

      <header><h2 data-testid="md-uom-conversion-title">{{ 'md.uom.conversions' | translate }}</h2>
        <button nz-button data-testid="md-uom-conversion-create"
          *erpHasPermission="'MD.UOM.CREATE'" (click)="openCreateConversion()">{{ 'common.action.add' | translate }}</button>
      </header>
      @if (conversionError()) { <nz-alert nzType="error" nzShowIcon data-testid="md-uom-conversion-error" [nzMessage]="conversionError()! | translate" /> }
      <nz-table #conversionTable data-testid="md-uom-conversion-table" nzSize="middle" [nzData]="conversions()"
        [nzShowPagination]="false" [nzPageSize]="conversions().length || 1" [nzNoResult]="'md.uom.noConversions' | translate">
        <thead><tr>
          <th>{{ 'md.uom.from' | translate }}</th><th>{{ 'md.uom.to' | translate }}</th>
          <th>{{ 'md.uom.factor' | translate }}</th><th>{{ 'md.uom.actions' | translate }}</th>
        </tr></thead>
        <tbody>@for (row of conversionTable.data; track row.id) {
          <tr [attr.data-testid]="'md-uom-conversion-row-' + row.fromUomCode + '-' + row.toUomCode">
            <td>{{ row.fromUomCode }}</td><td>{{ row.toUomCode }}</td>
            <td>{{ 'md.uom.rule' | translate: { from: row.fromUomCode, factor: row.factor, to: row.toUomCode } }}</td>
            <td>
              <button nz-button nzType="link" *erpHasPermission="'MD.UOM.EDIT'"
                [attr.data-testid]="'md-uom-conversion-edit-' + row.id" (click)="openEditConversion(row)">{{ 'common.action.edit' | translate }}</button>
              <button nz-button nzType="link" nzDanger *erpHasPermission="'MD.UOM.DELETE'"
                [attr.data-testid]="'md-uom-conversion-delete-' + row.id" [disabled]="busy()"
                (click)="confirmDeleteConversion(row)">{{ (busy() ? 'common.action.processing' : 'common.action.delete') | translate }}</button>
            </td>
          </tr>
        }</tbody>
      </nz-table>

      <h2>{{ 'md.uom.calculator' | translate }}</h2>
      <form class="calculator" [formGroup]="calculator" (ngSubmit)="convert()" data-testid="md-uom-calculator">
        <input nz-input data-testid="md-uom-calc-quantity" formControlName="quantity" [attr.aria-label]="'md.uom.quantity' | translate"
          [placeholder]="'md.uom.quantity' | translate" />
        <select data-testid="md-uom-calc-from" formControlName="fromUomId" [attr.aria-label]="'md.uom.from' | translate">
          <option [ngValue]="null">{{ 'md.uom.from' | translate }}</option>
          @for (uom of options(); track uom.id) { <option [ngValue]="uom.id">{{ uom.code }}</option> }
        </select>
        <select data-testid="md-uom-calc-to" formControlName="toUomId" [attr.aria-label]="'md.uom.to' | translate">
          <option [ngValue]="null">{{ 'md.uom.to' | translate }}</option>
          @for (uom of options(); track uom.id) { <option [ngValue]="uom.id">{{ uom.code }}</option> }
        </select>
        <button nz-button type="submit" data-testid="md-uom-calc-convert">{{ 'md.uom.convert' | translate }}</button>
        @if (calcResult() !== null) { <strong data-testid="md-uom-calc-result">{{ calcResult() }}</strong> }
        @if (calcError()) { <span role="alert" data-testid="md-uom-calc-error">{{ calcError()! | translate }}</span> }
      </form>
    </erp-list-page>

    <nz-modal [nzVisible]="dialogOpen()" [nzTitle]="'md.uom.formTitle' | translate"
      [nzOkText]="'common.action.save' | translate" [nzCancelText]="'common.action.cancel' | translate"
      [nzOkLoading]="saving()" (nzOnOk)="save()" (nzOnCancel)="closeDialog()">
      <ng-container *nzModalContent>
        @if (saveError()) { <nz-alert nzType="error" nzShowIcon data-testid="md-uom-save-error" [nzMessage]="saveError()! | translate" /> }
        <form class="uom-form" [formGroup]="form" (ngSubmit)="save()" data-testid="md-uom-form">
          <label for="uom-code">{{ 'md.uom.code' | translate }}</label>
          <input nz-input id="uom-code" data-testid="md-uom-code" formControlName="code" maxlength="30" />
          @if (form.controls.code.touched && form.controls.code.invalid) { <p role="alert">{{ 'md.uom.invalidCode' | translate }}</p> }
          <label for="uom-name">{{ 'md.uom.name' | translate }}</label>
          <input nz-input id="uom-name" data-testid="md-uom-name" formControlName="name" maxlength="200" />
          @if (form.controls.name.touched && form.controls.name.invalid) { <p role="alert">{{ 'md.uom.invalidName' | translate }}</p> }
          <label for="uom-decimal-places">{{ 'md.uom.decimalPlaces' | translate }}</label>
          <select id="uom-decimal-places" data-testid="md-uom-decimal-places" formControlName="decimalPlaces">
            @for (places of decimalPlaceOptions; track places) { <option [ngValue]="places">{{ places }}</option> }
          </select>
          @if (editing()) { <label><input type="checkbox" formControlName="isActive" data-testid="md-uom-active" />
            {{ 'md.uom.active' | translate }}</label> }
        </form>
      </ng-container>
    </nz-modal>

    <nz-modal [nzVisible]="conversionDialogOpen()" [nzTitle]="'md.uom.conversionFormTitle' | translate"
      [nzOkText]="'common.action.save' | translate" [nzCancelText]="'common.action.cancel' | translate"
      [nzOkLoading]="saving()" (nzOnOk)="saveConversion()" (nzOnCancel)="closeConversionDialog()">
      <ng-container *nzModalContent>
        @if (saveError()) { <nz-alert nzType="error" nzShowIcon data-testid="md-uom-conversion-save-error" [nzMessage]="saveError()! | translate" /> }
        <form class="uom-form" [formGroup]="conversionForm" (ngSubmit)="saveConversion()" data-testid="md-uom-conversion-form">
          <label for="uom-conversion-from">{{ 'md.uom.from' | translate }}</label>
          <select id="uom-conversion-from" data-testid="md-uom-conversion-from" formControlName="fromUomId">
            <option [ngValue]="null"></option>
            @for (uom of options(); track uom.id) {
              <option [ngValue]="uom.id" [disabled]="!uom.isActive">{{ uom.code }} — {{ uom.name }}</option>
            }
          </select>
          <label for="uom-conversion-to">{{ 'md.uom.to' | translate }}</label>
          <select id="uom-conversion-to" data-testid="md-uom-conversion-to" formControlName="toUomId">
            <option [ngValue]="null"></option>
            @for (uom of options(); track uom.id) {
              <option [ngValue]="uom.id" [disabled]="!uom.isActive">{{ uom.code }} — {{ uom.name }}</option>
            }
          </select>
          @if (conversionForm.touched && conversionForm.hasError('sameUnit')) { <p role="alert">{{ 'md.uom.sameUnit' | translate }}</p> }
          <label for="uom-conversion-factor">{{ 'md.uom.factor' | translate }}</label>
          <input nz-input id="uom-conversion-factor" data-testid="md-uom-conversion-factor" formControlName="factor" inputmode="decimal" />
          @if (conversionForm.controls.factor.touched && conversionForm.controls.factor.invalid) {
            <p role="alert">{{ 'md.uom.invalidFactor' | translate }}</p>
          }
        </form>
      </ng-container>
    </nz-modal>
  `,
  styles: `
    h2 { margin: 0; font-size: 16px; }
    header { display: flex; justify-content: space-between; align-items: center; }
    .calculator { display: flex; gap: 8px; align-items: center; flex-wrap: wrap; }
    input[erpListFilters], .calculator input { max-width: 240px; }
    .calculator [role='alert'], .uom-form p { color: var(--erp-status-error); margin: 0; }
    .uom-form { display: grid; gap: 8px; }
  `,
})
export class UomPage {
  readonly pageSizeOptions = [...PAGE_SIZE_OPTIONS];
  readonly decimalPlaceOptions = [0, 1, 2, 3, 4, 5, 6];
  private readonly api = inject(UomService);
  private readonly company = inject(CompanyContextService);
  private readonly dialogs = inject(ErpDialogService);
  private readonly listRequest = latestRequest();
  private readonly optionRequest = latestRequest();
  private readonly conversionRequest = latestRequest();
  private readonly calcRequest = latestRequest();
  private readonly companyId = computed(() => this.company.current()?.id ?? null);
  readonly rows = signal<Uom[]>([]);
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
  readonly options = signal<Uom[]>([]);
  readonly conversions = signal<UomConversion[]>([]);
  readonly conversionError = signal<string | null>(null);
  readonly dialogOpen = signal(false);
  readonly editing = signal<Uom | null>(null);
  readonly conversionDialogOpen = signal(false);
  readonly editingConversion = signal<UomConversion | null>(null);
  readonly saving = signal(false);
  readonly saveError = signal<string | null>(null);
  readonly busy = signal(false);
  readonly calcResult = signal<number | null>(null);
  readonly calcError = signal<string | null>(null);
  readonly searchControl = new FormControl('', { nonNullable: true });
  readonly form = new FormGroup({
    code: new FormControl('', { nonNullable: true, validators: [Validators.required, Validators.pattern(/^[A-Za-z0-9_-]{1,30}$/)] }),
    name: new FormControl('', { nonNullable: true, validators: [Validators.required, Validators.pattern(/.*\S.*/), Validators.maxLength(200)] }),
    decimalPlaces: new FormControl(0, { nonNullable: true }),
    isActive: new FormControl(true, { nonNullable: true }),
  });
  readonly conversionForm = new FormGroup({
    fromUomId: new FormControl<number | null>(null, Validators.required),
    toUomId: new FormControl<number | null>(null, Validators.required),
    factor: new FormControl('', { nonNullable: true, validators: [Validators.required, Validators.pattern(FACTOR_PATTERN)] }),
  }, { validators: (group) => {
    const { fromUomId, toUomId } = (group as FormGroup).getRawValue() as { fromUomId: number | null; toUomId: number | null };
    return fromUomId !== null && fromUomId === toUomId ? { sameUnit: true } : null;
  } });
  readonly calculator = new FormGroup({
    quantity: new FormControl('', { nonNullable: true, validators: [Validators.required, Validators.pattern(QUANTITY_PATTERN)] }),
    fromUomId: new FormControl<number | null>(null, Validators.required),
    toUomId: new FormControl<number | null>(null, Validators.required),
  });

  constructor() {
    effect(() => {
      this.companyId();
      untracked(() => {
        this.closeDialog();
        this.closeConversionDialog();
        this.calculator.reset({ quantity: '', fromUomId: null, toUomId: null });
        this.calcResult.set(null);
        this.calcError.set(null);
        this.page.set(1);
        this.reload();
        this.loadOptions();
        this.reloadConversions();
      });
    });
  }

  sortOrder(field: 'code' | 'name'): 'ascend' | 'descend' | null {
    return sortOrderOf(this.sort(), field);
  }

  search(): void { this.appliedKeyword.set(this.searchControl.value.trim()); this.page.set(1); this.reload(); }

  onQueryParams(params: NzTableQueryParams): void {
    const next = nextTableState(params, { page: this.page(), pageSize: this.pageSize(), sort: this.sort() }, ['code', 'name'], 'code');
    if (!next) return;
    this.page.set(next.page);
    this.pageSize.set(next.pageSize);
    this.sort.set(next.sort);
    this.reload();
  }

  /** 重新載入單位列表（目前頁）。 */
  reload(): void {
    this.loading.set(true);
    this.error.set(null); this.loadError.set(null); this.loadTraceId.set(null); this.actionTraceId.set(null);
    this.listRequest.run(this.api.list({ page: this.page(), pageSize: this.pageSize(),
      keyword: this.appliedKeyword(), sort: this.sort() }), {
      next: (result) => { this.rows.set(result.items); this.total.set(result.totalCount); this.loading.set(false); },
      error: (error: unknown) => { this.rows.set([]); this.total.set(0); this.error.set(this.api.errorKey(error));
        this.loadError.set(this.api.errorKey(error)); this.loadTraceId.set(apiErrorTraceId(error)); this.loading.set(false); },
    });
  }

  /** 換算表單與試算用的單位選項：跨頁載齊；只在切換公司及單位新增／修改／刪除後重載，翻頁不重載（MD-F02）。 */
  loadOptions(): void {
    this.optionRequest.run(fetchAllPages((page) => this.api.list({ page, pageSize: OPTION_PAGE_SIZE, sort: 'code' })), {
      next: (items) => this.options.set(items),
      error: () => this.options.set([]),
    });
  }

  reloadConversions(): void {
    this.conversionError.set(null);
    this.conversionRequest.run(this.api.listConversions(), {
      next: (items) => this.conversions.set(items),
      error: (error: unknown) => { this.conversions.set([]); this.conversionError.set(this.api.errorKey(error)); },
    });
  }

  openCreate(): void {
    this.editing.set(null);
    this.form.reset({ code: '', name: '', decimalPlaces: 0, isActive: true });
    this.form.controls.code.enable();
    this.saveError.set(null);
    this.dialogOpen.set(true);
  }

  openEdit(row: Uom): void {
    this.editing.set(row);
    this.form.reset({ code: row.code, name: row.name, decimalPlaces: row.decimalPlaces, isActive: row.isActive });
    this.form.controls.code.disable();
    this.saveError.set(null);
    this.dialogOpen.set(true);
  }

  closeDialog(): void { this.dialogOpen.set(false); this.saveError.set(null); }

  save(): void {
    if (this.saving()) return;
    this.form.markAllAsTouched();
    if (this.form.invalid) return;
    const draft = this.form.getRawValue();
    const row = this.editing();
    const request = row
      ? this.api.update(row.id, { name: draft.name, isActive: draft.isActive, decimalPlaces: draft.decimalPlaces, version: row.version })
      : this.api.create({ code: draft.code, name: draft.name, decimalPlaces: draft.decimalPlaces });
    this.saving.set(true);
    this.saveError.set(null);
    request.subscribe({
      next: () => { this.saving.set(false); this.closeDialog(); this.reload(); this.loadOptions(); },
      error: (error: unknown) => { this.saving.set(false); this.saveError.set(this.api.errorKey(error)); if (isStaleRowError(error)) this.reload(); },
    });
  }

  confirmDelete(row: Uom): void {
    if (this.busy()) return;
    this.dialogs.confirm({ titleKey: 'md.uom.deleteConfirm', contentKey: 'common.confirm.irreversible',
      params: { code: row.code }, okKey: 'common.action.delete', danger: true }).subscribe((ok) => {
      if (!ok) return;
      this.busy.set(true);
      this.api.remove(row.id, row.version).subscribe({
        next: () => { this.busy.set(false); this.reload(); this.loadOptions(); },
        error: (error: unknown) => { this.busy.set(false); if (isStaleRowError(error)) this.reload(); this.error.set(this.api.errorKey(error));
          this.actionTraceId.set(apiErrorTraceId(error)); },
      });
    });
  }

  openCreateConversion(): void {
    this.editingConversion.set(null);
    this.conversionForm.reset({ fromUomId: null, toUomId: null, factor: '' });
    this.conversionForm.controls.fromUomId.enable();
    this.conversionForm.controls.toUomId.enable();
    this.saveError.set(null);
    this.conversionDialogOpen.set(true);
  }

  openEditConversion(row: UomConversion): void {
    this.editingConversion.set(row);
    this.conversionForm.reset({ fromUomId: row.fromUomId, toUomId: row.toUomId, factor: String(row.factor) });
    this.conversionForm.controls.fromUomId.disable();
    this.conversionForm.controls.toUomId.disable();
    this.saveError.set(null);
    this.conversionDialogOpen.set(true);
  }

  closeConversionDialog(): void { this.conversionDialogOpen.set(false); this.saveError.set(null); }

  saveConversion(): void {
    if (this.saving()) return;
    this.conversionForm.markAllAsTouched();
    if (this.conversionForm.invalid) return;
    const draft = this.conversionForm.getRawValue();
    const row = this.editingConversion();
    const request = row ? this.api.updateConversion(row.id, { factor: draft.factor.trim(), version: row.version })
      : this.api.createConversion({ fromUomId: draft.fromUomId!, toUomId: draft.toUomId!, factor: draft.factor.trim() });
    this.saving.set(true);
    this.saveError.set(null);
    request.subscribe({
      next: () => { this.saving.set(false); this.closeConversionDialog(); this.reloadConversions(); },
      error: (error: unknown) => {
        this.saving.set(false); this.saveError.set(this.api.errorKey(error)); if (isStaleRowError(error)) this.reloadConversions();
      },
    });
  }

  confirmDeleteConversion(row: UomConversion): void {
    if (this.busy()) return;
    this.dialogs.confirm({ titleKey: 'md.uom.deleteConversionConfirm', contentKey: 'common.confirm.irreversible',
      params: { from: row.fromUomCode, to: row.toUomCode }, okKey: 'common.action.delete', danger: true }).subscribe((ok) => {
      if (!ok) return;
      this.busy.set(true);
      this.api.removeConversion(row.id, row.version).subscribe({
        next: () => { this.busy.set(false); this.reloadConversions(); },
        error: (error: unknown) => {
          this.busy.set(false); if (isStaleRowError(error)) this.reloadConversions(); this.conversionError.set(this.api.errorKey(error));
        },
      });
    });
  }

  /** 由後端以 decimal 換算並按目標單位小數位數捨入；前端不做數量運算。 */
  convert(): void {
    this.calculator.markAllAsTouched();
    this.calcResult.set(null);
    if (this.calculator.invalid) { this.calcError.set('md.uom.invalidCalculation'); return; }
    const draft = this.calculator.getRawValue();
    this.calcError.set(null);
    this.calcRequest.run(this.api.convert(draft.fromUomId!, draft.toUomId!, draft.quantity), {
      next: (result) => this.calcResult.set(result.result),
      error: (error: unknown) => this.calcError.set(this.api.errorKey(error)),
    });
  }
}
