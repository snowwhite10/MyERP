import { HttpErrorResponse } from '@angular/common/http';
import { disassemblyValidationDetails } from './disassembly-validation';

describe('disassembly validation', () => {
  const translate = (key: string, params?: { number: number }) => `${key}${params ? ` ${params.number}` : ''}`;

  it('labels component weight errors by row and field', () => {
    const error = new HttpErrorResponse({ status: 400, error: { errors: {
      'components[1].allocationWeight': ['Component allocation weight must be positive and have at most 6 decimals.']
    } } });
    expect(disassemblyValidationDetails(error, translate)).toEqual([
      'inv.disassembly.componentLine 2 · inv.disassembly.allocationWeightinv.disassembly.validationSeparatorinv.disassembly.validation.weightInvalid'
    ]);
  });

  it('limits untrusted server validation text and ignores nonvalidation errors', () => {
    const error = new HttpErrorResponse({ status: 400, error: { errors: { 'components[0].quantity': ['x'.repeat(600)] } } });
    expect(disassemblyValidationDetails(error, translate)[0].length).toBeLessThan(600);
    expect(disassemblyValidationDetails(new HttpErrorResponse({ status: 500, error: {} }), translate)).toEqual([]);
  });
});
