import { HttpErrorResponse } from '@angular/common/http';
import { compareDecimalText, DeliveryDraftForm, deliveryDraftJson, deliveryErrorLineNo, DeliverySourceOrder, draftFromOrder,
  matchesDeliveryReceipt, validateDeliveryDraft, validDeliveryQuantity } from './delivery-draft-form';

const order: DeliverySourceOrder = { id: 9, documentNo: 'SO1', status: 'Approved', customerCode: 'C1', customerName: 'Acme',
  lines: [
    { id: 21, lineNo: 1, itemCode: 'I1', itemName: 'Bolt', quantity: '10', deliveredQuantity: '4', pendingQuantity: '1', remainingQuantity: '5.000001' },
    { id: 22, lineNo: 2, itemCode: 'I2', itemName: 'Nut', quantity: '3', deliveredQuantity: '3', pendingQuantity: '0', remainingQuantity: '0.000000' },
    { id: 23, lineNo: 3, itemCode: 'I3', itemName: 'Pin', quantity: '9999999999999.999999', deliveredQuantity: '0', pendingQuantity: '0',
      remainingQuantity: '9999999999999.999999' }] };
const ready = (): DeliveryDraftForm => ({ orderId: 9, businessDate: '2028-02-29', lines: [
  { lineNo: 1, orderLineId: 21, warehouseId: 4, locationId: null, quantity: '5.000001' },
  { lineNo: 2, orderLineId: 23, warehouseId: 4, locationId: 12, quantity: '9999999999999.999999' }] });

describe('SD-DEL-CR delivery draft form rules', () => {
  it('SD-DEL-CR-001 prefills one line per order line with a positive balance, keeping exact text and skipping exhausted lines', () => {
    const draft = draftFromOrder(order, '2028-02-29');
    expect(draft.orderId).toBe(9);
    expect(draft.lines.map(line => [line.lineNo, line.orderLineId, line.quantity, line.warehouseId]))
      .toEqual([[1, 21, '5.000001', null], [2, 23, '9999999999999.999999', null]]);
    expect(draftFromOrder({ ...order, lines: [order.lines[1]] }, '').lines).toEqual([]);
    expect(draftFromOrder({ ...order, lines: [{ ...order.lines[0], remainingQuantity: '2.500000' }] }, '').lines[0].quantity).toBe('2.5');
  });

  it('SD-DEL-CR-002 accepts six decimals and 13 integer digits; refuses zero, negatives, seven decimals, 14 digits and exponent text', () => {
    for (const ok of ['1', '0.000001', '9999999999999.999999']) expect(validDeliveryQuantity(ok)).toBe(true);
    for (const bad of ['0', '0.000000', '-1', '1.0000001', '10000000000000', '1e3', ' 1', '01', '']) expect(validDeliveryQuantity(bad)).toBe(false);
    expect(compareDecimalText('5.000001', '5.000000')).toBe(1);
    expect(compareDecimalText('9999999999999.999999', '9999999999999.999999')).toBe(0);
  });

  it('SD-DEL-CR-003 reports order, leap-day date, warehouse, quantity and line-count issues without a request', () => {
    expect(validateDeliveryDraft(ready(), order)).toEqual({});
    const broken: DeliveryDraftForm = { orderId: null, businessDate: '2027-02-29', lines: [
      { lineNo: 1, orderLineId: 21, warehouseId: null, locationId: null, quantity: '0' }] };
    expect(validateDeliveryDraft(broken, order)).toEqual({ orderId: 'required', businessDate: 'invalidDate',
      'lines[0].warehouseId': 'required', 'lines[0].quantity': 'decimal' });
    expect(validateDeliveryDraft({ ...ready(), lines: [] }, order)).toEqual({ lines: 'count' });
    expect(() => deliveryDraftJson(broken)).toThrow();
  });

  it('SD-DEL-CR-004 sums split lines of one order line against its advisory balance with exact six-place arithmetic', () => {
    const split = ready();
    split.lines = [{ lineNo: 1, orderLineId: 21, warehouseId: 4, locationId: null, quantity: '5' },
      { lineNo: 3, orderLineId: 21, warehouseId: 5, locationId: null, quantity: '0.000001' }];
    expect(validateDeliveryDraft(split, order)).toEqual({});
    split.lines[1].quantity = '0.000002';
    expect(validateDeliveryDraft(split, order)).toEqual({ 'lines[0].quantity': 'aboveRemaining', 'lines[1].quantity': 'aboveRemaining' });
    expect(validateDeliveryDraft(split, null)).toEqual({});
  });

  it('SD-DEL-CR-005 writes exact JSON numbers and only draft fields', () => {
    const json = deliveryDraftJson(ready());
    expect(json).toBe('{"orderId":9,"businessDate":"2028-02-29","lines":[' +
      '{"lineNo":1,"orderLineId":21,"warehouseId":4,"locationId":null,"quantity":5.000001},' +
      '{"lineNo":2,"orderLineId":23,"warehouseId":4,"locationId":12,"quantity":9999999999999.999999}]}');
    expect(json).not.toMatch(/remaining|status|invMovement|version/);
  });

  it('SD-DEL-CR-006 reads the 422 lineNo extension only from a positive integer on a 422 response', () => {
    const problem = (status: number, body: object) => new HttpErrorResponse({ status, error: body });
    expect(deliveryErrorLineNo(problem(422, { errorCode: 'SAL.DELIVERY.ORDER_LINE_INVALID', lineNo: 2 }))).toBe(2);
    expect(deliveryErrorLineNo(problem(422, { errorCode: 'SAL.DELIVERY.ORDER_BALANCE_EXCEEDED' }))).toBeNull();
    expect(deliveryErrorLineNo(problem(422, { lineNo: '2' }))).toBeNull();
    expect(deliveryErrorLineNo(problem(400, { lineNo: 2 }))).toBeNull();
    expect(deliveryErrorLineNo(new Error('x'))).toBeNull();
  });

  it('SD-DEL-CR-007 confirms success only when the saved Draft matches every sent line', () => {
    const draft = ready();
    const receipt = { id: 61, documentNo: 'DH1', orderId: 9, businessDate: '2028-02-29', status: 'Draft', lines: [
      { lineNo: 2, orderLineId: 23, warehouseId: 4, locationId: 12, quantity: '9999999999999.999999' },
      { lineNo: 1, orderLineId: 21, warehouseId: 4, locationId: null, quantity: '5.000001' }] };
    expect(matchesDeliveryReceipt(receipt, draft)).toBe(true);
    expect(matchesDeliveryReceipt({ ...receipt, status: 'Pending' }, draft)).toBe(false);
    expect(matchesDeliveryReceipt({ ...receipt, documentNo: ' ' }, draft)).toBe(false);
    expect(matchesDeliveryReceipt({ ...receipt, lines: [receipt.lines[0], { ...receipt.lines[1], quantity: '5' }] }, draft)).toBe(false);
    expect(matchesDeliveryReceipt({ ...receipt, lines: [receipt.lines[0]] }, draft)).toBe(false);
    expect(matchesDeliveryReceipt(null, draft)).toBe(false);
  });
});
