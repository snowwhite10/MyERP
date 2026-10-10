import { Directive, effect, inject, input, TemplateRef, ViewContainerRef } from '@angular/core';
import { PermissionService } from './permission.service';

/**
 * 無權限時不渲染（ui-conventions：無權限的按鈕與選單不顯示）。可給單一代碼或代碼陣列（任一符合即顯示）：
 * `<button *erpHasPermission="'SYS.ROLE.CREATE'">`、`<a *erpHasPermission="['SYS.ROLE.EDIT', 'SYS.ROLE.DELETE']">`。
 * 切換公司後權限更新，畫面隨之更新。只控制顯示，後端仍檢查。
 */
@Directive({ selector: '[erpHasPermission]' })
export class HasPermissionDirective {
  private readonly template = inject(TemplateRef<unknown>);
  private readonly container = inject(ViewContainerRef);
  private readonly permissions = inject(PermissionService);

  readonly erpHasPermission = input.required<string | readonly string[]>();

  private shown = false;

  constructor() {
    effect(() => {
      const required = this.erpHasPermission();
      const allowed = this.permissions.hasAny(typeof required === 'string' ? [required] : required);
      if (allowed && !this.shown) {
        this.container.createEmbeddedView(this.template);
      } else if (!allowed && this.shown) {
        this.container.clear();
      }
      this.shown = allowed;
    });
  }
}
