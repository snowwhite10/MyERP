import { ChangeDetectionStrategy, Component, input } from '@angular/core';
import { TranslatePipe } from '@ngx-translate/core';
import { NzAlertModule } from 'ng-zorro-antd/alert';

/** 共用 API 錯誤顯示，5xx 同時呈現追蹤碼。 */
@Component({
  selector: 'erp-api-error-display',
  imports: [TranslatePipe, NzAlertModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `<div aria-live="polite"><nz-alert nzType="error" nzShowIcon
    [nzMessage]="messageKey() | translate" [nzDescription]="traceId() ? 'Trace ID: ' + traceId() : null" />
    @if (details().length) { <ul class="api-error-fields">@for (detail of details(); track $index) { <li>{{ detail }}</li> }</ul> }
  </div>`,
  styles: `.api-error-fields{margin:6px 0 0;padding-left:24px;color:var(--erp-color-error,#cf1322);overflow-wrap:anywhere}`,
})
export class ApiErrorDisplay {
  readonly messageKey = input.required<string>();
  readonly traceId = input<string | null>(null);
  readonly details = input<readonly string[]>([]);
}
