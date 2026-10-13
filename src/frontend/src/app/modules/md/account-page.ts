import { ChangeDetectionStrategy, Component, computed, effect, inject, signal, untracked } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
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
  ACCOUNT_TYPES, AccountRow, AccountService, AccountType, accountCodeValidator, BALANCE_DIRECTIONS, BalanceDirection,
  defaultBalanceDirection, flattenAccountTree,
} from './account.service';
import { filterCategoryRows } from './item-category.service';

/**
 * 會計科目樹維護頁（介面 8.87–8.88）：樹狀列表、新增根科目／子科目、修改名稱／方向／啟用、刪除；權限 `MD.ACCOUNT.*`。
 * 代碼、上層與類別建立後不可修改（ADR-039）。
 */
@Component({
  selector: 'erp-account-page',
  imports: [ReactiveFormsModule, TranslatePipe, NzAlertModule, NzButtonModule, NzInputModule,
    NzModalModule, NzTableModule, NzTagModule, HasPermissionDirective],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <section class="md-accounts">
      <header><h1 data-testid="md-account-title">{{ 'md.account.title' | translate }}</h1>
        <button nz-button nzType="primary" data-testid="md-account-create"
          *erpHasPermission="'MD.ACCOUNT.CREATE'" (click)="openCreate(null)">{{ 'md.account.addRoot' | translate }}</button>
      </header>
      <p class="hint">{{ 'md.account.hint' | translate }}</p>
      <div class="filters">
        <input nz-input type="search" data-testid="md-account-search" [formControl]="searchControl"
          [placeholder]="'md.account.search' | translate" (keyup.enter)="search()" />
        <button nz-button data-testid="md-account-search-button" (click)="search()">{{ 'common.action.search' | translate }}</button>
      </div>
      @if (error()) { <nz-alert nzType="error" nzShowIcon data-testid="md-account-error" [nzMessage]="error()! | translate" /> }
      <nz-table #table data-testid="md-account-table" nzSize="middle" [nzData]="visibleRows()" [nzLoading]="loading()"
        [nzShowPagination]="false" [nzPageSize]="visibleRows().length || 1" [nzNoResult]="'md.account.empty' | translate">
        <thead><tr>
          <th>{{ 'md.account.code' | translate }}</th><th>{{ 'md.account.name' | translate }}</th>
          <th>{{ 'md.account.accountType' | translate }}</th><th>{{ 'md.account.balanceDirection' | translate }}</th>
          <th>{{ 'md.account.postable' | translate }}</th><th>{{ 'md.account.status' | translate }}</th>
          <th>{{ 'md.account.actions' | translate }}</th>
        </tr></thead>
        <tbody>@for (row of table.data; track row.id) {
          <tr [attr.data-testid]="'md-account-row-' + row.code" [attr.aria-level]="row.level + 1">
            <td [nzIndentSize]="row.level * 24">{{ row.code }}</td><td>{{ row.name }}</td>
            <td>{{ 'md.account.type' + row.accountType | translate }}</td>
            <td>{{ 'md.account.direction' + row.balanceDirection | translate }}</td>
            <td><nz-tag [nzColor]="row.isPostable ? 'blue' : 'default'">
              {{ (row.isPostable ? 'md.account.isPostable' : 'md.account.summary') | translate }}
            </nz-tag></td>
            <td><nz-tag [nzColor]="row.isActive ? 'success' : 'default'">
              {{ (row.isActive ? 'md.account.active' : 'md.account.inactive') | translate }}
            </nz-tag></td>
            <td>
              @if (row.isActive) {
                <button nz-button nzType="link" *erpHasPermission="'MD.ACCOUNT.CREATE'"
                  [attr.data-testid]="'md-account-add-child-' + row.code" (click)="openCreate(row)">
                  {{ 'md.account.addChild' | translate }}</button>
              }
              <button nz-button nzType="link" *erpHasPermission="'MD.ACCOUNT.EDIT'"
                [attr.data-testid]="'md-account-edit-' + row.code" (click)="openEdit(row)">{{ 'common.action.edit' | translate }}</button>
              <button nz-button nzType="link" nzDanger *erpHasPermission="'MD.ACCOUNT.DELETE'"
                [attr.data-testid]="'md-account-delete-' + row.code" [disabled]="busy()"
                (click)="confirmDelete(row)">{{ 'common.action.delete' | translate }}</button>
            </td>
          </tr>
        }</tbody>
      </nz-table>
    </section>

    <nz-modal [nzVisible]="dialogOpen()" [nzTitle]="'md.account.formTitle' | translate"
      [nzOkText]="'common.action.save' | translate" [nzCancelText]="'common.action.cancel' | translate"
      [nzOkLoading]="saving()" (nzOnOk)="save()" (nzOnCancel)="closeDialog()">
      <ng-container *nzModalContent>
        @if (saveError()) { <nz-alert nzType="error" nzShowIcon data-testid="md-account-save-error" [nzMessage]="saveError()! | translate" /> }
        <form class="account-form" [formGroup]="form" (ngSubmit)="save()" data-testid="md-account-form">
          @if (parent(); as p) {
            <p class="parent" data-testid="md-account-parent">{{ 'md.account.parent' | translate }}：{{ p.code }} — {{ p.name }}</p>
          }
          <label for="account-code">{{ 'md.account.code' | translate }}</label>
          <input nz-input id="account-code" data-testid="md-account-code" formControlName="code" maxlength="30" inputmode="numeric" />
          @if (form.controls.code.touched && form.controls.code.invalid) {
            <p role="alert">{{ (form.controls.code.hasError('prefix') ? 'md.account.invalidPrefix' : 'md.account.invalidCode')
              | translate: { code: parent()?.code } }}</p>
          }
          <label for="account-name">{{ 'md.account.name' | translate }}</label>
          <input nz-input id="account-name" data-testid="md-account-name" formControlName="name" maxlength="200" />
          @if (form.controls.name.touched && form.controls.name.invalid) { <p role="alert">{{ 'md.account.invalidName' | translate }}</p> }
          <label for="account-type">{{ 'md.account.accountType' | translate }}</label>
          <select id="account-type" data-testid="md-account-type" formControlName="accountType">
            <option [ngValue]="null" disabled>{{ 'md.account.chooseType' | translate }}</option>
            @for (type of types; track type) { <option [ngValue]="type">{{ 'md.account.type' + type | translate }}</option> }
          </select>
          @if (form.controls.accountType.touched && form.controls.accountType.invalid) {
            <p role="alert">{{ 'md.account.invalidType' | translate }}</p>
          }
          <label for="account-direction">{{ 'md.account.balanceDirection' | translate }}</label>
          <select id="account-direction" data-testid="md-account-direction" formControlName="balanceDirection">
            @for (direction of directions; track direction) {
              <option [ngValue]="direction">{{ 'md.account.direction' + direction | translate }}</option>
            }
          </select>
          @if (editing()) { <label><input type="checkbox" formControlName="isActive" data-testid="md-account-active" />
            {{ 'md.account.active' | translate }}</label> }
        </form>
      </ng-container>
    </nz-modal>
  `,
  styles: `
    .md-accounts { display: grid; gap: 16px; color: var(--erp-color-text); }
    header { display: flex; justify-content: space-between; align-items: center; }
    h1 { margin: 0; font-size: 20px; }
    .hint { margin: 0; color: var(--erp-color-text-secondary); }
    .filters { display: flex; gap: 8px; }
    .filters input { max-width: 320px; }
    .account-form { display: grid; gap: 8px; }
    .account-form p[role="alert"] { color: var(--erp-status-error); margin: 0; }
    .account-form .parent { margin: 0; }
  `,
})
export class AccountPage {
  readonly types = ACCOUNT_TYPES;
  readonly directions = BALANCE_DIRECTIONS;
  private readonly api = inject(AccountService);
  private readonly company = inject(CompanyContextService);
  private readonly dialogs = inject(ErpDialogService);
  private readonly listRequest = latestRequest();
  private readonly companyId = computed(() => this.company.current()?.id ?? null);
  readonly rows = signal<AccountRow[]>([]);
  readonly keyword = signal('');
  readonly visibleRows = computed(() => filterCategoryRows(this.rows(), this.keyword()));
  readonly loading = signal(false);
  readonly error = signal<string | null>(null);
  readonly dialogOpen = signal(false);
  readonly editing = signal<AccountRow | null>(null);
  /** 新增子科目時的上層。 */
  readonly parent = signal<AccountRow | null>(null);
  readonly saving = signal(false);
  readonly saveError = signal<string | null>(null);
  readonly busy = signal(false);
  readonly searchControl = new FormControl('', { nonNullable: true });
  readonly form = new FormGroup({
    code: new FormControl('', { nonNullable: true, validators: [accountCodeValidator(() => this.parent()?.code ?? null)] }),
    name: new FormControl('', { nonNullable: true, validators: [Validators.required, Validators.pattern(/.*\S.*/), Validators.maxLength(200)] }),
    accountType: new FormControl<AccountType | null>(null, { validators: [Validators.required] }),
    balanceDirection: new FormControl<BalanceDirection>('Debit', { nonNullable: true }),
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
    // 新增根科目時，選擇類別即帶出預設方向（仍可改，例如備抵科目）。
    this.form.controls.accountType.valueChanges.pipe(takeUntilDestroyed()).subscribe((type) => {
      if (type && this.form.controls.accountType.enabled) {
        this.form.controls.balanceDirection.setValue(defaultBalanceDirection(type));
      }
    });
  }

  /** 樹一次載入，搜尋只在前端過濾（保留命中節點的上層）。 */
  search(): void { this.keyword.set(this.searchControl.value); }

  reload(): void {
    this.loading.set(true);
    this.error.set(null);
    this.listRequest.run(this.api.list(), {
      next: (items) => { this.rows.set(flattenAccountTree(items)); this.loading.set(false); },
      error: (error: unknown) => { this.rows.set([]); this.error.set(this.api.errorKey(error)); this.loading.set(false); },
    });
  }

  /** `parent` 為 null 時新增根科目；否則代碼預填上層代碼，類別沿用上層且唯讀。 */
  openCreate(parent: AccountRow | null): void {
    this.editing.set(null);
    this.parent.set(parent);
    this.form.controls.code.enable();
    if (parent) this.form.controls.accountType.disable({ emitEvent: false });
    else this.form.controls.accountType.enable({ emitEvent: false });
    this.form.reset({ code: parent?.code ?? '', name: '', accountType: parent?.accountType ?? null,
      balanceDirection: parent ? defaultBalanceDirection(parent.accountType) : 'Debit', isActive: true });
    this.saveError.set(null);
    this.dialogOpen.set(true);
  }

  openEdit(row: AccountRow): void {
    this.editing.set(row);
    this.parent.set(null);
    this.form.controls.code.disable();
    this.form.controls.accountType.disable({ emitEvent: false });
    this.form.reset({ code: row.code, name: row.name, accountType: row.accountType,
      balanceDirection: row.balanceDirection, isActive: row.isActive });
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
    const parent = this.parent();
    const request = row
      ? this.api.update(row.id, { name: draft.name, balanceDirection: draft.balanceDirection, isActive: draft.isActive, version: row.version })
      : this.api.create({ code: draft.code, name: draft.name, parentId: parent?.id ?? null,
        accountType: parent ? null : draft.accountType, balanceDirection: draft.balanceDirection });
    this.saving.set(true);
    this.saveError.set(null);
    request.subscribe({
      next: () => { this.saving.set(false); this.closeDialog(); this.reload(); },
      error: (error: unknown) => { this.saving.set(false); this.saveError.set(this.api.errorKey(error)); if (isStaleRowError(error)) this.reload(); },
    });
  }

  confirmDelete(row: AccountRow): void {
    if (this.busy()) return;
    this.dialogs.confirm({ titleKey: 'md.account.deleteConfirm', contentKey: 'common.confirm.irreversible',
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
