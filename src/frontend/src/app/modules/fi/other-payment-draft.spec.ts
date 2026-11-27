import { describe, expect, it } from 'vitest';
import { otherPaymentIssues } from './other-payment-draft';

const draft = { payeeDescription: 'Power', expenseCategoryId: 3, bankAccountId: 9,
  bankReference: 'WD-1', businessDate: '2026-11-27', amount: '12.3400', remark: '' };

describe('FI-OP-UI-008 draft validation', () => {
  it('accepts exact 19,4 amount and a real leap day', () => {
    expect(otherPaymentIssues({ ...draft, amount: '999999999999999.9999', businessDate: '2028-02-29' })).toEqual({});
  });
  it('names every invalid field without rounding or parsing as a JavaScript number', () => {
    expect(Object.keys(otherPaymentIssues({ ...draft, payeeDescription: '', expenseCategoryId: 0, bankAccountId: 0,
      bankReference: '', businessDate: '2026-02-29', amount: '1.12345', remark: 'x'.repeat(501) })).sort())
      .toEqual(['amount', 'bankAccountId', 'bankReference', 'businessDate', 'expenseCategoryId', 'payeeDescription', 'remark']);
  });
  it('rejects zero, negative, separators, extra integer digit and overlength text', () => {
    for (const amount of ['0', '-1', '1,000', '1000000000000000', '1.12345']) {
      expect(otherPaymentIssues({ ...draft, amount }).amount).toBeTruthy();
    }
    expect(otherPaymentIssues({ ...draft, payeeDescription: 'x'.repeat(201), bankReference: 'x'.repeat(51) }))
      .toMatchObject({ payeeDescription: expect.any(String), bankReference: expect.any(String) });
  });
});
