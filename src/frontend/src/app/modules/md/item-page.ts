import { ChangeDetectionStrategy, Component, computed, effect, inject, signal, untracked } from '@angular/core';
import { FormControl, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { TranslatePipe } from '@ngx-translate/core';
import { EMPTY, expand, map, Observable, reduce } from 'rxjs';
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
import { PAGE_SIZE_OPTIONS, PagedResult } from '../../shared/list-page/table-query';
import { Item, ItemService } from './item.service';
import { Uom, UomService } from './uom.service';
import { ItemBrandService } from './item-brand.service';
import { MasterCategory } from './master-category.service';
import { flattenCategoryTree, ItemCategoryRow, ItemCategoryService } from './item-category.service';
import { ItemConversionDialog } from './item-conversion-dialog';

/** 物料主檔維護（介面 8.11–8.13）與物料專屬換算（介面 8.77）：只讀參照權限與新增／修改物料權限獨立。 */
@Component({
  selector: 'erp-item-page',
  imports: [ReactiveFormsModule, TranslatePipe, NzAlertModule, NzButtonModule, NzInputModule,
    NzModalModule, NzTableModule, NzTagModule, HasPermissionDirective, ItemConversionDialog],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <section class="md-items">
      <header><h1 data-testid="md-item-title">{{ 'md.item.title' | translate }}</h1>
        <button nz-button nzType="primary" data-testid="md-item-create" *erpHasPermission="'MD.ITEM.CREATE'"
          (click)="openCreate()">{{ 'common.action.add' | translate }}</button></header>
      <div class="filters"><input nz-input type="search" data-testid="md-item-search" [formControl]="searchControl"
        [placeholder]="'md.item.search' | translate" (keyup.enter)="search()" />
        <button nz-button data-testid="md-item-search-button" (click)="search()">{{ 'common.action.search' | translate }}</button></div>
      @if (error()) { <nz-alert nzType="error" nzShowIcon data-testid="md-item-error" [nzMessage]="error()! | translate" /> }
      <nz-table #table data-testid="md-item-table" nzSize="middle" [nzData]="rows()" [nzLoading]="loading()"
        [nzFrontPagination]="false" [nzTotal]="total()" [nzPageIndex]="page()" [nzPageSize]="pageSize()"
        nzShowSizeChanger [nzPageSizeOptions]="pageSizeOptions" [nzNoResult]="'md.item.empty' | translate"
        (nzQueryParams)="onQueryParams($event)">
        <thead><tr>
          <th nzColumnKey="code" [nzSortFn]="true" [nzSortOrder]="sortOrder('code')">{{ 'md.item.code' | translate }}</th>
          <th nzColumnKey="name" [nzSortFn]="true" [nzSortOrder]="sortOrder('name')">{{ 'md.item.name' | translate }}</th>
          <th>{{ 'md.item.specification' | translate }}</th><th>{{ 'md.item.baseUom' | translate }}</th>
          <th>{{ 'md.item.brand' | translate }}</th><th>{{ 'md.item.category' | translate }}</th>
          <th>{{ 'md.item.status' | translate }}</th><th>{{ 'md.item.actions' | translate }}</th>
        </tr></thead>
        <tbody>@for (row of table.data; track row.id) {
          <tr [attr.data-testid]="'md-item-row-' + row.code"><td>{{ row.code }}</td><td>{{ row.name }}</td>
            <td>{{ row.specification }}</td><td>{{ uomName(row.baseUomId) }}</td>
            <td>{{ brandName(row.brandId) }}</td><td>{{ categoryName(row.categoryId) }}</td>
            <td><nz-tag [nzColor]="row.isActive ? 'success' : 'default'">{{ (row.isActive ? 'md.item.active' : 'md.item.inactive') | translate }}</nz-tag></td>
            <td><button nz-button nzType="link" *erpHasPermission="'MD.ITEM.EDIT'"
              [attr.data-testid]="'md-item-edit-' + row.code" (click)="openEdit(row)">{{ 'common.action.edit' | translate }}</button>
              <button nz-button nzType="link" [attr.data-testid]="'md-item-conversions-' + row.code"
                (click)="conversionItem.set(row)">{{ 'md.item.conversions' | translate }}</button></td></tr>
        }</tbody>
      </nz-table>
    </section>
    <nz-modal [nzVisible]="dialogOpen()" [nzTitle]="'md.item.formTitle' | translate"
      [nzOkText]="'common.action.save' | translate" [nzCancelText]="'common.action.cancel' | translate"
      [nzOkLoading]="saving()" (nzOnOk)="save()" (nzOnCancel)="closeDialog()">
      <ng-container *nzModalContent>
        @if (saveError()) { <nz-alert nzType="error" nzShowIcon data-testid="md-item-save-error" [nzMessage]="saveError()! | translate" /> }
        <form class="item-form" [formGroup]="form" (ngSubmit)="save()" data-testid="md-item-form">
          <label for="item-code">{{ 'md.item.code' | translate }}</label>
          <input nz-input id="item-code" data-testid="md-item-code" formControlName="code" maxlength="30" />
          @if (form.controls.code.touched && form.controls.code.invalid) { <p role="alert">{{ 'md.item.invalidCode' | translate }}</p> }
          <label for="item-name">{{ 'md.item.name' | translate }}</label>
          <input nz-input id="item-name" data-testid="md-item-name" formControlName="name" maxlength="200" />
          @if (form.controls.name.touched && form.controls.name.invalid) { <p role="alert">{{ 'md.item.invalidName' | translate }}</p> }
          <label for="item-specification">{{ 'md.item.specification' | translate }}</label>
          <input nz-input id="item-specification" data-testid="md-item-specification" formControlName="specification" maxlength="500" />
          <label for="item-uom">{{ 'md.item.baseUom' | translate }}</label>
          <select id="item-uom" data-testid="md-item-uom" formControlName="baseUomId">
            <option [ngValue]="null">{{ 'md.item.selectUom' | translate }}</option>
            @for (option of uomOptions(); track option.id) { <option [ngValue]="option.id" [disabled]="!option.isActive">{{ option.code }} — {{ option.name }}</option> }
          </select>
          @if (form.controls.baseUomId.touched && form.controls.baseUomId.invalid) { <p role="alert">{{ 'md.item.selectUom' | translate }}</p> }
          <label for="item-brand">{{ 'md.item.brand' | translate }}</label>
          <select id="item-brand" data-testid="md-item-brand" formControlName="brandId">
            <option [ngValue]="null">{{ 'md.item.none' | translate }}</option>
            @for (option of brandOptions(); track option.id) { <option [ngValue]="option.id" [disabled]="!option.isActive">{{ option.code }} — {{ option.name }}</option> }
          </select>
          <label for="item-category">{{ 'md.item.category' | translate }}</label>
          <select id="item-category" data-testid="md-item-category" formControlName="categoryId">
            <option [ngValue]="null">{{ 'md.item.none' | translate }}</option>
            @for (option of categoryOptions(); track option.id) { <option [ngValue]="option.id" [disabled]="!option.isActive">{{ indent(option.level) }}{{ option.code }} — {{ option.name }}</option> }
          </select>
          @if (editing()) { <label><input type="checkbox" formControlName="isActive" data-testid="md-item-active" />{{ 'md.item.active' | translate }}</label> }
        </form>
      </ng-container>
    </nz-modal>
    <erp-item-conversion-dialog [item]="conversionItem()" [uoms]="uomOptions()" (closed)="conversionItem.set(null)" />
  `,
  styles: `
    .md-items { display: grid; gap: 16px; color: var(--erp-color-text); }
    header { display: flex; justify-content: space-between; align-items: center; }
    h1 { margin: 0; font-size: 20px; }
    .filters { display: flex; gap: 8px; } .filters input { max-width: 320px; }
    .item-form { display: grid; gap: 8px; } .item-form p { color: var(--erp-status-error); margin: 0; }
  `,
})
export class ItemPage {
  readonly pageSizeOptions = [...PAGE_SIZE_OPTIONS];
  private readonly api = inject(ItemService);
  private readonly uomApi = inject(UomService);
  private readonly brandApi = inject(ItemBrandService);
  private readonly categoryApi = inject(ItemCategoryService);
  private readonly permissions = inject(PermissionService);
  private readonly company = inject(CompanyContextService);
  private readonly listRequest = latestRequest();
  private readonly uomRequest = latestRequest();
  private readonly brandRequest = latestRequest();
  private readonly categoryRequest = latestRequest();
  private readonly companyId = computed(() => this.company.current()?.id ?? null);
  readonly rows = signal<Item[]>([]);
  readonly total = signal(0);
  readonly page = signal(1);
  readonly pageSize = signal(20);
  readonly sort = signal('code');
  readonly loading = signal(false);
  readonly error = signal<string | null>(null);
  readonly uomOptions = signal<Uom[]>([]);
  readonly brandOptions = signal<MasterCategory[]>([]);
  readonly categoryOptions = signal<ItemCategoryRow[]>([]);
  readonly dialogOpen = signal(false);
  readonly editing = signal<Item | null>(null);
  readonly conversionItem = signal<Item | null>(null);
  readonly saving = signal(false);
  readonly saveError = signal<string | null>(null);
  readonly searchControl = new FormControl('', { nonNullable: true });
  readonly form = new FormGroup({
    code: new FormControl('', { nonNullable: true, validators: [Validators.required, Validators.pattern(/^[A-Za-z0-9_-]{1,30}$/)] }),
    name: new FormControl('', { nonNullable: true, validators: [Validators.required, Validators.pattern(/.*\S.*/), Validators.maxLength(200)] }),
    specification: new FormControl('', { nonNullable: true, validators: [Validators.maxLength(500)] }),
    baseUomId: new FormControl<number | null>(null, Validators.required),
    brandId: new FormControl<number | null>(null),
    categoryId: new FormControl<number | null>(null),
    isActive: new FormControl(true, { nonNullable: true }),
  });
  constructor() {
    effect(() => { this.companyId(); untracked(() => {
      this.closeDialog(); this.conversionItem.set(null); this.page.set(1); this.reload(); this.loadReferences();
    }); });
  }
  indent(level: number): string { return '　'.repeat(level); }
  uomName(id: number): string { return this.uomOptions().find((x) => x.id === id)?.name ?? ''; }
  brandName(id: number | null): string { return this.brandOptions().find((x) => x.id === id)?.name ?? ''; }
  categoryName(id: number | null): string { return this.categoryOptions().find((x) => x.id === id)?.name ?? ''; }
  sortOrder(field: 'code' | 'name'): 'ascend' | 'descend' | null {
    return this.sort() === field ? 'ascend' : this.sort() === `-${field}` ? 'descend' : null;
  }
  search(): void { this.page.set(1); this.reload(); }
  onQueryParams(params: NzTableQueryParams): void {
    const active = params.sort.find((x) => x.value !== null);
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
  loadReferences(): void {
    if (this.permissions.has('MD.UOM.VIEW')) this.uomRequest.run(this.allPages((page) => this.uomApi.list({ page, pageSize: 200, sort: 'code' })), {
      next: (items) => this.uomOptions.set(items), error: () => this.uomOptions.set([]),
    }); else this.uomOptions.set([]);
    if (this.permissions.has('MD.ITEM_BRAND.VIEW')) this.brandRequest.run(this.allPages((page) => this.brandApi.list({ page, pageSize: 200, sort: 'code' })), {
      next: (items) => this.brandOptions.set(items), error: () => this.brandOptions.set([]),
    }); else this.brandOptions.set([]);
    if (this.permissions.has('MD.ITEM_CATEGORY.VIEW')) this.categoryRequest.run(this.categoryApi.list(), {
      next: (items) => this.categoryOptions.set(flattenCategoryTree(items)), error: () => this.categoryOptions.set([]),
    }); else this.categoryOptions.set([]);
  }
  /** 下拉選項跨頁載齊，避免第 201 筆起無法選用或辨識既有參照。 */
  private allPages<T>(get: (page: number) => Observable<PagedResult<T>>): Observable<T[]> {
    let page = 1;
    let loaded = 0;
    return get(page).pipe(
      expand((result) => {
        loaded += result.items.length;
        return loaded < result.totalCount && result.items.length > 0 ? get(++page) : EMPTY;
      }),
      map((result) => result.items),
      reduce((all, items) => [...all, ...items], [] as T[]),
    );
  }
  openCreate(): void {
    this.editing.set(null); this.form.reset({ code: '', name: '', specification: '', baseUomId: null,
      brandId: null, categoryId: null, isActive: true });
    this.form.controls.code.enable(); this.saveError.set(null); this.dialogOpen.set(true);
  }
  openEdit(row: Item): void {
    this.editing.set(row); this.form.reset({ code: row.code, name: row.name, specification: row.specification ?? '',
      baseUomId: row.baseUomId, brandId: row.brandId, categoryId: row.categoryId, isActive: row.isActive });
    this.form.controls.code.disable(); this.saveError.set(null); this.dialogOpen.set(true);
  }
  closeDialog(): void { this.dialogOpen.set(false); this.saveError.set(null); }
  save(): void {
    if (this.saving()) return;
    this.form.markAllAsTouched(); if (this.form.invalid) return;
    const draft = this.form.getRawValue(); const row = this.editing();
    const body = { name: draft.name, specification: draft.specification, baseUomId: draft.baseUomId!,
      brandId: draft.brandId, categoryId: draft.categoryId };
    const request = row ? this.api.update(row.id, { ...body, isActive: draft.isActive, version: row.version })
      : this.api.create({ ...body, code: draft.code });
    this.saving.set(true); this.saveError.set(null);
    request.subscribe({
      next: () => { this.saving.set(false); this.closeDialog(); this.reload(); },
      error: (error: unknown) => { this.saving.set(false); this.saveError.set(this.api.errorKey(error)); },
    });
  }
}
