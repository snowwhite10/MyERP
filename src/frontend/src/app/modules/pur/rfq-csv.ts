import { RfqDetail } from './rfq.service';

export const RFQ_CSV_LIMIT = 10000;

export function rfqCsvRowCount(details: Pick<RfqDetail, 'lines'>[]): number {
  const count = details.reduce((sum, detail) => sum + detail.lines.length, 0);
  if (count > RFQ_CSV_LIMIT) throw new RangeError('PUR.RFQ.EXPORT_LIMIT');
  return count;
}

function cell(value: string | number | null | undefined): string {
  let content = String(value ?? '');
  if (typeof value === 'string' && /^\s*[=+\-@]/.test(content)) content = "'" + content;
  return '"' + content.replaceAll('"', '""') + '"';
}

export function rfqCsv(details: RfqDetail[], headers: string[]): string {
  rfqCsvRowCount(details);
  const data: (string | number | null)[][] = [headers, ...details.flatMap(detail => detail.lines.map(line => [
    detail.documentNo, detail.businessDate, detail.replyDeadline, detail.status,
    line.lineNo, line.itemId, line.requisitionLineId, line.requiredDate, line.quantity,
    line.remark ?? detail.remark,
    detail.invitations.map(invitation => invitation.supplierCodeSnapshot ?? String(invitation.supplierId)).join('; '),
  ]))];
  return '\uFEFF' + data.map(row => row.map(cell).join(',')).join('\r\n');
}
