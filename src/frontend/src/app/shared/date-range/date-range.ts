import { ChangeDetectionStrategy, Component, computed, input, model, output } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { TranslatePipe } from '@ngx-translate/core';
import { NzButtonModule } from 'ng-zorro-antd/button';
import { isoDate } from '../date';

/** 日期範圍快選（ui-conventions 第 5 節）；「自訂」即直接修改兩個日期欄位。 */
export const QUICK_RANGES = ['today', 'thisWeek', 'thisMonth', 'lastMonth', 'thisQuarter', 'thisYear'] as const;
export type QuickRange = (typeof QUICK_RANGES)[number];

export interface DateRange {
  from: string;
  to: string;
}

/**
 * 快選範圍（`yyyy-MM-dd`，含頭尾）：本週由星期一起算；上月為上個月整月；其他到期末為止。
 * `today` 由呼叫端傳入（瀏覽器本地日期），方便測試。
 */
export function quickRange(kind: QuickRange, today: Date): DateRange {
  const y = today.getFullYear();
  const m = today.getMonth();
  const d = today.getDate();
  const range = (from: Date, to: Date): DateRange => ({ from: isoDate(from), to: isoDate(to) });
  switch (kind) {
    case 'today':
      return range(today, today);
    case 'thisWeek': {
      const offset = (today.getDay() + 6) % 7;
      return range(new Date(y, m, d - offset), new Date(y, m, d - offset + 6));
    }
    case 'thisMonth':
      return range(new Date(y, m, 1), new Date(y, m + 1, 0));
    case 'lastMonth':
      return range(new Date(y, m - 1, 1), new Date(y, m, 0));
    case 'thisQuarter': {
      const start = m - (m % 3);
      return range(new Date(y, start, 1), new Date(y, start + 3, 0));
    }
    case 'thisYear':
      return range(new Date(y, 0, 1), new Date(y, 11, 31));
  }
}

/** 兩個日期都有值且開始晚於結束。 */
export function isInvalidRange(range: DateRange): boolean {
  return !!range.from && !!range.to && range.from > range.to;
}

/**
 * 日期範圍篩選（`erp-date-range`）：開始／結束兩個原生日期欄位 + 快選按鈕；`[(from)]`、`[(to)]` 雙向綁定。
 * 按快選時同時發出 `quickSelected`，列表頁可以立即查詢。開始晚於結束時顯示提示（是否阻止查詢由頁面決定）。
 */
@Component({
  selector: 'erp-date-range',
  imports: [FormsModule, TranslatePipe, NzButtonModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="erp-date-range" role="group" [attr.aria-label]="labelKey() | translate">
      <label [attr.for]="testId() + '-from'">{{ 'common.dateRange.from' | translate }}</label>
      <input type="date" [id]="testId() + '-from'" [attr.data-testid]="testId() + '-from'"
        [ngModel]="from()" (ngModelChange)="from.set($event ?? '')" />
      <label [attr.for]="testId() + '-to'">{{ 'common.dateRange.to' | translate }}</label>
      <input type="date" [id]="testId() + '-to'" [attr.data-testid]="testId() + '-to'"
        [ngModel]="to()" (ngModelChange)="to.set($event ?? '')" />
      @for (kind of ranges(); track kind) {
        <button nz-button nzSize="small" type="button" [attr.data-testid]="testId() + '-' + kind" (click)="select(kind)">
          {{ 'common.dateRange.' + kind | translate }}
        </button>
      }
      @if (invalid()) {
        <span role="alert" class="erp-date-range__error" [attr.data-testid]="testId() + '-invalid'">
          {{ 'common.dateRange.invalid' | translate }}
        </span>
      }
    </div>
  `,
  styles: `
    .erp-date-range { display: inline-flex; flex-wrap: wrap; align-items: center; gap: 8px; }
    .erp-date-range input[type='date'] {
      padding: 4px 8px; border: 1px solid var(--erp-color-border); border-radius: 6px;
      background: var(--erp-color-bg-container); color: var(--erp-color-text);
    }
    .erp-date-range__error { color: var(--erp-color-down); font-size: 12px; }
  `,
})
export class DateRangeFilter {
  readonly from = model('');
  readonly to = model('');
  /** `data-testid` 與欄位 id 的前綴，例如 `sal-order-date`。 */
  readonly testId = input('erp-date-range');
  readonly labelKey = input('common.dateRange.label');
  readonly ranges = input<readonly QuickRange[]>(QUICK_RANGES);
  /** 測試用：指定「今天」；正式使用為瀏覽器本地日期。 */
  readonly today = input<Date | null>(null);

  readonly quickSelected = output<DateRange>();

  readonly invalid = computed(() => isInvalidRange({ from: this.from(), to: this.to() }));

  select(kind: QuickRange): void {
    const range = quickRange(kind, this.today() ?? new Date());
    this.from.set(range.from);
    this.to.set(range.to);
    this.quickSelected.emit(range);
  }
}
