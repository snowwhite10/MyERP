import { describe, expect, it } from 'vitest';
import { duplicateSerialAcrossLines, serialsFromText, trackingError } from './tracking';

describe('inventory tracking input', () => {
  it('compares lot quantities exactly at six decimal places', () => {
    const rows = [{ lotNo: ' L1 ', quantity: '0.100001' }, { lotNo: 'L2', quantity: '0.200002' }];
    expect(trackingError('Lot', '0.300003', rows, true)).toBeNull();
    expect(trackingError('Lot', '0.300004', rows, true)).toBe('errors.INV.TRACKING.REQUIRED');
  });
  it('parses pasted serials, rejects duplicates and fractional unit counts', () => {
    expect(serialsFromText(' S1\r\n\nS2 ')).toEqual([{ serialNo: 'S1', quantity: '1' }, { serialNo: 'S2', quantity: '1' }]);
    expect(trackingError('Serial', '2', serialsFromText('S1\ns1'), true)).toBe('inv.tracking.duplicate');
    expect(trackingError('Serial', '1.5', serialsFromText('S1'), true)).toBe('errors.INV.TRACKING.REQUIRED');
  });
  it('checks mode and lot dates for the posting direction', () => {
    const rows = [{ lotNo: 'L1', quantity: '1', manufactureDate: '2026-10-14', expiryDate: '2026-10-13' }];
    expect(trackingError('Lot', '1', rows, true)).toBe('inv.tracking.dateOrder');
    expect(trackingError('Lot', '1', rows, false)).toBe('errors.INV.TRACKING.MODE_MISMATCH');
    expect(trackingError('None', '1', rows, true)).toBe('errors.INV.TRACKING.MODE_MISMATCH');
  });
  it('rejects a repeated serial on another line of the same item', () => {
    expect(duplicateSerialAcrossLines([{ itemId: 1, tracking: serialsFromText('S1') },
      { itemId: 1, tracking: serialsFromText('s1') }])).toBe(true);
    expect(duplicateSerialAcrossLines([{ itemId: 1, tracking: serialsFromText('S1') },
      { itemId: 2, tracking: serialsFromText('S1') }])).toBe(false);
  });
});
