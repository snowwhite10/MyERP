import { ChangeDetectionStrategy, Component, effect, input, output } from '@angular/core';
import { FormControl, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { TranslatePipe } from '@ngx-translate/core';
import { OrganizationKind, OrganizationOption } from './organization.service';

export interface OrganizationDraft {
  code: string;
  name: string;
  branchId: number | null;
  parentDepartmentId: number | null;
  isActive: boolean;
}

/** 編輯時帶入的現有資料（代碼不可修改）。 */
export interface OrganizationFormValue {
  code: string;
  name: string;
  isActive: boolean;
  branchId?: number | null;
  parentDepartmentId?: number | null;
}

const CODE_PATTERN = /^[A-Za-z0-9_-]{1,30}$/;

/**
 * 公司／分店／部門表單（介面 8.58–8.60）：驗證後輸出正規化草稿（代碼大寫、名稱去空白）。
 * 帶入 `value` 為編輯模式：代碼唯讀，可切換啟用狀態。保存由頁面呼叫 API。
 */
@Component({
  selector: 'erp-organization-form',
  imports: [ReactiveFormsModule, TranslatePipe],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <form class="erp-organization-form" [formGroup]="form" (ngSubmit)="submit()" data-testid="sys-organization-form">
      <label for="organization-code">{{ 'sys.organization.code' | translate }}</label>
      <input id="organization-code" data-testid="sys-code" formControlName="code" maxlength="30" required />
      @if (form.controls.code.touched && form.controls.code.invalid) {
        <p role="alert">{{ 'sys.organization.invalidCode' | translate }}</p>
      }
      <label for="organization-name">{{ 'sys.organization.name' | translate }}</label>
      <input id="organization-name" data-testid="sys-name" formControlName="name" maxlength="200" required />
      @if (form.controls.name.touched && form.controls.name.invalid) {
        <p role="alert">{{ 'sys.organization.invalidName' | translate }}</p>
      }
      @if (kind() === 'department') {
        <label for="organization-branch">{{ 'sys.organization.branchRef' | translate }}</label>
        <select id="organization-branch" data-testid="sys-branch-id" formControlName="branchId">
          <option [ngValue]="null">{{ 'sys.organization.none' | translate }}</option>
          @for (branch of branches(); track branch.id) {
            <option [ngValue]="branch.id">{{ branch.code }} — {{ branch.name }}</option>
          }
        </select>
        <label for="organization-parent">{{ 'sys.organization.parentDepartment' | translate }}</label>
        <select id="organization-parent" data-testid="sys-parent-id" formControlName="parentDepartmentId">
          <option [ngValue]="null">{{ 'sys.organization.none' | translate }}</option>
          @for (department of departments(); track department.id) {
            <option [ngValue]="department.id">{{ department.code }} — {{ department.name }}</option>
          }
        </select>
      }
      @if (value()) {
        <label class="erp-organization-form__check">
          <input type="checkbox" data-testid="sys-active" formControlName="isActive" />
          {{ 'sys.organization.active' | translate }}
        </label>
      }
    </form>
  `,
  styles: `
    .erp-organization-form { display: grid; gap: 8px; color: var(--erp-color-text); }
    .erp-organization-form input:not([type='checkbox']), .erp-organization-form select {
      padding: 6px 8px; border: 1px solid var(--erp-color-border); border-radius: 6px;
      background: var(--erp-color-bg-container); color: var(--erp-color-text);
    }
    .erp-organization-form p { color: var(--erp-status-error); margin: 0; }
    .erp-organization-form__check { display: flex; align-items: center; gap: 8px; }
  `,
})
export class OrganizationForm {
  readonly kind = input.required<OrganizationKind>();
  readonly value = input<OrganizationFormValue | null>(null);
  readonly branches = input<readonly OrganizationOption[]>([]);
  readonly departments = input<readonly OrganizationOption[]>([]);
  readonly save = output<OrganizationDraft>();

  readonly form = new FormGroup({
    code: new FormControl('', { nonNullable: true, validators: [Validators.required, Validators.pattern(CODE_PATTERN)] }),
    name: new FormControl('', { nonNullable: true, validators: [Validators.required, Validators.pattern(/.*\S.*/), Validators.maxLength(200)] }),
    branchId: new FormControl<number | null>(null),
    parentDepartmentId: new FormControl<number | null>(null),
    isActive: new FormControl(true, { nonNullable: true }),
  });

  constructor() {
    effect(() => {
      const value = this.value();
      this.form.reset({
        code: value?.code ?? '',
        name: value?.name ?? '',
        branchId: value?.branchId ?? null,
        parentDepartmentId: value?.parentDepartmentId ?? null,
        isActive: value?.isActive ?? true,
      });
      if (value) this.form.controls.code.disable();
      else this.form.controls.code.enable();
    });
  }

  submit(): void {
    this.form.markAllAsTouched();
    if (this.form.invalid) return;
    const value = this.form.getRawValue();
    this.save.emit({
      code: value.code.toUpperCase(),
      name: value.name.trim(),
      branchId: this.kind() === 'department' ? value.branchId : null,
      parentDepartmentId: this.kind() === 'department' ? value.parentDepartmentId : null,
      isActive: value.isActive,
    });
  }
}
