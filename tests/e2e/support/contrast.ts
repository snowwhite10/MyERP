import { expect, type Locator } from '@playwright/test';

/**
 * WCAG 2.1 contrast of an element's rendered text color against the first opaque ancestor background
 * (https://www.w3.org/TR/WCAG21/#dfn-contrast-ratio). Used by E2E to keep error text readable in light and dark themes
 * (WU-UI-ERRCOLOR: `--erp-status-error` is a tag background, not a text color).
 */
export async function textContrast(locator: Locator): Promise<number> {
  return locator.evaluate((element) => {
    const rgb = (value: string): number[] | null => {
      const match = /rgba?\(([^)]+)\)/.exec(value);
      if (!match) return null;
      const parts = match[1].split(/[\s,/]+/).filter(Boolean).map(Number);
      return parts.length > 3 && parts[3] === 0 ? null : parts.slice(0, 3);
    };
    const luminance = ([r, g, b]: number[]): number => {
      const linear = (c: number) => { const s = c / 255; return s <= 0.04045 ? s / 12.92 : Math.pow((s + 0.055) / 1.055, 2.4); };
      return 0.2126 * linear(r) + 0.7152 * linear(g) + 0.0722 * linear(b);
    };
    const text = rgb(getComputedStyle(element).color) ?? [0, 0, 0];
    let background: number[] | null = null;
    for (let node: Element | null = element; node && !background; node = node.parentElement) {
      background = rgb(getComputedStyle(node).backgroundColor);
    }
    const [a, b] = [luminance(text), luminance(background ?? [255, 255, 255])];
    return (Math.max(a, b) + 0.05) / (Math.min(a, b) + 0.05);
  });
}

/** Asserts every matched element is readable text (AA 4.5:1 by default). */
export async function expectReadableText(locator: Locator, minimum = 4.5): Promise<void> {
  const count = await locator.count();
  expect(count, 'no element to check').toBeGreaterThan(0);
  for (let index = 0; index < count; index++) {
    const item = locator.nth(index);
    const ratio = await textContrast(item);
    expect(ratio, `contrast of "${(await item.textContent())?.trim()}"`).toBeGreaterThanOrEqual(minimum);
  }
}
