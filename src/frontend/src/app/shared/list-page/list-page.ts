import { ChangeDetectionStrategy, Component, input, output } from '@angular/core';
import { TranslatePipe } from '@ngx-translate/core';
import { NzAlertModule } from 'ng-zorro-antd/alert';
import { NzButtonModule } from 'ng-zorro-antd/button';

/**
 * 標準列表頁範本（`erp-list-page`，ui-conventions 第 5 節）。由上到下：
 * 1. 標題列：`titleKey` + `[erpListActions]`（新增、匯入、匯出，各自加 `*erpHasPermission`）；
 * 2. 篩選區：`[erpListFilters]`（欄位、`erp-date-range`）+「查詢」「重設」（`filterable=false` 時不顯示）；
 * 3. 批次操作列：`selectedCount > 0` 時出現，顯示已選筆數、`[erpListBatch]` 按鈕與「清除選取」；
 * 4. 載入錯誤：`errorKey` 有值時顯示錯誤與「重試」；
 * 5. 預設內容：提示訊息與 `nz-table`（伺服器端分頁、排序，見 `table-query.ts`）。
 * `data-testid` 以 `testId` 為前綴，例如 `sys-role` → `sys-role-title`、`sys-role-search-button`。
 */
@Component({
  selector: 'erp-list-page',
  imports: [TranslatePipe, NzAlertModule, NzButtonModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <section class="erp-list-page" [attr.data-testid]="testId() + '-page'">
      <header class="erp-list-page__header">
        <h1 [attr.data-testid]="titleTestId() ?? testId() + '-title'">{{ titleKey() | translate }}</h1>
        <div class="erp-list-page__actions"><ng-content select="[erpListActions]" /></div>
      </header>
      @if (filterable()) {
        <div class="erp-list-page__filters" role="search">
          <ng-content select="[erpListFilters]" />
          <button nz-button nzType="primary" type="button" [attr.data-testid]="testId() + '-search-button'" (click)="querySubmit.emit()">
            {{ 'common.action.search' | translate }}
          </button>
          @if (resettable()) {
            <button nz-button type="button" [attr.data-testid]="testId() + '-reset'" (click)="filterReset.emit()">
              {{ 'common.action.reset' | translate }}
            </button>
          }
        </div>
      }
      @if (selectedCount() > 0) {
        <div class="erp-list-page__batch" role="region" [attr.aria-label]="'common.list.batchActions' | translate"
          [attr.data-testid]="testId() + '-batch'">
          <span [attr.data-testid]="testId() + '-selected-count'">
            {{ 'common.list.selectedCount' | translate: { count: selectedCount() } }}
          </span>
          <ng-content select="[erpListBatch]" />
          <button nz-button nzType="link" type="button" [attr.data-testid]="testId() + '-clear-selection'"
            (click)="selectionClear.emit()">
            {{ 'common.list.clearSelection' | translate }}
          </button>
        </div>
      }
      @if (errorKey()) {
        <nz-alert nzType="error" nzShowIcon [attr.data-testid]="testId() + '-load-error'" [nzMessage]="errorMessage"
          [nzAction]="retryButton" />
        <ng-template #errorMessage>
          {{ errorKey()! | translate }}
          @if (errorTraceId()) { <span [attr.data-testid]="testId() + '-trace-id'">{{ 'common.state.traceId' | translate }}: {{ errorTraceId() }}</span> }
        </ng-template>
        <ng-template #retryButton>
          <button nz-button nzSize="small" type="button" [attr.data-testid]="testId() + '-retry'" (click)="retryLoad.emit()">
            {{ 'common.action.retry' | translate }}
          </button>
        </ng-template>
      }
      <ng-content />
    </section>
  `,
  styles: `
    .erp-list-page { display: grid; gap: var(--erp-space-2, 16px); color: var(--erp-color-text); }
    .erp-list-page__header { display: flex; flex-wrap: wrap; align-items: center; justify-content: space-between; gap: 16px; }
    .erp-list-page__header h1 { margin: 0; font-size: 20px; }
    .erp-list-page__actions { display: flex; flex-wrap: wrap; gap: 8px; }
    .erp-list-page__filters { display: flex; flex-wrap: wrap; align-items: center; gap: 8px; }
    .erp-list-page__batch {
      display: flex; flex-wrap: wrap; align-items: center; gap: 8px; padding: 8px 16px; border-radius: 8px;
      background: var(--erp-color-bg-container); border: 1px solid var(--erp-color-border);
    }
  `,
})
export class ListPage {
  readonly titleKey = input.required<string>();
  /** `data-testid` 前綴（`{prefix}-{resource}`）。 */
  readonly testId = input.required<string>();
  /** 既有頁面搬入共用範本時保留原有標題定位點。 */
  readonly titleTestId = input<string | null>(null);
  readonly filterable = input(true);
  readonly resettable = input(true);
  /** 已勾選筆數；大於 0 時顯示批次操作列。 */
  readonly selectedCount = input(0);
  /** 載入錯誤的 i18n key。 */
  readonly errorKey = input<string | null>(null);
  readonly errorTraceId = input<string | null>(null);

  // 不用 search／reset 等名稱：會同時收到投影內容冒泡上來的原生 DOM 事件
  readonly querySubmit = output<void>();
  readonly filterReset = output<void>();
  readonly retryLoad = output<void>();
  readonly selectionClear = output<void>();
}
