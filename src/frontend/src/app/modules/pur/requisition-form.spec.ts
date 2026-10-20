import { requisitionFieldErrors } from './requisition-form';

describe('requisition form validation', () => {
  it('reports each invalid header and line without sending a partial document', () => {
    const errors = requisitionFieldErrors({ businessDate: '2026-02-29', remark: 'x'.repeat(501), lines: [
      { itemId: null, requiredDate: '2026-02-30', quantity: '1.1234567', remark: '' },
    ] });
    expect(errors).toEqual(['businessDate', 'remark', 'lines.0.itemId',
      'lines.0.requiredDate', 'lines.0.quantity']);
  });

  it('accepts leap day, six decimal places and repeated items', () => {
    expect(requisitionFieldErrors({ businessDate: '2028-02-29', remark: '', lines: [
      { itemId: 4, requiredDate: '2028-03-01', quantity: '0.000001', remark: '' },
      { itemId: 4, requiredDate: '2028-03-02', quantity: '9999999999999.999999', remark: '' },
    ] })).toEqual([]);
  });
});
