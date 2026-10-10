import { ChangeDetectionStrategy, Component, computed, input, output } from '@angular/core';
import { TranslatePipe } from '@ngx-translate/core';
import { injectChartContext } from './chart-context';
import { buildPieSpec, ChartDatum } from './chart-specs';
import { G2Chart } from './g2-chart';

/** 佔比圖（環形）；類別 > 7 時合併「其他」。 */
@Component({
  selector: 'erp-pie-chart',
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
export class PieChart {
  readonly data = input.required<readonly ChartDatum[]>();
  readonly categoryField = input.required<string>();
  readonly valueField = input.required<string>();
  readonly translateCategories = input(false);
  readonly titleKey = input<string>();
  readonly height = input(300);
  readonly elementClick = output<ChartDatum>();

  private readonly context = injectChartContext();

  protected readonly spec = computed(() =>
    buildPieSpec(
      {
        data: this.data(),
        categoryField: this.categoryField(),
        valueField: this.valueField(),
        translateCategories: this.translateCategories(),
      },
      this.context(),
    ),
  );
}
