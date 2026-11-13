import { ChangeDetectionStrategy, Component, DestroyRef, effect, inject, signal, untracked } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router } from '@angular/router';
import { TranslatePipe } from '@ngx-translate/core';
import { NzButtonModule } from 'ng-zorro-antd/button';
import { NzTableModule, NzTableQueryParams } from 'ng-zorro-antd/table';
import { NzTagModule } from 'ng-zorro-antd/tag';
import { CompanyContextService } from '../../core/company/company-context.service';
import { apiErrorTraceId } from '../../core/http/api-error';
import { ApiErrorDisplay } from '../../shared/api-error-display';
import { displayTime } from '../../shared/date';
import { nextTableState, PAGE_SIZE_OPTIONS, sortOrderOf } from '../../shared/list-page/table-query';
import {
  DEFAULT_GL_PERIOD_SORT, GL_PERIOD_SORTS, GL_PERIOD_STATUSES, GlPeriod, GlPeriodService, GlPeriodStatus,
} from './gl-period.service';

const DATE = /^\d{4}-\d{2}-\d{2}$/;

/** True for a real calendar date `yyyy-MM-dd` (rejects 2026-02-30). */
export function validPeriodDate(value: string): boolean {
  if (!DATE.test(value)) return false;
  const parsed = new Date(`${value}T00:00:00Z`);
  return !Number.isNaN(parsed.getTime()) && parsed.toISOString().slice(0, 10) === value;
}

/** Accounting Period View Interface 6.20 (REQ_F4116.10.1, REQ_IO_A6.20.1–4, WU-FI-004c1). Read only. */
@Component({
  selector: 'erp-gl-period-page',
  imports: [FormsModule, TranslatePipe, NzButtonModule, NzTableModule, NzTagModule, ApiErrorDisplay],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <section class="period-page">
      <header><h1>{{ 'fi.glPeriod.title' | translate }}</h1></header>
      <p class="hint">{{ 'fi.glPeriod.intro' | translate }}</p>
      <form class="filters" (ngSubmit)="search()" data-testid="fi-gl-period-filters">
        <label class="field">{{ 'fi.glPeriod.status' | translate }}
          <select name="status" [(ngModel)]="status" data-testid="fi-gl-period-status">
            <option value="">{{ 'fi.glPeriod.allStatuses' | translate }}</option>
            @for (value of statuses; track value) { <option [value]="value">{{ statusKey(value) | translate }}</option> }
          </select></label>
        <label class="field">{{ 'fi.glPeriod.windowFrom' | translate }}
          <input type="date" name="from" autocomplete="off" [(ngModel)]="from" data-testid="fi-gl-period-from" /></label>
        <label class="field">{{ 'fi.glPeriod.windowTo' | translate }}
          <input type="date" name="to" autocomplete="off" [(ngModel)]="to" data-testid="fi-gl-period-to" /></label>
        <div class="filter-actions">
          <button nz-button nzType="primary" type="submit" data-testid="fi-gl-period-search">{{ 'fi.glPeriod.search' | translate }}</button>
          <button nz-button type="button" data-testid="fi-gl-period-reset" (click)="reset()">{{ 'fi.glPeriod.reset' | translate }}</button>
        </div>
      </form>
      @if (validation()) { <p class="field-error" role="alert" data-testid="fi-gl-period-validation">{{ validation()! | translate }}</p> }
      @if (error()) {
        <erp-api-error-display data-testid="fi-gl-period-error" [messageKey]="error()!" [traceId]="traceId()" />
        <button nz-button type="button" class="retry" data-testid="fi-gl-period-retry" (click)="load()">{{ 'fi.glPeriod.retry' | translate }}</button>
      }
      <nz-table #table data-testid="fi-gl-period-table" nzSize="middle" [nzData]="rows()" [nzLoading]="loading()" [nzFrontPagination]="false"
        [nzTotal]="total()" [nzPageIndex]="page()" [nzPageSize]="pageSize()" nzShowSizeChanger [nzPageSizeOptions]="pageSizeOptions"
        [nzNoResult]="(error() ? 'fi.glPeriod.loadFailed' : filtered() ? 'fi.glPeriod.noResults' : 'fi.glPeriod.empty') | translate"
        (nzQueryParams)="onQuery($event)">
        <thead><tr>
          <th nzColumnKey="startDate" [nzSortFn]="true" [nzSortOrder]="sortOrderOf(sort(), 'startDate')">{{ 'fi.glPeriod.startDate' | translate }}</th>
          <th nzColumnKey="endDate" [nzSortFn]="true" [nzSortOrder]="sortOrderOf(sort(), 'endDate')">{{ 'fi.glPeriod.endDate' | translate }}</th>
          <th>{{ 'fi.glPeriod.status' | translate }}</th>
          <th>{{ 'fi.glPeriod.version' | translate }}</th>
          <th>{{ 'fi.glPeriod.lastChanged' | translate }}</th>
        </tr></thead>
        <tbody>@for (row of table.data; track row.id) { <tr [attr.data-testid]="'fi-gl-period-row-' + row.id">
          <td [attr.data-testid]="'fi-gl-period-start-' + row.id">{{ row.startDate }}</td>
          <td [attr.data-testid]="'fi-gl-period-end-' + row.id">{{ row.endDate }}</td>
          <td><nz-tag [nzColor]="row.status === 'Open' ? 'success' : 'default'" [attr.data-testid]="'fi-gl-period-state-' + row.id">{{ statusKey(row.status) | translate }}</nz-tag>
            @if (row.status === 'Closed') { <span class="muted" [attr.data-testid]="'fi-gl-period-closed-hint-' + row.id">{{ 'fi.glPeriod.closedHint' | translate }}</span> }</td>
          <td [attr.data-testid]="'fi-gl-period-version-' + row.id">{{ row.version }}</td>
          <td [attr.data-testid]="'fi-gl-period-changed-' + row.id">{{ changedAt(row) }}</td>
        </tr> }</tbody>
      </nz-table>
    </section>`,
  styles: `.period-page{display:grid;gap:16px;color:var(--erp-color-text);min-width:0}h1{margin:0}.hint,.muted{margin:0;color:var(--erp-color-text-secondary)}.muted{display:block;font-size:12px}.filters{display:flex;gap:12px;flex-wrap:wrap;align-items:end}.field{display:grid;gap:4px}.filters input,.filters select{min-height:32px;padding:4px;background:var(--erp-color-bg-container);color:var(--erp-color-text);border:1px solid var(--erp-color-border);border-radius:4px}.filter-actions{display:flex;gap:8px}.retry{justify-self:start}.field-error{margin:0;color:var(--erp-status-error,#cf1322)}td{font-variant-numeric:tabular-nums}button:focus-visible,input:focus-visible,select:focus-visible{outline:2px solid var(--erp-color-primary);outline-offset:2px}`,
})
export class GlPeriodPage {
  private readonly api = inject(GlPeriodService);
  private readonly company = inject(CompanyContextService);
  private readonly route = inject(ActivatedRoute, { optional: true });
  private readonly router = inject(Router, { optional: true });
  readonly statuses = GL_PERIOD_STATUSES;
  readonly pageSizeOptions = [...PAGE_SIZE_OPTIONS];
  readonly sortOrderOf = sortOrderOf;
  readonly rows = signal<GlPeriod[]>([]);
  readonly total = signal(0);
  readonly page = signal(1);
  readonly pageSize = signal(20);
  readonly sort = signal(DEFAULT_GL_PERIOD_SORT);
  readonly loading = signal(false);
  readonly filtered = signal(false);
  readonly validation = signal<string | null>(null);
  readonly error = signal<string | null>(null);
  readonly traceId = signal<string | null>(null);
  status = ''; from = ''; to = '';
  private applied: { status?: GlPeriodStatus; from?: string; to?: string } = {};
  private listEpoch = 0;
  private first = true;

  constructor() {
    const params = this.route?.snapshot.queryParamMap;
    if (params) {
      const state = params.get('status') ?? '';
      this.status = GL_PERIOD_STATUSES.includes(state as GlPeriodStatus) ? state : '';
      this.from = validPeriodDate(params.get('from') ?? '') ? params.get('from')! : '';
      this.to = validPeriodDate(params.get('to') ?? '') ? params.get('to')! : '';
      const page = Number(params.get('page'));
      if (Number.isSafeInteger(page) && page > 0 && page <= 100_000) this.page.set(page);
      const pageSize = Number(params.get('pageSize'));
      if (PAGE_SIZE_OPTIONS.some(value => value === pageSize)) this.pageSize.set(pageSize);
      const sort = params.get('sort') ?? '';
      if ((GL_PERIOD_SORTS as readonly string[]).includes(sort.replace(/^-/, ''))) this.sort.set(sort);
    }
    inject(DestroyRef).onDestroy(() => this.listEpoch++);
    // Periods belong to one company: a company switch drops filters and re-reads page 1.
    effect(() => { this.company.current(); untracked(() => {
      if (!this.first) { this.page.set(1); this.status = ''; this.from = ''; this.to = ''; }
      this.first = false; this.search(false);
    }); });
  }

  statusKey(status: string): string { return `fi.glPeriod.status${status}`; }
  changedAt(row: GlPeriod): string { return displayTime(row.updatedAt ?? row.createdAt); }

  search(resetPage = true): void {
    if ((this.from && !validPeriodDate(this.from)) || (this.to && !validPeriodDate(this.to)) || (this.from && this.to && this.from > this.to)) {
      this.validation.set('fi.glPeriod.invalidWindow'); return;
    }
    this.validation.set(null);
    this.applied = { status: (this.status || undefined) as GlPeriodStatus | undefined,
      from: this.from || undefined, to: this.to || undefined };
    this.filtered.set(Object.values(this.applied).some(value => value !== undefined));
    if (resetPage) this.page.set(1);
    this.updateUrl(); this.load();
  }

  reset(): void { this.status = ''; this.from = ''; this.to = ''; this.search(); }

  onQuery(params: NzTableQueryParams): void {
    const next = nextTableState(params, { page: this.page(), pageSize: this.pageSize(), sort: this.sort() },
      [...GL_PERIOD_SORTS], DEFAULT_GL_PERIOD_SORT);
    if (!next) return;
    this.page.set(next.page); this.pageSize.set(next.pageSize); this.sort.set(next.sort);
    this.updateUrl(); this.load();
  }

  load(): void {
    const epoch = ++this.listEpoch;
    this.loading.set(true); this.error.set(null); this.traceId.set(null);
    this.api.list({ ...this.applied, page: this.page(), pageSize: this.pageSize(), sort: this.sort() }).subscribe({
      next: result => { if (epoch !== this.listEpoch) return;
        // A deep link past the last page shows page 1 instead of an empty table that hides saved rows.
        if (!result.items.length && result.totalCount > 0 && this.page() > 1) { this.page.set(1); this.updateUrl(); this.load(); return; }
        this.rows.set(result.items); this.total.set(result.totalCount); this.loading.set(false); },
      error: (failure: unknown) => { if (epoch !== this.listEpoch) return;
        this.rows.set([]); this.total.set(0);
        this.error.set(this.api.errorKey(failure)); this.traceId.set(apiErrorTraceId(failure)); this.loading.set(false); },
    });
  }

  private updateUrl(): void {
    if (!this.route || !this.router) return;
    void this.router.navigate([], { relativeTo: this.route, replaceUrl: true, queryParams: {
      status: this.applied.status ?? null, from: this.applied.from ?? null, to: this.applied.to ?? null,
      page: this.page() > 1 ? this.page() : null, pageSize: this.pageSize() !== 20 ? this.pageSize() : null,
      sort: this.sort() !== DEFAULT_GL_PERIOD_SORT ? this.sort() : null,
    } });
  }
}
