import { HttpErrorResponse, provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { ResourceService } from '../../core/resources/resource.service';
import { isReportableSightingFailure, sightingErrorMessage, sightingFailure } from './sighting-errors';

describe('sighting errors', () => {
  function badRequest(
    codes: string[],
    extensions: Record<string, unknown> = {},
  ): HttpErrorResponse {
    const errors = Object.fromEntries(codes.map((code) => [code, ['x']]));

    return new HttpErrorResponse({ status: 400, error: { status: 400, errors, ...extensions } });
  }

  describe('sightingFailure', () => {
    it('reads the remaining cooldown seconds', () => {
      expect(sightingFailure(badRequest(['CooldownActive'], { retryAfterSeconds: 42 }))).toEqual({
        kind: 'cooldown',
        retryAfterSeconds: 42,
      });
    });

    it('rounds a fractional remainder up', () => {
      expect(sightingFailure(badRequest(['CooldownActive'], { retryAfterSeconds: 1.2 }))).toEqual({
        kind: 'cooldown',
        retryAfterSeconds: 2,
      });
    });

    it.each([undefined, 0, -3, '42'])(
      'falls back to generic when the cooldown seconds are %j',
      (retryAfterSeconds) => {
        expect(sightingFailure(badRequest(['CooldownActive'], { retryAfterSeconds }))).toEqual({
          kind: 'generic',
        });
      },
    );

    it.each(['InvalidInitialStatus', 'InvalidCriticality', 'InvalidLocation', 'Description'])(
      'maps %s to generic',
      (code) => {
        expect(sightingFailure(badRequest([code]))).toEqual({ kind: 'generic' });
      },
    );

    it.each([
      ['PhotoTooLarge', 'photoTooLarge'],
      ['PhotoUnsupportedType', 'photoUnsupportedType'],
      ['PhotoUnreadable', 'photoUnreadable'],
    ])('maps a 400 carrying %s to the %s photo problem', (code, kind) => {
      const error = badRequest([code], { traceId: '00-abc-def-01' });
      const failure = sightingFailure(error);

      expect(failure).toEqual({ kind });
      expect(isReportableSightingFailure(error, failure)).toBe(false);
    });

    it('maps a 503 carrying PhotoStorageUnavailable to the storage outage and reports it', () => {
      const error = new HttpErrorResponse({
        status: 503,
        error: { status: 503, traceId: '00-abc-def-01', errors: { PhotoStorageUnavailable: ['x'] } },
      });
      const failure = sightingFailure(error);

      expect(failure).toEqual({ kind: 'photoStorageUnavailable', traceId: '00-abc-def-01' });
      expect(isReportableSightingFailure(error, failure)).toBe(true);
    });

    it('keeps a 400 carrying PhotoStorageUnavailable generic', () => {
      expect(sightingFailure(badRequest(['PhotoStorageUnavailable']))).toEqual({ kind: 'generic' });
    });

    it('maps 404 to not found', () => {
      expect(sightingFailure(new HttpErrorResponse({ status: 404 }))).toEqual({
        kind: 'notFound',
      });
    });

    it.each([0, 429, 500, 503])('maps status %i to generic', (status) => {
      expect(sightingFailure(new HttpErrorResponse({ status }))).toEqual({ kind: 'generic' });
    });

    it('maps a non-HTTP error to generic', () => {
      expect(sightingFailure(new Error('boom'))).toEqual({ kind: 'generic' });
    });
  });

  describe('sightingErrorMessage', () => {
    function resources(): ResourceService {
      TestBed.configureTestingModule({
        providers: [provideHttpClient(), provideHttpClientTesting()],
      });

      return TestBed.inject(ResourceService);
    }

    it('puts the seconds into the cooldown message', () => {
      expect(sightingErrorMessage(resources(), { kind: 'cooldown', retryAfterSeconds: 17 })).toBe(
        'You can report another sighting in 17 seconds.',
      );
    });

    it('renders the not found and generic messages', () => {
      const labels = resources();

      expect(sightingErrorMessage(labels, { kind: 'notFound' })).toBe(
        "This sighting doesn't exist or is no longer available.",
      );
      expect(sightingErrorMessage(labels, { kind: 'generic' })).toBe(
        "We couldn't reach the sightings right now. Try again.",
      );
    });

    it('renders the photo problem and storage outage messages from their labels', () => {
      const labels = resources();

      expect(sightingErrorMessage(labels, { kind: 'photoTooLarge' })).toBe(
        labels.translate('sighting.form.photoTooLarge'),
      );
      expect(sightingErrorMessage(labels, { kind: 'photoStorageUnavailable' })).toBe(
        labels.translate('sighting.form.photoStorageUnavailable'),
      );
      expect(labels.translate('sighting.form.photoStorageUnavailable')).toContain('remove the photo');
    });
  });
});
