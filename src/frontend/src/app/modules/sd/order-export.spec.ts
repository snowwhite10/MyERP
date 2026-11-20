import { HttpErrorResponse, HttpHeaders, HttpResponse } from '@angular/common/http';
import { TimeoutError } from 'rxjs';
import { checkOrderExport, orderExportFailure } from './order-export';

const csv = (text: string) => new Blob([text], { type: 'text/csv' });
const response = (count: string | null, body: Blob | null) => new HttpResponse<Blob>({ status: 200, body,
  headers: count === null ? new HttpHeaders() : new HttpHeaders({ 'X-Export-Row-Count': count }) });

describe('SD-ORDER-EXPORT-UI-001/003/004 checkOrderExport', () => {
  it('accepts a file whose CSV data rows match X-Export-Row-Count', async () => {
    const result = await checkOrderExport(response('2', csv('﻿h1,h2\r\n1,2\r\n"a\r\nb",3\r\n')));
    expect(result.kind).toBe('saved');
    expect(result.kind === 'saved' && result.count).toBe(2);
  });

  it('reports header-only output as empty so the page warns instead of downloading', async () => {
    expect(await checkOrderExport(response('0', csv('﻿h1,h2\r\n')))).toEqual({ kind: 'empty' });
  });

  it.each([
    ['missing count', null, 'h\r\n1\r\n'],
    ['non-numeric count', '1x', 'h\r\n1\r\n'],
    ['negative count', '-1', 'h\r\n'],
    ['count mismatch', '3', 'h\r\n1\r\n2\r\n'],
    ['empty file', '0', ''],
    ['unterminated quote', '1', 'h\r\n"1\r\n'],
  ])('rejects %s as an invalid export', async (_name, count, text) => {
    expect(await checkOrderExport(response(count, csv(text)))).toEqual({ kind: 'invalid' });
  });

  it('rejects a response without a body', async () => {
    expect(await checkOrderExport(response('0', null))).toEqual({ kind: 'invalid' });
  });
});

describe('SD-ORDER-EXPORT-UI-005/007 orderExportFailure', () => {
  const http = (status: number, error: unknown) => new HttpErrorResponse({ status, error });

  it('names permission, limit, validation, timeout and network failures without a trace', () => {
    expect(orderExportFailure(http(403, { errorCode: 'COMMON.PERMISSION.DENIED' })))
      .toEqual({ key: 'sd.order.exportDenied', traceId: null, details: [] });
    expect(orderExportFailure(http(422, { errorCode: 'SAL.ORDER.EXPORT_LIMIT' })).key).toBe('sd.order.exportLimit');
    expect(orderExportFailure(new TimeoutError()).key).toBe('sd.order.exportTimeout');
    expect(orderExportFailure(http(0, null)).key).toBe('errors.http.network');
    const invalid = orderExportFailure(http(400, { errors: { to: ['To must be on or after From.'] } }));
    expect(invalid.key).toBe('sd.order.exportInvalid');
    expect(invalid.details).toEqual(['to: To must be on or after From.']);
  });

  it('shows the server trace ID for 5xx and a generic export failure for unknown 4xx', () => {
    expect(orderExportFailure(http(500, { traceId: 'trace-export-500' })))
      .toEqual({ key: 'sd.order.exportFailed', traceId: 'trace-export-500', details: [] });
    expect(orderExportFailure(http(422, { errorCode: 'OTHER' })).key).toBe('sd.order.exportFailed');
  });
});
