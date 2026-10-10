import { ChangeDetectionStrategy, Component, computed, effect, inject, input, signal, untracked, viewChild } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { TranslatePipe } from '@ngx-translate/core';
import { NzAlertModule } from 'ng-zorro-antd/alert';
import { NzButtonModule } from 'ng-zorro-antd/button';
import { NzCheckboxModule } from 'ng-zorro-antd/checkbox';
import { NzInputModule } from 'ng-zorro-antd/input';
import { NzModalModule } from 'ng-zorro-antd/modal';
import { NzTableModule, NzTableQueryParams } from 'ng-zorro-antd/table';
import { NzTagModule } from 'ng-zorro-antd/tag';
import { catchError, of } from 'rxjs';
import { HasPermissionDirective } from '../../../core/auth/has-permission.directive';
import { CompanyContextService } from '../../../core/company/company-context.service';
import { OrganizationDraft, OrganizationForm, OrganizationFormValue } from './organization-form';
import {
  organizationErrorKey,
  OrganizationKind,
  OrganizationOption,
  OrganizationRecord,
  organizationPermission,
  OrganizationService,
} from './organization.service';

/** 下拉選項最多取 200 筆（後端分頁上限）。 */
const OPTION_PAGE_SIZE = 200;

/**
 * 公司／分店／部門維護頁（介面 8.58–8.60）：伺服器端分頁、排序、關鍵字搜尋；
 * 新增與編輯以對話框呼叫 API，按鈕依權限顯示。切換公司後重新載入（分店／部門屬於目前公司）。
 */
@Component({
  selector: 'erp-organization-list',
  imports: [
    FormsModule,
    TranslatePipe,
    NzAlertModule,
    NzButtonModule,
    NzCheckboxModule,
    NzInputModule,
    NzModalModule,
    NzTableModule,
    NzTagModule,
    HasPermissionDirective,
    OrganizationForm,
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <section class="erp-organization">
      <header class="erp-organization__header">
        <h1 data-testid="sys-organization-title">{{ ('sys.organization.' + kind() + '.title') | translate }}</h1>
        <button nz-button nzType="primary" data-testid="sys-organization-create" *erpHasPermission="createPermission()"
          (click)="openCreate()">
          {{ 'common.action.add' | translate }}
        </button>
      </header>
      <div class="erp-organization__filters">
        <input nz-input id="organization-search" data-testid="sys-organization-search" type="search"
          [attr.aria-label]="'sys.organization.search' | translate"
          [placeholder]="'sys.organization.search' | translate"
          [ngModel]="keyword()" (ngModelChange)="keyword.set($event)" (keyup.enter)="search()" />
        <button nz-button data-testid="sys-organization-search-button" (click)="search()">
          {{ 'common.action.search' | translate }}
        </button>
        @if (kind() === 'company') {
          <label nz-checkbox data-testid="sys-organization-include-inactive"
            [ngModel]="includeInactive()" (ngModelChange)="includeInactive.set($event); search()">
            {{ 'sys.organization.includeInactive' | translate }}
          </label>
        }
      </div>
      @if (loadError()) {
        <nz-alert nzType="error" nzShowIcon data-testid="sys-organization-load-error" [nzMessage]="loadError()! | translate" />
      }
      <nz-table #table data-testid="sys-organization-table" nzSize="middle" [nzData]="rows()" [nzLoading]="loading()"
        [nzFrontPagination]="false" [nzTotal]="total()" [nzPageIndex]="page()" [nzPageSize]="pageSize()"
        nzShowSizeChanger [nzNoResult]="'sys.organization.empty' | translate" (nzQueryParams)="onQueryParams($event)">
        <thead>
          <tr>
            <th nzColumnKey="code" [nzSortFn]="true" [nzSortOrder]="sortOrder('code')">{{ 'sys.organization.code' | translate }}</th>
            <th nzColumnKey="name" [nzSortFn]="true" [nzSortOrder]="sortOrder('name')">{{ 'sys.organization.name' | translate }}</th>
            @if (kind() === 'department') {
              <th>{{ 'sys.organization.branchRef' | translate }}</th>
              <th>{{ 'sys.organization.parentDepartment' | translate }}</th>
            }
            <th>{{ 'sys.organization.status' | translate }}</th>
            <th>{{ 'sys.organization.actions' | translate }}</th>
          </tr>
        </thead>
        <tbody>
          @for (row of table.data; track row.id) {
            <tr [attr.data-testid]="'sys-organization-row-' + row.code">
              <td>{{ row.code }}</td>
              <td>{{ row.name }}</td>
              @if (kind() === 'department') {
                <td>{{ label(branchOptions(), row.branchId) }}</td>
                <td>{{ label(departmentOptions(), row.parentDepartmentId) }}</td>
              }
              <td>
                <nz-tag [nzColor]="row.isActive ? 'success' : 'default'">
                  {{ (row.isActive ? 'sys.organization.active' : 'sys.organization.inactive') | translate }}
                </nz-tag>
              </td>
              <td>
                <button nz-button nzType="link" nzSize="small" *erpHasPermission="editPermission()"
                  [attr.data-testid]="'sys-organization-edit-' + row.code" (click)="openEdit(row)">
                  {{ 'common.action.edit' | translate }}
                </button>
              </td>
            </tr>
          }
        </tbody>
      </nz-table>
    </section>

    <nz-modal [nzVisible]="dialogOpen()" [nzTitle]="('sys.organization.' + kind() + '.formTitle') | translate"
      [nzOkText]="'common.action.save' | translate" [nzCancelText]="'common.action.cancel' | translate"
      [nzOkLoading]="saving()" (nzOnOk)="form()?.submit()" (nzOnCancel)="closeDialog()">
      <ng-container *nzModalContent>
        @if (saveError()) {
          <nz-alert nzType="error" nzShowIcon data-testid="sys-organization-save-error" [nzMessage]="saveError()! | translate" />
        }
        <erp-organization-form [kind]="kind()" [value]="editing()" [branches]="activeBranches()"
          [departments]="parentChoices()" (save)="persist($event)" />
      </ng-container>
    </nz-modal>
  `,
  styles: `
    .erp-organization { display: grid; gap: var(--erp-space-2, 16px); color: var(--erp-color-text); }
    .erp-organization__header { display: flex; align-items: center; justify-content: space-between; gap: 16px; }
    .erp-organization__header h1 { margin: 0; font-size: 20px; }
    .erp-organization__filters { display: flex; flex-wrap: wrap; align-items: center; gap: 8px; }
    .erp-organization__filters input { max-width: 320px; }
    nz-alert { margin-bottom: 12px; }
  `,
})
export class OrganizationList {
  private readonly api = inject(OrganizationService);
  private readonly company = inject(CompanyContextService);

  /** 由路由 data 綁定（withComponentInputBinding）。 */
  readonly kind = input.required<OrganizationKind>();

  readonly rows = signal<OrganizationRecord[]>([]);
  readonly total = signal(0);
  readonly page = signal(1);
  readonly pageSize = signal(20);
  readonly sort = signal('code');
  readonly keyword = signal('');
  readonly includeInactive = signal(false);
  readonly loading = signal(false);
  readonly loadError = signal<string | null>(null);

  readonly dialogOpen = signal(false);
  readonly editing = signal<(OrganizationFormValue & { id: number; version: number }) | null>(null);
  readonly saving = signal(false);
  readonly saveError = signal<string | null>(null);

  readonly branchOptions = signal<OrganizationRecord[]>([]);
  readonly departmentOptions = signal<OrganizationRecord[]>([]);

  readonly form = viewChild(OrganizationForm);
  readonly createPermission = computed(() => organizationPermission(this.kind(), 'CREATE'));
  readonly editPermission = computed(() => organizationPermission(this.kind(), 'EDIT'));
  readonly activeBranches = computed(() => this.branchOptions().filter((b) => b.isActive));
  /** 上級部門：啟用中且不是正在編輯的部門本身（循環由後端再檢查）。 */
  readonly parentChoices = computed(() =>
    this.departmentOptions().filter((d) => d.isActive && d.id !== this.editing()?.id),
  );

  /** 只比較公司 ID：重新載入權限會換掉公司物件，但公司沒變時不必重新查詢。 */
  private readonly companyId = computed(() => this.company.current()?.id ?? null);

  constructor() {
    // 首次載入及切換公司時重新查詢（分店／部門屬於目前公司）
    effect(() => {
      this.companyId();
      this.kind();
      untracked(() => {
        this.page.set(1);
        this.reload();
      });
    });
  }

  sortOrder(field: 'code' | 'name'): 'ascend' | 'descend' | null {
    const sort = this.sort();
    if (sort === field) return 'ascend';
    if (sort === `-${field}`) return 'descend';
    return null;
  }

  label(options: readonly OrganizationOption[], id: number | null | undefined): string {
    if (id == null) return '';
    const option = options.find((o) => o.id === id);
    return option ? `${option.code} — ${option.name}` : '';
  }

  search(): void {
    this.page.set(1);
    this.reload();
  }

  onQueryParams(params: NzTableQueryParams): void {
    const sorted = params.sort.find((s) => s.value !== null);
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
    const kind = this.kind();
    this.api
      .list(kind, {
        page: this.page(),
        pageSize: this.pageSize(),
        keyword: this.keyword(),
        sort: this.sort(),
        includeInactive: this.includeInactive(),
      })
      .subscribe({
        next: (result) => {
          this.rows.set(result.items);
          this.total.set(result.totalCount);
          this.loading.set(false);
        },
        error: (error: unknown) => {
          this.rows.set([]);
          this.total.set(0);
          this.loadError.set(organizationErrorKey(error));
          this.loading.set(false);
        },
      });
    if (kind === 'department') this.loadOptions();
  }

  openCreate(): void {
    this.editing.set(null);
    this.openDialog();
  }

  openEdit(row: OrganizationRecord): void {
    this.editing.set({
      id: row.id,
      version: row.version,
      code: row.code,
      name: row.name,
      isActive: row.isActive,
      branchId: row.branchId ?? null,
      parentDepartmentId: row.parentDepartmentId ?? null,
    });
    this.openDialog();
  }

  closeDialog(): void {
    this.dialogOpen.set(false);
    this.saveError.set(null);
  }

  persist(draft: OrganizationDraft): void {
    const kind = this.kind();
    const editing = this.editing();
    const references = kind === 'department' ? { branchId: draft.branchId, parentDepartmentId: draft.parentDepartmentId } : {};
    const request = editing
      ? this.api.update(kind, editing.id, { name: draft.name, isActive: draft.isActive, version: editing.version, ...references })
      : this.api.create(kind, { code: draft.code, name: draft.name, ...references });
    this.saving.set(true);
    this.saveError.set(null);
    request.subscribe({
      next: () => {
        this.saving.set(false);
        this.dialogOpen.set(false);
        this.reload();
      },
      error: (error: unknown) => {
        this.saving.set(false);
        this.saveError.set(organizationErrorKey(error));
      },
    });
  }

  private openDialog(): void {
    this.saveError.set(null);
    this.dialogOpen.set(true);
  }

  /** 部門頁的分店與上級部門選項；無分店檢視權限等錯誤時以空清單繼續。 */
  private loadOptions(): void {
    const query = { page: 1, pageSize: OPTION_PAGE_SIZE, sort: 'code' };
    this.api
      .list('branch', query)
      .pipe(catchError(() => of({ items: [] as OrganizationRecord[] })))
      .subscribe((result) => this.branchOptions.set(result.items));
    this.api
      .list('department', query)
      .pipe(catchError(() => of({ items: [] as OrganizationRecord[] })))
      .subscribe((result) => this.departmentOptions.set(result.items));
  }
}
