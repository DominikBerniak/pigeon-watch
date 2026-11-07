import { HttpErrorResponse, provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { ResourceService } from '../../core/resources/resource.service';
import {
  commentErrorMessage,
  commentFailure,
  commentFailureTraceId,
  isReportableCommentFailure,
} from './comment-errors';

const traceId = '00-abc123-def456-01';

describe('comment errors', () => {
  function badRequest(codes: string[], extensions: Record<string, unknown> = {}): HttpErrorResponse {
    const errors = Object.fromEntries(codes.map((code) => [code, ['x']]));

    return new HttpErrorResponse({ status: 400, error: { status: 400, errors, ...extensions } });
  }

  it('reads the cooldown seconds and does not report it', () => {
    const error = badRequest(['CooldownActive'], { retryAfterSeconds: 41.2 });
    const failure = commentFailure(error);

    expect(failure).toEqual({ kind: 'cooldown', retryAfterSeconds: 42 });
    expect(isReportableCommentFailure(error, failure)).toBe(false);
  });

  it.each([undefined, 0, -3, '42'])(
    'treats a cooldown without usable seconds (%j) as unexpected but silent',
    (retryAfterSeconds) => {
      const error = badRequest(['CooldownActive'], { retryAfterSeconds });
      const failure = commentFailure(error);

      expect(failure).toEqual({ kind: 'unexpected' });
      expect(isReportableCommentFailure(error, failure)).toBe(false);
    },
  );

  it('maps CommentTextRequired to the required state', () => {
    expect(commentFailure(badRequest(['CommentTextRequired']))).toEqual({ kind: 'textRequired' });
  });

  it('maps a 404 to not found without a report', () => {
    const error = new HttpErrorResponse({ status: 404, error: { traceId } });
    const failure = commentFailure(error);

    expect(failure).toEqual({ kind: 'notFound' });
    expect(isReportableCommentFailure(error, failure)).toBe(false);
  });

  it('maps a 403 to forbidden with its trace id and reports it', () => {
    const error = new HttpErrorResponse({ status: 403, error: { traceId } });
    const failure = commentFailure(error);

    expect(failure).toEqual({ kind: 'forbidden', traceId });
    expect(commentFailureTraceId(failure)).toBe(traceId);
    expect(isReportableCommentFailure(error, failure)).toBe(true);
  });

  it('maps a 500 to unexpected with its trace id and reports it', () => {
    const error = new HttpErrorResponse({ status: 500, error: { traceId } });
    const failure = commentFailure(error);

    expect(failure).toEqual({ kind: 'unexpected', traceId });
    expect(isReportableCommentFailure(error, failure)).toBe(true);
  });

  it.each([401, 429])('keeps a %i unexpected but silent and without a reference', (status) => {
    const error = new HttpErrorResponse({ status, error: { traceId } });
    const failure = commentFailure(error);

    expect(failure).toEqual({ kind: 'unexpected' });
    expect(commentFailureTraceId(failure)).toBeNull();
    expect(isReportableCommentFailure(error, failure)).toBe(false);
  });

  it('reports a non-HTTP error', () => {
    const error = new Error('boom');

    expect(isReportableCommentFailure(error, commentFailure(error))).toBe(true);
  });

  describe('commentErrorMessage', () => {
    let resources: ResourceService;

    beforeEach(() => {
      TestBed.configureTestingModule({
        providers: [provideHttpClient(), provideHttpClientTesting()],
      });
      resources = TestBed.inject(ResourceService);
    });

    it('shows the remaining seconds for the cooldown', () => {
      expect(commentErrorMessage(resources, { kind: 'cooldown', retryAfterSeconds: 42 })).toBe(
        'You can post or edit another comment in 42 seconds.',
      );
    });

    it('asks to refresh when the comment no longer exists', () => {
      expect(commentErrorMessage(resources, { kind: 'notFound' })).toBe(
        'This comment no longer exists. Refresh the comments.',
      );
    });

    it.each([
      [{ kind: 'forbidden' } as const, 'sighting.comments.errors.forbidden'],
      [{ kind: 'unexpected' } as const, 'sighting.comments.errors.generic'],
      [{ kind: 'textRequired' } as const, 'sighting.comments.validation.required'],
    ])('uses the label for %j', (failure, key) => {
      expect(commentErrorMessage(resources, failure)).toBe(resources.translate(key));
    });
  });
});
