import { rfqDraftFromDetail } from './rfq-form';
import { RfqDetail } from './rfq.service';

describe('RFQ editor saved draft', () => {
  it('restores saved lines, supplier IDs and six-place decimal quantity for a revision', () => {
    const saved = { id: 7, documentNo: 'RFQ-7', businessDate: '2028-02-29',
      replyDeadline: '2028-03-01', ownerId: 1, status: 'Draft', version: 2,
      remark: null, sentAt: null, createdAt: '',
      lines: [{ id: 8, lineNo: 1, itemId: 9, requisitionLineId: null,
        requiredDate: '2028-03-01', quantity: 1.000001, remark: null }],
      invitations: [{ id: 10, supplierId: 11, supplierCodeSnapshot: null,
        supplierNameSnapshot: null, sentAt: null }] } satisfies RfqDetail;
    expect(rfqDraftFromDetail(saved)).toEqual({ businessDate: '2028-02-29',
      replyDeadline: '2028-03-01', remark: '', supplierIds: [11], lines: [
        { itemId: 9, requisitionLineId: null, requiredDate: '2028-03-01', quantity: '1.000001', remark: '' }] });
  });
});
