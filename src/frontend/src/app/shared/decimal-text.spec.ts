import { formatDecimalText, isZeroDecimalText, parseJsonWithDecimalText } from './decimal-text';

describe('decimal text helpers', () => {
  it('keeps every digit of decimal(19,4) values beyond double precision', () => {
    const text = '{"items":[{"id":1,"totalDebit":999999999999999.9999,"totalCredit": 12.5000,"name":"x"}],"debit":0}';
    const parsed = parseJsonWithDecimalText<{ items: { id: number; totalDebit: string; totalCredit: string; name: string }[]; debit: string }>(
      text, ['totalDebit', 'totalCredit', 'debit']);
    expect(parsed.items[0].totalDebit).toBe('999999999999999.9999');
    expect(parsed.items[0].totalCredit).toBe('12.5000');
    expect(parsed.items[0].id).toBe(1);
    expect(parsed.debit).toBe('0');
  });

  it('does not touch an escaped key inside a string value or keys not listed', () => {
    const text = '{"accountName":"a \\"debit\\":5 b","credit":7,"sequence":3}';
    const parsed = parseJsonWithDecimalText<{ accountName: string; credit: string; sequence: number }>(text, ['debit', 'credit']);
    expect(parsed.accountName).toBe('a "debit":5 b');
    expect(parsed.credit).toBe('7');
    expect(parsed.sequence).toBe(3);
  });

  it('groups the integer part by locale and drops trailing fraction zeros without floating point', () => {
    expect(formatDecimalText('999999999999999.9999', 'en')).toBe('999,999,999,999,999.9999');
    expect(formatDecimalText('1234567.5000', 'en')).toBe('1,234,567.5');
    expect(formatDecimalText('-1000.0000', 'en')).toBe('-1,000');
    expect(formatDecimalText('abc')).toBe('abc');
  });

  it('recognises zero decimal text only', () => {
    expect(isZeroDecimalText('0')).toBe(true);
    expect(isZeroDecimalText('0.0000')).toBe(true);
    expect(isZeroDecimalText('0.0001')).toBe(false);
    expect(isZeroDecimalText('')).toBe(false);
  });
});
