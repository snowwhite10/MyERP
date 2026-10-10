import { Provider } from '@angular/core';
import { CHART_FACTORY, ChartHandle } from '../app/shared/charts/chart-factory';
import { ChartSpec } from '../app/shared/charts/chart-specs';

/** 測試用假圖表（jsdom 沒有 canvas，不建立真正的 G2 實例）。 */
export class FakeChart implements ChartHandle {
  readonly specs: ChartSpec[] = [];
  renderCount = 0;
  destroyed = false;
  readonly handlers = new Map<string, (event: unknown) => void>();

  constructor(readonly container: HTMLElement) {}

  get lastSpec(): ChartSpec | undefined {
    return this.specs.at(-1);
  }

  options(spec: ChartSpec): void {
    this.specs.push(spec);
  }

  render(): Promise<unknown> {
    this.renderCount++;
    return Promise.resolve(this);
  }

  on(event: string, callback: (event: unknown) => void): void {
    this.handlers.set(event, callback);
  }

  destroy(): void {
    this.destroyed = true;
  }
}

export class FakeChartRegistry {
  readonly charts: FakeChart[] = [];

  get last(): FakeChart | undefined {
    return this.charts.at(-1);
  }
}

export function provideFakeCharts(registry: FakeChartRegistry): Provider {
  return {
    provide: CHART_FACTORY,
    useValue: (container: HTMLElement) => {
      const chart = new FakeChart(container);
      registry.charts.push(chart);
      return chart;
    },
  };
}
