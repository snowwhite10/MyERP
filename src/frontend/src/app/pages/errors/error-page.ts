import { ChangeDetectionStrategy, Component, input } from '@angular/core';
import { RouterLink } from '@angular/router';
import { TranslatePipe } from '@ngx-translate/core';
import { NzButtonModule } from 'ng-zorro-antd/button';
import { NzResultModule } from 'ng-zorro-antd/result';

/** 403 / 404 頁（路由 data.status 決定顯示內容）。 */
@Component({
  selector: 'erp-error-page',
  imports: [RouterLink, TranslatePipe, NzButtonModule, NzResultModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <nz-result
      [attr.data-testid]="'error-page-' + status()"
      [nzStatus]="status() === 403 ? '403' : '404'"
      [nzTitle]="'errors.page.' + key() + '.title' | translate"
      [nzSubTitle]="'errors.page.' + key() + '.description' | translate"
    >
      <div nz-result-extra>
        <a nz-button nzType="primary" routerLink="/" data-testid="error-page-home">
          {{ 'common.action.backHome' | translate }}
        </a>
      </div>
    </nz-result>
  `,
})
export class ErrorPage {
  /** 由路由 data 綁定（withComponentInputBinding） */
  readonly status = input<403 | 404>(404);

  protected key(): 'forbidden' | 'notFound' {
    return this.status() === 403 ? 'forbidden' : 'notFound';
  }
}
