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
import { Currency, CurrencyService, ExchangeRate } from './currency.service';

/** 前端限 6 位整數、8 位小數：14 位有效數字內，JSON 數值不失真（後端允許 11 位整數）。 */
export const RATE_PATTERN = /^\d{1,6}(\.\d{1,8})?$/;

function periodValidator(group: AbstractControl): ValidationErrors | null {
  const { effectiveFrom, effectiveTo } = group.value as { effectiveFrom: string; effectiveTo: string };
  return effectiveFrom && effectiveTo && effectiveTo < effectiveFrom ? { period: true } : null;
}

/**
 * 外幣匯率對話框（介面 8.82，ADR-037）：1 外幣 = 匯率 本位幣。查看需 `MD.CURRENCY.VIEW`，
 * 新增／修改／刪除需 `MD.CURRENCY.EDIT`。停用幣別只隱藏新增表單，既有匯率仍可修改或刪除。
 */
@Component({
  selector: 'erp-exchange-rate-dialog',
  imports: [ReactiveFormsModule, TranslatePipe, NzAlertModule, NzButtonModule, NzInputModule,
    NzModalModule, NzTableModule, HasPermissionDirective],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <nz-modal [nzVisible]="currency() !== null" [nzTitle]="'md.exchangeRate.title' | translate: { code: currency()?.code }"
      [nzFooter]="null" nzWidth="760px" (nzOnCancel)="closed.emit()">
      <ng-container *nzModalContent>
        <div class="rates" data-testid="md-rate-dialog">
          <p class="hint">{{ 'md.exchangeRate.hint' | translate: { code: currency()?.code } }}</p>
          @if (currency() && !currency()!.isActive) {
            <nz-alert nzType="warning" nzShowIcon data-testid="md-rate-inactive" [nzMessage]="'md.exchangeRate.inactiveCurrency' | translate" /> }
          @if (error()) { <nz-alert nzType="error" nzShowIcon data-testid="md-rate-error" [nzMessage]="error()! | translate" /> }
          <div class="row"><label for="rate-on">{{ 'md.exchangeRate.on' | translate }}</label>
            <input nz-input id="rate-on" type="date" data-testid="md-rate-on" [formControl]="onControl" (change)="search()" />
            <button nz-button data-testid="md-rate-clear" (click)="onControl.setValue(''); search()">{{ 'md.exchangeRate.all' | translate }}</button></div>
          <nz-table #table data-testid="md-rate-table" nzSize="small" [nzData]="rows()" [nzLoading]="loading()"
            [nzFrontPagination]="false" [nzTotal]="total()" [nzPageIndex]="page()" [nzPageSize]="pageSize"
            [nzNoResult]="'md.exchangeRate.empty' | translate" (nzQueryParams)="onQueryParams($event)">
            <thead><tr><th>{{ 'md.exchangeRate.effectiveFrom' | translate }}</th><th>{{ 'md.exchangeRate.effectiveTo' | translate }}</th>
              <th>{{ 'md.exchangeRate.rate' | translate }}</th><th>{{ 'md.exchangeRate.actions' | translate }}</th></tr></thead>
            <tbody>@for (row of table.data; track row.id) {
              <tr [attr.data-testid]="'md-rate-row-' + row.effectiveFrom"><td>{{ row.effectiveFrom }}</td>
                <td>{{ row.effectiveTo ?? ('md.exchangeRate.openEnded' | translate) }}</td><td>{{ row.rate }}</td>
                <td>
                  <button nz-button nzType="link" *erpHasPermission="'MD.CURRENCY.EDIT'"
                    [attr.data-testid]="'md-rate-edit-' + row.effectiveFrom" (click)="startEdit(row)">{{ 'common.action.edit' | translate }}</button>
                  <button nz-button nzType="link" nzDanger *erpHasPermission="'MD.CURRENCY.EDIT'" [disabled]="saving()"
                    [attr.data-testid]="'md-rate-delete-' + row.effectiveFrom" (click)="confirmDelete(row)">{{ 'common.action.delete' | translate }}</button>
                </td></tr>
            }</tbody>
          </nz-table>
          @if (editing() || currency()?.isActive) {
            <form *erpHasPermission="'MD.CURRENCY.EDIT'" class="row" [formGroup]="form" (ngSubmit)="save()" data-testid="md-rate-form">
              <input nz-input type="date" data-testid="md-rate-from" formControlName="effectiveFrom"
                [attr.aria-label]="'md.exchangeRate.effectiveFrom' | translate" />
              <input nz-input type="date" data-testid="md-rate-to" formControlName="effectiveTo"
                [attr.aria-label]="'md.exchangeRate.effectiveTo' | translate" />
              <input nz-input data-testid="md-rate-rate" formControlName="rate" inputmode="decimal" maxlength="15"
                [attr.aria-label]="'md.exchangeRate.rate' | translate" [placeholder]="'md.exchangeRate.rate' | translate" />
              <button nz-button nzType="primary" type="submit" data-testid="md-rate-save" [nzLoading]="saving()">
                {{ (editing() ? 'common.action.save' : 'md.exchangeRate.add') | translate }}</button>
              @if (editing()) { <button nz-button type="button" data-testid="md-rate-cancel" (click)="resetForm()">{{ 'common.action.cancel' | translate }}</button> }
              @if (form.controls.effectiveFrom.touched && form.controls.effectiveFrom.invalid) { <p role="alert">{{ 'md.exchangeRate.invalidFrom' | translate }}</p> }
              @if (form.touched && form.hasError('period')) { <p role="alert" data-testid="md-rate-period-error">{{ 'md.exchangeRate.invalidPeriod' | translate }}</p> }
              @if (form.controls.rate.touched && form.controls.rate.invalid) { <p role="alert" data-testid="md-rate-rate-error">{{ 'md.exchangeRate.invalidRate' | translate }}</p> }
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
export class ExchangeRateDialog {
  readonly currency = input<Currency | null>(null);
  readonly closed = output<void>();
  readonly pageSize = 10;
  private readonly api = inject(CurrencyService);
  private readonly dialogs = inject(ErpDialogService);
  private readonly listRequest = latestRequest();
  readonly rows = signal<ExchangeRate[]>([]);
  readonly total = signal(0);
  readonly page = signal(1);
  readonly loading = signal(false);
  readonly error = signal<string | null>(null);
  readonly editing = signal<ExchangeRate | null>(null);
  readonly saving = signal(false);
  readonly onControl = new FormControl('', { nonNullable: true });
  readonly form = new FormGroup({
    effectiveFrom: new FormControl('', { nonNullable: true, validators: [Validators.required] }),
    effectiveTo: new FormControl('', { nonNullable: true }),
    rate: new FormControl('', { nonNullable: true, validators: [Validators.required, Validators.pattern(RATE_PATTERN),
      (c: AbstractControl) => Number(c.value) > 0 || !c.value ? null : { positive: true }] }),
  }, { validators: periodValidator });

  constructor() {
    effect(() => {
      const currency = this.currency();
      untracked(() => {
        this.resetForm(); this.onControl.setValue(''); this.page.set(1);
        this.rows.set([]); this.total.set(0); this.error.set(null);
        if (currency) this.reload();
      });
    });
  }

  search(): void { this.page.set(1); this.reload(); }

  onQueryParams(params: NzTableQueryParams): void {
    if (params.pageIndex === this.page()) return;
    this.page.set(params.pageIndex); this.reload();
  }

  reload(): void {
    const currency = this.currency();
    if (!currency) return;
    this.loading.set(true); this.error.set(null);
    this.listRequest.run(this.api.listRates(currency.id,
      { page: this.page(), pageSize: this.pageSize, sort: '-effectiveFrom', on: this.onControl.value || null }), {
      next: (result) => { this.rows.set(result.items); this.total.set(result.totalCount); this.loading.set(false); },
      error: (error: unknown) => { this.rows.set([]); this.total.set(0); this.error.set(this.api.errorKey(error)); this.loading.set(false); },
    });
  }

  startEdit(row: ExchangeRate): void {
    this.editing.set(row);
    this.form.reset({ effectiveFrom: row.effectiveFrom, effectiveTo: row.effectiveTo ?? '', rate: String(row.rate) });
  }

  resetForm(): void {
    this.editing.set(null);
    this.form.reset({ effectiveFrom: '', effectiveTo: '', rate: '' });
  }

  save(): void {
    const currency = this.currency();
    if (!currency || this.saving()) return;
    this.form.markAllAsTouched(); if (this.form.invalid) return;
    const draft = this.form.getRawValue(); const row = this.editing();
    const body = { effectiveFrom: draft.effectiveFrom, effectiveTo: draft.effectiveTo || null, rate: Number(draft.rate) };
    const request = row ? this.api.updateRate(currency.id, row.id, { ...body, version: row.version })
      : this.api.createRate(currency.id, body);
    this.saving.set(true); this.error.set(null);
    request.subscribe({
      next: () => { this.saving.set(false); this.resetForm(); this.reload(); },
      error: (error: unknown) => { this.saving.set(false); this.error.set(this.api.errorKey(error)); },
    });
  }

  confirmDelete(row: ExchangeRate): void {
    const currency = this.currency();
    if (!currency || this.saving()) return;
    this.dialogs.confirm({ titleKey: 'md.exchangeRate.deleteConfirm', contentKey: 'common.confirm.irreversible',
      params: { date: row.effectiveFrom }, okKey: 'common.action.delete', danger: true }).subscribe((ok) => {
      if (!ok) return;
      this.saving.set(true); this.error.set(null);
      this.api.removeRate(currency.id, row.id, row.version).subscribe({
        next: () => { this.saving.set(false); if (this.editing()?.id === row.id) this.resetForm(); this.reload(); },
        error: (error: unknown) => { this.saving.set(false); this.error.set(this.api.errorKey(error)); },
      });
    });
  }
}
