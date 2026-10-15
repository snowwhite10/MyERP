import { HttpErrorResponse } from '@angular/common/http';
import { apiErrorKey, apiErrorTraceId, apiValidationDetails } from './api-error';

describe('apiErrorKey', () => {
  const known = ['SYS.ROLE.IN_USE'] as const;

  it.each([
    [new HttpErrorResponse({ status: 422, error: { errorCode: 'SYS.ROLE.IN_USE' } }), 'errors.SYS.ROLE.IN_USE'],
    [new HttpErrorResponse({ status: 422, error: { errorCode: 'SYS.OTHER.CODE' } }), 'errors.http.unknown'],
    [new HttpErrorResponse({ status: 400, error: { errorCode: 'COMMON.REQUEST.VALIDATION_FAILED' } }), 'errors.COMMON.REQUEST.VALIDATION_FAILED'],
    [new HttpErrorResponse({ status: 429, error: { errorCode: 'COMMON.REQUEST.RATE_LIMITED' } }), 'errors.COMMON.REQUEST.RATE_LIMITED'],
    [new HttpErrorResponse({ status: 429 }), 'errors.COMMON.REQUEST.RATE_LIMITED'],
    [new HttpErrorResponse({ status: 0 }), 'errors.http.network'],
    [new HttpErrorResponse({ status: 500, error: null }), 'errors.http.unknown'],
    ['not an http error', 'errors.http.unknown'],
  ])('maps %s', (error, expected) => {
    expect(apiErrorKey(error, known)).toBe(expected);
  });

  it('uses the caller validation key for 400 without a known code', () => {
    expect(apiErrorKey(new HttpErrorResponse({ status: 400 }), known, 'sys.user.passwordRejected')).toBe('sys.user.passwordRejected');
  });

  it('exposes a server trace ID only for 5xx responses', () => {
    expect(apiErrorTraceId(new HttpErrorResponse({ status: 500, error: { traceId: '00-abc-01' } }))).toBe('00-abc-01');
    expect(apiErrorTraceId(new HttpErrorResponse({ status: 422, error: { traceId: '00-abc-01' } }))).toBeNull();
    expect(apiErrorTraceId(new HttpErrorResponse({ status: 500, error: { traceId: {} } }))).toBeNull();
  });

  it('keeps field paths and reasons from 400 validation responses', () => {
    const error = new HttpErrorResponse({ status: 400, error: { errors: {
      'components[0].quantity': ['Quantity must be positive.'], businessDate: ['Date is required.'],
    } } });
    expect(apiValidationDetails(error)).toEqual([
      'components[0].quantity: Quantity must be positive.', 'businessDate: Date is required.',
    ]);
    expect(apiValidationDetails(new HttpErrorResponse({ status: 500, error: { errors: { x: ['secret'] } } }))).toEqual([]);
  });
});
