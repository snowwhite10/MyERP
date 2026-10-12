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
import { PermissionService } from '../../core/auth/permission.service';
import { CompanyContextService } from '../../core/company/company-context.service';
import { latestRequest } from '../../shared/list-page/latest-request';
import { PAGE_SIZE_OPTIONS } from '../../shared/list-page/table-query';
import { BusinessPartner, PARTNER_PAGE } from './business-partner.service';
import { PartnerContactDialog } from './partner-contact-dialog';
import { PartnerImportDialog } from './partner-import-dialog';
import { MasterCategory } from './master-category.service';

/** 客戶、供應商共用的維護頁；API、權限、文字與 data-testid 前綴由路由提供的 `PARTNER_PAGE` 決定。 */
@Component({
  selector: 'erp-partner-list',
  imports: [ReactiveFormsModule, TranslatePipe, NzAlertModule, NzButtonModule, NzInputModule,
    NzModalModule, NzTableModule, NzTagModule, HasPermissionDirective, PartnerContactDialog, PartnerImportDialog],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <section class="md-partners">
      <header><h1 [attr.data-testid]="id('title')">{{ t('title') | translate }}</h1><div class="actions">
        @if (importKind) { <button nz-button [attr.data-testid]="id('import-open')" *erpHasPermission="perm('IMPORT')"
          (click)="importOpen.set(true)">{{ 'md.customerImport.open' | translate }}</button> }
        <button nz-button nzType="primary" [attr.data-testid]="id('create')"
          *erpHasPermission="perm('CREATE')" (click)="openCreate()">{{ 'common.action.add' | translate }}</button>
      </div></header>
      <div class="filters">
        <input nz-input type="search" [attr.data-testid]="id('search')" [formControl]="searchControl"
          [placeholder]="t('search') | translate" (keyup.enter)="search()" />
        <button nz-button [attr.data-testid]="id('search-button')" (click)="search()">{{ 'common.action.search' | translate }}</button>
      </div>
      @if (error()) { <nz-alert nzType="error" nzShowIcon [nzMessage]="error()! | translate" /> }
      <nz-table #table [attr.data-testid]="id('table')" nzSize="middle" [nzData]="rows()" [nzLoading]="loading()"
        [nzFrontPagination]="false" [nzTotal]="total()" [nzPageIndex]="page()" [nzPageSize]="pageSize()"
        nzShowSizeChanger [nzPageSizeOptions]="pageSizeOptions" [nzNoResult]="t('empty') | translate"
        (nzQueryParams)="onQueryParams($event)">
        <thead><tr>
          <th nzColumnKey="code" [nzSortFn]="true" [nzSortOrder]="sortOrder('code')">{{ t('code') | translate }}</th>
          <th nzColumnKey="name" [nzSortFn]="true" [nzSortOrder]="sortOrder('name')">{{ t('name') | translate }}</th>
          <th>{{ t('category') | translate }}</th><th>{{ t('status') | translate }}</th>
          <th>{{ t('actions') | translate }}</th>
        </tr></thead>
        <tbody>@for (row of table.data; track row.id) {
          <tr [attr.data-testid]="id('row-' + row.code)">
            <td>{{ row.code }}</td><td>{{ row.name }}</td>
            <td>{{ categoryName(row.categoryId) }}</td>
            <td><nz-tag [nzColor]="row.isActive ? 'success' : 'default'">
              {{ t(row.isActive ? 'active' : 'inactive') | translate }}
            </nz-tag></td>
            <td class="actions"><button nz-button nzType="link" *erpHasPermission="perm('EDIT')"
              [attr.data-testid]="id('edit-' + row.code)" (click)="openEdit(row)">{{ 'common.action.edit' | translate }}</button>
              @if (hasContacts) { <button nz-button nzType="link" [attr.data-testid]="id('contacts-' + row.code)"
                (click)="contactPartner.set(row)">{{ 'md.customerContact.open' | translate }}</button> }</td>
          </tr>
        }</tbody>
      </nz-table>
    </section>
    <nz-modal [nzVisible]="dialogOpen()" [nzTitle]="t('formTitle') | translate"
      [nzOkText]="'common.action.save' | translate" [nzCancelText]="'common.action.cancel' | translate"
      [nzOkLoading]="saving()" (nzOnOk)="save()" (nzOnCancel)="closeDialog()">
      <ng-container *nzModalContent>
        @if (saveError()) { <nz-alert nzType="error" nzShowIcon [attr.data-testid]="id('save-error')" [nzMessage]="saveError()! | translate" /> }
        <form class="partner-form" [formGroup]="form" (ngSubmit)="save()" [attr.data-testid]="id('form')">
          <label [for]="id('code')">{{ t('code') | translate }}</label>
          <input nz-input [id]="id('code')" [attr.data-testid]="id('code')" formControlName="code" maxlength="30" />
          @if (form.controls.code.touched && form.controls.code.invalid) { <p role="alert">{{ t('invalidCode') | translate }}</p> }
          <label [for]="id('name')">{{ t('name') | translate }}</label>
          <input nz-input [id]="id('name')" [attr.data-testid]="id('name')" formControlName="name" maxlength="200" />
          @if (form.controls.name.touched && form.controls.name.invalid) { <p role="alert">{{ t('invalidName') | translate }}</p> }
          <label [for]="id('category')">{{ t('category') | translate }}</label>
          <select [id]="id('category')" [attr.data-testid]="id('category')" formControlName="categoryId">
            <option [ngValue]="null">{{ t('noCategory') | translate }}</option>
            @for (category of categories(); track category.id) {
              <option [ngValue]="category.id" [disabled]="!category.isActive && category.id !== editing()?.categoryId">{{ category.name }}</option>
            }
          </select>
          @if (editing()) { <label><input type="checkbox" formControlName="isActive" [attr.data-testid]="id('active')" />
            {{ t('active') | translate }}</label> }
        </form>
      </ng-container>
    </nz-modal>
    @if (contactKind; as kind) { <erp-partner-contact-dialog [kind]="kind" [partner]="contactPartner()" (closed)="contactPartner.set(null)" /> }
    @if (importKind; as kind) { <erp-partner-import-dialog [kind]="kind" [open]="importOpen()" (closed)="importOpen.set(false)"
      (imported)="reload()" /> }
  `,
  styles: `
    .md-partners { display: grid; gap: 16px; color: var(--erp-color-text); }
    header { display: flex; justify-content: space-between; align-items: center; }
    h1 { margin: 0; font-size: 20px; }
    .filters { display: flex; gap: 8px; }
    .filters input { max-width: 320px; }
    .actions { white-space: nowrap; }
    .partner-form { display: grid; gap: 8px; }
    .partner-form p { color: var(--erp-status-error); margin: 0; }
  `,
})
export class PartnerList {
  readonly pageSizeOptions = [...PAGE_SIZE_OPTIONS];
  private readonly config = inject(PARTNER_PAGE);
  private readonly api = inject(this.config.api);
  private readonly categoryApi = inject(this.config.categoryApi);
  private readonly company = inject(CompanyContextService);
  private readonly permissions = inject(PermissionService);
  private readonly listRequest = latestRequest();
  private readonly categoryRequest = latestRequest();
  private readonly companyId = computed(() => this.company.current()?.id ?? null);
  readonly rows = signal<BusinessPartner[]>([]);
  readonly categories = signal<MasterCategory[]>([]);
  readonly total = signal(0);
  readonly page = signal(1);
  readonly pageSize = signal(20);
  readonly sort = signal('code');
  readonly loading = signal(false);
  readonly error = signal<string | null>(null);
  readonly dialogOpen = signal(false);
  readonly editing = signal<BusinessPartner | null>(null);
  /** 正在查看聯絡人的往來對象（只限設定了 `PartnerPage.contacts` 的頁面）。 */
  readonly contactPartner = signal<BusinessPartner | null>(null);
  readonly contactKind = this.config.contacts;
  readonly hasContacts = this.contactKind !== undefined;
  readonly importKind = this.config.imports;
  readonly importOpen = signal(false);
  readonly saving = signal(false);
  readonly saveError = signal<string | null>(null);
  readonly searchControl = new FormControl('', { nonNullable: true });
  readonly form = new FormGroup({
    code: new FormControl('', { nonNullable: true, validators: [Validators.required, Validators.pattern(/^[A-Za-z0-9_-]{1,30}$/)] }),
    name: new FormControl('', { nonNullable: true, validators: [Validators.required, Validators.pattern(/.*\S.*/), Validators.maxLength(200)] }),
    categoryId: new FormControl<number | null>(null),
    isActive: new FormControl(true, { nonNullable: true }),
  });

  constructor() {
    effect(() => {
      this.companyId();
      untracked(() => { this.closeDialog(); this.contactPartner.set(null); this.importOpen.set(false); this.page.set(1); this.reload(); this.loadCategories(); });
    });
  }

  /** 此頁的 i18n key。 */
  t(key: string): string { return `${this.config.i18n}.${key}`; }
  /** 此頁的權限代碼。 */
  perm(action: string): string { return `${this.config.permission}.${action}`; }
  /** 此頁的 data-testid／元素 id。 */
  id(key: string): string { return `${this.config.testId}-${key}`; }
  categoryName(id: number | null): string { return this.categories().find((category) => category.id === id)?.name ?? ''; }
  sortOrder(field: 'code' | 'name'): 'ascend' | 'descend' | null {
    return this.sort() === field ? 'ascend' : this.sort() === `-${field}` ? 'descend' : null;
  }
  search(): void { this.page.set(1); this.reload(); }
  onQueryParams(params: NzTableQueryParams): void {
    const active = params.sort.find((item) => item.value !== null);
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
  loadCategories(): void {
    if (!this.permissions.has(this.config.categoryPermission)) { this.categories.set([]); return; }
    this.categoryRequest.run(this.categoryApi.list({ page: 1, pageSize: 100, sort: 'name' }), {
      next: (result) => this.categories.set(result.items),
      error: () => this.categories.set([]),
    });
  }
  openCreate(): void {
    this.editing.set(null);
    this.form.reset({ code: '', name: '', categoryId: null, isActive: true });
    this.form.controls.code.enable(); this.saveError.set(null); this.dialogOpen.set(true);
  }
  openEdit(row: BusinessPartner): void {
    this.editing.set(row);
    this.form.reset({ code: row.code, name: row.name, categoryId: row.categoryId, isActive: row.isActive });
    this.form.controls.code.disable(); this.saveError.set(null); this.dialogOpen.set(true);
  }
  closeDialog(): void { this.dialogOpen.set(false); this.saveError.set(null); }
  save(): void {
    if (this.saving()) return;
    this.form.markAllAsTouched();
    if (this.form.invalid) return;
    const draft = this.form.getRawValue();
    const row = this.editing();
    const request = row ? this.api.update(row.id, { name: draft.name, categoryId: draft.categoryId,
      isActive: draft.isActive, version: row.version }) : this.api.create({ code: draft.code,
      name: draft.name, categoryId: draft.categoryId });
    this.saving.set(true); this.saveError.set(null);
    request.subscribe({
      next: () => { this.saving.set(false); this.closeDialog(); this.reload(); },
      error: (error: unknown) => { this.saving.set(false); this.saveError.set(this.api.errorKey(error)); },
    });
  }
}
