import { ChangeDetectionStrategy, Component, computed, effect, inject, signal, untracked } from '@angular/core';
import { FormControl, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { TranslatePipe } from '@ngx-translate/core';
import { NzAlertModule } from 'ng-zorro-antd/alert';
import { NzButtonModule } from 'ng-zorro-antd/button';
import { NzInputModule } from 'ng-zorro-antd/input';
import { NzModalModule } from 'ng-zorro-antd/modal';
import { NzPopconfirmModule } from 'ng-zorro-antd/popconfirm';
import { NzTableModule, NzTableQueryParams } from 'ng-zorro-antd/table';
import { NzTagModule } from 'ng-zorro-antd/tag';
import { Observable } from 'rxjs';
import { HasPermissionDirective } from '../../core/auth/has-permission.directive';
import { PermissionService } from '../../core/auth/permission.service';
import { apiErrorTraceId } from '../../core/http/api-error';
import { CompanyContextService } from '../../core/company/company-context.service';
import { fetchAllPages, OPTION_PAGE_SIZE } from '../../shared/list-page/fetch-all-pages';
import { latestRequest } from '../../shared/list-page/latest-request';
import { isStaleRowError } from '../../shared/list-page/stale-row';
import { ListPage } from '../../shared/list-page/list-page';
import { PAGE_SIZE_OPTIONS, nextTableState, sortOrderOf } from '../../shared/list-page/table-query';
import { BusinessPartner, PARTNER_PAGE } from './business-partner.service';
import { PartnerContactDialog } from './partner-contact-dialog';
import { PartnerImportDialog } from './partner-import-dialog';
import { MasterCategory } from './master-category.service';
import { PaymentTermService } from './payment-term.service';
import { SettlementMethodService } from './settlement-method.service';

/** 客戶、供應商共用的維護頁；API、權限、文字與 data-testid 前綴由路由提供的 `PARTNER_PAGE` 決定。 */
@Component({
  selector: 'erp-partner-list',
  imports: [ReactiveFormsModule, TranslatePipe, NzAlertModule, NzButtonModule, NzInputModule,
    NzModalModule, NzPopconfirmModule, NzTableModule, NzTagModule, HasPermissionDirective, ListPage, PartnerContactDialog, PartnerImportDialog],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <erp-list-page [titleKey]="t('title')" [testId]="config.testId" [titleTestId]="id('title')"
      [errorKey]="loadError()" [errorTraceId]="loadTraceId()" [resettable]="false"
      (querySubmit)="search()" (retryLoad)="reload()">
        @if (importKind) { <button erpListActions nz-button [attr.data-testid]="id('import-open')" *erpHasPermission="perm('IMPORT')"
          (click)="importOpen.set(true)">{{ 'md.customerImport.open' | translate }}</button> }
        <button erpListActions nz-button nzType="primary" [attr.data-testid]="id('create')"
          *erpHasPermission="perm('CREATE')" (click)="openCreate()">{{ 'common.action.add' | translate }}</button>
        <input erpListFilters nz-input type="search" name="keyword" autocomplete="off" [attr.data-testid]="id('search')" [formControl]="searchControl"
          [attr.aria-label]="t('search') | translate" [placeholder]="t('search') | translate" (keyup.enter)="search()" />
      @if (error() && !loadError()) { <nz-alert nzType="error" nzShowIcon [nzMessage]="actionErrorMessage" />
        <ng-template #actionErrorMessage>{{ error()! | translate }}
          @if (actionTraceId()) { {{ 'common.state.traceId' | translate }}: {{ actionTraceId() }} }
        </ng-template>
      }
      @if (!loadError()) {
      <nz-table #table [attr.data-testid]="id('table')" nzSize="middle" [nzData]="rows()" [nzLoading]="loading()"
        [nzFrontPagination]="false" [nzTotal]="total()" [nzPageIndex]="page()" [nzPageSize]="pageSize()"
        nzShowSizeChanger [nzPageSizeOptions]="pageSizeOptions" [nzNoResult]="t(appliedKeyword() ? 'noResults' : 'empty') | translate"
        (nzQueryParams)="onQueryParams($event)">
        <thead><tr>
          <th nzColumnKey="code" [nzSortFn]="true" [nzSortOrder]="sortOrder('code')">{{ t('code') | translate }}</th>
          <th nzColumnKey="name" [nzSortFn]="true" [nzSortOrder]="sortOrder('name')">{{ t('name') | translate }}</th>
          <th>{{ t('category') | translate }}</th><th>{{ 'md.partnerPayment.paymentTerm' | translate }}</th>
          <th>{{ t('status') | translate }}</th>
          <th>{{ t('review') | translate }}</th>
          <th>{{ t('actions') | translate }}</th>
        </tr></thead>
        <tbody>@for (row of table.data; track row.id) {
          <tr [attr.data-testid]="id('row-' + row.code)">
            <td>{{ row.code }}</td><td>{{ row.name }}</td>
            <td>{{ categoryName(row.categoryId) }}</td>
            <td [attr.data-testid]="id('payment-term-' + row.code)">{{ optionName(paymentTerms(), row.defaultPaymentTermId) }}</td>
            <td><nz-tag [nzColor]="row.isActive ? 'success' : 'default'">
              {{ t(row.isActive ? 'active' : 'inactive') | translate }}
            </nz-tag></td>
            <td><nz-tag [nzColor]="reviewColor(row.reviewStatus)">
              {{ t('review' + (row.reviewStatus ?? 'Approved')) | translate }}
            </nz-tag></td>
            <td class="actions"><button nz-button nzType="link" *erpHasPermission="perm('EDIT')"
              [attr.data-testid]="id('edit-' + row.code)" (click)="openEdit(row)">{{ 'common.action.edit' | translate }}</button>
              @if (hasContacts) { <button nz-button nzType="link" [attr.data-testid]="id('contacts-' + row.code)"
                (click)="contactPartner.set(row)">{{ 'md.customerContact.open' | translate }}</button> }
              @if (row.reviewStatus !== 'Approved') {
                <button nz-button nzType="link" *erpHasPermission="perm('APPROVE')" [nzLoading]="actingId() === row.id"
                  [attr.data-testid]="id('submit-' + row.code)" (click)="submitPartner(row)">{{ t('submit') | translate }}</button>
              }
              <button nz-button nzType="link" nzDanger *erpHasPermission="perm('DELETE')" [nzLoading]="actingId() === row.id"
                nz-popconfirm [nzPopconfirmTitle]="t('deleteConfirm') | translate" (nzOnConfirm)="deletePartner(row)"
                [attr.data-testid]="id('delete-' + row.code)">{{ t('delete') | translate }}</button></td>
          </tr>
        }</tbody>
      </nz-table>
      }
    </erp-list-page>
    <nz-modal [nzVisible]="dialogOpen()" [nzTitle]="t('formTitle') | translate"
      [nzOkText]="'common.action.save' | translate" [nzCancelText]="'common.action.cancel' | translate"
      [nzOkLoading]="saving()" (nzOnOk)="save()" (nzOnCancel)="closeDialog()">
      <ng-container *nzModalContent>
        @if (saveError()) { <nz-alert nzType="error" nzShowIcon [attr.data-testid]="id('save-error')" [nzMessage]="saveError()! | translate" /> }
        <form class="partner-form" [formGroup]="form" (ngSubmit)="save()" [attr.data-testid]="id('form')">
          <label [for]="id('code')">{{ t('code') | translate }}</label>
          <input nz-input [id]="id('code')" [attr.data-testid]="id('code')" formControlName="code" maxlength="30" />
          @if (form.controls.code.touched && form.controls.code.invalid) { <p role="alert">{{ t('invalidCode') | translate }}</p> }
          <label [for]="id('name')">{{ t('name') | translate }}</label>
          <input nz-input [id]="id('name')" [attr.data-testid]="id('name')" formControlName="name" maxlength="200" />
          @if (form.controls.name.touched && form.controls.name.invalid) { <p role="alert">{{ t('invalidName') | translate }}</p> }
          <label [for]="id('category')">{{ t('category') | translate }}</label>
          <select [id]="id('category')" [attr.data-testid]="id('category')" formControlName="categoryId">
            <option [ngValue]="null">{{ t('noCategory') | translate }}</option>
            @for (category of categories(); track category.id) {
              <option [ngValue]="category.id" [disabled]="!category.isActive && category.id !== editing()?.categoryId">{{ category.name }}</option>
            }
          </select>
          <label [for]="id('payment-term')">{{ 'md.partnerPayment.paymentTerm' | translate }}</label>
          <select [id]="id('payment-term')" [attr.data-testid]="id('payment-term')" formControlName="defaultPaymentTermId">
            <option [ngValue]="null">{{ 'md.partnerPayment.none' | translate }}</option>
            @for (term of paymentTerms(); track term.id) {
              <option [ngValue]="term.id" [disabled]="!term.isActive && term.id !== editing()?.defaultPaymentTermId">{{ term.name }}</option>
            }
          </select>
          <label [for]="id('settlement-method')">{{ 'md.partnerPayment.settlementMethod' | translate }}</label>
          <select [id]="id('settlement-method')" [attr.data-testid]="id('settlement-method')" formControlName="defaultSettlementMethodId">
            <option [ngValue]="null">{{ 'md.partnerPayment.none' | translate }}</option>
            @for (method of settlementMethods(); track method.id) {
              <option [ngValue]="method.id" [disabled]="!method.isActive && method.id !== editing()?.defaultSettlementMethodId">{{ method.name }}</option>
            }
          </select>
          @if (editing()) { <label><input type="checkbox" formControlName="isActive" [attr.data-testid]="id('active')" />
            {{ t('active') | translate }}</label> }
        </form>
      </ng-container>
    </nz-modal>
    @if (contactKind; as kind) { <erp-partner-contact-dialog [kind]="kind" [partner]="contactPartner()" (closed)="contactPartner.set(null)" /> }
    @if (importKind; as kind) { <erp-partner-import-dialog [kind]="kind" [open]="importOpen()" (closed)="importOpen.set(false)"
      (imported)="reload()" /> }
  `,
  styles: `
    input[erpListFilters] { max-width: 320px; }
    .actions { white-space: nowrap; }
    .partner-form { display: grid; gap: 8px; }
    .partner-form p { color: var(--erp-status-error); margin: 0; }
  `,
})
export class PartnerList {
  readonly pageSizeOptions = [...PAGE_SIZE_OPTIONS];
  readonly config = inject(PARTNER_PAGE);
  private readonly api = inject(this.config.api);
  private readonly categoryApi = inject(this.config.categoryApi);
  private readonly company = inject(CompanyContextService);
  private readonly permissions = inject(PermissionService);
  private readonly listRequest = latestRequest();
  private readonly categoryRequest = latestRequest();
  private readonly paymentTermApi = inject(PaymentTermService);
  private readonly settlementMethodApi = inject(SettlementMethodService);
  private readonly paymentTermRequest = latestRequest();
  private readonly settlementMethodRequest = latestRequest();
  private readonly companyId = computed(() => this.company.current()?.id ?? null);
  readonly rows = signal<BusinessPartner[]>([]);
  readonly categories = signal<MasterCategory[]>([]);
  /** 預設付款條件與結算方式選項（ADR-047）；沒有 VIEW 權限時為空。 */
  readonly paymentTerms = signal<MasterCategory[]>([]);
  readonly settlementMethods = signal<MasterCategory[]>([]);
  readonly total = signal(0);
  readonly page = signal(1);
  readonly pageSize = signal(20);
  readonly sort = signal('code');
  readonly loading = signal(false);
  readonly error = signal<string | null>(null);
  readonly loadError = signal<string | null>(null);
  readonly loadTraceId = signal<string | null>(null);
  readonly actionTraceId = signal<string | null>(null);
  readonly appliedKeyword = signal('');
  readonly dialogOpen = signal(false);
  readonly editing = signal<BusinessPartner | null>(null);
  /** 正在查看聯絡人的往來對象（只限設定了 `PartnerPage.contacts` 的頁面）。 */
  readonly contactPartner = signal<BusinessPartner | null>(null);
  readonly contactKind = this.config.contacts;
  readonly hasContacts = this.contactKind !== undefined;
  readonly importKind = this.config.imports;
  readonly importOpen = signal(false);
  readonly saving = signal(false);
  readonly actingId = signal<number | null>(null);
  readonly saveError = signal<string | null>(null);
  readonly searchControl = new FormControl('', { nonNullable: true });
  readonly form = new FormGroup({
    code: new FormControl('', { nonNullable: true, validators: [Validators.required, Validators.pattern(/^[A-Za-z0-9_-]{1,30}$/)] }),
    name: new FormControl('', { nonNullable: true, validators: [Validators.required, Validators.pattern(/.*\S.*/), Validators.maxLength(200)] }),
    categoryId: new FormControl<number | null>(null),
    defaultPaymentTermId: new FormControl<number | null>(null),
    defaultSettlementMethodId: new FormControl<number | null>(null),
    isActive: new FormControl(true, { nonNullable: true }),
  });

  constructor() {
    effect(() => {
      this.companyId();
      untracked(() => {
        this.closeDialog(); this.contactPartner.set(null); this.importOpen.set(false); this.page.set(1);
        this.reload(); this.loadCategories(); this.loadPaymentOptions();
      });
    });
  }

  /** 此頁的 i18n key。 */
  t(key: string): string { return `${this.config.i18n}.${key}`; }
  /** 此頁的權限代碼。 */
  perm(action: string): string { return `${this.config.permission}.${action}`; }
  /** 此頁的 data-testid／元素 id。 */
  id(key: string): string { return `${this.config.testId}-${key}`; }
  categoryName(id: number | null): string { return this.optionName(this.categories(), id); }
  optionName(options: MasterCategory[], id: number | null | undefined): string {
    return options.find((option) => option.id === id)?.name ?? '';
  }
  reviewColor(status: BusinessPartner['reviewStatus']): string {
    return status === 'Pending' ? 'processing' : status === 'Rejected' ? 'error' : 'success';
  }
  /** 送審（ADR-046）；同一時間只處理一筆，完成後重新載入以取得新版本與審核狀態。 */
  submitPartner(row: BusinessPartner): void { this.act(row, this.api.submit(row.id, row.version)); }
  /**
   * 軟刪除。Pending 同時代表「從未送審」與「審批中」，前端無法分辨，所以一律顯示刪除鈕，
   * 由後端判斷：審批中回 422 `APPROVAL_PENDING`、已被單據引用回 409（MD-B02）。
   */
  deletePartner(row: BusinessPartner): void { this.act(row, this.api.delete(row.id, row.version)); }
  private act(row: BusinessPartner, request: Observable<unknown>): void {
    if (this.actingId() !== null) return;
    this.actingId.set(row.id); this.error.set(null); this.actionTraceId.set(null);
    request.subscribe({
      next: () => { this.actingId.set(null); this.reload(); },
      error: (error: unknown) => {
        this.actingId.set(null); if (isStaleRowError(error)) this.reload(); this.error.set(this.api.errorKey(error));
        this.actionTraceId.set(apiErrorTraceId(error));
      },
    });
  }
  sortOrder(field: 'code' | 'name'): 'ascend' | 'descend' | null {
    return sortOrderOf(this.sort(), field);
  }
  search(): void { this.appliedKeyword.set(this.searchControl.value.trim()); this.page.set(1); this.reload(); }
  onQueryParams(params: NzTableQueryParams): void {
    const next = nextTableState(params, { page: this.page(), pageSize: this.pageSize(), sort: this.sort() }, ['code', 'name'], 'code');
    if (!next) return;
    this.page.set(next.page); this.pageSize.set(next.pageSize); this.sort.set(next.sort); this.reload();
  }
  reload(): void {
    this.loading.set(true); this.error.set(null); this.loadError.set(null); this.loadTraceId.set(null); this.actionTraceId.set(null);
    this.listRequest.run(this.api.list({ page: this.page(), pageSize: this.pageSize(),
      keyword: this.appliedKeyword(), sort: this.sort() }), {
      next: (result) => { this.rows.set(result.items); this.total.set(result.totalCount); this.loading.set(false); },
      error: (error: unknown) => { this.rows.set([]); this.total.set(0); this.error.set(this.api.errorKey(error));
        this.loadError.set(this.api.errorKey(error)); this.loadTraceId.set(apiErrorTraceId(error)); this.loading.set(false); },
    });
  }
  loadCategories(): void {
    if (!this.permissions.has(this.config.categoryPermission)) { this.categories.set([]); return; }
    this.categoryRequest.run(fetchAllPages((page) => this.categoryApi.list(optionQuery(page))), {
      next: (items) => this.categories.set(items),
      error: () => this.categories.set([]),
    });
  }
  /** 付款條件與結算方式選項；各自依 VIEW 權限載入，跨頁載齊（MD-F02）。 */
  loadPaymentOptions(): void {
    if (this.permissions.has('MD.PAYMENT_TERM.VIEW')) {
      this.paymentTermRequest.run(fetchAllPages((page) => this.paymentTermApi.list(optionQuery(page))), {
        next: (items) => this.paymentTerms.set(items), error: () => this.paymentTerms.set([]),
      });
    } else this.paymentTerms.set([]);
    if (this.permissions.has('MD.SETTLEMENT_METHOD.VIEW')) {
      this.settlementMethodRequest.run(fetchAllPages((page) => this.settlementMethodApi.list(optionQuery(page))), {
        next: (items) => this.settlementMethods.set(items), error: () => this.settlementMethods.set([]),
      });
    } else this.settlementMethods.set([]);
  }
  openCreate(): void {
    this.editing.set(null);
    this.form.reset({ code: '', name: '', categoryId: null, defaultPaymentTermId: null, defaultSettlementMethodId: null, isActive: true });
    this.form.controls.code.enable(); this.saveError.set(null); this.dialogOpen.set(true);
  }
  openEdit(row: BusinessPartner): void {
    this.editing.set(row);
    this.form.reset({ code: row.code, name: row.name, categoryId: row.categoryId,
      defaultPaymentTermId: row.defaultPaymentTermId ?? null, defaultSettlementMethodId: row.defaultSettlementMethodId ?? null,
      isActive: row.isActive });
    this.form.controls.code.disable(); this.saveError.set(null); this.dialogOpen.set(true);
  }
  closeDialog(): void { this.dialogOpen.set(false); this.saveError.set(null); }
  save(): void {
    if (this.saving()) return;
    this.form.markAllAsTouched();
    if (this.form.invalid) return;
    const draft = this.form.getRawValue();
    const row = this.editing();
    const fields = { name: draft.name, categoryId: draft.categoryId,
      defaultPaymentTermId: draft.defaultPaymentTermId, defaultSettlementMethodId: draft.defaultSettlementMethodId };
    const request = row ? this.api.update(row.id, { ...fields, isActive: draft.isActive, version: row.version })
      : this.api.create({ ...fields, code: draft.code });
    this.saving.set(true); this.saveError.set(null);
    request.subscribe({
      next: () => { this.saving.set(false); this.closeDialog(); this.reload(); },
      error: (error: unknown) => { this.saving.set(false); this.saveError.set(this.api.errorKey(error)); if (isStaleRowError(error)) this.reload(); },
    });
  }
}

/** 下拉選項的分頁查詢，依名稱排序。 */
function optionQuery(page: number) { return { page, pageSize: OPTION_PAGE_SIZE, sort: 'name' }; }
