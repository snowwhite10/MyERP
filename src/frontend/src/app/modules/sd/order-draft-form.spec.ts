import { OrderDraftForm, orderDraftJson, validateOrderDraft } from './order-draft-form';

const valid = (): OrderDraftForm => ({ customerId: 7, businessDate: '2028-02-29',
  currencyCode: 'USD', remark: '', lines: [{ lineNo: 1, itemId: 12,
    quantity: '1.000001', unitPrice: '0.000001', discountRate: '100.0000', taxRateId: 4 }] });

describe('SD-ORDER-UI-008 exact manual draft', () => {
  it('accepts a leap day and keeps six-place decimals as JSON number tokens', () => {
    const draft = valid();
    expect(validateOrderDraft(draft)).toEqual({});
    expect(orderDraftJson(draft)).toContain('"quantity":1.000001');
    expect(orderDraftJson(draft)).toContain('"unitPrice":0.000001');
  });

  it('rejects missing references, impossible date, long remark and empty lines', () => {
    const draft = valid();
    draft.customerId = null; draft.businessDate = '2027-02-29';
    draft.currencyCode = 'usd'; draft.remark = 'x'.repeat(501); draft.lines = [];
    expect(validateOrderDraft(draft)).toEqual({ customerId: 'required',
      businessDate: 'invalidDate', currencyCode: 'currency', remark: 'tooLong', lines: 'count' });
    expect(() => orderDraftJson(draft)).toThrow();
  });

  it('rejects duplicate line numbers and invalid decimal scales and bounds', () => {
    const draft = valid();
    draft.lines = [
      { lineNo: 1, itemId: 0, quantity: '0', unitPrice: '-1', discountRate: '100.0001', taxRateId: 0 },
      { lineNo: 1, itemId: 1, quantity: '1.0000001', unitPrice: '10000000000000', discountRate: '0', taxRateId: 1 },
    ];
    expect(validateOrderDraft(draft)).toEqual({
      'lines[0].itemId': 'required', 'lines[0].quantity': 'decimal',
      'lines[0].unitPrice': 'decimal', 'lines[0].discountRate': 'decimal',
      'lines[0].taxRateId': 'required', 'lines[1].lineNo': 'duplicate',
      'lines[1].quantity': 'decimal', 'lines[1].unitPrice': 'decimal',
    });
  });

  it('rejects more than 1000 lines without changing the draft', () => {
    const draft = valid();
    draft.lines = Array.from({ length: 1001 }, (_, index) => ({ ...valid().lines[0], lineNo: index + 1 }));
    expect(validateOrderDraft(draft)).toEqual({ lines: 'count' });
    expect(draft.lines.length).toBe(1001);
  });
});
