/**
 * Exact decimal text for JSON money fields. `decimal(19,4)` can exceed the 15–17 significant digits of a JavaScript
 * number, so the raw digits are taken from the response text before `JSON.parse` and never pass through a double.
 */

const PLAIN_DECIMAL = /^-?\d+(\.\d+)?$/;

/**
 * Parses JSON text, turning plain numeric values of the named keys into strings with the exact server digits.
 * Matches inside JSON string values cannot occur: an embedded quote there is escaped (`\"`), so `"key":` never matches.
 */
export function parseJsonWithDecimalText<T>(text: string, keys: readonly string[]): T {
  if (!keys.length) return JSON.parse(text) as T;
  const names = keys.map(key => key.replace(/[^A-Za-z0-9_]/g, '')).join('|');
  const pattern = new RegExp(`"(${names})"(\\s*):(\\s*)(-?\\d+(?:\\.\\d+)?)(?=\\s*[,}\\]])`, 'g');
  return JSON.parse(text.replace(pattern, '"$1"$2:$3"$4"')) as T;
}

/** Adds locale digit grouping to the integer part without floating point; trailing fraction zeros are dropped. */
export function formatDecimalText(amountText: string, locale?: string): string {
  const match = /^(-?)(\d+)(?:\.(\d+))?$/.exec(amountText);
  if (!match) return amountText;
  const fraction = (match[3] ?? '').replace(/0+$/, '');
  const integer = BigInt(match[2]).toLocaleString(locale);
  return `${match[1]}${integer}${fraction ? '.' + fraction : ''}`;
}

/** True for a plain decimal text equal to zero (`0`, `0.0000`, `-0`). */
export function isZeroDecimalText(amountText: string): boolean {
  return PLAIN_DECIMAL.test(amountText) && /^-?0+(\.0+)?$/.test(amountText);
}
