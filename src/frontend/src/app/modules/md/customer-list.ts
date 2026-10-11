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
import { CustomerCategory, CustomerCategoryService } from './customer-category.service';
import { Customer, customerErrorKey, CustomerService } from './customer.service';

@Component({
  selector: 'erp-customer-list',
  imports: [ReactiveFormsModule, TranslatePipe, NzAlertModule, NzButtonModule, NzInputModule,
    NzModalModule, NzTableModule, NzTagModule, HasPermissionDirective],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <section class="md-customers">
      <header><h1 data-testid="md-customer-title">{{ 'md.customer.title' | translate }}</h1>
        <button nz-button nzType="primary" data-testid="md-customer-create"
          *erpHasPermission="'MD.CUSTOMER.CREATE'" (click)="openCreate()">{{ 'common.action.add' | translate }}</button>
      </header>
      <div class="filters">
        <input nz-input type="search" data-testid="md-customer-search" [formControl]="searchControl"
          [placeholder]="'md.customer.search' | translate" (keyup.enter)="search()" />
        <button nz-button data-testid="md-customer-search-button" (click)="search()">{{ 'common.action.search' | translate }}</button>
      </div>
      @if (error()) { <nz-alert nzType="error" nzShowIcon [nzMessage]="error()! | translate" /> }
      <nz-table #table data-testid="md-customer-table" nzSize="middle" [nzData]="rows()" [nzLoading]="loading()"
        [nzFrontPagination]="false" [nzTotal]="total()" [nzPageIndex]="page()" [nzPageSize]="pageSize()"
        nzShowSizeChanger [nzPageSizeOptions]="pageSizeOptions" [nzNoResult]="'md.customer.empty' | translate"
        (nzQueryParams)="onQueryParams($event)">
        <thead><tr>
          <th nzColumnKey="code" [nzSortFn]="true" [nzSortOrder]="sortOrder('code')">{{ 'md.customer.code' | translate }}</th>
          <th nzColumnKey="name" [nzSortFn]="true" [nzSortOrder]="sortOrder('name')">{{ 'md.customer.name' | translate }}</th>
          <th>{{ 'md.customer.category' | translate }}</th><th>{{ 'md.customer.status' | translate }}</th>
          <th>{{ 'md.customer.actions' | translate }}</th>
        </tr></thead>
        <tbody>@for (row of table.data; track row.id) {
          <tr [attr.data-testid]="'md-customer-row-' + row.code">
            <td>{{ row.code }}</td><td>{{ row.name }}</td>
            <td>{{ categoryName(row.categoryId) }}</td>
            <td><nz-tag [nzColor]="row.isActive ? 'success' : 'default'">
              {{ (row.isActive ? 'md.customer.active' : 'md.customer.inactive') | translate }}
            </nz-tag></td>
            <td><button nz-button nzType="link" *erpHasPermission="'MD.CUSTOMER.EDIT'"
              [attr.data-testid]="'md-customer-edit-' + row.code" (click)="openEdit(row)">{{ 'common.action.edit' | translate }}</button></td>
          </tr>
        }</tbody>
      </nz-table>
    </section>
    <nz-modal [nzVisible]="dialogOpen()" [nzTitle]="'md.customer.formTitle' | translate"
      [nzOkText]="'common.action.save' | translate" [nzCancelText]="'common.action.cancel' | translate"
      [nzOkLoading]="saving()" (nzOnOk)="save()" (nzOnCancel)="closeDialog()">
      <ng-container *nzModalContent>
        @if (saveError()) { <nz-alert nzType="error" nzShowIcon data-testid="md-customer-save-error" [nzMessage]="saveError()! | translate" /> }
        <form class="customer-form" [formGroup]="form" (ngSubmit)="save()" data-testid="md-customer-form">
          <label for="customer-code">{{ 'md.customer.code' | translate }}</label>
          <input nz-input id="customer-code" data-testid="md-customer-code" formControlName="code" maxlength="30" />
          @if (form.controls.code.touched && form.controls.code.invalid) { <p role="alert">{{ 'md.customer.invalidCode' | translate }}</p> }
          <label for="customer-name">{{ 'md.customer.name' | translate }}</label>
          <input nz-input id="customer-name" data-testid="md-customer-name" formControlName="name" maxlength="200" />
          @if (form.controls.name.touched && form.controls.name.invalid) { <p role="alert">{{ 'md.customer.invalidName' | translate }}</p> }
          <label for="customer-category">{{ 'md.customer.category' | translate }}</label>
          <select id="customer-category" data-testid="md-customer-category" formControlName="categoryId">
            <option [ngValue]="null">{{ 'md.customer.noCategory' | translate }}</option>
            @for (category of categories(); track category.id) {
              <option [ngValue]="category.id" [disabled]="!category.isActive && category.id !== editing()?.categoryId">{{ category.name }}</option>
            }
          </select>
          @if (editing()) { <label><input type="checkbox" formControlName="isActive" data-testid="md-customer-active" />
            {{ 'md.customer.active' | translate }}</label> }
        </form>
      </ng-container>
    </nz-modal>
  `,
  styles: `
    .md-customers { display: grid; gap: 16px; color: var(--erp-color-text); }
    header { display: flex; justify-content: space-between; align-items: center; }
    h1 { margin: 0; font-size: 20px; }
    .filters { display: flex; gap: 8px; }
    .filters input { max-width: 320px; }
    .customer-form { display: grid; gap: 8px; }
    .customer-form p { color: var(--erp-status-error); margin: 0; }
  `,
})
export class CustomerList {
  readonly pageSizeOptions = [...PAGE_SIZE_OPTIONS];
  private readonly api = inject(CustomerService);
  private readonly categoryApi = inject(CustomerCategoryService);
  private readonly company = inject(CompanyContextService);
  private readonly permissions = inject(PermissionService);
  private readonly listRequest = latestRequest();
  private readonly categoryRequest = latestRequest();
  private readonly companyId = computed(() => this.company.current()?.id ?? null);
  readonly rows = signal<Customer[]>([]);
  readonly categories = signal<CustomerCategory[]>([]);
  readonly total = signal(0);
  readonly page = signal(1);
  readonly pageSize = signal(20);
  readonly sort = signal('code');
  readonly loading = signal(false);
  readonly error = signal<string | null>(null);
  readonly dialogOpen = signal(false);
  readonly editing = signal<Customer | null>(null);
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
      untracked(() => { this.closeDialog(); this.page.set(1); this.reload(); this.loadCategories(); });
    });
  }

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
      error: (error: unknown) => { this.rows.set([]); this.total.set(0); this.error.set(customerErrorKey(error)); this.loading.set(false); },
    });
  }
  loadCategories(): void {
    if (!this.permissions.has('MD.CUSTOMER_CATEGORY.VIEW')) { this.categories.set([]); return; }
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
  openEdit(row: Customer): void {
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
      error: (error: unknown) => { this.saving.set(false); this.saveError.set(customerErrorKey(error)); },
    });
  }
}
