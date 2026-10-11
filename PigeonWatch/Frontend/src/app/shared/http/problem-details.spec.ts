import { HttpErrorResponse } from '@angular/common/http';
import { problemDetail, problemErrorCodes, problemExtension } from './problem-details';

describe('problem details', () => {
  function response(error: unknown): HttpErrorResponse {
    return new HttpErrorResponse({ status: 400, error });
  }

  it('lists the error codes of a validation problem', () => {
    const error = response({ errors: { CooldownActive: ['Wait.'], InvalidLocation: ['Bad.'] } });

    expect(problemErrorCodes(error)).toEqual(['CooldownActive', 'InvalidLocation']);
  });

  it.each([null, 'text', [], { errors: ['x'] }])('finds no codes in %j', (body) => {
    expect(problemErrorCodes(response(body))).toEqual([]);
  });

  it('reads the detail string', () => {
    expect(problemDetail(response({ detail: 'LockedOut' }))).toBe('LockedOut');
    expect(problemDetail(response({ detail: 1 }))).toBeNull();
    expect(problemDetail(response(null))).toBeNull();
  });

  it('reads an extension member', () => {
    expect(problemExtension(response({ retryAfterSeconds: 42 }), 'retryAfterSeconds')).toBe(42);
    expect(problemExtension(response({}), 'retryAfterSeconds')).toBeUndefined();
    expect(problemExtension(response('text'), 'retryAfterSeconds')).toBeUndefined();
  });
});
