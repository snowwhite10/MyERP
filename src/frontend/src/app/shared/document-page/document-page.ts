import { ChangeDetectionStrategy, Component, input, output } from '@angular/core';
import { TranslatePipe } from '@ngx-translate/core';
import { NzAlertModule } from 'ng-zorro-antd/alert';
import { NzButtonModule } from 'ng-zorro-antd/button';
import { NzIconModule } from 'ng-zorro-antd/icon';
import { NzTooltipModule } from 'ng-zorro-antd/tooltip';
import { DocumentStatus, StatusTag } from '../status-tag/status-tag';

/** 列表進入單據頁時的前後筆（依進入時列表的篩選與排序）。 */
export interface AdjacentIds<K> {
  previous: K | null;
  next: K | null;
}

/** 由列表目前頁的 ID 順序算出前後筆；不在清單內時兩者皆為 null。 */
export function adjacentIds<K>(ids: readonly K[], current: K): AdjacentIds<K> {
  const index = ids.indexOf(current);
  if (index < 0) return { previous: null, next: null };
  return {
    previous: index > 0 ? ids[index - 1] : null,
    next: index < ids.length - 1 ? ids[index + 1] : null,
  };
}

/**
 * 標準單據頁範本（`erp-document-page`，ui-conventions 第 6 節）：
 * 1. 表頭列：返回列表、標題（`titleKey`）與單號、狀態標籤、上一筆／下一筆、`[erpDocumentActions]`（依狀態與權限顯示的動作）；
 * 2. 業務錯誤（422）：`errorKey` 以頁面頂部 `nz-alert` 顯示；`conflict` 為 true 時顯示 409 版本衝突與「重新載入」；
 * 3. 預設內容：表頭表單、明細表格與合計列；
 * 4. `[erpDocumentTabs]`：審批記錄、附件、稽核歷史、關聯單據頁籤。
 * 未儲存離開的確認由路由 `canDeactivate: [unsavedChangesGuard]` 處理（見 unsaved-changes.guard.ts）。
 */
@Component({
  selector: 'erp-document-page',
  imports: [TranslatePipe, NzAlertModule, NzButtonModule, NzIconModule, NzTooltipModule, StatusTag],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <section class="erp-document" [attr.data-testid]="testId() + '-page'">
      <header class="erp-document__header">
        <div class="erp-document__heading">
          <button nz-button nzType="text" type="button" [attr.data-testid]="testId() + '-back'"
            [attr.aria-label]="'common.document.back' | translate" [nz-tooltip]="'common.document.back' | translate"
            (click)="back.emit()">
            <nz-icon nzType="arrow-left" />
          </button>
          <h1 [attr.data-testid]="testId() + '-title'">
            {{ titleKey() | translate }}
            @if (docNo()) {
              <span class="erp-document__no" [attr.data-testid]="testId() + '-doc-no'">{{ docNo() }}</span>
            } @else {
              <span class="erp-document__no erp-document__no--new">{{ 'common.document.new' | translate }}</span>
            }
          </h1>
          @if (status(); as current) {
            <erp-status-tag [status]="current" [labelKey]="statusLabelKey()" [testId]="testId() + '-status'" />
          }
          @if (dirty()) {
            <span class="erp-document__dirty" [attr.data-testid]="testId() + '-dirty'">{{ 'common.document.unsaved' | translate }}</span>
          }
        </div>
        <div class="erp-document__actions">
          @if (navigable()) {
            <button nz-button type="button" [attr.data-testid]="testId() + '-previous'" [disabled]="!hasPrevious()"
              [attr.aria-label]="'common.document.previous' | translate" [nz-tooltip]="'common.document.previous' | translate"
              (click)="previous.emit()">
              <nz-icon nzType="left" />
            </button>
            <button nz-button type="button" [attr.data-testid]="testId() + '-next'" [disabled]="!hasNext()"
              [attr.aria-label]="'common.document.next' | translate" [nz-tooltip]="'common.document.next' | translate"
              (click)="next.emit()">
              <nz-icon nzType="right" />
            </button>
          }
          <ng-content select="[erpDocumentActions]" />
        </div>
      </header>
      @if (conflict()) {
        <nz-alert nzType="warning" nzShowIcon [attr.data-testid]="testId() + '-conflict'"
          [nzMessage]="'common.document.conflict' | translate" [nzAction]="reloadButton" />
        <ng-template #reloadButton>
          <button nz-button nzSize="small" type="button" [attr.data-testid]="testId() + '-reload'" (click)="reload.emit()">
            {{ 'common.document.reload' | translate }}
          </button>
        </ng-template>
      }
      @if (errorKey()) {
        <nz-alert nzType="error" nzShowIcon nzCloseable [attr.data-testid]="testId() + '-error'"
          [nzMessage]="errorKey()! | translate: errorParams()" (nzOnClose)="errorClose.emit()" />
      }
      <div class="erp-document__body"><ng-content /></div>
      <div class="erp-document__tabs"><ng-content select="[erpDocumentTabs]" /></div>
    </section>
  `,
  styles: `
    .erp-document { display: grid; gap: var(--erp-space-2, 16px); color: var(--erp-color-text); }
    .erp-document__header { display: flex; flex-wrap: wrap; align-items: center; justify-content: space-between; gap: 16px; }
    .erp-document__heading { display: flex; flex-wrap: wrap; align-items: center; gap: 8px; }
    .erp-document__heading h1 { margin: 0; font-size: 20px; }
    .erp-document__no { margin-inline-start: 8px; font-family: ui-monospace, Consolas, monospace; }
    .erp-document__no--new, .erp-document__dirty { color: var(--erp-color-text-secondary); font-size: 14px; font-family: inherit; }
    .erp-document__actions { display: flex; flex-wrap: wrap; gap: 8px; }
    .erp-document__body {
      display: grid; gap: var(--erp-space-2, 16px); padding: var(--erp-space-3, 24px); border-radius: var(--erp-radius-card, 8px);
      background: var(--erp-color-bg-container);
    }
    .erp-document__tabs:empty { display: none; }
  `,
})
export class DocumentPage {
  readonly titleKey = input.required<string>();
  readonly testId = input.required<string>();
  /** 單號；新單據為 null（顯示「新增」）。 */
  readonly docNo = input<string | null>(null);
  readonly status = input<DocumentStatus | null>(null);
  readonly statusLabelKey = input<string | null>(null);
  /** 是否顯示上一筆／下一筆（由列表進入時為 true）。 */
  readonly navigable = input(false);
  readonly hasPrevious = input(false);
  readonly hasNext = input(false);
  /** 表單有未儲存的修改。 */
  readonly dirty = input(false);
  /** 業務錯誤（422）的 i18n key。 */
  readonly errorKey = input<string | null>(null);
  readonly errorParams = input<Record<string, unknown>>({});
  /** 儲存時發生 409 版本衝突。 */
  readonly conflict = input(false);

  readonly back = output<void>();
  readonly previous = output<void>();
  readonly next = output<void>();
  readonly reload = output<void>();
  readonly errorClose = output<void>();
}
