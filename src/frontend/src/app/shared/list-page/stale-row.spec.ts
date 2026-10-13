import { HttpErrorResponse } from '@angular/common/http';
import { CONCURRENCY_CONFLICT, isMissingRowError, isStaleRowError } from './stale-row';

const http = (status: number, errorCode?: string) => new HttpErrorResponse({ status, error: errorCode ? { errorCode } : null });

describe('MD-F06 isStaleRowError', () => {
  it('treats a deleted row (404) and a version conflict (409) as stale', () => {
    expect(isStaleRowError(http(404))).toBe(true);
    expect(isStaleRowError(http(409, CONCURRENCY_CONFLICT))).toBe(true);
  });

  it('does not treat other 409 business errors as stale', () => {
    expect(isStaleRowError(http(409, 'MD.CUSTOMER.IN_USE'))).toBe(false);
    expect(isStaleRowError(http(409, 'SYS.APPROVAL.ALREADY_PENDING'))).toBe(false);
    expect(isStaleRowError(http(409))).toBe(false);
  });

  it.each([0, 400, 403, 422, 500])('does not treat HTTP %i as stale', (status) => {
    expect(isStaleRowError(http(status, CONCURRENCY_CONFLICT))).toBe(false);
  });

  it('ignores non-HTTP errors', () => {
    expect(isStaleRowError(new Error('boom'))).toBe(false);
    expect(isStaleRowError(null)).toBe(false);
    expect(isMissingRowError(new Error('boom'))).toBe(false);
  });

  it('treats only HTTP 404 as a missing row', () => {
    expect(isMissingRowError(http(404))).toBe(true);
    expect(isMissingRowError(http(409, CONCURRENCY_CONFLICT))).toBe(false);
  });
});
