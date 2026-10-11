import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { TranslatePipe } from '@ngx-translate/core';
import { NzButtonModule } from 'ng-zorro-antd/button';
import { NzIconModule } from 'ng-zorro-antd/icon';
import { NZ_MODAL_DATA, NzModalRef } from 'ng-zorro-antd/modal';

/** 提示框類型；`confirm` 為確認框（有取消按鈕）。 */
export type MessageDialogKind = 'confirm' | 'info' | 'success' | 'warning' | 'error';

export interface MessageDialogData {
  kind: MessageDialogKind;
  titleKey: string;
  contentKey?: string;
  /** 標題與內容共用的翻譯參數。 */
  params?: Record<string, unknown>;
  okKey: string;
  cancelKey: string;
  /** 危險動作（刪除、作廢）：確定按鈕為紅色。 */
  danger: boolean;
}

const ICONS: Record<MessageDialogKind, string> = {
  confirm: 'exclamation-circle',
  info: 'info-circle',
  success: 'check-circle',
  warning: 'exclamation-circle',
  error: 'close-circle',
};

/**
 * 確認框／提示框的內容（BlueWhale 4.4.1.9 Confirmation / Prompt Box），由 `ErpDialogService` 開啟。
 * 標題放在內容內而非 `nzTitle`：nz-modal 開啟後不更新 nzTitle，放在內容內可隨語言即時切換。
 */
@Component({
  selector: 'erp-message-dialog',
  imports: [TranslatePipe, NzButtonModule, NzIconModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="erp-message" [class]="'erp-message--' + data.kind" role="alertdialog" aria-modal="true"
      aria-labelledby="erp-message-title" data-testid="erp-message-dialog" [attr.data-kind]="data.kind">
      <nz-icon class="erp-message__icon" [nzType]="icon" nzTheme="outline" aria-hidden="true" />
      <div class="erp-message__body">
        <h2 id="erp-message-title" data-testid="erp-message-title">{{ data.titleKey | translate: data.params }}</h2>
        @if (data.contentKey) {
          <p data-testid="erp-message-content">{{ data.contentKey | translate: data.params }}</p>
        }
      </div>
    </div>
    <div class="erp-message__footer">
      @if (data.kind === 'confirm') {
        <button nz-button type="button" data-testid="erp-message-cancel" (click)="close(false)">
          {{ data.cancelKey | translate }}
        </button>
      }
      <button nz-button nzType="primary" type="button" data-testid="erp-message-ok" [nzDanger]="data.danger"
        (click)="close(true)">
        {{ data.okKey | translate }}
      </button>
    </div>
  `,
  styles: `
    .erp-message { display: flex; gap: 16px; align-items: flex-start; color: var(--erp-color-text); }
    .erp-message__icon { font-size: 22px; margin-top: 2px; color: var(--erp-color-primary); }
    .erp-message--confirm .erp-message__icon, .erp-message--warning .erp-message__icon { color: var(--erp-color-text-secondary); }
    .erp-message--success .erp-message__icon { color: var(--erp-color-up); }
    .erp-message--error .erp-message__icon { color: var(--erp-color-down); }
    .erp-message__body h2 { margin: 0; font-size: 16px; font-weight: 600; }
    .erp-message__body p { margin: 8px 0 0; color: var(--erp-color-text-secondary); white-space: pre-line; }
    .erp-message__footer { display: flex; justify-content: flex-end; gap: 8px; margin-top: 24px; }
  `,
})
export class MessageDialog {
  readonly data = inject<MessageDialogData>(NZ_MODAL_DATA);
  private readonly ref = inject<NzModalRef<MessageDialog, boolean>>(NzModalRef);

  readonly icon = ICONS[this.data.kind];

  close(result: boolean): void {
    this.ref.close(result);
  }
}
