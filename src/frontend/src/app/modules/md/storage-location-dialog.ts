import { ChangeDetectionStrategy, Component, effect, inject, input, output, signal, untracked } from '@angular/core';
import { FormControl, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { TranslatePipe } from '@ngx-translate/core';
import { NzAlertModule } from 'ng-zorro-antd/alert';
import { NzButtonModule } from 'ng-zorro-antd/button';
import { NzInputModule } from 'ng-zorro-antd/input';
import { NzModalModule } from 'ng-zorro-antd/modal';
import { NzTableModule, NzTableQueryParams } from 'ng-zorro-antd/table';
import { NzTagModule } from 'ng-zorro-antd/tag';
import { HasPermissionDirective } from '../../core/auth/has-permission.directive';
import { ErpDialogService } from '../../shared/dialogs/dialog.service';
import { latestRequest } from '../../shared/list-page/latest-request';
import { isMissingRowError, isStaleRowError } from '../../shared/list-page/stale-row';
import { StorageLocation, Warehouse, WarehouseService } from './warehouse.service';

/**
 * 倉庫儲位對話框（介面 8.78，ADR-036）：查看需 `MD.WAREHOUSE.VIEW`，新增／修改／刪除需 `MD.WAREHOUSE.EDIT`。
 * 停用倉庫只隱藏新增表單，既有儲位仍可修改或刪除；後端仍檢查並回 WAREHOUSE_INACTIVE。
 */
@Component({
  selector: 'erp-storage-location-dialog',
  imports: [ReactiveFormsModule, TranslatePipe, NzAlertModule, NzButtonModule, NzInputModule,
    NzModalModule, NzTableModule, NzTagModule, HasPermissionDirective],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <nz-modal [nzVisible]="warehouse() !== null" [nzTitle]="'md.storageLocation.title' | translate: { code: warehouse()?.code }"
      [nzFooter]="null" nzWidth="720px" (nzOnCancel)="closed.emit()">
      <ng-container *nzModalContent>
        <div class="locations" data-testid="md-location-dialog">
          <p class="hint">{{ 'md.storageLocation.hint' | translate }}</p>
          @if (warehouse() && !warehouse()!.isActive) {
            <nz-alert nzType="warning" nzShowIcon data-testid="md-location-inactive" [nzMessage]="'md.storageLocation.inactiveWarehouse' | translate" /> }
          @if (error()) { <nz-alert nzType="error" nzShowIcon data-testid="md-location-error" [nzMessage]="error()! | translate" /> }
          <div class="row"><input nz-input type="search" data-testid="md-location-search" [formControl]="searchControl"
            [placeholder]="'md.storageLocation.search' | translate" (keyup.enter)="search()" />
            <button nz-button data-testid="md-location-search-button" (click)="search()">{{ 'common.action.search' | translate }}</button></div>
          <nz-table #table data-testid="md-location-table" nzSize="small" [nzData]="rows()" [nzLoading]="loading()"
            [nzFrontPagination]="false" [nzTotal]="total()" [nzPageIndex]="page()" [nzPageSize]="pageSize"
            [nzNoResult]="'md.storageLocation.empty' | translate" (nzQueryParams)="onQueryParams($event)">
            <thead><tr><th>{{ 'md.storageLocation.code' | translate }}</th><th>{{ 'md.storageLocation.name' | translate }}</th>
              <th>{{ 'md.storageLocation.status' | translate }}</th><th>{{ 'md.storageLocation.actions' | translate }}</th></tr></thead>
            <tbody>@for (row of table.data; track row.id) {
              <tr [attr.data-testid]="'md-location-row-' + row.code"><td>{{ row.code }}</td><td>{{ row.name }}</td>
                <td><nz-tag [nzColor]="row.isActive ? 'success' : 'default'">{{ (row.isActive ? 'md.storageLocation.active' : 'md.storageLocation.inactive') | translate }}</nz-tag></td>
                <td>
                  <button nz-button nzType="link" *erpHasPermission="'MD.WAREHOUSE.EDIT'"
                    [attr.data-testid]="'md-location-edit-' + row.code" (click)="startEdit(row)">{{ 'common.action.edit' | translate }}</button>
                  <button nz-button nzType="link" nzDanger *erpHasPermission="'MD.WAREHOUSE.EDIT'" [disabled]="saving()"
                    [attr.data-testid]="'md-location-delete-' + row.code" (click)="confirmDelete(row)">{{ 'common.action.delete' | translate }}</button>
                </td></tr>
            }</tbody>
          </nz-table>
          @if (editing() || warehouse()?.isActive) {
            <form *erpHasPermission="'MD.WAREHOUSE.EDIT'" class="row" [formGroup]="form" (ngSubmit)="save()" data-testid="md-location-form">
              <input nz-input data-testid="md-location-code" formControlName="code" maxlength="30"
                [attr.aria-label]="'md.storageLocation.code' | translate" [placeholder]="'md.storageLocation.code' | translate" />
              <input nz-input data-testid="md-location-name" formControlName="name" maxlength="200"
                [attr.aria-label]="'md.storageLocation.name' | translate" [placeholder]="'md.storageLocation.name' | translate" />
              @if (editing()) { <label><input type="checkbox" formControlName="isActive" data-testid="md-location-active" />{{ 'md.storageLocation.active' | translate }}</label> }
              <button nz-button nzType="primary" type="submit" data-testid="md-location-save" [nzLoading]="saving()">
                {{ (editing() ? 'common.action.save' : 'md.storageLocation.add') | translate }}</button>
              @if (editing()) { <button nz-button type="button" data-testid="md-location-cancel" (click)="resetForm()">{{ 'common.action.cancel' | translate }}</button> }
              @if (form.controls.code.touched && form.controls.code.invalid) { <p role="alert">{{ 'md.storageLocation.invalidCode' | translate }}</p> }
              @if (form.controls.name.touched && form.controls.name.invalid) { <p role="alert">{{ 'md.storageLocation.invalidName' | translate }}</p> }
            </form>
          }
        </div>
      </ng-container>
    </nz-modal>
  `,
  styles: `
    .locations { display: grid; gap: 12px; color: var(--erp-color-text); }
    .hint { margin: 0; color: var(--erp-color-text-secondary); }
    .row { display: flex; gap: 8px; align-items: center; flex-wrap: wrap; }
    .row input[nz-input] { max-width: 220px; }
    .row p { color: var(--erp-status-error); margin: 0; flex-basis: 100%; }
  `,
})
export class StorageLocationDialog {
  readonly warehouse = input<Warehouse | null>(null);
  readonly closed = output<void>();
  readonly pageSize = 10;
  private readonly api = inject(WarehouseService);
  private readonly dialogs = inject(ErpDialogService);
  private readonly listRequest = latestRequest();
  readonly rows = signal<StorageLocation[]>([]);
  readonly total = signal(0);
  readonly page = signal(1);
  readonly loading = signal(false);
  readonly error = signal<string | null>(null);
  readonly editing = signal<StorageLocation | null>(null);
  readonly saving = signal(false);
  readonly searchControl = new FormControl('', { nonNullable: true });
  readonly form = new FormGroup({
    code: new FormControl('', { nonNullable: true, validators: [Validators.required, Validators.pattern(/^[A-Za-z0-9_-]{1,30}$/)] }),
    name: new FormControl('', { nonNullable: true, validators: [Validators.required, Validators.pattern(/.*\S.*/), Validators.maxLength(200)] }),
    isActive: new FormControl(true, { nonNullable: true }),
  });

  constructor() {
    effect(() => {
      const warehouse = this.warehouse();
      untracked(() => {
        this.resetForm(); this.searchControl.setValue(''); this.page.set(1);
        this.rows.set([]); this.total.set(0); this.error.set(null);
        if (warehouse) this.reload();
      });
    });
  }

  search(): void { this.page.set(1); this.reload(); }

  onQueryParams(params: NzTableQueryParams): void {
    if (params.pageIndex === this.page()) return;
    this.page.set(params.pageIndex); this.reload();
  }

  reload(): void {
    const warehouse = this.warehouse();
    if (!warehouse) return;
    this.loading.set(true); this.error.set(null);
    this.listRequest.run(this.api.listLocations(warehouse.id,
      { page: this.page(), pageSize: this.pageSize, keyword: this.searchControl.value, sort: 'code' }), {
      next: (result) => { this.rows.set(result.items); this.total.set(result.totalCount); this.loading.set(false); },
      error: (error: unknown) => { this.rows.set([]); this.total.set(0); this.error.set(this.api.errorKey(error)); this.loading.set(false); },
    });
  }

  startEdit(row: StorageLocation): void {
    this.editing.set(row);
    this.form.reset({ code: row.code, name: row.name, isActive: row.isActive });
    this.form.controls.code.disable();
  }

  resetForm(): void {
    this.editing.set(null);
    this.form.reset({ code: '', name: '', isActive: true });
    this.form.controls.code.enable();
  }

  save(): void {
    const warehouse = this.warehouse();
    if (!warehouse || this.saving()) return;
    this.form.markAllAsTouched(); if (this.form.invalid) return;
    const draft = this.form.getRawValue(); const row = this.editing();
    const request = row
      ? this.api.updateLocation(warehouse.id, row.id, { name: draft.name, isActive: draft.isActive, version: row.version })
      : this.api.createLocation(warehouse.id, { code: draft.code, name: draft.name });
    this.saving.set(true); this.error.set(null);
    request.subscribe({
      next: () => { this.saving.set(false); this.resetForm(); this.reload(); },
      error: (error: unknown) => this.failed(error),
    });
  }

  confirmDelete(row: StorageLocation): void {
    const warehouse = this.warehouse();
    if (!warehouse || this.saving()) return;
    this.dialogs.confirm({ titleKey: 'md.storageLocation.deleteConfirm', contentKey: 'common.confirm.irreversible',
      params: { code: row.code }, okKey: 'common.action.delete', danger: true }).subscribe((ok) => {
      if (!ok) return;
      this.saving.set(true); this.error.set(null);
      this.api.removeLocation(warehouse.id, row.id, row.version).subscribe({
        next: () => { this.saving.set(false); if (this.editing()?.id === row.id) this.resetForm(); this.reload(); },
        error: (error: unknown) => this.failed(error),
      });
    });
  }

  /** 資料已被刪除（404）或被別人修改（409）時重新查詢，避免一直帶舊版本重試（MD-F06）；404 時放棄編輯中的儲位。 */
  private failed(error: unknown): void {
    this.saving.set(false);
    if (isStaleRowError(error)) {
      if (isMissingRowError(error)) this.resetForm();
      this.reload();
    }
    this.error.set(this.api.errorKey(error));
  }
}
