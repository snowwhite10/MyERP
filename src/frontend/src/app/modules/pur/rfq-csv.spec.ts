import { describe, expect, it } from 'vitest';
import { rfqCsv, rfqCsvRowCount } from './rfq-csv';
import { RfqDetail } from './rfq.service';

const saved: RfqDetail = {
  id: 1, documentNo: '=RFQ,"A"\nB', businessDate: '2024-02-29', replyDeadline: '2024-03-01',
  ownerId: 2, status: 'Sent', version: 3, remark: null, sentAt: null, createdAt: '2024-02-29T12:00:00',
  lines: [{ id: 4, lineNo: 1, itemId: 9, requisitionLineId: null, requiredDate: '2024-03-01',
    quantity: 1.123456, remark: '+formula' }],
  invitations: [{ id: 5, supplierId: 6, supplierCodeSnapshot: '@supplier',
    supplierNameSnapshot: 'Saved supplier', sentAt: null }],
};

describe('RFQ saved CSV', () => {
  it('keeps saved line precision, BOM, quoted line breaks and formula safety', () => {
    const csv = rfqCsv([saved], ['Number', 'Remark']);
    expect(csv.startsWith('\uFEFF')).toBe(true);
    expect(csv).toContain('"\'=RFQ,""A""\nB"');
    expect(csv).toContain('"1.123456"');
    expect(csv).toContain('"\'+formula"');
    expect(csv).toContain('"\'@supplier"');
    expect(rfqCsvRowCount([saved])).toBe(1);
  });

  it('rejects a 10001st line without creating a partial file', () => {
    const tenThousand = { ...saved, lines: Array(10000).fill(saved.lines[0]) };
    expect(rfqCsvRowCount([tenThousand])).toBe(10000);
    expect(() => rfqCsvRowCount([{ ...tenThousand, lines: [...tenThousand.lines, saved.lines[0]] }]))
      .toThrow('PUR.RFQ.EXPORT_LIMIT');
  });
});
