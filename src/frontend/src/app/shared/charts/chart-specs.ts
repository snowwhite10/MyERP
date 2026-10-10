/**
 * G2 規格（spec）產生器：純函式，不碰 DOM，可直接單元測試。
 * 圖表元件負責把目前主題、語言與資料交給這些函式，再交給 G2 渲染。
 */
import { EffectiveTheme, ThemeTokens } from '../../core/theme/macaron-palette';

export type ChartDatum = Readonly<Record<string, unknown>>;
export type ChartSpec = Record<string, unknown>;

/** 圓餅圖最多顯示的扇區數（含「其他」），ui-conventions.md 第 9 節。 */
export const PIE_MAX_SLICES = 7;

export interface ChartContext {
  theme: EffectiveTheme;
  tokens: ThemeTokens;
  /** 數值顯示格式（千分位，依目前語言） */
  formatNumber: (value: number) => string;
  /** 翻譯 i18n key */
  translate: (key: string) => string;
}

export interface XYChartInput {
  data: readonly ChartDatum[];
  xField: string;
  yField: string;
  /** 多系列時的系列欄位 */
  seriesField?: string;
  /** 系列欄位值為 i18n key 時設 true，渲染前翻譯 */
  translateSeries?: boolean;
}

export interface PieChartInput {
  data: readonly ChartDatum[];
  categoryField: string;
  valueField: string;
  translateCategories?: boolean;
}

export function g2ThemeName(theme: EffectiveTheme): 'classic' | 'classicDark' {
  return theme === 'dark' ? 'classicDark' : 'classic';
}

export function toNumber(value: unknown): number {
  const n = typeof value === 'number' ? value : Number(value);
  return Number.isFinite(n) ? n : 0;
}

/** 系列 / 類別欄位為 i18n key 時翻譯；回傳新陣列，不修改輸入。 */
export function translateField(
  data: readonly ChartDatum[],
  field: string | undefined,
  translate: (key: string) => string,
): ChartDatum[] {
  if (!field) {
    return [...data];
  }
  return data.map((d) => ({ ...d, [field]: translate(String(d[field] ?? '')) }));
}

/**
 * 類別數 > maxSlices 時，保留數值最大的 (maxSlices - 1) 項，其餘合併為「其他」。
 * 依數值由大到小排序；合計值不變。
 */
export function mergeSmallCategories(
  data: readonly ChartDatum[],
  categoryField: string,
  valueField: string,
  otherLabel: string,
  maxSlices = PIE_MAX_SLICES,
): ChartDatum[] {
  const sorted = [...data].sort((a, b) => toNumber(b[valueField]) - toNumber(a[valueField]));
  if (sorted.length <= maxSlices) {
    return sorted;
  }
  const kept = sorted.slice(0, maxSlices - 1);
  const otherTotal = sorted
    .slice(maxSlices - 1)
    .reduce((sum, d) => sum + toNumber(d[valueField]), 0);
  return [...kept, { [categoryField]: otherLabel, [valueField]: otherTotal }];
}

function base(ctx: ChartContext): ChartSpec {
  return {
    theme: g2ThemeName(ctx.theme),
    autoFit: true,
    scale: { color: { range: [...ctx.tokens.chart] } },
  };
}

function valueTooltip(ctx: ChartContext, channel: string, field: string): ChartSpec {
  return {
    items: [{ channel, field, valueFormatter: (v: unknown) => ctx.formatNumber(toNumber(v)) }],
  };
}

export function buildLineSpec(input: XYChartInput, ctx: ChartContext): ChartSpec {
  const data = input.translateSeries
    ? translateField(input.data, input.seriesField, ctx.translate)
    : [...input.data];
  return {
    ...base(ctx),
    type: 'line',
    data,
    encode: {
      x: input.xField,
      y: input.yField,
      ...(input.seriesField ? { color: input.seriesField } : {}),
    },
    ...(input.seriesField ? {} : { legend: false, style: { stroke: ctx.tokens.chart[0] } }),
    axis: { y: { labelFormatter: (v: unknown) => ctx.formatNumber(toNumber(v)) } },
    tooltip: valueTooltip(ctx, 'y', input.yField),
  };
}

export function buildColumnSpec(input: XYChartInput, ctx: ChartContext): ChartSpec {
  const data = input.translateSeries
    ? translateField(input.data, input.seriesField, ctx.translate)
    : [...input.data];
  return {
    ...base(ctx),
    type: 'interval',
    data,
    encode: {
      x: input.xField,
      y: input.yField,
      ...(input.seriesField ? { color: input.seriesField } : {}),
    },
    ...(input.seriesField
      ? { transform: [{ type: 'dodgeX' }] }
      : { legend: false, style: { fill: ctx.tokens.chart[0] } }),
    axis: { y: { labelFormatter: (v: unknown) => ctx.formatNumber(toNumber(v)) } },
    tooltip: valueTooltip(ctx, 'y', input.yField),
  };
}

export function buildPieSpec(input: PieChartInput, ctx: ChartContext): ChartSpec {
  const translated = input.translateCategories
    ? translateField(input.data, input.categoryField, ctx.translate)
    : [...input.data];
  const data = mergeSmallCategories(
    translated,
    input.categoryField,
    input.valueField,
    ctx.translate('common.chart.other'),
  );
  return {
    ...base(ctx),
    type: 'interval',
    data,
    coordinate: { type: 'theta', outerRadius: 0.85, innerRadius: 0.5 },
    transform: [{ type: 'stackY' }],
    encode: { y: input.valueField, color: input.categoryField },
    legend: { color: { position: 'right' } },
    tooltip: valueTooltip(ctx, 'y', input.valueField),
  };
}

/** KPI 卡片迷你趨勢：無座標軸、圖例、tooltip，使用主色。 */
export function buildSparklineSpec(values: readonly number[], ctx: ChartContext): ChartSpec {
  return {
    theme: g2ThemeName(ctx.theme),
    autoFit: true,
    type: 'area',
    data: values.map((value, index) => ({ index, value: toNumber(value) })),
    encode: { x: 'index', y: 'value', shape: 'smooth' },
    style: { fill: ctx.tokens.primary, fillOpacity: 0.25, stroke: ctx.tokens.primary, lineWidth: 1.5 },
    axis: false,
    legend: false,
    tooltip: false,
    interaction: { tooltip: false },
    margin: 0,
    padding: 0,
  };
}

export type ChangeDirection = 'up' | 'down' | 'flat';

export interface PeriodChange {
  /** 變動比率（0.125 = 12.5%）；上期為 0 或缺少時為 null */
  ratio: number | null;
  direction: ChangeDirection;
}

export function computeChange(current: number, previous: number | null | undefined): PeriodChange {
  if (previous === null || previous === undefined || previous === 0) {
    return { ratio: null, direction: 'flat' };
  }
  const ratio = (current - previous) / Math.abs(previous);
  return { ratio, direction: ratio > 0 ? 'up' : ratio < 0 ? 'down' : 'flat' };
}
