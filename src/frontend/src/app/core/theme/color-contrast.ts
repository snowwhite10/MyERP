/** WCAG 2.1 相對亮度與對比度計算（https://www.w3.org/TR/WCAG21/#dfn-contrast-ratio）。 */

const HEX_COLOR = /^#([0-9a-f]{6})$/i;

export function parseHexColor(hex: string): [number, number, number] {
  const match = HEX_COLOR.exec(hex);
  if (!match) {
    throw new Error(`Invalid color "${hex}", expected #rrggbb`);
  }
  const value = parseInt(match[1], 16);
  return [(value >> 16) & 0xff, (value >> 8) & 0xff, value & 0xff];
}

function channelToLinear(channel: number): number {
  const srgb = channel / 255;
  return srgb <= 0.04045 ? srgb / 12.92 : Math.pow((srgb + 0.055) / 1.055, 2.4);
}

export function relativeLuminance(hex: string): number {
  const [r, g, b] = parseHexColor(hex).map(channelToLinear);
  return 0.2126 * r + 0.7152 * g + 0.0722 * b;
}

export function contrastRatio(foreground: string, background: string): number {
  const a = relativeLuminance(foreground);
  const b = relativeLuminance(background);
  const [lighter, darker] = a > b ? [a, b] : [b, a];
  return (lighter + 0.05) / (darker + 0.05);
}
