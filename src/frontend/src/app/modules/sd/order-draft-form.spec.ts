import { matchesOrderReceipt, orderEditDraft, OrderDraftForm, orderDraftJson, validateOrderDraft } from './order-draft-form';

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

describe('SD-ORDER-EDIT-001 saved draft mapping', () => {
  const saved = () => ({ id: 8, version: 2, status: 'Rejected', sourceQuotationId: null,
    customerId: 7, businessDate: '2028-02-29', currencyCode: 'USD', remark: null,
    lines: [{ lineNo: 1, itemId: 12, quantity: '1.000001', unitPrice: '0.000001',
      discountRate: '0.1234', taxRateId: 4, deliveredQuantity: 0, pendingQuantity: 0 }] });

  it('maps saved exact decimals and versioned Rejected input without rounding', () => {
    const mapped = orderEditDraft(saved());
    expect(mapped?.version).toBe(2);
    expect(mapped?.draft.lines[0].quantity).toBe('1.000001');
    expect(mapped?.draft.lines[0].unitPrice).toBe('0.000001');
    expect(mapped?.draft.lines[0].discountRate).toBe('0.1234');
    expect(validateOrderDraft(mapped!.draft)).toEqual({});
  });

  it('does not offer a mutable draft for a converted, delivered or immutable order', () => {
    expect(orderEditDraft({ ...saved(), status: 'Pending' })).toBeNull();
    expect(orderEditDraft({ ...saved(), sourceQuotationId: 3 })).toBeNull();
    expect(orderEditDraft({ ...saved(), lines: [{ ...saved().lines[0], deliveredQuantity: 1 }] })).toBeNull();
    expect(orderEditDraft({ ...saved(), version: 0 })).toBeNull();
  });
});

describe('SD-ORDER-UI-011 saved receipt identity', () => {
  const receipt = () => ({ id: 81, status: 'Draft', documentNo: 'SO-0081', customerId: 7,
    businessDate: '2028-02-29', currencyCode: 'USD', sourceQuotationId: null,
    lines: [{ lineNo: 1, itemId: 12 }] });

  it('accepts a manual receipt only when customer, date, currency and line identities match', () => {
    expect(matchesOrderReceipt(receipt(), { draft: valid() })).toBe(true);
    expect(matchesOrderReceipt({ ...receipt(), customerId: 8 }, { draft: valid() })).toBe(false);
    expect(matchesOrderReceipt({ ...receipt(), lines: [{ lineNo: 1, itemId: 99 }] }, { draft: valid() })).toBe(false);
    expect(matchesOrderReceipt({ ...receipt(), businessDate: '2028-03-01' }, { draft: valid() })).toBe(false);
  });

  it('requires the selected source on a converted order', () => {
    expect(matchesOrderReceipt({ ...receipt(), sourceQuotationId: 17 }, { quotationId: 17 })).toBe(true);
    expect(matchesOrderReceipt({ ...receipt(), sourceQuotationId: 18 }, { quotationId: 17 })).toBe(false);
  });
});

describe('SD-OPRICE-UI-U01 order pricing hints and reasons (WU-SD-005b2b)', () => {
  it('sends the quote pair only together and a trimmed reason only when entered', () => {
    const draft = valid();
    draft.lines[0] = { ...draft.lines[0], pricingRuleId: 5, pricingRuleVersion: 3, overrideReason: '  ' };
    expect(orderDraftJson(draft)).toContain('"taxRateId":4,"pricingRuleId":5,"pricingRuleVersion":3}');
    expect(orderDraftJson(draft)).not.toContain('overrideReason');
    draft.lines[0] = { ...draft.lines[0], pricingRuleVersion: null, overrideReason: ' Launch "A" ' };
    expect(orderDraftJson(draft)).toContain('"taxRateId":4,"overrideReason":"Launch \\"A\\""}');
    expect(orderDraftJson(draft)).not.toContain('pricingRuleId');
  });

  it('requires a reason only where flagged and refuses one over 200 characters', () => {
    const draft = valid();
    expect(validateOrderDraft(draft, [true])).toEqual({ 'lines[0].overrideReason': 'required' });
    draft.lines[0].overrideReason = 'x'.repeat(201);
    expect(validateOrderDraft(draft)).toEqual({ 'lines[0].overrideReason': 'tooLong' });
    draft.lines[0].overrideReason = 'x'.repeat(200);
    expect(validateOrderDraft(draft, [true])).toEqual({});
  });

  it('maps saved provenance: rule pair only for Rule/Override with a rule, reason only for Override', () => {
    const base = { id: 8, version: 2, status: 'Draft', sourceQuotationId: null, customerId: 7, businessDate: '2028-02-29',
      currencyCode: 'USD', remark: null };
    const line = { lineNo: 1, itemId: 12, quantity: '1', unitPrice: '5', discountRate: '0', taxRateId: 4,
      deliveredQuantity: 0, pendingQuantity: 0 };
    const rule = orderEditDraft({ ...base, lines: [{ ...line, pricingMode: 'Rule', pricingRuleId: 5, pricingRuleVersion: 3,
      overrideReason: null }] });
    expect(rule?.draft.lines[0]).toMatchObject({ pricingRuleId: 5, pricingRuleVersion: 3, overrideReason: '' });
    const override = orderEditDraft({ ...base, lines: [{ ...line, pricingMode: 'Override', pricingRuleId: null,
      pricingRuleVersion: null, overrideReason: 'Clearance' }] });
    expect(override?.draft.lines[0]).toMatchObject({ pricingRuleId: null, pricingRuleVersion: null, overrideReason: 'Clearance' });
    const legacy = orderEditDraft({ ...base, lines: [line] });
    expect(legacy?.draft.lines[0]).toMatchObject({ pricingRuleId: null, pricingRuleVersion: null, overrideReason: '' });
  });
});
