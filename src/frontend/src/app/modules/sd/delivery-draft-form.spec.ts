import { HttpErrorResponse } from '@angular/common/http';
import type { SalesDeliveryDetail } from './delivery.service';
import { addStockPicks, compareDecimalText, DeliveryDraftForm, DeliveryDraftLineForm, deliveryDraftJson, deliveryErrorLineNo, deliveryErrorLineNos, DeliverySourceOrder, draftFromDelivery, draftFromOrder,
  matchesDeliveryReceipt, serialPicksFromText, trackingProgress, validateDeliveryDraft, validDeliveryQuantity } from './delivery-draft-form';

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

  it('SD-DEL-CR-024 lists every positive line number a 422 names, ascending and unique (ADR-154)', () => {
    const problem = (status: number, body: object) => new HttpErrorResponse({ status, error: body });
    expect(deliveryErrorLineNos(problem(422, { lineNo: 2, lineNos: [4, 2, 4, 0, -1, 1.5, '3'] }))).toEqual([2, 4]);
    expect(deliveryErrorLineNos(problem(422, { lineNo: 3 }))).toEqual([3]);
    expect(deliveryErrorLineNos(problem(422, { lineNo: 3, lineNos: 'x' }))).toEqual([3]);
    expect(deliveryErrorLineNos(problem(422, { lineNos: [1] }))).toEqual([]);
    expect(deliveryErrorLineNos(problem(409, { lineNo: 1, lineNos: [1] }))).toEqual([]);
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

describe('SD-DEL-ED delivery edit form rules', () => {
  const saved = (status = 'Draft'): SalesDeliveryDetail => ({ id: 61, documentNo: 'DH0061', orderId: 9, orderDocumentNo: 'SO1',
    customerId: 3, customerCode: 'C1', customerName: 'Acme', businessDate: '2028-02-29', status: status as SalesDeliveryDetail['status'],
    remark: null, approvalInstanceId: null, postedAt: null, version: 3, lines: [
      { id: 72, lineNo: 2, orderLineId: 23, orderLineNo: 3, itemId: 8, itemCode: 'I3', itemName: 'Pin', warehouseId: 4, locationId: null,
        quantity: '9999999999999.999999', tracking: [], invMovementId: null },
      { id: 71, lineNo: 1, orderLineId: 21, orderLineNo: 1, itemId: 7, itemCode: 'I1', itemName: 'Bolt', warehouseId: 4, locationId: 12,
        quantity: '2.500000', tracking: [{ seqNo: 2, lotNo: null, serialNo: 'S2', quantity: '1.000000' },
          { seqNo: 1, lotNo: null, serialNo: 'S1', quantity: '1' }], invMovementId: null }] });

  it('SD-DEL-ED-002 maps a saved Draft or Rejected delivery to the form in line order with trimmed exact text and saved tracking', () => {
    for (const status of ['Draft', 'Rejected']) {
      const draft = draftFromDelivery(saved(status));
      expect(draft?.orderId).toBe(9);
      expect(draft?.lines.map(line => [line.lineNo, line.orderLineId, line.warehouseId, line.locationId, line.quantity])).toEqual([
        [1, 21, 4, 12, '2.5'], [2, 23, 4, null, '9999999999999.999999']]);
      expect(draft?.lines[0].tracking).toEqual([{ lotNo: null, serialNo: 'S1', quantity: '1' }, { lotNo: null, serialNo: 'S2', quantity: '1' }]);
      expect(draft?.lines[1].tracking).toEqual([]);
    }
  });

  it('SD-DEL-ED-003 refuses Pending, Posted and malformed saved deliveries instead of guessing', () => {
    expect(draftFromDelivery(saved('Pending'))).toBeNull();
    expect(draftFromDelivery(saved('Posted'))).toBeNull();
    expect(draftFromDelivery({ ...saved(), lines: [] })).toBeNull();
    expect(draftFromDelivery({ ...saved(), lines: [{ ...saved().lines[0], quantity: '1e3' }] })).toBeNull();
    expect(draftFromDelivery(null)).toBeNull();
  });

  it('SD-DEL-ED-004 flags a quantity below its saved lot/serial total and accepts an equal or larger quantity', () => {
    const draft = draftFromDelivery(saved())!;
    draft.lines[0].quantity = '1.999999';
    expect(validateDeliveryDraft(draft)['lines[0].quantity']).toBe('belowTracking');
    draft.lines[0].quantity = '2';
    expect(validateDeliveryDraft(draft)).toEqual({});
  });

  it('SD-DEL-ED-005 matches a saved reply only when tracking selections come back unchanged', () => {
    const draft = draftFromDelivery(saved())!;
    const reply = { ...saved(), status: 'Draft' as const, version: 4 };
    expect(matchesDeliveryReceipt(reply, draft)).toBe(true);
    const lost = { ...reply, lines: reply.lines.map(line => ({ ...line, tracking: [] })) };
    expect(matchesDeliveryReceipt(lost, draft)).toBe(false);
    const changed = { ...reply, lines: reply.lines.map(line => ({ ...line, tracking: line.tracking.map(t => ({ ...t, serialNo: 'X' })) })) };
    expect(matchesDeliveryReceipt(changed, draft)).toBe(false);
  });
  it('SD-DEL-ED-006 flags blank, over-long, mixed and duplicate lot/serial numbers and bad pick quantities on the line', () => {
    const draft = draftFromDelivery(saved())!;
    const check = (tracking: DeliveryDraftForm['lines'][number]['tracking']) => {
      draft.lines[1].tracking = tracking; return validateDeliveryDraft(draft)['lines[1].tracking']; };
    expect(check([{ lotNo: 'A', serialNo: null, quantity: '1' }, { lotNo: 'B', serialNo: null, quantity: '0.5' }])).toBeUndefined();
    expect(check([{ lotNo: '  ', serialNo: null, quantity: '1' }])).toBe('trackingNumber');
    expect(check([{ lotNo: 'L'.repeat(51), serialNo: null, quantity: '1' }])).toBe('trackingNumber');
    expect(check([{ lotNo: null, serialNo: 'S'.repeat(101), quantity: '1' }])).toBe('trackingNumber');
    expect(check([{ lotNo: 'A', serialNo: 'S', quantity: '1' }])).toBe('trackingNumber');
    expect(check([{ lotNo: 'a', serialNo: null, quantity: '1' }, { lotNo: 'A ', serialNo: null, quantity: '1' }])).toBe('trackingDuplicate');
    expect(check([{ lotNo: 'A', serialNo: null, quantity: '0' }])).toBe('trackingQuantity');
    expect(check([{ lotNo: 'A', serialNo: null, quantity: '1.0000001' }])).toBe('trackingQuantity');
    expect(check([{ lotNo: null, serialNo: 'S9', quantity: '2' }])).toBe('trackingQuantity');
    expect(check([])).toBeUndefined();
  });

  it('SD-DEL-ED-007 reads one serial per non-blank trimmed line and reports exact selected-versus-line progress', () => {
    expect(serialPicksFromText(' S1 \r\n\nS2\n  ')).toEqual([{ lotNo: null, serialNo: 'S1', quantity: '1' }, { lotNo: null, serialNo: 'S2', quantity: '1' }]);
    expect(serialPicksFromText('')).toEqual([]);
    const line = draftFromDelivery(saved())!.lines[0];
    expect(trackingProgress(line)).toEqual({ selected: '2', complete: false });
    line.quantity = '2';
    expect(trackingProgress(line)).toEqual({ selected: '2', complete: true });
    line.tracking = [{ lotNo: 'A', serialNo: null, quantity: '0.000001' }, { lotNo: 'B', serialNo: null, quantity: 'x' }];
    expect(trackingProgress(line)).toEqual({ selected: '0.000001', complete: false });
    line.tracking = undefined;
    expect(trackingProgress(line)).toEqual({ selected: '0', complete: false });
  });
});

describe('SD-DEL-ED-044 pick from stock (addStockPicks, ADR-152)', () => {
  const line = (quantity: string, tracking: DeliveryDraftLineForm['tracking'] = []): DeliveryDraftLineForm =>
    ({ lineNo: 1, orderLineId: 21, warehouseId: 4, locationId: null, quantity, tracking });

  it('adds lots earliest expiry first, each min(on hand, still needed), and skips numbers already on the line', () => {
    const target = line('2', [{ lotNo: 'l-1', serialNo: null, quantity: '1.5' }]);
    const result = addStockPicks(target, 'Lot', [{ number: 'L-3', quantity: '9', expiryDate: null }, { number: 'L-1', quantity: '3', expiryDate: '2027-01-01' },
      { number: 'L-2', quantity: '0.25', expiryDate: '2028-01-01' }, { number: 'L-4', quantity: '1', expiryDate: '2029-01-01' }]);
    expect(result).toEqual({ added: 2, duplicate: 1, full: 1 });
    expect(target.tracking).toEqual([{ lotNo: 'l-1', serialNo: null, quantity: '1.5' }, { lotNo: 'L-2', serialNo: null, quantity: '0.25' },
      { lotNo: 'L-4', serialNo: null, quantity: '0.25' }]);
    expect(trackingProgress(target).complete).toBe(true);
  });

  it('keeps exact six-place text for large quantities and replaces fully blank lot rows', () => {
    const target = line('9999999999999.999999', [{ lotNo: '', serialNo: null, quantity: ' ' }]);
    expect(addStockPicks(target, 'Lot', [{ number: 'BIG', quantity: '9999999999999.999998' }, { number: 'TINY', quantity: '5' }]))
      .toEqual({ added: 2, duplicate: 0, full: 0 });
    expect(target.tracking).toEqual([{ lotNo: 'BIG', serialNo: null, quantity: '9999999999999.999998' }, { lotNo: 'TINY', serialNo: null, quantity: '0.000001' }]);
  });

  it('adds serials by number with quantity 1 until the line is full, case-insensitive duplicates skipped', () => {
    const target = line('2.5', [{ lotNo: null, serialNo: 'sn-1', quantity: '1' }]);
    expect(addStockPicks(target, 'Serial', [{ number: 'SN-3', quantity: '1' }, { number: 'SN-1', quantity: '1' }, { number: 'SN-2', quantity: '1' }]))
      .toEqual({ added: 1, duplicate: 1, full: 1 });
    expect(target.tracking!.map(pick => pick.serialNo)).toEqual(['sn-1', 'SN-2']);
  });

  it('changes nothing when the line quantity is invalid or already reached', () => {
    const invalid = line('abc');
    expect(addStockPicks(invalid, 'Lot', [{ number: 'L-1', quantity: '1' }])).toEqual({ added: 0, duplicate: 0, full: 1 });
    expect(invalid.tracking).toEqual([]);
    const done = line('1', [{ lotNo: 'L-1', serialNo: null, quantity: '1' }]);
    expect(addStockPicks(done, 'Lot', [{ number: 'L-2', quantity: '1' }, { number: ' ', quantity: '1' }])).toEqual({ added: 0, duplicate: 1, full: 1 });
    expect(done.tracking).toEqual([{ lotNo: 'L-1', serialNo: null, quantity: '1' }]);
  });
});
