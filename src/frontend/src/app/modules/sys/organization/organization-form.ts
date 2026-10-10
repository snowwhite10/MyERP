import { ChangeDetectionStrategy, Component, input, output } from '@angular/core';
import { FormControl, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { TranslatePipe } from '@ngx-translate/core';
import { OrganizationKind, OrganizationRow } from './organization-list';

export interface OrganizationDraft {
  code: string;
  name: string;
  branchId: number | null;
  parentDepartmentId: number | null;
}

const CODE_PATTERN = /^[A-Za-z0-9_-]{1,30}$/;

/** Form shell emits a validated draft; persistence and authorization are wired in WU-SYS-004. */
@Component({
  selector: 'erp-organization-form',
  imports: [ReactiveFormsModule, TranslatePipe],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <form class="erp-organization-form" [formGroup]="form" (ngSubmit)="submit()">
      <h1>{{ ('sys.organization.' + kind() + '.formTitle') | translate }}</h1>
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
      <button type="submit">{{ 'sys.organization.save' | translate }}</button>
    </form>
  `,
  styles: `
    .erp-organization-form { display: grid; max-width: 560px; gap: 8px; color: var(--erp-color-text); }
    .erp-organization-form h1 { font-size: 20px; }
    .erp-organization-form input, .erp-organization-form select { padding: 8px; }
    .erp-organization-form p { color: var(--erp-status-error); margin: 0; }
  `,
})
export class OrganizationForm {
  readonly kind = input.required<OrganizationKind>();
  readonly branches = input<readonly OrganizationRow[]>([]);
  readonly departments = input<readonly OrganizationRow[]>([]);
  readonly save = output<OrganizationDraft>();

  readonly form = new FormGroup({
    code: new FormControl('', { nonNullable: true, validators: [Validators.required, Validators.pattern(CODE_PATTERN)] }),
    name: new FormControl('', { nonNullable: true, validators: [Validators.required, Validators.pattern(/.*\S.*/), Validators.maxLength(200)] }),
    branchId: new FormControl<number | null>(null),
    parentDepartmentId: new FormControl<number | null>(null),
  });

  submit(): void {
    this.form.markAllAsTouched();
    if (this.form.invalid) return;
    const value = this.form.getRawValue();
    this.save.emit({
      code: value.code.toUpperCase(),
      name: value.name.trim(),
      branchId: this.kind() === 'department' ? value.branchId : null,
      parentDepartmentId: this.kind() === 'department' ? value.parentDepartmentId : null,
    });
  }
}
