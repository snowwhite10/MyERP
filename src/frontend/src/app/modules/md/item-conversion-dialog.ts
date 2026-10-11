import { ChangeDetectionStrategy, Component, computed, effect, inject, input, output, signal, untracked } from '@angular/core';
import { FormControl, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { TranslatePipe } from '@ngx-translate/core';
import { NzAlertModule } from 'ng-zorro-antd/alert';
import { NzButtonModule } from 'ng-zorro-antd/button';
import { NzInputModule } from 'ng-zorro-antd/input';
import { NzModalModule } from 'ng-zorro-antd/modal';
import { NzTableModule } from 'ng-zorro-antd/table';
import { HasPermissionDirective } from '../../core/auth/has-permission.directive';
import { ErpDialogService } from '../../shared/dialogs/dialog.service';
import { latestRequest } from '../../shared/list-page/latest-request';
import { Item, ItemService, ItemUomConversion } from './item.service';
import { FACTOR_PATTERN, QUANTITY_PATTERN, Uom } from './uom.service';

/**
 * 物料專屬換算對話框（介面 8.77，ADR-035）：1 個換算單位 = 係數 個基本單位，覆寫同一單位的公司換算。
 * 查看與試算需 `MD.ITEM.VIEW`，新增／修改／刪除需 `MD.ITEM.EDIT`；數量運算一律交由後端。
 */
@Component({
  selector: 'erp-item-conversion-dialog',
  imports: [ReactiveFormsModule, TranslatePipe, NzAlertModule, NzButtonModule, NzInputModule,
    NzModalModule, NzTableModule, HasPermissionDirective],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <nz-modal [nzVisible]="item() !== null" [nzTitle]="'md.itemConversion.title' | translate: { code: item()?.code }"
      [nzFooter]="null" nzWidth="720px" (nzOnCancel)="closed.emit()">
      <ng-container *nzModalContent>
        <div class="item-conversions" data-testid="md-item-conversion-dialog">
          <p class="hint">{{ 'md.itemConversion.hint' | translate }}</p>
          @if (error()) { <nz-alert nzType="error" nzShowIcon data-testid="md-item-conversion-error" [nzMessage]="error()! | translate" /> }
          <nz-table #table data-testid="md-item-conversion-table" nzSize="small" [nzData]="rows()" [nzLoading]="loading()"
            [nzShowPagination]="false" [nzPageSize]="rows().length || 1" [nzNoResult]="'md.itemConversion.empty' | translate">
            <thead><tr><th>{{ 'md.itemConversion.unit' | translate }}</th><th>{{ 'md.itemConversion.factor' | translate }}</th>
              <th>{{ 'md.itemConversion.actions' | translate }}</th></tr></thead>
            <tbody>@for (row of table.data; track row.id) {
              <tr [attr.data-testid]="'md-item-conversion-row-' + row.uomCode"><td>{{ row.uomCode }}</td>
                <td>{{ 'md.itemConversion.rule' | translate: { unit: row.uomCode, factor: row.factor, base: baseCode() } }}</td>
                <td>
                  <button nz-button nzType="link" *erpHasPermission="'MD.ITEM.EDIT'"
                    [attr.data-testid]="'md-item-conversion-edit-' + row.uomCode" (click)="startEdit(row)">{{ 'common.action.edit' | translate }}</button>
                  <button nz-button nzType="link" nzDanger *erpHasPermission="'MD.ITEM.EDIT'" [disabled]="saving()"
                    [attr.data-testid]="'md-item-conversion-delete-' + row.uomCode" (click)="confirmDelete(row)">{{ 'common.action.delete' | translate }}</button>
                </td></tr>
            }</tbody>
          </nz-table>
          <form *erpHasPermission="'MD.ITEM.EDIT'" class="row" [formGroup]="form" (ngSubmit)="save()" data-testid="md-item-conversion-form">
            <select data-testid="md-item-conversion-unit" formControlName="uomId" [attr.aria-label]="'md.itemConversion.unit' | translate">
              <option [ngValue]="null">{{ 'md.itemConversion.selectUnit' | translate }}</option>
              @for (uom of unitOptions(); track uom.id) {
                <option [ngValue]="uom.id" [disabled]="!uom.isActive && uom.id !== editing()?.uomId">{{ uom.code }} — {{ uom.name }}</option>
              }
            </select>
            <span>= </span>
            <input nz-input data-testid="md-item-conversion-factor" formControlName="factor" inputmode="decimal"
              [attr.aria-label]="'md.itemConversion.factor' | translate" [placeholder]="'md.itemConversion.factor' | translate" />
            <span>{{ baseCode() }}</span>
            <button nz-button nzType="primary" type="submit" data-testid="md-item-conversion-save" [nzLoading]="saving()">
              {{ (editing() ? 'common.action.save' : 'md.itemConversion.add') | translate }}</button>
            @if (editing()) { <button nz-button type="button" data-testid="md-item-conversion-cancel" (click)="resetForm()">{{ 'common.action.cancel' | translate }}</button> }
            @if (form.controls.factor.touched && form.controls.factor.invalid) { <p role="alert">{{ 'md.itemConversion.invalidFactor' | translate }}</p> }
          </form>
          <h3>{{ 'md.itemConversion.calculator' | translate }}</h3>
          <form class="row" [formGroup]="calculator" (ngSubmit)="convert()" data-testid="md-item-conversion-calculator">
            <input nz-input data-testid="md-item-conversion-calc-quantity" formControlName="quantity"
              [attr.aria-label]="'md.itemConversion.quantity' | translate" [placeholder]="'md.itemConversion.quantity' | translate" />
            <select data-testid="md-item-conversion-calc-from" formControlName="fromUomId" [attr.aria-label]="'md.itemConversion.from' | translate">
              <option [ngValue]="null">{{ 'md.itemConversion.from' | translate }}</option>
              @for (uom of uoms(); track uom.id) { <option [ngValue]="uom.id">{{ uom.code }}</option> }
            </select>
            <select data-testid="md-item-conversion-calc-to" formControlName="toUomId" [attr.aria-label]="'md.itemConversion.to' | translate">
              <option [ngValue]="null">{{ 'md.itemConversion.to' | translate }}</option>
              @for (uom of uoms(); track uom.id) { <option [ngValue]="uom.id">{{ uom.code }}</option> }
            </select>
            <button nz-button type="submit" data-testid="md-item-conversion-calc-convert">{{ 'md.itemConversion.convert' | translate }}</button>
            @if (calcResult() !== null) { <strong data-testid="md-item-conversion-calc-result">{{ calcResult() }}</strong> }
            @if (calcError()) { <span role="alert" data-testid="md-item-conversion-calc-error">{{ calcError()! | translate }}</span> }
          </form>
        </div>
      </ng-container>
    </nz-modal>
  `,
  styles: `
    .item-conversions { display: grid; gap: 12px; color: var(--erp-color-text); }
    .hint { margin: 0; color: var(--erp-color-text-secondary); }
    h3 { margin: 0; font-size: 14px; }
    .row { display: flex; gap: 8px; align-items: center; flex-wrap: wrap; }
    .row input { max-width: 200px; }
    .row p, .row [role='alert'] { color: var(--erp-status-error); margin: 0; flex-basis: 100%; }
  `,
})
export class ItemConversionDialog {
  readonly item = input<Item | null>(null);
  readonly uoms = input<Uom[]>([]);
  readonly closed = output<void>();
  private readonly api = inject(ItemService);
  private readonly dialogs = inject(ErpDialogService);
  private readonly listRequest = latestRequest();
  private readonly calcRequest = latestRequest();
  readonly rows = signal<ItemUomConversion[]>([]);
  readonly loading = signal(false);
  readonly error = signal<string | null>(null);
  readonly editing = signal<ItemUomConversion | null>(null);
  readonly saving = signal(false);
  readonly calcResult = signal<number | null>(null);
  readonly calcError = signal<string | null>(null);
  readonly baseCode = computed(() => this.uoms().find((x) => x.id === this.item()?.baseUomId)?.code ?? '');
  /** 可設定換算的單位：排除基本單位。 */
  readonly unitOptions = computed(() => this.uoms().filter((x) => x.id !== this.item()?.baseUomId));
  readonly form = new FormGroup({
    uomId: new FormControl<number | null>(null, Validators.required),
    factor: new FormControl('', { nonNullable: true, validators: [Validators.required, Validators.pattern(FACTOR_PATTERN)] }),
  });
  readonly calculator = new FormGroup({
    quantity: new FormControl('', { nonNullable: true, validators: [Validators.required, Validators.pattern(QUANTITY_PATTERN)] }),
    fromUomId: new FormControl<number | null>(null, Validators.required),
    toUomId: new FormControl<number | null>(null, Validators.required),
  });

  constructor() {
    effect(() => {
      const item = this.item();
      untracked(() => {
        this.resetForm();
        this.calculator.reset({ quantity: '', fromUomId: null, toUomId: null });
        this.calcResult.set(null); this.calcError.set(null); this.rows.set([]);
        if (item) this.reload(item.id);
      });
    });
  }

  reload(itemId: number): void {
    this.loading.set(true); this.error.set(null);
    this.listRequest.run(this.api.listConversions(itemId), {
      next: (rows) => { this.rows.set(rows); this.loading.set(false); },
      error: (error: unknown) => { this.rows.set([]); this.error.set(this.api.errorKey(error)); this.loading.set(false); },
    });
  }

  startEdit(row: ItemUomConversion): void {
    this.editing.set(row);
    this.form.reset({ uomId: row.uomId, factor: String(row.factor) });
    this.form.controls.uomId.disable();
  }

  resetForm(): void {
    this.editing.set(null);
    this.form.reset({ uomId: null, factor: '' });
    this.form.controls.uomId.enable();
  }

  save(): void {
    const item = this.item();
    if (!item || this.saving()) return;
    this.form.markAllAsTouched(); if (this.form.invalid) return;
    const draft = this.form.getRawValue(); const row = this.editing();
    const request = row ? this.api.updateConversion(item.id, row.id, { factor: draft.factor, version: row.version })
      : this.api.createConversion(item.id, { uomId: draft.uomId!, factor: draft.factor });
    this.saving.set(true); this.error.set(null);
    request.subscribe({
      next: () => { this.saving.set(false); this.resetForm(); this.reload(item.id); },
      error: (error: unknown) => { this.saving.set(false); this.error.set(this.api.errorKey(error)); },
    });
  }

  confirmDelete(row: ItemUomConversion): void {
    const item = this.item();
    if (!item || this.saving()) return;
    this.dialogs.confirm({ titleKey: 'md.itemConversion.deleteConfirm', contentKey: 'common.confirm.irreversible',
      params: { unit: row.uomCode }, okKey: 'common.action.delete', danger: true }).subscribe((ok) => {
      if (!ok) return;
      this.saving.set(true); this.error.set(null);
      this.api.removeConversion(item.id, row.id, row.version).subscribe({
        next: () => { this.saving.set(false); if (this.editing()?.id === row.id) this.resetForm(); this.reload(item.id); },
        error: (error: unknown) => { this.saving.set(false); this.error.set(this.api.errorKey(error)); },
      });
    });
  }

  convert(): void {
    const item = this.item();
    this.calculator.markAllAsTouched(); this.calcResult.set(null);
    if (!item || this.calculator.invalid) { this.calcError.set('md.itemConversion.invalidCalculation'); return; }
    const draft = this.calculator.getRawValue();
    this.calcError.set(null);
    this.calcRequest.run(this.api.convert(item.id, draft.fromUomId!, draft.toUomId!, draft.quantity), {
      next: (result) => this.calcResult.set(result.result),
      error: (error: unknown) => this.calcError.set(this.api.errorKey(error)),
    });
  }
}
