import { ChangeDetectionStrategy, Component, computed, inject, input } from '@angular/core';
import { TranslatePipe } from '@ngx-translate/core';
import { NzCardModule } from 'ng-zorro-antd/card';
import { NzIconModule } from 'ng-zorro-antd/icon';
import { LanguageService } from '../../core/i18n/language.service';
import { injectChartContext } from './chart-context';
import { buildSparklineSpec, computeChange } from './chart-specs';
import { G2Chart } from './g2-chart';

/** 幣別小數位，與後端 Money.Round 相同（TWD / JPY 0 位，其他 2 位，ADR-005）。 */
export function currencyFractionDigits(currency: string): number {
  return ['TWD', 'JPY'].includes(currency.toUpperCase()) ? 0 : 2;
}

export function formatKpiValue(
  value: number,
  lang: string,
  currency: string | undefined,
): string {
  if (currency) {
    const digits = currencyFractionDigits(currency);
    return new Intl.NumberFormat(lang, {
      style: 'currency',
      currency,
      minimumFractionDigits: digits,
      maximumFractionDigits: digits,
    }).format(value);
  }
  return new Intl.NumberFormat(lang, { maximumFractionDigits: 2 }).format(value);
}

/** 數字指標 + 環比 / 同比 + 迷你趨勢（ui-conventions.md 第 9 節）。 */
@Component({
  selector: 'erp-kpi-card',
  imports: [NzCardModule, NzIconModule, G2Chart, TranslatePipe],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <nz-card class="erp-kpi-card" [nzBordered]="false">
      <div class="erp-kpi-card__title">{{ titleKey() | translate }}</div>
      <div class="erp-kpi-card__value" data-testid="kpi-value">{{ formattedValue() }}</div>
      <div class="erp-kpi-card__change" data-testid="kpi-change">
        @if (change().ratio === null) {
          <span class="erp-kpi-card__muted">{{ 'common.kpi.noPrevious' | translate }}</span>
        } @else {
          <span class="erp-kpi-card__muted">{{ 'common.kpi.vsPrevious' | translate }}</span>
          <span [class]="'erp-kpi-card__delta erp-kpi-card__delta--' + change().direction">
            @if (change().direction === 'up') {
              <nz-icon nzType="arrow-up" />
            } @else if (change().direction === 'down') {
              <nz-icon nzType="arrow-down" />
            }
            {{ formattedRatio() }}
          </span>
        }
      </div>
      @if (trend().length > 1) {
        <erp-g2-chart [spec]="sparkline()" [height]="40" [showEmpty]="false" />
      }
    </nz-card>
  `,
  styles: `
    :host {
      display: block;
    }
    .erp-kpi-card {
      border-radius: var(--erp-radius-card);
    }
    .erp-kpi-card__title,
    .erp-kpi-card__muted {
      color: var(--erp-color-text-secondary);
    }
    .erp-kpi-card__value {
      margin: var(--erp-space-1) 0;
      font-size: 28px;
      font-weight: 600;
      color: var(--erp-color-text);
      font-variant-numeric: tabular-nums;
    }
    .erp-kpi-card__change {
      display: flex;
      gap: var(--erp-space-1);
      margin-bottom: var(--erp-space-1);
    }
    .erp-kpi-card__delta--up {
      color: var(--erp-color-up);
    }
    .erp-kpi-card__delta--down {
      color: var(--erp-color-down);
    }
  `,
})
export class KpiCard {
  readonly titleKey = input.required<string>();
  readonly value = input.required<number>();
  /** 上期值；null / 0 時不顯示變動比率 */
  readonly previous = input<number | null>(null);
  /** ISO 4217 幣別；未設定時以一般數字顯示 */
  readonly currency = input<string>();
  readonly trend = input<readonly number[]>([]);

  private readonly language = inject(LanguageService);
  private readonly context = injectChartContext();

  protected readonly change = computed(() => computeChange(this.value(), this.previous()));
  protected readonly formattedValue = computed(() =>
    formatKpiValue(this.value(), this.language.current(), this.currency()),
  );
  protected readonly formattedRatio = computed(() => {
    const ratio = this.change().ratio ?? 0;
    return new Intl.NumberFormat(this.language.current(), {
      style: 'percent',
      maximumFractionDigits: 1,
    }).format(Math.abs(ratio));
  });
  protected readonly sparkline = computed(() => buildSparklineSpec(this.trend(), this.context()));
}
