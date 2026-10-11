import { inject } from '@angular/core';
import { CanDeactivateFn } from '@angular/router';
import { Observable, of } from 'rxjs';
import { ErpDialogService } from '../dialogs/dialog.service';

/** 有表單的頁面（單據頁、設定頁）實作此介面，供 `unsavedChangesGuard` 判斷。 */
export interface HasUnsavedChanges {
  hasUnsavedChanges(): boolean;
}

/**
 * 未儲存離開時提示確認（ui-conventions 第 6 節）：路由加 `canDeactivate: [unsavedChangesGuard]`。
 * 沒有修改時直接離開；有修改時開確認框，選「離開」才放行。
 */
export const unsavedChangesGuard: CanDeactivateFn<HasUnsavedChanges> = (component): Observable<boolean> => {
  if (!component?.hasUnsavedChanges()) return of(true);
  return inject(ErpDialogService).confirm({
    titleKey: 'common.document.leaveTitle',
    contentKey: 'common.document.leaveContent',
    okKey: 'common.document.leave',
    cancelKey: 'common.document.stay',
    danger: true,
  });
};
