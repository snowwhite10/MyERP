import { validateQuotationDraft, QuotationDraftForm } from './quotation-draft-form';

const valid = (): QuotationDraftForm => ({
  customerId: 7, businessDate: '2028-02-29', expiryDate: '2028-03-01', remark: '',
  lines: [{ lineNo: 1, itemId: 12, quantity: '1.000001', unitPrice: '0.000001',
    discountRate: '100.0000', currencyCode: 'USD', taxRateId: 4 }],
});

describe('SD-QUOTE-FORM-014 draft form validation', () => {
  it('accepts a leap day and exact decimal text without converting through Number', () => {
    const draft = valid();
    expect(validateQuotationDraft(draft)).toEqual({});
    expect(draft.lines[0].quantity).toBe('1.000001');
  });

  it('rejects missing customer, impossible or reversed dates, long remark and no lines', () => {
    const draft = valid();
    draft.customerId = null;
    draft.businessDate = '2027-02-29';
    draft.expiryDate = '2027-02-30';
    draft.remark = 'x'.repeat(501);
    draft.lines = [];
    expect(validateQuotationDraft(draft)).toEqual({
      customerId: 'required', businessDate: 'invalidDate', expiryDate: 'invalidDate',
      remark: 'tooLong', lines: 'count',
    });
  });

  it('rejects an expiry date before a valid business date', () => {
    const draft = valid();
    draft.expiryDate = '2028-02-28';
    expect(validateQuotationDraft(draft)).toEqual({ expiryDate: 'invalidDate' });
  });

  it('checks line references, duplicate numbers, currency and exact precision', () => {
    const draft = valid();
    draft.lines = [
      { lineNo: 1, itemId: 0, quantity: '0', unitPrice: '-1', discountRate: '100.0001', currencyCode: 'usd', taxRateId: 0 },
      { lineNo: 1, itemId: 1, quantity: '1.0000001', unitPrice: '10000000000000', discountRate: '0', currencyCode: 'USD', taxRateId: 1 },
    ];
    expect(validateQuotationDraft(draft)).toEqual({
      'lines[0].itemId': 'required', 'lines[0].quantity': 'decimal',
      'lines[0].unitPrice': 'decimal', 'lines[0].discountRate': 'decimal',
      'lines[0].currencyCode': 'currency', 'lines[0].taxRateId': 'required',
      'lines[1].lineNo': 'duplicate', 'lines[1].quantity': 'decimal',
      'lines[1].unitPrice': 'decimal',
    });
  });

  it('rejects 1001 lines and keeps input unchanged', () => {
    const draft = valid();
    draft.lines = Array.from({ length: 1001 }, (_, index) => ({ ...valid().lines[0], lineNo: index + 1 }));
    expect(validateQuotationDraft(draft)).toEqual({ lines: 'count' });
    expect(draft.lines.length).toBe(1001);
  });

  it('accepts the largest six-place values below the decimal(19,6) limit', () => {
    const draft = valid();
    draft.lines[0].quantity = '9999999999999.999999';
    draft.lines[0].unitPrice = '9999999999999.999999';
    draft.lines[0].discountRate = '0.0000';
    expect(validateQuotationDraft(draft)).toEqual({});
  });
});
