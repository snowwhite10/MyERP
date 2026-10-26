import { ChangeDetectionStrategy, Component, computed, effect, inject, InjectionToken, signal, untracked } from '@angular/core';
import { FormControl, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { ActivatedRoute, ParamMap, Router } from '@angular/router';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { TranslatePipe } from '@ngx-translate/core';
import { NzAlertModule } from 'ng-zorro-antd/alert';
import { NzButtonModule } from 'ng-zorro-antd/button';
import { NzInputModule } from 'ng-zorro-antd/input';
import { NzModalModule } from 'ng-zorro-antd/modal';
import { NzTableModule, NzTableQueryParams } from 'ng-zorro-antd/table';
import { NzTagModule } from 'ng-zorro-antd/tag';
import { Observable, skip } from 'rxjs';
import { HasPermissionDirective } from '../../core/auth/has-permission.directive';
import { apiErrorTraceId } from '../../core/http/api-error';
import { PermissionService } from '../../core/auth/permission.service';
import { CompanyContextService } from '../../core/company/company-context.service';
import { ErpDialogService } from '../../shared/dialogs/dialog.service';
import { fetchAllPages, OPTION_PAGE_SIZE } from '../../shared/list-page/fetch-all-pages';
import { latestRequest } from '../../shared/list-page/latest-request';
import { isStaleRowError } from '../../shared/list-page/stale-row';
import { ListPage } from '../../shared/list-page/list-page';
import { PAGE_SIZE_OPTIONS, PagedResult, nextTableState, sortOrderOf } from '../../shared/list-page/table-query';
import { MAX_SORT_ORDER, Process, ProcessCategory, ProcessCategoryService, ProcessService, RATE_PATTERN } from './process.service';
import { Uom, UomService } from './uom.service';
import { ItemAttribute, ItemAttributeService } from './item-attribute.service';

type Row = ProcessCategory | Process | ItemAttribute;
type SortField = 'sortOrder' | 'code' | 'name';
export type SortedMasterKind = 'category' | 'process' | 'attribute';
export const PROCESS_PAGE_KIND = new InjectionToken<SortedMasterKind>('PROCESS_PAGE_KIND');
const PERMISSIONS: Record<SortedMasterKind, string> = { category: 'MD.PROCESS_CATEGORY', process: 'MD.PROCESS', attribute: 'MD.ITEM_ATTRIBUTE' };
const I18N_SCOPES: Record<SortedMasterKind, string> = { category: 'processCategory', process: 'process', attribute: 'itemAttribute' };

/**
 * 介面 8.51–8.53（工序類別）、8.27–8.29（工序）與 8.16（物料屬性定義）：共用列表、排序號、使用數與對話框；
 * 工序另選類別、計件單位與標準工價。
 */
@Component({
  selector: 'erp-process-page',
  imports: [ReactiveFormsModule, TranslatePipe, NzAlertModule, NzButtonModule, NzInputModule,
    NzModalModule, NzTableModule, NzTagModule, HasPermissionDirective, ListPage],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <erp-list-page [titleKey]="key('title')" testId="md-process" [resettable]="false"
      [errorKey]="loadError()" [errorTraceId]="loadTraceId()" (querySubmit)="search()" (retryLoad)="reload()">
      <button erpListActions nz-button nzType="primary" *erpHasPermission="permission + '.CREATE'" data-testid="md-process-create"
        (click)="openCreate()">{{ 'common.action.add' | translate }}</button>
      <input erpListFilters nz-input type="search" name="keyword" autocomplete="off" [formControl]="searchControl" data-testid="md-process-search"
          [attr.aria-label]="key('search') | translate" [placeholder]="key('search') | translate" (keyup.enter)="search()" />
        @if (kind === 'process') {
          <select erpListFilters name="category" [formControl]="filterControl" [attr.aria-label]="key('allCategories') | translate" data-testid="md-process-filter" (change)="search()">
            <option [ngValue]="null">{{ key('allCategories') | translate }}</option>
            @for (category of categories(); track category.id) { <option [ngValue]="category.id">{{ category.code }} — {{ category.name }}</option> }
          </select>
        }
      @if (error() && !loadError()) { <nz-alert nzType="error" nzShowIcon [nzMessage]="error()! | translate" /> }
      @if (!loadError()) {
      <nz-table #table data-testid="md-process-table" nzSize="middle" [nzData]="rows()" [nzLoading]="loading()"
        [nzFrontPagination]="false" [nzTotal]="total()" [nzPageIndex]="page()" [nzPageSize]="pageSize()"
        nzShowSizeChanger [nzPageSizeOptions]="pageSizeOptions" [nzNoResult]="key(appliedKeyword() || appliedFilter() !== null ? 'noResults' : 'empty') | translate"
        (nzQueryParams)="onQueryParams($event)">
        <thead><tr><th nzColumnKey="code" [nzSortFn]="true" [nzSortOrder]="sortOrder('code')">{{ key('code') | translate }}</th>
          <th nzColumnKey="name" [nzSortFn]="true" [nzSortOrder]="sortOrder('name')">{{ key('name') | translate }}</th>
          @if (kind === 'process') { <th>{{ key('category') | translate }}</th><th>{{ key('standardRate') | translate }}</th>
            <th>{{ key('uom') | translate }}</th> }
          <th nzColumnKey="sortOrder" [nzSortFn]="true" [nzSortOrder]="sortOrder('sortOrder')">{{ key('sortOrder') | translate }}</th>
          @if (kind !== 'process') { <th>{{ key(kind === 'category' ? 'processCount' : 'itemCount') | translate }}</th> }
          <th>{{ key('status') | translate }}</th><th>{{ key('actions') | translate }}</th></tr></thead>
        <tbody>@for (row of table.data; track row.id) { <tr [attr.data-testid]="'md-process-row-' + row.code">
          <td>{{ row.code }}</td><td>{{ row.name }}</td>
          @if (kind === 'process') { <td>{{ categoryName(asProcess(row).categoryId) }}</td>
            <td class="number">{{ asProcess(row).standardRate }}</td><td>{{ uomName(asProcess(row).uomId) }}</td> }
          <td class="number">{{ row.sortOrder }}</td>
          @if (kind !== 'process') { <td class="number">{{ usageCount(row) }}</td> }
          <td><nz-tag [nzColor]="row.isActive ? 'success' : 'default'">{{ (row.isActive ? key('active') : key('inactive')) | translate }}</nz-tag></td>
          <td><button nz-button nzType="link" *erpHasPermission="permission + '.EDIT'" (click)="openEdit(row)">{{ 'common.action.edit' | translate }}</button>
            <button nz-button nzType="link" nzDanger *erpHasPermission="permission + '.DELETE'" [disabled]="busyId() !== null"
              (click)="confirmDelete(row)">{{ 'common.action.delete' | translate }}</button></td>
        </tr>}</tbody>
      </nz-table>
      }
    </erp-list-page>
    <nz-modal [nzVisible]="dialogOpen()" [nzTitle]="key('formTitle') | translate"
      [nzOkText]="'common.action.save' | translate" [nzCancelText]="'common.action.cancel' | translate"
      [nzOkLoading]="saving()" (nzOnOk)="save()" (nzOnCancel)="closeDialog()">
      <ng-container *nzModalContent>
        @if (saveError()) { <nz-alert nzType="error" nzShowIcon [nzMessage]="saveError()! | translate" /> }
        <form class="process-form" [formGroup]="form" (ngSubmit)="save()" data-testid="md-process-form">
          <label for="process-code">{{ key('code') | translate }}</label>
          <input nz-input id="process-code" formControlName="code" maxlength="30" data-testid="md-process-code" />
          @if (form.controls.code.touched && form.controls.code.invalid) { <p role="alert">{{ key('invalidCode') | translate }}</p> }
          <label for="process-name">{{ key('name') | translate }}</label>
          <input nz-input id="process-name" formControlName="name" maxlength="200" data-testid="md-process-name" />
          @if (form.controls.name.touched && form.controls.name.invalid) { <p role="alert">{{ key('invalidName') | translate }}</p> }
          @if (kind === 'process') {
            <label for="process-category">{{ key('category') | translate }}</label>
            <select id="process-category" formControlName="categoryId" data-testid="md-process-category">
              <option [ngValue]="null">{{ key('noCategory') | translate }}</option>
              @for (category of categoryOptions(); track category.id) { <option [ngValue]="category.id">{{ category.code }} — {{ category.name }}</option> }
            </select>
            <label for="process-uom">{{ key('uom') | translate }}</label>
            <select id="process-uom" formControlName="uomId" data-testid="md-process-uom">
              <option [ngValue]="null">{{ key('noUom') | translate }}</option>
              @for (uom of uomOptions(); track uom.id) { <option [ngValue]="uom.id">{{ uom.code }} — {{ uom.name }}</option> }
            </select>
            <label for="process-rate">{{ key('standardRate') | translate }}</label>
            <input nz-input id="process-rate" formControlName="standardRate" inputmode="decimal" data-testid="md-process-rate" />
            @if (form.controls.standardRate.touched && form.controls.standardRate.invalid) { <p role="alert">{{ key('invalidRate') | translate }}</p> }
          }
          <label for="process-sort">{{ key('sortOrder') | translate }}</label>
          <input nz-input id="process-sort" type="number" formControlName="sortOrder" min="0" [max]="maxSortOrder" data-testid="md-process-sort" />
          @if (form.controls.sortOrder.touched && form.controls.sortOrder.invalid) { <p role="alert">{{ key('invalidSortOrder') | translate }}</p> }
          @if (editing()) { <label><input type="checkbox" formControlName="isActive" data-testid="md-process-active" />{{ key('active') | translate }}</label> }
        </form>
      </ng-container>
    </nz-modal>
  `,
  styles: `input[erpListFilters]{max-width:320px}.number{text-align:right}.process-form{display:grid;gap:8px}.process-form p[role=alert]{color:var(--erp-status-error);margin:0}`,
})
export class ProcessPage {
  readonly kind = inject(PROCESS_PAGE_KIND);
  readonly permission = PERMISSIONS[this.kind];
  readonly maxSortOrder = MAX_SORT_ORDER;
  readonly pageSizeOptions = [...PAGE_SIZE_OPTIONS];
  private readonly categoryApi = inject(ProcessCategoryService);
  private readonly processApi = inject(ProcessService);
  private readonly attributeApi = inject(ItemAttributeService);
  private readonly uomApi = inject(UomService);
  private readonly permissions = inject(PermissionService);
  private readonly dialogs = inject(ErpDialogService);
  private readonly company = inject(CompanyContextService);
  private readonly route = inject(ActivatedRoute, { optional: true });
  private readonly router = inject(Router, { optional: true });
  private readonly listRequest = latestRequest();
  private readonly categoryRequest = latestRequest();
  private readonly uomRequest = latestRequest();
  private readonly companyId = computed(() => this.company.current()?.id ?? null);
  readonly rows = signal<Row[]>([]); readonly total = signal(0); readonly page = signal(1);
  readonly pageSize = signal(20); readonly sort = signal('sortOrder'); readonly loading = signal(false);
  readonly error = signal<string | null>(null); readonly dialogOpen = signal(false);
  readonly loadError = signal<string | null>(null); readonly loadTraceId = signal<string | null>(null);
  readonly appliedKeyword = signal(''); readonly appliedFilter = signal<number | null>(null);
  readonly editing = signal<Row | null>(null); readonly saving = signal(false);
  readonly saveError = signal<string | null>(null); readonly busyId = signal<number | null>(null);
  readonly categories = signal<ProcessCategory[]>([]); readonly uoms = signal<Uom[]>([]);
  readonly searchControl = new FormControl('', { nonNullable: true });
  readonly filterControl = new FormControl<number | null>(null);
  readonly form = new FormGroup({
    code: new FormControl('', { nonNullable: true, validators: [Validators.required, Validators.pattern(/^[A-Za-z0-9_-]{1,30}$/)] }),
    name: new FormControl('', { nonNullable: true, validators: [Validators.required, Validators.pattern(/.*\S.*/), Validators.maxLength(200)] }),
    categoryId: new FormControl<number | null>(null), uomId: new FormControl<number | null>(null),
    standardRate: new FormControl('0', { nonNullable: true, validators: [Validators.required, Validators.pattern(RATE_PATTERN)] }),
    sortOrder: new FormControl(0, { nonNullable: true, validators: [Validators.required, Validators.min(0),
      Validators.max(MAX_SORT_ORDER), Validators.pattern(/^\d+$/)] }),
    isActive: new FormControl(true, { nonNullable: true }),
  });
  /** 只列啟用選項；編輯時保留原有的停用類別／單位供查看。 */
  readonly categoryOptions = computed(() => this.activeOrCurrent(this.categories(), (this.editing() as Process | null)?.categoryId));
  readonly uomOptions = computed(() => this.activeOrCurrent(this.uoms(), (this.editing() as Process | null)?.uomId));

  constructor() {
    if (this.kind !== 'attribute' && this.route && this.router) {
      this.applyUrl(this.route.snapshot.queryParamMap);
      this.syncUrl(true);
      this.route.queryParamMap.pipe(skip(1), takeUntilDestroyed()).subscribe(params => {
        if (this.applyUrl(params)) this.reload();
        this.syncUrl(true);
      });
    }
    let companyInitialized = false;
    effect(() => {
      this.companyId();
      untracked(() => {
        this.closeDialog();
        if (companyInitialized) { this.filterControl.setValue(null); this.appliedFilter.set(null); this.page.set(1); this.syncUrl(true); }
        companyInitialized = true; this.reload(); this.loadReferences();
      });
    });
  }
  private applyUrl(params: ParamMap): boolean {
    const positiveInt = (value: string | null, fallback: number): number =>
      value && /^[1-9]\d*$/.test(value) && Number.isSafeInteger(Number(value)) ? Number(value) : fallback;
    const keyword = (params.get('keyword') ?? '').trim();
    const size = positiveInt(params.get('pageSize'), 20);
    const pageSize = PAGE_SIZE_OPTIONS.some(option => option === size) ? size : 20;
    const requestedPage = positiveInt(params.get('page'), 1);
    const page = (requestedPage - 1) * pageSize <= 2_147_483_647 ? requestedPage : 1;
    const requestedSort = params.get('sort') ?? 'sortOrder';
    const sort = ['sortOrder', '-sortOrder', 'code', '-code', 'name', '-name'].includes(requestedSort) ? requestedSort : 'sortOrder';
    const requestedFilter = params.get('filter');
    const filter = this.kind === 'process' && requestedFilter && /^[1-9]\d*$/.test(requestedFilter)
      && Number.isSafeInteger(Number(requestedFilter)) ? Number(requestedFilter) : null;
    const changed = keyword !== this.appliedKeyword() || page !== this.page() || pageSize !== this.pageSize()
      || sort !== this.sort() || filter !== this.appliedFilter();
    this.searchControl.setValue(keyword, { emitEvent: false }); this.filterControl.setValue(filter, { emitEvent: false });
    this.appliedKeyword.set(keyword); this.appliedFilter.set(filter);
    this.page.set(page); this.pageSize.set(pageSize); this.sort.set(sort);
    return changed;
  }
  private syncUrl(replaceUrl = false): void {
    if (this.kind === 'attribute' || !this.route || !this.router) return;
    const queryParams = { keyword: this.appliedKeyword() || null, page: this.page() === 1 ? null : this.page(),
      pageSize: this.pageSize() === 20 ? null : this.pageSize(), sort: this.sort() === 'sortOrder' ? null : this.sort(),
      filter: this.kind === 'process' ? this.appliedFilter() : null };
    const current = this.route.snapshot.queryParamMap;
    if (Object.entries(queryParams).every(([key, value]) => current.get(key) === (value === null ? null : String(value)))) return;
    void this.router.navigate([], { relativeTo: this.route, queryParams, queryParamsHandling: 'merge', replaceUrl });
  }
  key(name: string): string { return `md.${I18N_SCOPES[this.kind]}.${name}`; }
  asProcess(row: Row): Process { return row as Process; }
  usageCount(row: Row): number {
    return this.kind === 'category' ? (row as ProcessCategory).processCount : (row as ItemAttribute).itemCount;
  }
  categoryName(id: number | null): string {
    const x = this.categories().find(c => c.id === id); return x ? `${x.code} — ${x.name}` : id === null ? '' : String(id);
  }
  uomName(id: number | null): string { const x = this.uoms().find(u => u.id === id); return x ? x.code : id === null ? '' : String(id); }
  sortOrder(field: SortField): 'ascend' | 'descend' | null {
    return sortOrderOf(this.sort(), field);
  }
  search(): void { this.appliedKeyword.set(this.searchControl.value.trim()); this.appliedFilter.set(this.filterControl.value); this.page.set(1); this.syncUrl(); this.reload(); }
  onQueryParams(params: NzTableQueryParams): void {
    const next = nextTableState(params, { page: this.page(), pageSize: this.pageSize(), sort: this.sort() }, ['sortOrder', 'code', 'name'], 'sortOrder');
    if (!next) return;
    this.page.set(next.page); this.pageSize.set(next.pageSize); this.sort.set(next.sort); this.syncUrl(); this.reload();
  }
  reload(): void {
    this.loading.set(true); this.error.set(null); this.loadError.set(null); this.loadTraceId.set(null);
    const query = { page: this.page(), pageSize: this.pageSize(), keyword: this.appliedKeyword(), sort: this.sort() };
    const request: Observable<PagedResult<Row>> = this.kind === 'process'
      ? this.processApi.list({ ...query, categoryId: this.appliedFilter() }) : this.simpleApi.list(query);
    this.listRequest.run(request, {
      next: result => { this.rows.set(result.items); this.total.set(result.totalCount); this.loading.set(false); },
      error: error => { this.rows.set([]); this.total.set(0); this.loadError.set(this.errorKey(error)); this.loadTraceId.set(apiErrorTraceId(error)); this.loading.set(false); },
    });
  }
  private loadReferences(): void {
    this.categoryRequest.cancel(); this.uomRequest.cancel();
    this.categories.set([]); this.uoms.set([]);
    if (this.kind !== 'process') return;
    if (this.permissions.has('MD.PROCESS_CATEGORY.VIEW')) {
      this.categoryRequest.run(fetchAllPages(page => this.categoryApi.list({ page, pageSize: OPTION_PAGE_SIZE, sort: 'sortOrder' })), {
        next: rows => this.categories.set(rows), error: () => this.categories.set([]),
      });
    }
    if (this.permissions.has('MD.UOM.VIEW')) {
      this.uomRequest.run(fetchAllPages(page => this.uomApi.list({ page, pageSize: OPTION_PAGE_SIZE, sort: 'code' })), {
        next: rows => this.uoms.set(rows), error: () => this.uoms.set([]),
      });
    }
  }
  private activeOrCurrent<T extends { id: number; isActive: boolean }>(rows: readonly T[], current: number | null | undefined): T[] {
    return rows.filter(x => x.isActive || x.id === current);
  }
  openCreate(): void {
    this.editing.set(null);
    this.form.reset({ code: '', name: '', categoryId: this.filterControl.value, uomId: null, standardRate: '0', sortOrder: 0, isActive: true });
    if (this.form.controls.categoryId.value !== null && !this.categoryOptions().some(x => x.id === this.form.controls.categoryId.value))
      this.form.controls.categoryId.setValue(null);
    this.form.controls.code.enable(); this.saveError.set(null); this.dialogOpen.set(true);
  }
  openEdit(row: Row): void {
    this.editing.set(row);
    const process = this.kind === 'process' ? row as Process : null;
    this.form.reset({ code: row.code, name: row.name, categoryId: process?.categoryId ?? null, uomId: process?.uomId ?? null,
      standardRate: process ? String(process.standardRate) : '0', sortOrder: row.sortOrder, isActive: row.isActive });
    this.form.controls.code.disable(); this.saveError.set(null); this.dialogOpen.set(true);
  }
  closeDialog(): void { this.dialogOpen.set(false); this.saveError.set(null); }
  save(): void {
    if (this.saving()) return;
    this.form.markAllAsTouched();
    if (this.kind !== 'process') { this.form.controls.standardRate.setValue('0'); }
    if (this.form.invalid) return;
    const value = this.form.getRawValue(), row = this.editing();
    const request: Observable<Row> = this.kind !== 'process'
      ? (row ? this.simpleApi.update(row.id, { name: value.name, sortOrder: value.sortOrder, isActive: value.isActive, version: row.version })
        : this.simpleApi.create({ code: value.code, name: value.name, sortOrder: value.sortOrder }))
      : (row ? this.processApi.update(row.id, { ...value, version: row.version }) : this.processApi.create(value));
    this.saving.set(true); this.saveError.set(null);
    request.subscribe({
      next: () => { this.saving.set(false); this.closeDialog(); this.reload(); },
      error: error => { this.saving.set(false); this.saveError.set(this.errorKey(error)); if (isStaleRowError(error)) this.reload(); },
    });
  }
  confirmDelete(row: Row): void {
    if (this.busyId() !== null) return;
    this.dialogs.confirm({ titleKey: this.key('deleteConfirm'), contentKey: 'common.confirm.irreversible',
      params: { code: row.code }, okKey: 'common.action.delete', danger: true }).subscribe(ok => {
      if (!ok) return;
      this.busyId.set(row.id);
      const request = this.kind === 'process' ? this.processApi.remove(row.id, row.version) : this.simpleApi.remove(row.id, row.version);
      request.subscribe({
        next: () => { this.busyId.set(null); this.reload(); },
        error: error => { this.busyId.set(null); if (isStaleRowError(error)) this.reload(); this.error.set(this.errorKey(error)); },
      });
    });
  }
  private errorKey(error: unknown): string { return this.kind === 'process' ? this.processApi.errorKey(error) : this.simpleApi.errorKey(error); }
  /** 只有代碼、名稱與排序號的主檔（工序類別、物料屬性定義）。 */
  private get simpleApi(): ProcessCategoryService | ItemAttributeService {
    return this.kind === 'category' ? this.categoryApi : this.attributeApi;
  }
}
