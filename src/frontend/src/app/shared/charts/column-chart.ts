import { ChangeDetectionStrategy, Component, computed, input, output } from '@angular/core';
import { TranslatePipe } from '@ngx-translate/core';
import { injectChartContext } from './chart-context';
import { buildColumnSpec, ChartDatum } from './chart-specs';
import { G2Chart } from './g2-chart';

/** 比較圖（本期 vs 上期 / 預算）；有 seriesField 時並列。 */
@Component({
  selector: 'erp-column-chart',
  imports: [G2Chart, TranslatePipe],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @if (titleKey()) {
      <div class="erp-chart-title">{{ titleKey() | translate }}</div>
    }
    <erp-g2-chart [spec]="spec()" [height]="height()" (elementClick)="elementClick.emit($event)" />
  `,
  styleUrl: './chart.scss',
})
export class ColumnChart {
  readonly data = input.required<readonly ChartDatum[]>();
  readonly xField = input.required<string>();
  readonly yField = input.required<string>();
  readonly seriesField = input<string>();
  readonly translateSeries = input(false);
  readonly titleKey = input<string>();
  readonly height = input(300);
  readonly elementClick = output<ChartDatum>();

  private readonly context = injectChartContext();

  protected readonly spec = computed(() =>
    buildColumnSpec(
      {
        data: this.data(),
        xField: this.xField(),
        yField: this.yField(),
        seriesField: this.seriesField(),
        translateSeries: this.translateSeries(),
      },
      this.context(),
    ),
  );
}
