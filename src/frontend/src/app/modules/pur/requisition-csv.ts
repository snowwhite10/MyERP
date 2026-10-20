import { RequisitionDetail } from './requisition.service';

export const REQUISITION_CSV_LIMIT = 10000;

export function requisitionCsvRowCount(details: Pick<RequisitionDetail, 'lines'>[]): number {
  const count = details.reduce((sum, detail) => sum + detail.lines.length, 0);
  if (count > REQUISITION_CSV_LIMIT) throw new RangeError('PUR.REQUISITION.EXPORT_LIMIT');
  return count;
}

function cell(value: string | number | null | undefined): string {
  let content = String(value ?? '');
  if (typeof value === 'string' && /^\s*[=+\-@]/.test(content)) content = "'" + content;
  return '"' + content.replaceAll('"', '""') + '"';
}

export function requisitionCsv(details: Pick<RequisitionDetail,
  'documentNo' | 'businessDate' | 'status' | 'requesterId' | 'remark' | 'lines'>[], headers: string[]): string {
  requisitionCsvRowCount(details);
  const data: (string | number | null)[][] = [headers, ...details.flatMap(detail => detail.lines.map(line => [
    detail.documentNo, detail.businessDate, detail.status, detail.requesterId, line.lineNo,
    line.itemId, line.requiredDate, line.quantity, line.orderedQuantity, line.remark ?? detail.remark,
  ]))];
  return '\uFEFF' + data.map(row => row.map(cell).join(',')).join('\r\n');
}
