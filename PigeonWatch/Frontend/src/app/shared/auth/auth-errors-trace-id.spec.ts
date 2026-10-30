import { HttpErrorResponse } from '@angular/common/http';
import {
  isReportableAuthFailure,
  loginFailureResult,
  passwordChangeFailure,
  profileFailure,
  registerFailure,
} from './auth-errors';

const traceId = '00-abc123-def456-01';
const serverError = new HttpErrorResponse({ status: 500, error: { traceId } });

describe('auth failures with a trace id', () => {
  it('carries the trace id of an unexpected login failure', () => {
    expect(loginFailureResult(serverError)).toEqual({ formError: 'unexpected', traceId });
  });

  it('has no trace id for an invalid-credential 401', () => {
    const error = new HttpErrorResponse({ status: 401, error: { traceId } });

    expect(loginFailureResult(error)).toEqual({ formError: 'invalidCredentials' });
    expect(isReportableAuthFailure(error, 'invalidCredentials')).toBe(false);
  });

  it('has no trace id for a warm-up failure', () => {
    const error = new HttpErrorResponse({ status: 503, error: { traceId } });

    expect(loginFailureResult(error)).toEqual({ formError: 'warmupFailed' });
    expect(isReportableAuthFailure(error, 'warmupFailed')).toBe(false);
  });

  it.each([
    ['register', registerFailure],
    ['profile', profileFailure],
    ['password change', passwordChangeFailure],
  ])('carries the trace id of an unexpected %s failure', (_name, failure) => {
    const result = failure(serverError);

    expect(result.formError).toBe('unexpected');
    expect(result.traceId).toBe(traceId);
    expect(isReportableAuthFailure(serverError, result.formError)).toBe(true);
  });

  it.each([
    ['register', registerFailure],
    ['profile', profileFailure],
    ['password change', passwordChangeFailure],
  ])('has no trace id for a %s validation 400', (_name, failure) => {
    const error = new HttpErrorResponse({
      status: 400,
      error: { traceId, errors: { Unknown: ['x'] } },
    });
    const result = failure(error);

    expect(result.formError).toBe('unexpected');
    expect(result.traceId).toBeUndefined();
    expect(isReportableAuthFailure(error, result.formError)).toBe(false);
  });

  it('reports a failure that is not an http error', () => {
    expect(loginFailureResult(new Error('boom'))).toEqual({ formError: 'unexpected' });
    expect(isReportableAuthFailure(new Error('boom'), 'unexpected')).toBe(true);
  });

  it('never reports when there is no form error', () => {
    expect(isReportableAuthFailure(serverError, null)).toBe(false);
  });
});
