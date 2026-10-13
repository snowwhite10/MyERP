import { ChangeDetectionStrategy, Component, effect, inject, input, output, signal, untracked } from '@angular/core';
import { AbstractControl, FormControl, FormGroup, ReactiveFormsModule, ValidationErrors, Validators } from '@angular/forms';
import { TranslatePipe } from '@ngx-translate/core';
import { NzAlertModule } from 'ng-zorro-antd/alert';
import { NzButtonModule } from 'ng-zorro-antd/button';
import { NzInputModule } from 'ng-zorro-antd/input';
import { NzModalModule } from 'ng-zorro-antd/modal';
import { NzTableModule, NzTableQueryParams } from 'ng-zorro-antd/table';
import { HasPermissionDirective } from '../../core/auth/has-permission.directive';
import { ErpDialogService } from '../../shared/dialogs/dialog.service';
import { latestRequest } from '../../shared/list-page/latest-request';
import { isMissingRowError, isStaleRowError } from '../../shared/list-page/stale-row';
import { TaxCode, TaxRate, TaxService } from './tax.service';

/** 稅率百分比 0–100，最多 4 位小數（`DECIMAL(7,4)`，ADR-038）。 */
export const TAX_PERCENT_PATTERN = /^(100(\.0{1,4})?|\d{1,2}(\.\d{1,4})?)$/;

function periodValidator(group: AbstractControl): ValidationErrors | null {
  const { effectiveFrom, effectiveTo } = group.value as { effectiveFrom: string; effectiveTo: string };
  return effectiveFrom && effectiveTo && effectiveTo < effectiveFrom ? { period: true } : null;
}

/**
 * 稅率期間對話框（介面 8.85，ADR-038）。查看需 `MD.TAX_CODE.VIEW`，新增／修改／刪除需 `MD.TAX_CODE.EDIT`。
 * 停用稅別只隱藏新增表單，既有稅率仍可修改或刪除。
 */
@Component({
  selector: 'erp-tax-rate-dialog',
  imports: [ReactiveFormsModule, TranslatePipe, NzAlertModule, NzButtonModule, NzInputModule,
    NzModalModule, NzTableModule, HasPermissionDirective],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <nz-modal [nzVisible]="taxCode() !== null" [nzTitle]="'md.taxRate.title' | translate: { code: taxCode()?.code }"
      [nzFooter]="null" nzWidth="760px" (nzOnCancel)="closed.emit()">
      <ng-container *nzModalContent>
        <div class="rates" data-testid="md-tax-rate-dialog">
          <p class="hint">{{ 'md.taxRate.hint' | translate }}</p>
          @if (taxCode() && !taxCode()!.isActive) {
            <nz-alert nzType="warning" nzShowIcon data-testid="md-tax-rate-inactive" [nzMessage]="'md.taxRate.inactiveTaxCode' | translate" /> }
          @if (error()) { <nz-alert nzType="error" nzShowIcon data-testid="md-tax-rate-error" [nzMessage]="error()! | translate" /> }
          <div class="row"><label for="tax-rate-on">{{ 'md.taxRate.on' | translate }}</label>
            <input nz-input id="tax-rate-on" type="date" data-testid="md-tax-rate-on" [formControl]="onControl" (change)="search()" />
            <button nz-button data-testid="md-tax-rate-clear" (click)="onControl.setValue(''); search()">{{ 'md.taxRate.all' | translate }}</button></div>
          <nz-table #table data-testid="md-tax-rate-table" nzSize="small" [nzData]="rows()" [nzLoading]="loading()"
            [nzFrontPagination]="false" [nzTotal]="total()" [nzPageIndex]="page()" [nzPageSize]="pageSize"
            [nzNoResult]="'md.taxRate.empty' | translate" (nzQueryParams)="onQueryParams($event)">
            <thead><tr><th>{{ 'md.taxRate.effectiveFrom' | translate }}</th><th>{{ 'md.taxRate.effectiveTo' | translate }}</th>
              <th>{{ 'md.taxRate.ratePercent' | translate }}</th><th>{{ 'md.taxRate.actions' | translate }}</th></tr></thead>
            <tbody>@for (row of table.data; track row.id) {
              <tr [attr.data-testid]="'md-tax-rate-row-' + row.effectiveFrom"><td>{{ row.effectiveFrom }}</td>
                <td>{{ row.effectiveTo ?? ('md.taxRate.openEnded' | translate) }}</td><td>{{ row.ratePercent }}%</td>
                <td>
                  <button nz-button nzType="link" *erpHasPermission="'MD.TAX_CODE.EDIT'"
                    [attr.data-testid]="'md-tax-rate-edit-' + row.effectiveFrom" (click)="startEdit(row)">{{ 'common.action.edit' | translate }}</button>
                  <button nz-button nzType="link" nzDanger *erpHasPermission="'MD.TAX_CODE.EDIT'" [disabled]="saving()"
                    [attr.data-testid]="'md-tax-rate-delete-' + row.effectiveFrom" (click)="confirmDelete(row)">{{ 'common.action.delete' | translate }}</button>
                </td></tr>
            }</tbody>
          </nz-table>
          @if (editing() || taxCode()?.isActive) {
            <form *erpHasPermission="'MD.TAX_CODE.EDIT'" class="row" [formGroup]="form" (ngSubmit)="save()" data-testid="md-tax-rate-form">
              <input nz-input type="date" data-testid="md-tax-rate-from" formControlName="effectiveFrom"
                [attr.aria-label]="'md.taxRate.effectiveFrom' | translate" />
              <input nz-input type="date" data-testid="md-tax-rate-to" formControlName="effectiveTo"
                [attr.aria-label]="'md.taxRate.effectiveTo' | translate" />
              <input nz-input data-testid="md-tax-rate-percent" formControlName="ratePercent" inputmode="decimal" maxlength="8"
                [attr.aria-label]="'md.taxRate.ratePercent' | translate" [placeholder]="'md.taxRate.ratePercent' | translate" />
              <button nz-button nzType="primary" type="submit" data-testid="md-tax-rate-save" [nzLoading]="saving()">
                {{ (editing() ? 'common.action.save' : 'md.taxRate.add') | translate }}</button>
              @if (editing()) { <button nz-button type="button" data-testid="md-tax-rate-cancel" (click)="resetForm()">{{ 'common.action.cancel' | translate }}</button> }
              @if (form.controls.effectiveFrom.touched && form.controls.effectiveFrom.invalid) { <p role="alert">{{ 'md.taxRate.invalidFrom' | translate }}</p> }
              @if (form.touched && form.hasError('period')) { <p role="alert" data-testid="md-tax-rate-period-error">{{ 'md.taxRate.invalidPeriod' | translate }}</p> }
              @if (form.controls.ratePercent.touched && form.controls.ratePercent.invalid) { <p role="alert" data-testid="md-tax-rate-percent-error">{{ 'md.taxRate.invalidPercent' | translate }}</p> }
            </form>
          }
        </div>
      </ng-container>
    </nz-modal>
  `,
  styles: `
    .rates { display: grid; gap: 12px; color: var(--erp-color-text); }
    .hint { margin: 0; color: var(--erp-color-text-secondary); }
    .row { display: flex; gap: 8px; align-items: center; flex-wrap: wrap; }
    .row input[nz-input] { max-width: 180px; }
    .row p { color: var(--erp-status-error); margin: 0; flex-basis: 100%; }
  `,
})
export class TaxRateDialog {
  readonly taxCode = input<TaxCode | null>(null);
  readonly closed = output<void>();
  /** 新增、修改或刪除稅率後通知列表重新載入今日稅率。 */
  readonly changed = output<void>();
  readonly pageSize = 10;
  private readonly api = inject(TaxService);
  private readonly dialogs = inject(ErpDialogService);
  private readonly listRequest = latestRequest();
  readonly rows = signal<TaxRate[]>([]);
  readonly total = signal(0);
  readonly page = signal(1);
  readonly loading = signal(false);
  readonly error = signal<string | null>(null);
  readonly editing = signal<TaxRate | null>(null);
  readonly saving = signal(false);
  readonly onControl = new FormControl('', { nonNullable: true });
  readonly form = new FormGroup({
    effectiveFrom: new FormControl('', { nonNullable: true, validators: [Validators.required] }),
    effectiveTo: new FormControl('', { nonNullable: true }),
    ratePercent: new FormControl('', { nonNullable: true, validators: [Validators.required, Validators.pattern(TAX_PERCENT_PATTERN)] }),
  }, { validators: periodValidator });

  constructor() {
    effect(() => {
      const taxCode = this.taxCode();
      untracked(() => {
        this.resetForm(); this.onControl.setValue(''); this.page.set(1);
        this.rows.set([]); this.total.set(0); this.error.set(null);
        if (taxCode) this.reload();
      });
    });
  }

  search(): void { this.page.set(1); this.reload(); }

  onQueryParams(params: NzTableQueryParams): void {
    if (params.pageIndex === this.page()) return;
    this.page.set(params.pageIndex); this.reload();
  }

  reload(): void {
    const taxCode = this.taxCode();
    if (!taxCode) return;
    this.loading.set(true); this.error.set(null);
    this.listRequest.run(this.api.listRates(taxCode.id,
      { page: this.page(), pageSize: this.pageSize, sort: '-effectiveFrom', on: this.onControl.value || null }), {
      next: (result) => { this.rows.set(result.items); this.total.set(result.totalCount); this.loading.set(false); },
      error: (error: unknown) => { this.rows.set([]); this.total.set(0); this.error.set(this.api.errorKey(error)); this.loading.set(false); },
    });
  }

  startEdit(row: TaxRate): void {
    this.editing.set(row);
    this.form.reset({ effectiveFrom: row.effectiveFrom, effectiveTo: row.effectiveTo ?? '', ratePercent: String(row.ratePercent) });
  }

  resetForm(): void {
    this.editing.set(null);
    this.form.reset({ effectiveFrom: '', effectiveTo: '', ratePercent: '' });
  }

  save(): void {
    const taxCode = this.taxCode();
    if (!taxCode || this.saving()) return;
    this.form.markAllAsTouched(); if (this.form.invalid) return;
    const draft = this.form.getRawValue(); const row = this.editing();
    const body = { effectiveFrom: draft.effectiveFrom, effectiveTo: draft.effectiveTo || null, ratePercent: Number(draft.ratePercent) };
    const request = row ? this.api.updateRate(taxCode.id, row.id, { ...body, version: row.version })
      : this.api.createRate(taxCode.id, body);
    this.saving.set(true); this.error.set(null);
    request.subscribe({
      next: () => { this.saving.set(false); this.resetForm(); this.reload(); this.changed.emit(); },
      error: (error: unknown) => this.failed(error),
    });
  }

  confirmDelete(row: TaxRate): void {
    const taxCode = this.taxCode();
    if (!taxCode || this.saving()) return;
    this.dialogs.confirm({ titleKey: 'md.taxRate.deleteConfirm', contentKey: 'common.confirm.irreversible',
      params: { date: row.effectiveFrom }, okKey: 'common.action.delete', danger: true }).subscribe((ok) => {
      if (!ok) return;
      this.saving.set(true); this.error.set(null);
      this.api.removeRate(taxCode.id, row.id, row.version).subscribe({
        next: () => {
          this.saving.set(false); if (this.editing()?.id === row.id) this.resetForm();
          this.reload(); this.changed.emit();
        },
        error: (error: unknown) => this.failed(error),
      });
    });
  }

  /** 資料已被刪除（404）或被別人修改（409）時重新查詢，避免一直帶舊版本重試（MD-F06）；404 時放棄編輯中的稅率。 */
  private failed(error: unknown): void {
    this.saving.set(false);
    if (isStaleRowError(error)) {
      if (isMissingRowError(error)) this.resetForm();
      this.reload();
    }
    this.error.set(this.api.errorKey(error));
  }
}
