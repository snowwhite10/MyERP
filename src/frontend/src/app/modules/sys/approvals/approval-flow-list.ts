import { DecimalPipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, computed, effect, inject, signal, untracked } from '@angular/core';
import { AbstractControl, FormArray, FormControl, FormGroup, FormsModule, ReactiveFormsModule, ValidationErrors, Validators } from '@angular/forms';
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
import { DOCUMENT_TYPE_MAX_LENGTH, DOCUMENT_TYPE_PATTERN } from '../number-rules/number-rule.service';
import {
  AMOUNT_PATTERN,
  ApprovalFlow,
  approvalErrorKey,
  ApprovalService,
  ApprovalStep,
  ApproverOption,
  ApproverType,
  APPROVER_TYPES,
  approverTypeKey,
  FLOW_NAME_MAX_LENGTH,
  MAX_STEPS,
} from './approval.service';

/** 對話框：新增流程，或編輯某流程。 */
export type ApprovalFlowDialog = { mode: 'create' } | { mode: 'edit'; flow: ApprovalFlow };

type StepForm = FormGroup<{
  name: FormControl<string>;
  approverType: FormControl<ApproverType>;
  roleId: FormControl<number | null>;
  userId: FormControl<number | null>;
  minAmount: FormControl<string>;
}>;

function documentTypeFormat(control: AbstractControl<string>): ValidationErrors | null {
  const value = control.value.trim().toUpperCase();
  return value.length <= DOCUMENT_TYPE_MAX_LENGTH && DOCUMENT_TYPE_PATTERN.test(value) ? null : { documentType: true };
}

function notBlank(control: AbstractControl<string>): ValidationErrors | null {
  return control.value.trim() ? null : { required: true };
}

function amountFormat(control: AbstractControl<string>): ValidationErrors | null {
  const value = control.value.trim();
  return !value || AMOUNT_PATTERN.test(value) ? null : { amount: true };
}

/** 角色步驟必須選角色，用戶步驟必須選用戶（錯誤放在步驟群組上：`approver`）。 */
function approverSelected(group: AbstractControl): ValidationErrors | null {
  const { approverType, roleId, userId } = (group as StepForm).getRawValue();
  return (approverType === 'Role' ? roleId : userId) ? null : { approver: true };
}

function newStep(step?: Partial<ApprovalStep>): StepForm {
  return new FormGroup(
    {
      name: new FormControl(step?.name ?? '', { nonNullable: true, validators: [notBlank, Validators.maxLength(FLOW_NAME_MAX_LENGTH)] }),
      approverType: new FormControl<ApproverType>(step?.approverType ?? 'Role', { nonNullable: true }),
      roleId: new FormControl<number | null>(step?.roleId ?? null),
      userId: new FormControl<number | null>(step?.userId ?? null),
      minAmount: new FormControl(step?.minAmount != null ? String(step.minAmount) : '', { nonNullable: true, validators: [amountFormat] }),
    },
    { validators: approverSelected },
  );
}

const SORTABLE = ['documentType', 'name'] as const;

/**
 * 審批流程維護頁（介面 8.68，REQ_F4118.29.1、29.2、29.8）：伺服器端分頁、單據類型／名稱排序與搜尋；
 * 新增／編輯對話框可增刪與排序 1–10 個步驟，每步選擇角色或用戶審批人並可設金額門檻。流程不可刪除，以停用代替。
 */
@Component({
  selector: 'erp-approval-flow-list',
  imports: [
    DecimalPipe,
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
    <section class="erp-approval-flow">
      <header class="erp-approval-flow__header">
        <h1 data-testid="sys-approval-flow-title">{{ 'sys.approvalFlow.title' | translate }}</h1>
        <button nz-button nzType="primary" data-testid="sys-approval-flow-create" *erpHasPermission="'SYS.APPROVAL_FLOW.CREATE'"
          (click)="openCreate()">
          {{ 'common.action.add' | translate }}
        </button>
      </header>
      <div class="erp-approval-flow__filters">
        <input nz-input data-testid="sys-approval-flow-search" type="search"
          [attr.aria-label]="'sys.approvalFlow.search' | translate" [placeholder]="'sys.approvalFlow.search' | translate"
          [ngModel]="keyword()" (ngModelChange)="keyword.set($event)" (keyup.enter)="search()" />
        <button nz-button data-testid="sys-approval-flow-search-button" (click)="search()">
          {{ 'common.action.search' | translate }}
        </button>
      </div>
      @if (loadError()) {
        <nz-alert nzType="error" nzShowIcon data-testid="sys-approval-flow-load-error" [nzMessage]="loadError()! | translate" />
      }
      @if (notice()) {
        <nz-alert nzType="success" nzShowIcon nzCloseable data-testid="sys-approval-flow-notice"
          [nzMessage]="notice()! | translate: noticeParams()" (nzOnClose)="notice.set(null)" />
      }
      <nz-table #table data-testid="sys-approval-flow-table" nzSize="middle" [nzData]="rows()" [nzLoading]="loading()"
        [nzFrontPagination]="false" [nzTotal]="total()" [nzPageIndex]="page()" [nzPageSize]="pageSize()"
        nzShowSizeChanger [nzNoResult]="'sys.approvalFlow.empty' | translate" (nzQueryParams)="onQueryParams($event)">
        <thead>
          <tr>
            <th nzColumnKey="documentType" [nzSortFn]="true" [nzSortOrder]="sortOrder('documentType')">
              {{ 'sys.approvalFlow.documentType' | translate }}
            </th>
            <th nzColumnKey="name" [nzSortFn]="true" [nzSortOrder]="sortOrder('name')">{{ 'sys.approvalFlow.name' | translate }}</th>
            <th>{{ 'sys.approvalFlow.steps' | translate }}</th>
            <th>{{ 'sys.approvalFlow.status' | translate }}</th>
            <th>{{ 'sys.approvalFlow.actions' | translate }}</th>
          </tr>
        </thead>
        <tbody>
          @for (row of table.data; track row.id) {
            <tr [attr.data-testid]="'sys-approval-flow-row-' + row.documentType">
              <td>{{ row.documentType }}</td>
              <td>{{ row.name }}</td>
              <td>
                <ol class="erp-approval-flow__steps" [attr.data-testid]="'sys-approval-flow-steps-' + row.documentType">
                  @for (step of row.steps; track step.stepNo) {
                    <li>
                      {{ step.name }}（{{ approverTypeKey(step.approverType) | translate }}：{{ approverLabel(step) }}）
                      @if (step.minAmount !== null) {
                        <span class="erp-approval-flow__threshold">≥ {{ step.minAmount | number: '1.0-4' }}</span>
                      }
                    </li>
                  }
                </ol>
              </td>
              <td>
                <nz-tag [nzColor]="row.isActive ? 'success' : 'default'">
                  {{ (row.isActive ? 'sys.approvalFlow.active' : 'sys.approvalFlow.inactive') | translate }}
                </nz-tag>
                @if (row.allowSelfApproval) {
                  <nz-tag nzColor="orange" [attr.data-testid]="'sys-approval-flow-self-' + row.documentType">
                    {{ 'sys.approvalFlow.allowSelfApproval' | translate }}
                  </nz-tag>
                }
              </td>
              <td>
                <button nz-button nzType="link" nzSize="small" *erpHasPermission="'SYS.APPROVAL_FLOW.EDIT'"
                  [attr.data-testid]="'sys-approval-flow-edit-' + row.documentType" (click)="openEdit(row)">
                  {{ 'common.action.edit' | translate }}
                </button>
              </td>
            </tr>
          }
        </tbody>
      </nz-table>
    </section>

    <nz-modal [nzVisible]="dialog() !== null" [nzTitle]="dialogTitle() | translate: dialogTitleParams()" nzWidth="860px"
      [nzOkText]="'common.action.save' | translate" [nzCancelText]="'common.action.cancel' | translate"
      [nzOkLoading]="saving()" (nzOnOk)="submit()" (nzOnCancel)="closeDialog()">
      <ng-container *nzModalContent>
        @if (saveError()) {
          <nz-alert nzType="error" nzShowIcon data-testid="sys-approval-flow-save-error" [nzMessage]="saveError()! | translate" />
        }
        @if (optionsError()) {
          <nz-alert nzType="warning" nzShowIcon data-testid="sys-approval-flow-options-error"
            [nzMessage]="'sys.approvalFlow.optionsUnavailable' | translate" />
        }
        <form class="erp-approval-flow-form" [formGroup]="form" (ngSubmit)="submit()" data-testid="sys-approval-flow-form">
          <label for="approval-flow-type">{{ 'sys.approvalFlow.documentType' | translate }}</label>
          <input id="approval-flow-type" data-testid="sys-approval-flow-type" formControlName="documentType"
            [attr.maxlength]="documentTypeMaxLength" autocomplete="off" placeholder="PUR.ORDER" required />
          @if (form.controls.documentType.touched && form.controls.documentType.invalid) {
            <p role="alert">{{ 'sys.approvalFlow.invalidDocumentType' | translate }}</p>
          }
          <label for="approval-flow-name">{{ 'sys.approvalFlow.name' | translate }}</label>
          <input id="approval-flow-name" data-testid="sys-approval-flow-name" formControlName="name" [attr.maxlength]="nameMaxLength" required />
          @if (form.controls.name.touched && form.controls.name.invalid) {
            <p role="alert">{{ 'sys.approvalFlow.invalidName' | translate }}</p>
          }
          <label nz-checkbox data-testid="sys-approval-flow-self" formControlName="allowSelfApproval">
            {{ 'sys.approvalFlow.allowSelfApproval' | translate }}
          </label>
          <small class="erp-approval-flow-form__hint">{{ 'sys.approvalFlow.selfApprovalHint' | translate }}</small>
          @if (dialog()?.mode === 'edit') {
            <label nz-checkbox data-testid="sys-approval-flow-active" formControlName="isActive">
              {{ 'sys.approvalFlow.active' | translate }}
            </label>
            <small class="erp-approval-flow-form__hint">{{ 'sys.approvalFlow.editHint' | translate }}</small>
          }

          <h3>{{ 'sys.approvalFlow.steps' | translate }}</h3>
          <small class="erp-approval-flow-form__hint">{{ 'sys.approvalFlow.thresholdHint' | translate }}</small>
          <div formArrayName="steps" class="erp-approval-flow-form__steps">
            @for (step of steps.controls; track step; let i = $index) {
              <fieldset [formGroupName]="i" class="erp-approval-flow-form__step" [attr.data-testid]="'sys-approval-flow-step-' + (i + 1)">
                <legend>{{ 'sys.approvalFlow.stepNo' | translate: { no: i + 1 } }}</legend>
                <input [attr.aria-label]="'sys.approvalFlow.stepName' | translate" [placeholder]="'sys.approvalFlow.stepName' | translate"
                  [attr.data-testid]="'sys-approval-flow-step-name-' + (i + 1)" formControlName="name" [attr.maxlength]="nameMaxLength" />
                <nz-select [attr.data-testid]="'sys-approval-flow-step-type-' + (i + 1)" formControlName="approverType"
                  [attr.aria-label]="'sys.approvalFlow.approverType' | translate">
                  @for (type of approverTypes; track type) {
                    <nz-option [nzValue]="type" [nzLabel]="approverTypeKey(type) | translate" />
                  }
                </nz-select>
                @if (step.controls.approverType.value === 'Role') {
                  <nz-select [attr.data-testid]="'sys-approval-flow-step-role-' + (i + 1)" formControlName="roleId" nzShowSearch
                    [nzPlaceHolder]="'sys.approvalFlow.selectRole' | translate" [attr.aria-label]="'sys.approvalFlow.role' | translate">
                    @for (option of roleChoices(step.controls.roleId.value); track option.id) {
                      <nz-option [nzValue]="option.id" [nzLabel]="option.label" />
                    }
                  </nz-select>
                } @else {
                  <nz-select [attr.data-testid]="'sys-approval-flow-step-user-' + (i + 1)" formControlName="userId" nzShowSearch
                    [nzPlaceHolder]="'sys.approvalFlow.selectUser' | translate" [attr.aria-label]="'sys.approvalFlow.user' | translate">
                    @for (option of userChoices(step.controls.userId.value); track option.id) {
                      <nz-option [nzValue]="option.id" [nzLabel]="option.label" />
                    }
                  </nz-select>
                }
                <input inputmode="decimal" [attr.aria-label]="'sys.approvalFlow.minAmount' | translate"
                  [placeholder]="'sys.approvalFlow.minAmount' | translate" [attr.data-testid]="'sys-approval-flow-step-amount-' + (i + 1)"
                  formControlName="minAmount" />
                <span class="erp-approval-flow-form__step-actions">
                  <button nz-button nzSize="small" type="button" [disabled]="i === 0" [attr.data-testid]="'sys-approval-flow-step-up-' + (i + 1)"
                    [attr.aria-label]="'sys.approvalFlow.moveUp' | translate" (click)="moveStep(i, -1)">↑</button>
                  <button nz-button nzSize="small" type="button" [disabled]="i === steps.length - 1"
                    [attr.data-testid]="'sys-approval-flow-step-down-' + (i + 1)" [attr.aria-label]="'sys.approvalFlow.moveDown' | translate"
                    (click)="moveStep(i, 1)">↓</button>
                  <button nz-button nzSize="small" nzDanger type="button" [disabled]="steps.length === 1"
                    [attr.data-testid]="'sys-approval-flow-step-remove-' + (i + 1)" (click)="removeStep(i)">
                    {{ 'sys.approvalFlow.removeStep' | translate }}
                  </button>
                </span>
                @if (step.touched && step.invalid) {
                  <p role="alert" [attr.data-testid]="'sys-approval-flow-step-error-' + (i + 1)">
                    @if (step.controls.name.invalid) { {{ 'sys.approvalFlow.invalidStepName' | translate }} }
                    @if (step.hasError('approver')) { {{ 'sys.approvalFlow.approverRequired' | translate }} }
                    @if (step.controls.minAmount.invalid) { {{ 'sys.approvalFlow.invalidAmount' | translate }} }
                  </p>
                }
              </fieldset>
            }
          </div>
          <button nz-button nzType="dashed" type="button" data-testid="sys-approval-flow-add-step" [disabled]="steps.length >= maxSteps"
            (click)="addStep()">
            {{ 'sys.approvalFlow.addStep' | translate: { max: maxSteps } }}
          </button>
          <button type="submit" hidden></button>
        </form>
      </ng-container>
    </nz-modal>
  `,
  styles: `
    .erp-approval-flow { display: grid; gap: var(--erp-space-2, 16px); color: var(--erp-color-text); }
    .erp-approval-flow__header { display: flex; align-items: center; justify-content: space-between; gap: 16px; }
    .erp-approval-flow__header h1 { margin: 0; font-size: 20px; }
    .erp-approval-flow__filters { display: flex; flex-wrap: wrap; align-items: center; gap: 8px; }
    .erp-approval-flow__filters input { max-width: 320px; }
    .erp-approval-flow__steps { margin: 0; padding-left: 20px; }
    .erp-approval-flow__threshold { color: var(--erp-color-text-secondary, inherit); margin-left: 4px; }
    nz-alert { margin-bottom: 12px; }
    .erp-approval-flow-form { display: grid; gap: 8px; color: var(--erp-color-text); }
    .erp-approval-flow-form h3 { margin: 8px 0 0; font-size: 16px; }
    .erp-approval-flow-form input {
      padding: 6px 8px; border: 1px solid var(--erp-color-border, #d9d9d9); border-radius: 6px;
      background: var(--erp-color-bg-container, #fff); color: var(--erp-color-text);
    }
    .erp-approval-flow-form p[role='alert'] { color: var(--erp-status-error); margin: 0; grid-column: 1 / -1; }
    .erp-approval-flow-form__hint { color: var(--erp-color-text-secondary, inherit); margin: 0; }
    .erp-approval-flow-form__steps { display: grid; gap: 8px; }
    .erp-approval-flow-form__step {
      display: grid; grid-template-columns: 1.2fr 0.8fr 1.4fr 1fr auto; gap: 8px; align-items: center;
      border: 1px solid var(--erp-color-border, #d9d9d9); border-radius: 6px; padding: 8px; margin: 0;
    }
    .erp-approval-flow-form__step legend { font-size: 13px; padding: 0 4px; width: auto; margin: 0; border: 0; }
    .erp-approval-flow-form__step-actions { display: flex; gap: 4px; }
    @media (max-width: 768px) { .erp-approval-flow-form__step { grid-template-columns: 1fr; } }
  `,
})
export class ApprovalFlowList {
  private readonly api = inject(ApprovalService);
  private readonly listRequest = latestRequest();
  private readonly company = inject(CompanyContextService);

  readonly approverTypes = APPROVER_TYPES;
  readonly approverTypeKey = approverTypeKey;
  readonly documentTypeMaxLength = DOCUMENT_TYPE_MAX_LENGTH;
  readonly nameMaxLength = FLOW_NAME_MAX_LENGTH;
  readonly maxSteps = MAX_STEPS;

  readonly rows = signal<ApprovalFlow[]>([]);
  readonly total = signal(0);
  readonly page = signal(1);
  readonly pageSize = signal(20);
  readonly sort = signal('documentType');
  readonly keyword = signal('');
  readonly loading = signal(false);
  readonly loadError = signal<string | null>(null);
  readonly notice = signal<string | null>(null);
  readonly noticeParams = signal<Record<string, string>>({});

  readonly roles = signal<ApproverOption[]>([]);
  readonly users = signal<ApproverOption[]>([]);
  readonly optionsError = signal(false);

  readonly dialog = signal<ApprovalFlowDialog | null>(null);
  readonly saving = signal(false);
  readonly saveError = signal<string | null>(null);

  readonly steps = new FormArray<StepForm>([newStep()]);
  readonly form = new FormGroup({
    documentType: new FormControl('', { nonNullable: true, validators: [Validators.required, documentTypeFormat] }),
    name: new FormControl('', { nonNullable: true, validators: [notBlank, Validators.maxLength(FLOW_NAME_MAX_LENGTH)] }),
    allowSelfApproval: new FormControl(false, { nonNullable: true }),
    isActive: new FormControl(true, { nonNullable: true }),
    steps: this.steps,
  });

  readonly dialogFlow = computed(() => {
    const dialog = this.dialog();
    return dialog?.mode === 'edit' ? dialog.flow : null;
  });
  readonly dialogTitle = computed(() => (this.dialog()?.mode === 'edit' ? 'sys.approvalFlow.editTitle' : 'sys.approvalFlow.createTitle'));
  readonly dialogTitleParams = computed(() => ({ type: this.dialogFlow()?.documentType ?? '' }));

  private readonly companyId = computed(() => this.company.current()?.id ?? null);

  constructor() {
    // 流程與用戶選項屬於目前公司：切換公司後回到第一頁重新查詢
    effect(() => {
      this.companyId();
      untracked(() => {
        this.page.set(1);
        this.notice.set(null);
        this.closeDialog();
        this.loadOptions();
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
    const sort = sorted ? `${sorted.value === 'descend' ? '-' : ''}${sorted.key}` : 'documentType';
    if (params.pageIndex === this.page() && params.pageSize === this.pageSize() && sort === this.sort()) return;
    this.page.set(params.pageIndex);
    this.pageSize.set(params.pageSize);
    this.sort.set(sort);
    this.reload();
  }

  reload(): void {
    this.loading.set(true);
    this.loadError.set(null);
    this.listRequest.run(this.api.listFlows({ page: this.page(), pageSize: this.pageSize(), keyword: this.keyword(), sort: this.sort() }), {
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

  /** 列表顯示的審批人：選項內找不到（停用或沒有檢視權限）時顯示 `#Id`。 */
  approverLabel(step: Pick<ApprovalStep, 'approverType' | 'roleId' | 'userId'>): string {
    const [options, id] = step.approverType === 'Role' ? [this.roles(), step.roleId] : [this.users(), step.userId];
    return options.find((o) => o.id === id)?.label ?? `#${id}`;
  }

  /** 下拉選項；目前值不在選項內（例如角色已停用）時補上，令編輯時仍能顯示。 */
  roleChoices(current: number | null): ApproverOption[] {
    return withCurrent(this.roles(), current);
  }

  userChoices(current: number | null): ApproverOption[] {
    return withCurrent(this.users(), current);
  }

  openCreate(): void {
    this.resetForm({ documentType: '', name: '', allowSelfApproval: false, isActive: true }, [{}]);
    this.form.controls.documentType.enable();
    this.openDialog({ mode: 'create' });
  }

  openEdit(flow: ApprovalFlow): void {
    this.resetForm(
      { documentType: flow.documentType, name: flow.name, allowSelfApproval: flow.allowSelfApproval, isActive: flow.isActive },
      flow.steps,
    );
    // 單據類型建立後不可修改
    this.form.controls.documentType.disable();
    this.openDialog({ mode: 'edit', flow });
  }

  closeDialog(): void {
    this.dialog.set(null);
    this.saveError.set(null);
  }

  addStep(): void {
    if (this.steps.length < MAX_STEPS) this.steps.push(newStep());
  }

  removeStep(index: number): void {
    if (this.steps.length > 1) this.steps.removeAt(index);
  }

  /** 上移（-1）或下移（+1）步驟；步驟依清單次序編號。 */
  moveStep(index: number, offset: -1 | 1): void {
    const target = index + offset;
    if (target < 0 || target >= this.steps.length) return;
    const step = this.steps.at(index);
    this.steps.removeAt(index, { emitEvent: false });
    this.steps.insert(target, step);
  }

  submit(): void {
    const dialog = this.dialog();
    if (!dialog || this.saving()) return;
    this.form.markAllAsTouched();
    if (this.form.invalid) return;
    const value = this.form.getRawValue();
    const steps = value.steps.map((step) => ({
      name: step.name.trim(),
      approverType: step.approverType,
      // 只送出所選類型的審批人
      roleId: step.approverType === 'Role' ? step.roleId : null,
      userId: step.approverType === 'User' ? step.userId : null,
      minAmount: step.minAmount.trim() ? Number(step.minAmount.trim()) : null,
    }));
    const settings = { name: value.name.trim(), allowSelfApproval: value.allowSelfApproval, steps };
    const documentType = dialog.mode === 'create' ? value.documentType.trim().toUpperCase() : dialog.flow.documentType;
    const request: Observable<ApprovalFlow> =
      dialog.mode === 'create'
        ? this.api.createFlow({ documentType, ...settings })
        : this.api.updateFlow(dialog.flow.id, { ...settings, isActive: value.isActive, version: dialog.flow.version });
    this.saving.set(true);
    this.saveError.set(null);
    request.subscribe({
      next: () => {
        this.saving.set(false);
        this.noticeParams.set({ type: documentType });
        this.notice.set(dialog.mode === 'create' ? 'sys.approvalFlow.created' : 'sys.approvalFlow.updated');
        this.closeDialog();
        this.reload();
      },
      error: (error: unknown) => {
        this.saving.set(false);
        this.saveError.set(approvalErrorKey(error));
      },
    });
  }

  private loadOptions(): void {
    this.optionsError.set(false);
    this.api.roleOptions().subscribe({
      next: (options) => this.roles.set(options),
      error: () => {
        this.roles.set([]);
        this.optionsError.set(true);
      },
    });
    this.api.userOptions().subscribe({
      next: (options) => this.users.set(options),
      error: () => {
        this.users.set([]);
        this.optionsError.set(true);
      },
    });
  }

  private resetForm(
    header: { documentType: string; name: string; allowSelfApproval: boolean; isActive: boolean },
    steps: Partial<ApprovalStep>[],
  ): void {
    this.form.reset(header);
    this.steps.clear({ emitEvent: false });
    for (const step of steps) this.steps.push(newStep(step), { emitEvent: false });
    this.steps.updateValueAndValidity();
  }

  private openDialog(dialog: ApprovalFlowDialog): void {
    this.saveError.set(null);
    this.dialog.set(dialog);
  }
}

function withCurrent(options: ApproverOption[], current: number | null): ApproverOption[] {
  return current === null || options.some((o) => o.id === current) ? options : [...options, { id: current, label: `#${current}` }];
}
