import { describe, expect, it } from 'vitest';
import { paymentRule, validPaymentRule } from './payment-term.service';

describe('MD-PAY-008 payment rule form', () => {
  it('keeps only fields for the selected rule', () => {
    expect(paymentRule({ dueType: 'NetDays', days: 30, closingDay: 20, months: 1, paymentDay: 25 }))
      .toEqual({ dueType: 'NetDays', days: 30, closingDay: null, months: null, paymentDay: null });
    expect(paymentRule({ dueType: 'ClosingDays', days: 30, closingDay: 20, months: 1, paymentDay: 25 }))
      .toEqual({ dueType: 'ClosingDays', days: 30, closingDay: 20, months: null, paymentDay: null });
    expect(paymentRule({ dueType: 'ClosingMonthDay', days: 30, closingDay: 20, months: 1, paymentDay: 25 }))
      .toEqual({ dueType: 'ClosingMonthDay', days: null, closingDay: 20, months: 1, paymentDay: 25 });
  });
  it('enforces server boundaries including same-month ordering', () => {
    expect(validPaymentRule({ dueType: 'ClosingMonthDay', days: null, closingDay: 20, months: 0, paymentDay: 20 })).toBe(false);
    expect(validPaymentRule({ dueType: 'ClosingMonthDay', days: null, closingDay: 20, months: 0, paymentDay: 31 })).toBe(true);
    expect(validPaymentRule({ dueType: 'NetDays', days: 366, closingDay: null, months: null, paymentDay: null })).toBe(false);
    expect(validPaymentRule({ dueType: 'ClosingDays', days: 0, closingDay: 31, months: null, paymentDay: null })).toBe(true);
  });
});
