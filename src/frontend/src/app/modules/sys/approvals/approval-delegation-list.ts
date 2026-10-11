import { ChangeDetectionStrategy, Component, computed, effect, inject, signal, untracked } from '@angular/core';
import { AbstractControl, FormControl, FormGroup, FormsModule, ReactiveFormsModule, ValidationErrors, Validators } from '@angular/forms';
import { TranslatePipe } from '@ngx-translate/core';
import { NzAlertModule } from 'ng-zorro-antd/alert';
import { NzButtonModule } from 'ng-zorro-antd/button';
import { NzCheckboxModule } from 'ng-zorro-antd/checkbox';
import { NzInputModule } from 'ng-zorro-antd/input';
import { NzModalModule } from 'ng-zorro-antd/modal';
import { NzSelectModule } from 'ng-zorro-antd/select';
import { NzTableModule, NzTableQueryParams } from 'ng-zorro-antd/table';
import { NzTagModule } from 'ng-zorro-antd/tag';
import { Observable } from 'rxjs';
import { HasPermissionDirective } from '../../../core/auth/has-permission.directive';
import { CompanyContextService } from '../../../core/company/company-context.service';
import { latestRequest } from '../../../shared/list-page/latest-request';
import { isoDate } from '../../../shared/date';
import { ApprovalDelegation, approvalErrorKey, ApprovalService, ApproverOption, REMARK_MAX_LENGTH } from './approval.service';

/** 對話框：新增代理，或編輯某代理。 */
export type ApprovalDelegationDialog = { mode: 'create' } | { mode: 'edit'; delegation: ApprovalDelegation };

/** 被代理人與代理人不可相同（`sameUser`）；結束日不可早於開始日（`dateRange`）。 */
function delegationRules(group: AbstractControl): ValidationErrors | null {
  const { delegatorUserId, delegateUserId, startDate, endDate } = group.getRawValue() as {
    delegatorUserId: number | null;
    delegateUserId: number | null;
    startDate: string;
    endDate: string;
  };
  const errors: ValidationErrors = {};
  if (delegatorUserId !== null && delegatorUserId === delegateUserId) errors['sameUser'] = true;
  if (startDate && endDate && endDate < startDate) errors['dateRange'] = true;
  return Object.keys(errors).length ? errors : null;
}

/**
 * 審批代理維護頁（介面 8.69，REQ_F4118.29.3、29.8）：依用戶與有效日期篩選，開始日期排序（預設新到舊）；
 * 新增時選擇被代理人與代理人，編輯只可改期間、備註與啟用。代理不可刪除，以停用代替。
 */
@Component({
  selector: 'erp-approval-delegation-list',
  imports: [
    FormsModule,
    ReactiveFormsModule,
    TranslatePipe,
    NzAlertModule,
    NzButtonModule,
    NzCheckboxModule,
    NzInputModule,
    NzModalModule,
    NzSelectModule,
    NzTableModule,
    NzTagModule,
    HasPermissionDirective,
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <section class="erp-approval-delegation">
      <header class="erp-approval-delegation__header">
        <h1 data-testid="sys-approval-delegation-title">{{ 'sys.approvalDelegation.title' | translate }}</h1>
        <button nz-button nzType="primary" data-testid="sys-approval-delegation-create"
          *erpHasPermission="'SYS.APPROVAL_DELEGATION.CREATE'" (click)="openCreate()">
          {{ 'common.action.add' | translate }}
        </button>
      </header>
      <div class="erp-approval-delegation__filters">
        <nz-select data-testid="sys-approval-delegation-user-filter" nzAllowClear nzShowSearch
          [nzPlaceHolder]="'sys.approvalDelegation.userFilter' | translate" [attr.aria-label]="'sys.approvalDelegation.userFilter' | translate"
          [ngModel]="userFilter()" (ngModelChange)="userFilter.set($event ?? null)">
          @for (option of users(); track option.id) {
            <nz-option [nzValue]="option.id" [nzLabel]="option.label" />
          }
        </nz-select>
        <label for="approval-delegation-active-on">{{ 'sys.approvalDelegation.activeOn' | translate }}</label>
        <input id="approval-delegation-active-on" type="date" data-testid="sys-approval-delegation-active-on"
          [ngModel]="activeOn()" (ngModelChange)="activeOn.set($event ?? '')" />
        <button nz-button data-testid="sys-approval-delegation-search-button" (click)="search()">
          {{ 'common.action.search' | translate }}
        </button>
      </div>
      @if (loadError()) {
        <nz-alert nzType="error" nzShowIcon data-testid="sys-approval-delegation-load-error" [nzMessage]="loadError()! | translate" />
      }
      @if (optionsError()) {
        <nz-alert nzType="warning" nzShowIcon data-testid="sys-approval-delegation-options-error"
          [nzMessage]="'sys.approvalDelegation.usersUnavailable' | translate" />
      }
      @if (notice()) {
        <nz-alert nzType="success" nzShowIcon nzCloseable data-testid="sys-approval-delegation-notice"
          [nzMessage]="notice()! | translate: noticeParams()" (nzOnClose)="notice.set(null)" />
      }
      <nz-table #table data-testid="sys-approval-delegation-table" nzSize="middle" [nzData]="rows()" [nzLoading]="loading()"
        [nzFrontPagination]="false" [nzTotal]="total()" [nzPageIndex]="page()" [nzPageSize]="pageSize()"
        nzShowSizeChanger [nzNoResult]="'sys.approvalDelegation.empty' | translate" (nzQueryParams)="onQueryParams($event)">
        <thead>
          <tr>
            <th>{{ 'sys.approvalDelegation.delegator' | translate }}</th>
            <th>{{ 'sys.approvalDelegation.delegate' | translate }}</th>
            <th nzColumnKey="startDate" [nzSortFn]="true" [nzSortOrder]="sortOrder()">{{ 'sys.approvalDelegation.period' | translate }}</th>
            <th>{{ 'sys.approvalDelegation.remark' | translate }}</th>
            <th>{{ 'sys.approvalDelegation.status' | translate }}</th>
            <th>{{ 'sys.approvalDelegation.actions' | translate }}</th>
          </tr>
        </thead>
        <tbody>
          @for (row of table.data; track row.id) {
            <tr [attr.data-testid]="'sys-approval-delegation-row-' + row.id">
              <td>{{ row.delegatorUsername }}</td>
              <td>{{ row.delegateUsername }}</td>
              <td>{{ row.startDate }} ~ {{ row.endDate }}</td>
              <td>{{ row.remark }}</td>
              <td>
                <nz-tag [nzColor]="row.isActive ? 'success' : 'default'">
                  {{ (row.isActive ? 'sys.approvalDelegation.active' : 'sys.approvalDelegation.inactive') | translate }}
                </nz-tag>
                @if (row.isActive && row.startDate <= today && row.endDate >= today) {
                  <nz-tag nzColor="blue" [attr.data-testid]="'sys-approval-delegation-current-' + row.id">
                    {{ 'sys.approvalDelegation.current' | translate }}
                  </nz-tag>
                }
              </td>
              <td>
                <button nz-button nzType="link" nzSize="small" *erpHasPermission="'SYS.APPROVAL_DELEGATION.EDIT'"
                  [attr.data-testid]="'sys-approval-delegation-edit-' + row.id" (click)="openEdit(row)">
                  {{ 'common.action.edit' | translate }}
                </button>
              </td>
            </tr>
          }
        </tbody>
      </nz-table>
    </section>

    <nz-modal [nzVisible]="dialog() !== null" [nzTitle]="dialogTitle() | translate" nzWidth="560px"
      [nzOkText]="'common.action.save' | translate" [nzCancelText]="'common.action.cancel' | translate"
      [nzOkLoading]="saving()" (nzOnOk)="submit()" (nzOnCancel)="closeDialog()">
      <ng-container *nzModalContent>
        @if (saveError()) {
          <nz-alert nzType="error" nzShowIcon data-testid="sys-approval-delegation-save-error" [nzMessage]="saveError()! | translate" />
        }
        <form class="erp-approval-delegation-form" [formGroup]="form" (ngSubmit)="submit()" data-testid="sys-approval-delegation-form">
          <label for="approval-delegation-delegator">{{ 'sys.approvalDelegation.delegator' | translate }}</label>
          @if (dialogDelegation(); as current) {
            <span data-testid="sys-approval-delegation-delegator-name">{{ current.delegatorUsername }}</span>
          } @else {
            <nz-select nzId="approval-delegation-delegator" data-testid="sys-approval-delegation-delegator" formControlName="delegatorUserId"
              nzShowSearch [nzPlaceHolder]="'sys.approvalDelegation.selectUser' | translate">
              @for (option of users(); track option.id) {
                <nz-option [nzValue]="option.id" [nzLabel]="option.label" />
              }
            </nz-select>
          }
          <label for="approval-delegation-delegate">{{ 'sys.approvalDelegation.delegate' | translate }}</label>
          @if (dialogDelegation(); as current) {
            <span data-testid="sys-approval-delegation-delegate-name">{{ current.delegateUsername }}</span>
          } @else {
            <nz-select nzId="approval-delegation-delegate" data-testid="sys-approval-delegation-delegate" formControlName="delegateUserId"
              nzShowSearch [nzPlaceHolder]="'sys.approvalDelegation.selectUser' | translate">
              @for (option of users(); track option.id) {
                <nz-option [nzValue]="option.id" [nzLabel]="option.label" />
              }
            </nz-select>
          }
          @if (form.touched && (form.controls.delegatorUserId.invalid || form.controls.delegateUserId.invalid)) {
            <p role="alert">{{ 'sys.approvalDelegation.usersRequired' | translate }}</p>
          }
          @if (form.hasError('sameUser')) {
            <p role="alert" data-testid="sys-approval-delegation-same-user">{{ 'sys.approvalDelegation.sameUser' | translate }}</p>
          }
          <label for="approval-delegation-start">{{ 'sys.approvalDelegation.startDate' | translate }}</label>
          <input id="approval-delegation-start" type="date" data-testid="sys-approval-delegation-start" formControlName="startDate" required />
          <label for="approval-delegation-end">{{ 'sys.approvalDelegation.endDate' | translate }}</label>
          <input id="approval-delegation-end" type="date" data-testid="sys-approval-delegation-end" formControlName="endDate" required />
          @if (form.touched && (form.controls.startDate.invalid || form.controls.endDate.invalid)) {
            <p role="alert">{{ 'sys.approvalDelegation.datesRequired' | translate }}</p>
          }
          @if (form.hasError('dateRange')) {
            <p role="alert" data-testid="sys-approval-delegation-date-range">{{ 'sys.approvalDelegation.invalidRange' | translate }}</p>
          }
          <label for="approval-delegation-remark">{{ 'sys.approvalDelegation.remark' | translate }}</label>
          <textarea nz-input id="approval-delegation-remark" data-testid="sys-approval-delegation-remark" formControlName="remark" rows="2"
            [attr.maxlength]="remarkMaxLength"></textarea>
          @if (form.controls.remark.invalid) {
            <p role="alert">{{ 'sys.approvalDelegation.invalidRemark' | translate: { max: remarkMaxLength } }}</p>
          }
          @if (dialog()?.mode === 'edit') {
            <label nz-checkbox data-testid="sys-approval-delegation-active" formControlName="isActive">
              {{ 'sys.approvalDelegation.active' | translate }}
            </label>
          }
          <small class="erp-approval-delegation-form__hint">{{ 'sys.approvalDelegation.hint' | translate }}</small>
          <button type="submit" hidden></button>
        </form>
      </ng-container>
    </nz-modal>
  `,
  styles: `
    .erp-approval-delegation { display: grid; gap: var(--erp-space-2, 16px); color: var(--erp-color-text); }
    .erp-approval-delegation__header { display: flex; align-items: center; justify-content: space-between; gap: 16px; }
    .erp-approval-delegation__header h1 { margin: 0; font-size: 20px; }
    .erp-approval-delegation__filters { display: flex; flex-wrap: wrap; align-items: center; gap: 8px; }
    .erp-approval-delegation__filters nz-select { min-width: 200px; }
    nz-alert { margin-bottom: 12px; }
    .erp-approval-delegation-form { display: grid; gap: 8px; color: var(--erp-color-text); }
    .erp-approval-delegation input[type='date'], .erp-approval-delegation-form input {
      padding: 6px 8px; border: 1px solid var(--erp-color-border, #d9d9d9); border-radius: 6px;
      background: var(--erp-color-bg-container, #fff); color: var(--erp-color-text);
    }
    .erp-approval-delegation-form p[role='alert'] { color: var(--erp-status-error); margin: 0; }
    .erp-approval-delegation-form__hint { color: var(--erp-color-text-secondary, inherit); margin: 0; }
  `,
})
export class ApprovalDelegationList {
  private readonly api = inject(ApprovalService);
  private readonly listRequest = latestRequest();
  private readonly company = inject(CompanyContextService);

  readonly remarkMaxLength = REMARK_MAX_LENGTH;
  /** 今天（本地業務日期），用於標示「生效中」。 */
  readonly today = isoDate(new Date());

  readonly rows = signal<ApprovalDelegation[]>([]);
  readonly total = signal(0);
  readonly page = signal(1);
  readonly pageSize = signal(20);
  readonly sort = signal('-startDate');
  readonly userFilter = signal<number | null>(null);
  readonly activeOn = signal('');
  readonly loading = signal(false);
  readonly loadError = signal<string | null>(null);
  readonly notice = signal<string | null>(null);
  readonly noticeParams = signal<Record<string, string>>({});

  readonly users = signal<ApproverOption[]>([]);
  readonly optionsError = signal(false);

  readonly dialog = signal<ApprovalDelegationDialog | null>(null);
  readonly saving = signal(false);
  readonly saveError = signal<string | null>(null);

  readonly form = new FormGroup(
    {
      delegatorUserId: new FormControl<number | null>(null, { validators: Validators.required }),
      delegateUserId: new FormControl<number | null>(null, { validators: Validators.required }),
      startDate: new FormControl('', { nonNullable: true, validators: Validators.required }),
      endDate: new FormControl('', { nonNullable: true, validators: Validators.required }),
      remark: new FormControl('', { nonNullable: true, validators: Validators.maxLength(REMARK_MAX_LENGTH) }),
      isActive: new FormControl(true, { nonNullable: true }),
    },
    { validators: delegationRules },
  );

  readonly dialogDelegation = computed(() => {
    const dialog = this.dialog();
    return dialog?.mode === 'edit' ? dialog.delegation : null;
  });
  readonly dialogTitle = computed(() =>
    this.dialog()?.mode === 'edit' ? 'sys.approvalDelegation.editTitle' : 'sys.approvalDelegation.createTitle',
  );

  private readonly companyId = computed(() => this.company.current()?.id ?? null);

  constructor() {
    // 代理與用戶屬於目前公司：切換公司後清除用戶篩選、回到第一頁重新查詢
    effect(() => {
      this.companyId();
      untracked(() => {
        this.page.set(1);
        this.userFilter.set(null);
        this.notice.set(null);
        this.closeDialog();
        this.loadUsers();
        this.reload();
      });
    });
  }

  sortOrder(): 'ascend' | 'descend' {
    return this.sort() === 'startDate' ? 'ascend' : 'descend';
  }

  search(): void {
    this.page.set(1);
    this.reload();
  }

  onQueryParams(params: NzTableQueryParams): void {
    const sorted = params.sort.find((s) => s.key === 'startDate' && s.value !== null);
    const sort = sorted?.value === 'ascend' ? 'startDate' : '-startDate';
    if (params.pageIndex === this.page() && params.pageSize === this.pageSize() && sort === this.sort()) return;
    this.page.set(params.pageIndex);
    this.pageSize.set(params.pageSize);
    this.sort.set(sort);
    this.reload();
  }

  reload(): void {
    this.loading.set(true);
    this.loadError.set(null);
    this.listRequest.run(
      this.api.listDelegations({
        page: this.page(),
        pageSize: this.pageSize(),
        userId: this.userFilter(),
        activeOn: this.activeOn() || undefined,
        sort: this.sort(),
      }),
      {
        next: (result) => {
          this.rows.set(result.items);
          this.total.set(result.totalCount);
          this.loading.set(false);
        },
        error: (error: unknown) => {
          this.rows.set([]);
          this.total.set(0);
          this.loadError.set(approvalErrorKey(error));
          this.loading.set(false);
        },
      });
  }

  openCreate(): void {
    this.form.reset({ delegatorUserId: null, delegateUserId: null, startDate: this.today, endDate: this.today, remark: '', isActive: true });
    this.form.controls.delegatorUserId.enable();
    this.form.controls.delegateUserId.enable();
    this.openDialog({ mode: 'create' });
  }

  openEdit(delegation: ApprovalDelegation): void {
    this.form.reset({
      delegatorUserId: delegation.delegatorUserId,
      delegateUserId: delegation.delegateUserId,
      startDate: delegation.startDate,
      endDate: delegation.endDate,
      remark: delegation.remark ?? '',
      isActive: delegation.isActive,
    });
    // 被代理人與代理人建立後不可修改
    this.form.controls.delegatorUserId.disable();
    this.form.controls.delegateUserId.disable();
    this.openDialog({ mode: 'edit', delegation });
  }

  closeDialog(): void {
    this.dialog.set(null);
    this.saveError.set(null);
  }

  submit(): void {
    const dialog = this.dialog();
    if (!dialog || this.saving()) return;
    this.form.markAllAsTouched();
    if (this.form.invalid) return;
    const value = this.form.getRawValue();
    const period = { startDate: value.startDate, endDate: value.endDate, remark: value.remark.trim() || null };
    const request: Observable<ApprovalDelegation> =
      dialog.mode === 'create'
        ? this.api.createDelegation({ delegatorUserId: value.delegatorUserId!, delegateUserId: value.delegateUserId!, ...period })
        : this.api.updateDelegation(dialog.delegation.id, { ...period, isActive: value.isActive, version: dialog.delegation.version });
    this.saving.set(true);
    this.saveError.set(null);
    request.subscribe({
      next: (saved) => {
        this.saving.set(false);
        this.noticeParams.set({ delegator: saved.delegatorUsername, delegate: saved.delegateUsername });
        this.notice.set(dialog.mode === 'create' ? 'sys.approvalDelegation.created' : 'sys.approvalDelegation.updated');
        this.closeDialog();
        this.reload();
      },
      error: (error: unknown) => {
        this.saving.set(false);
        this.saveError.set(approvalErrorKey(error));
      },
    });
  }

  private loadUsers(): void {
    this.optionsError.set(false);
    this.api.userOptions().subscribe({
      next: (options) => this.users.set(options),
      error: () => {
        this.users.set([]);
        this.optionsError.set(true);
      },
    });
  }

  private openDialog(dialog: ApprovalDelegationDialog): void {
    this.saveError.set(null);
    this.dialog.set(dialog);
  }
}
