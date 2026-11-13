import { ChangeDetectionStrategy, Component, DestroyRef, effect, HostListener, inject, signal, untracked } from '@angular/core';
import { FormControl, FormGroup, FormsModule, ReactiveFormsModule, Validators } from '@angular/forms';
import { ActivatedRoute, Router } from '@angular/router';
import { TranslatePipe } from '@ngx-translate/core';
import { NzAlertModule } from 'ng-zorro-antd/alert';
import { NzButtonModule } from 'ng-zorro-antd/button';
import { NzInputModule } from 'ng-zorro-antd/input';
import { NzModalModule } from 'ng-zorro-antd/modal';
import { NzTableModule, NzTableQueryParams } from 'ng-zorro-antd/table';
import { NzTagModule } from 'ng-zorro-antd/tag';
import { Observable } from 'rxjs';
import { HasPermissionDirective } from '../../core/auth/has-permission.directive';
import { CompanyContextService } from '../../core/company/company-context.service';
import { apiErrorTraceId } from '../../core/http/api-error';
import { ApiErrorDisplay } from '../../shared/api-error-display';
import { displayTime, validIsoDate } from '../../shared/date';
import { ErpDialogService } from '../../shared/dialogs/dialog.service';
import { HasUnsavedChanges } from '../../shared/document-page/unsaved-changes.guard';
import { isMissingRowError, isStaleRowError } from '../../shared/list-page/stale-row';
import { nextTableState, PAGE_SIZE_OPTIONS, sortOrderOf } from '../../shared/list-page/table-query';
import {
  DEFAULT_GL_PERIOD_SORT, GL_PERIOD_SORTS, GL_PERIOD_STATUSES, GlPeriod, GlPeriodDates, GlPeriodService, GlPeriodStatus,
  GlPeriodWriteResult,
} from './gl-period.service';

/** True for a real calendar date `yyyy-MM-dd` (rejects 2026-02-30). */
export function validPeriodDate(value: string): boolean { return validIsoDate(value); }

type Notice = { type: 'success' | 'warning'; key: string; params: Record<string, string> };
type RowAction = 'close' | 'reopen' | 'delete';

/**
 * Accounting periods: view (interface 6.20, WU-FI-004c1) and create / date edit / close / reopen / delete
 * (interface 6.21, WU-FI-004c2). Each write has its own permission; `changed=false` is a warning, never a success.
 */
@Component({
  selector: 'erp-gl-period-page',
  imports: [FormsModule, ReactiveFormsModule, TranslatePipe, NzAlertModule, NzButtonModule, NzInputModule, NzModalModule,
    NzTableModule, NzTagModule, HasPermissionDirective, ApiErrorDisplay],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <section class="period-page">
      <header class="period-head"><h1>{{ 'fi.glPeriod.title' | translate }}</h1>
        <button nz-button nzType="primary" type="button" data-testid="fi-gl-period-create" *erpHasPermission="'GL.PERIOD.CREATE'"
          (click)="openCreate()">{{ 'fi.glPeriod.create' | translate }}</button></header>
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
      <div class="live" role="status" aria-live="polite">@if (notice(); as message) { <nz-alert [nzType]="message.type" nzShowIcon nzCloseable
        data-testid="fi-gl-period-notice" [attr.data-type]="message.type" [nzMessage]="message.key | translate: message.params" (nzOnClose)="notice.set(null)" /> }</div>
      @if (actionError()) { <div class="action-error">@if (actionTarget(); as target) { <p class="action-target" data-testid="fi-gl-period-action-target">{{ 'fi.glPeriod.actionTarget' | translate: target }}</p> }
        <erp-api-error-display data-testid="fi-gl-period-action-error" [messageKey]="actionError()!" [traceId]="actionTraceId()" /></div> }
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
          <th *erpHasPermission="writePermissions">{{ 'fi.glPeriod.actions' | translate }}</th>
        </tr></thead>
        <tbody>@for (row of table.data; track row.id) { <tr [attr.data-testid]="'fi-gl-period-row-' + row.id">
          <td [attr.data-testid]="'fi-gl-period-start-' + row.id">{{ row.startDate }}</td>
          <td [attr.data-testid]="'fi-gl-period-end-' + row.id">{{ row.endDate }}</td>
          <td><nz-tag [nzColor]="row.status === 'Open' ? 'success' : 'default'" [attr.data-testid]="'fi-gl-period-state-' + row.id">{{ statusKey(row.status) | translate }}</nz-tag>
            @if (row.status === 'Closed') { <span class="muted" [attr.data-testid]="'fi-gl-period-closed-hint-' + row.id">{{ 'fi.glPeriod.closedHint' | translate }}</span> }</td>
          <td [attr.data-testid]="'fi-gl-period-version-' + row.id">{{ row.version }}</td>
          <td [attr.data-testid]="'fi-gl-period-changed-' + row.id">{{ changedAt(row) }}</td>
          <td class="actions" *erpHasPermission="writePermissions">
            <button nz-button nzType="link" type="button" *erpHasPermission="'GL.PERIOD.EDIT'" [disabled]="busy()"
              [attr.data-testid]="'fi-gl-period-edit-' + row.id" (click)="openEdit(row)">{{ 'fi.glPeriod.edit' | translate }}</button>
            @if (row.status === 'Open') {
              <button nz-button nzType="link" type="button" *erpHasPermission="'GL.PERIOD.CLOSE'" [disabled]="busy()"
                [attr.data-testid]="'fi-gl-period-close-' + row.id" (click)="confirmClose(row)">{{ 'fi.glPeriod.close' | translate }}</button>
            } @else {
              <button nz-button nzType="link" type="button" *erpHasPermission="'GL.PERIOD.REOPEN'" [disabled]="busy()"
                [attr.data-testid]="'fi-gl-period-reopen-' + row.id" (click)="confirmReopen(row)">{{ 'fi.glPeriod.reopen' | translate }}</button>
            }
            <button nz-button nzType="link" nzDanger type="button" *erpHasPermission="'GL.PERIOD.DELETE'" [disabled]="busy()"
              [attr.data-testid]="'fi-gl-period-delete-' + row.id" (click)="confirmDelete(row)">{{ 'fi.glPeriod.delete' | translate }}</button>
          </td>
        </tr> }</tbody>
      </nz-table>
    </section>
    <nz-modal [nzVisible]="dialogOpen()" [nzTitle]="(editing() ? 'fi.glPeriod.editTitle' : 'fi.glPeriod.createTitle') | translate"
      [nzOkText]="'fi.glPeriod.save' | translate" [nzCancelText]="'fi.glPeriod.cancel' | translate" [nzOkLoading]="saving()"
      (nzOnOk)="save()" (nzOnCancel)="requestCloseDialog()">
      <ng-container *nzModalContent>
        @if (saveError()) { <erp-api-error-display data-testid="fi-gl-period-save-error" [messageKey]="saveError()!" [traceId]="saveTraceId()" /> }
        <form class="period-form" [formGroup]="form" (ngSubmit)="save()" data-testid="fi-gl-period-form" novalidate>
          @if (editing(); as row) { <p class="muted" data-testid="fi-gl-period-form-state">{{ 'fi.glPeriod.editStateHint' | translate: { status: (statusKey(row.status) | translate), version: row.version } }}</p> }
          @else { <p class="muted" data-testid="fi-gl-period-form-state">{{ 'fi.glPeriod.createHint' | translate }}</p> }
          <label for="fi-gl-period-form-start">{{ 'fi.glPeriod.startDate' | translate }} <span class="required" aria-hidden="true">*</span></label>
          <input nz-input id="fi-gl-period-form-start" type="date" name="startDate" autocomplete="off" formControlName="startDate" required
            data-testid="fi-gl-period-form-start" [attr.aria-invalid]="invalid('startDate')" />
          @if (invalid('startDate')) { <p class="field-error" role="alert" data-testid="fi-gl-period-start-error">{{ 'fi.glPeriod.dateRequired' | translate }}</p> }
          <label for="fi-gl-period-form-end">{{ 'fi.glPeriod.endDate' | translate }} <span class="required" aria-hidden="true">*</span></label>
          <input nz-input id="fi-gl-period-form-end" type="date" name="endDate" autocomplete="off" formControlName="endDate" required
            data-testid="fi-gl-period-form-end" [attr.aria-invalid]="invalid('endDate')" />
          @if (invalid('endDate')) { <p class="field-error" role="alert" data-testid="fi-gl-period-end-error">{{ 'fi.glPeriod.dateOrder' | translate }}</p> }
          <p class="muted">{{ 'fi.glPeriod.inclusiveHint' | translate }}</p>
        </form>
      </ng-container>
    </nz-modal>`,
  styles: `.live{display:contents}.period-page{display:grid;gap:16px;color:var(--erp-color-text);min-width:0}h1{margin:0}.period-head{display:flex;justify-content:space-between;gap:12px;flex-wrap:wrap;align-items:center}.hint,.muted{margin:0;color:var(--erp-color-text-secondary)}.muted{display:block;font-size:12px}.filters{display:flex;gap:12px;flex-wrap:wrap;align-items:end}.field{display:grid;gap:4px}.filters input,.filters select{min-height:32px;padding:4px;background:var(--erp-color-bg-container);color:var(--erp-color-text);border:1px solid var(--erp-color-border);border-radius:4px}.filter-actions{display:flex;gap:8px}.retry{justify-self:start}.action-error{display:grid;gap:4px}.action-target{margin:0;font-weight:600}.actions{white-space:nowrap}.period-form{display:grid;gap:8px}.period-form .muted{font-size:14px}.required,.field-error{color:var(--erp-status-error,#cf1322)}.field-error{margin:0}td{font-variant-numeric:tabular-nums}button:focus-visible,input:focus-visible,select:focus-visible{outline:2px solid var(--erp-color-primary);outline-offset:2px}`,
})
export class GlPeriodPage implements HasUnsavedChanges {
  private readonly api = inject(GlPeriodService);
  private readonly dialogs = inject(ErpDialogService);
  private readonly company = inject(CompanyContextService);
  private readonly route = inject(ActivatedRoute, { optional: true });
  private readonly router = inject(Router, { optional: true });
  readonly statuses = GL_PERIOD_STATUSES;
  readonly writePermissions = ['GL.PERIOD.EDIT', 'GL.PERIOD.CLOSE', 'GL.PERIOD.REOPEN', 'GL.PERIOD.DELETE'];
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
  readonly notice = signal<Notice | null>(null);
  readonly actionError = signal<string | null>(null);
  readonly actionTraceId = signal<string | null>(null);
  /** Period the failed action was about (saved dates held on screen), so the reason names its row. */
  readonly actionTarget = signal<{ start: string; end: string } | null>(null);
  readonly dialogOpen = signal(false);
  readonly editing = signal<GlPeriod | null>(null);
  readonly saving = signal(false);
  readonly saveError = signal<string | null>(null);
  readonly saveTraceId = signal<string | null>(null);
  /** A row action (close/reopen/delete) is in flight; row buttons are disabled meanwhile. */
  readonly busy = signal(false);
  readonly form = new FormGroup({
    startDate: new FormControl('', { nonNullable: true, validators: [Validators.required] }),
    endDate: new FormControl('', { nonNullable: true, validators: [Validators.required] }),
  });
  status = ''; from = ''; to = '';
  private applied: { status?: GlPeriodStatus; from?: string; to?: string } = {};
  private listEpoch = 0;
  private writeEpoch = 0;
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
    inject(DestroyRef).onDestroy(() => { this.listEpoch++; this.writeEpoch++; });
    // Periods belong to one company: a company switch drops filters, any open dialog and feedback, and re-reads page 1.
    effect(() => { this.company.current(); untracked(() => {
      this.writeEpoch++; this.closeDialog(); this.busy.set(false); this.notice.set(null); this.actionError.set(null);
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

  openCreate(): void {
    this.editing.set(null);
    this.form.reset({ startDate: '', endDate: '' });
    this.openDialog();
  }

  openEdit(row: GlPeriod): void {
    this.editing.set(row);
    this.form.reset({ startDate: row.startDate, endDate: row.endDate });
    this.openDialog();
  }

  closeDialog(): void {
    this.writeEpoch++; this.dialogOpen.set(false); this.saving.set(false);
    this.saveError.set(null); this.saveTraceId.set(null);
  }

  hasUnsavedChanges(): boolean { return this.dialogOpen() && this.form.dirty; }

  requestCloseDialog(): void {
    if (this.saving()) return;
    if (!this.hasUnsavedChanges()) { this.closeDialog(); return; }
    this.dialogs.confirm({ titleKey: 'common.document.leaveTitle', contentKey: 'common.document.leaveContent',
      okKey: 'common.document.leave', cancelKey: 'common.document.stay', danger: true }).subscribe(leave => {
      if (leave) this.closeDialog();
    });
  }

  @HostListener('window:beforeunload', ['$event'])
  beforeUnload(event: BeforeUnloadEvent): void {
    if (this.hasUnsavedChanges()) { event.preventDefault(); event.returnValue = ''; }
  }

  invalid(field: keyof GlPeriodDates): boolean {
    const control = this.form.controls[field];
    if (!control.touched) return false;
    if (field === 'startDate') return !validPeriodDate(control.value);
    return !validPeriodDate(control.value) || this.reversed();
  }

  save(): void {
    if (this.saving()) return;
    this.form.markAllAsTouched();
    if (this.invalid('startDate') || this.invalid('endDate')) { this.focusFirstError(); return; }
    const dates = this.form.getRawValue();
    const row = this.editing();
    const epoch = ++this.writeEpoch;
    this.saving.set(true); this.saveError.set(null); this.saveTraceId.set(null);
    (row ? this.api.update(row.id, row.version, dates) : this.api.create(dates)).subscribe({
      next: result => { if (epoch !== this.writeEpoch) return;
        this.closeDialog();
        this.announce(row ? 'updated' : 'created', result); this.load(); },
      error: (failure: unknown) => { if (epoch !== this.writeEpoch) return;
        this.saving.set(false);
        if (row && isStaleRowError(failure)) {
          // The held version or row can never succeed again: close, explain above the list and show fresh saved data.
          this.closeDialog(); this.fail(failure, row); this.load(); return;
        }
        // Overlap, Posted-date exclusion, validation and server errors keep the entered dates for correction.
        this.saveError.set(this.api.errorKey(failure)); this.saveTraceId.set(apiErrorTraceId(failure)); },
    });
  }

  confirmClose(row: GlPeriod): void {
    this.confirmAction(row, 'close', () => this.api.close(row.id, row.version));
  }

  confirmReopen(row: GlPeriod): void {
    this.confirmAction(row, 'reopen', () => this.api.reopen(row.id, row.version));
  }

  confirmDelete(row: GlPeriod): void {
    this.confirmAction(row, 'delete', () => this.api.remove(row.id, row.version));
  }

  private confirmAction(row: GlPeriod, action: RowAction, send: () => Observable<GlPeriodWriteResult | void>): void {
    if (this.busy()) return;
    this.dialogs.confirm({ titleKey: `fi.glPeriod.${action}Confirm`, contentKey: `fi.glPeriod.${action}Content`,
      params: { start: row.startDate, end: row.endDate }, okKey: `fi.glPeriod.${action}`, danger: action !== 'reopen' }).subscribe(ok => {
      if (!ok || this.busy()) return;
      const epoch = this.writeEpoch;
      this.busy.set(true); this.actionError.set(null); this.actionTraceId.set(null); this.notice.set(null);
      send().subscribe({
        next: result => { if (epoch !== this.writeEpoch) return;
          this.busy.set(false);
          if (result) this.announce(action === 'close' ? 'closed' : 'reopened', result);
          else this.notice.set({ type: 'success', key: 'fi.glPeriod.deleted', params: { start: row.startDate, end: row.endDate } });
          this.load(); },
        error: (failure: unknown) => { if (epoch !== this.writeEpoch) return;
          this.busy.set(false); this.fail(failure, row);
          if (isStaleRowError(failure) || isMissingRowError(failure)) this.load(); },
      });
    });
  }

  /** Success text uses only the saved values the server returned; an unchanged result is a warning. */
  private announce(action: 'created' | 'updated' | 'closed' | 'reopened', result: GlPeriodWriteResult): void {
    const saved = result.period;
    const params = { start: saved.startDate, end: saved.endDate, version: String(saved.version) };
    if (result.changed) { this.notice.set({ type: 'success', key: `fi.glPeriod.${action}`, params }); return; }
    const key = action === 'updated' ? 'fi.glPeriod.unchangedDates' : saved.status === 'Open' ? 'fi.glPeriod.unchangedOpen' : 'fi.glPeriod.unchangedClosed';
    this.notice.set({ type: 'warning', key, params });
  }

  private fail(failure: unknown, row: GlPeriod): void {
    this.actionTarget.set({ start: row.startDate, end: row.endDate });
    this.actionError.set(this.api.errorKey(failure)); this.actionTraceId.set(apiErrorTraceId(failure));
  }

  private openDialog(): void {
    this.saveError.set(null); this.saveTraceId.set(null); this.notice.set(null); this.actionError.set(null);
    this.dialogOpen.set(true);
  }

  private reversed(): boolean {
    const { startDate, endDate } = this.form.getRawValue();
    return validPeriodDate(startDate) && validPeriodDate(endDate) && endDate < startDate;
  }

  private focusFirstError(): void {
    setTimeout(() => {
      const form = typeof document === 'undefined' ? null : document.querySelector('[data-testid="fi-gl-period-form"]');
      form?.querySelector<HTMLElement>('[aria-invalid="true"]')?.focus();
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
