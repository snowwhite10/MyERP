import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  effect,
  ElementRef,
  inject,
  input,
  output,
  viewChild,
} from '@angular/core';
import { TranslatePipe } from '@ngx-translate/core';
import { NzEmptyModule } from 'ng-zorro-antd/empty';
import { CHART_FACTORY, ChartHandle } from './chart-factory';
import { ChartDatum, ChartSpec } from './chart-specs';

/**
 * G2 渲染容器（內部元件）：spec 為 null 或無資料時顯示空狀態；spec 改變時更新同一個圖表實例。
 * 業務頁面使用 erp-line-chart / erp-column-chart / erp-pie-chart / erp-kpi-card，不直接使用本元件。
 */
@Component({
  selector: 'erp-g2-chart',
  imports: [NzEmptyModule, TranslatePipe],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @if (hasData()) {
      <div #container class="erp-g2-chart" [style.height.px]="height()"></div>
    } @else if (showEmpty()) {
      <div class="erp-g2-chart erp-g2-chart--empty" [style.height.px]="height()">
        <nz-empty nzNotFoundImage="simple" [nzNotFoundContent]="'common.chart.empty' | translate" />
      </div>
    }
  `,
  styles: `
    :host {
      display: block;
    }
    .erp-g2-chart {
      width: 100%;
    }
    .erp-g2-chart--empty {
      display: flex;
      align-items: center;
      justify-content: center;
    }
  `,
})
export class G2Chart {
  readonly spec = input<ChartSpec | null>(null);
  readonly height = input(300);
  readonly showEmpty = input(true);
  /** 點擊資料元素（鑽取），輸出該筆資料 */
  readonly elementClick = output<ChartDatum>();

  private readonly container = viewChild<ElementRef<HTMLElement>>('container');
  private readonly factory = inject(CHART_FACTORY);
  private chart: ChartHandle | null = null;
  private chartHost: HTMLElement | null = null;

  constructor() {
    effect(() => {
      const spec = this.spec();
      const host = this.container()?.nativeElement ?? null;
      this.renderChart(spec, host);
    });
    inject(DestroyRef).onDestroy(() => this.destroyChart());
  }

  hasData(): boolean {
    const data = this.spec()?.['data'];
    return Array.isArray(data) && data.length > 0;
  }

  private renderChart(spec: ChartSpec | null, host: HTMLElement | null): void {
    if (!spec || !host) {
      this.destroyChart();
      return;
    }
    if (this.chart && this.chartHost !== host) {
      this.destroyChart();
    }
    if (!this.chart) {
      this.chart = this.factory(host);
      this.chartHost = host;
      this.chart.on('element:click', (event) => {
        const datum = (event as { data?: { data?: ChartDatum } }).data?.data;
        if (datum) {
          this.elementClick.emit(datum);
        }
      });
    }
    this.chart.options(spec);
    void this.chart.render();
  }

  private destroyChart(): void {
    this.chart?.destroy();
    this.chart = null;
    this.chartHost = null;
  }
}
