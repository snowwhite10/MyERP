import { MACARON_THEMES } from '../../core/theme/macaron-palette';
import {
  buildColumnSpec,
  buildLineSpec,
  buildPieSpec,
  buildSparklineSpec,
  ChartContext,
  ChartSpec,
  computeChange,
  g2ThemeName,
  mergeSmallCategories,
  PIE_MAX_SLICES,
  toNumber,
  translateField,
} from './chart-specs';

const DICTIONARY: Record<string, string> = {
  'layout.home.seriesCurrent': 'Current',
  'common.chart.other': 'Other',
};

function context(theme: 'light' | 'dark' = 'light'): ChartContext {
  return {
    theme,
    tokens: MACARON_THEMES[theme],
    formatNumber: (v) => new Intl.NumberFormat('en').format(v),
    translate: (key) => DICTIONARY[key] ?? key,
  };
}

type Formatter = (v: unknown) => string;

function yLabelFormatter(spec: ChartSpec): Formatter {
  return (spec['axis'] as { y: { labelFormatter: Formatter } }).y.labelFormatter;
}

function tooltipFormatter(spec: ChartSpec): Formatter {
  return (spec['tooltip'] as { items: { valueFormatter: Formatter }[] }).items[0].valueFormatter;
}

const SERIES_DATA = [
  { month: '2026-09', amount: 1200, series: 'layout.home.seriesCurrent' },
  { month: '2026-10', amount: 3400.5, series: 'layout.home.seriesCurrent' },
];

describe('chart-specs', () => {
  describe('theme', () => {
    it('maps effective theme to G2 theme names', () => {
      expect(g2ThemeName('light')).toBe('classic');
      expect(g2ThemeName('dark')).toBe('classicDark');
    });

    it('uses the macaron palette of the current theme', () => {
      const light = buildLineSpec({ data: SERIES_DATA, xField: 'month', yField: 'amount' }, context());
      const dark = buildLineSpec(
        { data: SERIES_DATA, xField: 'month', yField: 'amount' },
        context('dark'),
      );

      expect(light['theme']).toBe('classic');
      expect(light['scale']).toEqual({ color: { range: [...MACARON_THEMES.light.chart] } });
      expect(dark['theme']).toBe('classicDark');
      expect(dark['scale']).toEqual({ color: { range: [...MACARON_THEMES.dark.chart] } });
    });
  });

  describe('buildLineSpec', () => {
    it('encodes x / y / series and translates series keys when asked', () => {
      const spec = buildLineSpec(
        { data: SERIES_DATA, xField: 'month', yField: 'amount', seriesField: 'series', translateSeries: true },
        context(),
      );

      expect(spec['type']).toBe('line');
      expect(spec['encode']).toEqual({ x: 'month', y: 'amount', color: 'series' });
      expect((spec['data'] as { series: string }[]).map((d) => d.series)).toEqual(['Current', 'Current']);
      expect(spec['legend']).toBeUndefined();
    });

    it('does not mutate the input data', () => {
      buildLineSpec(
        { data: SERIES_DATA, xField: 'month', yField: 'amount', seriesField: 'series', translateSeries: true },
        context(),
      );

      expect(SERIES_DATA[0].series).toBe('layout.home.seriesCurrent');
    });

    it('keeps series values as-is by default', () => {
      const spec = buildLineSpec(
        { data: SERIES_DATA, xField: 'month', yField: 'amount', seriesField: 'series' },
        context(),
      );

      expect((spec['data'] as { series: string }[])[0].series).toBe('layout.home.seriesCurrent');
    });

    it('hides the legend and uses the first palette color for a single series', () => {
      const spec = buildLineSpec({ data: SERIES_DATA, xField: 'month', yField: 'amount' }, context());

      expect(spec['legend']).toBe(false);
      expect(spec['style']).toEqual({ stroke: MACARON_THEMES.light.chart[0] });
      expect(spec['encode']).toEqual({ x: 'month', y: 'amount' });
    });

    it('formats axis labels and tooltip values with thousands separators', () => {
      const spec = buildLineSpec({ data: SERIES_DATA, xField: 'month', yField: 'amount' }, context());

      expect(yLabelFormatter(spec)(1234567)).toBe('1,234,567');
      expect(tooltipFormatter(spec)('3400.5')).toBe('3,400.5');
      expect(tooltipFormatter(spec)('n/a')).toBe('0');
    });
  });

  describe('buildColumnSpec', () => {
    it('dodges multiple series side by side', () => {
      const spec = buildColumnSpec(
        { data: SERIES_DATA, xField: 'month', yField: 'amount', seriesField: 'series', translateSeries: true },
        context(),
      );

      expect(spec['type']).toBe('interval');
      expect(spec['transform']).toEqual([{ type: 'dodgeX' }]);
      expect(spec['encode']).toEqual({ x: 'month', y: 'amount', color: 'series' });
      expect((spec['data'] as { series: string }[])[0].series).toBe('Current');
    });

    it('uses a single color without legend for one series', () => {
      const spec = buildColumnSpec({ data: SERIES_DATA, xField: 'month', yField: 'amount' }, context('dark'));

      expect(spec['transform']).toBeUndefined();
      expect(spec['legend']).toBe(false);
      expect(spec['style']).toEqual({ fill: MACARON_THEMES.dark.chart[0] });
      expect(yLabelFormatter(spec)(5000)).toBe('5,000');
    });
  });

  describe('buildPieSpec', () => {
    it('renders a donut with stacked values', () => {
      const data = [
        { category: 'A', amount: 30 },
        { category: 'B', amount: 70 },
      ];

      const spec = buildPieSpec({ data, categoryField: 'category', valueField: 'amount' }, context());

      expect(spec['coordinate']).toEqual({ type: 'theta', outerRadius: 0.85, innerRadius: 0.5 });
      expect(spec['transform']).toEqual([{ type: 'stackY' }]);
      expect(spec['encode']).toEqual({ y: 'amount', color: 'category' });
      expect((spec['data'] as { category: string }[]).map((d) => d.category)).toEqual(['B', 'A']);
      expect(tooltipFormatter(spec)(12345)).toBe('12,345');
    });

    it('merges small categories into a translated "Other" slice', () => {
      const data = 'ABCDEFGHI'.split('').map((category, i) => ({ category, amount: 100 - i * 10 }));

      const spec = buildPieSpec({ data, categoryField: 'category', valueField: 'amount' }, context());
      const slices = spec['data'] as { category: string; amount: number }[];

      expect(slices).toHaveLength(PIE_MAX_SLICES);
      expect(slices.at(-1)).toEqual({ category: 'Other', amount: 40 + 30 + 20 });
    });

    it('translates category keys when asked', () => {
      const data = [{ category: 'layout.home.seriesCurrent', amount: 1 }];

      const spec = buildPieSpec(
        { data, categoryField: 'category', valueField: 'amount', translateCategories: true },
        context(),
      );

      expect((spec['data'] as { category: string }[])[0].category).toBe('Current');
    });
  });

  describe('mergeSmallCategories', () => {
    it('keeps up to the maximum number of slices unchanged (sorted)', () => {
      const data = [
        { c: 'x', v: 1 },
        { c: 'y', v: 3 },
      ];

      expect(mergeSmallCategories(data, 'c', 'v', 'Other')).toEqual([
        { c: 'y', v: 3 },
        { c: 'x', v: 1 },
      ]);
    });

    it('keeps exactly maxSlices categories without merging', () => {
      const data = Array.from({ length: PIE_MAX_SLICES }, (_, i) => ({ c: `c${i}`, v: i }));

      expect(mergeSmallCategories(data, 'c', 'v', 'Other')).toHaveLength(PIE_MAX_SLICES);
      expect(mergeSmallCategories(data, 'c', 'v', 'Other').some((d) => d['c'] === 'Other')).toBe(false);
    });

    it('preserves the total when merging', () => {
      const data = Array.from({ length: 12 }, (_, i) => ({ c: `c${i}`, v: i + 1 }));
      const total = (rows: readonly Record<string, unknown>[]) =>
        rows.reduce((sum, d) => sum + toNumber(d['v']), 0);

      const merged = mergeSmallCategories(data, 'c', 'v', 'Other', 4);

      expect(merged).toHaveLength(4);
      expect(total(merged)).toBe(total(data));
    });

    it('treats non-numeric values as zero', () => {
      const data = [
        { c: 'a', v: 'abc' },
        { c: 'b', v: '5' },
      ];

      expect(mergeSmallCategories(data, 'c', 'v', 'Other').map((d) => d['c'])).toEqual(['b', 'a']);
    });
  });

  describe('translateField', () => {
    it('returns a copy when no field is given', () => {
      const data = [{ a: 1 }];
      const result = translateField(data, undefined, (k) => k);

      expect(result).toEqual(data);
      expect(result).not.toBe(data);
    });

    it('translates missing values as empty keys', () => {
      expect(translateField([{ a: 1 }], 's', (k) => `[${k}]`)).toEqual([{ a: 1, s: '[]' }]);
    });
  });

  describe('buildSparklineSpec', () => {
    it('draws a minimal area chart in the primary color', () => {
      const spec = buildSparklineSpec([3, 5, 4], context('dark'));

      expect(spec['type']).toBe('area');
      expect(spec['data']).toEqual([
        { index: 0, value: 3 },
        { index: 1, value: 5 },
        { index: 2, value: 4 },
      ]);
      expect(spec['axis']).toBe(false);
      expect(spec['legend']).toBe(false);
      expect(spec['tooltip']).toBe(false);
      expect((spec['style'] as { stroke: string }).stroke).toBe(MACARON_THEMES.dark.primary);
    });
  });

  describe('computeChange', () => {
    it.each([
      [110, 100, 0.1, 'up'],
      [90, 100, -0.1, 'down'],
      [100, 100, 0, 'flat'],
      [-50, -100, 0.5, 'up'],
    ] as const)('current %d vs previous %d -> %d (%s)', (current, previous, ratio, direction) => {
      const change = computeChange(current, previous);
      expect(change.ratio).toBeCloseTo(ratio, 10);
      expect(change.direction).toBe(direction);
    });

    it.each([null, undefined, 0])('has no ratio when previous is %s', (previous) => {
      expect(computeChange(100, previous)).toEqual({ ratio: null, direction: 'flat' });
    });
  });
});
