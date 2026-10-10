import { ChangeDetectionStrategy, Component, computed, effect, inject, signal, untracked } from '@angular/core';
import { AbstractControl, FormControl, FormGroup, FormsModule, ReactiveFormsModule, ValidationErrors, Validators } from '@angular/forms';
import { TranslatePipe } from '@ngx-translate/core';
import { NzAlertModule } from 'ng-zorro-antd/alert';
import { NzButtonModule } from 'ng-zorro-antd/button';
import { NzCheckboxModule } from 'ng-zorro-antd/checkbox';
import { NzInputModule } from 'ng-zorro-antd/input';
import { NzModalModule } from 'ng-zorro-antd/modal';
import { NzPopconfirmModule } from 'ng-zorro-antd/popconfirm';
import { NzSelectModule } from 'ng-zorro-antd/select';
import { NzTableModule, NzTableQueryParams } from 'ng-zorro-antd/table';
import { NzTagModule } from 'ng-zorro-antd/tag';
import { Observable } from 'rxjs';
import { DataScope } from '../../../core/auth/permission.service';
import { HasPermissionDirective } from '../../../core/auth/has-permission.directive';
import { CompanyContextService } from '../../../core/company/company-context.service';
import {
  DATA_SCOPES,
  dataScopeKey,
  groupPermissions,
  PermissionGroup,
  Role,
  ROLE_CODE_PATTERN,
  ROLE_DESCRIPTION_MAX_LENGTH,
  ROLE_NAME_MAX_LENGTH,
  roleErrorKey,
  RoleService,
} from './role.service';

/** 對話框：新增角色，或編輯某角色。 */
export type RoleDialog = { mode: 'create' } | { mode: 'edit'; role: Role };

/** 代碼去除前後空白後須符合格式（送出時也會去除空白並轉大寫）。 */
function roleCodeFormat(control: AbstractControl<string>): ValidationErrors | null {
  return ROLE_CODE_PATTERN.test(control.value.trim()) ? null : { code: true };
}

/** 名稱去除前後空白後不可為空。 */
function notBlank(control: AbstractControl<string>): ValidationErrors | null {
  return control.value.trim() ? null : { required: true };
}

const SORTABLE = ['code', 'name'] as const;

/**
 * 角色維護頁（介面 8.64，REQ_F4118.25.1–4）：伺服器端分頁、代碼／名稱排序與搜尋；
 * 新增／編輯對話框含資料範圍、全權與依模組分組的權限勾選；刪除使用中角色顯示 `SYS.ROLE.IN_USE`。
 */
@Component({
  selector: 'erp-role-list',
  imports: [
    FormsModule,
    ReactiveFormsModule,
    TranslatePipe,
    NzAlertModule,
    NzButtonModule,
    NzCheckboxModule,
    NzInputModule,
    NzModalModule,
    NzPopconfirmModule,
    NzSelectModule,
    NzTableModule,
    NzTagModule,
    HasPermissionDirective,
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <section class="erp-role">
      <header class="erp-role__header">
        <h1 data-testid="sys-role-title">{{ 'sys.role.title' | translate }}</h1>
        <button nz-button nzType="primary" data-testid="sys-role-create" *erpHasPermission="'SYS.ROLE.CREATE'"
          (click)="openCreate()">
          {{ 'common.action.add' | translate }}
        </button>
      </header>
      <div class="erp-role__filters">
        <input nz-input id="role-search" data-testid="sys-role-search" type="search"
          [attr.aria-label]="'sys.role.search' | translate" [placeholder]="'sys.role.search' | translate"
          [ngModel]="keyword()" (ngModelChange)="keyword.set($event)" (keyup.enter)="search()" />
        <button nz-button data-testid="sys-role-search-button" (click)="search()">
          {{ 'common.action.search' | translate }}
        </button>
      </div>
      @if (loadError()) {
        <nz-alert nzType="error" nzShowIcon data-testid="sys-role-load-error" [nzMessage]="loadError()! | translate" />
      }
      @if (actionError()) {
        <nz-alert nzType="error" nzShowIcon nzCloseable data-testid="sys-role-action-error"
          [nzMessage]="actionError()! | translate" (nzOnClose)="actionError.set(null)" />
      }
      @if (notice()) {
        <nz-alert nzType="success" nzShowIcon nzCloseable data-testid="sys-role-notice"
          [nzMessage]="notice()! | translate: noticeParams()" (nzOnClose)="notice.set(null)" />
      }
      <nz-table #table data-testid="sys-role-table" nzSize="middle" [nzData]="rows()" [nzLoading]="loading()"
        [nzFrontPagination]="false" [nzTotal]="total()" [nzPageIndex]="page()" [nzPageSize]="pageSize()"
        nzShowSizeChanger [nzNoResult]="'sys.role.empty' | translate" (nzQueryParams)="onQueryParams($event)">
        <thead>
          <tr>
            <th nzColumnKey="code" [nzSortFn]="true" [nzSortOrder]="sortOrder('code')">{{ 'sys.role.code' | translate }}</th>
            <th nzColumnKey="name" [nzSortFn]="true" [nzSortOrder]="sortOrder('name')">{{ 'sys.role.name' | translate }}</th>
            <th>{{ 'sys.role.dataScope' | translate }}</th>
            <th>{{ 'sys.role.permissions' | translate }}</th>
            <th>{{ 'sys.role.status' | translate }}</th>
            <th>{{ 'sys.role.actions' | translate }}</th>
          </tr>
        </thead>
        <tbody>
          @for (row of table.data; track row.id) {
            <tr [attr.data-testid]="'sys-role-row-' + row.code">
              <td>{{ row.code }}</td>
              <td>{{ row.name }}</td>
              <td>{{ scopeKey(row.dataScope) | translate }}</td>
              <td>
                @if (row.isFullAccess) {
                  <nz-tag nzColor="purple" [attr.data-testid]="'sys-role-full-' + row.code">{{ 'sys.role.fullAccess' | translate }}</nz-tag>
                } @else {
                  {{ 'sys.role.permissionCount' | translate: { count: row.permissions.length } }}
                }
              </td>
              <td>
                <nz-tag [nzColor]="row.isActive ? 'success' : 'default'">
                  {{ (row.isActive ? 'sys.role.active' : 'sys.role.inactive') | translate }}
                </nz-tag>
              </td>
              <td>
                <button nz-button nzType="link" nzSize="small" *erpHasPermission="'SYS.ROLE.EDIT'"
                  [attr.data-testid]="'sys-role-edit-' + row.code" (click)="openEdit(row)">
                  {{ 'common.action.edit' | translate }}
                </button>
                <button nz-button nzType="link" nzDanger nzSize="small" *erpHasPermission="'SYS.ROLE.DELETE'"
                  [attr.data-testid]="'sys-role-delete-' + row.code" [disabled]="busyId() === row.id"
                  nz-popconfirm [nzPopconfirmTitle]="'sys.role.deleteConfirm' | translate: { code: row.code }"
                  [nzOkText]="'common.action.delete' | translate" [nzCancelText]="'common.action.cancel' | translate"
                  (nzOnConfirm)="remove(row)">
                  {{ 'common.action.delete' | translate }}
                </button>
              </td>
            </tr>
          }
        </tbody>
      </nz-table>
    </section>

    <nz-modal [nzVisible]="dialog() !== null" [nzTitle]="dialogTitle() | translate: dialogTitleParams()" nzWidth="720px"
      [nzOkText]="'common.action.save' | translate" [nzCancelText]="'common.action.cancel' | translate"
      [nzOkLoading]="saving()" (nzOnOk)="submit()" (nzOnCancel)="closeDialog()">
      <ng-container *nzModalContent>
        @if (saveError()) {
          <nz-alert nzType="error" nzShowIcon data-testid="sys-role-save-error" [nzMessage]="saveError()! | translate" />
        }
        <form class="erp-role-form" [formGroup]="form" (ngSubmit)="submit()" data-testid="sys-role-form">
          @if (dialog()?.mode === 'create') {
            <label for="role-code">{{ 'sys.role.code' | translate }}</label>
            <input id="role-code" data-testid="sys-role-code" formControlName="code" maxlength="50" autocomplete="off" required />
            @if (form.controls.code.touched && form.controls.code.invalid) {
              <p role="alert">{{ 'sys.role.invalidCode' | translate }}</p>
            }
          }
          <label for="role-name">{{ 'sys.role.name' | translate }}</label>
          <input id="role-name" data-testid="sys-role-name" formControlName="name" [attr.maxlength]="nameMaxLength" required />
          @if (form.controls.name.touched && form.controls.name.invalid) {
            <p role="alert">{{ 'sys.role.invalidName' | translate }}</p>
          }
          <label for="role-description">{{ 'sys.role.description' | translate }}</label>
          <textarea id="role-description" data-testid="sys-role-description" formControlName="description" rows="2"
            [attr.maxlength]="descriptionMaxLength"></textarea>
          <label for="role-scope">{{ 'sys.role.dataScope' | translate }}</label>
          <nz-select nzId="role-scope" data-testid="sys-role-scope" formControlName="dataScope">
            @for (scope of dataScopes; track scope) {
              <nz-option [nzValue]="scope" [nzLabel]="scopeKey(scope) | translate" />
            }
          </nz-select>
          <small class="erp-role-form__hint">{{ 'sys.role.dataScopeHint' | translate }}</small>
          <label nz-checkbox data-testid="sys-role-full-access" formControlName="isFullAccess">
            {{ 'sys.role.fullAccess' | translate }}
          </label>
          @if (dialog()?.mode === 'edit') {
            <label nz-checkbox data-testid="sys-role-active" formControlName="isActive">{{ 'sys.role.active' | translate }}</label>
          }
          <fieldset class="erp-role-form__permissions" data-testid="sys-role-permissions">
            <legend>{{ 'sys.role.permissions' | translate }}</legend>
            @if (form.controls.isFullAccess.value) {
              <p class="erp-role-form__hint" data-testid="sys-role-full-access-hint">{{ 'sys.role.fullAccessHint' | translate }}</p>
            }
            @for (group of permissionGroups(); track group.module) {
              <div class="erp-role-form__group" [attr.data-testid]="'sys-role-module-' + group.module">
                <label nz-checkbox [attr.data-testid]="'sys-role-module-toggle-' + group.module"
                  [nzChecked]="groupState(group) === 'all'" [nzIndeterminate]="groupState(group) === 'some'"
                  [nzDisabled]="form.controls.isFullAccess.value" (nzCheckedChange)="toggleGroup(group, $event)">
                  <strong>{{ group.module }}</strong>
                </label>
                <div class="erp-role-form__codes">
                  @for (code of group.codes; track code) {
                    <label nz-checkbox [attr.data-testid]="'sys-role-permission-' + code" [nzChecked]="selected().has(code)"
                      [nzDisabled]="form.controls.isFullAccess.value" (nzCheckedChange)="togglePermission(code, $event)">
                      {{ code }}
                    </label>
                  }
                </div>
              </div>
            }
          </fieldset>
          <button type="submit" hidden></button>
        </form>
      </ng-container>
    </nz-modal>
  `,
  styles: `
    .erp-role { display: grid; gap: var(--erp-space-2, 16px); color: var(--erp-color-text); }
    .erp-role__header { display: flex; align-items: center; justify-content: space-between; gap: 16px; }
    .erp-role__header h1 { margin: 0; font-size: 20px; }
    .erp-role__filters { display: flex; flex-wrap: wrap; align-items: center; gap: 8px; }
    .erp-role__filters input { max-width: 320px; }
    nz-alert { margin-bottom: 12px; }
    .erp-role-form { display: grid; gap: 8px; color: var(--erp-color-text); }
    .erp-role-form input, .erp-role-form textarea {
      padding: 6px 8px; border: 1px solid var(--erp-color-border, #d9d9d9); border-radius: 6px;
      background: var(--erp-color-bg-container, #fff); color: var(--erp-color-text);
    }
    .erp-role-form p[role='alert'] { color: var(--erp-status-error); margin: 0; }
    .erp-role-form__hint { color: var(--erp-color-text-secondary, inherit); margin: 0; }
    .erp-role-form__permissions {
      display: grid; gap: 12px; margin: 8px 0 0; padding: 8px 12px;
      border: 1px solid var(--erp-color-border, #d9d9d9); border-radius: 6px; max-height: 320px; overflow: auto;
    }
    .erp-role-form__codes { display: flex; flex-wrap: wrap; gap: 4px 16px; padding-left: 24px; }
    .erp-role-form__codes label { margin-inline-start: 0; }
  `,
})
export class RoleList {
  private readonly api = inject(RoleService);
  private readonly company = inject(CompanyContextService);

  readonly dataScopes = DATA_SCOPES;
  readonly scopeKey = dataScopeKey;
  readonly nameMaxLength = ROLE_NAME_MAX_LENGTH;
  readonly descriptionMaxLength = ROLE_DESCRIPTION_MAX_LENGTH;

  readonly rows = signal<Role[]>([]);
  readonly total = signal(0);
  readonly page = signal(1);
  readonly pageSize = signal(20);
  readonly sort = signal('code');
  readonly keyword = signal('');
  readonly loading = signal(false);
  readonly loadError = signal<string | null>(null);

  /** 刪除進行中的角色 ID，避免重複送出。 */
  readonly busyId = signal<number | null>(null);
  readonly actionError = signal<string | null>(null);
  readonly notice = signal<string | null>(null);
  readonly noticeParams = signal<Record<string, string>>({});

  readonly dialog = signal<RoleDialog | null>(null);
  readonly saving = signal(false);
  readonly saveError = signal<string | null>(null);
  /** 權限目錄（第一次開啟對話框時載入，之後重用）。 */
  readonly permissionGroups = signal<PermissionGroup[]>([]);
  readonly selected = signal<ReadonlySet<string>>(new Set());

  readonly form = new FormGroup({
    code: new FormControl('', { nonNullable: true, validators: [Validators.required, roleCodeFormat] }),
    name: new FormControl('', { nonNullable: true, validators: [notBlank, Validators.maxLength(ROLE_NAME_MAX_LENGTH)] }),
    description: new FormControl('', { nonNullable: true, validators: [Validators.maxLength(ROLE_DESCRIPTION_MAX_LENGTH)] }),
    dataScope: new FormControl<DataScope>('Self', { nonNullable: true }),
    isFullAccess: new FormControl(false, { nonNullable: true }),
    isActive: new FormControl(true, { nonNullable: true }),
  });

  readonly dialogTitle = computed(() => (this.dialog()?.mode === 'edit' ? 'sys.role.editTitle' : 'sys.role.createTitle'));
  readonly dialogTitleParams = computed(() => {
    const dialog = this.dialog();
    return dialog?.mode === 'edit' ? { code: dialog.role.code } : {};
  });

  private catalogLoaded = false;
  private readonly companyId = computed(() => this.company.current()?.id ?? null);

  constructor() {
    // 角色不分公司，但權限在目前公司內檢查：切換公司後重新查詢
    effect(() => {
      this.companyId();
      untracked(() => {
        this.page.set(1);
        this.actionError.set(null);
        this.notice.set(null);
        this.reload();
      });
    });
  }

  sortOrder(column: (typeof SORTABLE)[number]): 'ascend' | 'descend' | null {
    const sort = this.sort();
    if (sort === column) return 'ascend';
    return sort === `-${column}` ? 'descend' : null;
  }

  search(): void {
    this.page.set(1);
    this.reload();
  }

  onQueryParams(params: NzTableQueryParams): void {
    const sorted = params.sort.find((s) => s.value !== null && (SORTABLE as readonly string[]).includes(s.key));
    const sort = sorted ? `${sorted.value === 'descend' ? '-' : ''}${sorted.key}` : 'code';
    if (params.pageIndex === this.page() && params.pageSize === this.pageSize() && sort === this.sort()) return;
    this.page.set(params.pageIndex);
    this.pageSize.set(params.pageSize);
    this.sort.set(sort);
    this.reload();
  }

  reload(): void {
    this.loading.set(true);
    this.loadError.set(null);
    this.api.list({ page: this.page(), pageSize: this.pageSize(), keyword: this.keyword(), sort: this.sort() }).subscribe({
      next: (result) => {
        this.rows.set(result.items);
        this.total.set(result.totalCount);
        this.loading.set(false);
      },
      error: (error: unknown) => {
        this.rows.set([]);
        this.total.set(0);
        this.loadError.set(roleErrorKey(error));
        this.loading.set(false);
      },
    });
  }

  openCreate(): void {
    this.form.reset();
    this.form.controls.code.enable();
    this.openDialog({ mode: 'create' }, []);
  }

  openEdit(role: Role): void {
    this.form.reset({
      code: role.code,
      name: role.name,
      description: role.description ?? '',
      dataScope: role.dataScope,
      isFullAccess: role.isFullAccess,
      isActive: role.isActive,
    });
    // 代碼建立後不可修改：停用其驗證
    this.form.controls.code.disable();
    this.openDialog({ mode: 'edit', role }, role.permissions);
  }

  closeDialog(): void {
    this.dialog.set(null);
    this.saveError.set(null);
    this.form.reset();
    this.selected.set(new Set());
  }

  groupState(group: PermissionGroup): 'all' | 'some' | 'none' {
    const selected = this.selected();
    const count = group.codes.filter((code) => selected.has(code)).length;
    if (count === 0) return 'none';
    return count === group.codes.length ? 'all' : 'some';
  }

  togglePermission(code: string, checked: boolean): void {
    const next = new Set(this.selected());
    if (checked) next.add(code);
    else next.delete(code);
    this.selected.set(next);
  }

  toggleGroup(group: PermissionGroup, checked: boolean): void {
    const next = new Set(this.selected());
    for (const code of group.codes) {
      if (checked) next.add(code);
      else next.delete(code);
    }
    this.selected.set(next);
  }

  submit(): void {
    const dialog = this.dialog();
    if (!dialog || this.saving()) return;
    this.form.markAllAsTouched();
    if (this.form.invalid) return;
    const value = this.form.getRawValue();
    const common = {
      name: value.name.trim(),
      description: value.description.trim() || null,
      dataScope: value.dataScope,
      isFullAccess: value.isFullAccess,
      permissions: [...this.selected()].sort(),
    };
    const code = dialog.mode === 'create' ? value.code.trim().toUpperCase() : dialog.role.code;
    const request: Observable<Role> =
      dialog.mode === 'create'
        ? this.api.create({ code, ...common })
        : this.api.update(dialog.role.id, { ...common, isActive: value.isActive, version: dialog.role.version });
    this.saving.set(true);
    this.saveError.set(null);
    request.subscribe({
      next: () => {
        this.saving.set(false);
        this.showNotice(dialog.mode === 'create' ? 'sys.role.created' : 'sys.role.updated', code);
        this.closeDialog();
        this.reload();
      },
      error: (error: unknown) => {
        this.saving.set(false);
        this.saveError.set(roleErrorKey(error));
      },
    });
  }

  remove(row: Role): void {
    if (this.busyId() !== null) return;
    this.busyId.set(row.id);
    this.actionError.set(null);
    this.notice.set(null);
    this.api.remove(row.id, row.version).subscribe({
      next: () => {
        this.busyId.set(null);
        this.showNotice('sys.role.deleted', row.code);
        this.reload();
      },
      error: (error: unknown) => {
        this.busyId.set(null);
        this.actionError.set(roleErrorKey(error));
      },
    });
  }

  private openDialog(dialog: RoleDialog, permissions: readonly string[]): void {
    this.selected.set(new Set(permissions));
    this.saveError.set(null);
    this.dialog.set(dialog);
    this.loadCatalog();
  }

  private loadCatalog(): void {
    if (this.catalogLoaded) return;
    this.api.permissions().subscribe({
      next: (entries) => {
        this.catalogLoaded = true;
        this.permissionGroups.set(groupPermissions(entries));
      },
      error: (error: unknown) => this.saveError.set(roleErrorKey(error)),
    });
  }

  private showNotice(key: string, code: string): void {
    this.noticeParams.set({ code });
    this.notice.set(key);
  }
}
