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
import { latestRequest } from '../../shared/list-page/latest-request';
import { isStaleRowError } from '../../shared/list-page/stale-row';
import { ListPage } from '../../shared/list-page/list-page';
import { PAGE_SIZE_OPTIONS, nextTableState, sortOrderOf } from '../../shared/list-page/table-query';
import { CATEGORY_PAGE, MasterCategory } from './master-category.service';

/** 客戶分類、供應商分類共用的維護頁；API、權限前綴與文字由路由提供的 `CATEGORY_PAGE` 決定。 */
@Component({
  selector: 'erp-category-list',
  imports: [ReactiveFormsModule, TranslatePipe, NzAlertModule, NzButtonModule, NzInputModule,
    NzModalModule, NzTableModule, NzTagModule, HasPermissionDirective, ListPage],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <erp-list-page [titleKey]="t('title')" testId="md-category" titleTestId="md-category-title"
      [errorKey]="loadError()" [errorTraceId]="errorTraceId()" [resettable]="false"
      (querySubmit)="search()" (retryLoad)="reload()">
      <button erpListActions nz-button nzType="primary" data-testid="md-category-create"
        *erpHasPermission="perm('CREATE')" (click)="openCreate()">{{ 'common.action.add' | translate }}</button>
      <input erpListFilters nz-input type="search" name="keyword" autocomplete="off" data-testid="md-category-search" [formControl]="searchControl"
        [attr.aria-label]="t('search') | translate" [placeholder]="t('search') | translate"
        (keyup.enter)="search()" />
      @if (error()) {
        <nz-alert nzType="error" nzShowIcon data-testid="md-category-action-error" [nzMessage]="actionErrorMessage" />
        <ng-template #actionErrorMessage>
          {{ error()! | translate }}
          @if (actionTraceId()) { {{ 'common.state.traceId' | translate }}: {{ actionTraceId() }} }
        </ng-template>
      }
      <nz-table #table data-testid="md-category-table" nzSize="middle" [nzData]="rows()" [nzLoading]="loading()"
        [nzFrontPagination]="false" [nzTotal]="total()" [nzPageIndex]="page()" [nzPageSize]="pageSize()"
        nzShowSizeChanger [nzPageSizeOptions]="pageSizeOptions"
        [nzNoResult]="t(appliedKeyword() ? 'noResults' : 'empty') | translate"
        (nzQueryParams)="onQueryParams($event)">
        <thead><tr>
          <th nzColumnKey="code" [nzSortFn]="true" [nzSortOrder]="sortOrder('code')">{{ t('code') | translate }}</th>
          <th nzColumnKey="name" [nzSortFn]="true" [nzSortOrder]="sortOrder('name')">{{ t('name') | translate }}</th>
          <th>{{ t('status') | translate }}</th><th>{{ t('actions') | translate }}</th>
        </tr></thead>
        <tbody>@for (row of table.data; track row.id) {
          <tr [attr.data-testid]="'md-category-row-' + row.code">
            <td>{{ row.code }}</td><td>{{ row.name }}</td>
            <td><nz-tag [nzColor]="row.isActive ? 'success' : 'default'">
              {{ t(row.isActive ? 'active' : 'inactive') | translate }}
            </nz-tag></td>
            <td>
              <button nz-button nzType="link" *erpHasPermission="perm('EDIT')"
                [attr.data-testid]="'md-category-edit-' + row.code" (click)="openEdit(row)">{{ 'common.action.edit' | translate }}</button>
              <button nz-button nzType="link" nzDanger *erpHasPermission="perm('DELETE')"
                [attr.data-testid]="'md-category-delete-' + row.code" [disabled]="busyId() !== null"
                (click)="confirmDelete(row)">{{ 'common.action.delete' | translate }}</button>
            </td>
          </tr>
        }</tbody>
      </nz-table>
    </erp-list-page>
    <nz-modal [nzVisible]="dialogOpen()" [nzTitle]="t('formTitle') | translate"
      [nzOkText]="'common.action.save' | translate" [nzCancelText]="'common.action.cancel' | translate"
      [nzOkLoading]="saving()" (nzOnOk)="save()" (nzOnCancel)="closeDialog()">
      <ng-container *nzModalContent>
        @if (saveError()) {
          <nz-alert nzType="error" nzShowIcon data-testid="md-category-save-error" [nzMessage]="saveErrorMessage" />
          <ng-template #saveErrorMessage>
            {{ saveError()! | translate }}
            @if (saveTraceId()) { {{ 'common.state.traceId' | translate }}: {{ saveTraceId() }} }
          </ng-template>
        }
        <form class="category-form" [formGroup]="form" (ngSubmit)="save()" data-testid="md-category-form">
          <label for="category-code">{{ t('code') | translate }} <span aria-hidden="true">*</span></label>
          <input nz-input id="category-code" name="code" autocomplete="off" required data-testid="md-category-code" formControlName="code" maxlength="30" />
          @if (form.controls.code.touched && form.controls.code.invalid) { <p role="alert">{{ t('invalidCode') | translate }}</p> }
          <label for="category-name">{{ t('name') | translate }} <span aria-hidden="true">*</span></label>
          <input nz-input id="category-name" name="name" autocomplete="off" required data-testid="md-category-name" formControlName="name" maxlength="200" />
          @if (form.controls.name.touched && form.controls.name.invalid) { <p role="alert">{{ t('invalidName') | translate }}</p> }
          @if (editing()) { <label><input type="checkbox" formControlName="isActive" data-testid="md-category-active" />
            {{ t('active') | translate }}</label> }
        </form>
      </ng-container>
    </nz-modal>
  `,
  styles: `
    input[erpListFilters] { max-width: 320px; }
    .category-form { display: grid; gap: 8px; }
    .category-form p { color: var(--erp-status-error); margin: 0; }
  `,
})
export class CategoryList {
  readonly pageSizeOptions = [...PAGE_SIZE_OPTIONS];
  private readonly config = inject(CATEGORY_PAGE);
  private readonly api = inject(this.config.api);
  private readonly company = inject(CompanyContextService);
  private readonly dialogs = inject(ErpDialogService);
  private readonly listRequest = latestRequest();
  private readonly companyId = computed(() => this.company.current()?.id ?? null);
  readonly rows = signal<MasterCategory[]>([]);
  readonly total = signal(0);
  readonly page = signal(1);
  readonly pageSize = signal(20);
  readonly sort = signal('code');
  readonly loading = signal(false);
  readonly error = signal<string | null>(null);
  readonly loadError = signal<string | null>(null);
  readonly errorTraceId = signal<string | null>(null);
  readonly actionTraceId = signal<string | null>(null);
  readonly dialogOpen = signal(false);
  readonly editing = signal<MasterCategory | null>(null);
  readonly saving = signal(false);
  readonly saveError = signal<string | null>(null);
  readonly saveTraceId = signal<string | null>(null);
  readonly busyId = signal<number | null>(null);
  readonly searchControl = new FormControl('', { nonNullable: true });
  readonly appliedKeyword = signal('');
  readonly form = new FormGroup({
    code: new FormControl('', { nonNullable: true, validators: [Validators.required, Validators.pattern(/^[A-Za-z0-9_-]{1,30}$/)] }),
    name: new FormControl('', { nonNullable: true, validators: [Validators.required, Validators.pattern(/.*\S.*/), Validators.maxLength(200)] }),
    isActive: new FormControl(true, { nonNullable: true }),
  });

  constructor() {
    effect(() => {
      this.companyId();
      untracked(() => {
        this.closeDialog();
        this.page.set(1);
        this.reload();
      });
    });
  }

  /** 此頁的 i18n key。 */
  t(key: string): string { return `${this.config.i18n}.${key}`; }

  /** 此頁的權限代碼。 */
  perm(action: 'CREATE' | 'EDIT' | 'DELETE'): string { return `${this.config.permission}.${action}`; }

  sortOrder(field: 'code' | 'name'): 'ascend' | 'descend' | null {
    return sortOrderOf(this.sort(), field);
  }

  search(): void {
    this.appliedKeyword.set(this.searchControl.value.trim());
    this.page.set(1);
    this.reload();
  }

  onQueryParams(params: NzTableQueryParams): void {
    const next = nextTableState(params, { page: this.page(), pageSize: this.pageSize(), sort: this.sort() }, ['code', 'name'], 'code');
    if (!next) return;
    this.page.set(next.page);
    this.pageSize.set(next.pageSize);
    this.sort.set(next.sort);
    this.reload();
  }

  reload(): void {
    this.loading.set(true);
    this.error.set(null);
    this.actionTraceId.set(null);
    this.loadError.set(null);
    this.errorTraceId.set(null);
    this.listRequest.run(this.api.list({ page: this.page(), pageSize: this.pageSize(),
      keyword: this.appliedKeyword(), sort: this.sort() }), {
      next: (result) => { this.rows.set(result.items); this.total.set(result.totalCount); this.loading.set(false); },
      error: (error: unknown) => {
        this.rows.set([]); this.total.set(0);
        this.loadError.set(this.api.errorKey(error)); this.errorTraceId.set(apiErrorTraceId(error)); this.loading.set(false);
      },
    });
  }

  openCreate(): void {
    this.editing.set(null);
    this.form.reset({ code: '', name: '', isActive: true });
    this.form.controls.code.enable();
    this.saveError.set(null);
    this.saveTraceId.set(null);
    this.dialogOpen.set(true);
  }

  openEdit(row: MasterCategory): void {
    this.editing.set(row);
    this.form.reset({ code: row.code, name: row.name, isActive: row.isActive });
    this.form.controls.code.disable();
    this.saveError.set(null);
    this.saveTraceId.set(null);
    this.dialogOpen.set(true);
  }

  closeDialog(): void { this.dialogOpen.set(false); this.saveError.set(null); this.saveTraceId.set(null); }

  save(): void {
    if (this.saving()) return;
    this.form.markAllAsTouched();
    if (this.form.invalid) return;
    const draft = this.form.getRawValue();
    const row = this.editing();
    const request = row ? this.api.update(row.id, { name: draft.name, isActive: draft.isActive, version: row.version })
      : this.api.create({ code: draft.code, name: draft.name });
    this.saving.set(true);
    this.saveError.set(null);
    this.saveTraceId.set(null);
    request.subscribe({
      next: () => { this.saving.set(false); this.closeDialog(); this.reload(); },
      error: (error: unknown) => {
        this.saving.set(false); this.saveError.set(this.api.errorKey(error)); this.saveTraceId.set(apiErrorTraceId(error));
        if (isStaleRowError(error)) this.reload();
      },
    });
  }

  confirmDelete(row: MasterCategory): void {
    if (this.busyId() !== null) return;
    this.dialogs.confirm({ titleKey: this.t('deleteConfirm'), contentKey: 'common.confirm.irreversible',
      params: { code: row.code }, okKey: 'common.action.delete', danger: true }).subscribe((ok) => {
      if (!ok) return;
      this.busyId.set(row.id);
      this.api.remove(row.id, row.version).subscribe({
        next: () => { this.busyId.set(null); this.reload(); },
        error: (error: unknown) => {
          this.busyId.set(null); if (isStaleRowError(error)) this.reload();
          this.error.set(this.api.errorKey(error)); this.actionTraceId.set(apiErrorTraceId(error));
        },
      });
    });
  }
}
