import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';
import { TranslatePipe } from '@ngx-translate/core';

/** 單據狀態（ui-conventions 第 4.1 節）。 */
export const DOCUMENT_STATUSES = ['Draft', 'Submitted', 'Approved', 'Posted', 'Rejected', 'Voided', 'Closed'] as const;
export type DocumentStatus = (typeof DOCUMENT_STATUSES)[number];

/** 狀態對應的語意色：草稿灰、待審黃、核准／過帳綠、退回紅、作廢／結案淺灰加刪除線。 */
export const STATUS_TONES: Record<DocumentStatus, 'draft' | 'pending' | 'success' | 'error' | 'muted'> = {
  Draft: 'draft',
  Submitted: 'pending',
  Approved: 'success',
  Posted: 'success',
  Rejected: 'error',
  Voided: 'muted',
  Closed: 'muted',
};

/** 狀態的 i18n key：`common.status.draft` 等。 */
export function statusKey(status: DocumentStatus): string {
  return `common.status.${status.charAt(0).toLowerCase()}${status.slice(1)}`;
}

/**
 * 單據狀態標籤（`erp-status-tag`）：顏色來自 `--erp-status-*` token（亮／暗主題各一組，對比已檢查），
 * 文字為三語狀態名稱。模組自有狀態可傳 `labelKey` 覆寫文字，顏色仍依 `status`。
 */
@Component({
  selector: 'erp-status-tag',
  imports: [TranslatePipe],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <span class="erp-status-tag" [class]="'erp-status-tag--' + tone()" [attr.data-status]="status()"
      [attr.data-testid]="testId()">{{ label() | translate }}</span>
  `,
  styles: `
    .erp-status-tag {
      display: inline-block; padding: 0 8px; border-radius: 4px; font-size: 12px; line-height: 22px;
      color: var(--erp-status-text); white-space: nowrap;
    }
    .erp-status-tag--draft { background: var(--erp-status-draft); }
    .erp-status-tag--pending { background: var(--erp-status-pending); }
    .erp-status-tag--success { background: var(--erp-status-success); }
    .erp-status-tag--error { background: var(--erp-status-error); }
    .erp-status-tag--muted { background: var(--erp-status-muted); text-decoration: line-through; }
  `,
})
export class StatusTag {
  readonly status = input.required<DocumentStatus>();
  readonly labelKey = input<string | null>(null);
  readonly testId = input<string | null>(null);

  readonly tone = computed(() => STATUS_TONES[this.status()]);
  readonly label = computed(() => this.labelKey() ?? statusKey(this.status()));
}
