import { contrastRatio, parseHexColor, relativeLuminance } from './color-contrast';
import { EffectiveTheme, MACARON_THEMES } from './macaron-palette';

const THEMES: EffectiveTheme[] = ['light', 'dark'];

describe('color-contrast', () => {
  it('computes WCAG reference values', () => {
    expect(contrastRatio('#000000', '#ffffff')).toBeCloseTo(21, 5);
    expect(contrastRatio('#ffffff', '#ffffff')).toBeCloseTo(1, 5);
    expect(relativeLuminance('#ffffff')).toBeCloseTo(1, 5);
    expect(relativeLuminance('#000000')).toBe(0);
  });

  it('is symmetric', () => {
    expect(contrastRatio('#3f6fa3', '#ffffff')).toBeCloseTo(contrastRatio('#ffffff', '#3f6fa3'), 10);
  });

  it('rejects malformed colors', () => {
    expect(() => parseHexColor('red')).toThrowError(/Invalid color/);
    expect(() => parseHexColor('#fff')).toThrowError(/Invalid color/);
  });
});

describe('MACARON_THEMES', () => {
  for (const name of THEMES) {
    const theme = MACARON_THEMES[name];

    describe(name, () => {
      it('primary is readable as text on the container background (AA 4.5:1)', () => {
        expect(contrastRatio(theme.primary, theme.bgContainer)).toBeGreaterThanOrEqual(4.5);
      });

      it('primary button text is readable (AA 4.5:1)', () => {
        expect(contrastRatio(theme.onPrimary, theme.primary)).toBeGreaterThanOrEqual(4.5);
      });

      it('body and secondary text are readable (AA 4.5:1)', () => {
        expect(contrastRatio(theme.text, theme.bgBody)).toBeGreaterThanOrEqual(4.5);
        expect(contrastRatio(theme.textSecondary, theme.bgContainer)).toBeGreaterThanOrEqual(4.5);
      });

      it('up / down indicators are readable (AA 4.5:1)', () => {
        expect(contrastRatio(theme.up, theme.bgContainer)).toBeGreaterThanOrEqual(4.5);
        expect(contrastRatio(theme.down, theme.bgContainer)).toBeGreaterThanOrEqual(4.5);
      });

      it('status tag text is readable on every status color (AA 4.5:1)', () => {
        for (const color of Object.values(theme.status)) {
          expect(contrastRatio(theme.statusText, color)).toBeGreaterThanOrEqual(4.5);
        }
      });

      it('chart palette has 8 distinct colors', () => {
        expect(theme.chart).toHaveLength(8);
        expect(new Set(theme.chart.map((c) => c.toLowerCase())).size).toBe(8);
      });

      it('chart colors stand out from the background (non-text 1.8:1 light / 3:1 dark)', () => {
        const minimum = name === 'light' ? 1.8 : 3;
        for (const color of theme.chart) {
          expect(contrastRatio(color, theme.bgContainer)).toBeGreaterThanOrEqual(minimum);
        }
      });
    });
  }
});
