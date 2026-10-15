import { createEnvironmentInjector, EnvironmentInjector, inject, Injectable } from '@angular/core';
import { TranslateService } from '@ngx-translate/core';
import { NzMessageService } from 'ng-zorro-antd/message';
import { NzModalService } from 'ng-zorro-antd/modal';
import { map, Observable, take } from 'rxjs';
import { MessageDialog, MessageDialogData, MessageDialogKind } from './message-dialog';
import { SelectDialog, SelectDialogOptions } from './select-dialog';

export interface ConfirmOptions {
  titleKey: string;
  contentKey?: string;
  params?: Record<string, unknown>;
  /** 確定按鈕文字（預設「確定」；刪除可傳 `common.action.delete`）。 */
  okKey?: string;
  cancelKey?: string;
  /** 危險動作：確定按鈕為紅色（刪除、作廢必須使用，ui-conventions 第 7 節）。 */
  danger?: boolean;
}

export type AlertOptions = Pick<ConfirmOptions, 'titleKey' | 'contentKey' | 'params' | 'okKey'>;

/** 成功提示（`nz-message`）顯示 3 秒（ui-conventions 第 7 節）。 */
export const TOAST_DURATION_MS = 3000;

/**
 * 共用對話框（BlueWhale 4.4.1.9）：確認框、提示框、選擇視窗與成功提示。所有文字傳 i18n key，
 * 由服務翻譯，頁面不得寫死文字；Esc 與右上角關閉等同取消。各模組一律經本服務開啟，不直接用 NzModalService。
 */
@Injectable({ providedIn: 'root' })
export class ErpDialogService {
  // NzModalService 不是 root 服務（由 NzModalModule 提供）：在根環境下建立自己的一份，頁面不必另外提供
  private readonly modal = createEnvironmentInjector([NzModalService], inject(EnvironmentInjector)).get(NzModalService);
  private readonly message = inject(NzMessageService);
  private readonly translate = inject(TranslateService);

  /** 確認框：按確定發出 `true`，取消／Esc／關閉發出 `false`，然後完成。 */
  confirm(options: ConfirmOptions): Observable<boolean> {
    return this.open('confirm', options).pipe(map((result) => result === true));
  }

  /** 提示框（需要用戶注意的結果，例如匯入結果）：關閉後發出一次並完成。 */
  alert(kind: Exclude<MessageDialogKind, 'confirm'>, options: AlertOptions): Observable<void> {
    return this.open(kind, options).pipe(map(() => undefined));
  }

  /** 選擇視窗：確認發出選取資料；多選可回空陣列以清空，取消或關閉回 null。 */
  select<T>(options: SelectDialogOptions<T>): Observable<T[] | null> {
    const ref = this.modal.create<SelectDialog<T>, SelectDialogOptions<T>, T[]>({
      nzContent: SelectDialog<T>,
      nzData: options,
      nzFooter: null,
      nzWidth: 720,
      nzWrapClassName: 'erp-select-dialog-wrap',
    });
    return ref.afterClose.pipe(take(1), map((rows) => rows ?? null));
  }

  /** 關閉本服務開啟的全部對話框（登出、測試清理）。 */
  closeAll(): void {
    this.modal.closeAll();
  }

  /** 成功提示（頁面上方訊息，3 秒）。 */
  success(key: string, params?: Record<string, unknown>): void {
    this.message.success(this.translate.instant(key, params), { nzDuration: TOAST_DURATION_MS });
  }

  private open(kind: MessageDialogKind, options: ConfirmOptions): Observable<boolean | undefined> {
    const data: MessageDialogData = {
      kind,
      titleKey: options.titleKey,
      contentKey: options.contentKey,
      params: options.params,
      okKey: options.okKey ?? 'common.action.confirm',
      cancelKey: options.cancelKey ?? 'common.action.cancel',
      danger: options.danger ?? false,
    };
    const ref = this.modal.create<MessageDialog, MessageDialogData, boolean>({
      nzContent: MessageDialog,
      nzData: data,
      nzFooter: null,
      nzClosable: false,
      nzMaskClosable: kind !== 'confirm',
      nzWidth: 416,
      nzCentered: true,
      nzWrapClassName: 'erp-message-dialog-wrap',
    });
    return ref.afterClose.pipe(take(1));
  }
}
