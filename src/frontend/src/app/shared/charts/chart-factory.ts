import { InjectionToken } from '@angular/core';
import { Chart, G2Spec } from '@antv/g2';
import { ChartSpec } from './chart-specs';

/** 圖表實例的最小介面（方便測試以假物件替代 G2；jsdom 沒有 canvas）。 */
export interface ChartHandle {
  options(spec: ChartSpec): void;
  render(): Promise<unknown>;
  on(event: string, callback: (event: unknown) => void): void;
  destroy(): void;
}

export type ChartFactory = (container: HTMLElement) => ChartHandle;

export function createG2Chart(container: HTMLElement): ChartHandle {
  const chart = new Chart({ container, autoFit: true });
  return {
    options: (spec) => chart.options(spec as G2Spec),
    render: () => chart.render(),
    on: (event, callback) => chart.on(event, callback),
    destroy: () => chart.destroy(),
  };
}

export const CHART_FACTORY = new InjectionToken<ChartFactory>('CHART_FACTORY', {
  providedIn: 'root',
  factory: () => createG2Chart,
});
