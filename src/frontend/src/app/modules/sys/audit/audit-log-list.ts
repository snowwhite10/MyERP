import { ChangeDetectionStrategy, Component, computed, effect, inject, input, signal, untracked } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { TranslatePipe } from '@ngx-translate/core';
import { NzAlertModule } from 'ng-zorro-antd/alert';
import { NzButtonModule } from 'ng-zorro-antd/button';
import { NzInputModule } from 'ng-zorro-antd/input';
import { NzSelectModule } from 'ng-zorro-antd/select';
import { NzTableModule, NzTableQueryParams } from 'ng-zorro-antd/table';
import { NzTagModule } from 'ng-zorro-antd/tag';
import { CompanyContextService } from '../../../core/company/company-context.service';
import { latestRequest } from '../../../shared/list-page/latest-request';
import { isoDate } from '../../../shared/date';
import {
  AUDIT_ACTIONS,
  AUDIT_CATEGORIES,
  auditActionKey,
  auditCategoryKey,
  AuditChangeRow,
  auditChangeRows,
  auditErrorKey,
  AuditLogItem,
  AuditLogService,
} from './audit-log.service';

/** 列表的一列：記錄本身及展開用的明細。 */
export type AuditLogRow = AuditLogItem & { details: AuditChangeRow[] };

/** 日期快選：今天、近 7 天、近 30 天（含今天）。 */
export const AUDIT_QUICK_RANGES = [1, 7, 30] as const;

export { isoDate };

/**
 * 稽核查詢頁（介面 8.66，REQ_F4118.27.6）：日期範圍（含快選）、類別、操作、資料表與記錄 ID 篩選；伺服器端分頁與時間排序；
 * 展開列顯示欄位差異（old → new，`***` 照原樣）或安全事件明細。只讀，沒有修改或刪除。
 * query string `tableName`＋`recordId`（及 `userId`）開啟單據修改歷史，供單據頁的稽核頁籤連結。切換公司後重新查詢。
 */
@Component({
  selector: 'erp-audit-log-list',
  imports: [FormsModule, TranslatePipe, NzAlertModule, NzButtonModule, NzInputModule, NzSelectModule, NzTableModule, NzTagModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <section class="erp-audit">
      <header class="erp-audit__header">
        <h1 data-testid="sys-audit-title">{{ 'sys.audit.title' | translate }}</h1>
      </header>
      @if (historyMode()) {
        <nz-alert nzType="info" nzShowIcon data-testid="sys-audit-history"
          [nzMessage]="'sys.audit.historyOf' | translate: { table: tableName(), record: recordId() }" />
      }
      <div class="erp-audit__filters">
        <label for="audit-from">{{ 'sys.audit.from' | translate }}</label>
        <input id="audit-from" type="date" data-testid="sys-audit-from" [ngModel]="from()" (ngModelChange)="from.set($event ?? '')" />
        <label for="audit-to">{{ 'sys.audit.to' | translate }}</label>
        <input id="audit-to" type="date" data-testid="sys-audit-to" [ngModel]="to()" (ngModelChange)="to.set($event ?? '')" />
        @for (days of quickRanges; track days) {
          <button nz-button nzSize="small" [attr.data-testid]="'sys-audit-range-' + days" (click)="quickRange(days)">
            {{ (days === 1 ? 'sys.audit.today' : 'sys.audit.lastDays') | translate: { days: days } }}
          </button>
        }
      </div>
      <div class="erp-audit__filters">
        <nz-select nzId="audit-category" data-testid="sys-audit-category" nzAllowClear
          [nzPlaceHolder]="'sys.audit.category' | translate" [ngModel]="category()" (ngModelChange)="category.set($event ?? null)">
          @for (code of categories; track code) {
            <nz-option [nzValue]="code" [nzLabel]="'sys.audit.categories.' + code | translate" />
          }
        </nz-select>
        <nz-select nzId="audit-action" data-testid="sys-audit-action" nzAllowClear nzShowSearch
          [nzPlaceHolder]="'sys.audit.action' | translate" [ngModel]="action()" (ngModelChange)="action.set($event ?? null)">
          @for (code of actions; track code) {
            <nz-option [nzValue]="code" [nzLabel]="'sys.audit.actions.' + code | translate" />
          }
        </nz-select>
        <input nz-input data-testid="sys-audit-table-name" maxlength="128" [attr.aria-label]="'sys.audit.tableName' | translate"
          [placeholder]="'sys.audit.tableName' | translate" [ngModel]="tableName()" (ngModelChange)="tableName.set($event)"
          (keyup.enter)="search()" />
        <input nz-input data-testid="sys-audit-record-id" maxlength="100" [attr.aria-label]="'sys.audit.recordId' | translate"
          [placeholder]="'sys.audit.recordId' | translate" [ngModel]="recordId()" (ngModelChange)="recordId.set($event)"
          (keyup.enter)="search()" />
        @if (userId() !== null) {
          <nz-tag nzMode="closeable" data-testid="sys-audit-user-filter" (nzOnClose)="clearUser()">
            {{ 'sys.audit.userFilter' | translate: { user: userId() } }}
          </nz-tag>
        }
        <button nz-button nzType="primary" data-testid="sys-audit-search" (click)="search()">
          {{ 'common.action.search' | translate }}
        </button>
        <button nz-button data-testid="sys-audit-reset" (click)="reset()">{{ 'common.action.reset' | translate }}</button>
      </div>
      @if (filterError()) {
        <nz-alert nzType="warning" nzShowIcon data-testid="sys-audit-filter-error" [nzMessage]="filterError()! | translate" />
      }
      @if (loadError()) {
        <nz-alert nzType="error" nzShowIcon data-testid="sys-audit-load-error" [nzMessage]="loadError()! | translate" />
      }
      <nz-table #table data-testid="sys-audit-table" nzSize="middle" [nzData]="rows()" [nzLoading]="loading()"
        [nzFrontPagination]="false" [nzTotal]="total()" [nzPageIndex]="page()" [nzPageSize]="pageSize()"
        nzShowSizeChanger [nzNoResult]="'sys.audit.empty' | translate" (nzQueryParams)="onQueryParams($event)">
        <thead>
          <tr>
            <th nzWidth="48px"></th>
            <th nzColumnKey="occurredAt" [nzSortFn]="true" [nzSortOrder]="sortOrder()">{{ 'sys.audit.occurredAt' | translate }}</th>
            <th>{{ 'sys.audit.category' | translate }}</th>
            <th>{{ 'sys.audit.action' | translate }}</th>
            <th>{{ 'sys.audit.record' | translate }}</th>
            <th>{{ 'sys.audit.user' | translate }}</th>
            <th>{{ 'sys.audit.remoteIp' | translate }}</th>
          </tr>
        </thead>
        <tbody>
          @for (row of table.data; track row.id) {
            <tr [attr.data-testid]="'sys-audit-row-' + row.id">
              <td [nzShowExpand]="row.details.length > 0" [nzExpand]="expanded().has(row.id)"
                [attr.data-testid]="'sys-audit-expand-' + row.id" (nzExpandChange)="toggle(row.id, $event)"></td>
              <td>{{ displayTime(row.occurredAt) }}</td>
              <td>
                <nz-tag [nzColor]="row.category === 'SECURITY' ? 'purple' : 'blue'">{{ label(categoryKey(row.category), row.category) | translate }}</nz-tag>
              </td>
              <td>
                <nz-tag [nzColor]="row.action === 'LOGIN_FAILED' || row.action === 'MASTER_CODE_LOGIN' ? 'warning' : 'default'">
                  {{ label(actionKey(row.action), row.action) | translate }}
                </nz-tag>
              </td>
              <td>
                @if (row.tableName) {
                  <button nz-button nzType="link" nzSize="small" [attr.data-testid]="'sys-audit-history-' + row.id"
                    [disabled]="!row.recordId" (click)="openHistory(row)">
                    {{ row.tableName }}@if (row.recordId) { #{{ row.recordId }} }
                  </button>
                } @else {
                  <span class="erp-audit__muted">—</span>
                }
              </td>
              <td>{{ row.username ?? (row.userId !== null ? '#' + row.userId : ('sys.audit.noUser' | translate)) }}</td>
              <td>{{ row.remoteIp ?? '—' }}</td>
            </tr>
            <tr [nzExpand]="expanded().has(row.id)">
              <!-- 用 grid 而非巢狀 table：nz-table 會接管內層的 thead -->
              <div class="erp-audit__changes" role="table" [attr.data-testid]="'sys-audit-changes-' + row.id">
                <div class="erp-audit__change erp-audit__change--head" role="row">
                  <span role="columnheader">{{ 'sys.audit.field' | translate }}</span>
                  <span role="columnheader">{{ 'sys.audit.oldValue' | translate }}</span>
                  <span role="columnheader">{{ 'sys.audit.newValue' | translate }}</span>
                </div>
                @for (change of row.details; track change.field) {
                  <div class="erp-audit__change" role="row" [attr.data-testid]="'sys-audit-change-' + row.id + '-' + change.field">
                    <span role="cell">{{ change.field }}</span>
                    @if (change.kind === 'diff') {
                      <span role="cell" class="erp-audit__old">{{ change.old ?? ('sys.audit.nullValue' | translate) }}</span>
                      <span role="cell" class="erp-audit__new">{{ change.new ?? ('sys.audit.nullValue' | translate) }}</span>
                    } @else {
                      <span role="cell" class="erp-audit__detail">{{ change.value ?? ('sys.audit.nullValue' | translate) }}</span>
                    }
                  </div>
                }
              </div>
              @if (row.traceId) {
                <small class="erp-audit__muted">{{ 'sys.audit.traceId' | translate }}: {{ row.traceId }}</small>
              }
            </tr>
          }
        </tbody>
      </nz-table>
    </section>
  `,
  styles: `
    .erp-audit { display: grid; gap: var(--erp-space-2, 16px); color: var(--erp-color-text); }
    .erp-audit__header h1 { margin: 0; font-size: 20px; }
    .erp-audit__filters { display: flex; flex-wrap: wrap; align-items: center; gap: 8px; }
    .erp-audit__filters input[type='date'] {
      padding: 4px 8px; border: 1px solid var(--erp-color-border, #d9d9d9); border-radius: 6px;
      background: var(--erp-color-bg-container, #fff); color: var(--erp-color-text);
    }
    .erp-audit__filters nz-select { min-width: 160px; }
    .erp-audit__filters input[nz-input] { max-width: 200px; }
    .erp-audit__changes { display: grid; margin-bottom: 4px; }
    .erp-audit__change { display: grid; grid-template-columns: minmax(120px, 1fr) 2fr 2fr; }
    .erp-audit__change > span { padding: 4px 8px; border-bottom: 1px solid var(--erp-color-border, #f0f0f0); word-break: break-all; }
    .erp-audit__change--head { font-weight: 600; }
    .erp-audit__detail { grid-column: span 2; }
    .erp-audit__old { color: var(--erp-status-error); }
    .erp-audit__new { color: var(--erp-status-success); }
    .erp-audit__muted { color: var(--erp-color-text-secondary, inherit); }
    nz-alert { margin-bottom: 12px; }
  `,
})
export class AuditLogList {
  private readonly api = inject(AuditLogService);
  private readonly listRequest = latestRequest();
  private readonly company = inject(CompanyContextService);
  private readonly router = inject(Router);

  /** query string（withComponentInputBinding）：單據修改歷史與用戶篩選。 */
  readonly tableNameParam = input<string | undefined>(undefined, { alias: 'tableName' });
  readonly recordIdParam = input<string | undefined>(undefined, { alias: 'recordId' });
  readonly userIdParam = input<string | undefined>(undefined, { alias: 'userId' });

  readonly quickRanges = AUDIT_QUICK_RANGES;
  readonly categories = AUDIT_CATEGORIES;
  readonly actions = AUDIT_ACTIONS;

  readonly from = signal('');
  readonly to = signal('');
  readonly category = signal<string | null>(null);
  readonly action = signal<string | null>(null);
  readonly tableName = signal('');
  readonly recordId = signal('');
  readonly userId = signal<number | null>(null);

  readonly rows = signal<AuditLogRow[]>([]);
  readonly total = signal(0);
  readonly page = signal(1);
  readonly pageSize = signal(20);
  readonly sort = signal('-occurredAt');
  readonly loading = signal(false);
  readonly loadError = signal<string | null>(null);
  readonly filterError = signal<string | null>(null);
  readonly expanded = signal<ReadonlySet<number>>(new Set());

  /** query string 帶了資料表與記錄 ID：顯示「單據修改歷史」提示。 */
  readonly historyMode = computed(() => !!this.tableNameParam()?.trim() && !!this.recordIdParam()?.trim());

  private readonly companyId = computed(() => this.company.current()?.id ?? null);

  constructor() {
    // 首次載入、query string 改變及切換公司時重新查詢
    effect(() => {
      this.companyId();
      const tableName = this.tableNameParam() ?? '';
      const recordId = this.recordIdParam() ?? '';
      const userId = Number(this.userIdParam());
      untracked(() => {
        this.tableName.set(tableName);
        this.recordId.set(recordId);
        this.userId.set(Number.isSafeInteger(userId) && userId > 0 ? userId : null);
        this.search();
      });
    });
  }

  sortOrder(): 'ascend' | 'descend' {
    return this.sort() === 'occurredAt' ? 'ascend' : 'descend';
  }

  /** `2026-10-11T08:30:15+08:00` → `2026-10-11 08:30:15`（伺服器 UTC+8 時間，不換算瀏覽器時區）。 */
  displayTime(value: string | null | undefined): string {
    return value ? value.slice(0, 19).replace('T', ' ') : '';
  }

  actionKey(action: string): string | null {
    return auditActionKey(action);
  }

  categoryKey(category: string): string | null {
    return auditCategoryKey(category);
  }

  /** 有 i18n key 用 key，否則顯示原代碼（translate 找不到 key 時原樣輸出）。 */
  label(key: string | null, raw: string): string {
    return key ?? raw;
  }

  /** 日期快選：結束日為今天，開始日往前推 `days - 1` 天，並立即查詢。 */
  quickRange(days: number): void {
    const today = new Date();
    const start = new Date(today.getFullYear(), today.getMonth(), today.getDate() - (days - 1));
    this.from.set(isoDate(start));
    this.to.set(isoDate(today));
    this.search();
  }

  search(): void {
    this.page.set(1);
    this.reload();
  }

  reset(): void {
    this.from.set('');
    this.to.set('');
    this.category.set(null);
    this.action.set(null);
    this.tableName.set('');
    this.recordId.set('');
    this.userId.set(null);
    // 由 query string 開啟的歷史：一併清除網址參數（輸入改變後由 effect 重新查詢）
    if (this.tableNameParam() || this.recordIdParam() || this.userIdParam()) {
      void this.router.navigate([], { queryParams: {} });
      return;
    }
    this.search();
  }

  clearUser(): void {
    this.userId.set(null);
    this.search();
  }

  /** 開啟該記錄的完整修改歷史（寫入 query string，可分享或由單據頁連結）。 */
  openHistory(row: AuditLogItem): void {
    if (!row.tableName || !row.recordId) return;
    void this.router.navigate([], { queryParams: { tableName: row.tableName, recordId: row.recordId } });
  }

  toggle(id: number, open: boolean): void {
    const next = new Set(this.expanded());
    if (open) next.add(id);
    else next.delete(id);
    this.expanded.set(next);
  }

  onQueryParams(params: NzTableQueryParams): void {
    const sorted = params.sort.find((s) => s.value !== null);
    const sort = sorted?.value === 'ascend' ? 'occurredAt' : '-occurredAt';
    if (params.pageIndex === this.page() && params.pageSize === this.pageSize() && sort === this.sort()) return;
    this.page.set(params.pageIndex);
    this.pageSize.set(params.pageSize);
    this.sort.set(sort);
    this.reload();
  }

  reload(): void {
    if (this.from() && this.to() && this.from() > this.to()) {
      this.filterError.set('sys.audit.invalidRange');
      return;
    }
    this.filterError.set(null);
    this.loading.set(true);
    this.loadError.set(null);
    this.listRequest.run(
      this.api.list({
        page: this.page(),
        pageSize: this.pageSize(),
        from: this.from(),
        to: this.to(),
        category: this.category() ?? undefined,
        action: this.action() ?? undefined,
        tableName: this.tableName(),
        recordId: this.recordId(),
        userId: this.userId(),
        sort: this.sort(),
      }),
      {
        next: (result) => {
          this.rows.set(result.items.map((item) => ({ ...item, details: auditChangeRows(item.changes) })));
          this.total.set(result.totalCount);
          this.expanded.set(new Set());
          this.loading.set(false);
        },
        error: (error: unknown) => {
          this.rows.set([]);
          this.total.set(0);
          this.loadError.set(auditErrorKey(error));
          this.loading.set(false);
        },
      });
  }
}
