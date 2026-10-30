import { HttpErrorResponse } from '@angular/common/http';
import { isExpectedClientStatus, unexpectedTraceId } from './problem-details';

describe('unexpectedTraceId', () => {
  it('returns the trace id of a 500 problem body', () => {
    const error = new HttpErrorResponse({ status: 500, error: { traceId: '00-abc-01' } });

    expect(unexpectedTraceId(error)).toBe('00-abc-01');
  });

  it('returns undefined when the response carried no trace id', () => {
    expect(unexpectedTraceId(new HttpErrorResponse({ status: 500 }))).toBeUndefined();
    expect(unexpectedTraceId(new HttpErrorResponse({ status: 0 }))).toBeUndefined();
  });

  it.each([400, 401, 404, 429])('ignores the trace id of an expected %s', (status) => {
    const error = new HttpErrorResponse({ status, error: { traceId: '00-abc-01' } });

    expect(isExpectedClientStatus(error)).toBe(true);
    expect(unexpectedTraceId(error)).toBeUndefined();
  });

  it('treats anything that is not an http error as not expected', () => {
    expect(isExpectedClientStatus(new Error('boom'))).toBe(false);
    expect(unexpectedTraceId(new Error('boom'))).toBeUndefined();
    expect(isExpectedClientStatus(new HttpErrorResponse({ status: 500 }))).toBe(false);
  });
});
