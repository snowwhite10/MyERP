import { Component, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { firstValueFrom } from 'rxjs';
import { FakeChartRegistry, provideFakeCharts } from '../../../testing/fake-chart';
import { provideTestTranslations } from '../../../testing/i18n-fixtures';
import { LanguageService } from '../../core/i18n/language.service';
import { MACARON_THEMES } from '../../core/theme/macaron-palette';
import { ThemeService } from '../../core/theme/theme.service';
import { ChartDatum } from './chart-specs';
import { ColumnChart } from './column-chart';
import { LineChart } from './line-chart';
import { PieChart } from './pie-chart';

const TREND: ChartDatum[] = [
  { month: '2026-09', amount: 100, series: 'layout.home.seriesCurrent' },
  { month: '2026-09', amount: 80, series: 'layout.home.seriesPrevious' },
];

@Component({
  imports: [LineChart, ColumnChart, PieChart],
  template: `
    <erp-line-chart
      titleKey="layout.home.salesTrend"
      [data]="data()"
      xField="month"
      yField="amount"
      seriesField="series"
      [translateSeries]="true"
      (elementClick)="clicked.push($event)"
    />
    <erp-column-chart [data]="data()" xField="month" yField="amount" seriesField="series" />
    <erp-pie-chart [data]="data()" categoryField="series" valueField="amount" [translateCategories]="true" />
  `,
})
class HostComponent {
  readonly data = signal<readonly ChartDatum[]>(TREND);
  readonly clicked: ChartDatum[] = [];
}

describe('chart components', () => {
  let registry: FakeChartRegistry;

  function render() {
    const fixture = TestBed.createComponent(HostComponent);
    fixture.detectChanges();
    TestBed.tick();
    return fixture;
  }

  beforeEach(() => {
    localStorage.clear();
    window.matchMedia = undefined as unknown as typeof window.matchMedia;
    registry = new FakeChartRegistry();
    TestBed.configureTestingModule({
      providers: [...provideTestTranslations(), provideFakeCharts(registry)],
    });
  });

  afterEach(() => localStorage.clear());

  it('renders line, column and pie charts with translated titles and series', () => {
    const fixture = render();
    const [line, column, pie] = registry.charts;

    expect(registry.charts).toHaveLength(3);
    expect(fixture.nativeElement.querySelector('.erp-chart-title').textContent).toContain('月份銷售趨勢');
    expect(line.lastSpec?.['type']).toBe('line');
    expect((line.lastSpec?.['data'] as ChartDatum[])[0]['series']).toBe('本期');
    expect(column.lastSpec?.['type']).toBe('interval');
    expect((column.lastSpec?.['data'] as ChartDatum[])[0]['series']).toBe('layout.home.seriesCurrent');
    expect(pie.lastSpec?.['coordinate']).toBeDefined();
    expect((pie.lastSpec?.['data'] as ChartDatum[]).map((d) => d['series'])).toEqual(['本期', '上期']);
  });

  it('re-renders every chart with the dark theme and palette when the theme changes', () => {
    render();

    TestBed.inject(ThemeService).setMode('dark');
    TestBed.tick();

    for (const chart of registry.charts) {
      expect(chart.lastSpec?.['theme']).toBe('classicDark');
      expect(chart.lastSpec?.['scale']).toEqual({ color: { range: [...MACARON_THEMES.dark.chart] } });
    }
    TestBed.inject(ThemeService).setMode('light');
    TestBed.tick();
  });

  it('re-renders translated series when the language changes', async () => {
    render();

    await firstValueFrom(TestBed.inject(LanguageService).use('en'));
    TestBed.tick();

    expect((registry.charts[0].lastSpec?.['data'] as ChartDatum[])[0]['series']).toBe('Current');
  });

  it('re-renders when data changes', () => {
    const fixture = render();

    fixture.componentInstance.data.set([{ month: '2026-10', amount: 5, series: 'x' }]);
    fixture.detectChanges();
    TestBed.tick();

    expect((registry.charts[1].lastSpec?.['data'] as ChartDatum[])[0]['amount']).toBe(5);
  });

  it('forwards element clicks for drill-down', () => {
    const fixture = render();

    registry.charts[0].handlers.get('element:click')!({ data: { data: { month: '2026-09' } } });

    expect(fixture.componentInstance.clicked).toEqual([{ month: '2026-09' }]);
  });
});
