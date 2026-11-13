import { ChangeDetectionStrategy, Component, DestroyRef, effect, inject, signal, untracked } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router } from '@angular/router';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import { NzButtonModule } from 'ng-zorro-antd/button';
import { NzModalModule } from 'ng-zorro-antd/modal';
import { NzSpinModule } from 'ng-zorro-antd/spin';
import { NzTableModule, NzTableQueryParams } from 'ng-zorro-antd/table';
import { NzTagModule } from 'ng-zorro-antd/tag';
import { PermissionService } from '../../core/auth/permission.service';
import { CompanyContextService } from '../../core/company/company-context.service';
import { apiErrorTraceId } from '../../core/http/api-error';
import { ApiErrorDisplay } from '../../shared/api-error-display';
import { displayTime, validIsoDate } from '../../shared/date';
import { formatDecimalText, isZeroDecimalText } from '../../shared/decimal-text';
import { nextTableState, PAGE_SIZE_OPTIONS, sortOrderOf } from '../../shared/list-page/table-query';
import {
  DEFAULT_GL_JOURNAL_SORT, GL_JOURNAL_LIMITS, GL_JOURNAL_SORTS, GlJournal, GlJournalDetail, GlJournalQuery, GlJournalService,
} from './gl-journal.service';
import { GlPeriod, GlPeriodService } from './gl-period.service';

const PERIOD_VIEW = 'GL.PERIOD.VIEW';
/** Positive whole id within the safe integer range (server `long`, JavaScript number). */
const POSITIVE_ID = /^[1-9]\d{0,15}$/;

function positiveId(value: string | null | undefined): number | null {
  const text = (value ?? '').trim();
  if (!POSITIVE_ID.test(text)) return null;
  const id = Number(text);
  return Number.isSafeInteger(id) ? id : null;
}

type Filters = Omit<GlJournalQuery, 'page' | 'pageSize' | 'sort'>;

/**
 * Posted journal view (interface 6.22, WU-FI-004c3): read-only list with period, business date, saved source and
 * account filters, and a detail dialog with the saved source and ordered account/amount snapshots. Amounts are the
 * server's exact decimal text with digit grouping; nothing on this page can change a journal (BR-FI-024).
 */
@Component({
  selector: 'erp-gl-journal-page',
  imports: [FormsModule, TranslatePipe, NzButtonModule, NzModalModule, NzSpinModule, NzTableModule, NzTagModule, ApiErrorDisplay],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <section class="journal-page">
      <h1>{{ 'fi.glJournal.title' | translate }}</h1>
      <p class="hint">{{ 'fi.glJournal.intro' | translate }}</p>
      <form class="filters" (ngSubmit)="search()" data-testid="fi-gl-journal-filters">
        @if (canSeePeriods) {
          <label class="field">{{ 'fi.glJournal.period' | translate }}
            <select name="period" [(ngModel)]="periodId" data-testid="fi-gl-journal-period">
              <option value="">{{ 'fi.glJournal.allPeriods' | translate }}</option>
              @for (period of periods(); track period.id) { <option [value]="'' + period.id">{{ periodLabel(period) }}</option> }
            </select></label>
        }
        <label class="field">{{ 'fi.glJournal.dateFrom' | translate }}
          <input type="date" name="from" autocomplete="off" [(ngModel)]="from" data-testid="fi-gl-journal-from" /></label>
        <label class="field">{{ 'fi.glJournal.dateTo' | translate }}
          <input type="date" name="to" autocomplete="off" [(ngModel)]="to" data-testid="fi-gl-journal-to" /></label>
        <label class="field">{{ 'fi.glJournal.sourceModule' | translate }}
          <input name="sourceModule" autocomplete="off" spellcheck="false" [maxlength]="limits.sourceModule" [(ngModel)]="sourceModule"
            [placeholder]="'fi.glJournal.sourceModuleHint' | translate" data-testid="fi-gl-journal-source-module" /></label>
        <label class="field">{{ 'fi.glJournal.sourceType' | translate }}
          <input name="sourceType" autocomplete="off" spellcheck="false" [maxlength]="limits.sourceType" [(ngModel)]="sourceType"
            data-testid="fi-gl-journal-source-type" /></label>
        <label class="field">{{ 'fi.glJournal.sourceId' | translate }}
          <input name="sourceId" inputmode="numeric" autocomplete="off" maxlength="16" [(ngModel)]="sourceId"
            data-testid="fi-gl-journal-source-id" /></label>
        <label class="field">{{ 'fi.glJournal.accountCode' | translate }}
          <input name="accountCode" autocomplete="off" spellcheck="false" [maxlength]="limits.accountCode" [(ngModel)]="accountCode"
            data-testid="fi-gl-journal-account" /></label>
        <div class="filter-actions">
          <button nz-button nzType="primary" type="submit" data-testid="fi-gl-journal-search">{{ 'fi.glJournal.search' | translate }}</button>
          <button nz-button type="button" data-testid="fi-gl-journal-reset" (click)="reset()">{{ 'fi.glJournal.reset' | translate }}</button>
        </div>
      </form>
      @if (periodsFailed()) { <p class="muted" data-testid="fi-gl-journal-periods-failed">{{ 'fi.glJournal.periodsFailed' | translate }}</p> }
      @if (validation()) { <p class="field-error" role="alert" data-testid="fi-gl-journal-validation">{{ validation()! | translate }}</p> }
      @if (error()) {
        <erp-api-error-display data-testid="fi-gl-journal-error" [messageKey]="error()!" [traceId]="traceId()" />
        <button nz-button type="button" class="retry" data-testid="fi-gl-journal-retry" (click)="load()">{{ 'fi.glJournal.retry' | translate }}</button>
      }
      <nz-table #table data-testid="fi-gl-journal-table" nzSize="middle" [nzData]="rows()" [nzLoading]="loading()" [nzFrontPagination]="false"
        [nzTotal]="total()" [nzPageIndex]="page()" [nzPageSize]="pageSize()" nzShowSizeChanger [nzPageSizeOptions]="pageSizeOptions"
        [nzNoResult]="(error() ? 'fi.glJournal.loadFailed' : filtered() ? 'fi.glJournal.noResults' : 'fi.glJournal.empty') | translate"
        (nzQueryParams)="onQuery($event)">
        <thead><tr>
          <th class="nowrap" nzColumnKey="id" [nzSortFn]="true" [nzSortOrder]="sortOrderOf(sort(), 'id')">{{ 'fi.glJournal.journalNo' | translate }}</th>
          <th class="nowrap" nzColumnKey="businessDate" [nzSortFn]="true" [nzSortOrder]="sortOrderOf(sort(), 'businessDate')">{{ 'fi.glJournal.businessDate' | translate }}</th>
          <th>{{ 'fi.glJournal.period' | translate }}</th>
          <th>{{ 'fi.glJournal.source' | translate }}</th>
          <th class="number">{{ 'fi.glJournal.totalDebit' | translate }}</th>
          <th class="number">{{ 'fi.glJournal.totalCredit' | translate }}</th>
          <th>{{ 'fi.glJournal.postedAt' | translate }}</th>
        </tr></thead>
        <tbody>@for (row of table.data; track row.id) { <tr [attr.data-testid]="'fi-gl-journal-row-' + row.id">
          <td><button nz-button nzType="link" type="button" class="open" [attr.data-testid]="'fi-gl-journal-open-' + row.id"
            [attr.aria-label]="'fi.glJournal.openLabel' | translate: { id: row.id, date: row.businessDate }" (click)="openDetail(row.id)">#{{ row.id }}</button></td>
          <td class="nowrap" [attr.data-testid]="'fi-gl-journal-date-' + row.id">{{ row.businessDate }}</td>
          <td class="nowrap" [attr.data-testid]="'fi-gl-journal-period-' + row.id">{{ periodOf(row.periodId) }}</td>
          <td [attr.data-testid]="'fi-gl-journal-source-' + row.id">{{ sourceLabel(row) }}</td>
          <td class="number" [attr.data-testid]="'fi-gl-journal-debit-' + row.id">{{ amount(row.totalDebit) }} {{ row.baseCurrency }}</td>
          <td class="number" [attr.data-testid]="'fi-gl-journal-credit-' + row.id">{{ amount(row.totalCredit) }} {{ row.baseCurrency }}</td>
          <td class="nowrap" [attr.data-testid]="'fi-gl-journal-posted-' + row.id">{{ time(row.createdAt) }}</td>
        </tr> }</tbody>
      </nz-table>
    </section>
    <nz-modal [nzVisible]="detailId() !== null" [nzTitle]="'fi.glJournal.detailTitle' | translate: { id: detailId() }" nzWidth="960px"
      [nzFooter]="footer" (nzOnCancel)="closeDetail()">
      <ng-container *nzModalContent>
        <div class="detail" data-testid="fi-gl-journal-detail">
          @if (detailLoading()) { <nz-spin nzSimple data-testid="fi-gl-journal-detail-loading" /> }
          @if (detailError()) {
            <erp-api-error-display data-testid="fi-gl-journal-detail-error" [messageKey]="detailError()!" [traceId]="detailTraceId()" />
            @if (detailRetryable()) { <button nz-button type="button" class="retry" data-testid="fi-gl-journal-detail-retry"
              (click)="loadDetail()">{{ 'fi.glJournal.retry' | translate }}</button> }
          }
          @if (detail(); as journal) {
            <p class="muted">{{ 'fi.glJournal.immutableHint' | translate }}</p>
            <dl class="header-grid">
              <dt>{{ 'fi.glJournal.status' | translate }}</dt>
              <dd><nz-tag nzColor="success" data-testid="fi-gl-journal-detail-status">{{ 'fi.glJournal.statusPosted' | translate }}</nz-tag></dd>
              <dt>{{ 'fi.glJournal.businessDate' | translate }}</dt><dd data-testid="fi-gl-journal-detail-date">{{ journal.header.businessDate }}</dd>
              <dt>{{ 'fi.glJournal.period' | translate }}</dt><dd data-testid="fi-gl-journal-detail-period">{{ periodOf(journal.header.periodId) }}</dd>
              <dt>{{ 'fi.glJournal.currency' | translate }}</dt><dd data-testid="fi-gl-journal-detail-currency">{{ journal.header.baseCurrency }}</dd>
              <dt>{{ 'fi.glJournal.sourceModule' | translate }}</dt><dd data-testid="fi-gl-journal-detail-module">{{ journal.header.sourceModule }}</dd>
              <dt>{{ 'fi.glJournal.sourceType' | translate }}</dt><dd data-testid="fi-gl-journal-detail-type">{{ journal.header.sourceType }}</dd>
              <dt>{{ 'fi.glJournal.sourceId' | translate }}</dt><dd data-testid="fi-gl-journal-detail-source-id">{{ journal.header.sourceId }}</dd>
              <dt>{{ 'fi.glJournal.sourceVersion' | translate }}</dt><dd data-testid="fi-gl-journal-detail-source-version">{{ journal.header.sourceVersion }}</dd>
              <dt>{{ 'fi.glJournal.postedAt' | translate }}</dt><dd data-testid="fi-gl-journal-detail-posted">{{ time(journal.header.createdAt) }}</dd>
              <dt>{{ 'fi.glJournal.postedBy' | translate }}</dt><dd data-testid="fi-gl-journal-detail-posted-by">{{ journal.header.createdBy }}</dd>
            </dl>
            <nz-table #lines data-testid="fi-gl-journal-lines" nzSize="small" [nzData]="journal.lines" [nzShowPagination]="false"
              [nzFrontPagination]="false" [nzNoResult]="'fi.glJournal.noLines' | translate">
              <thead><tr>
                <th>{{ 'fi.glJournal.sequence' | translate }}</th>
                <th>{{ 'fi.glJournal.accountCode' | translate }}</th>
                <th>{{ 'fi.glJournal.accountName' | translate }}</th>
                <th>{{ 'fi.glJournal.snapshotVersion' | translate }}</th>
                <th class="number">{{ 'fi.glJournal.debit' | translate }}</th>
                <th class="number">{{ 'fi.glJournal.credit' | translate }}</th>
              </tr></thead>
              <tbody>
                @for (line of lines.data; track line.sequence) { <tr [attr.data-testid]="'fi-gl-journal-line-' + line.sequence">
                  <td>{{ line.sequence }}</td>
                  <td [attr.data-testid]="'fi-gl-journal-line-code-' + line.sequence">{{ line.accountCode }}</td>
                  <td [attr.data-testid]="'fi-gl-journal-line-name-' + line.sequence">{{ line.accountName }}</td>
                  <td [attr.data-testid]="'fi-gl-journal-line-version-' + line.sequence">{{ 'fi.glJournal.versions' | translate: { account: line.accountVersion, mapping: line.mappingVersion } }}</td>
                  <td class="number" [attr.data-testid]="'fi-gl-journal-line-debit-' + line.sequence">{{ side(line.debit) }}</td>
                  <td class="number" [attr.data-testid]="'fi-gl-journal-line-credit-' + line.sequence">{{ side(line.credit) }}</td>
                </tr> }
                <tr class="totals" data-testid="fi-gl-journal-totals">
                  <td colspan="4">{{ 'fi.glJournal.totals' | translate: { currency: journal.header.baseCurrency } }}</td>
                  <td class="number" data-testid="fi-gl-journal-total-debit">{{ amount(journal.header.totalDebit) }}</td>
                  <td class="number" data-testid="fi-gl-journal-total-credit">{{ amount(journal.header.totalCredit) }}</td>
                </tr>
              </tbody>
            </nz-table>
          }
        </div>
      </ng-container>
    </nz-modal>
    <ng-template #footer><button nz-button type="button" data-testid="fi-gl-journal-detail-close" (click)="closeDetail()">{{ 'fi.glJournal.close' | translate }}</button></ng-template>`,
  styles: `.journal-page{display:grid;gap:16px;color:var(--erp-color-text);min-width:0}h1{margin:0}.hint,.muted{margin:0;color:var(--erp-color-text-secondary)}.filters{display:flex;gap:12px;flex-wrap:wrap;align-items:end}.field{display:grid;gap:4px}.filters input,.filters select{min-height:32px;padding:4px;background:var(--erp-color-bg-container);color:var(--erp-color-text);border:1px solid var(--erp-color-border);border-radius:4px}.filters input:not([type=date]){width:140px}.filters select{max-width:260px}.filter-actions{display:flex;gap:8px}.retry{justify-self:start}.field-error{margin:0;color:var(--erp-status-error,#cf1322)}.number{text-align:right;white-space:nowrap}.nowrap{white-space:nowrap}td{font-variant-numeric:tabular-nums}.open{padding:0}.detail{display:grid;gap:12px}.header-grid{display:grid;grid-template-columns:max-content 1fr max-content 1fr;gap:6px 16px;margin:0}.header-grid dt{color:var(--erp-color-text-secondary)}.header-grid dd{margin:0;overflow-wrap:anywhere}.totals td{font-weight:600}@media (max-width:720px){.header-grid{grid-template-columns:max-content 1fr}}button:focus-visible,input:focus-visible,select:focus-visible{outline:2px solid var(--erp-color-primary);outline-offset:2px}`,
})
export class GlJournalPage {
  private readonly api = inject(GlJournalService);
  private readonly periodApi = inject(GlPeriodService);
  private readonly permissions = inject(PermissionService);
  private readonly translate = inject(TranslateService);
  private readonly company = inject(CompanyContextService);
  private readonly route = inject(ActivatedRoute, { optional: true });
  private readonly router = inject(Router, { optional: true });
  readonly limits = GL_JOURNAL_LIMITS;
  readonly pageSizeOptions = [...PAGE_SIZE_OPTIONS];
  readonly sortOrderOf = sortOrderOf;
  /** The period filter and labels need the period list, which has its own permission. */
  readonly canSeePeriods = this.permissions.has(PERIOD_VIEW);
  readonly rows = signal<GlJournal[]>([]);
  readonly total = signal(0);
  readonly page = signal(1);
  readonly pageSize = signal(20);
  readonly sort = signal(DEFAULT_GL_JOURNAL_SORT);
  readonly loading = signal(false);
  readonly filtered = signal(false);
  readonly validation = signal<string | null>(null);
  readonly error = signal<string | null>(null);
  readonly traceId = signal<string | null>(null);
  readonly periods = signal<GlPeriod[]>([]);
  readonly periodsFailed = signal(false);
  readonly detailId = signal<number | null>(null);
  readonly detail = signal<GlJournalDetail | null>(null);
  readonly detailLoading = signal(false);
  readonly detailError = signal<string | null>(null);
  readonly detailTraceId = signal<string | null>(null);
  /** 404 means the journal is not in this company; retrying cannot help, so only other failures offer retry. */
  readonly detailRetryable = signal(false);
  periodId = ''; from = ''; to = ''; sourceModule = ''; sourceType = ''; sourceId = ''; accountCode = '';
  private applied: Filters = {};
  private listEpoch = 0;
  private detailEpoch = 0;
  private periodEpoch = 0;
  private first = true;

  constructor() {
    const params = this.route?.snapshot.queryParamMap;
    if (params) {
      const period = positiveId(params.get('periodId'));
      this.periodId = period && this.canSeePeriods ? String(period) : '';
      this.from = validIsoDate(params.get('from') ?? '') ? params.get('from')! : '';
      this.to = validIsoDate(params.get('to') ?? '') ? params.get('to')! : '';
      this.sourceModule = (params.get('sourceModule') ?? '').trim().slice(0, GL_JOURNAL_LIMITS.sourceModule);
      this.sourceType = (params.get('sourceType') ?? '').trim().slice(0, GL_JOURNAL_LIMITS.sourceType);
      this.sourceId = positiveId(params.get('sourceId')) ? params.get('sourceId')!.trim() : '';
      this.accountCode = (params.get('accountCode') ?? '').trim().slice(0, GL_JOURNAL_LIMITS.accountCode);
      const page = Number(params.get('page'));
      if (Number.isSafeInteger(page) && page > 0 && page <= 100_000) this.page.set(page);
      const pageSize = Number(params.get('pageSize'));
      if (PAGE_SIZE_OPTIONS.some(value => value === pageSize)) this.pageSize.set(pageSize);
      const sort = params.get('sort') ?? '';
      if ((GL_JOURNAL_SORTS as readonly string[]).includes(sort.replace(/^-/, ''))) this.sort.set(sort);
      const journal = positiveId(params.get('journal'));
      if (journal) this.detailId.set(journal);
    }
    inject(DestroyRef).onDestroy(() => { this.listEpoch++; this.detailEpoch++; this.periodEpoch++; });
    // Journals belong to one company: a switch drops filters and the open journal, then re-reads page 1.
    effect(() => { this.company.current(); untracked(() => {
      if (!this.first) {
        this.page.set(1); this.periodId = ''; this.from = ''; this.to = '';
        this.sourceModule = ''; this.sourceType = ''; this.sourceId = ''; this.accountCode = '';
        this.resetDetail(); this.detailId.set(null);
      }
      this.first = false;
      this.loadPeriods(); this.search(false);
      if (this.detailId() !== null) this.loadDetail();
    }); });
  }

  amount(text: string): string { return formatDecimalText(text, this.translate.getCurrentLang() || undefined); }
  /** A line has exactly one nonzero side; the zero side stays blank as on a printed journal. */
  side(text: string): string { return isZeroDecimalText(text) ? '' : this.amount(text); }
  time(value: string): string { return displayTime(value); }
  sourceLabel(row: GlJournal): string { return `${row.sourceModule} / ${row.sourceType} #${row.sourceId} v${row.sourceVersion}`; }
  periodLabel(period: GlPeriod): string { return `${period.startDate} – ${period.endDate}`; }
  periodOf(id: number): string {
    const period = this.periods().find(item => item.id === id);
    return period ? this.periodLabel(period) : `#${id}`;
  }

  search(resetPage = true): void {
    const from = this.from, to = this.to;
    if ((from && !validIsoDate(from)) || (to && !validIsoDate(to)) || (from && to && from > to)) {
      this.validation.set('fi.glJournal.invalidRange'); return;
    }
    const sourceId = this.sourceId.trim();
    if (sourceId && !positiveId(sourceId)) { this.validation.set('fi.glJournal.invalidSourceId'); return; }
    this.validation.set(null);
    this.applied = { periodId: positiveId(this.periodId) ?? undefined, from: from || undefined, to: to || undefined,
      sourceModule: this.sourceModule.trim() || undefined, sourceType: this.sourceType.trim() || undefined,
      sourceId: positiveId(sourceId) ?? undefined, accountCode: this.accountCode.trim() || undefined };
    this.filtered.set(Object.values(this.applied).some(value => value !== undefined));
    if (resetPage) this.page.set(1);
    this.updateUrl(); this.load();
  }

  reset(): void {
    this.periodId = ''; this.from = ''; this.to = ''; this.sourceModule = ''; this.sourceType = ''; this.sourceId = ''; this.accountCode = '';
    this.search();
  }

  onQuery(params: NzTableQueryParams): void {
    const next = nextTableState(params, { page: this.page(), pageSize: this.pageSize(), sort: this.sort() },
      [...GL_JOURNAL_SORTS], DEFAULT_GL_JOURNAL_SORT);
    if (!next) return;
    this.page.set(next.page); this.pageSize.set(next.pageSize); this.sort.set(next.sort);
    this.updateUrl(); this.load();
  }

  load(): void {
    const epoch = ++this.listEpoch;
    this.loading.set(true); this.error.set(null); this.traceId.set(null);
    this.api.list({ ...this.applied, page: this.page(), pageSize: this.pageSize(), sort: this.sort() }).subscribe({
      next: result => { if (epoch !== this.listEpoch) return;
        // A deep link past the last page shows page 1 instead of an empty table that hides saved journals.
        if (!result.items.length && result.totalCount > 0 && this.page() > 1) { this.page.set(1); this.updateUrl(); this.load(); return; }
        this.rows.set(result.items); this.total.set(result.totalCount); this.loading.set(false); },
      error: (failure: unknown) => { if (epoch !== this.listEpoch) return;
        this.rows.set([]); this.total.set(0);
        this.error.set(this.api.errorKey(failure)); this.traceId.set(apiErrorTraceId(failure)); this.loading.set(false); },
    });
  }

  openDetail(id: number): void {
    this.detailId.set(id); this.updateUrl(); this.loadDetail();
  }

  loadDetail(): void {
    const id = this.detailId();
    if (id === null) return;
    const epoch = ++this.detailEpoch;
    this.detail.set(null); this.detailLoading.set(true); this.detailError.set(null); this.detailTraceId.set(null);
    this.api.get(id).subscribe({
      next: journal => { if (epoch !== this.detailEpoch) return; this.detail.set(journal); this.detailLoading.set(false); },
      error: (failure: unknown) => { if (epoch !== this.detailEpoch) return;
        const key = this.api.errorKey(failure);
        this.detailError.set(key); this.detailTraceId.set(apiErrorTraceId(failure));
        this.detailRetryable.set(key !== 'errors.FI.GL_JOURNAL.NOT_FOUND' && key !== 'errors.COMMON.PERMISSION.DENIED');
        this.detailLoading.set(false); },
    });
  }

  closeDetail(): void {
    this.resetDetail(); this.detailId.set(null); this.updateUrl();
  }

  private resetDetail(): void {
    this.detailEpoch++; this.detail.set(null); this.detailLoading.set(false);
    this.detailError.set(null); this.detailTraceId.set(null); this.detailRetryable.set(false);
  }

  /** Period labels and the period filter; a failure keeps the list usable with `#id` labels. */
  private loadPeriods(): void {
    const epoch = ++this.periodEpoch;
    this.periods.set([]); this.periodsFailed.set(false);
    if (!this.canSeePeriods) return;
    this.periodApi.list({ page: 1, pageSize: 200, sort: '-startDate' }).subscribe({
      next: result => { if (epoch === this.periodEpoch) this.periods.set(result.items); },
      error: () => { if (epoch === this.periodEpoch) this.periodsFailed.set(true); },
    });
  }

  private updateUrl(): void {
    if (!this.route || !this.router) return;
    const a = this.applied;
    void this.router.navigate([], { relativeTo: this.route, replaceUrl: true, queryParams: {
      periodId: a.periodId ?? null, from: a.from ?? null, to: a.to ?? null, sourceModule: a.sourceModule ?? null,
      sourceType: a.sourceType ?? null, sourceId: a.sourceId ?? null, accountCode: a.accountCode ?? null,
      page: this.page() > 1 ? this.page() : null, pageSize: this.pageSize() !== 20 ? this.pageSize() : null,
      sort: this.sort() !== DEFAULT_GL_JOURNAL_SORT ? this.sort() : null, journal: this.detailId(),
    } });
  }
}
