import { ChangeDetectionStrategy, Component, computed, effect, inject, input, output, signal, untracked } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormControl, FormGroup, ReactiveFormsModule } from '@angular/forms';
import { TranslatePipe } from '@ngx-translate/core';
import { NzAlertModule } from 'ng-zorro-antd/alert';
import { NzCheckboxModule } from 'ng-zorro-antd/checkbox';
import { NzModalModule } from 'ng-zorro-antd/modal';
import { NzSelectModule } from 'ng-zorro-antd/select';
import { NzTagModule } from 'ng-zorro-antd/tag';
import { catchError, of } from 'rxjs';
import { CompanyContextService } from '../../../core/company/company-context.service';
import { OrganizationOption } from '../organization/organization.service';
import { Role, roleErrorKey, RoleService } from '../roles/role.service';
import { UserCompanyAssignment, userErrorKey, UserService, UserSummary } from './user.service';

/** 角色下拉最多載入的筆數（角色全系統共用，數量有限）。 */
export const ROLE_OPTION_LIMIT = 200;

/**
 * 用戶權限對話框（介面 8.65，BlueWhale 4.4.1.8.26 User Permission Edit Interface，REQ_F4118.25.6、REQ_F4118.25.8）：
 * 選擇操作者可管理的公司，設定分店／部門、預設公司、啟用與角色（整組取代）。`user` 非 null 時開啟。
 */
@Component({
  selector: 'erp-user-permission-dialog',
  imports: [ReactiveFormsModule, TranslatePipe, NzAlertModule, NzCheckboxModule, NzModalModule, NzSelectModule, NzTagModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <nz-modal [nzVisible]="user() !== null" [nzTitle]="'sys.user.permissionTitle' | translate: { username: user()?.username ?? '' }"
      nzWidth="640px" [nzOkText]="'common.action.save' | translate" [nzCancelText]="'common.action.cancel' | translate"
      [nzOkLoading]="saving()" [nzOkDisabled]="loading() || companyId() === null" (nzOnOk)="save()" (nzOnCancel)="closed.emit(false)">
      <ng-container *nzModalContent>
        @if (error()) {
          <nz-alert nzType="error" nzShowIcon data-testid="sys-user-permission-error" [nzMessage]="error()! | translate" />
        }
        <form class="erp-user-permission" [formGroup]="form" (ngSubmit)="save()" data-testid="sys-user-permission-form">
          <label for="permission-company">{{ 'sys.user.company' | translate }}</label>
          <nz-select nzId="permission-company" data-testid="sys-user-permission-company" [nzLoading]="loading()"
            [formControl]="company">
            @for (option of companies(); track option.id) {
              <nz-option [nzValue]="option.id" [nzLabel]="option.code + ' ' + option.name" />
            }
          </nz-select>
          <div class="erp-user-permission__status" data-testid="sys-user-permission-status">
            @if (current(); as assignment) {
              <nz-tag [nzColor]="assignment.isActive ? 'success' : 'default'">
                {{ (assignment.isActive ? 'sys.user.active' : 'sys.user.inactive') | translate }}
              </nz-tag>
              @if (assignment.isDefault) {
                <nz-tag nzColor="blue">{{ 'sys.user.defaultCompany' | translate }}</nz-tag>
              }
            } @else if (companyId() !== null && !loading()) {
              <nz-tag nzColor="warning" data-testid="sys-user-permission-new">{{ 'sys.user.notAssigned' | translate }}</nz-tag>
            }
          </div>
          <label for="permission-branch">{{ 'sys.user.branch' | translate }}</label>
          <nz-select nzId="permission-branch" data-testid="sys-user-permission-branch" formControlName="branchId" nzAllowClear
            [nzPlaceHolder]="'sys.user.none' | translate">
            @for (option of branches(); track option.id) {
              <nz-option [nzValue]="option.id" [nzLabel]="option.code + ' ' + option.name" />
            }
          </nz-select>
          <label for="permission-department">{{ 'sys.user.department' | translate }}</label>
          <nz-select nzId="permission-department" data-testid="sys-user-permission-department" formControlName="departmentId" nzAllowClear
            [nzPlaceHolder]="'sys.user.none' | translate">
            @for (option of departments(); track option.id) {
              <nz-option [nzValue]="option.id" [nzLabel]="option.code + ' ' + option.name" />
            }
          </nz-select>
          @if (organizationUnavailable()) {
            <small class="erp-user-permission__hint" data-testid="sys-user-permission-org-hint">{{ 'sys.user.organizationUnavailable' | translate }}</small>
          }
          <label for="permission-roles">{{ 'sys.user.roles' | translate }}</label>
          <nz-select nzId="permission-roles" data-testid="sys-user-permission-roles" nzMode="multiple" formControlName="roleIds"
            [nzPlaceHolder]="'sys.user.noRoles' | translate">
            @for (option of roleOptions(); track option.id) {
              <nz-option [nzValue]="option.id" [nzLabel]="option.code + ' ' + option.name" />
            }
          </nz-select>
          <label nz-checkbox data-testid="sys-user-permission-default" formControlName="isDefault">
            {{ 'sys.user.defaultCompany' | translate }}
          </label>
          <label nz-checkbox data-testid="sys-user-permission-active" formControlName="isActive">
            {{ 'sys.user.companyAccessActive' | translate }}
          </label>
          <button type="submit" hidden></button>
        </form>
      </ng-container>
    </nz-modal>
  `,
  styles: `
    .erp-user-permission { display: grid; gap: 8px; color: var(--erp-color-text); }
    .erp-user-permission nz-select { width: 100%; }
    .erp-user-permission__status { min-height: 22px; }
    .erp-user-permission__hint { color: var(--erp-color-text-secondary, inherit); }
    nz-alert { margin-bottom: 12px; }
  `,
})
export class UserPermissionDialog {
  private readonly users = inject(UserService);
  private readonly roles = inject(RoleService);
  private readonly companyContext = inject(CompanyContextService);

  /** 要設定權限的用戶；null 時關閉。 */
  readonly user = input<UserSummary | null>(null);
  /** 關閉對話框；`true` 表示已保存。 */
  readonly closed = output<boolean>();

  /** 操作者可切換（可管理）的公司。 */
  readonly companies = this.companyContext.companies;
  readonly assignments = signal<UserCompanyAssignment[]>([]);
  readonly roleOptions = signal<Role[]>([]);
  readonly branches = signal<OrganizationOption[]>([]);
  readonly departments = signal<OrganizationOption[]>([]);
  /** 操作者在所選公司沒有分店／部門檢視權限，或查詢失敗。 */
  readonly organizationUnavailable = signal(false);
  readonly loading = signal(false);
  readonly saving = signal(false);
  readonly error = signal<string | null>(null);

  readonly company = new FormControl<number | null>(null);
  readonly companyId = signal<number | null>(null);
  readonly form = new FormGroup({
    branchId: new FormControl<number | null>(null),
    departmentId: new FormControl<number | null>(null),
    isDefault: new FormControl(false, { nonNullable: true }),
    isActive: new FormControl(true, { nonNullable: true }),
    roleIds: new FormControl<number[]>([], { nonNullable: true }),
  });

  /** 所選公司的現有指派；尚未授權時為 null。 */
  readonly current = computed(() => this.assignments().find((a) => a.companyId === this.companyId()) ?? null);

  constructor() {
    effect(() => {
      const user = this.user();
      untracked(() => (user ? this.open(user) : this.reset()));
    });
    this.company.valueChanges.pipe(takeUntilDestroyed()).subscribe((companyId) => this.selectCompany(companyId));
  }

  save(): void {
    const user = this.user();
    const companyId = this.companyId();
    if (!user || companyId === null || this.saving() || this.loading()) return;
    const value = this.form.getRawValue();
    this.saving.set(true);
    this.error.set(null);
    this.users
      .assign(user.id, companyId, {
        branchId: value.branchId,
        departmentId: value.departmentId,
        isDefault: value.isDefault,
        isActive: value.isActive,
        roleIds: [...value.roleIds].sort((a, b) => a - b),
      })
      .subscribe({
        next: () => {
          this.saving.set(false);
          this.closed.emit(true);
        },
        error: (error: unknown) => {
          this.saving.set(false);
          this.error.set(userErrorKey(error));
        },
      });
  }

  private open(user: UserSummary): void {
    this.reset();
    this.loading.set(true);
    let pending = 2;
    // 載入期間已關閉或改開另一位用戶：回應一律不套用
    const settle = (apply: () => void) => {
      if (this.user() !== user) return;
      apply();
      if (--pending > 0) return;
      this.loading.set(false);
      // 預設選目前公司；指派載入後才套用，表單才會帶出現有設定
      this.company.setValue(this.companyContext.current()?.id ?? this.companies()[0]?.id ?? null);
    };
    this.users.assignments(user.id).subscribe({
      next: (assignments) => settle(() => this.assignments.set(assignments)),
      error: (error: unknown) => settle(() => this.error.set(userErrorKey(error))),
    });
    this.roles.list({ page: 1, pageSize: ROLE_OPTION_LIMIT, sort: 'code' }).subscribe({
      next: (result) => settle(() => this.roleOptions.set(result.items.filter((role) => role.isActive))),
      error: (error: unknown) => settle(() => this.error.set(roleErrorKey(error))),
    });
  }

  private selectCompany(companyId: number | null): void {
    this.companyId.set(companyId);
    const assignment = this.current();
    this.form.reset({
      branchId: assignment?.branchId ?? null,
      departmentId: assignment?.departmentId ?? null,
      isDefault: assignment?.isDefault ?? false,
      isActive: assignment?.isActive ?? true,
      roleIds: assignment?.roleIds ?? [],
    });
    this.branches.set([]);
    this.departments.set([]);
    this.organizationUnavailable.set(false);
    if (companyId === null) return;
    const unavailable = () => {
      this.organizationUnavailable.set(true);
      return of<OrganizationOption[]>([]);
    };
    // 切換公司後才回來的舊回應不套用
    this.users.organizationOptions('branch', companyId).pipe(catchError(unavailable)).subscribe((options) => {
      if (this.companyId() === companyId) this.branches.set(options);
    });
    this.users.organizationOptions('department', companyId).pipe(catchError(unavailable)).subscribe((options) => {
      if (this.companyId() === companyId) this.departments.set(options);
    });
  }

  private reset(): void {
    this.assignments.set([]);
    this.error.set(null);
    this.loading.set(false);
    this.saving.set(false);
    this.company.setValue(null, { emitEvent: false });
    this.selectCompany(null);
  }
}
