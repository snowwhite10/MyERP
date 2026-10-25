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
import { StorageLocationDialog } from './storage-location-dialog';
import { Warehouse, WarehouseService } from './warehouse.service';

/** 倉庫維護（介面 8.17–8.19）與儲位（介面 8.78，ADR-036）；庫存數量不在此頁。 */
@Component({
  selector: 'erp-warehouse-page',
  imports: [ReactiveFormsModule, TranslatePipe, NzAlertModule, NzButtonModule, NzInputModule,
    NzModalModule, NzTableModule, NzTagModule, HasPermissionDirective, StorageLocationDialog, ListPage],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <erp-list-page titleKey="md.warehouse.title" testId="md-warehouse" [resettable]="false"
      [errorKey]="loadError()" [errorTraceId]="loadTraceId()" (querySubmit)="search()" (retryLoad)="reload()">
      <button erpListActions nz-button nzType="primary" data-testid="md-warehouse-create" *erpHasPermission="'MD.WAREHOUSE.CREATE'"
        (click)="openCreate()">{{ 'common.action.add' | translate }}</button>
      <input erpListFilters nz-input type="search" name="keyword" autocomplete="off" data-testid="md-warehouse-search" [formControl]="searchControl"
        [attr.aria-label]="'md.warehouse.search' | translate" [placeholder]="'md.warehouse.search' | translate" (keyup.enter)="search()" />
      @if (error() && !loadError()) { <nz-alert nzType="error" nzShowIcon data-testid="md-warehouse-error" [nzMessage]="actionErrorMessage" />
        <ng-template #actionErrorMessage>{{ error()! | translate }}
          @if (actionTraceId()) { {{ 'common.state.traceId' | translate }}: {{ actionTraceId() }} }
        </ng-template>
      }
      @if (!loadError()) {
      <nz-table #table data-testid="md-warehouse-table" nzSize="middle" [nzData]="rows()" [nzLoading]="loading()"
        [nzFrontPagination]="false" [nzTotal]="total()" [nzPageIndex]="page()" [nzPageSize]="pageSize()"
        nzShowSizeChanger [nzPageSizeOptions]="pageSizeOptions" [nzNoResult]="(appliedKeyword() ? 'md.warehouse.noResults' : 'md.warehouse.empty') | translate"
        (nzQueryParams)="onQueryParams($event)">
        <thead><tr>
          <th nzColumnKey="code" [nzSortFn]="true" [nzSortOrder]="sortOrder('code')">{{ 'md.warehouse.code' | translate }}</th>
          <th nzColumnKey="name" [nzSortFn]="true" [nzSortOrder]="sortOrder('name')">{{ 'md.warehouse.name' | translate }}</th>
          <th>{{ 'md.warehouse.address' | translate }}</th><th>{{ 'md.warehouse.contactName' | translate }}</th>
          <th>{{ 'md.warehouse.phone' | translate }}</th><th>{{ 'md.warehouse.status' | translate }}</th>
          <th>{{ 'md.warehouse.actions' | translate }}</th>
        </tr></thead>
        <tbody>@for (row of table.data; track row.id) {
          <tr [attr.data-testid]="'md-warehouse-row-' + row.code"><td>{{ row.code }}</td><td>{{ row.name }}</td>
            <td>{{ row.address }}</td><td>{{ row.contactName }}</td><td>{{ row.phone }}</td>
            <td><nz-tag [nzColor]="row.isActive ? 'success' : 'default'">{{ (row.isActive ? 'md.warehouse.active' : 'md.warehouse.inactive') | translate }}</nz-tag></td>
            <td><button nz-button nzType="link" [attr.data-testid]="'md-warehouse-locations-' + row.code"
                (click)="locationWarehouse.set(row)">{{ 'md.warehouse.locations' | translate }}</button>
              <button nz-button nzType="link" *erpHasPermission="'MD.WAREHOUSE.EDIT'"
                [attr.data-testid]="'md-warehouse-edit-' + row.code" (click)="openEdit(row)">{{ 'common.action.edit' | translate }}</button>
              <button nz-button nzType="link" nzDanger *erpHasPermission="'MD.WAREHOUSE.DELETE'"
                [attr.data-testid]="'md-warehouse-delete-' + row.code" [disabled]="deleting()" (click)="confirmDelete(row)">{{ (deleting() ? 'common.action.processing' : 'common.action.delete') | translate }}</button></td></tr>
        }</tbody>
      </nz-table>
      }
    </erp-list-page>
    <nz-modal [nzVisible]="dialogOpen()" [nzTitle]="'md.warehouse.formTitle' | translate"
      [nzOkText]="'common.action.save' | translate" [nzCancelText]="'common.action.cancel' | translate"
      [nzOkLoading]="saving()" (nzOnOk)="save()" (nzOnCancel)="closeDialog()">
      <ng-container *nzModalContent>
        @if (saveError()) { <nz-alert nzType="error" nzShowIcon data-testid="md-warehouse-save-error" [nzMessage]="saveError()! | translate" /> }
        <form class="warehouse-form" [formGroup]="form" (ngSubmit)="save()" data-testid="md-warehouse-form">
          <label for="warehouse-code">{{ 'md.warehouse.code' | translate }}</label>
          <input nz-input id="warehouse-code" data-testid="md-warehouse-code" formControlName="code" maxlength="30" />
          @if (form.controls.code.touched && form.controls.code.invalid) { <p role="alert">{{ 'md.warehouse.invalidCode' | translate }}</p> }
          <label for="warehouse-name">{{ 'md.warehouse.name' | translate }}</label>
          <input nz-input id="warehouse-name" data-testid="md-warehouse-name" formControlName="name" maxlength="200" />
          @if (form.controls.name.touched && form.controls.name.invalid) { <p role="alert">{{ 'md.warehouse.invalidName' | translate }}</p> }
          <label for="warehouse-address">{{ 'md.warehouse.address' | translate }}</label>
          <input nz-input id="warehouse-address" data-testid="md-warehouse-address" formControlName="address" maxlength="300" />
          <label for="warehouse-contact">{{ 'md.warehouse.contactName' | translate }}</label>
          <input nz-input id="warehouse-contact" data-testid="md-warehouse-contact" formControlName="contactName" maxlength="100" />
          <label for="warehouse-phone">{{ 'md.warehouse.phone' | translate }}</label>
          <input nz-input id="warehouse-phone" data-testid="md-warehouse-phone" formControlName="phone" maxlength="50" />
          @if (detailsInvalid()) { <p role="alert">{{ 'md.warehouse.invalidText' | translate }}</p> }
          @if (editing()) { <label><input type="checkbox" formControlName="isActive" data-testid="md-warehouse-active" />{{ 'md.warehouse.active' | translate }}</label> }
        </form>
      </ng-container>
    </nz-modal>
    <erp-storage-location-dialog [warehouse]="locationWarehouse()" (closed)="locationWarehouse.set(null)" />
  `,
  styles: `
    input[erpListFilters] { max-width: 320px; }
    .warehouse-form { display: grid; gap: 8px; } .warehouse-form p { color: var(--erp-status-error); margin: 0; }
  `,
})
export class WarehousePage {
  readonly pageSizeOptions = [...PAGE_SIZE_OPTIONS];
  private readonly api = inject(WarehouseService);
  private readonly dialogs = inject(ErpDialogService);
  private readonly company = inject(CompanyContextService);
  private readonly listRequest = latestRequest();
  private readonly companyId = computed(() => this.company.current()?.id ?? null);
  readonly rows = signal<Warehouse[]>([]);
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
  readonly editing = signal<Warehouse | null>(null);
  readonly locationWarehouse = signal<Warehouse | null>(null);
  readonly saving = signal(false);
  /** 刪除進行中：避免第二次確認帶舊版本而得到 409（MD-F05）。 */
  readonly deleting = signal(false);
  readonly saveError = signal<string | null>(null);
  readonly searchControl = new FormControl('', { nonNullable: true });
  readonly form = new FormGroup({
    code: new FormControl('', { nonNullable: true, validators: [Validators.required, Validators.pattern(/^[A-Za-z0-9_-]{1,30}$/)] }),
    name: new FormControl('', { nonNullable: true, validators: [Validators.required, Validators.pattern(/.*\S.*/), Validators.maxLength(200)] }),
    address: new FormControl('', { nonNullable: true, validators: [Validators.maxLength(300)] }),
    contactName: new FormControl('', { nonNullable: true, validators: [Validators.maxLength(100)] }),
    phone: new FormControl('', { nonNullable: true, validators: [Validators.maxLength(50)] }),
    isActive: new FormControl(true, { nonNullable: true }),
  });
  constructor() {
    effect(() => { this.companyId(); untracked(() => {
      this.closeDialog(); this.locationWarehouse.set(null); this.page.set(1); this.reload();
    }); });
  }
  detailsInvalid(): boolean {
    const { address, contactName, phone } = this.form.controls;
    return [address, contactName, phone].some((control) => control.touched && control.invalid);
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
    this.form.reset({ code: '', name: '', address: '', contactName: '', phone: '', isActive: true });
    this.form.controls.code.enable(); this.saveError.set(null); this.dialogOpen.set(true);
  }
  openEdit(row: Warehouse): void {
    this.editing.set(row);
    this.form.reset({ code: row.code, name: row.name, address: row.address ?? '', contactName: row.contactName ?? '',
      phone: row.phone ?? '', isActive: row.isActive });
    this.form.controls.code.disable(); this.saveError.set(null); this.dialogOpen.set(true);
  }
  closeDialog(): void { this.dialogOpen.set(false); this.saveError.set(null); }
  save(): void {
    if (this.saving()) return;
    this.form.markAllAsTouched(); if (this.form.invalid) return;
    const draft = this.form.getRawValue(); const row = this.editing();
    const body = { name: draft.name, address: draft.address, contactName: draft.contactName, phone: draft.phone };
    const request = row ? this.api.update(row.id, { ...body, isActive: draft.isActive, version: row.version })
      : this.api.create({ ...body, code: draft.code });
    this.saving.set(true); this.saveError.set(null);
    request.subscribe({
      next: () => { this.saving.set(false); this.closeDialog(); this.reload(); },
      error: (error: unknown) => { this.saving.set(false); this.saveError.set(this.api.errorKey(error)); if (isStaleRowError(error)) this.reload(); },
    });
  }
  confirmDelete(row: Warehouse): void {
    if (this.deleting()) return;
    this.dialogs.confirm({ titleKey: 'md.warehouse.deleteConfirm', contentKey: 'common.confirm.irreversible',
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
