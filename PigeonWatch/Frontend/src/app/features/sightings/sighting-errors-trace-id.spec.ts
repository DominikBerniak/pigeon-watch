import { HttpErrorResponse } from '@angular/common/http';
import { isReportableSightingFailure, sightingFailure } from './sighting-errors';

const traceId = '00-abc123-def456-01';

describe('sightingFailure trace id', () => {
  it('carries the trace id on a generic 500 and reports it', () => {
    const error = new HttpErrorResponse({ status: 500, error: { traceId } });
    const failure = sightingFailure(error);

    expect(failure).toEqual({ kind: 'generic', traceId });
    expect(isReportableSightingFailure(error, failure)).toBe(true);
  });

  it('has no trace id when the response carried none', () => {
    const error = new HttpErrorResponse({ status: 0 });
    const failure = sightingFailure(error);

    expect(failure).toEqual({ kind: 'generic' });
    expect(isReportableSightingFailure(error, failure)).toBe(true);
  });

  it('does not report or carry an id for a validation 400', () => {
    const error = new HttpErrorResponse({
      status: 400,
      error: { traceId, errors: { InvalidLocation: ['x'] } },
    });
    const failure = sightingFailure(error);

    expect(failure).toEqual({ kind: 'generic' });
    expect(isReportableSightingFailure(error, failure)).toBe(false);
  });

  it('does not report a 429', () => {
    const error = new HttpErrorResponse({ status: 429, error: { traceId } });
    const failure = sightingFailure(error);

    expect(failure).toEqual({ kind: 'generic' });
    expect(isReportableSightingFailure(error, failure)).toBe(false);
  });

  it('does not report a cooldown or a 404', () => {
    const cooldown = new HttpErrorResponse({
      status: 400,
      error: { traceId, errors: { CooldownActive: ['x'] }, retryAfterSeconds: 30 },
    });
    const missing = new HttpErrorResponse({ status: 404, error: { traceId } });

    expect(isReportableSightingFailure(cooldown, sightingFailure(cooldown))).toBe(false);
    expect(isReportableSightingFailure(missing, sightingFailure(missing))).toBe(false);
  });

  it('reports a failure that is not an http error', () => {
    const failure = sightingFailure(new Error('boom'));

    expect(failure).toEqual({ kind: 'generic' });
    expect(isReportableSightingFailure(new Error('boom'), failure)).toBe(true);
  });
});
