import { ChangeDetectionStrategy, Component, computed, effect, inject, signal, untracked } from '@angular/core';
import { FormControl, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { TranslatePipe } from '@ngx-translate/core';
import { NzAlertModule } from 'ng-zorro-antd/alert';
import { NzButtonModule } from 'ng-zorro-antd/button';
import { NzInputModule } from 'ng-zorro-antd/input';
import { NzModalModule } from 'ng-zorro-antd/modal';
import { NzTableModule } from 'ng-zorro-antd/table';
import { NzTagModule } from 'ng-zorro-antd/tag';
import { HasPermissionDirective } from '../../core/auth/has-permission.directive';
import { CompanyContextService } from '../../core/company/company-context.service';
import { ErpDialogService } from '../../shared/dialogs/dialog.service';
import { latestRequest } from '../../shared/list-page/latest-request';
import { isStaleRowError } from '../../shared/list-page/stale-row';
import {
  filterCategoryRows, flattenCategoryTree, ITEM_CATEGORY_MAX_DEPTH, ItemCategoryRow, ItemCategoryService, parentOptions,
} from './item-category.service';

/** 物料分類樹維護頁（介面 8.36–8.38）：樹狀列表、新增（可指定上層）、修改（含搬移）、刪除；權限 `MD.ITEM_CATEGORY.*`。 */
@Component({
  selector: 'erp-item-category-page',
  imports: [ReactiveFormsModule, TranslatePipe, NzAlertModule, NzButtonModule, NzInputModule,
    NzModalModule, NzTableModule, NzTagModule, HasPermissionDirective],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <section class="md-item-categories">
      <header><h1 data-testid="md-item-category-title">{{ 'md.itemCategory.title' | translate }}</h1>
        <button nz-button nzType="primary" data-testid="md-item-category-create"
          *erpHasPermission="'MD.ITEM_CATEGORY.CREATE'" (click)="openCreate(null)">{{ 'common.action.add' | translate }}</button>
      </header>
      <div class="filters">
        <input nz-input type="search" data-testid="md-item-category-search" [formControl]="searchControl"
          [placeholder]="'md.itemCategory.search' | translate" (keyup.enter)="search()" />
        <button nz-button data-testid="md-item-category-search-button" (click)="search()">{{ 'common.action.search' | translate }}</button>
      </div>
      @if (error()) { <nz-alert nzType="error" nzShowIcon data-testid="md-item-category-error" [nzMessage]="error()! | translate" /> }
      <nz-table #table data-testid="md-item-category-table" nzSize="middle" [nzData]="visibleRows()" [nzLoading]="loading()"
        [nzShowPagination]="false" [nzPageSize]="visibleRows().length || 1" [nzNoResult]="'md.itemCategory.empty' | translate">
        <thead><tr>
          <th>{{ 'md.itemCategory.code' | translate }}</th><th>{{ 'md.itemCategory.name' | translate }}</th>
          <th>{{ 'md.itemCategory.status' | translate }}</th><th>{{ 'md.itemCategory.actions' | translate }}</th>
        </tr></thead>
        <tbody>@for (row of table.data; track row.id) {
          <tr [attr.data-testid]="'md-item-category-row-' + row.code" [attr.aria-level]="row.level + 1">
            <td [nzIndentSize]="row.level * 24">{{ row.code }}</td><td>{{ row.name }}</td>
            <td><nz-tag [nzColor]="row.isActive ? 'success' : 'default'">
              {{ (row.isActive ? 'md.itemCategory.active' : 'md.itemCategory.inactive') | translate }}
            </nz-tag></td>
            <td>
              @if (row.isActive && row.level + 1 < maxDepth) {
                <button nz-button nzType="link" *erpHasPermission="'MD.ITEM_CATEGORY.CREATE'"
                  [attr.data-testid]="'md-item-category-add-child-' + row.code" (click)="openCreate(row)">
                  {{ 'md.itemCategory.addChild' | translate }}</button>
              }
              <button nz-button nzType="link" *erpHasPermission="'MD.ITEM_CATEGORY.EDIT'"
                [attr.data-testid]="'md-item-category-edit-' + row.code" (click)="openEdit(row)">{{ 'common.action.edit' | translate }}</button>
              <button nz-button nzType="link" nzDanger *erpHasPermission="'MD.ITEM_CATEGORY.DELETE'"
                [attr.data-testid]="'md-item-category-delete-' + row.code" [disabled]="busy()"
                (click)="confirmDelete(row)">{{ 'common.action.delete' | translate }}</button>
            </td>
          </tr>
        }</tbody>
      </nz-table>
    </section>

    <nz-modal [nzVisible]="dialogOpen()" [nzTitle]="'md.itemCategory.formTitle' | translate"
      [nzOkText]="'common.action.save' | translate" [nzCancelText]="'common.action.cancel' | translate"
      [nzOkLoading]="saving()" (nzOnOk)="save()" (nzOnCancel)="closeDialog()">
      <ng-container *nzModalContent>
        @if (saveError()) { <nz-alert nzType="error" nzShowIcon data-testid="md-item-category-save-error" [nzMessage]="saveError()! | translate" /> }
        <form class="item-category-form" [formGroup]="form" (ngSubmit)="save()" data-testid="md-item-category-form">
          <label for="item-category-code">{{ 'md.itemCategory.code' | translate }}</label>
          <input nz-input id="item-category-code" data-testid="md-item-category-code" formControlName="code" maxlength="30" />
          @if (form.controls.code.touched && form.controls.code.invalid) { <p role="alert">{{ 'md.itemCategory.invalidCode' | translate }}</p> }
          <label for="item-category-name">{{ 'md.itemCategory.name' | translate }}</label>
          <input nz-input id="item-category-name" data-testid="md-item-category-name" formControlName="name" maxlength="200" />
          @if (form.controls.name.touched && form.controls.name.invalid) { <p role="alert">{{ 'md.itemCategory.invalidName' | translate }}</p> }
          <label for="item-category-parent">{{ 'md.itemCategory.parent' | translate }}</label>
          <select id="item-category-parent" data-testid="md-item-category-parent" formControlName="parentId">
            <option [ngValue]="null">{{ 'md.itemCategory.noParent' | translate }}</option>
            @for (option of parentChoices(); track option.id) {
              <option [ngValue]="option.id">{{ indent(option.level) }}{{ option.code }} — {{ option.name }}</option>
            }
          </select>
          @if (editing()) { <label><input type="checkbox" formControlName="isActive" data-testid="md-item-category-active" />
            {{ 'md.itemCategory.active' | translate }}</label> }
        </form>
      </ng-container>
    </nz-modal>
  `,
  styles: `
    .md-item-categories { display: grid; gap: 16px; color: var(--erp-color-text); }
    header { display: flex; justify-content: space-between; align-items: center; }
    h1 { margin: 0; font-size: 20px; }
    .filters { display: flex; gap: 8px; }
    .filters input { max-width: 320px; }
    .item-category-form { display: grid; gap: 8px; }
    .item-category-form p { color: var(--erp-status-error); margin: 0; }
  `,
})
export class ItemCategoryPage {
  readonly maxDepth = ITEM_CATEGORY_MAX_DEPTH;
  private readonly api = inject(ItemCategoryService);
  private readonly company = inject(CompanyContextService);
  private readonly dialogs = inject(ErpDialogService);
  private readonly listRequest = latestRequest();
  private readonly companyId = computed(() => this.company.current()?.id ?? null);
  readonly rows = signal<ItemCategoryRow[]>([]);
  readonly keyword = signal('');
  readonly visibleRows = computed(() => filterCategoryRows(this.rows(), this.keyword()));
  readonly loading = signal(false);
  readonly error = signal<string | null>(null);
  readonly dialogOpen = signal(false);
  readonly editing = signal<ItemCategoryRow | null>(null);
  readonly parentChoices = computed(() => parentOptions(this.rows(), this.editing()));
  readonly saving = signal(false);
  readonly saveError = signal<string | null>(null);
  readonly busy = signal(false);
  readonly searchControl = new FormControl('', { nonNullable: true });
  readonly form = new FormGroup({
    code: new FormControl('', { nonNullable: true, validators: [Validators.required, Validators.pattern(/^[A-Za-z0-9_-]{1,30}$/)] }),
    name: new FormControl('', { nonNullable: true, validators: [Validators.required, Validators.pattern(/.*\S.*/), Validators.maxLength(200)] }),
    parentId: new FormControl<number | null>(null),
    isActive: new FormControl(true, { nonNullable: true }),
  });

  constructor() {
    effect(() => {
      this.companyId();
      untracked(() => {
        this.closeDialog();
        this.reload();
      });
    });
  }

  /** 下拉選單內以全形空白表示層級。 */
  indent(level: number): string { return '　'.repeat(level); }

  /** 樹一次載入，搜尋只在前端過濾（保留命中節點的上層）。 */
  search(): void { this.keyword.set(this.searchControl.value); }

  reload(): void {
    this.loading.set(true);
    this.error.set(null);
    this.listRequest.run(this.api.list(), {
      next: (items) => { this.rows.set(flattenCategoryTree(items)); this.loading.set(false); },
      error: (error: unknown) => { this.rows.set([]); this.error.set(this.api.errorKey(error)); this.loading.set(false); },
    });
  }

  openCreate(parent: ItemCategoryRow | null): void {
    this.editing.set(null);
    this.form.reset({ code: '', name: '', parentId: parent?.id ?? null, isActive: true });
    this.form.controls.code.enable();
    this.saveError.set(null);
    this.dialogOpen.set(true);
  }

  openEdit(row: ItemCategoryRow): void {
    this.editing.set(row);
    this.form.reset({ code: row.code, name: row.name, parentId: row.parentId, isActive: row.isActive });
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
      ? this.api.update(row.id, { name: draft.name, isActive: draft.isActive, parentId: draft.parentId, version: row.version })
      : this.api.create({ code: draft.code, name: draft.name, parentId: draft.parentId });
    this.saving.set(true);
    this.saveError.set(null);
    request.subscribe({
      next: () => { this.saving.set(false); this.closeDialog(); this.reload(); },
      error: (error: unknown) => { this.saving.set(false); this.saveError.set(this.api.errorKey(error)); if (isStaleRowError(error)) this.reload(); },
    });
  }

  confirmDelete(row: ItemCategoryRow): void {
    if (this.busy()) return;
    this.dialogs.confirm({ titleKey: 'md.itemCategory.deleteConfirm', contentKey: 'common.confirm.irreversible',
      params: { code: row.code }, okKey: 'common.action.delete', danger: true }).subscribe((ok) => {
      if (!ok) return;
      this.busy.set(true);
      this.api.remove(row.id, row.version).subscribe({
        next: () => { this.busy.set(false); this.reload(); },
        error: (error: unknown) => { this.busy.set(false); if (isStaleRowError(error)) this.reload(); this.error.set(this.api.errorKey(error)); },
      });
    });
  }
}
