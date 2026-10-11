import { ChangeDetectionStrategy, Component, computed, effect, inject, signal, untracked } from '@angular/core';
import { AbstractControl, FormControl, FormGroup, FormsModule, ReactiveFormsModule, ValidationErrors, Validators } from '@angular/forms';
import { TranslatePipe } from '@ngx-translate/core';
import { NzAlertModule } from 'ng-zorro-antd/alert';
import { NzButtonModule } from 'ng-zorro-antd/button';
import { NzInputModule } from 'ng-zorro-antd/input';
import { NzModalModule } from 'ng-zorro-antd/modal';
import { NzTableModule, NzTableQueryParams } from 'ng-zorro-antd/table';
import { NzTagModule } from 'ng-zorro-antd/tag';
import { Observable } from 'rxjs';
import { HasPermissionDirective } from '../../../core/auth/has-permission.directive';
import { CompanyContextService } from '../../../core/company/company-context.service';
import { latestRequest } from '../../../shared/list-page/latest-request';
import { UserPermissionDialog } from './user-permission-dialog';
import { PASSWORD_MAX_LENGTH, USERNAME_PATTERN, userErrorKey, UserService, UserSummary } from './user.service';

/** 對話框：新增用戶，或為某用戶重設密碼。 */
export type UserDialog = { mode: 'create' } | { mode: 'reset'; user: UserSummary };

/** 確認密碼須與密碼相同。 */
function passwordsMatch(group: AbstractControl): ValidationErrors | null {
  const { password, confirm } = (group as FormGroup).getRawValue() as { password: string; confirm: string };
  return confirm && password !== confirm ? { mismatch: true } : null;
}

/** 帳號去除前後空白後須符合格式（送出時也會去除空白）。 */
function usernameFormat(control: AbstractControl<string>): ValidationErrors | null {
  return USERNAME_PATTERN.test(control.value.trim()) ? null : { username: true };
}

/**
 * 用戶維護頁（介面 8.63，REQ_F4118.23.5–8）：伺服器端分頁、帳號排序與搜尋；
 * 新增、停用／啟用、重設密碼、解鎖按鈕依權限顯示。切換公司後重新載入（只列目前公司的用戶）。
 */
@Component({
  selector: 'erp-user-list',
  imports: [
    FormsModule,
    ReactiveFormsModule,
    TranslatePipe,
    NzAlertModule,
    NzButtonModule,
    NzInputModule,
    NzModalModule,
    NzTableModule,
    NzTagModule,
    HasPermissionDirective,
    UserPermissionDialog,
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <section class="erp-user">
      <header class="erp-user__header">
        <h1 data-testid="sys-user-title">{{ 'sys.user.title' | translate }}</h1>
        <button nz-button nzType="primary" data-testid="sys-user-create" *erpHasPermission="'SYS.USER.CREATE'"
          (click)="openCreate()">
          {{ 'common.action.add' | translate }}
        </button>
      </header>
      <div class="erp-user__filters">
        <input nz-input id="user-search" data-testid="sys-user-search" type="search"
          [attr.aria-label]="'sys.user.search' | translate" [placeholder]="'sys.user.search' | translate"
          [ngModel]="keyword()" (ngModelChange)="keyword.set($event)" (keyup.enter)="search()" />
        <button nz-button data-testid="sys-user-search-button" (click)="search()">
          {{ 'common.action.search' | translate }}
        </button>
      </div>
      @if (loadError()) {
        <nz-alert nzType="error" nzShowIcon data-testid="sys-user-load-error" [nzMessage]="loadError()! | translate" />
      }
      @if (actionError()) {
        <nz-alert nzType="error" nzShowIcon nzCloseable data-testid="sys-user-action-error"
          [nzMessage]="actionError()! | translate" (nzOnClose)="actionError.set(null)" />
      }
      @if (notice()) {
        <nz-alert nzType="success" nzShowIcon nzCloseable data-testid="sys-user-notice"
          [nzMessage]="notice()! | translate: noticeParams()" (nzOnClose)="notice.set(null)" />
      }
      <nz-table #table data-testid="sys-user-table" nzSize="middle" [nzData]="rows()" [nzLoading]="loading()"
        [nzFrontPagination]="false" [nzTotal]="total()" [nzPageIndex]="page()" [nzPageSize]="pageSize()"
        nzShowSizeChanger [nzNoResult]="'sys.user.empty' | translate" (nzQueryParams)="onQueryParams($event)">
        <thead>
          <tr>
            <th nzColumnKey="username" [nzSortFn]="true" [nzSortOrder]="sortOrder()">{{ 'sys.user.username' | translate }}</th>
            <th>{{ 'sys.user.status' | translate }}</th>
            <th>{{ 'sys.user.passwordChangedAt' | translate }}</th>
            <th>{{ 'sys.user.actions' | translate }}</th>
          </tr>
        </thead>
        <tbody>
          @for (row of table.data; track row.id) {
            <tr [attr.data-testid]="'sys-user-row-' + row.username">
              <td>{{ row.username }}</td>
              <td>
                <nz-tag [nzColor]="row.isActive ? 'success' : 'default'">
                  {{ (row.isActive ? 'sys.user.active' : 'sys.user.inactive') | translate }}
                </nz-tag>
                @if (row.isLocked) {
                  <nz-tag nzColor="error" [attr.data-testid]="'sys-user-locked-' + row.username">{{ 'sys.user.locked' | translate }}</nz-tag>
                }
                @if (row.isPasswordExpired) {
                  <nz-tag nzColor="warning" [attr.data-testid]="'sys-user-expired-' + row.username">{{ 'sys.user.passwordExpired' | translate }}</nz-tag>
                }
              </td>
              <td>{{ displayTime(row.passwordChangedAt) }}</td>
              <td>
                <button nz-button nzType="link" nzSize="small" *erpHasPermission="'SYS.USER.EDIT'"
                  [attr.data-testid]="'sys-user-toggle-' + row.username" [disabled]="busyId() === row.id"
                  (click)="toggleActive(row)">
                  {{ (row.isActive ? 'sys.user.deactivate' : 'sys.user.activate') | translate }}
                </button>
                @if (row.isLocked) {
                  <button nz-button nzType="link" nzSize="small" *erpHasPermission="'SYS.USER.EDIT'"
                    [attr.data-testid]="'sys-user-unlock-' + row.username" [disabled]="busyId() === row.id"
                    (click)="unlock(row)">
                    {{ 'sys.user.unlock' | translate }}
                  </button>
                }
                <button nz-button nzType="link" nzSize="small" *erpHasPermission="'SYS.USER.EDIT'"
                  [attr.data-testid]="'sys-user-permissions-' + row.username" (click)="permissionUser.set(row)">
                  {{ 'sys.user.permissions' | translate }}
                </button>
                <button nz-button nzType="link" nzSize="small" *erpHasPermission="'SYS.USER.RESET_PASSWORD'"
                  [attr.data-testid]="'sys-user-reset-' + row.username" (click)="openReset(row)">
                  {{ 'sys.user.resetPassword' | translate }}
                </button>
              </td>
            </tr>
          }
        </tbody>
      </nz-table>
    </section>

    <nz-modal [nzVisible]="dialog() !== null" [nzTitle]="dialogTitle() | translate: dialogTitleParams()"
      [nzOkText]="'common.action.save' | translate" [nzCancelText]="'common.action.cancel' | translate"
      [nzOkLoading]="saving()" (nzOnOk)="submit()" (nzOnCancel)="closeDialog()">
      <ng-container *nzModalContent>
        @if (saveError()) {
          <nz-alert nzType="error" nzShowIcon data-testid="sys-user-save-error" [nzMessage]="saveError()! | translate" />
        }
        <form class="erp-user-form" [formGroup]="form" (ngSubmit)="submit()" data-testid="sys-user-form">
          @if (dialog()?.mode === 'create') {
            <label for="user-username">{{ 'sys.user.username' | translate }}</label>
            <input id="user-username" data-testid="sys-user-username" formControlName="username" maxlength="100"
              autocomplete="off" required />
            @if (form.controls.username.touched && form.controls.username.invalid) {
              <p role="alert">{{ 'sys.user.invalidUsername' | translate }}</p>
            }
          }
          <label for="user-password">{{ 'sys.user.password' | translate }}</label>
          <input id="user-password" data-testid="sys-user-password" type="password" formControlName="password"
            [attr.maxlength]="passwordMaxLength" autocomplete="new-password" required />
          <small class="erp-user-form__hint">{{ 'sys.user.passwordHint' | translate }}</small>
          @if (form.controls.password.touched && form.controls.password.invalid) {
            <p role="alert">{{ 'sys.user.passwordRequired' | translate }}</p>
          }
          <label for="user-confirm">{{ 'sys.user.confirmPassword' | translate }}</label>
          <input id="user-confirm" data-testid="sys-user-confirm" type="password" formControlName="confirm"
            [attr.maxlength]="passwordMaxLength" autocomplete="new-password" required />
          @if (form.controls.confirm.touched && (form.controls.confirm.invalid || form.hasError('mismatch'))) {
            <p role="alert">{{ 'sys.user.passwordMismatch' | translate }}</p>
          }
          <button type="submit" hidden></button>
        </form>
      </ng-container>
    </nz-modal>

    <erp-user-permission-dialog [user]="permissionUser()" (closed)="onPermissionsClosed($event)" />
  `,
  styles: `
    .erp-user { display: grid; gap: var(--erp-space-2, 16px); color: var(--erp-color-text); }
    .erp-user__header { display: flex; align-items: center; justify-content: space-between; gap: 16px; }
    .erp-user__header h1 { margin: 0; font-size: 20px; }
    .erp-user__filters { display: flex; flex-wrap: wrap; align-items: center; gap: 8px; }
    .erp-user__filters input { max-width: 320px; }
    nz-alert { margin-bottom: 12px; }
    .erp-user-form { display: grid; gap: 8px; color: var(--erp-color-text); }
    .erp-user-form input {
      padding: 6px 8px; border: 1px solid var(--erp-color-border, #d9d9d9); border-radius: 6px;
      background: var(--erp-color-bg-container, #fff); color: var(--erp-color-text);
    }
    .erp-user-form p { color: var(--erp-status-error); margin: 0; }
    .erp-user-form__hint { color: var(--erp-color-text-secondary, inherit); }
  `,
})
export class UserList {
  private readonly api = inject(UserService);
  private readonly listRequest = latestRequest();
  private readonly company = inject(CompanyContextService);

  readonly passwordMaxLength = PASSWORD_MAX_LENGTH;

  readonly rows = signal<UserSummary[]>([]);
  readonly total = signal(0);
  readonly page = signal(1);
  readonly pageSize = signal(20);
  readonly sort = signal('username');
  readonly keyword = signal('');
  readonly loading = signal(false);
  readonly loadError = signal<string | null>(null);

  /** 列上操作（停用／啟用、解鎖）進行中的用戶 ID，避免重複送出。 */
  readonly busyId = signal<number | null>(null);
  readonly actionError = signal<string | null>(null);
  readonly notice = signal<string | null>(null);
  readonly noticeParams = signal<Record<string, string>>({});

  readonly dialog = signal<UserDialog | null>(null);
  /** 開啟用戶權限對話框的用戶（介面 8.65）。 */
  readonly permissionUser = signal<UserSummary | null>(null);
  readonly saving = signal(false);
  readonly saveError = signal<string | null>(null);

  readonly form = new FormGroup(
    {
      username: new FormControl('', { nonNullable: true, validators: [Validators.required, usernameFormat] }),
      password: new FormControl('', { nonNullable: true, validators: [Validators.required, Validators.maxLength(PASSWORD_MAX_LENGTH)] }),
      confirm: new FormControl('', { nonNullable: true, validators: [Validators.required] }),
    },
    { validators: passwordsMatch },
  );

  readonly dialogTitle = computed(() => (this.dialog()?.mode === 'reset' ? 'sys.user.resetTitle' : 'sys.user.createTitle'));
  readonly dialogTitleParams = computed(() => {
    const dialog = this.dialog();
    return dialog?.mode === 'reset' ? { username: dialog.user.username } : {};
  });

  /** 只比較公司 ID：重新載入權限會換掉公司物件，但公司沒變時不必重新查詢。 */
  private readonly companyId = computed(() => this.company.current()?.id ?? null);

  constructor() {
    // 首次載入及切換公司時重新查詢（只列目前公司的用戶）
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

  sortOrder(): 'ascend' | 'descend' {
    return this.sort() === '-username' ? 'descend' : 'ascend';
  }

  /** `2026-10-11T08:30:00+08:00` → `2026-10-11 08:30`（保留伺服器 UTC+8 時間，不換算瀏覽器時區）。 */
  displayTime(value: string | null | undefined): string {
    return value ? value.slice(0, 16).replace('T', ' ') : '';
  }

  search(): void {
    this.page.set(1);
    this.reload();
  }

  onQueryParams(params: NzTableQueryParams): void {
    const sorted = params.sort.find((s) => s.value !== null);
    const sort = sorted?.value === 'descend' ? '-username' : 'username';
    if (params.pageIndex === this.page() && params.pageSize === this.pageSize() && sort === this.sort()) return;
    this.page.set(params.pageIndex);
    this.pageSize.set(params.pageSize);
    this.sort.set(sort);
    this.reload();
  }

  reload(): void {
    this.loading.set(true);
    this.loadError.set(null);
    this.listRequest.run(this.api.list({ page: this.page(), pageSize: this.pageSize(), keyword: this.keyword(), sort: this.sort() }), {
      next: (result) => {
        this.rows.set(result.items);
        this.total.set(result.totalCount);
        this.loading.set(false);
      },
      error: (error: unknown) => {
        this.rows.set([]);
        this.total.set(0);
        this.loadError.set(userErrorKey(error));
        this.loading.set(false);
      },
    });
  }

  toggleActive(row: UserSummary): void {
    const activate = !row.isActive;
    this.runRowAction(row, this.api.setActive(row.id, activate, row.version), activate ? 'sys.user.activated' : 'sys.user.deactivated');
  }

  unlock(row: UserSummary): void {
    this.runRowAction(row, this.api.unlock(row.id), 'sys.user.unlocked');
  }

  openCreate(): void {
    this.openDialog({ mode: 'create' });
  }

  openReset(row: UserSummary): void {
    this.openDialog({ mode: 'reset', user: row });
  }

  closeDialog(): void {
    this.dialog.set(null);
    this.saveError.set(null);
    this.form.reset();
  }

  submit(): void {
    const dialog = this.dialog();
    if (!dialog || this.saving()) return;
    this.form.markAllAsTouched();
    if (this.form.invalid) return;
    const { username, password } = this.form.getRawValue();
    const name = username.trim();
    const request: Observable<unknown> =
      dialog.mode === 'create' ? this.api.create(name, password) : this.api.resetPassword(dialog.user.id, password);
    this.saving.set(true);
    this.saveError.set(null);
    request.subscribe({
      next: () => {
        this.saving.set(false);
        this.showNotice(
          dialog.mode === 'create' ? 'sys.user.created' : 'sys.user.passwordReset',
          dialog.mode === 'create' ? name : dialog.user.username,
        );
        this.closeDialog();
        this.reload();
      },
      error: (error: unknown) => {
        this.saving.set(false);
        // 密碼不保留在畫面：失敗後清空密碼欄位
        this.form.patchValue({ password: '', confirm: '' });
        this.form.controls.password.markAsUntouched();
        this.form.controls.confirm.markAsUntouched();
        this.saveError.set(userErrorKey(error, true));
      },
    });
  }

  onPermissionsClosed(saved: boolean): void {
    const user = this.permissionUser();
    this.permissionUser.set(null);
    if (saved && user) this.showNotice('sys.user.permissionsSaved', user.username);
  }

  private openDialog(dialog: UserDialog): void {
    this.form.reset();
    // 重設密碼時沒有帳號欄位：停用其驗證
    if (dialog.mode === 'reset') this.form.controls.username.disable();
    else this.form.controls.username.enable();
    this.saveError.set(null);
    this.dialog.set(dialog);
  }

  private runRowAction(row: UserSummary, request: Observable<unknown>, successKey: string): void {
    if (this.busyId() !== null) return;
    this.busyId.set(row.id);
    this.actionError.set(null);
    this.notice.set(null);
    request.subscribe({
      next: () => {
        this.busyId.set(null);
        this.showNotice(successKey, row.username);
        this.reload();
      },
      error: (error: unknown) => {
        this.busyId.set(null);
        this.actionError.set(userErrorKey(error));
      },
    });
  }

  private showNotice(key: string, username: string): void {
    this.noticeParams.set({ username });
    this.notice.set(key);
  }
}
