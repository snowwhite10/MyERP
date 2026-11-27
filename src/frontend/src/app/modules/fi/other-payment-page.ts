import { ChangeDetectionStrategy, Component, DestroyRef, effect, inject, signal, untracked, ViewChild } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router } from '@angular/router';
import { TranslatePipe } from '@ngx-translate/core';
import { NzAlertModule } from 'ng-zorro-antd/alert';
import { NzButtonModule } from 'ng-zorro-antd/button';
import { NzInputModule } from 'ng-zorro-antd/input';
import { NzTableModule, NzTableQueryParams } from 'ng-zorro-antd/table';
import { PermissionService } from '../../core/auth/permission.service';
import { CompanyContextService } from '../../core/company/company-context.service';
import { apiErrorTraceId } from '../../core/http/api-error';
import { ApiErrorDisplay } from '../../shared/api-error-display';
import { HasUnsavedChanges } from '../../shared/document-page/unsaved-changes.guard';
import { validIsoDate } from '../../shared/date';
import { formatDecimalText } from '../../shared/decimal-text';
import { nextTableState, PAGE_SIZE_OPTIONS, sortOrderOf } from '../../shared/list-page/table-query';
import { DocumentStatus, StatusTag } from '../../shared/status-tag/status-tag';
import { OTHER_PAYMENT_STATUSES, OtherPaymentMapRow, OtherPaymentService, OtherPaymentStatus, OtherPaymentSummary } from './other-payment.service';
import { OtherPaymentEditor } from './other-payment-editor';
import { OtherPaymentMapEditor } from './other-payment-map-editor';

/** Server sort fields (OtherPaymentQueryService.SortFields) and the server default. */
const SORTS = ['businessDate', 'documentNo', 'amount'] as const;
const DEFAULT_SORT = '-businessDate';
const KEYWORD_MAX = 50;
interface Filters { keyword: string; status: string; from: string; to: string }
const NO_FILTERS: Filters = { keyword: '', status: '', from: '', to: '' };

const TAG: Record<OtherPaymentStatus, DocumentStatus> = {
  Draft: 'Draft', Pending: 'Submitted', Rejected: 'Rejected', Approved: 'Approved', Posted: 'Posted',
};

/** Company-scoped other-payment list; the editor owns document writes and workflow actions. */
@Component({
  selector: 'erp-other-payment-page',
  imports: [FormsModule, TranslatePipe, NzAlertModule, NzButtonModule, NzInputModule, NzTableModule, ApiErrorDisplay, StatusTag, OtherPaymentEditor, OtherPaymentMapEditor],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <section class="payments">
      <header><h1>{{ 'fi.otherPayment.title' | translate }}</h1>
        <div class="head-actions">@if (canViewMaps) { <button nz-button type="button" (click)="showMapEditor.set(!showMapEditor())" data-testid="fi-op-manage-map">
          {{ 'fi.otherPayment.mapEditor.open' | translate }}</button> }
          @if (canCreate) { <button nz-button nzType="primary" type="button" (click)="openCreate()" data-testid="fi-op-create">{{ 'fi.otherPayment.create' | translate }}</button> }</div>
      </header>
      <p class="hint">{{ 'fi.otherPayment.nonAp' | translate }}</p>
      @if (showMapEditor() && canViewMaps) { <erp-other-payment-map-editor [rows]="mapRows()" [canEdit]="canEditMaps"
        (changed)="load()" (dismissed)="showMapEditor.set(false)" /> }
      @if (deletedNo(); as no) { <nz-alert nzType="success" [nzMessage]="'fi.otherPayment.deleted' | translate: { no }" nzShowIcon
        role="status" aria-live="polite" data-testid="fi-op-deleted" /> }
      @if (editor(); as selection) { <erp-other-payment-editor [paymentId]="selection.id" (changed)="load()"
        (deleted)="deletedNo.set($event)" (dismissed)="editor.set(null)" /> }
      <form class="filters" (ngSubmit)="search()" data-testid="fi-op-filters">
        <input nz-input type="search" name="keyword" autocomplete="off" spellcheck="false" maxlength="50" [(ngModel)]="keyword"
          [attr.aria-label]="'fi.otherPayment.keyword' | translate" [placeholder]="'fi.otherPayment.keywordHint' | translate" data-testid="fi-op-keyword" />
        <label>{{ 'fi.otherPayment.status' | translate }}
          <select name="status" [(ngModel)]="status" data-testid="fi-op-status-filter">
            <option value="">{{ 'fi.otherPayment.allStatuses' | translate }}</option>
            @for (value of statuses; track value) { <option [value]="value">{{ statusKey(value) | translate }}</option> }
          </select></label>
        <label>{{ 'fi.otherPayment.from' | translate }} <input type="date" name="from" [(ngModel)]="from" data-testid="fi-op-from" /></label>
        <label>{{ 'fi.otherPayment.to' | translate }} <input type="date" name="to" [(ngModel)]="to" data-testid="fi-op-to" /></label>
        <button nz-button nzType="primary" type="submit" data-testid="fi-op-search">{{ 'fi.otherPayment.search' | translate }}</button>
        <button nz-button type="button" (click)="reset()" data-testid="fi-op-reset">{{ 'fi.otherPayment.reset' | translate }}</button>
      </form>
      @if (rangeInvalid()) { <p class="field-error" role="alert" data-testid="fi-op-range-error">{{ 'fi.otherPayment.rangeInvalid' | translate }}</p> }
      @if (error()) { <erp-api-error-display [messageKey]="error()!" [traceId]="traceId()" data-testid="fi-op-error" />
        <button nz-button type="button" (click)="load()" data-testid="fi-op-retry">{{ 'fi.otherPayment.retry' | translate }}</button> }
      <p class="scroll-hint">{{ 'fi.otherPayment.scrollHint' | translate }}</p>
      <div class="table-scroll"><nz-table #table [nzData]="rows()" [nzLoading]="loading()" [nzFrontPagination]="false" [nzTotal]="total()"
        [nzPageIndex]="page()" [nzPageSize]="pageSize()" [nzShowSizeChanger]="true" [nzPageSizeOptions]="pageSizeOptions" (nzQueryParams)="onQuery($event)"
        [nzNoResult]="(listFailed() ? 'fi.otherPayment.loadFailed' : filtered() ? 'fi.otherPayment.searchEmpty' : 'fi.otherPayment.empty') | translate"
        data-testid="fi-op-table">
        <thead><tr>
          <th nzColumnKey="documentNo" [nzSortFn]="true" [nzSortOrder]="sortOrderOf(sort(), 'documentNo')">{{ 'fi.otherPayment.documentNo' | translate }}</th>
          <th nzColumnKey="businessDate" [nzSortFn]="true" [nzSortOrder]="sortOrderOf(sort(), 'businessDate')">{{ 'fi.otherPayment.businessDate' | translate }}</th>
          <th>{{ 'fi.otherPayment.payee' | translate }}</th><th>{{ 'fi.otherPayment.category' | translate }}</th>
          <th>{{ 'fi.otherPayment.mapping' | translate }}</th><th>{{ 'fi.otherPayment.fundAccount' | translate }}</th>
          <th>{{ 'fi.otherPayment.bankReference' | translate }}</th>
          <th nzColumnKey="amount" [nzSortFn]="true" [nzSortOrder]="sortOrderOf(sort(), 'amount')" nzAlign="right">{{ 'fi.otherPayment.amount' | translate }}</th>
          <th>{{ 'fi.otherPayment.status' | translate }}</th><th>{{ 'fi.otherPayment.journal' | translate }}</th>
        </tr></thead>
        <tbody>@for (row of table.data; track row.id) {
          <tr [attr.data-testid]="'fi-op-row-' + row.id">
            <td><button class="doc-link" type="button" (click)="openDetail(row.id)" [attr.aria-label]="row.documentNo"
              [attr.data-testid]="'fi-op-open-' + row.id" translate="no">{{ row.documentNo }}</button></td><td>{{ row.businessDate }}</td>
            <td class="wrap" [title]="row.payeeDescription">{{ row.payeeDescription }}</td>
            <td>{{ label(row.expenseCategoryCode, row.expenseCategoryName, row.expenseCategoryId) }}</td>
            <td class="mapping" aria-live="polite" [attr.data-testid]="'fi-op-map-' + row.id">{{ mappingKey(row) | translate }}</td>
            <td>{{ label(row.bankAccountCode, row.bankAccountName, row.bankAccountId) }}</td>
            <td class="wrap" [title]="row.bankReference">{{ row.bankReference }}</td>
            <td class="num">{{ money(row.amount) }} {{ row.currency }}</td>
            <td><erp-status-tag [status]="tag(row.status)" [labelKey]="statusKey(row.status)" /></td>
            <td translate="no">{{ row.journalId ? '#' + row.journalId : '—' }}</td>
          </tr>
        }</tbody>
      </nz-table></div>
    </section>`,
  styles: `:host{display:block;min-width:0}.payments{display:grid;gap:16px;min-width:0;color:var(--erp-color-text)}header{display:flex;align-items:center;justify-content:space-between;gap:8px}h1{font-size:20px;margin:0;text-wrap:balance}.head-actions{display:flex;flex-wrap:wrap;gap:8px}.hint,.scroll-hint{margin:0;color:var(--erp-color-text-secondary)}.scroll-hint{display:none}@media(max-width:1500px){.scroll-hint{display:block}}.filters{display:flex;align-items:center;gap:8px;flex-wrap:wrap}.filters input[type=search]{max-width:260px}.filters label{display:flex;gap:4px;align-items:center}.filters select,.filters input[type=date]{background:var(--erp-color-bg-container);color:var(--erp-color-text);border:1px solid var(--erp-color-border);border-radius:4px}.field-error{margin:0;color:var(--erp-color-error-text)}.table-scroll{min-width:0;overflow-x:auto;overscroll-behavior-inline:contain;scrollbar-gutter:stable}.table-scroll nz-table{display:block;min-width:1300px}.table-scroll th{white-space:nowrap}.table-scroll ::ng-deep .ant-table-pagination.ant-pagination{position:sticky;left:0;width:fit-content;max-width:100%;justify-content:flex-start}.table-scroll th:nth-child(2),.table-scroll td:nth-child(2){min-width:130px;white-space:nowrap}.table-scroll th:nth-child(5),.table-scroll td:nth-child(5){min-width:220px}.wrap{overflow-wrap:anywhere;min-width:9em}.num{text-align:right;font-variant-numeric:tabular-nums;white-space:nowrap}.doc-link{border:0;background:none;color:var(--erp-color-primary);text-decoration:underline;cursor:pointer}.doc-link:hover{filter:brightness(.8)}button:focus-visible,input:focus-visible,select:focus-visible{outline:2px solid var(--erp-color-primary);outline-offset:2px}`,
})
export class OtherPaymentPage implements HasUnsavedChanges {
  @ViewChild(OtherPaymentEditor) private editorComponent?: OtherPaymentEditor;
  private readonly api = inject(OtherPaymentService);
  private readonly company = inject(CompanyContextService);
  private readonly route = inject(ActivatedRoute, { optional: true });
  private readonly router = inject(Router, { optional: true });
  readonly pageSizeOptions = [...PAGE_SIZE_OPTIONS];
  readonly sortOrderOf = sortOrderOf;
  readonly canCreate = inject(PermissionService).has('AP.OTHER_PAYMENT.CREATE');
  readonly canEditMaps = inject(PermissionService).has('GL.OTHER_PAYMENT_MAP.EDIT');
  readonly canViewMaps = inject(PermissionService).has('GL.OTHER_PAYMENT_MAP.VIEW');
  readonly statuses = OTHER_PAYMENT_STATUSES;
  readonly rows = signal<OtherPaymentSummary[]>([]);
  readonly editor = signal<{ id: number | null } | null>(null);
  readonly showMapEditor = signal(false);
  /** Document number of the draft just deleted from the editor; cleared when another editor opens. */
  readonly deletedNo = signal<string | null>(null);
  readonly mapRows = signal<OtherPaymentMapRow[]>([]);
  readonly maps = signal<Map<number, OtherPaymentMapRow>>(new Map());
  readonly mapFailed = signal(false); readonly mapLoading = signal(false);
  readonly total = signal(0); readonly page = signal(1); readonly pageSize = signal(20); readonly sort = signal(DEFAULT_SORT);
  readonly loading = signal(false); readonly error = signal<string | null>(null); readonly traceId = signal<string | null>(null);
  readonly listFailed = signal(false); readonly filtered = signal(false); readonly rangeInvalid = signal(false);
  keyword = ''; status = ''; from = ''; to = '';
  /** Filters of the last accepted search: paging, sorting, retry and the URL use these, not unsent typing. */
  private applied: Filters = NO_FILTERS;
  private generation = 0; private first = true;

  constructor() {
    this.restoreFromUrl();
    inject(DestroyRef).onDestroy(() => this.generation++);
    // Payments belong to one company: a switch drops filters, page and the open editor (sort and page size stay).
    effect(() => { this.company.current(); untracked(() => {
      this.editor.set(null); this.showMapEditor.set(false);
      if (this.first) { this.first = false; this.search(false); } else this.reset();
    }); });
  }
  hasUnsavedChanges(): boolean { return this.editorComponent?.dirty() ?? false; }
  statusKey(value: string): string { return `fi.otherPayment.state.${value.toLowerCase()}`; }
  tag(value: OtherPaymentStatus): DocumentStatus { return TAG[value] ?? 'Draft'; }
  label(code: string | null, name: string | null, id: number): string { return code ? `${code} · ${name ?? ''}` : `#${id}`; }
  money(value: string): string { return formatDecimalText(value, 'en-US'); }
  mappingKey(row: OtherPaymentSummary): string {
    if (!this.canViewMaps) return 'fi.otherPayment.map.hidden';
    if (this.mapFailed()) return 'fi.otherPayment.map.unavailable';
    if (this.mapLoading()) return 'fi.otherPayment.map.loading';
    const map = this.maps().get(row.expenseCategoryId);
    if (!map?.configured) return 'fi.otherPayment.map.missing';
    if (!map.expenseCategoryActive) return 'fi.otherPayment.map.categoryInactive';
    if (!map.expenseAccount?.isUsable) return 'fi.otherPayment.map.accountInvalid';
    return 'fi.otherPayment.map.ready';
  }
  openCreate(): void { if (this.canCreate) { this.deletedNo.set(null); this.editor.set({ id: null }); } }
  openDetail(id: number): void { this.deletedNo.set(null); this.editor.set({ id }); }
  reset(): void { this.keyword = ''; this.status = ''; this.from = ''; this.to = ''; this.search(); }
  search(resetPage = true): void {
    this.rangeInvalid.set(!!this.from && !!this.to && this.from > this.to);
    if (this.rangeInvalid()) return;
    this.keyword = this.keyword.trim();
    this.applied = { keyword: this.keyword, status: this.status, from: this.from, to: this.to };
    this.filtered.set(Object.values(this.applied).some(value => value !== ''));
    if (resetPage) this.page.set(1);
    this.updateUrl(); this.load();
  }
  onQuery(query: NzTableQueryParams): void {
    const next = nextTableState(query, { page: this.page(), pageSize: this.pageSize(), sort: this.sort() }, SORTS, DEFAULT_SORT);
    if (!next) return;
    this.page.set(next.page); this.pageSize.set(next.pageSize); this.sort.set(next.sort);
    this.updateUrl(); this.load();
  }
  load(): void {
    const current = ++this.generation;
    this.loading.set(true); this.error.set(null); this.traceId.set(null); this.listFailed.set(false);
    this.maps.set(new Map()); this.mapRows.set([]); this.mapFailed.set(false); this.mapLoading.set(this.canViewMaps);
    this.api.list({ ...this.applied, page: this.page(), pageSize: this.pageSize(), sort: this.sort() }).subscribe({
      next: result => { if (current !== this.generation) return;
        // A shared link past the last page shows page 1 instead of an empty table that hides saved payments.
        if (!result.items.length && result.totalCount > 0 && this.page() > 1) { this.page.set(1); this.updateUrl(); this.load(); return; }
        this.rows.set(result.items); this.total.set(result.totalCount); this.loading.set(false); },
      error: e => { if (current !== this.generation) return; this.error.set(this.api.errorKey(e)); this.traceId.set(apiErrorTraceId(e));
        this.listFailed.set(true); this.rows.set([]); this.total.set(0); this.loading.set(false); },
    });
    if (this.canViewMaps) this.api.maps().subscribe({
      next: result => { if (current === this.generation) { this.mapRows.set(result.items); this.maps.set(new Map(result.items.map(row => [row.expenseCategoryId, row]))); this.mapLoading.set(false); } },
      error: () => { if (current === this.generation) { this.mapFailed.set(true); this.mapLoading.set(false); } },
    });
  }

  /** Shared or returned-to links restore the query; anything invalid silently falls back to the default. */
  private restoreFromUrl(): void {
    const params = this.route?.snapshot.queryParamMap;
    if (!params) return;
    const keyword = (params.get('keyword') ?? '').trim();
    this.keyword = keyword.length <= KEYWORD_MAX ? keyword : '';
    const status = params.get('status') ?? '';
    this.status = (this.statuses as readonly string[]).includes(status) ? status : '';
    const from = params.get('from') ?? '', to = params.get('to') ?? '';
    this.from = validIsoDate(from) ? from : '';
    this.to = validIsoDate(to) ? to : '';
    if (this.from && this.to && this.from > this.to) { this.from = ''; this.to = ''; }
    const page = Number(params.get('page'));
    if (Number.isSafeInteger(page) && page > 0 && page <= 100_000) this.page.set(page);
    const pageSize = Number(params.get('pageSize'));
    if (PAGE_SIZE_OPTIONS.some(value => value === pageSize)) this.pageSize.set(pageSize);
    const sort = params.get('sort') ?? '';
    if ((SORTS as readonly string[]).includes(sort.replace(/^-/, ''))) this.sort.set(sort);
  }

  /** Replaces the URL query with the applied filters, page and sort; defaults are omitted. */
  private updateUrl(): void {
    if (!this.route || !this.router) return;
    const a = this.applied;
    void this.router.navigate([], { relativeTo: this.route, replaceUrl: true, queryParams: {
      keyword: a.keyword || null, status: a.status || null, from: a.from || null, to: a.to || null,
      page: this.page() > 1 ? this.page() : null, pageSize: this.pageSize() !== 20 ? this.pageSize() : null,
      sort: this.sort() !== DEFAULT_SORT ? this.sort() : null,
    } });
  }
}
