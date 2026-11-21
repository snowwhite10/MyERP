import { HttpErrorResponse } from '@angular/common/http';
import { TimeoutError } from 'rxjs';
import { emptyPriceRuleDraft, PriceRuleDraft, priceRuleDraftFromRule, priceRuleJson, priceRuleSaveFailure,
  validatePriceRuleDraft } from './price-rule-form';
import { SalesPriceRule } from './price-rule.service';

const valid = (extra: Partial<PriceRuleDraft> = {}): PriceRuleDraft => ({ ...emptyPriceRuleDraft(), itemId: 7,
  currencyCode: 'TWD', validFrom: '2028-01-01', validTo: '2028-12-31', unitPrice: '1234.5', discountCeiling: '10', ...extra });
const failure = (status: number, body: unknown) => new HttpErrorResponse({ status, error: body });

describe('SD-PRULE-FORM price rule draft rules (WU-SD-005b1b2)', () => {
  it('SD-PRULE-FORM-001 accepts a typical default and customer rule', () => {
    expect(validatePriceRuleDraft(valid())).toEqual({});
    expect(validatePriceRuleDraft(valid({ scope: 'customer', customerId: 9 }))).toEqual({});
  });

  it('SD-PRULE-FORM-002 empty draft lists every required field at once', () => {
    const issues = validatePriceRuleDraft({ ...emptyPriceRuleDraft(), scope: 'customer', discountCeiling: '' });
    expect(Object.keys(issues).sort()).toEqual(['currencyCode', 'customerId', 'discountCeiling', 'itemId', 'unitPrice',
      'validFrom', 'validTo']);
    expect(issues.unitPrice).toBe('required');
    expect(validatePriceRuleDraft(emptyPriceRuleDraft()).discountCeiling).toBeUndefined();
  });

  it('SD-PRULE-FORM-003 price and ceiling boundaries match the server (DECIMAL(19,6), 0–100 with 4 places)', () => {
    expect(validatePriceRuleDraft(valid({ unitPrice: '0' }))).toEqual({});
    expect(validatePriceRuleDraft(valid({ unitPrice: '9999999999999.999999' }))).toEqual({});
    expect(validatePriceRuleDraft(valid({ unitPrice: '10000000000000' })).unitPrice).toBe('range');
    expect(validatePriceRuleDraft(valid({ unitPrice: '1.1234567' })).unitPrice).toBe('scale');
    expect(validatePriceRuleDraft(valid({ unitPrice: '-1' })).unitPrice).toBe('format');
    expect(validatePriceRuleDraft(valid({ unitPrice: '1e3' })).unitPrice).toBe('format');
    expect(validatePriceRuleDraft(valid({ discountCeiling: '100.0000' }))).toEqual({});
    expect(validatePriceRuleDraft(valid({ discountCeiling: '0' }))).toEqual({});
    expect(validatePriceRuleDraft(valid({ discountCeiling: '100.0001' })).discountCeiling).toBe('range');
    expect(validatePriceRuleDraft(valid({ discountCeiling: '12.34567' })).discountCeiling).toBe('scale');
  });

  it('SD-PRULE-FORM-004 dates: real calendar days, same day allowed, end before start refused', () => {
    expect(validatePriceRuleDraft(valid({ validFrom: '2028-02-29', validTo: '2028-02-29' }))).toEqual({});
    expect(validatePriceRuleDraft(valid({ validFrom: '2027-02-29' })).validFrom).toBe('format');
    expect(validatePriceRuleDraft(valid({ validFrom: '2028-12-31', validTo: '2028-01-01' })).validTo).toBe('beforeFrom');
    expect(validatePriceRuleDraft(valid({ currencyCode: 'tw' })).currencyCode).toBe('required');
  });

  it('SD-PRULE-FORM-005 JSON keeps exact digits, strips leading zeros and sends null customer for a default rule', () => {
    const json = priceRuleJson(valid({ unitPrice: '0012345678901.123456', discountCeiling: '012.5', customerId: 9 }));
    expect(json).toBe('{"itemId":7,"customerId":null,"currencyCode":"TWD","validFrom":"2028-01-01",' +
      '"validTo":"2028-12-31","unitPrice":12345678901.123456,"discountCeiling":12.5}');
    expect(priceRuleJson(valid({ scope: 'customer', customerId: 9 }), 4)).toContain('"customerId":9,');
    expect(priceRuleJson(valid(), 4)).toMatch(/,"version":4}$/);
    expect(() => priceRuleJson(valid({ unitPrice: 'x' }))).toThrow();
    expect(() => priceRuleJson(valid(), 0)).toThrow();
  });

  it('SD-PRULE-FORM-006 draft from a saved rule keeps server digits and scope', () => {
    const rule = { itemId: 7, customerId: 9, currencyCode: 'USD', validFrom: '2028-01-01', validTo: '2028-03-31',
      unitPrice: '12.500000', discountCeiling: '5.0000' } as SalesPriceRule;
    expect(priceRuleDraftFromRule(rule)).toEqual({ itemId: 7, scope: 'customer', customerId: 9, currencyCode: 'USD',
      validFrom: '2028-01-01', validTo: '2028-03-31', unitPrice: '12.500000', discountCeiling: '5.0000' });
    expect(priceRuleDraftFromRule({ ...rule, customerId: null }).scope).toBe('default');
  });

  it('SD-PRULE-FORM-007 save failures map to named outcomes', () => {
    expect(priceRuleSaveFailure(failure(409, { errorCode: 'SAL.PRICING.RULE_OVERLAP', conflictingRuleIds: [12, 3] })))
      .toEqual({ kind: 'overlap', conflictingRuleIds: [12, 3] });
    expect(priceRuleSaveFailure(failure(409, { errorCode: 'SAL.PRICING.RULE_LOCK_TIMEOUT' })).kind).toBe('busy');
    expect(priceRuleSaveFailure(failure(409, { errorCode: 'COMMON.CONCURRENCY.CONFLICT' })).kind).toBe('stale');
    expect(priceRuleSaveFailure(failure(422, { errorCode: 'SAL.PRICING.RULE_INACTIVE' })).kind).toBe('inactive');
    expect(priceRuleSaveFailure(failure(404, { errorCode: 'SAL.PRICE_RULE.NOT_FOUND' })).kind).toBe('missing');
    expect(priceRuleSaveFailure(failure(422, { errorCode: 'MD.ITEM.INACTIVE' })))
      .toEqual({ kind: 'reference', field: 'itemId', key: 'sd.priceRuleForm.refused.itemInactive' });
    expect(priceRuleSaveFailure(failure(404, { errorCode: 'MD.CUSTOMER.NOT_FOUND' })))
      .toEqual({ kind: 'reference', field: 'customerId', key: 'sd.priceRuleForm.refused.customerMissing' });
    expect(priceRuleSaveFailure(failure(422, { errorCode: 'MD.CURRENCY.INACTIVE' })))
      .toEqual({ kind: 'reference', field: 'currencyCode', key: 'sd.priceRuleForm.refused.currencyInactive' });
    expect(priceRuleSaveFailure(failure(400, { errors: { unitPrice: ['x'], validTo: ['y'], other: ['z'] } })))
      .toEqual({ kind: 'validation', fields: ['unitPrice', 'validTo'], unknown: 1 });
    expect(priceRuleSaveFailure(failure(500, { traceId: 't' })).kind).toBe('unknown');
    expect(priceRuleSaveFailure(failure(0, null)).kind).toBe('unknown');
    expect(priceRuleSaveFailure(new TimeoutError()).kind).toBe('unknown');
    expect(priceRuleSaveFailure(failure(422, { errorCode: 'X.Y' })).kind).toBe('rejected');
    expect(priceRuleSaveFailure(failure(403, { errorCode: 'COMMON.PERMISSION.DENIED' })).kind).toBe('denied');
    expect(priceRuleSaveFailure(failure(409, { errorCode: 'SAL.PRICING.RULE_OVERLAP', conflictingRuleIds: 'bad' })))
      .toEqual({ kind: 'overlap', conflictingRuleIds: [] });
  });
});
