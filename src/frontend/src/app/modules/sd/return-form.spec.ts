import { HttpErrorResponse } from '@angular/common/http';
import { draftFromSaved, draftFromSource, earliestReturnDate, lineTotal, matchesReturnReceipt, mergeDraft, returnRefusal, returnRequest,
  validateReturnDraft } from './return-form';
import { SalesReturnDetail, SalesReturnSourceLine, SalesReturnSourceOrder } from './return.service';

const line = (deliveryLineId: number, extra: Partial<SalesReturnSourceLine> = {}): SalesReturnSourceLine => ({
  deliveryId: 30, deliveryDocumentNo: 'DH1', deliveryDate: '2028-02-10', deliveryLineId, deliveryLineNo: 1, orderLineId: 21,
  orderLineNo: 1, itemId: 7, itemCode: 'I1', itemName: 'Bolt', warehouseId: 51, locationId: 9, trackingUnsupported: false,
  postedQuantity: '2', returnedQuantity: '0', returnableQuantity: '2', invoicedQuantity: '1', unbilledRemainingQuantity: '1',
  invoiceLines: [{ invoiceLineId: 61, invoiceId: 60, invoiceDocumentNo: 'SI1', invoiceDate: '2028-02-15', quantity: '1',
    creditedQuantity: '0', remainingQuantity: '1' }], ...extra });
const source = (lines = [line(31), line(33, { deliveryDate: '2028-02-01', invoiceLines: [], invoicedQuantity: '0',
  unbilledRemainingQuantity: '1.5', returnableQuantity: '1.5', warehouseId: 52, locationId: null })]): SalesReturnSourceOrder => ({
  id: 9, documentNo: 'SO1', customerId: 3, customerCode: 'C1', customerName: 'Acme', businessDate: '2028-01-01', currencyCode: 'USD',
  baseCurrencyCode: 'TWD', exchangeRate: '31', version: 2, lines });

describe('SD-RETURN-FORM sales return draft form rules', () => {
  it('SD-RETURN-FORM-001 starts unselected and blank with the delivery destination and one portion per open invoice line', () => {
    const draft = draftFromSource(source(), '2028-03-01', 'note');
    expect(draft).toEqual({ orderId: 9, businessDate: '2028-03-01', remark: 'note', lines: [
      { deliveryLineId: 31, selected: false, warehouseId: 51, locationId: 9, unbilledQuantity: '', billed: [{ invoiceLineId: 61, quantity: '' }] },
      { deliveryLineId: 33, selected: false, warehouseId: 52, locationId: null, unbilledQuantity: '', billed: [] }] });
  });

  it('SD-RETURN-FORM-002 requires an order, a valid date and at least one selected line', () => {
    expect(validateReturnDraft(draftFromSource(source(), ''), null)).toEqual({ orderId: 'required' });
    const draft = draftFromSource(source(), '2028-02-30');
    expect(validateReturnDraft(draft, source())).toEqual({ businessDate: 'invalidDate', lines: 'noLines' });
  });

  it('SD-RETURN-FORM-003 checks each split exactly against its advisory balance (decimal text, no binary rounding)', () => {
    const draft = draftFromSource(source(), '2028-03-01');
    draft.lines[0] = { ...draft.lines[0], selected: true, unbilledQuantity: '1.000001', billed: [{ invoiceLineId: 61, quantity: '1.0000001' }] };
    draft.lines[1] = { ...draft.lines[1], selected: true, unbilledQuantity: '0' };
    expect(validateReturnDraft(draft, source())).toEqual({ 'unbilled-31': 'aboveUnbilled', 'billed-31-61': 'decimal', 'line-33': 'zero' });
    draft.lines[0].unbilledQuantity = '1'; draft.lines[0].billed[0].quantity = '1.000001';
    draft.lines[1].unbilledQuantity = '-1';
    // 1 + 1.000001 also exceeds the returnable 2.
    expect(validateReturnDraft(draft, source())).toEqual({ 'billed-31-61': 'aboveCredit', 'unbilled-33': 'decimal', 'line-31': 'aboveReturnable' });
    draft.lines[0].billed[0].quantity = '1'; draft.lines[1].unbilledQuantity = '1.5';
    expect(validateReturnDraft(draft, source())).toEqual({});
    expect(lineTotal(draft.lines[0])).toBe('2');
  });

  it('SD-RETURN-FORM-004 the total may not exceed the returnable quantity even when each split is within its own balance', () => {
    const tight = source([line(31, { returnableQuantity: '1.5' })]);
    const draft = draftFromSource(tight, '2028-03-01');
    draft.lines[0] = { ...draft.lines[0], selected: true, unbilledQuantity: '1', billed: [{ invoiceLineId: 61, quantity: '0.500001' }] };
    expect(validateReturnDraft(draft, tight)).toEqual({ 'line-31': 'aboveReturnable' });
  });

  it('SD-RETURN-FORM-005 the date may not precede the selected delivery or a credited invoice; the hint shows the latest', () => {
    const draft = draftFromSource(source(), '2028-02-12');
    draft.lines[0] = { ...draft.lines[0], selected: true, unbilledQuantity: '1' };
    expect(validateReturnDraft(draft, source())).toEqual({});
    expect(earliestReturnDate(draft, source())).toBe('2028-02-10');
    draft.lines[0].billed[0].quantity = '0.5';
    expect(earliestReturnDate(draft, source())).toBe('2028-02-15');
    expect(validateReturnDraft(draft, source())).toEqual({ businessDate: 'beforeInvoice' });
    draft.businessDate = '2028-02-09';
    expect(Object.keys(validateReturnDraft(draft, source()))[0]).toBe('businessDate');
    expect(validateReturnDraft(draft, source())['businessDate']).toBe('beforeDelivery');
  });

  it('SD-RETURN-FORM-006 a tracked line cannot be returned and a remark is limited to 500 characters', () => {
    const tracked = source([line(31, { trackingUnsupported: true })]);
    const draft = draftFromSource(tracked, '2028-03-01', 'x'.repeat(501));
    draft.lines[0] = { ...draft.lines[0], selected: true, unbilledQuantity: '1' };
    expect(validateReturnDraft(draft, tracked)).toEqual({ remark: 'remarkLength', 'line-31': 'tracking' });
  });

  it('SD-RETURN-FORM-007 the request numbers selected lines 1..n, sends blank as 0 and omits zero billed portions', () => {
    const draft = draftFromSource(source(), '2028-03-01', '  ');
    draft.lines[0] = { ...draft.lines[0], selected: false };
    draft.lines[1] = { ...draft.lines[1], selected: true, unbilledQuantity: ' 1.5 ' };
    expect(returnRequest(draft, 'k1')).toEqual({ requestKey: 'k1', businessDate: '2028-03-01', remark: null, lines: [
      { lineNo: 1, deliveryLineId: 33, warehouseId: 52, locationId: null, unbilledQuantity: '1.5', billed: [] }] });
    draft.lines[0] = { ...draft.lines[0], selected: true, unbilledQuantity: '', billed: [{ invoiceLineId: 61, quantity: '1' }] };
    draft.remark = ' Damaged ';
    const request = returnRequest(draft, 'k1');
    expect(request.remark).toBe('Damaged');
    expect(request.lines[0]).toEqual({ lineNo: 1, deliveryLineId: 31, warehouseId: 51, locationId: 9, unbilledQuantity: '0',
      billed: [{ invoiceLineId: 61, quantity: '1' }] });
    expect(request.lines[1].lineNo).toBe(2);
  });

  it('SD-RETURN-FORM-008 reloading keeps entries of lines still offered, drops gone ones and adds new ones blank', () => {
    const draft = draftFromSource(source(), '2028-03-01');
    draft.lines[0] = { ...draft.lines[0], selected: true, unbilledQuantity: '0.5', billed: [{ invoiceLineId: 61, quantity: '0.25' }] };
    const reread = source([line(31, { invoiceLines: [{ invoiceLineId: 62, invoiceId: 63, invoiceDocumentNo: 'SI2', invoiceDate: '2028-02-16',
      quantity: '1', creditedQuantity: '0', remainingQuantity: '1' }] }), line(35)]);
    const merged = mergeDraft(draft, reread);
    expect(merged.lines.map(l => [l.deliveryLineId, l.selected, l.unbilledQuantity, l.billed])).toEqual([
      [31, true, '0.5', [{ invoiceLineId: 62, quantity: '' }]], [35, false, '', [{ invoiceLineId: 61, quantity: '' }]]]);
  });

  it('SD-RETURN-FORM-009 the receipt must match key, date and every line split; 422 refusals expose line, invoice line and balance', () => {
    const request = { requestKey: 'k1', businessDate: '2028-03-01', remark: null, lines: [
      { lineNo: 1, deliveryLineId: 31, warehouseId: 51, locationId: 9, unbilledQuantity: '0.5', billed: [{ invoiceLineId: 61, quantity: '1' }] }] };
    const saved = { id: 5, requestKey: 'k1', businessDate: '2028-03-01', lines: [{ deliveryLineId: 31, unbilledQuantity: '0.500000',
      allocations: [{ invoiceLineId: 61, quantity: '1.000000' }] }] } as unknown as SalesReturnDetail;
    expect(matchesReturnReceipt(saved, request)).toBe(true);
    expect(matchesReturnReceipt({ ...saved, requestKey: 'k2' }, request)).toBe(false);
    expect(matchesReturnReceipt({ ...saved, lines: [{ ...saved.lines[0], unbilledQuantity: '0.4' }] }, request)).toBe(false);
    expect(matchesReturnReceipt({ ...saved, lines: [{ ...saved.lines[0], allocations: [] }] }, request)).toBe(false);
    expect(matchesReturnReceipt(null, request)).toBe(false);
    const refusal = new HttpErrorResponse({ status: 422, error: { lineNo: 2, invoiceLineId: 61, remaining: 0.25 } });
    expect(returnRefusal(refusal)).toEqual({ lineNo: 2, invoiceLineId: 61, remaining: '0.25' });
    expect(returnRefusal(new HttpErrorResponse({ status: 422, error: { lineNo: -1, remaining: 'x' } })))
      .toEqual({ lineNo: null, invoiceLineId: null, remaining: null });
    expect(returnRefusal(new HttpErrorResponse({ status: 409, error: { lineNo: 1 } })).lineNo).toBeNull();
  });

  it('SD-RETURN-FORM-010 a saved return fills the form; tracked, vanished lines and uncreditable invoice portions are dropped', () => {
    const order = source([line(31), line(33, { invoiceLines: [] }), line(34, { trackingUnsupported: true, invoiceLines: [] })]);
    const savedLine = (lineNo: number, deliveryLineId: number, unbilled: string, allocations: { invoiceLineId: number; invoiceDocumentNo: string; quantity: string }[]) =>
      ({ lineNo, deliveryLineId, deliveryDocumentNo: `DH${deliveryLineId}`, itemCode: 'I1', warehouseId: 52, locationId: null,
        quantity: '9', unbilledQuantity: unbilled, allocations });
    const saved = { businessDate: '2028-03-01', remark: null, lines: [
      savedLine(3, 99, '2', []), savedLine(1, 31, '0.5', [{ invoiceLineId: 61, invoiceDocumentNo: 'SI1', quantity: '1' },
        { invoiceLineId: 62, invoiceDocumentNo: 'SI2', quantity: '0.25' }]), savedLine(2, 34, '1', [])] } as unknown as SalesReturnDetail;
    const { draft, dropped } = draftFromSaved(order, saved);
    expect(draft.businessDate).toBe('2028-03-01');
    expect(draft.remark).toBe('');
    expect(draft.lines.map(form => [form.deliveryLineId, form.selected, form.warehouseId, form.locationId, form.unbilledQuantity,
      form.billed.map(portion => portion.quantity)])).toEqual([[31, true, 52, null, '0.5', ['1']], [33, false, 51, 9, '', []],
      [34, false, 51, 9, '', []]]);
    expect(dropped).toEqual([
      { lineNo: 1, deliveryDocumentNo: 'DH31', itemCode: 'I1', invoiceDocumentNo: 'SI2', quantity: '0.25' },
      { lineNo: 2, deliveryDocumentNo: 'DH34', itemCode: 'I1', invoiceDocumentNo: null, quantity: '9' },
      { lineNo: 3, deliveryDocumentNo: 'DH99', itemCode: 'I1', invoiceDocumentNo: null, quantity: '9' }]);
  });
});
