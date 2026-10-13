import { ItemTrackingMode } from '../md/item.service';

export interface TrackingDraft {
  lotNo?: string | null;
  serialNo?: string | null;
  quantity: string;
  manufactureDate?: string | null;
  expiryDate?: string | null;
}

export interface TrackingResponse {
  lotNo: string | null;
  serialNo: string | null;
  quantity: number;
  manufactureDate: string | null;
  expiryDate: string | null;
}

/** Base units have six decimal places. Integer arithmetic avoids browser floating point rounding. */
function units(value: string): bigint | null {
  if (!/^(?:0|[1-9]\d*)(?:\.\d{1,6})?$/.test(value.trim())) return null;
  const [whole, fraction = ''] = value.trim().split('.');
  return BigInt(whole) * 1_000_000n + BigInt(fraction.padEnd(6, '0'));
}

export function serialsFromText(text: string): TrackingDraft[] {
  return text.split(/\r?\n/).map(value => value.trim()).filter(Boolean)
    .map(serialNo => ({ serialNo, quantity: '1' }));
}

export function trackingError(mode: ItemTrackingMode, quantity: string, tracking: TrackingDraft[], incoming: boolean): string | null {
  if (mode === 'None') return tracking.length ? 'errors.INV.TRACKING.MODE_MISMATCH' : null;
  const total = units(quantity);
  if (total === null || total === 0n || !tracking.length) return 'errors.INV.TRACKING.REQUIRED';
  let sum = 0n;
  const numbers = new Set<string>();
  for (const entry of tracking) {
    const lot = entry.lotNo?.trim();
    const serial = entry.serialNo?.trim();
    if (mode === 'Lot' ? !lot || !!serial || lot.length > 50 : !serial || !!lot || serial.length > 100)
      return 'errors.INV.TRACKING.MODE_MISMATCH';
    const number = (lot ?? serial)!.toUpperCase();
    if (numbers.has(number)) return 'inv.tracking.duplicate';
    numbers.add(number);
    const amount = units(entry.quantity);
    if (amount === null || amount === 0n || (mode === 'Serial' && amount !== 1_000_000n))
      return 'errors.INV.TRACKING.REQUIRED';
    if ((!incoming || mode === 'Serial') && (entry.manufactureDate || entry.expiryDate))
      return 'errors.INV.TRACKING.MODE_MISMATCH';
    if (entry.manufactureDate && entry.expiryDate && entry.expiryDate < entry.manufactureDate)
      return 'inv.tracking.dateOrder';
    sum += amount;
  }
  return sum === total ? null : 'errors.INV.TRACKING.REQUIRED';
}

export function trackingBody(tracking: TrackingDraft[] | undefined): TrackingDraft[] {
  return (tracking ?? []).map(entry => ({ lotNo: entry.lotNo?.trim() || null, serialNo: entry.serialNo?.trim() || null,
    quantity: entry.quantity.trim(), manufactureDate: entry.manufactureDate || null, expiryDate: entry.expiryDate || null }));
}

export function trackingLabel(tracking: TrackingResponse[] | undefined): string {
  return (tracking ?? []).map(entry => `${entry.lotNo ?? entry.serialNo} × ${entry.quantity}` +
    (entry.manufactureDate ? ` (${entry.manufactureDate}` + (entry.expiryDate ? ` – ${entry.expiryDate})` : ')') :
      entry.expiryDate ? ` (${entry.expiryDate})` : '')).join('; ');
}

export function duplicateSerialAcrossLines(lines: readonly { itemId: number | null; tracking?: TrackingDraft[] }[]): boolean {
  const seen = new Set<string>();
  for (const line of lines) for (const entry of line.tracking ?? []) {
    if (!entry.serialNo?.trim()) continue;
    const key = `${line.itemId}:${entry.serialNo.trim().toUpperCase()}`;
    if (seen.has(key)) return true;
    seen.add(key);
  }
  return false;
}
