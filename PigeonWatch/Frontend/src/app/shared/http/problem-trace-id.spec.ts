import { HttpErrorResponse } from '@angular/common/http';
import { problemTraceId } from './problem-details';

describe('problemTraceId', () => {
  function response(error: unknown): HttpErrorResponse {
    return new HttpErrorResponse({ status: 500, error });
  }

  it('reads the trace id string', () => {
    expect(problemTraceId(response({ traceId: '00-abc-def-00' }))).toBe('00-abc-def-00');
  });

  it.each([null, 'text', {}, { traceId: 5 }, { traceId: '' }])('finds none in %j', (body) => {
    expect(problemTraceId(response(body))).toBeNull();
  });
});
