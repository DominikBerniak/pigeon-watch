import { HttpErrorResponse, provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { ResourceService } from '../../core/resources/resource.service';
import {
  isReportableMessageFailure,
  messageErrorMessage,
  messageFailure,
  messageFailureTraceId,
} from './message-errors';

const traceId = '00-abc123-def456-01';

describe('message errors', () => {
  function badRequest(codes: string[]): HttpErrorResponse {
    const errors = Object.fromEntries(codes.map((code) => [code, ['x']]));

    return new HttpErrorResponse({ status: 400, error: { status: 400, errors, traceId } });
  }

  it('maps OwnSightingContact to ownSighting with its trace id and reports it', () => {
    const error = badRequest(['OwnSightingContact']);
    const failure = messageFailure(error);

    expect(failure).toEqual({ kind: 'ownSighting', traceId });
    expect(messageFailureTraceId(failure)).toBe(traceId);
    expect(isReportableMessageFailure(error, failure)).toBe(true);
  });

  it('maps MessageTextRequired to the required state without a report', () => {
    const error = badRequest(['MessageTextRequired']);
    const failure = messageFailure(error);

    expect(failure).toEqual({ kind: 'textRequired' });
    expect(isReportableMessageFailure(error, failure)).toBe(false);
  });

  it('keeps MessageTextTooLong unexpected but silent and without a reference', () => {
    const error = badRequest(['MessageTextTooLong']);
    const failure = messageFailure(error);

    expect(failure).toEqual({ kind: 'unexpected' });
    expect(messageFailureTraceId(failure)).toBeNull();
    expect(isReportableMessageFailure(error, failure)).toBe(false);
  });

  it('maps a 404 to not found without a report', () => {
    const error = new HttpErrorResponse({ status: 404, error: { traceId } });
    const failure = messageFailure(error);

    expect(failure).toEqual({ kind: 'notFound' });
    expect(messageFailureTraceId(failure)).toBeNull();
    expect(isReportableMessageFailure(error, failure)).toBe(false);
  });

  it('maps a 500 to unexpected with its trace id and reports it', () => {
    const error = new HttpErrorResponse({ status: 500, error: { traceId } });
    const failure = messageFailure(error);

    expect(failure).toEqual({ kind: 'unexpected', traceId });
    expect(isReportableMessageFailure(error, failure)).toBe(true);
  });

  it.each([401, 429])('keeps a %i unexpected but silent and without a reference', (status) => {
    const error = new HttpErrorResponse({ status, error: { traceId } });
    const failure = messageFailure(error);

    expect(failure).toEqual({ kind: 'unexpected' });
    expect(messageFailureTraceId(failure)).toBeNull();
    expect(isReportableMessageFailure(error, failure)).toBe(false);
  });

  it('reports a non-HTTP error', () => {
    const error = new Error('boom');

    expect(isReportableMessageFailure(error, messageFailure(error))).toBe(true);
  });

  describe('messageErrorMessage', () => {
    let resources: ResourceService;

    beforeEach(() => {
      TestBed.configureTestingModule({
        providers: [provideHttpClient(), provideHttpClientTesting()],
      });
      resources = TestBed.inject(ResourceService);
    });

    it('says the conversation is gone for a 404', () => {
      expect(messageErrorMessage(resources, { kind: 'notFound' })).toBe(
        'This conversation is no longer available. Reload the page.',
      );
    });

    it.each([
      [{ kind: 'ownSighting' } as const, 'sighting.messages.errors.ownSighting'],
      [{ kind: 'unexpected' } as const, 'sighting.messages.errors.send'],
      [{ kind: 'textRequired' } as const, 'sighting.messages.validation.required'],
    ])('uses the label for %j', (failure, key) => {
      expect(messageErrorMessage(resources, failure)).toBe(resources.translate(key));
    });
  });
});
