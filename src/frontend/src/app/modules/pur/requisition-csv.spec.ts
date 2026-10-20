import { requisitionCsv, requisitionCsvRowCount } from './requisition-csv';

describe('PUR-CSV-002/008', () => {
  const line = { id: 1, lineNo: 1, itemId: 11, requiredDate: '2026-10-31', quantity: 1.123456,
    orderedQuantity: 0, remark: '=HYPERLINK("x")\n下一行' };
  it('counts every saved line and rejects the export limit', () => {
    expect(requisitionCsvRowCount([{ lines: [line, line] }, { lines: [line] }])).toBe(3);
    expect(requisitionCsvRowCount([{ lines: Array(10000).fill(line) }])).toBe(10000);
    expect(() => requisitionCsvRowCount([{ lines: Array(10001).fill(line) }])).toThrow();
  });

  it('escapes formulas, quotes and newlines in a UTF-8 CSV', () => {
    const csv = requisitionCsv([{ documentNo: 'REQ-7', businessDate: '2026-10-20', status: 'Draft',
      requesterId: 4, remark: ' +SUM(1)', lines: [line] }], ['No', 'Date', 'Status', 'Requester', 'Line', 'Item', 'Required', 'Quantity', 'Ordered', 'Remark']);
    expect(csv.startsWith('\uFEFF')).toBe(true);
    expect(csv).toContain('"\'=HYPERLINK(""x"")\n下一行"');
    for (const prefix of ['=', '+', '-', '@']) {
      const escaped = requisitionCsv([{ documentNo: `\t${prefix}SUM(1)`, businessDate: '2026-10-20',
        status: 'Draft', requesterId: 4, remark: null, lines: [line] }], ['No']);
      expect(escaped).toContain(`"'\t${prefix}SUM(1)"`);
    }
  });
});
