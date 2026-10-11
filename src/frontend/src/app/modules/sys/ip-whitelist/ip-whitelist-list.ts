import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { FormControl, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { TranslatePipe } from '@ngx-translate/core';
import { NzAlertModule } from 'ng-zorro-antd/alert';
import { NzButtonModule } from 'ng-zorro-antd/button';
import { NzInputModule } from 'ng-zorro-antd/input';
import { NzModalModule } from 'ng-zorro-antd/modal';
import { NzSelectModule } from 'ng-zorro-antd/select';
import { NzTableModule, NzTableQueryParams } from 'ng-zorro-antd/table';
import { NzTagModule } from 'ng-zorro-antd/tag';
import { HasPermissionDirective } from '../../../core/auth/has-permission.directive';
import { ErpDialogService } from '../../../shared/dialogs/dialog.service';
import { ListPage } from '../../../shared/list-page/list-page';
import { latestRequest } from '../../../shared/list-page/latest-request';
import { PAGE_SIZE_OPTIONS, nextTableState, sortOrderOf } from '../../../shared/list-page/table-query';
import { IpCreate, IpEntry, ipErrorKey, IpScope, IpWhitelistService, validIpRange } from './ip-whitelist.service';

@Component({
  selector: 'erp-ip-whitelist-list',
  imports: [ReactiveFormsModule, TranslatePipe, NzAlertModule, NzButtonModule, NzInputModule,
    NzModalModule, NzSelectModule, NzTableModule, NzTagModule, HasPermissionDirective, ListPage],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <erp-list-page titleKey="sys.ipWhitelist.title" testId="sys-ip-whitelist" [errorKey]="loadError()"
      (querySubmit)="search()" (retryLoad)="reload()" (filterReset)="reset()">
      <button erpListActions nz-button nzType="primary" data-testid="ip-create" *erpHasPermission="'SYS.IP_WHITELIST.CREATE'"
        (click)="openCreate()">{{ 'common.action.add' | translate }}</button>
      <input erpListFilters nz-input type="search" data-testid="ip-search" [formControl]="keyword"
        [placeholder]="'sys.ipWhitelist.search' | translate" (keyup.enter)="search()" />
      @if (actionError()) { <nz-alert nzType="error" nzShowIcon [nzMessage]="actionError()! | translate" data-testid="ip-action-error" /> }
      @if (notice()) { <nz-alert nzType="success" nzShowIcon [nzMessage]="notice()! | translate" /> }
      <nz-table #table data-testid="ip-table" [nzData]="rows()" [nzLoading]="loading()" [nzFrontPagination]="false"
        [nzTotal]="total()" [nzPageIndex]="page()" [nzPageSize]="pageSize()" nzShowSizeChanger [nzPageSizeOptions]="pageSizeOptions"
        [nzNoResult]="'sys.ipWhitelist.empty' | translate" (nzQueryParams)="onQueryParams($event)">
        <thead><tr>
          <th nzColumnKey="ipRange" [nzSortFn]="true" [nzSortOrder]="sortOrder('ipRange')">{{ 'sys.ipWhitelist.range' | translate }}</th>
          <th>{{ 'sys.ipWhitelist.scope' | translate }}</th><th>{{ 'sys.ipWhitelist.target' | translate }}</th>
          <th>{{ 'sys.ipWhitelist.validFrom' | translate }}</th><th>{{ 'sys.ipWhitelist.validTo' | translate }}</th>
          <th>{{ 'sys.ipWhitelist.description' | translate }}</th><th>{{ 'sys.ipWhitelist.status' | translate }}</th>
          <th>{{ 'sys.ipWhitelist.actions' | translate }}</th>
        </tr></thead>
        <tbody>@for (row of table.data; track row.id) {
          <tr [attr.data-testid]="'ip-row-' + row.id">
            <td>{{ row.ipRange }}</td><td>{{ scopeKey(row.scope) | translate }}</td>
            <td>{{ row.targetId ?? '—' }}</td><td>{{ row.validFrom ?? '—' }}</td><td>{{ row.validTo ?? '—' }}</td>
            <td>{{ row.description ?? '—' }}</td><td><nz-tag [nzColor]="row.isActive ? 'success' : 'default'">
              {{ (row.isActive ? 'sys.ipWhitelist.active' : 'sys.ipWhitelist.inactive') | translate }}</nz-tag></td>
            <td><button nz-button nzType="link" *erpHasPermission="'SYS.IP_WHITELIST.EDIT'" [attr.data-testid]="'ip-edit-' + row.id"
                (click)="openEdit(row)">{{ 'common.action.edit' | translate }}</button>
              @if (row.isActive) { <button nz-button nzDanger nzType="link" *erpHasPermission="'SYS.IP_WHITELIST.EDIT'"
                [attr.data-testid]="'ip-disable-' + row.id" [nzLoading]="disabling() === row.id" [disabled]="disabling() !== null"
                (click)="confirmDisable(row)">{{ 'sys.ipWhitelist.disable' | translate }}</button> }
            </td>
          </tr>
        }</tbody>
      </nz-table>
    </erp-list-page>
    <nz-modal [nzVisible]="dialog() !== null" [nzTitle]="(dialog()?.mode === 'edit' ? 'sys.ipWhitelist.editTitle' : 'sys.ipWhitelist.createTitle') | translate"
      [nzOkText]="'common.action.save' | translate" [nzCancelText]="'common.action.cancel' | translate"
      [nzOkLoading]="saving()" (nzOnOk)="submit()" (nzOnCancel)="closeDialog()">
      <ng-container *nzModalContent>
        @if (saveError()) { <nz-alert nzType="error" nzShowIcon [nzMessage]="saveError()! | translate" data-testid="ip-save-error" /> }
        <form class="ip-form" [formGroup]="form" (ngSubmit)="submit()" data-testid="ip-form">
          <label for="ip-scope">{{ 'sys.ipWhitelist.scope' | translate }}</label>
          <nz-select nzId="ip-scope" formControlName="scope" data-testid="ip-scope">
            @for (scope of scopes; track scope) { <nz-option [nzValue]="scope" [nzLabel]="scopeKey(scope) | translate" /> }
          </nz-select>
          @if (form.controls.scope.value !== 'System') {
            <label for="ip-target">{{ 'sys.ipWhitelist.target' | translate }}</label>
            <input id="ip-target" type="number" min="1" step="1" formControlName="targetId" data-testid="ip-target" />
          }
          <label for="ip-range">{{ 'sys.ipWhitelist.range' | translate }}</label>
          <input id="ip-range" formControlName="ipRange" maxlength="50" data-testid="ip-range" autocomplete="off" />
          <small>{{ 'sys.ipWhitelist.rangeHint' | translate }}</small>
          <label for="ip-from">{{ 'sys.ipWhitelist.validFrom' | translate }}</label>
          <input id="ip-from" type="date" formControlName="validFrom" data-testid="ip-from" />
          <label for="ip-to">{{ 'sys.ipWhitelist.validTo' | translate }}</label>
          <input id="ip-to" type="date" formControlName="validTo" data-testid="ip-to" />
          <label for="ip-description">{{ 'sys.ipWhitelist.description' | translate }}</label>
          <textarea id="ip-description" formControlName="description" maxlength="200" data-testid="ip-description"></textarea>
          @if (form.invalid && form.touched) { <p role="alert">{{ 'sys.ipWhitelist.invalid' | translate }}</p> }
          <button type="submit" hidden></button>
        </form>
      </ng-container>
    </nz-modal>
  `,
  styles: `.ip-form { display: grid; gap: 8px; color: var(--erp-color-text); }
    .ip-form input, .ip-form textarea { padding: 6px 8px; border: 1px solid var(--erp-color-border); border-radius: 6px;
      background: var(--erp-color-bg-container); color: var(--erp-color-text); }
    .ip-form p[role=alert] { color: var(--erp-status-error); margin: 0; }`,
})
export class IpWhitelistList {
  readonly pageSizeOptions = [...PAGE_SIZE_OPTIONS];
  private readonly api = inject(IpWhitelistService);
  private readonly dialogs = inject(ErpDialogService);
  readonly scopes: readonly IpScope[] = ['System', 'Role', 'User'];
  readonly keyword = new FormControl('', { nonNullable: true });
  readonly rows = signal<IpEntry[]>([]);
  readonly total = signal(0);
  readonly page = signal(1);
  readonly pageSize = signal(20);
  readonly sort = signal('id');
  readonly loading = signal(false);
  readonly loadError = signal<string | null>(null);
  readonly actionError = signal<string | null>(null);
  readonly notice = signal<string | null>(null);
  readonly dialog = signal<{ mode: 'create' } | { mode: 'edit'; entry: IpEntry } | null>(null);
  readonly saving = signal(false);
  /** 停用中的規則 ID：防止重複點擊送出兩次。 */
  readonly disabling = signal<number | null>(null);
  readonly saveError = signal<string | null>(null);
  readonly form = new FormGroup({
    scope: new FormControl<IpScope>('System', { nonNullable: true }),
    targetId: new FormControl<number | null>(null),
    ipRange: new FormControl('', { nonNullable: true, validators: [Validators.required, c => validIpRange(c.value) ? null : { ipRange: true }] }),
    validFrom: new FormControl('', { nonNullable: true }),
    validTo: new FormControl('', { nonNullable: true }),
    description: new FormControl('', { nonNullable: true, validators: [Validators.maxLength(200)] }),
  }, { validators: group => {
    const v = group.getRawValue();
    return (v.scope !== 'System' && (!Number.isSafeInteger(v.targetId) || (v.targetId ?? 0) <= 0)) ||
      (v.validFrom && v.validTo && v.validFrom > v.validTo) ? { invalid: true } : null;
  }});

  private readonly listRequest = latestRequest();

  constructor() {
    this.reload();
  }

  scopeKey(scope: IpScope): string {
    return `sys.ipWhitelist.scopes.${scope.toLowerCase()}`;
  }

  sortOrder(column: string): 'ascend' | 'descend' | null {
    return sortOrderOf(this.sort(), column);
  }

  search(): void {
    this.page.set(1);
    this.reload();
  }

  reset(): void {
    this.keyword.setValue('');
    this.search();
  }

  onQueryParams(params: NzTableQueryParams): void {
    const next = nextTableState(params, { page: this.page(), pageSize: this.pageSize(), sort: this.sort() }, ['ipRange'], 'id');
    if (!next) return;
    this.page.set(next.page);
    this.pageSize.set(next.pageSize);
    this.sort.set(next.sort);
    this.reload();
  }

  reload(): void {
    this.loading.set(true);
    this.loadError.set(null);
    const query = { page: this.page(), pageSize: this.pageSize(), keyword: this.keyword.value, sort: this.sort() };
    this.listRequest.run(this.api.list(query), {
      next: result => {
        this.rows.set(result.items);
        this.total.set(result.totalCount);
        this.loading.set(false);
      },
      error: error => {
        this.rows.set([]);
        this.total.set(0);
        this.loadError.set(ipErrorKey(error));
        this.loading.set(false);
      },
    });
  }

  openCreate(): void {
    this.form.reset({ scope: 'System', targetId: null, ipRange: '', validFrom: '', validTo: '', description: '' });
    this.form.controls.scope.enable();
    this.form.controls.targetId.enable();
    this.form.controls.ipRange.enable();
    this.saveError.set(null);
    this.dialog.set({ mode: 'create' });
  }

  openEdit(entry: IpEntry): void {
    this.form.reset({ scope: entry.scope, targetId: entry.targetId, ipRange: entry.ipRange,
      validFrom: entry.validFrom ?? '', validTo: entry.validTo ?? '', description: entry.description ?? '' });
    this.form.controls.scope.disable();
    this.form.controls.targetId.disable();
    this.form.controls.ipRange.disable();
    this.saveError.set(null);
    this.dialog.set({ mode: 'edit', entry });
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
    const v = this.form.getRawValue();
    const dates = { validFrom: v.validFrom || null, validTo: v.validTo || null, description: v.description.trim() || null };
    const request = dialog.mode === 'create'
      ? this.api.create({ ...dates, scope: v.scope, targetId: v.scope === 'System' ? null : v.targetId, ipRange: v.ipRange.trim() } satisfies IpCreate)
      : this.api.update(dialog.entry.id, { ...dates, isActive: dialog.entry.isActive, version: dialog.entry.version });
    this.saving.set(true);
    this.saveError.set(null);
    this.actionError.set(null);
    request.subscribe({
      next: () => {
        this.saving.set(false);
        this.notice.set(dialog.mode === 'create' ? 'sys.ipWhitelist.created' : 'sys.ipWhitelist.updated');
        this.closeDialog();
        this.reload();
      },
      error: error => {
        this.saving.set(false);
        this.saveError.set(ipErrorKey(error));
      },
    });
  }

  confirmDisable(entry: IpEntry): void {
    if (this.disabling() !== null) return;
    this.dialogs.confirm({ titleKey: 'sys.ipWhitelist.disableConfirm', contentKey: 'sys.ipWhitelist.disableHint', danger: true,
      okKey: 'sys.ipWhitelist.disable' }).subscribe(ok => {
      if (!ok || this.disabling() !== null) return;
      this.disabling.set(entry.id);
      this.actionError.set(null);
      this.notice.set(null);
      this.api.update(entry.id, { validFrom: entry.validFrom, validTo: entry.validTo, description: entry.description,
        isActive: false, version: entry.version }).subscribe({
        next: () => {
          this.disabling.set(null);
          this.notice.set('sys.ipWhitelist.disabled');
          this.reload();
        },
        error: error => {
          this.disabling.set(null);
          this.actionError.set(ipErrorKey(error));
        },
      });
    });
  }
}
